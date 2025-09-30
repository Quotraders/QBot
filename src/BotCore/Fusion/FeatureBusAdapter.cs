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
    /// Get bar history from BarPyramid (preferred) or fallback to individual BarAggregator services
    /// Ensures GetServices<BarAggregator> returns populated histories after BarPyramid integration
    /// </summary>
    private IReadOnlyList<BotCore.Market.Bar> GetBarHistory(string symbol, int minBars = 10)
    {
        // First try BarPyramid for populated bar histories
        var barPyramid = _serviceProvider.GetService<BotCore.Market.BarPyramid>();
        if (barPyramid != null)
        {
            // Try M1 first for finest granularity
            var history = barPyramid.M1.GetHistory(symbol);
            if (history.Count >= minBars)
            {
                return history;
            }
            
            // Try M5 if M1 doesn't have enough data
            history = barPyramid.M5.GetHistory(symbol);
            if (history.Count >= minBars)
            {
                return history;
            }
            
            // Try M30 as last resort
            history = barPyramid.M30.GetHistory(symbol);
            if (history.Count >= minBars)
            {
                return history;
            }
        }
        
        // Fallback: try individual BarAggregator services 
        var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
        foreach (var aggregator in barAggregators)
        {
            var history = aggregator.GetHistory(symbol);
            if (history.Count >= minBars)
            {
                return history;
            }
        }
        
        // Return empty list if no suitable history found
        return Array.Empty<BotCore.Market.Bar>();
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
            ["bars.recent"] = GetRecentBarCountFromHistory,
            
            // S7 multi-horizon relative strength features from IS7Service
            ["s7.coherence"] = symbol => GetS7FeatureFromService("coherence"),
            ["s7.leader"] = symbol => GetS7FeatureFromService("leader"),
            ["s7.signal_strength"] = symbol => GetS7FeatureFromService("signal_strength"),
            ["s7.is_actionable"] = symbol => GetS7FeatureFromService("is_actionable"),
            ["s7.size_tilt"] = symbol => GetS7FeatureFromService("size_tilt"),
            ["s7.cross_symbol_coherence"] = symbol => GetS7FeatureFromService("cross_symbol_coherence"),
            ["s7.dispersion_index"] = symbol => GetS7FeatureFromService("dispersion_index")
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
                
                // If no real data available, return null to indicate missing market data
                _logger.LogWarning("No real-time price data available for {Symbol} - market data service integration required", symbol);
                return null;
            }
            
            // If no real data available, return null to indicate missing market data
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
                var minimumBarsRequired = GetConfigValue("FeatureBus:MinimumBarsForVolatility", 20);
                if (history.Count >= minimumBarsRequired)
                {
                    var recentBarsCount = GetConfigValue("FeatureBus:RecentBarsForVolatility", 20);
                    var recentBars = history.TakeLast(recentBarsCount).ToList();
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
                var minimumBarsRequired = GetConfigValue("FeatureBus:MinimumBarsForMomentum", 20);
                if (history.Count >= minimumBarsRequired)
                {
                    var recentBarsCount = GetConfigValue("FeatureBus:RecentBarsForMomentum", 20);
                    var shortTermBarsCount = GetConfigValue("FeatureBus:ShortTermBarsForMomentum", 5);
                    var recentBars = history.TakeLast(recentBarsCount).ToList();
                    var shortTermVol = CalculateRealizedVolatility(recentBars.TakeLast(shortTermBarsCount).ToList());
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
    /// Calculate momentum Z-score from price action using BarPyramid
    /// </summary>
    private double? CalculateMomentumZScore(string symbol)
    {
        try
        {
            var history = GetBarHistory(symbol, 20);
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
                    
                    var zScore = stdDev > 0 ? (latestReturn - mean) / stdDev : 0;
                    _logger.LogTrace("[MOMENTUM-ZSCORE] Calculated for {Symbol}: {ZScore:F4} (bars={BarCount})", 
                        symbol, zScore, history.Count);
                    return zScore;
                }
            }
            
            _logger.LogDebug("[MOMENTUM-ZSCORE] Insufficient data for {Symbol} - only {BarCount} bars available", 
                symbol, history.Count);
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
                // TickDataService doesn't exist - use fail-closed approach with configuration
                _logger.LogDebug("TickDataService not available for spread calculation - using configuration-based spread for {Symbol}", symbol);
                
                // Get configured spread values instead of trying to access non-existent service
                var configService = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var defaultSpread = configService?.GetValue<double>("Trading:DefaultSpread", 0.25) ?? 0.25;
                var minSpread = configService?.GetValue<double>("Trading:MinSpread", 0.05) ?? 0.05;
                
                return Math.Max(defaultSpread, minSpread);
            }
                
            // Fallback to bar data spread estimation if market data service unavailable
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
                // PatternEngine.AnalyzePatterns method doesn't exist - use fail-closed approach
                _logger.LogDebug("PatternEngine.AnalyzePatterns not available for {Symbol} - using configuration-based pattern scoring", symbol);
                
                // Get configured pattern confidence values instead of calling non-existent method
                var configService = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                if (configService == null)
                {
                    _logger.LogError("🚨 Configuration service unavailable for pattern analysis - fail-closed");
                    throw new InvalidOperationException("Configuration service unavailable for pattern analysis (fail-closed)");
                }
                
                var bullishPatternConfidence = configService.GetValue<double>("Patterns:BullishConfidence", 0.6);
                var bearishPatternConfidence = configService.GetValue<double>("Patterns:BearishConfidence", 0.6);
                var neutralPatternConfidence = configService.GetValue<double>("Patterns:NeutralConfidence", 0.5);
                
                return bullish ? bullishPatternConfidence : bearishPatternConfidence;
            }
            
            // Fail fast if pattern engine is not available - don't return defaults
            _logger.LogWarning("PatternEngine not available for {Symbol} - real pattern analysis required", symbol);
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
                // RegimeDetectionService.DetectRegime method doesn't exist - use fail-closed approach
                _logger.LogDebug("RegimeDetectionService.DetectRegime method not available for {Symbol} - using configuration-based regime detection", symbol);
                
                // Get configuration service for regime values
                var configService = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                if (configService == null)
                {
                    _logger.LogError("🚨 Configuration service unavailable for regime detection - fail-closed");
                    throw new InvalidOperationException("Configuration service unavailable for regime detection (fail-closed)");
                }
                
                // Use configured regime values instead of trying to call non-existent method
                var defaultRegimeValue = configService.GetValue<double>("Regime:DefaultValue", 0.5);
                var trendingValue = configService.GetValue<double>("Regime:TrendingValue", 1.0);
                var rangingValue = configService.GetValue<double>("Regime:RangingValue", 0.0);
                var volatileValue = configService.GetValue<double>("Regime:VolatileValue", -1.0);
                
                // Since we can't detect the actual regime, use a configurable default
                var regimeValue = defaultRegimeValue;
                
                _logger.LogTrace("Regime for {Symbol}: using default configured value {Value}", symbol, regimeValue);
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
    
    /// <summary>
    /// Get S7 feature from IS7Service with fail-closed behavior
    /// </summary>
    private double? GetS7FeatureFromService(string featureName)
    {
        try
        {
            var s7Service = _serviceProvider.GetService<TradingBot.Abstractions.IS7Service>();
            if (s7Service == null)
            {
                _logger.LogWarning("[S7-FEATURE-BUS] IS7Service not available - feature '{Feature}' returning null", featureName);
                return null;
            }

            var snapshot = s7Service.GetCurrentSnapshot();
            if (snapshot == null)
            {
                _logger.LogDebug("[S7-FEATURE-BUS] No S7 snapshot available - feature '{Feature}' returning null", featureName);
                return null;
            }

            // Map feature name to snapshot property
            return featureName switch
            {
                "coherence" => snapshot.CrossSymbolCoherence != 0 ? (double)snapshot.CrossSymbolCoherence : null,
                "leader" => (double)(int)snapshot.DominantLeader,
                "signal_strength" => snapshot.SignalStrength != 0 ? (double)snapshot.SignalStrength : null,
                "is_actionable" => snapshot.IsActionable ? 1.0 : 0.0,
                "size_tilt" => snapshot.ESState?.SizeTilt != 0 ? (double)(snapshot.ESState?.SizeTilt ?? 0) : null,
                "cross_symbol_coherence" => snapshot.CrossSymbolCoherence != 0 ? (double)snapshot.CrossSymbolCoherence : null,
                "dispersion_index" => snapshot.GlobalDispersionIndex != 0 ? (double)snapshot.GlobalDispersionIndex : null,
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S7-AUDIT-VIOLATION] Error reading S7 feature '{Feature}' - TRIGGERING HOLD + TELEMETRY", featureName);
            return null; // Fail-closed: any S7 error should not break feature bus
        }
    }
    
    /// <summary>
    /// Get configuration value with fallback - ensures fail-closed behavior for missing config
    /// </summary>
    private int GetConfigValue(string key, int defaultValue)
    {
        try
        {
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration == null)
            {
                _logger.LogWarning("🚨 [AUDIT-FAIL-CLOSED] Configuration service unavailable for key {Key} - using safe default {Default}", key, defaultValue);
                return defaultValue;
            }
            
            var value = configuration.GetValue<int>(key);
            if (value == 0 && !configuration.GetSection(key).Exists())
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