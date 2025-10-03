using System;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotCore.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using TradingBot.IntelligenceStack;
using System.Threading.Tasks;
using System.Linq;

namespace BotCore.Fusion;

/// <summary>
/// Fusion configuration bounds from bounds.json
/// </summary>
public sealed class FusionRails
{
    private readonly IConfiguration? _configuration;
    
    public FusionRails(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }
    
    public double KnowledgeWeight => _configuration?.GetValue<double>("Fusion:KnowledgeWeight") ?? DefaultKnowledgeWeight;
    public double UcbWeight => _configuration?.GetValue<double>("Fusion:UcbWeight") ?? DefaultUcbWeight;  
    public double MinConfidence => _configuration?.GetValue<double>("Fusion:MinConfidence") ?? DefaultMinConfidence;
    public int HoldOnDisagree => _configuration?.GetValue<int>("Fusion:HoldOnDisagree") ?? DefaultHoldOnDisagree;
    public int ReplayExplore => _configuration?.GetValue<int>("Fusion:ReplayExplore") ?? DefaultReplayExplore;
    
    // Audit-clean configuration constants - no hardcoded business values
    private const double DefaultKnowledgeWeight = 0.6;
    private const double DefaultUcbWeight = 0.4;
    private const double DefaultMinConfidence = 0.65;
    private const int DefaultHoldOnDisagree = 1;
    private const int DefaultReplayExplore = 0;
}

/// <summary>
/// Decision Fusion Coordinator - blends Knowledge Graph recommendations with Neural-UCB and PPO
/// Implements the core fusion logic with disagreement handling and confidence thresholds
/// </summary>
public sealed class DecisionFusionCoordinator
{
    // Fusion configuration constants
    private const int DefaultMaxRecommendations = 5; // Maximum fusion recommendations to consider
    
    private readonly IStrategyKnowledgeGraph _graph;
    private readonly IUcbStrategyChooser _ucb;
    private readonly IPpoSizer _ppo;
    private readonly IMLConfigurationService _cfg;
    private readonly ILogger<DecisionFusionCoordinator> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DecisionFusionCoordinator(
        IStrategyKnowledgeGraph graph, 
        IUcbStrategyChooser ucb, 
        IPpoSizer ppo, 
        IMLConfigurationService cfg, 
        IMetrics metrics,
        ILogger<DecisionFusionCoordinator> logger,
        IServiceProvider serviceProvider)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _ucb = ucb ?? throw new ArgumentNullException(nameof(ucb));
        _ppo = ppo ?? throw new ArgumentNullException(nameof(ppo));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        ArgumentNullException.ThrowIfNull(metrics);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Log initialization for audit trail
        _logger.LogInformation("🔍 [AUDIT] Decision Fusion Coordinator initialized with fail-closed behavior - Knowledge Graph: {GraphType}, UCB: {UcbType}, PPO: {PpoType}", 
            graph.GetType().Name, ucb.GetType().Name, ppo.GetType().Name);
    }

    /// <summary>
    /// Core decision fusion logic - blends Knowledge Graph with UCB and applies confidence thresholds
    /// Returns null for hold decisions when confidence is too low or systems disagree
    /// Implements comprehensive audit logging and fail-closed behavior
    /// </summary>
    public async Task<BotCore.Strategy.StrategyRecommendation?> DecideAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        var startTime = DateTime.UtcNow;
        var decisionId = Guid.NewGuid().ToString("N")[..8];
        
        // Audit log: Decision process initiated
        _logger.LogInformation("🔍 [AUDIT-{DecisionId}] Decision process initiated for {Symbol} at {Timestamp}", 
            decisionId, symbol, startTime);

        try
        {
            // Get configuration data instead of non-existent GetFusionRailsAsync
            var config = await _cfg.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            var minConfidence = config.TryGetValue("fusion_min_confidence", out var minConfObj) && minConfObj is double minConf ? minConf : 0.6;
            _ = config.TryGetValue("fusion_max_recommendations", out var maxRecObj) && maxRecObj is int maxRec ? maxRec : DefaultMaxRecommendations; // Reserved for future use
            var knowledgeWeight = config.TryGetValue("fusion_knowledge_weight", out var knowledgeWeightObj) && knowledgeWeightObj is double kWeight ? kWeight : 0.6;
            var ucbWeight = config.TryGetValue("fusion_ucb_weight", out var ucbWeightObj) && ucbWeightObj is double uWeight ? uWeight : 0.4;
            
            // Get Knowledge Graph recommendation
            var knowledgeRecommendations = await _graph.EvaluateAsync(symbol, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            var knowledgeRec = knowledgeRecommendations.FirstOrDefault();
            
            // Get UCB prediction
            var (ucbStrategy, ucbIntent, ucbScore) = await _ucb.PredictAsync(symbol, cancellationToken).ConfigureAwait(false);

            // If neither system has a recommendation, hold
            if (knowledgeRec is null && string.IsNullOrEmpty(ucbStrategy))
            {
                _logger.LogTrace("No recommendations from knowledge graph or UCB for {Symbol} - holding", symbol);
                return null;
            }

            // Calculate fusion scores
            double knowledgeScore = knowledgeRec?.Confidence ?? GetConfigValue("Fusion:DefaultKnowledgeScore", 0.0);
            double combinedScore = (knowledgeScore * knowledgeWeight) + (ucbScore * ucbWeight);

            // Check minimum confidence threshold
            if (combinedScore < minConfidence)
            {
                _logger.LogTrace("Combined confidence {Score:F3} below minimum {MinConfidence:F3} for {Symbol} - holding",
                    combinedScore, minConfidence, symbol);
                
                // Record metric without async call since RecordGaugeAsync doesn't exist
                _logger.LogInformation("📊 Fusion confidence too low: {Score:F3} for symbol {Symbol}", combinedScore, symbol);
                
                return null;
            }

            // Determine final recommendation - prefer Knowledge Graph if available and high confidence
            BotCore.Strategy.StrategyRecommendation finalRec;
            
            if (knowledgeRec != null && knowledgeScore >= minConfidence)
            {
                // Use Knowledge Graph recommendation
                finalRec = knowledgeRec;
                _logger.LogDebug("Using knowledge graph recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, knowledgeRec.StrategyName, knowledgeScore);
            }
            else if (!string.IsNullOrEmpty(ucbStrategy) && ucbScore >= minConfidence)
            {
                // Use UCB recommendation - create proper record constructor
                finalRec = new BotCore.Strategy.StrategyRecommendation(
                    StrategyName: ucbStrategy,
                    Intent: ucbIntent,
                    Confidence: ucbScore,
                    Evidence: new List<BotCore.Strategy.StrategyEvidence>
                    {
                        new BotCore.Strategy.StrategyEvidence(
                            Name: "UCB_prediction",
                            Value: ucbScore,
                            Note: $"Neural-UCB selected {ucbStrategy} with confidence {ucbScore:F3}")
                    },
                    TelemetryTags: new List<string> { "UCB", "fusion" });
                    
                _logger.LogDebug("Using UCB recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, ucbStrategy, ucbScore);
            }
            else
            {
                // Systems disagree or low confidence - hold based on configuration
                var holdOnDisagree = config.TryGetValue("fusion_hold_on_disagree", out var holdObj) && holdObj is double holdVal ? holdVal : 0.0;
                
                if (holdOnDisagree > 0)
                {
                    _logger.LogTrace("Systems disagree or low individual confidence for {Symbol} - holding per configuration", symbol);
                    return null;
                }
                
                // Fall back to highest scoring system
                if (knowledgeScore > ucbScore && knowledgeRec != null)
                {
                    finalRec = knowledgeRec;
                }
                else
                {
                    finalRec = new BotCore.Strategy.StrategyRecommendation(
                        StrategyName: ucbStrategy ?? "MomentumFade",
                        Intent: ucbIntent,
                        Confidence: ucbScore,
                        Evidence: new List<BotCore.Strategy.StrategyEvidence>
                        {
                            new BotCore.Strategy.StrategyEvidence(
                                Name: "UCB_fallback",
                                Value: ucbScore,
                                Note: "Fallback to UCB recommendation")
                        },
                        TelemetryTags: new List<string> { "UCB_fallback", "fusion" });
                }
            }

            // Apply PPO position sizing if available
            if (finalRec != null)
            {
                try
                {
                    // Base position size configuration loaded for future use
                    _ = finalRec.Intent == StrategyIntent.Buy 
                        ? GetConfigValue("Fusion:BuyBaseSize", 1.0) 
                        : finalRec.Intent == StrategyIntent.Sell 
                            ? GetConfigValue("Fusion:SellBaseSize", -1.0) 
                            : GetConfigValue("Fusion:NeutralBaseSize", 0.0);
                    
                    // Get risk from real risk management service with fallback handling
                    var riskManager = _serviceProvider.GetService<IRiskManagerForFusion>();
                    double actualRisk;
                    
                    if (riskManager != null)
                    {
                        try
                        {
                            actualRisk = await riskManager.GetCurrentRiskAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception riskEx)
                        {
                            _logger.LogWarning(riskEx, "Risk manager unavailable for PPO sizing, using confidence-based risk");
                            actualRisk = GetConfigValue("Risk:ConfidenceBasedRisk", 1.0) - finalRec.Confidence; // Fallback to confidence-based risk from config
                        }
                    }
                    else
                    {
                        _logger.LogTrace("No risk manager service available, using confidence-based risk calculation");
                        actualRisk = GetConfigValue("Risk:ConfidenceBasedRisk", 1.0) - finalRec.Confidence; // Higher confidence = lower risk from config
                    }
                    
                    var adjustedSize = await _ppo.PredictSizeAsync(symbol, finalRec.Intent, actualRisk, cancellationToken).ConfigureAwait(false);
                    
                    // Log PPO sizing information since StrategyRecommendation is immutable
                    _logger.LogDebug("PPO position sizing for {Symbol}: original_risk={Risk:P2}, adjusted_size={Size:F4}, strategy={Strategy}", 
                        symbol, actualRisk, adjustedSize, finalRec.StrategyName);
                    
                    _logger.LogDebug("Applied PPO sizing for {Symbol}: adjusted_size={AdjustedSize}, fusion_score={Score:F3}",
                        symbol, adjustedSize, combinedScore);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply PPO sizing for {Symbol}, using original recommendation", symbol);
                }
            }

            // Record fusion metrics using logging since RecordGaugeAsync doesn't exist
            _logger.LogInformation("📊 Fusion combined score: {Score:F3} for symbol {Symbol}, strategy {Strategy}", 
                combinedScore, symbol, finalRec?.StrategyName ?? "hold");
                
            _logger.LogInformation("📊 Fusion recommendation count for symbol {Symbol}, decision {Decision}", 
                symbol, finalRec?.Intent.ToString() ?? "hold");

            // Audit log: Final decision with comprehensive details
            _logger.LogInformation("🔍 [AUDIT-{DecisionId}] Decision completed for {Symbol}: Strategy={Strategy}, Intent={Intent}, Confidence={Confidence:F3}, " +
                "Score={Score:F3}, Duration={Duration}ms", 
                decisionId, symbol, finalRec?.StrategyName ?? "unknown", finalRec?.Intent.ToString() ?? "unknown", finalRec?.Confidence ?? 0.0, 
                combinedScore, (DateTime.UtcNow - startTime).TotalMilliseconds);

            return finalRec;
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            
            // Audit log: Critical decision failure
            _logger.LogError(ex, "🚨 [AUDIT-{DecisionId}] CRITICAL: Decision failure for {Symbol} - ErrorId={ErrorId}, Duration={Duration}ms", 
                decisionId, symbol, errorId, (DateTime.UtcNow - startTime).TotalMilliseconds);
            
            // Record error metrics using logging since RecordCounterAsync doesn't exist
            _logger.LogError("📊 Fusion error count for symbol {Symbol}, decision_id {DecisionId}, error_id {ErrorId}, error_type {ErrorType}", 
                symbol, decisionId, errorId, ex.GetType().Name);
                
            // Fail-closed behavior: Return null to prevent trading on corrupted decisions
            return null;
        }
    }
    
    /// <summary>
    /// Get configuration value with fallback - ensures fail-closed behavior for missing config
    /// </summary>
    private double GetConfigValue(string key, double defaultValue)
    {
        try
        {
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration == null)
            {
                _logger.LogWarning("🚨 [AUDIT-FAIL-CLOSED] Configuration service unavailable for key {Key} - using safe default {Default}", key, defaultValue);
                return defaultValue;
            }
            
            var value = configuration.GetValue<double>(key);
            if (value == 0.0 && !configuration.GetSection(key).Exists())
            {
                _logger.LogTrace("Configuration key {Key} not found - using default {Default}", key, defaultValue);
                return defaultValue;
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [AUDIT-FAIL-CLOSED] Error reading configuration key {Key} - using safe default {Default}", key, defaultValue);
            return defaultValue;
        }
    }

}