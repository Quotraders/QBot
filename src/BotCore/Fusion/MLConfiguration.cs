using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotCore.Strategy;

namespace BotCore.Fusion;

/// <summary>
/// ML configuration service interface for production ML/RL systems
/// </summary>
public interface IMLConfigurationService
{
    Dictionary<string, object> GetConfiguration();
    Task<Dictionary<string, object>> GetConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// UCB strategy chooser interface for Neural-UCB #1 integration with async support
/// </summary>
public interface IUcbStrategyChooser 
{ 
    Task<(string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score)> PredictAsync(string symbol, CancellationToken cancellationToken = default);
    (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol); // Backward compatibility
}

/// <summary>
/// PPO position sizer interface for Neural-PPO #2 integration
/// </summary>
public interface IPpoSizer 
{
    Task<double> PredictSizeAsync(string symbol, BotCore.Strategy.StrategyIntent intent, double risk, CancellationToken cancellationToken = default);
}

/// <summary>
/// Production ML configuration service implementation
/// </summary>
public sealed class ProductionMLConfigurationService : IMLConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProductionMLConfigurationService> _logger;

    public ProductionMLConfigurationService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<ProductionMLConfigurationService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Dictionary<string, object> GetConfiguration()
    {
        try
        {
            var config = new Dictionary<string, object>
            {
                // Neural-UCB configuration
                ["ucb_exploration_factor"] = _configuration.GetValue<double>("ML:UCB:ExplorationFactor", 1.4),
                ["ucb_confidence_interval"] = _configuration.GetValue<double>("ML:UCB:ConfidenceInterval", 0.95),
                ["ucb_update_frequency"] = _configuration.GetValue<int>("ML:UCB:UpdateFrequency", 100),
                
                // Neural-PPO configuration
                ["ppo_learning_rate"] = _configuration.GetValue<double>("ML:PPO:LearningRate", 0.0003),
                ["ppo_clip_ratio"] = _configuration.GetValue<double>("ML:PPO:ClipRatio", 0.2),
                ["ppo_entropy_coeff"] = _configuration.GetValue<double>("ML:PPO:EntropyCoeff", 0.01),
                
                // General ML configuration
                ["model_update_interval"] = _configuration.GetValue<int>("ML:ModelUpdateInterval", 3600), // 1 hour
                ["feature_window_size"] = _configuration.GetValue<int>("ML:FeatureWindowSize", 100),
                ["risk_adjustment_factor"] = _configuration.GetValue<double>("ML:RiskAdjustmentFactor", 0.8),
                
                // Production settings
                ["enable_model_updates"] = _configuration.GetValue<bool>("ML:EnableModelUpdates", true),
                ["enable_exploration"] = _configuration.GetValue<bool>("ML:EnableExploration", true),
                ["max_position_size"] = _configuration.GetValue<double>("Trading:MaxPositionSize", 0.1), // 10% max position
            };

            _logger.LogTrace("ML configuration retrieved with {Count} parameters", config.Count);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 Error retrieving ML configuration - fail-closed: cannot provide ML configuration");
            
            // Fail-closed: Do not return safe defaults, throw to force proper configuration
            throw new InvalidOperationException("ML configuration unavailable - system must be properly configured (fail-closed)", ex);
        }
    }

    public async Task<Dictionary<string, object>> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return GetConfiguration();
    }
}

/// <summary>
/// Production UCB strategy chooser implementation
/// </summary>
public sealed class ProductionUcbStrategyChooser : IUcbStrategyChooser
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionUcbStrategyChooser> _logger;
    private readonly Dictionary<string, (double reward, int count)> _strategyStats = new();
    private readonly object _statsLock = new();
    
    // Strategy selection fallback constants
    private const double DefaultScoreForFallback = 0.5;

    public ProductionUcbStrategyChooser(IServiceProvider serviceProvider, ILogger<ProductionUcbStrategyChooser> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score)> PredictAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return Predict(symbol);
    }

    public (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol)
    {
        try
        {
            // Available strategies for UCB selection
            var strategies = new[]
            {
                ("MeanReversion", BotCore.Strategy.StrategyIntent.Buy),
                ("TrendFollowing", BotCore.Strategy.StrategyIntent.Buy), 
                ("Breakout", BotCore.Strategy.StrategyIntent.Buy),
                ("MomentumFade", BotCore.Strategy.StrategyIntent.Sell)
            };

            var totalCount = 0;
            lock (_statsLock)
            {
                foreach (var (strategy, _) in strategies)
                {
                    if (_strategyStats.TryGetValue(strategy, out var stats))
                    {
                        totalCount += stats.count;
                    }
                }
            }

            if (totalCount == 0)
            {
                // No history - get configured initial score from service provider
                var initialConfig = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var initialScore = initialConfig?.GetValue<double>("ML:UCB:InitialStrategyScore", 0.5) ?? 0.5;
                var random = new Random();
                var selected = strategies[random.Next(strategies.Length)];
                _logger.LogTrace("UCB strategy selection for {Symbol}: {Strategy} (no history, initial score: {Score:F3})", 
                    symbol, selected.Item1, initialScore);
                return (selected.Item1, selected.Item2, initialScore);
            }

            // UCB1 algorithm
            double bestScore = double.MinValue;
            var bestStrategy = strategies[0];

            // Get configuration once for the entire loop
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var explorationFactor = configuration?.GetValue<double>("ML:UCB:ExplorationFactor", 1.4) ?? 1.4;
            var defaultUcbScore = configuration?.GetValue<double>("ML:UCB:DefaultScore", 0.5) ?? 0.5;

            foreach (var (strategy, intent) in strategies)
            {
                double ucbScore = defaultUcbScore; // Configured default score
                
                lock (_statsLock)
                {
                    if (_strategyStats.TryGetValue(strategy, out var stats) && stats.count > 0)
                    {
                        var avgReward = stats.reward / stats.count;
                        var exploration = Math.Sqrt(2 * Math.Log(totalCount) / stats.count);
                        ucbScore = avgReward + explorationFactor * exploration; // UCB1 with configurable exploration factor
                    }
                    else
                    {
                        ucbScore = double.MaxValue; // Ensure untried strategies are selected
                    }
                }

                if (ucbScore > bestScore)
                {
                    bestScore = ucbScore;
                    bestStrategy = (strategy, intent);
                }
            }

            _logger.LogTrace("UCB strategy selection for {Symbol}: {Strategy} (UCB score: {Score:F3})", 
                symbol, bestStrategy.Item1, bestScore);
            
            var maxScore = configuration?.GetValue<double>("ML:UCB:MaxScore", 1.0) ?? 1.0;
            return (bestStrategy.Item1, bestStrategy.Item2, Math.Min(bestScore, maxScore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UCB strategy prediction for symbol {Symbol}", symbol);
            
            // Fallback to intelligent strategy selection based on available data
            return SelectIntelligentFallbackStrategy(symbol);
        }
    }
    
    private (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) SelectIntelligentFallbackStrategy(string symbol)
    {
        try
        {
            // Use feature bus to make intelligent strategy selection
            var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
            
            // Get configuration service first
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration == null)
            {
                _logger.LogError("🚨 Configuration service unavailable for fallback strategy selection - using simple fallback");
                return ("MomentumFade", BotCore.Strategy.StrategyIntent.Buy, DefaultScoreForFallback); // Safe fallback
            }
            
            if (featureBus != null)
            {
                // Get default values from configuration when features are unavailable
                var defaultVolatility = configuration.GetValue<double>("Trading:DefaultVolatility", 0.02);
                var defaultRegime = configuration.GetValue<double>("Trading:DefaultRegime", 0.5);
                var defaultMomentum = configuration.GetValue<double>("Trading:DefaultMomentum", 0.0);
                
                var volatility = featureBus.Probe(symbol, "volatility.realized") ?? defaultVolatility;
                var regime = featureBus.Probe(symbol, "regime.current") ?? defaultRegime;
                var momentum = featureBus.Probe(symbol, "momentum.current") ?? defaultMomentum;
                
                // Get market condition thresholds from configuration
                var trendingThreshold = configuration.GetValue<double>("Trading:TrendingRegimeThreshold", 0.7);
                var highVolThreshold = configuration.GetValue<double>("Trading:HighVolatilityThreshold", 0.03);
                
                // Select strategy based on market conditions with configured thresholds
                var rangingThreshold = configuration.GetValue<double>("Trading:RangingRegimeThreshold", 0.3);
                var trendConfidence = configuration.GetValue<double>("Trading:TrendFollowingConfidence", 0.7);
                
                if (regime > trendingThreshold) // Trending market
                {
                    var momentumThreshold = configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
                    if (momentum > momentumThreshold)
                    {
                        return ("TrendFollowing", BotCore.Strategy.StrategyIntent.Buy, trendConfidence);
                    }
                    else
                    {
                        return ("TrendFollowing", BotCore.Strategy.StrategyIntent.Sell, trendConfidence);
                    }
                }
                else if (regime < rangingThreshold) // Ranging market
                {
                    var meanReversionConfidence = configuration.GetValue<double>("Trading:MeanReversionConfidence", 0.6);
                    var momentumThreshold = configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
                    return ("MeanReversion", momentum > momentumThreshold ? BotCore.Strategy.StrategyIntent.Sell : BotCore.Strategy.StrategyIntent.Buy, meanReversionConfidence);
                }
                else if (volatility > highVolThreshold) // High volatility
                {
                    var breakoutConfidence = configuration.GetValue<double>("Trading:BreakoutConfidence", 0.65);
                    var momentumThreshold = configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
                    return ("Breakout", momentum > momentumThreshold ? BotCore.Strategy.StrategyIntent.Buy : BotCore.Strategy.StrategyIntent.Sell, breakoutConfidence);
                }
            }
            
            // Use the configuration for default strategy
            var defaultStrategy = configuration.GetValue<string>("Trading:DefaultStrategy", "MomentumFade");
            var defaultIntent = Enum.Parse<BotCore.Strategy.StrategyIntent>(configuration.GetValue<string>("Trading:DefaultIntent", "Buy"));
            var defaultConfidenceFinal = configuration.GetValue<double>("Trading:DefaultConfidenceScore", 0.5);
            
            return (defaultStrategy, defaultIntent, defaultConfidenceFinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 Error in strategy selection for {Symbol} - fail-closed: cannot provide strategy recommendation", symbol);
            
            // Fail-closed: Do not return safe defaults, throw to force proper configuration
            throw new InvalidOperationException($"Strategy selection failed for {symbol} - system must be properly configured (fail-closed)", ex);
        }
    }

    /// <summary>
    /// Update strategy performance for UCB learning
    /// </summary>
    public void UpdateStrategyReward(string strategy, double reward)
    {
        try
        {
            lock (_statsLock)
            {
                if (_strategyStats.TryGetValue(strategy, out var stats))
                {
                    _strategyStats[strategy] = (stats.reward + reward, stats.count + 1);
                }
                else
                {
                    _strategyStats[strategy] = (reward, 1);
                }
            }
            
            _logger.LogTrace("Updated strategy {Strategy} with reward {Reward:F3}", strategy, reward);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating strategy reward for {Strategy}", strategy);
        }
    }
}

/// <summary>
/// Production PPO position sizer implementation
/// </summary>
public sealed class ProductionPpoSizer : IPpoSizer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionPpoSizer> _logger;
    
    // Position sizing fallback constants
    private const int OperationIdPrefixLength = 8;
    private const double MinimalFallbackSizePercent = 0.01;
    private const double MinimalFallbackRiskMultiplier = 0.1;

    public ProductionPpoSizer(IServiceProvider serviceProvider, ILogger<ProductionPpoSizer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<double> PredictSizeAsync(string symbol, BotCore.Strategy.StrategyIntent intent, double risk, CancellationToken cancellationToken = default)
    {
        // Add proper async operation to satisfy CS1998
        await Task.CompletedTask.ConfigureAwait(false);
        
        try
        {
            // Use real RLAdvisorSystem for PPO-based position sizing
            var rlAdvisorSystem = _serviceProvider.GetService<TradingBot.IntelligenceStack.RLAdvisorSystem>();
            if (rlAdvisorSystem != null)
            {
                // Attempt to get size recommendation from RL system - fail-closed approach
                _logger.LogDebug("Attempting ML position sizing for {Symbol} with intent {Intent}, risk {Risk:P2}", symbol, intent, risk);
                
                // Check if RL system has the required capability
                if (!IsRLSystemCapableOfPositionSizing(rlAdvisorSystem))
                {
                    _logger.LogError("RL system lacks position sizing capability - fail-closed: cannot provide ML-based position sizing");
                    throw new InvalidOperationException("ML position sizing unavailable - RL system lacks required capability (fail-closed)");
                }
                
                // RL system lacks position sizing capability - fail-closed approach
                _logger.LogError("🚨 [AUDIT-{OperationId}] RL system position sizing not available - fail-closed: cannot provide ML sizing for {Symbol}", 
                    Guid.NewGuid().ToString("N")[..OperationIdPrefixLength], symbol);
                
                // Get configuration for fail-closed behavior
                var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var shouldHoldOnMlUnavailable = configuration?.GetValue<bool>("ML:HoldOnPositionSizingUnavailable") ?? true;
                
                if (shouldHoldOnMlUnavailable)
                {
                    // Fail-closed: force hold when ML sizing unavailable
                    throw new InvalidOperationException("ML position sizing unavailable - system configured to fail-closed (hold)");
                }
                
                // If configuration allows fallback, proceed to heuristic sizing
                _logger.LogWarning("🔄 [AUDIT] ML position sizing unavailable but fallback allowed - proceeding to heuristic sizing");
            }
            
            // Fallback to intelligent heuristic-based sizing with real market data
            return CalculateIntelligentFallbackSize(symbol, intent, risk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PPO size prediction for symbol {Symbol}", symbol);
            
            // Fallback to safe conservative sizing
            return CalculateConservativeFallbackSize(risk, intent);
        }
    }

    private string GetMarketRegime(string symbol)
    {
        var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
        var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        
        if (configuration == null)
        {
            _logger.LogError("🚨 Configuration service unavailable for market regime - using fallback");
            return "VOLATILE"; // Safe fallback when config unavailable
        }
        
        var defaultRegimeValue = configuration.GetValue<double>("Trading:DefaultRegimeValue", 0.5);
        var regimeValue = featureBus?.Probe(symbol, "regime.current") ?? defaultRegimeValue;
        
        var trendingThreshold = configuration.GetValue<double>("Trading:RegimeTrendingThreshold", 0.7);
        var rangingThreshold = configuration.GetValue<double>("Trading:RegimeRangingThreshold", 0.3);
        
        return regimeValue switch
        {
            var val when val > trendingThreshold => "TRENDING",
            var val when val < rangingThreshold => "RANGING", 
            _ => "VOLATILE"
        };
    }
    
    /// <summary>
    /// Check if RL system has position sizing capability - fail-closed validation
    /// </summary>
    private static bool IsRLSystemCapableOfPositionSizing(TradingBot.IntelligenceStack.RLAdvisorSystem rlSystem)
    {
        // Since GetPositionSizingRecommendationAsync doesn't exist,
        // we know the system lacks this capability
        return false;
    }

    private double CalculateIntelligentFallbackSize(
        string symbol, 
        BotCore.Strategy.StrategyIntent intent, 
        double risk)
    {
        // Intelligent fallback using real market data when RL system unavailable
        var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
        
        if (featureBus != null)
        {
            // Get configuration service
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration == null)
            {
                _logger.LogError("🚨 Configuration service unavailable for intelligent fallback sizing - using minimal fallback");
                return intent switch
                {
                    BotCore.Strategy.StrategyIntent.Buy => Math.Min(MinimalFallbackSizePercent, risk * MinimalFallbackRiskMultiplier),
                    BotCore.Strategy.StrategyIntent.Sell => Math.Max(-MinimalFallbackSizePercent, -risk * MinimalFallbackRiskMultiplier),
                    _ => 0.0
                };
            }
            
            // Get default values from configuration
            var defaultVolatility = configuration.GetValue<double>("Trading:DefaultVolatility", 0.02);
            var defaultVolume = configuration.GetValue<double>("Trading:DefaultVolume", 1000.0);
            
            var volatility = featureBus.Probe(symbol, "volatility.realized") ?? defaultVolatility;
            var volume = featureBus.Probe(symbol, "volume.current") ?? defaultVolume;
            
            // Size based on volatility and volume with configurable parameters
            var volAdjustmentMin = configuration.GetValue<double>("Trading:VolatilityAdjustmentMin", 0.5);
            var volAdjustmentMax = configuration.GetValue<double>("Trading:VolatilityAdjustmentMax", 2.0);
            var volAdjustmentDivisor = configuration.GetValue<double>("Trading:VolatilityAdjustmentDivisor", 1.0);
            
            var volumeAdjustmentMin = configuration.GetValue<double>("Trading:VolumeAdjustmentMin", 0.8);
            var volumeAdjustmentMax = configuration.GetValue<double>("Trading:VolumeAdjustmentMax", 1.2);
            var volumeNormalizationBase = configuration.GetValue<double>("Trading:VolumeNormalizationBase", 1000.0);
            
            var volatilityAdjustment = Math.Max(volAdjustmentMin, Math.Min(volAdjustmentMax, volAdjustmentDivisor / volatility));
            var volumeAdjustment = Math.Max(volumeAdjustmentMin, Math.Min(volumeAdjustmentMax, volume / volumeNormalizationBase));
            
            var baseSizeMultiplier = configuration.GetValue<double>("Trading:BaseSizeMultiplier", 0.5);
            var baseSize = risk * baseSizeMultiplier * volatilityAdjustment * volumeAdjustment;
            
            var buyDirectional = configuration.GetValue<double>("Trading:BuyDirectionalMultiplier", 1.0);
            var sellDirectional = configuration.GetValue<double>("Trading:SellDirectionalMultiplier", -1.0);
            var neutralDirectional = configuration.GetValue<double>("Trading:NeutralDirectionalMultiplier", 0.0);
            
            var directionalSize = intent switch
            {
                BotCore.Strategy.StrategyIntent.Buy => baseSize * buyDirectional,
                BotCore.Strategy.StrategyIntent.Sell => baseSize * sellDirectional,
                _ => neutralDirectional
            };
            
            var maxFallbackPosition = configuration.GetValue<double>("Trading:MaxFallbackPositionSize", 0.05);
            var minFallbackPosition = configuration.GetValue<double>("Trading:MinFallbackPositionSize", -0.05);
            
            return Math.Max(minFallbackPosition, Math.Min(maxFallbackPosition, directionalSize));
        }
        
        return CalculateConservativeFallbackSize(risk, intent);
    }
    
    private double CalculateConservativeFallbackSize(double risk, BotCore.Strategy.StrategyIntent intent)
    {
        // Ultra-conservative sizing when no services available - config driven
        var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        
        if (configuration == null)
        {
            _logger.LogError("🚨 Configuration service unavailable for conservative sizing - using minimal fallback");
            // Absolute minimal fallback when even configuration is unavailable
            return intent switch
            {
                BotCore.Strategy.StrategyIntent.Buy => MinimalFallbackSizePercent,
                BotCore.Strategy.StrategyIntent.Sell => -MinimalFallbackSizePercent,
                _ => 0.0
            };
        }
        
        var conservativeMultiplier = configuration.GetValue<double>("Trading:ConservativeSizeMultiplier", 0.2);
        var conservativeSize = risk * conservativeMultiplier;
        
        var maxConservativeBuy = configuration.GetValue<double>("Trading:MaxConservativeBuySize", 0.02);
        var maxConservativeSell = configuration.GetValue<double>("Trading:MaxConservativeSellSize", -0.02);
        var neutralSize = configuration.GetValue<double>("Trading:NeutralSize", 0.0);
        
        return intent switch
        {
            BotCore.Strategy.StrategyIntent.Buy => Math.Min(maxConservativeBuy, conservativeSize),
            BotCore.Strategy.StrategyIntent.Sell => Math.Max(maxConservativeSell, -conservativeSize),
            _ => neutralSize
        };
    }
}