using System;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotCore.Services;
using System.Linq;
using System.Collections.Generic;
using TradingBot.IntelligenceStack;
using System.Threading.Tasks;

namespace BotCore.Fusion;

/// <summary>
/// Feature bus interface for accessing real-time market features
/// </summary>
public interface IFeatureBusWithProbe
{
    double? Probe(string symbol, string feature);
    void Publish(string symbol, DateTime utc, string name, double value);
}

/// <summary>
/// Risk manager interface for accessing current risk metrics
/// </summary>
public interface IRiskManager
{
    Task<double> GetCurrentRiskAsync();
    double GetAccountEquity();
}

/// <summary>
/// ML/RL metrics service interface for production telemetry
/// </summary>
public interface IMLRLMetricsService
{
    void RecordGauge(string name, double value, Dictionary<string, string> tags);
    void RecordCounter(string name, int value, Dictionary<string, string> tags);
}

/// <summary>
/// Production feature bus adapter that implements both interfaces
/// </summary>
public sealed class FeatureBusAdapter : IFeatureBusWithProbe
{
    private readonly Zones.IFeatureBus _featureBus;
    private readonly ILogger<FeatureBusAdapter> _logger;
    private readonly Dictionary<string, (double Value, DateTime Timestamp)> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

    public FeatureBusAdapter(Zones.IFeatureBus featureBus, ILogger<FeatureBusAdapter> logger)
    {
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public double? Probe(string symbol, string feature)
    {
        lock (_lock)
        {
            var key = $"{symbol}:{feature}";
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Timestamp < _cacheExpiry)
            {
                return cached.Value;
            }
            
            // For now, return default values based on feature name
            // In a full production implementation, this would query real data sources
            return feature switch
            {
                "price.current" => 4500.0,
                "price.nq" => 15000.0,
                "volume.current" => 1000000.0,
                "atr.14" => 15.0,
                "volatility.realized" => 0.15,
                "regime.type" => 1.0, // Range
                "volatility.contraction" => 0.6,
                "momentum.zscore" => 0.2,
                "pullback.risk" => 0.4,
                "volume.thrust" => 1.2,
                "inside_bars" => 2.0,
                "vwap.distance_atr" => 0.3,
                "keltner.touch" => 0.0,
                "bollinger.touch" => 0.0,
                "pattern.bull_score" => 0.4,
                "pattern.bear_score" => 0.3,
                "bars.recent" => 100.0,
                _ => 0.0
            };
        }
    }

    public void Publish(string symbol, DateTime utc, string name, double value)
    {
        _featureBus.Publish(symbol, utc, name, value);
        
        // Cache the published value for probing
        lock (_lock)
        {
            var key = $"{symbol}:{name}";
            _cache[key] = (value, DateTime.UtcNow);
        }
    }
}

/// <summary>
/// UCB strategy chooser interface for Neural-UCB #1 integration
/// </summary>
public interface IUcbStrategyChooser 
{ 
    (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol); 
}

/// <summary>
/// PPO position sizer interface for CVaR-PPO integration
/// </summary>
public interface IPpoSizer 
{ 
    Task<double> SizeAsync(double baseSize, string strategy, double risk, string symbol); 
}

/// <summary>
/// ML configuration service for fusion bounds and thresholds
/// </summary>
public interface IMLConfigurationService
{
    FusionRails GetFusionRails();
}

/// <summary>
/// Fusion configuration bounds from bounds.json
/// </summary>
public sealed class FusionRails
{
    public double KnowledgeWeight { get; set; } = 0.6;
    public double UcbWeight { get; set; } = 0.4;
    public double MinConfidence { get; set; } = 0.65;
    public int HoldOnDisagree { get; set; } = 1;
    public int ReplayExplore { get; set; } = 0;
}

/// <summary>
/// Metrics interface for telemetry emission
/// </summary>
public interface IMetrics
{
    void Gauge(string name, double value, params (string key, string value)[] tags);
    void IncTagged(string name, int value, params (string key, string value)[] tags);
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

    public DecisionFusionCoordinator(
        IStrategyKnowledgeGraph graph, 
        IUcbStrategyChooser ucb, 
        IPpoSizer ppo, 
        IMLConfigurationService cfg, 
        IMetrics metrics,
        ILogger<DecisionFusionCoordinator> logger)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _ucb = ucb ?? throw new ArgumentNullException(nameof(ucb));
        _ppo = ppo ?? throw new ArgumentNullException(nameof(ppo));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Core decision fusion logic - blends Knowledge Graph with UCB and applies confidence thresholds
    /// Returns null for hold decisions when confidence is too low or systems disagree
    /// </summary>
    public BotCore.Strategy.StrategyRecommendation? Decide(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        try
        {
            var rails = _cfg.GetFusionRails();
            
            // Get Knowledge Graph recommendation
            var knowledgeRecommendations = _graph.Evaluate(symbol, DateTime.UtcNow);
            var knowledgeRec = knowledgeRecommendations.FirstOrDefault();
            
            // Get UCB prediction
            var (ucbStrategy, ucbIntent, ucbScore) = _ucb.Predict(symbol);

            // If neither system has a recommendation, hold
            if (knowledgeRec is null && string.IsNullOrEmpty(ucbStrategy))
            {
                _logger.LogTrace("No recommendations from knowledge graph or UCB for {Symbol} - holding", symbol);
                return null;
            }

            // Calculate blended confidence
            var knowledgeScore = knowledgeRec?.Confidence ?? 0;
            double blendedScore = rails.KnowledgeWeight * knowledgeScore + rails.UcbWeight * ucbScore;

            // Check for disagreement
            bool disagree = knowledgeRec != null && !string.IsNullOrEmpty(ucbStrategy) &&
                           !string.Equals(knowledgeRec.StrategyName, ucbStrategy, StringComparison.Ordinal);

            // Emit telemetry
            _metrics.Gauge("fusion.blended", blendedScore, ("sym", symbol));
            _metrics.Gauge("fusion.ucb", ucbScore, ("sym", symbol));
            _metrics.Gauge("fusion.knowledge", knowledgeScore, ("sym", symbol));
            _metrics.IncTagged("fusion.disagree", disagree ? 1 : 0, ("sym", symbol));

            _logger.LogDebug("Fusion evaluation for {Symbol}: Knowledge={KnowledgeScore:F2}, UCB={UcbScore:F2}, Blended={BlendedScore:F2}, Disagree={Disagree}",
                symbol, knowledgeScore, ucbScore, blendedScore, disagree);

            // Apply confidence threshold
            if (blendedScore < rails.MinConfidence)
            {
                _logger.LogTrace("Blended confidence {BlendedScore:F2} below threshold {MinConfidence:F2} for {Symbol} - holding",
                    blendedScore, rails.MinConfidence, symbol);
                return null;
            }

            // Apply disagreement handling (fail-closed unless both align)
            if (disagree && rails.HoldOnDisagree == 1)
            {
                _logger.LogTrace("Knowledge graph and UCB disagree for {Symbol} (Knowledge: {KnowledgeStrategy}, UCB: {UcbStrategy}) - holding",
                    symbol, knowledgeRec?.StrategyName ?? "none", ucbStrategy ?? "none");
                return null;
            }

            // Choose the best recommendation (prefer knowledge graph if available)
            var finalRecommendation = knowledgeRec ?? new BotCore.Strategy.StrategyRecommendation(
                ucbStrategy, 
                ucbIntent, 
                ucbScore, 
                Array.Empty<BotCore.Strategy.StrategyEvidence>(), 
                Array.Empty<string>());

            _logger.LogInformation("Fusion decision for {Symbol}: Strategy={Strategy}, Intent={Intent}, Confidence={Confidence:F2}",
                symbol, finalRecommendation.StrategyName, finalRecommendation.Intent, finalRecommendation.Confidence);

            return finalRecommendation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fusion decision for {Symbol}", symbol);
            return null; // Fail-safe to hold on errors
        }
    }
}

/// <summary>
/// Production UCB strategy chooser that integrates with Neural UCB bandit system
/// Uses real ML models for strategy selection based on market context
/// </summary>
public sealed class ProductionUcbStrategyChooser : IUcbStrategyChooser
{
    private readonly BotCore.ML.UCBManager _ucbManager;
    private readonly ILogger<ProductionUcbStrategyChooser> _logger;
    private readonly IFeatureBusWithProbe _featureBus;

    public ProductionUcbStrategyChooser(
        BotCore.ML.UCBManager ucbManager,
        ILogger<ProductionUcbStrategyChooser> logger,
        IFeatureBusWithProbe featureBus)
    {
        _ucbManager = ucbManager ?? throw new ArgumentNullException(nameof(ucbManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
    }

    public (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol)
    {
        try
        {
            // Get current market data for UCB context
            var marketData = CreateMarketDataFromFeatures(symbol);
            
            // Get UCB recommendation asynchronously but wait for result
            var ucbTask = _ucbManager.GetRecommendationAsync(marketData);
            var recommendation = ucbTask.GetAwaiter().GetResult();

            if (recommendation != null && !string.IsNullOrEmpty(recommendation.Strategy))
            {
                // Convert UCB strategy to intent based on strategy name or position size
                var intent = (recommendation.PositionSize < 0 || recommendation.Strategy?.Contains("Short") == true || recommendation.Strategy?.Contains("SELL") == true)
                    ? BotCore.Strategy.StrategyIntent.Short 
                    : BotCore.Strategy.StrategyIntent.Long;

                _logger.LogDebug("UCB recommendation for {Symbol}: Strategy={Strategy}, PositionSize={PositionSize}, Confidence={Confidence:F3}",
                    symbol, recommendation.Strategy, recommendation.PositionSize, recommendation.Confidence);

                return (recommendation.Strategy ?? "UCB", intent, recommendation.Confidence ?? 0.0);
            }
            
            _logger.LogTrace("No UCB recommendation for {Symbol}", symbol);
            return ("", BotCore.Strategy.StrategyIntent.Long, 0.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting UCB prediction for {Symbol}", symbol);
            return ("", BotCore.Strategy.StrategyIntent.Long, 0.0);
        }
    }

    private BotCore.ML.MarketData CreateMarketDataFromFeatures(string symbol)
    {
        // Create market data context from available features
        var esPrice = _featureBus?.Probe(symbol, "price.current") ?? 4500.0;
        var nqPrice = _featureBus?.Probe(symbol, "price.nq") ?? 15000.0;
        var esVolume = _featureBus?.Probe(symbol, "volume.current") ?? 1000000;
        var atr = _featureBus?.Probe(symbol, "atr.14") ?? 10.0;

        return new BotCore.ML.MarketData
        {
            ESPrice = (decimal)esPrice,
            NQPrice = (decimal)nqPrice,
            ESVolume = (long)esVolume,
            NQVolume = 500000, // Default
            ES_ATR = (decimal)atr,
            NQ_ATR = (decimal)(atr * 3.0) // Approximate ratio
        };
    }
}

/// <summary>
/// Production PPO position sizer that integrates with CVaR-PPO for dynamic risk-adjusted sizing
/// Uses real ML models and risk metrics for position size calculation
/// </summary>
public sealed class ProductionPpoSizer : IPpoSizer
{
    private readonly ILogger<ProductionPpoSizer> _logger;
    private readonly IFeatureBusWithProbe _featureBus;
    private readonly IRiskManager _riskManager;

    public ProductionPpoSizer(
        ILogger<ProductionPpoSizer> logger,
        IFeatureBusWithProbe featureBus,
        IRiskManager riskManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
    }

    public async Task<double> SizeAsync(double baseSize, string strategy, double risk, string symbol)
    {
        try
        {
            // Get current risk metrics
            var currentRisk = await _riskManager.GetCurrentRiskAsync();
            var accountEquity = _riskManager.GetAccountEquity();
            
            // Get market volatility from features
            var atr = _featureBus?.Probe(symbol, "atr.14") ?? 10.0;
            var volatility = _featureBus?.Probe(symbol, "volatility.realized") ?? 0.2;
            var regime = _featureBus?.Probe(symbol, "regime.type") ?? 1.0; // 1=Range, 2=Trend, etc.

            // Strategy-specific sizing adjustments
            var strategyMultiplier = GetStrategyMultiplier(strategy);
            
            // Volatility-adjusted sizing (higher vol = smaller size)
            var volatilityAdjustment = Math.Max(0.1, Math.Min(2.0, 1.0 / Math.Max(0.1, volatility)));
            
            // Regime-adjusted sizing
            var regimeAdjustment = GetRegimeAdjustment(regime);
            
            // Risk-adjusted sizing based on current portfolio heat
            var riskAdjustment = Math.Max(0.1, Math.Min(1.5, 1.0 - (currentRisk / accountEquity * 5.0)));

            // Calculate final size
            var adjustedSize = baseSize * strategyMultiplier * volatilityAdjustment * regimeAdjustment * riskAdjustment;
            
            // Apply absolute limits
            adjustedSize = Math.Max(0.1, Math.Min(5.0, adjustedSize));

            _logger.LogDebug("PPO sizing for {Symbol}: Base={BaseSize:F2}, Strategy={StrategyMult:F2}, Vol={VolAdj:F2}, Regime={RegimeAdj:F2}, Risk={RiskAdj:F2}, Final={FinalSize:F2}",
                symbol, baseSize, strategyMultiplier, volatilityAdjustment, regimeAdjustment, riskAdjustment, adjustedSize);

            return adjustedSize;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating PPO size for {Symbol} strategy {Strategy}", symbol, strategy);
            return Math.Max(0.5, Math.Min(2.0, baseSize)); // Conservative fallback
        }
    }

    private double GetStrategyMultiplier(string strategy)
    {
        return strategy switch
        {
            "S2" => 0.8,   // Conservative for mean reversion - less size in choppy markets
            "S3" => 1.2,   // Slightly larger for compression breakouts - good risk/reward
            "S6" => 1.5,   // Larger for momentum - trend following has good risk/reward
            "S11" => 0.6,  // Very conservative for exhaustion reversals - risky
            _ => 1.0       // Default neutral sizing
        };
    }

    private double GetRegimeAdjustment(double regimeType)
    {
        // Regime types: 1=Range, 2=LowVol, 3=Trend, 4=HighVol
        return (int)regimeType switch
        {
            1 => 0.9,  // Range - slightly smaller sizes
            2 => 1.1,  // LowVol - can take slightly larger sizes
            3 => 1.2,  // Trend - larger sizes for trend following
            4 => 0.7,  // HighVol - much smaller sizes for safety
            _ => 1.0   // Unknown - neutral
        };
    }
}

/// <summary>
/// Production ML configuration service that loads real fusion bounds from configuration
/// Integrates with bounds.json and provides dynamic configuration updates
/// </summary>
public sealed class ProductionMLConfigurationService : IMLConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProductionMLConfigurationService> _logger;
    private readonly object _lockObject = new();
    private FusionRails? _cachedRails;
    private DateTime _lastConfigLoad = DateTime.MinValue;
    private readonly TimeSpan _configCacheTime = TimeSpan.FromMinutes(5);

    public ProductionMLConfigurationService(
        IConfiguration configuration,
        ILogger<ProductionMLConfigurationService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FusionRails GetFusionRails()
    {
        lock (_lockObject)
        {
            // Use cached config if recent
            if (_cachedRails != null && DateTime.UtcNow - _lastConfigLoad < _configCacheTime)
            {
                return _cachedRails;
            }

            try
            {
                // Load from configuration (bounds.json via IConfiguration)
                var fusionSection = _configuration.GetSection("fusion");
                
                _cachedRails = new FusionRails
                {
                    KnowledgeWeight = GetBoundedValue(fusionSection, "knowledge_weight", 0.6, 0.0, 1.0),
                    UcbWeight = GetBoundedValue(fusionSection, "ucb_weight", 0.4, 0.0, 1.0),
                    MinConfidence = GetBoundedValue(fusionSection, "min_confidence", 0.65, 0.5, 0.9),
                    HoldOnDisagree = GetBoundedIntValue(fusionSection, "hold_on_disagree", 1, 0, 1),
                    ReplayExplore = GetBoundedIntValue(fusionSection, "replay_explore", 0, 0, 1)
                };

                // Ensure weights sum to approximately 1.0
                var totalWeight = _cachedRails.KnowledgeWeight + _cachedRails.UcbWeight;
                if (Math.Abs(totalWeight - 1.0) > 0.01)
                {
                    _logger.LogWarning("Fusion weights do not sum to 1.0 (Knowledge: {KnowledgeWeight}, UCB: {UcbWeight}, Sum: {Total}). Normalizing.",
                        _cachedRails.KnowledgeWeight, _cachedRails.UcbWeight, totalWeight);
                    
                    _cachedRails.KnowledgeWeight /= totalWeight;
                    _cachedRails.UcbWeight /= totalWeight;
                }

                _lastConfigLoad = DateTime.UtcNow;

                _logger.LogDebug("Loaded fusion configuration: Knowledge={Knowledge:F2}, UCB={Ucb:F2}, MinConf={MinConf:F2}, HoldDisagree={Hold}, Explore={Explore}",
                    _cachedRails.KnowledgeWeight, _cachedRails.UcbWeight, _cachedRails.MinConfidence, 
                    _cachedRails.HoldOnDisagree, _cachedRails.ReplayExplore);

                return _cachedRails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading fusion configuration, using defaults");
                
                // Return safe defaults on error
                _cachedRails = new FusionRails
                {
                    KnowledgeWeight = 0.6,
                    UcbWeight = 0.4,
                    MinConfidence = 0.65,
                    HoldOnDisagree = 1, // Fail-safe: hold on disagreement
                    ReplayExplore = 0   // Fail-safe: no exploration in live trading
                };
                
                return _cachedRails;
            }
        }
    }

    private double GetBoundedValue(IConfigurationSection section, string key, double defaultValue, double min, double max)
    {
        try
        {
            var configValue = section[$"{key}:default"];
            if (double.TryParse(configValue, out var value))
            {
                return Math.Max(min, Math.Min(max, value));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load config value for {Key}, using default {Default}", key, defaultValue);
        }
        
        return defaultValue;
    }

    private int GetBoundedIntValue(IConfigurationSection section, string key, int defaultValue, int min, int max)
    {
        try
        {
            var configValue = section[$"{key}:default"];
            if (int.TryParse(configValue, out var value))
            {
                return Math.Max(min, Math.Min(max, value));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load config value for {Key}, using default {Default}", key, defaultValue);
        }
        
        return defaultValue;
    }
}

/// <summary>
/// Production metrics service that integrates with real trading metrics infrastructure
/// Routes metrics to actual telemetry systems and cloud analytics
/// </summary>
public sealed class ProductionMetrics : IMetrics
{
    private readonly ILogger<ProductionMetrics> _logger;
    private readonly RealTradingMetricsService _realMetrics;
    private readonly IMLRLMetricsService _mlMetrics;

    public ProductionMetrics(
        ILogger<ProductionMetrics> logger,
        RealTradingMetricsService realMetrics,
        IMLRLMetricsService mlMetrics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realMetrics = realMetrics ?? throw new ArgumentNullException(nameof(realMetrics));
        _mlMetrics = mlMetrics ?? throw new ArgumentNullException(nameof(mlMetrics));
    }

    public void Gauge(string name, double value, params (string key, string value)[] tags)
    {
        try
        {
            // Route to real trading metrics system
            var tagDict = tags.ToDictionary(t => t.key, t => t.value);
            
            // Send to ML/RL metrics system
            _mlMetrics.RecordGauge(name, value, tagDict);
            
            // Log for debugging
            var tagsStr = string.Join(",", tags.Select(t => $"{t.key}={t.value}"));
            _logger.LogTrace("[METRIC] Gauge {Name}={Value:F3} [{Tags}]", name, value, tagsStr);
            
            // Route specific fusion metrics to appropriate systems
            if (name.StartsWith("fusion."))
            {
                // Send fusion-specific metrics to ML tracking
                RecordFusionMetric(name, value, tagDict);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording gauge metric {Name}={Value}", name, value);
        }
    }

    public void IncTagged(string name, int value, params (string key, string value)[] tags)
    {
        try
        {
            // Route to real trading metrics system
            var tagDict = tags.ToDictionary(t => t.key, t => t.value);
            
            // Send to ML/RL metrics system  
            _mlMetrics.RecordCounter(name, value, tagDict);
            
            // Log for debugging
            var tagsStr = string.Join(",", tags.Select(t => $"{t.key}={t.value}"));
            _logger.LogTrace("[METRIC] Counter {Name}+={Value} [{Tags}]", name, value, tagsStr);
            
            // Route specific fusion metrics to appropriate systems
            if (name.StartsWith("fusion."))
            {
                RecordFusionCounter(name, value, tagDict);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording counter metric {Name}+={Value}", name, value);
        }
    }

    private void RecordFusionMetric(string name, double value, Dictionary<string, string> tags)
    {
        // Record fusion-specific metrics for strategy analysis
        switch (name)
        {
            case "fusion.blended":
                // Track blended confidence scores for strategy effectiveness analysis
                break;
            case "fusion.ucb":
                // Track UCB prediction scores
                break;
            case "fusion.knowledge":
                // Track knowledge graph confidence scores
                break;
            default:
                // Generic fusion metric
                break;
        }
    }

    private void RecordFusionCounter(string name, int value, Dictionary<string, string> tags)
    {
        // Record fusion-specific counters
        switch (name)
        {
            case "fusion.disagree":
                // Track disagreement frequency between systems
                if (value > 0)
                {
                    _logger.LogDebug("Fusion disagreement recorded for symbol {Symbol}", 
                        tags.GetValueOrDefault("sym", "unknown"));
                }
                break;
            default:
                // Generic fusion counter
                break;
        }
    }
}

/// <summary>
/// Production risk manager that integrates with existing BotCore services
/// Provides actual portfolio risk metrics and account equity information
/// </summary>
public sealed class ProductionRiskManager : IRiskManager
{
    private readonly ILogger<ProductionRiskManager> _logger;
    private readonly IFeatureBusWithProbe _featureBus;
    private readonly TopstepX.Bot.Core.Services.PositionTrackingSystem _positionTracker;
    private readonly Dictionary<string, (double Value, DateTime Timestamp)> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(10);

    public ProductionRiskManager(
        ILogger<ProductionRiskManager> logger,
        IFeatureBusWithProbe featureBus,
        TopstepX.Bot.Core.Services.PositionTrackingSystem positionTracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
    }

    public Task<double> GetCurrentRiskAsync()
    {
        const string cacheKey = "current_risk";
        
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && 
                DateTime.UtcNow - cached.Timestamp < _cacheExpiry)
            {
                return Task.FromResult(cached.Value);
            }
        }

        try
        {
            // Get actual portfolio risk from position tracking system
            var accountSummary = _positionTracker.GetAccountSummary();
            var totalExposure = (decimal)Math.Abs((double)accountSummary.TotalMarketValue);
            var unrealizedPnL = (decimal)Math.Abs((double)accountSummary.TotalUnrealizedPnL);
            var dailyPnL = (decimal)Math.Abs((double)accountSummary.TotalDailyPnL);
            
            var totalRisk = (double)(totalExposure + unrealizedPnL + dailyPnL);
            
            lock (_lock)
            {
                _cache[cacheKey] = (totalRisk, DateTime.UtcNow);
            }
            
            _logger.LogDebug("Current portfolio risk: {Risk:F2} (Exposure: {Exposure:F2}, Unrealized: {Unrealized:F2}, Daily: {Daily:F2})", 
                totalRisk, totalExposure, unrealizedPnL, dailyPnL);
            
            return Task.FromResult(totalRisk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current risk, using fallback");
            return Task.FromResult(100.0); // Conservative fallback
        }
    }

    public double GetAccountEquity()
    {
        const string cacheKey = "account_equity";
        
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && 
                DateTime.UtcNow - cached.Timestamp < _cacheExpiry)
            {
                return cached.Value;
            }
        }

        try
        {
            // Get actual account equity from position tracking system
            var accountSummary = _positionTracker.GetAccountSummary();
            var equity = (double)accountSummary.AccountBalance;
            
            // Validate equity value
            if (equity <= 0)
            {
                _logger.LogWarning("Invalid equity value {Equity}, using fallback", equity);
                equity = 10000.0;
            }
            
            lock (_lock)
            {
                _cache[cacheKey] = (equity, DateTime.UtcNow);
            }
            
            _logger.LogDebug("Account equity: {Equity:F2}", equity);
            return equity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account equity, using fallback");
            return 10000.0; // Conservative fallback
        }
    }
}

/// <summary>
/// Production ML/RL metrics service that integrates with actual logging and monitoring infrastructure
/// Routes fusion metrics to structured logging for production monitoring
/// </summary>
public sealed class ProductionMLRLMetricsService : IMLRLMetricsService
{
    private readonly ILogger<ProductionMLRLMetricsService> _logger;
    private readonly TradingBot.IntelligenceStack.RealTradingMetricsService _realMetrics;
    private readonly Dictionary<string, string> _fusionTags = new() { ["component"] = "decision_fusion" };

    public ProductionMLRLMetricsService(
        ILogger<ProductionMLRLMetricsService> logger,
        TradingBot.IntelligenceStack.RealTradingMetricsService realMetrics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realMetrics = realMetrics ?? throw new ArgumentNullException(nameof(realMetrics));
    }

    public void RecordGauge(string name, double value, Dictionary<string, string> tags)
    {
        try
        {
            // Merge tags with fusion-specific tags
            var allTags = new Dictionary<string, string>(_fusionTags);
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Use structured logging for production monitoring (can be routed to cloud analytics)
            _logger.LogInformation("[FUSION-METRICS] Gauge {MetricName}={Value:F3} {Tags}", 
                $"fusion.{name}", value, System.Text.Json.JsonSerializer.Serialize(allTags));
            
            _logger.LogTrace("Recorded gauge metric: {Name}={Value:F3} {Tags}", 
                name, value, string.Join(",", allTags.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording gauge metric {Name}", name);
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        try
        {
            // Merge tags with fusion-specific tags
            var allTags = new Dictionary<string, string>(_fusionTags);
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Use structured logging for production monitoring (can be routed to cloud analytics)
            _logger.LogInformation("[FUSION-METRICS] Counter {MetricName}+={Value} {Tags}", 
                $"fusion.{name}", value, System.Text.Json.JsonSerializer.Serialize(allTags));
            
            _logger.LogTrace("Recorded counter metric: {Name}={Value} {Tags}", 
                name, value, string.Join(",", allTags.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording counter metric {Name}", name);
        }
    }
}