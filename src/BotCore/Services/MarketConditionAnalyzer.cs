using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BotCore.Models;
using TradingBot.Abstractions;

namespace BotCore.Services;

/// <summary>
/// 📊 MARKET CONDITION ANALYZER 📊
/// 
/// Analyzes real-time market conditions to help the autonomous engine make
/// intelligent trading decisions. This component identifies market regimes,
/// volatility levels, and optimal trading periods for profit maximization.
/// 
/// KEY FEATURES:
/// ✅ Real-time market regime detection (Trending, Ranging, Volatile, etc.)
/// ✅ Volatility analysis for position sizing adjustments
/// ✅ Market session analysis for optimal trading times
/// ✅ Volume analysis for liquidity assessment
/// ✅ Market momentum tracking for strategy selection
/// ✅ Economic event awareness for risk management
/// 
/// This helps the autonomous engine:
/// - Select the best strategy for current conditions
/// - Adjust position sizes based on volatility
/// - Identify high-probability trading periods
/// - Avoid trading during unfavorable conditions
/// </summary>
public class MarketConditionAnalyzer
{
    private readonly ILogger<MarketConditionAnalyzer> _logger;
    
    // Market data tracking
    private readonly Queue<MarketDataPoint> _recentData = new();
    private readonly Queue<VolumeDataPoint> _recentVolume = new();
    private readonly object _dataLock = new();
    
    // Analysis parameters
    private const int ShortTermPeriod = 20;
    private const int MediumTermPeriod = 50;
    private const int LongTermPeriod = 200;
    private const int VolatilityPeriod = 20;
    private const int MaxDataPoints = 500;
    
    // Trend strength thresholds (percentage of price movement)
    private const decimal TrendingThreshold = 0.02m;           // 2% move indicates trending
    private const decimal VolatileRangeThreshold = 0.015m;     // 1.5% range indicates volatile
    private const decimal LowVolatilityRangeThreshold = 0.005m; // 0.5% range indicates low volatility
    
    // Trend strength scaling factor
    private const decimal TrendStrengthScalingFactor = 10m;    // Scale trend strength to 0-1 range
    
    // Volume relative thresholds (vs average volume)
    private const decimal VeryHighVolumeThreshold = 2.0m;      // 2x average = very high liquidity
    private const decimal HighVolumeThreshold = 1.5m;          // 1.5x average = high liquidity
    private const decimal LowVolumeThreshold = 0.5m;           // 0.5x average = low liquidity
    private const decimal VeryLowVolumeThreshold = 0.3m;       // 0.3x average = very low liquidity
    
    // ES futures volatility thresholds (ATR in points)
    private const decimal VeryLowVolatilityAtr = 10m;          // Very quiet market
    private const decimal LowVolatilityAtr = 15m;              // Below normal volatility
    private const decimal NormalVolatilityAtr = 25m;           // Normal market conditions
    private const decimal HighVolatilityAtr = 35m;             // Elevated volatility
    private const decimal VeryHighVolatilityAtr = 50m;         // Extreme volatility
    
    // Market regime scoring (0-1 scale for strategy selection)
    private const decimal TrendingRegimeScore = 0.9m;          // Best for trend-following
    private const decimal RangingRegimeScore = 0.7m;           // Good for mean reversion
    private const decimal VolatileRegimeScore = 0.5m;          // Challenging conditions
    private const decimal LowVolatilityRegimeScore = 0.6m;     // Limited opportunities
    private const decimal UnknownRegimeScore = 0.3m;           // Avoid trading
    private const decimal DefaultRegimeScore = 0.5m;           // Fallback score
    
    // Volatility level scoring (0-1 scale for position sizing)
    private const decimal IdealVolatilityScore = 1.0m;         // Normal volatility is ideal
    private const decimal LowVolatilityScore = 0.8m;           // Good but limited
    private const decimal HighVolatilityScore = 0.7m;          // Good but higher risk
    private const decimal VeryLowVolatilityScore = 0.5m;       // Limited opportunities
    private const decimal VeryHighVolatilityScore = 0.4m;      // High risk
    private const decimal DefaultVolatilityScore = 0.7m;       // Fallback score
    
    // Liquidity level scoring (0-1 scale for execution confidence)
    private const decimal IdealLiquidityScore = 1.0m;          // High liquidity is ideal
    private const decimal VeryHighLiquidityScore = 0.9m;       // Very good (may indicate news)
    private const decimal NormalLiquidityScore = 0.8m;         // Good liquidity
    private const decimal LowLiquidityScore = 0.4m;            // Limited liquidity
    private const decimal VeryLowLiquidityScore = 0.2m;        // Poor liquidity
    private const decimal DefaultLiquidityScore = 0.6m;        // Fallback score
    
    // Trend direction scoring
    private const decimal SidewaysTrendScore = 0.5m;           // Neutral for sideways
    private const decimal DirectionalTrendScore = 0.8m;        // Good for directional trends
    private const decimal TrendScoreDivisor = 2m;              // Average direction and strength
    
    // Fallback timezone offset for Eastern Time
    private const int EasternTimeOffsetHours = -5;             // EST offset from UTC
    
    // Current market state
    private TradingMarketRegime _currentRegime = TradingMarketRegime.Unknown;
    private MarketVolatility _currentVolatility = MarketVolatility.Normal;
    private decimal _currentVolatilityValue;
    private DateTime _lastAnalysis = DateTime.MinValue;
    
    public MarketConditionAnalyzer(ILogger<MarketConditionAnalyzer> logger)
    {
        _logger = logger;
        
        _logger.LogInformation("📊 [MARKET-ANALYZER] Initialized - Real-time market condition analysis ready");
    }
    
    /// <summary>
    /// Update market data for analysis
    /// </summary>
    public async Task UpdateMarketDataAsync(decimal price, decimal volume, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_dataLock)
        {
            // Add new data point
            _recentData.Enqueue(new MarketDataPoint
            {
                Price = price,
                Timestamp = timestamp
            });
            
            _recentVolume.Enqueue(new VolumeDataPoint
            {
                Volume = volume,
                Timestamp = timestamp
            });
            
            // Keep only recent data
            while (_recentData.Count > MaxDataPoints)
            {
                _recentData.Dequeue();
            }
            
            while (_recentVolume.Count > MaxDataPoints)
            {
                _recentVolume.Dequeue();
            }
        }
        
        // Update analysis if enough data and time has passed
        if (_recentData.Count >= LongTermPeriod && 
            DateTime.UtcNow - _lastAnalysis > TimeSpan.FromMinutes(1))
        {
            await AnalyzeMarketConditionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// Determine current market regime
    /// </summary>
    public async Task<TradingMarketRegime> DetermineMarketRegimeAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_dataLock)
        {
            if (_recentData.Count < LongTermPeriod)
            {
                return TradingMarketRegime.Unknown;
            }
            
            var prices = _recentData.TakeLast(LongTermPeriod).Select(d => d.Price).ToArray();
            
            // Calculate moving averages
            var shortMA = CalculateMovingAverage(prices, ShortTermPeriod);
            var mediumMA = CalculateMovingAverage(prices, MediumTermPeriod);
            var longMA = CalculateMovingAverage(prices, LongTermPeriod);
            
            // Calculate trend strength
            var trendStrength = Math.Abs(shortMA - longMA) / longMA;
            
            // Calculate range vs trend characteristics
            var recentPrices = prices.TakeLast(ShortTermPeriod).ToArray();
            var priceRange = recentPrices.Max() - recentPrices.Min();
            var avgPrice = recentPrices.Average();
            var rangePercent = priceRange / avgPrice;
            
            // Determine regime based on multiple factors
            if (trendStrength > TrendingThreshold && IsUptrend(shortMA, mediumMA, longMA))
            {
                _currentRegime = TradingMarketRegime.Trending;
            }
            else if (trendStrength > TrendingThreshold && IsDowntrend(shortMA, mediumMA, longMA))
            {
                _currentRegime = TradingMarketRegime.Trending;
            }
            else if (rangePercent > VolatileRangeThreshold && _currentVolatilityValue > GetVolatilityThreshold(MarketVolatility.High))
            {
                _currentRegime = TradingMarketRegime.Volatile;
            }
            else if (rangePercent < LowVolatilityRangeThreshold && _currentVolatilityValue < GetVolatilityThreshold(MarketVolatility.Low))
            {
                _currentRegime = TradingMarketRegime.LowVolatility;
            }
            else
            {
                _currentRegime = TradingMarketRegime.Ranging;
            }
            
            return _currentRegime;
        }
    }
    
    /// <summary>
    /// Get current market volatility level
    /// </summary>
    public async Task<MarketVolatility> GetCurrentVolatilityAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_dataLock)
        {
            if (_recentData.Count < VolatilityPeriod)
            {
                return MarketVolatility.Normal;
            }
            
            // Calculate ATR-based volatility
            var atr = CalculateATR(VolatilityPeriod);
            _currentVolatilityValue = atr;
            
            // Classify volatility level
            if (atr > GetVolatilityThreshold(MarketVolatility.VeryHigh))
            {
                _currentVolatility = MarketVolatility.VeryHigh;
            }
            else if (atr > GetVolatilityThreshold(MarketVolatility.High))
            {
                _currentVolatility = MarketVolatility.High;
            }
            else if (atr < GetVolatilityThreshold(MarketVolatility.Low))
            {
                _currentVolatility = MarketVolatility.Low;
            }
            else if (atr < GetVolatilityThreshold(MarketVolatility.VeryLow))
            {
                _currentVolatility = MarketVolatility.VeryLow;
            }
            else
            {
                _currentVolatility = MarketVolatility.Normal;
            }
            
            return _currentVolatility;
        }
    }
    
    /// <summary>
    /// Get current trend direction and strength
    /// </summary>
    public async Task<TrendAnalysis> GetTrendAnalysisAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_dataLock)
        {
            if (_recentData.Count < LongTermPeriod)
            {
                return new TrendAnalysis { Direction = TrendDirection.Sideways, Strength = 0m };
            }
            
            var prices = _recentData.TakeLast(LongTermPeriod).Select(d => d.Price).ToArray();
            
            // Calculate multiple timeframe trends
            var shortMA = CalculateMovingAverage(prices, ShortTermPeriod);
            var mediumMA = CalculateMovingAverage(prices, MediumTermPeriod);
            var longMA = CalculateMovingAverage(prices, LongTermPeriod);
            
            // Determine trend direction
            TrendDirection direction;
            if (shortMA > mediumMA && mediumMA > longMA)
            {
                direction = TrendDirection.Up;
            }
            else if (shortMA < mediumMA && mediumMA < longMA)
            {
                direction = TrendDirection.Down;
            }
            else
            {
                direction = TrendDirection.Sideways;
            }
            
            // Calculate trend strength (0 to 1)
            var trendStrength = Math.Abs(shortMA - longMA) / longMA;
            
            return new TrendAnalysis
            {
                Direction = direction,
                Strength = Math.Min(1m, (decimal)trendStrength * TrendStrengthScalingFactor), // Scale to 0-1
                ShortTermMA = shortMA,
                MediumTermMA = mediumMA,
                LongTermMA = longMA
            };
        }
    }
    
    /// <summary>
    /// Analyze volume patterns for liquidity assessment
    /// </summary>
    public async Task<VolumeAnalysis> GetVolumeAnalysisAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_dataLock)
        {
            if (_recentVolume.Count < ShortTermPeriod)
            {
                return new VolumeAnalysis { AverageVolume = 0m, RelativeVolume = 1m, LiquidityLevel = LiquidityLevel.Normal };
            }
            
            var volumes = _recentVolume.TakeLast(ShortTermPeriod).Select(v => v.Volume).ToArray();
            var currentVolume = volumes.LastOrDefault();
            var avgVolume = volumes.Average();
            var relativeVolume = currentVolume / avgVolume;
            
            // Determine liquidity level
            LiquidityLevel liquidityLevel;
            if (relativeVolume > VeryHighVolumeThreshold)
            {
                liquidityLevel = LiquidityLevel.VeryHigh;
            }
            else if (relativeVolume > HighVolumeThreshold)
            {
                liquidityLevel = LiquidityLevel.High;
            }
            else if (relativeVolume < LowVolumeThreshold)
            {
                liquidityLevel = LiquidityLevel.Low;
            }
            else if (relativeVolume < VeryLowVolumeThreshold)
            {
                liquidityLevel = LiquidityLevel.VeryLow;
            }
            else
            {
                liquidityLevel = LiquidityLevel.Normal;
            }
            
            return new VolumeAnalysis
            {
                CurrentVolume = currentVolume,
                AverageVolume = avgVolume,
                RelativeVolume = relativeVolume,
                LiquidityLevel = liquidityLevel
            };
        }
    }
    
    /// <summary>
    /// Get trading opportunity score for current conditions
    /// </summary>
    public async Task<decimal> GetTradingOpportunityScoreAsync(CancellationToken cancellationToken = default)
    {
        var regime = await DetermineMarketRegimeAsync(cancellationToken).ConfigureAwait(false);
        var volatility = await GetCurrentVolatilityAsync(cancellationToken).ConfigureAwait(false);
        var trend = await GetTrendAnalysisAsync(cancellationToken).ConfigureAwait(false);
        var volume = await GetVolumeAnalysisAsync(cancellationToken).ConfigureAwait(false);
        
        // Multi-factor opportunity scoring
        var regimeScore = GetRegimeScore(regime);
        var volatilityScore = GetVolatilityScore(volatility);
        var trendScore = GetTrendScore(trend);
        var volumeScore = GetVolumeScore(volume);
        
        // Weighted combination
        var totalScore = 
            (regimeScore * 0.3m) +
            (volatilityScore * 0.25m) +
            (trendScore * 0.25m) +
            (volumeScore * 0.2m);
        
        _logger.LogDebug("🎯 [MARKET-ANALYZER] Opportunity score: {Score:F3} (Regime:{Regime:F2}, Vol:{Vol:F2}, Trend:{Trend:F2}, Volume:{Volume:F2})",
            totalScore, regimeScore, volatilityScore, trendScore, volumeScore);
        
        return Math.Max(0, Math.Min(1, totalScore));
    }
    
    /// <summary>
    /// Check if current time is optimal for trading based on market patterns
    /// </summary>
    public async Task<bool> IsOptimalTradingTimeAsync(CancellationToken cancellationToken = default)
    {
        var easternTime = GetEasternTime();
        var timeOfDay = easternTime.TimeOfDay;
        var dayOfWeek = easternTime.DayOfWeek;
        
        // Avoid weekends
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            return false;
        }
        
        // Optimal times based on historical patterns
        var isOptimalTime = 
            (timeOfDay >= new TimeSpan(9, 30, 0) && timeOfDay <= new TimeSpan(11, 0, 0)) ||  // Morning session
            (timeOfDay >= new TimeSpan(13, 0, 0) && timeOfDay <= new TimeSpan(16, 0, 0)) ||  // Afternoon session
            (timeOfDay >= new TimeSpan(16, 0, 0) && timeOfDay <= new TimeSpan(17, 0, 0));    // Closing hour
        
        // Check market conditions
        var opportunityScore = await GetTradingOpportunityScoreAsync(cancellationToken).ConfigureAwait(false);
        var hasGoodConditions = opportunityScore > 0.6m;
        
        return isOptimalTime && hasGoodConditions;
    }
    
    private async Task AnalyzeMarketConditionsAsync(CancellationToken cancellationToken)
    {
        _lastAnalysis = DateTime.UtcNow;
        
        // Update all analysis components
        var regime = await DetermineMarketRegimeAsync(cancellationToken).ConfigureAwait(false);
        var volatility = await GetCurrentVolatilityAsync(cancellationToken).ConfigureAwait(false);
        var trend = await GetTrendAnalysisAsync(cancellationToken).ConfigureAwait(false);
        
        _logger.LogDebug("📊 [MARKET-ANALYZER] Analysis update: Regime={Regime}, Volatility={Volatility}, Trend={Direction}({Strength:F3})",
            regime, volatility, trend.Direction, trend.Strength);
    }
    
    private static decimal CalculateMovingAverage(decimal[] prices, int period)
    {
        if (prices.Length < period) return 0m;
        
        return prices.TakeLast(period).Average();
    }
    
    private decimal CalculateATR(int period)
    {
        if (_recentData.Count < period + 1) return 0m;
        
        var data = _recentData.TakeLast(period + 1).ToArray();
        var trueRanges = new List<decimal>();
        
        for (int i = 1; i < data.Length; i++)
        {
            var high = data[i].Price;
            var low = data[i].Price; // Simplified - in real implementation would use OHLC data
            var previousClose = data[i - 1].Price;
            
            var tr1 = high - low;
            var tr2 = Math.Abs(high - previousClose);
            var tr3 = Math.Abs(low - previousClose);
            
            var trueRange = Math.Max(tr1, Math.Max(tr2, tr3));
            trueRanges.Add(trueRange);
        }
        
        return trueRanges.Average();
    }
    
    private static bool IsUptrend(decimal shortMA, decimal mediumMA, decimal longMA)
    {
        return shortMA > mediumMA && mediumMA > longMA;
    }
    
    private static bool IsDowntrend(decimal shortMA, decimal mediumMA, decimal longMA)
    {
        return shortMA < mediumMA && mediumMA < longMA;
    }
    
    private static decimal GetVolatilityThreshold(MarketVolatility level)
    {
        // ES futures typical ATR values (in points)
        return level switch
        {
            MarketVolatility.VeryLow => VeryLowVolatilityAtr,
            MarketVolatility.Low => LowVolatilityAtr,
            MarketVolatility.Normal => NormalVolatilityAtr,
            MarketVolatility.High => HighVolatilityAtr,
            MarketVolatility.VeryHigh => VeryHighVolatilityAtr,
            _ => NormalVolatilityAtr
        };
    }
    
    private static decimal GetRegimeScore(TradingMarketRegime regime)
    {
        return regime switch
        {
            TradingMarketRegime.Trending => TrendingRegimeScore,        // Best for trend-following strategies
            TradingMarketRegime.Ranging => RangingRegimeScore,         // Good for mean reversion
            TradingMarketRegime.Volatile => VolatileRegimeScore,        // Challenging but tradeable
            TradingMarketRegime.LowVolatility => LowVolatilityRegimeScore,   // Limited opportunities
            TradingMarketRegime.Unknown => UnknownRegimeScore,         // Avoid trading
            _ => DefaultRegimeScore
        };
    }
    
    private static decimal GetVolatilityScore(MarketVolatility volatility)
    {
        return volatility switch
        {
            MarketVolatility.Normal => IdealVolatilityScore,      // Ideal volatility
            MarketVolatility.Low => LowVolatilityScore,         // Good but limited moves
            MarketVolatility.High => HighVolatilityScore,        // Good but higher risk
            MarketVolatility.VeryLow => VeryLowVolatilityScore,     // Limited opportunities
            MarketVolatility.VeryHigh => VeryHighVolatilityScore,    // High risk
            _ => DefaultVolatilityScore
        };
    }
    
    private static decimal GetTrendScore(TrendAnalysis trend)
    {
        var directionScore = trend.Direction == TrendDirection.Sideways ? SidewaysTrendScore : DirectionalTrendScore;
        var strengthScore = trend.Strength;
        
        return (directionScore + strengthScore) / TrendScoreDivisor;
    }
    
    private static decimal GetVolumeScore(VolumeAnalysis volume)
    {
        return volume.LiquidityLevel switch
        {
            LiquidityLevel.High => IdealLiquidityScore,          // Ideal liquidity
            LiquidityLevel.VeryHigh => VeryHighLiquidityScore,      // Very good but may indicate news
            LiquidityLevel.Normal => NormalLiquidityScore,        // Good liquidity
            LiquidityLevel.Low => LowLiquidityScore,           // Limited liquidity
            LiquidityLevel.VeryLow => VeryLowLiquidityScore,       // Poor liquidity
            _ => DefaultLiquidityScore
        };
    }
    
    private static DateTime GetEasternTime()
    {
        try
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(EasternTimeOffsetHours); // Fallback to EST
        }
    }
}

/// <summary>
/// Market data point for analysis
/// </summary>
public class MarketDataPoint
{
    public decimal Price { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Volume data point for analysis
/// </summary>
public class VolumeDataPoint
{
    public decimal Volume { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Trend analysis result
/// </summary>
public class TrendAnalysis
{
    public TrendDirection Direction { get; set; }
    public decimal Strength { get; set; }
    public decimal ShortTermMA { get; set; }
    public decimal MediumTermMA { get; set; }
    public decimal LongTermMA { get; set; }
}

/// <summary>
/// Volume analysis result
/// </summary>
public class VolumeAnalysis
{
    public decimal CurrentVolume { get; set; }
    public decimal AverageVolume { get; set; }
    public decimal RelativeVolume { get; set; }
    public LiquidityLevel LiquidityLevel { get; set; }
}

/// <summary>
/// Trend direction enumeration
/// </summary>
public enum TrendDirection
{
    Up,
    Down,
    Sideways
}

/// <summary>
/// Liquidity level enumeration
/// </summary>
public enum LiquidityLevel
{
    VeryLow,
    Low,
    Normal,
    High,
    VeryHigh
}

/// <summary>
/// Market volatility levels (Market Condition Analyzer)
/// </summary>
public enum MarketVolatility
{
    VeryLow,
    Low,
    Normal,
    High,
    VeryHigh
}