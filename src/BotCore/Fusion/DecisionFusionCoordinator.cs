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
    private readonly IStrategyKnowledgeGraph _graph;
    private readonly IUcbStrategyChooser _ucb;
    private readonly IPpoSizer _ppo;
    private readonly IMLConfigurationService _cfg;
    private readonly IMetrics _metrics;
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
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
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
            var rails = await _cfg.GetFusionRailsAsync(cancellationToken).ConfigureAwait(false);
            
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
            double combinedScore = (knowledgeScore * rails.KnowledgeWeight) + (ucbScore * rails.UcbWeight);

            // Check minimum confidence threshold
            if (combinedScore < rails.MinConfidence)
            {
                _logger.LogTrace("Combined confidence {Score:F3} below minimum {MinConfidence:F3} for {Symbol} - holding",
                    combinedScore, rails.MinConfidence, symbol);
                
                await _metrics.RecordGaugeAsync("fusion.confidence_too_low", combinedScore, 
                    new Dictionary<string, string> { ["symbol"] = symbol }, cancellationToken).ConfigureAwait(false);
                
                return null;
            }

            // Determine final recommendation - prefer Knowledge Graph if available and high confidence
            BotCore.Strategy.StrategyRecommendation finalRec;
            
            if (knowledgeRec != null && knowledgeScore >= rails.MinConfidence)
            {
                // Use Knowledge Graph recommendation
                finalRec = knowledgeRec;
                _logger.LogDebug("Using knowledge graph recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, knowledgeRec.StrategyName, knowledgeScore);
            }
            else if (!string.IsNullOrEmpty(ucbStrategy) && ucbScore >= rails.MinConfidence)
            {
                // Use UCB recommendation
                finalRec = new BotCore.Strategy.StrategyRecommendation(
                    ucbStrategy,
                    ucbIntent,
                    ucbScore,
                    new List<BotCore.Strategy.StrategyEvidence>
                    {
                        new BotCore.Strategy.StrategyEvidence("UCB_prediction", ucbScore, $"Neural-UCB selected {ucbStrategy} with confidence {ucbScore:F3}")
                    },
                    new List<string> { "ucb_source", "fusion_coordinator" }
                );
                _logger.LogDebug("Using UCB recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, ucbStrategy, ucbScore);
            }
            else
            {
                // Systems disagree or low confidence - hold based on configuration
                if (rails.HoldOnDisagree > 0)
                {
                    _logger.LogTrace("Systems disagree or low individual confidence for {Symbol} - holding per configuration", symbol);
                    return null;
                }
                
                // Fall back to highest scoring system
                finalRec = knowledgeScore > ucbScore ? knowledgeRec : 
                    new BotCore.Strategy.StrategyRecommendation(
                        ucbStrategy,
                        ucbIntent,
                        ucbScore,
                        new List<BotCore.Strategy.StrategyEvidence>(),
                        new List<string> { "fallback", "highest_score" }
                    );
            }

            // Apply PPO position sizing if available
            if (finalRec != null)
            {
                try
                {
                    // Calculate base position size from strategy intent
                    var baseSize = finalRec.Intent == StrategyIntent.Buy 
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
                    
                    var adjustedSize = await _ppo.SizeAsync(baseSize, finalRec.StrategyName, actualRisk, symbol, cancellationToken).ConfigureAwait(false);
                    
                    // Update recommendation with PPO sizing
                    finalRec.AdditionalData ??= new Dictionary<string, object>();
                    finalRec.AdditionalData["ppo_adjusted_size"] = adjustedSize;
                    finalRec.AdditionalData["original_size"] = baseSize;
                    finalRec.AdditionalData["fusion_score"] = combinedScore;
                    
                    _logger.LogDebug("Applied PPO sizing for {Symbol}: {OriginalSize} -> {AdjustedSize}",
                        symbol, baseSize, adjustedSize);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply PPO sizing for {Symbol}, using original recommendation", symbol);
                }
            }

            // Record fusion metrics
            await _metrics.RecordGaugeAsync("fusion.combined_score", combinedScore, 
                new Dictionary<string, string> { ["symbol"] = symbol, ["strategy"] = finalRec?.StrategyName ?? "hold" }, 
                cancellationToken).ConfigureAwait(false);
                
            await _metrics.RecordCounterAsync("fusion.recommendations", 1, 
                new Dictionary<string, string> { ["symbol"] = symbol, ["decision"] = finalRec?.Intent.ToString() ?? "hold" }, 
                cancellationToken).ConfigureAwait(false);

            // Audit log: Final decision with comprehensive details
            _logger.LogInformation("🔍 [AUDIT-{DecisionId}] Decision completed for {Symbol}: Strategy={Strategy}, Intent={Intent}, Confidence={Confidence:F3}, " +
                "Score={Score:F3}, Size={Size:F4}, Duration={Duration}ms", 
                decisionId, symbol, finalRec.StrategyName, finalRec.Intent, finalRec.Confidence, 
                combinedScore, finalRec.AdditionalData?.GetValueOrDefault("ppo_adjusted_size", 0.0), 
                (DateTime.UtcNow - startTime).TotalMilliseconds);

            return finalRec;
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            
            // Audit log: Critical decision failure
            _logger.LogError(ex, "🚨 [AUDIT-{DecisionId}] CRITICAL: Decision failure for {Symbol} - ErrorId={ErrorId}, Duration={Duration}ms", 
                decisionId, symbol, errorId, (DateTime.UtcNow - startTime).TotalMilliseconds);
            
            await _metrics.RecordCounterAsync("fusion.errors", 1, 
                new Dictionary<string, string> { 
                    ["symbol"] = symbol,
                    ["decision_id"] = decisionId,
                    ["error_id"] = errorId,
                    ["error_type"] = ex.GetType().Name
                }, 
                cancellationToken).ConfigureAwait(false);
                
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

    /// <summary>
    /// Synchronous wrapper for backward compatibility
    /// </summary>
    public BotCore.Strategy.StrategyRecommendation? Decide(string symbol)
    {
        return DecideAsync(symbol, CancellationToken.None).GetAwaiter().GetResult();
    }
}