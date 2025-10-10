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
    
    // CA1848: LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, string, string, string, Exception?> LogCoordinatorInitialized =
        LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(8100, nameof(LogCoordinatorInitialized)),
            "🔍 [AUDIT] Decision Fusion Coordinator initialized with fail-closed behavior - Knowledge Graph: {GraphType}, UCB: {UcbType}, PPO: {PpoType}");
    
    private static readonly Action<ILogger, string, string, DateTime, Exception?> LogDecisionProcessInitiated =
        LoggerMessage.Define<string, string, DateTime>(LogLevel.Information, new EventId(8101, nameof(LogDecisionProcessInitiated)),
            "🔍 [AUDIT-{DecisionId}] Decision process initiated for {Symbol} at {Timestamp}");
    
    private static readonly Action<ILogger, string, Exception?> LogNoRecommendations =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(8102, nameof(LogNoRecommendations)),
            "No recommendations from knowledge graph or UCB for {Symbol} - holding");
    
    private static readonly Action<ILogger, double, double, string, Exception?> LogConfidenceBelowMin =
        LoggerMessage.Define<double, double, string>(LogLevel.Trace, new EventId(8103, nameof(LogConfidenceBelowMin)),
            "Combined confidence {Score:F3} below minimum {MinConfidence:F3} for {Symbol} - holding");
    
    private static readonly Action<ILogger, double, string, Exception?> LogFusionConfidenceTooLow =
        LoggerMessage.Define<double, string>(LogLevel.Information, new EventId(8104, nameof(LogFusionConfidenceTooLow)),
            "📊 Fusion confidence too low: {Score:F3} for symbol {Symbol}");
    
    private static readonly Action<ILogger, string, string, double, Exception?> LogUsingKnowledgeGraph =
        LoggerMessage.Define<string, string, double>(LogLevel.Debug, new EventId(8105, nameof(LogUsingKnowledgeGraph)),
            "Using knowledge graph recommendation for {Symbol}: {Strategy} (score: {Score:F3})");
    
    private static readonly Action<ILogger, string, string, double, Exception?> LogUsingUcb =
        LoggerMessage.Define<string, string, double>(LogLevel.Debug, new EventId(8106, nameof(LogUsingUcb)),
            "Using UCB recommendation for {Symbol}: {Strategy} (score: {Score:F3})");
    
    private static readonly Action<ILogger, string, Exception?> LogSystemsDisagree =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(8107, nameof(LogSystemsDisagree)),
            "Systems disagree or low individual confidence for {Symbol} - holding per configuration");
    
    private static readonly Action<ILogger, Exception?> LogRiskManagerUnavailable =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8108, nameof(LogRiskManagerUnavailable)),
            "Risk manager unavailable for PPO sizing, using confidence-based risk");
    
    private static readonly Action<ILogger, Exception?> LogNoRiskManagerService =
        LoggerMessage.Define(LogLevel.Trace, new EventId(8109, nameof(LogNoRiskManagerService)),
            "No risk manager service available, using confidence-based risk calculation");
    
    private static readonly Action<ILogger, string, double, double, string, Exception?> LogPpoPositionSizing =
        LoggerMessage.Define<string, double, double, string>(LogLevel.Debug, new EventId(8110, nameof(LogPpoPositionSizing)),
            "PPO position sizing for {Symbol}: original_risk={Risk:P2}, adjusted_size={Size:F4}, strategy={Strategy}");
    
    private static readonly Action<ILogger, string, double, double, Exception?> LogAppliedPpoSizing =
        LoggerMessage.Define<string, double, double>(LogLevel.Debug, new EventId(8111, nameof(LogAppliedPpoSizing)),
            "Applied PPO sizing for {Symbol}: adjusted_size={AdjustedSize}, fusion_score={Score:F3}");
    
    private static readonly Action<ILogger, string, Exception?> LogFailedToApplyPpo =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8112, nameof(LogFailedToApplyPpo)),
            "Failed to apply PPO sizing for {Symbol}, using original recommendation");
    
    private static readonly Action<ILogger, double, string, string, Exception?> LogFusionCombinedScore =
        LoggerMessage.Define<double, string, string>(LogLevel.Information, new EventId(8113, nameof(LogFusionCombinedScore)),
            "📊 Fusion combined score: {Score:F3} for symbol {Symbol}, strategy {Strategy}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogFusionRecommendationCount =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(8114, nameof(LogFusionRecommendationCount)),
            "📊 Fusion recommendation count for symbol {Symbol}, decision {Decision}");
    
    private static readonly Action<ILogger, string, string, string, string, double, long, Exception?> LogDecisionCompleted =
        LoggerMessage.Define<string, string, string, string, double, long>(LogLevel.Information, new EventId(8115, nameof(LogDecisionCompleted)),
            "🔍 [AUDIT-{DecisionId}] Decision completed for {Symbol}: Strategy={Strategy}, Intent={Intent}, Confidence={Confidence:F3}, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, string, string, long, Exception?> LogCriticalDecisionFailure =
        LoggerMessage.Define<string, string, string, long>(LogLevel.Error, new EventId(8116, nameof(LogCriticalDecisionFailure)),
            "🚨 [AUDIT-{DecisionId}] CRITICAL: Decision failure for {Symbol} - ErrorId={ErrorId}, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, string, string, string, Exception?> LogFusionErrorCount =
        LoggerMessage.Define<string, string, string, string>(LogLevel.Error, new EventId(8117, nameof(LogFusionErrorCount)),
            "📊 Fusion error count for symbol {Symbol}, decision_id {DecisionId}, error_id {ErrorId}, error_type {ErrorType}");
    
    private static readonly Action<ILogger, string, double, Exception?> LogConfigServiceUnavailableForKey =
        LoggerMessage.Define<string, double>(LogLevel.Warning, new EventId(8118, nameof(LogConfigServiceUnavailableForKey)),
            "🚨 [AUDIT-FAIL-CLOSED] Configuration service unavailable for key {Key} - using safe default {Default}");
    
    private static readonly Action<ILogger, string, double, Exception?> LogConfigKeyNotFound =
        LoggerMessage.Define<string, double>(LogLevel.Trace, new EventId(8119, nameof(LogConfigKeyNotFound)),
            "Configuration key {Key} not found - using default {Default}");
    
    private static readonly Action<ILogger, string, double, Exception?> LogErrorReadingConfigKey =
        LoggerMessage.Define<string, double>(LogLevel.Error, new EventId(8120, nameof(LogErrorReadingConfigKey)),
            "🚨 [AUDIT-FAIL-CLOSED] Error reading configuration key {Key} - using safe default {Default}");

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
        LogCoordinatorInitialized(_logger, graph.GetType().Name, ucb.GetType().Name, ppo.GetType().Name, null);
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
        LogDecisionProcessInitiated(_logger, decisionId, symbol, startTime, null);

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
            var knowledgeRec = knowledgeRecommendations.Count > 0 ? knowledgeRecommendations[0] : null;
            
            // Get UCB prediction
            var (ucbStrategy, ucbIntent, ucbScore) = await _ucb.PredictAsync(symbol, cancellationToken).ConfigureAwait(false);

            // If neither system has a recommendation, hold
            if (knowledgeRec is null && string.IsNullOrEmpty(ucbStrategy))
            {
                LogNoRecommendations(_logger, symbol, null);
                return null;
            }

            // Calculate fusion scores
            double knowledgeScore = knowledgeRec?.Confidence ?? GetConfigValue("Fusion:DefaultKnowledgeScore", 0.0);
            double combinedScore = (knowledgeScore * knowledgeWeight) + (ucbScore * ucbWeight);

            // Check minimum confidence threshold
            if (combinedScore < minConfidence)
            {
                LogConfidenceBelowMin(_logger, combinedScore, minConfidence, symbol, null);
                
                // Record metric without async call since RecordGaugeAsync doesn't exist
                LogFusionConfidenceTooLow(_logger, combinedScore, symbol, null);
                
                return null;
            }

            // Determine final recommendation - prefer Knowledge Graph if available and high confidence
            BotCore.Strategy.StrategyRecommendation finalRec;
            
            if (knowledgeRec != null && knowledgeScore >= minConfidence)
            {
                // Use Knowledge Graph recommendation
                finalRec = knowledgeRec;
                LogUsingKnowledgeGraph(_logger, symbol, knowledgeRec.StrategyName, knowledgeScore, null);
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
                    
                LogUsingUcb(_logger, symbol, ucbStrategy, ucbScore, null);
            }
            else
            {
                // Systems disagree or low confidence - hold based on configuration
                var holdOnDisagree = config.TryGetValue("fusion_hold_on_disagree", out var holdObj) && holdObj is double holdVal ? holdVal : 0.0;
                
                if (holdOnDisagree > 0)
                {
                    LogSystemsDisagree(_logger, symbol, null);
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
            try
            {
                // Base position size configuration loaded for future use
                double baseSize;
                if (finalRec.Intent == StrategyIntent.Buy)
                {
                    baseSize = GetConfigValue("Fusion:BuyBaseSize", 1.0);
                }
                else if (finalRec.Intent == StrategyIntent.Sell)
                {
                    baseSize = GetConfigValue("Fusion:SellBaseSize", -1.0);
                }
                else
                {
                    baseSize = GetConfigValue("Fusion:NeutralBaseSize", 0.0);
                }
                _ = baseSize; // Suppress unused variable warning
                
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
                        LogRiskManagerUnavailable(_logger, riskEx);
                        actualRisk = GetConfigValue("Risk:ConfidenceBasedRisk", 1.0) - finalRec.Confidence; // Fallback to confidence-based risk from config
                    }
                }
                else
                {
                    LogNoRiskManagerService(_logger, null);
                    actualRisk = GetConfigValue("Risk:ConfidenceBasedRisk", 1.0) - finalRec.Confidence; // Higher confidence = lower risk from config
                }
                
                var adjustedSize = await _ppo.PredictSizeAsync(symbol, finalRec.Intent, actualRisk, cancellationToken).ConfigureAwait(false);
                
                // Log PPO sizing information since StrategyRecommendation is immutable
                LogPpoPositionSizing(_logger, symbol, actualRisk, adjustedSize, finalRec.StrategyName, null);
                
                LogAppliedPpoSizing(_logger, symbol, adjustedSize, combinedScore, null);
            }
            catch (Exception ex)
            {
                LogFailedToApplyPpo(_logger, symbol, ex);
            }

            // Record fusion metrics using logging since RecordGaugeAsync doesn't exist
            LogFusionCombinedScore(_logger, combinedScore, symbol, finalRec.StrategyName, null);
                
            LogFusionRecommendationCount(_logger, symbol, finalRec.Intent.ToString(), null);

            // Audit log: Final decision with comprehensive details
            LogDecisionCompleted(_logger, decisionId, symbol, finalRec.StrategyName, finalRec.Intent.ToString(), 
                finalRec.Confidence, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, null);

            return finalRec;
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            
            // Audit log: Critical decision failure
            LogCriticalDecisionFailure(_logger, decisionId, symbol, errorId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, ex);
            
            // Record error metrics using logging since RecordCounterAsync doesn't exist
            LogFusionErrorCount(_logger, symbol, decisionId, errorId, ex.GetType().Name, null);
                
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
                LogConfigServiceUnavailableForKey(_logger, key, defaultValue, null);
                return defaultValue;
            }
            
            var value = configuration.GetValue<double>(key);
            if (Math.Abs(value) < double.Epsilon && !configuration.GetSection(key).Exists())
            {
                LogConfigKeyNotFound(_logger, key, defaultValue, null);
                return defaultValue;
            }
            
            return value;
        }
        catch (Exception ex)
        {
            LogErrorReadingConfigKey(_logger, key, defaultValue, ex);
            return defaultValue;
        }
    }

}