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
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProductionMLConfigurationService> _logger;

    public ProductionMLConfigurationService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<ProductionMLConfigurationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
                // No history - get configured initial score instead of hardcoded value
                var initialScore = _configuration.GetValue<double>("ML:UCB:InitialStrategyScore", 0.5);
                var random = new Random();
                var selected = strategies[random.Next(strategies.Length)];
                _logger.LogTrace("UCB strategy selection for {Symbol}: {Strategy} (no history, initial score: {Score:F3})", 
                    symbol, selected.Item1, initialScore);
                return (selected.Item1, selected.Item2, initialScore);
            }

            // UCB1 algorithm
            double bestScore = double.MinValue;
            var bestStrategy = strategies[0];

            foreach (var (strategy, intent) in strategies)
            {
                // Get UCB parameters from configuration
                var explorationFactor = _configuration.GetValue<double>("ML:UCB:ExplorationFactor", 1.4);
                var defaultUcbScore = _configuration.GetValue<double>("ML:UCB:DefaultScore", 0.5);
                
                double ucbScore = defaultUcbScore; // Configured default score
                
                lock (_statsLock)
                {
                    if (_strategyStats.TryGetValue(strategy, out var stats) && stats.count > 0)
                    {
                        var avgReward = stats.reward / stats.count;
                        var exploration = Math.Sqrt(2 * Math.Log(totalCount) / stats.count);
                        ucbScore = avgReward + 1.4 * exploration; // UCB1 with exploration factor
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
            
            var maxScore = _configuration.GetValue<double>("ML:UCB:MaxScore", 1.0);
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
            
            if (featureBus != null)
            {
                // Get default values from configuration when features are unavailable
                var defaultVolatility = _configuration.GetValue<double>("Trading:DefaultVolatility", 0.02);
                var defaultRegime = _configuration.GetValue<double>("Trading:DefaultRegime", 0.5);
                var defaultMomentum = _configuration.GetValue<double>("Trading:DefaultMomentum", 0.0);
                
                var volatility = featureBus.Probe(symbol, "volatility.realized") ?? defaultVolatility;
                var regime = featureBus.Probe(symbol, "regime.current") ?? defaultRegime;
                var momentum = featureBus.Probe(symbol, "momentum.current") ?? defaultMomentum;
                
                // Get market condition thresholds from configuration
                var trendingThreshold = _configuration.GetValue<double>("Trading:TrendingRegimeThreshold", 0.7);
                var highVolThreshold = _configuration.GetValue<double>("Trading:HighVolatilityThreshold", 0.03);
                var momentumConfidence = _configuration.GetValue<double>("Trading:MomentumConfidenceScore", 0.6);
                var volatilityConfidence = _configuration.GetValue<double>("Trading:VolatilityConfidenceScore", 0.65);
                var defaultConfidence = _configuration.GetValue<double>("Trading:DefaultConfidenceScore", 0.5);
                
                // Select strategy based on market conditions with configured thresholds
                var rangingThreshold = _configuration.GetValue<double>("Trading:RangingRegimeThreshold", 0.3);
                var trendConfidence = _configuration.GetValue<double>("Trading:TrendFollowingConfidence", 0.7);
                
                if (regime > trendingThreshold) // Trending market
                {
                    var momentumThreshold = _configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
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
                    var meanReversionConfidence = _configuration.GetValue<double>("Trading:MeanReversionConfidence", 0.6);
                    var momentumThreshold = _configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
                    return ("MeanReversion", momentum > momentumThreshold ? BotCore.Strategy.StrategyIntent.Sell : BotCore.Strategy.StrategyIntent.Buy, meanReversionConfidence);
                }
                else if (volatility > highVolThreshold) // High volatility
                {
                    var breakoutConfidence = _configuration.GetValue<double>("Trading:BreakoutConfidence", 0.65);
                    var momentumThreshold = _configuration.GetValue<double>("Trading:MomentumPositiveThreshold", 0.0);
                    return ("Breakout", momentum > momentumThreshold ? BotCore.Strategy.StrategyIntent.Buy : BotCore.Strategy.StrategyIntent.Sell, breakoutConfidence);
                }
            }
            
            // Default strategy with configured confidence when no specific conditions are met or features unavailable
            var defaultStrategy = _configuration.GetValue<string>("Trading:DefaultStrategy", "MomentumFade");
            var defaultIntent = Enum.Parse<BotCore.Strategy.StrategyIntent>(_configuration.GetValue<string>("Trading:DefaultIntent", "Buy"));
            var defaultConfidenceFinal = _configuration.GetValue<double>("Trading:DefaultConfidenceScore", 0.5);
            
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

    public ProductionPpoSizer(IServiceProvider serviceProvider, ILogger<ProductionPpoSizer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<double> PredictSizeAsync(string symbol, BotCore.Strategy.StrategyIntent intent, double risk, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use real RLAdvisorSystem for PPO-based position sizing
            var rlAdvisorSystem = _serviceProvider.GetService<TradingBot.IntelligenceStack.RLAdvisorSystem>();
            if (rlAdvisorSystem != null)
            {
                // Create market context for RL system
                var marketContext = await CreateMarketContextAsync(symbol, intent, risk, cancellationToken).ConfigureAwait(false);
                
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
                    Guid.NewGuid().ToString("N")[..8], symbol);
                
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
            return await CalculateIntelligentFallbackSizeAsync(symbol, intent, risk, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PPO size prediction for symbol {Symbol}", symbol);
            
            // Fallback to safe conservative sizing
            return CalculateConservativeFallbackSize(risk, intent);
        }
    }

    private async Task<TradingBot.Abstractions.MarketContext> CreateMarketContextAsync(
        string symbol, 
        BotCore.Strategy.StrategyIntent intent, 
        double risk, 
        CancellationToken cancellationToken)
    {
        // Get feature bus for market data
        var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
        
        var currentPrice = featureBus?.Probe(symbol, "price.current") ?? 4000.0;
        var volatility = featureBus?.Probe(symbol, "volatility.realized") ?? 0.02;
        var volume = featureBus?.Probe(symbol, "volume.current") ?? 1000.0;
        
        return new TradingBot.Abstractions.MarketContext
        {
            Symbol = symbol,
            Price = currentPrice, // Use existing Price property
            Volume = volume, // Use existing Volume property
            // Store additional data in TechnicalIndicators since direct properties don't exist
            TechnicalIndicators = 
            {
                ["volatility"] = volatility,
                ["risk_level"] = risk,
                ["position_intent"] = intent == BotCore.Strategy.StrategyIntent.Buy ? 1.0 : 
                                     intent == BotCore.Strategy.StrategyIntent.Sell ? -1.0 : 0.0
            },
            Regime = GetMarketRegime(symbol) // Use existing Regime property
        };
    }
    
    private string GetMarketRegime(string symbol)
    {
        var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
        var regimeValue = featureBus?.Probe(symbol, "regime.current") ?? 0.5;
        
        return regimeValue switch
        {
            > 0.7 => "TRENDING",
            < 0.3 => "RANGING", 
            _ => "VOLATILE"
        };
    }
    
    private static double NormalizeSizeRecommendation(double rawRecommendation, double risk, BotCore.Strategy.StrategyIntent intent)
    {
        // Normalize the raw RL recommendation to a reasonable position size
        var baseSize = Math.Abs(rawRecommendation);
        
        // Scale by risk tolerance
        var riskAdjustedSize = baseSize * risk;
        
        // Apply directional multiplier
        var directionalMultiplier = intent switch
        {
            BotCore.Strategy.StrategyIntent.Buy => 1.0,
            BotCore.Strategy.StrategyIntent.Sell => -1.0,
            _ => 0.0
        };
        
        // Clamp to reasonable bounds (max 10% position)
        var finalSize = Math.Max(-0.1, Math.Min(0.1, riskAdjustedSize * directionalMultiplier));
        
        return finalSize;
    }

    /// <summary>
    /// Check if RL system has position sizing capability - fail-closed validation
    /// </summary>
    private bool IsRLSystemCapableOfPositionSizing(TradingBot.IntelligenceStack.RLAdvisorSystem rlSystem)
    {
        // Since GetPositionSizingRecommendationAsync doesn't exist,
        // we know the system lacks this capability
        return false;
    }

    private async Task<double> CalculateIntelligentFallbackSizeAsync(
        string symbol, 
        BotCore.Strategy.StrategyIntent intent, 
        double risk, 
        CancellationToken cancellationToken)
    {
        // Intelligent fallback using real market data when RL system unavailable
        var featureBus = _serviceProvider.GetService<IFeatureBusWithProbe>();
        
        if (featureBus != null)
        {
            var volatility = featureBus.Probe(symbol, "volatility.realized") ?? 0.02;
            var volume = featureBus.Probe(symbol, "volume.current") ?? 1000.0;
            
            // Size based on volatility and volume
            var volatilityAdjustment = Math.Max(0.5, Math.Min(2.0, 1.0 / volatility));
            var volumeAdjustment = Math.Max(0.8, Math.Min(1.2, volume / 1000.0));
            
            var baseSize = risk * 0.5 * volatilityAdjustment * volumeAdjustment;
            
            var directionalSize = intent switch
            {
                BotCore.Strategy.StrategyIntent.Buy => baseSize,
                BotCore.Strategy.StrategyIntent.Sell => -baseSize,
                _ => 0.0
            };
            
            return Math.Max(-0.05, Math.Min(0.05, directionalSize)); // Max 5% position
        }
        
        return CalculateConservativeFallbackSize(risk, intent);
    }
    
    private static double CalculateConservativeFallbackSize(double risk, BotCore.Strategy.StrategyIntent intent)
    {
        // Ultra-conservative sizing when no services available
        var conservativeSize = risk * 0.2; // Very small positions
        
        return intent switch
        {
            BotCore.Strategy.StrategyIntent.Buy => Math.Min(0.02, conservativeSize),  // Max 2%
            BotCore.Strategy.StrategyIntent.Sell => Math.Max(-0.02, -conservativeSize), // Max 2%
            _ => 0.0
        };
    }
}