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
/// Feature bus interface for accessing real-time market features
/// </summary>
public interface IFeatureBusWithProbe
{
    double? Probe(string symbol, string feature);
    void Publish(string symbol, DateTime utc, string name, double value);
}

/// <summary>
/// Production feature bus adapter that implements both interfaces with real data integration
/// </summary>
public sealed class FeatureBusAdapter : IFeatureBusWithProbe
{
    private readonly Zones.IFeatureBus _featureBus;
    private readonly ILogger<FeatureBusAdapter> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Func<string, double?>> _featureCalculators;

    public FeatureBusAdapter(Zones.IFeatureBus featureBus, ILogger<FeatureBusAdapter> logger, IServiceProvider serviceProvider)
    {
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        _featureCalculators = InitializeFeatureCalculators();
    }
    
    /// <summary>
    /// Initialize the feature calculator dictionary with all supported features
    /// </summary>
    private Dictionary<string, Func<string, double?>> InitializeFeatureCalculators()
    {
        return new Dictionary<string, Func<string, double?>>
        {
            // Real-time price features from live market data
            ["price.current"] = GetCurrentPriceFromMarketData,
            ["price.es"] = symbol => GetCurrentPriceFromMarketData("ES"),
            ["price.nq"] = symbol => GetCurrentPriceFromMarketData("NQ"),
            
            // Volume features from bar aggregator
            ["volume.current"] = GetCurrentVolumeFromBars,
            
            // Technical indicators calculated from real bars
            ["atr.14"] = symbol => CalculateATRFromBars(symbol, 14),
            ["atr.20"] = symbol => CalculateATRFromBars(symbol, 20),
            ["volatility.realized"] = CalculateRealizedVolatilityFromBars,
            
            // Market microstructure from enhanced market data service
            ["volatility.contraction"] = CalculateVolatilityContraction,
            ["momentum.zscore"] = CalculateMomentumZScore,
            ["pullback.risk"] = CalculatePullbackRisk,
            ["volume.thrust"] = CalculateVolumeThrust,
            ["inside_bars"] = CountInsideBars,
            ["vwap.distance_atr"] = CalculateVWAPDistance,
            ["keltner.touch"] = CalculateKeltnerTouch,
            ["bollinger.touch"] = CalculateBollingerTouch,
            
            // Market state features
            ["spread.current"] = CalculateBidAskSpread,
            ["liquidity.score"] = CalculateLiquidityScore,
            
            // Pattern scores from real pattern engine
            ["pattern.bull_score"] = symbol => GetPatternScoreFromEngine(symbol, true),
            ["pattern.bear_score"] = symbol => GetPatternScoreFromEngine(symbol, false),
            
            // Regime features from real regime detection
            ["regime.type"] = GetRegimeFromService,
            
            // Bar count from real bar history
            ["bars.recent"] = GetRecentBarCountFromHistory
        };
    }

    public double? Probe(string symbol, string feature)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(feature))
            {
                _logger.LogWarning("Invalid probe request: symbol='{Symbol}', feature='{Feature}'", symbol, feature);
                return null;
            }

            // First try the feature bus (only has Publish method, not Probe)
            // Note: The Zones.IFeatureBus only publishes values, doesn't query them
            // We'll rely on our calculated features instead
            
            // Try our calculated features
            if (_featureCalculators.TryGetValue(feature, out var calculator))
            {
                var calculatedValue = calculator(symbol);
                if (calculatedValue.HasValue)
                {
                    _logger.LogTrace("Feature '{Feature}' for {Symbol} calculated: {Value}", feature, symbol, calculatedValue);
                    return calculatedValue;
                }
            }

            _logger.LogTrace("Feature '{Feature}' not available for {Symbol}", feature, symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error probing feature '{Feature}' for {Symbol}", feature, symbol);
            return null;
        }
    }

    public void Publish(string symbol, DateTime utc, string name, double value)
    {
        try
        {
            _featureBus.Publish(symbol, utc, name, value);
            _logger.LogTrace("Published feature '{Name}' for {Symbol}: {Value}", name, symbol, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing feature '{Name}' for {Symbol}", name, symbol);
        }
    }

    /// <summary>
    /// Get current price from live market data service
    /// </summary>
    private double? GetCurrentPriceFromMarketData(string symbol)
    {
        try
        {
            var marketDataService = _serviceProvider.GetService<BotCore.Services.MarketTimeService>();
            if (marketDataService != null)
            {
                // Try to get real-time price from market data service
                var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
                foreach (var aggregator in barAggregators)
                {
                    var history = aggregator.GetHistory(symbol);
                    if (history.Count > 0)
                    {
                        var latestBar = history[^1];
                        return (double)latestBar.Close;
                    }
                }
                
                // If no real data available, fail fast rather than return stub values
                _logger.LogWarning("No real-time price data available for {Symbol} - market data service integration required", symbol);
                return null;
            }
            
            // If no real data available, fail fast rather than return stub values
            _logger.LogWarning("No real-time price data available for {Symbol} - market data service integration required", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current price for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Get current volume from bar aggregators
    /// </summary>
    private double? GetCurrentVolumeFromBars(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count > 0)
                {
                    var latestBar = history[^1];
                    return latestBar.Volume;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current volume for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate ATR from real bar data
    /// </summary>
    private double? CalculateATRFromBars(string symbol, int period)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= period)
                {
                    var recentBars = history.TakeLast(period).ToList();
                    return CalculateATR(recentBars);
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating ATR({Period}) for {Symbol}", period, symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate Average True Range from bar data
    /// </summary>
    private static double CalculateATR(IReadOnlyList<BotCore.Market.Bar> bars)
    {
        if (bars.Count < 2) return 0;

        var trueRanges = new List<double>();
        for (int i = 1; i < bars.Count; i++)
        {
            var high = (double)bars[i].High;
            var low = (double)bars[i].Low;
            var prevClose = (double)bars[i - 1].Close;

            var tr1 = high - low;
            var tr2 = Math.Abs(high - prevClose);
            var tr3 = Math.Abs(low - prevClose);

            trueRanges.Add(Math.Max(tr1, Math.Max(tr2, tr3)));
        }

        return trueRanges.Average();
    }

    /// <summary>
    /// Calculate realized volatility from real bar data
    /// </summary>
    private double? CalculateRealizedVolatilityFromBars(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var recentBars = history.TakeLast(20).ToList();
                    return CalculateRealizedVolatility(recentBars);
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating realized volatility for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate realized volatility from bars using returns
    /// </summary>
    private static double CalculateRealizedVolatility(IReadOnlyList<BotCore.Market.Bar> bars)
    {
        if (bars.Count < 2) return 0;

        var returns = new List<double>();
        for (int i = 1; i < bars.Count; i++)
        {
            var currentPrice = (double)bars[i].Close;
            var previousPrice = (double)bars[i - 1].Close;
            
            if (previousPrice > 0)
            {
                returns.Add(Math.Log(currentPrice / previousPrice));
            }
        }

        if (returns.Count == 0) return 0;

        var mean = returns.Average();
        var variance = returns.Select(r => Math.Pow(r - mean, 2)).Average();
        
        return Math.Sqrt(variance * 252); // Annualized volatility
    }

    /// <summary>
    /// Calculate volatility contraction from market data
    /// </summary>
    private double? CalculateVolatilityContraction(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var recentBars = history.TakeLast(20).ToList();
                    var shortTermVol = CalculateRealizedVolatility(recentBars.TakeLast(5).ToList());
                    var longTermVol = CalculateRealizedVolatility(recentBars);
                    
                    if (longTermVol > 0)
                    {
                        return shortTermVol / longTermVol; // Contraction ratio
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating volatility contraction for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate momentum Z-score from price action
    /// </summary>
    private double? CalculateMomentumZScore(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var prices = history.TakeLast(20).Select(b => (double)b.Close).ToList();
                    var returns = new List<double>();
                    
                    for (int i = 1; i < prices.Count; i++)
                    {
                        returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
                    }
                    
                    if (returns.Count > 0)
                    {
                        var mean = returns.Average();
                        var stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - mean, 2)).Average());
                        var latestReturn = returns[^1];
                        
                        return stdDev > 0 ? (latestReturn - mean) / stdDev : 0;
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating momentum Z-score for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate pullback risk from recent price action and volatility analysis
    /// </summary>
    private double? CalculatePullbackRisk(string symbol)
    {
        try
        {
            // Get real volatility using our own calculator since IFeatureBus doesn't have Probe method
            var volatility = CalculateRealizedVolatilityFromBars(symbol) ?? 0.0;
            
            // Get volatility thresholds from configuration instead of hard-coded values
            var configService = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var highVolatilityThreshold = configService?.GetValue<double>("Risk:HighVolatilityThreshold", 0.3) ?? 0.3;
            var lowVolatilityThreshold = configService?.GetValue<double>("Risk:LowVolatilityThreshold", 0.05) ?? 0.05;
            var highRiskLevel = configService?.GetValue<double>("Risk:HighVolatilityRiskLevel", 0.75) ?? 0.75;
            var lowRiskLevel = configService?.GetValue<double>("Risk:LowVolatilityRiskLevel", 0.15) ?? 0.15;
            var moderateRiskLevel = configService?.GetValue<double>("Risk:ModerateVolatilityRiskLevel", 0.4) ?? 0.4;
            
            if (volatility > highVolatilityThreshold)
            {
                return highRiskLevel; // High risk in high volatility environment
            }
            else if (volatility < lowVolatilityThreshold)
            {
                return lowRiskLevel; // Low risk in low volatility environment
            }
            
            return moderateRiskLevel; // Moderate risk for normal volatility
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating pullback risk for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate volume thrust indicator
    /// </summary>
    private double? CalculateVolumeThrust(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 10)
                {
                    var recentBars = history.TakeLast(10).ToList();
                    var avgVolume = recentBars.Average(b => b.Volume);
                    var latestVolume = recentBars[^1].Volume;
                    
                    return avgVolume > 0 ? latestVolume / avgVolume : 1.0;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating volume thrust for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Count inside bars pattern
    /// </summary>
    private double? CountInsideBars(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 5)
                {
                    var recentBars = history.TakeLast(5).ToList();
                    int insideBarCount = 0;
                    
                    for (int i = 1; i < recentBars.Count; i++)
                    {
                        var current = recentBars[i];
                        var previous = recentBars[i - 1];
                        
                        if (current.High <= previous.High && current.Low >= previous.Low)
                        {
                            insideBarCount++;
                        }
                    }
                    
                    return insideBarCount;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting inside bars for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate VWAP distance in ATR units
    /// </summary>
    private double? CalculateVWAPDistance(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var recentBars = history.TakeLast(20).ToList();
                    
                    // Calculate VWAP
                    decimal totalPriceVolume = 0;
                    decimal totalVolume = 0;
                    
                    foreach (var bar in recentBars)
                    {
                        var typicalPrice = (bar.High + bar.Low + bar.Close) / 3;
                        totalPriceVolume += typicalPrice * bar.Volume;
                        totalVolume += bar.Volume;
                    }
                    
                    if (totalVolume > 0)
                    {
                        var vwap = totalPriceVolume / totalVolume;
                        var currentPrice = recentBars[^1].Close;
                        var atr = CalculateATR(recentBars);
                        
                        if (atr > 0)
                        {
                            return Math.Abs((double)(currentPrice - vwap)) / atr;
                        }
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating VWAP distance for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate Keltner Channel touch
    /// </summary>
    private double? CalculateKeltnerTouch(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var recentBars = history.TakeLast(20).ToList();
                    var ema = recentBars.Select(b => (double)b.Close).Average(); // Simplified EMA
                    var atr = CalculateATR(recentBars);
                    var currentPrice = (double)recentBars[^1].Close;
                    
                    var upperBand = ema + (2.0 * atr);
                    var lowerBand = ema - (2.0 * atr);
                    
                    if (currentPrice >= upperBand) return 1.0; // Upper touch
                    if (currentPrice <= lowerBand) return -1.0; // Lower touch
                    return 0.0; // No touch
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Keltner touch for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Calculate Bollinger Band touch
    /// </summary>
    private double? CalculateBollingerTouch(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 20)
                {
                    var recentBars = history.TakeLast(20).ToList();
                    var prices = recentBars.Select(b => (double)b.Close).ToList();
                    var sma = prices.Average();
                    var stdDev = Math.Sqrt(prices.Select(p => Math.Pow(p - sma, 2)).Average());
                    var currentPrice = prices[^1];
                    
                    var upperBand = sma + (2.0 * stdDev);
                    var lowerBand = sma - (2.0 * stdDev);
                    
                    if (currentPrice >= upperBand) return 1.0; // Upper touch
                    if (currentPrice <= lowerBand) return -1.0; // Lower touch
                    return 0.0; // No touch
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating Bollinger touch for {Symbol}", symbol);
            return null;
        }
    }
    
    /// <summary>
    /// Calculate current bid-ask spread for the symbol
    /// </summary>
    private double? CalculateBidAskSpread(string symbol)
    {
        try
        {
            // Get market data service to calculate spread from real bid/ask data
            var marketDataService = _serviceProvider.GetService<BotCore.Services.MarketTimeService>();
            if (marketDataService != null)
            {
                // Try to get real bid/ask data from tick data or level 1 quotes
                var tickDataService = _serviceProvider.GetService<BotCore.Services.TickDataService>();
                if (tickDataService != null)
                {
                    var latestTick = tickDataService.GetLatestTick(symbol);
                    if (latestTick != null && latestTick.Bid > 0 && latestTick.Ask > 0)
                    {
                        var spread = latestTick.Ask - latestTick.Bid;
                        _logger.LogTrace("Real spread for {Symbol}: {Spread}", symbol, spread);
                        return spread;
                    }
                }
                
                // Fallback to bar data spread estimation if tick data unavailable
                var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
                foreach (var aggregator in barAggregators)
                {
                    var history = aggregator.GetHistory(symbol);
                    if (history.Count >= 2)
                    {
                        var recentBars = history.TakeLast(2).ToList();
                        var avgVolume = recentBars.Average(b => b.Volume);
                        
                        // Estimate spread based on recent volatility and volume
                        var priceRange = (double)(recentBars.Max(b => b.High) - recentBars.Min(b => b.Low));
                        var estimatedSpread = priceRange * (1000.0 / Math.Max(avgVolume, 100.0)) * 0.1;
                        
                        _logger.LogTrace("Estimated spread for {Symbol}: {Spread} (based on bars)", symbol, estimatedSpread);
                        return estimatedSpread;
                    }
                }
            }
            
            // Fail fast if no real market data is available
            _logger.LogWarning("No real market data available to calculate spread for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating bid-ask spread for {Symbol}", symbol);
            return null;
        }
    }
    
    /// <summary>
    /// Calculate liquidity score for the symbol
    /// </summary>
    private double? CalculateLiquidityScore(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count >= 10)
                {
                    var recentBars = history.TakeLast(10).ToList();
                    var avgVolume = recentBars.Average(b => b.Volume);
                    
                    // Calculate liquidity score based on volume and price stability
                    var priceRange = recentBars.Max(b => b.High) - recentBars.Min(b => b.Low);
                    var avgPrice = recentBars.Average(b => (b.High + b.Low) / 2);
                    
                    if (avgPrice > 0)
                    {
                        var volatilityRatio = (double)(priceRange / avgPrice);
                        var volumeScore = Math.Min(avgVolume / 1000.0, 10.0); // Normalize volume
                        var stabilityScore = Math.Max(0.1, 1.0 - volatilityRatio); // Stability bonus
                        
                        return Math.Min(10.0, volumeScore * stabilityScore);
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating liquidity score for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Get pattern score from real pattern engine
    /// </summary>
    private double? GetPatternScoreFromEngine(string symbol, bool bullish)
    {
        try
        {
            var patternEngine = _serviceProvider.GetService<BotCore.Patterns.PatternEngine>();
            if (patternEngine != null)
            {
                // Get actual pattern analysis from the pattern engine
                var patterns = patternEngine.AnalyzePatterns(symbol);
                if (patterns.Any())
                {
                    // Calculate aggregated score for bullish/bearish patterns
                    var relevantPatterns = bullish 
                        ? patterns.Where(p => p.Intent == BotCore.Strategy.StrategyIntent.Buy)
                        : patterns.Where(p => p.Intent == BotCore.Strategy.StrategyIntent.Sell);
                    
                    if (relevantPatterns.Any())
                    {
                        var avgScore = relevantPatterns.Average(p => p.Confidence);
                        _logger.LogTrace("Pattern score for {Symbol} (bullish: {Bullish}): {Score:F3}", symbol, bullish, avgScore);
                        return avgScore;
                    }
                }
            }
            
            // Fail fast if pattern engine is not available - don't return defaults
            _logger.LogWarning("PatternEngine not available or no patterns found for {Symbol} - real pattern analysis required", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pattern score for {Symbol} (bullish: {Bullish})", symbol, bullish);
            return null;
        }
    }

    /// <summary>
    /// Get regime type from real regime detection service
    /// </summary>
    private double? GetRegimeFromService(string symbol)
    {
        try
        {
            var regimeService = _serviceProvider.GetService<BotCore.Services.RegimeDetectionService>();
            if (regimeService != null)
            {
                // Get actual regime detection from service
                var regime = regimeService.DetectRegime(symbol);
                
                // Get regime mappings from configuration instead of hard-coded values
                var configService = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var trendingValue = configService?.GetValue<double>("Regime:TrendingValue", 1.0) ?? 1.0;
                var rangingValue = configService?.GetValue<double>("Regime:RangingValue", 0.0) ?? 0.0;
                var volatileValue = configService?.GetValue<double>("Regime:VolatileValue", -1.0) ?? -1.0;
                var neutralValue = configService?.GetValue<double>("Regime:NeutralValue", 0.5) ?? 0.5;
                
                var regimeValue = regime switch
                {
                    BotCore.Services.MarketRegime.Trending => trendingValue,
                    BotCore.Services.MarketRegime.Ranging => rangingValue,
                    BotCore.Services.MarketRegime.Volatile => volatileValue,
                    _ => neutralValue // Neutral/Unknown
                };
                
                _logger.LogTrace("Regime for {Symbol}: {Regime} (value: {Value})", symbol, regime, regimeValue);
                return regimeValue;
            }
            
            // Fail fast if regime service is not available
            _logger.LogWarning("RegimeDetectionService not available for {Symbol} - real regime analysis required", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting regime for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Get recent bar count from history
    /// </summary>
    private double? GetRecentBarCountFromHistory(string symbol)
    {
        try
        {
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                return history.Count;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bar count for {Symbol}", symbol);
            return null;
        }
    }
}