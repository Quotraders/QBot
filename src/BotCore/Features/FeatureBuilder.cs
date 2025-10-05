using BotCore.Market;
using BotCore.Config;
using TradingBot.Abstractions;
using Microsoft.Extensions.Logging;

namespace BotCore.Features;

/// <summary>
/// Builds standardized feature vectors from market data for ML model inference.
/// Features are computed according to the FeatureSpec and include returns, technical indicators,
/// orderbook data, and session information.
/// </summary>
public class FeatureBuilder
{
    private readonly FeatureSpec _spec;
    private readonly IS7Service? _s7Service;
    private readonly FeatureComputationConfig _config;
    private readonly ILogger<FeatureBuilder> _logger;

    public FeatureBuilder(
        FeatureSpec spec, 
        FeatureComputationConfig config,
        ILogger<FeatureBuilder> logger,
        IS7Service? s7Service = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        
        _spec = spec;
        _config = config;
        _logger = logger;
        _s7Service = s7Service;
        
        // Validate configuration on construction
        _config.Validate();
    }

    /// <summary>
    /// Build feature vector from market data.
    /// Returns a standardized array of features matching the spec.
    /// </summary>
    /// <param name="symbol">Trading symbol (e.g., "ES", "NQ")</param>
    /// <param name="bars">Historical bars (must have at least 20 bars for indicators)</param>
    /// <param name="currentPos">Current position (-1 short, 0 flat, 1 long)</param>
    /// <param name="currentTime">Current time for session detection</param>
    /// <param name="env">Optional environment data for ATR</param>
    /// <param name="levels">Optional order book levels for imbalance</param>
    /// <returns>Standardized feature array of length 12</returns>
    public decimal[] BuildFeatures(
        string symbol,
        List<Bar> bars,
        int currentPos,
        DateTime currentTime,
        object? env = null,
        object? levels = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        
        var features = new decimal[_spec.Columns.Count];
        
        try
        {
            // Compute raw features (12 features optimized for S2/S3/S6/S11)
            features[0] = ComputeReturn1m(bars);
            features[1] = ComputeReturn5m(bars);
            features[2] = ComputeAtr14(bars, env);
            features[3] = ComputeRsi14(bars);
            features[4] = ComputeVwapDist(bars);
            features[5] = ComputeBollingerWidth(bars);
            features[6] = ComputeOrderbookImbalance(levels);
            features[7] = ComputeAdrPct(bars);
            features[8] = ComputeHourFraction(currentTime);
            features[9] = ComputeSessionFlag(currentTime);
            features[10] = currentPos;
            features[11] = ComputeS7Regime(symbol);
            
            // Apply standardization: (x - mean) / std
            for (int i = 0; i < features.Length; i++)
            {
                if (_spec.Scaler.Std[i] != 0)
                {
                    features[i] = (features[i] - _spec.Scaler.Mean[i]) / _spec.Scaler.Std[i];
                }
            }
            
            return features;
        }
        catch (Exception ex)
        {
            // Fail-closed: Log error with telemetry and return fill values
            _logger.LogError(ex, "[FEATURE-BUILDER] Feature computation failed for symbol {Symbol}. Returning fill values. Exception: {ExceptionType}", 
                symbol, ex.GetType().Name);
            
            // Fill with default values as fallback
            for (int i = 0; i < features.Length; i++)
            {
                features[i] = _spec.Columns[i].FillValue;
            }
            return features;
        }
    }

    private decimal ComputeReturn1m(List<Bar> bars)
    {
        if (bars.Count < 2) return _spec.Columns[0].FillValue;
        
        var lastClose = bars[^1].Close;
        var prevClose = bars[^2].Close;
        
        if (prevClose == 0) return _spec.Columns[0].FillValue;
        
        return (lastClose - prevClose) / prevClose;
    }

    private decimal ComputeReturn5m(List<Bar> bars)
    {
        if (bars.Count < 6) return _spec.Columns[1].FillValue;
        
        var lastClose = bars[^1].Close;
        var fiveAgoClose = bars[^6].Close;
        
        if (fiveAgoClose == 0) return _spec.Columns[1].FillValue;
        
        return (lastClose - fiveAgoClose) / fiveAgoClose;
    }

    private decimal ComputeAtr14(List<Bar> bars, object? env)
    {
        // Try to get ATR from environment if available
        if (env != null)
        {
            var envType = env.GetType();
            var atrProp = envType.GetProperty("Atr") ?? envType.GetProperty("ATR");
            if (atrProp != null)
            {
                var atrValue = atrProp.GetValue(env);
                if (atrValue is decimal atrDecimal)
                {
                    return atrDecimal;
                }
            }
        }
        
        // Compute ATR manually if not available
        if (bars.Count < _config.AtrPeriod + 1) return _spec.Columns[2].FillValue;
        
        decimal sum = 0;
        for (int i = bars.Count - _config.AtrPeriod; i < bars.Count; i++)
        {
            var trueRange = bars[i].High - bars[i].Low;
            if (i > 0)
            {
                var highLowDiff = Math.Abs(bars[i].High - bars[i - 1].Close);
                var lowCloseDiff = Math.Abs(bars[i].Low - bars[i - 1].Close);
                trueRange = Math.Max(trueRange, Math.Max(highLowDiff, lowCloseDiff));
            }
            sum += trueRange;
        }
        
        return sum / _config.AtrPeriod;
    }

    private decimal ComputeRsi14(List<Bar> bars)
    {
        if (bars.Count < _config.RsiPeriod + 1) return _spec.Columns[3].FillValue;
        
        decimal gainSum = 0;
        decimal lossSum = 0;
        
        for (int i = bars.Count - _config.RsiPeriod; i < bars.Count; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            if (change > 0)
                gainSum += change;
            else
                lossSum += Math.Abs(change);
        }
        
        var avgGain = gainSum / _config.RsiPeriod;
        var avgLoss = lossSum / _config.RsiPeriod;
        
        if (avgLoss == 0) return 100;
        
        var rs = avgGain / avgLoss;
        var rsi = 100 - (100 / (1 + rs));
        
        return rsi;
    }

    private decimal ComputeVwapDist(List<Bar> bars)
    {
        if (bars.Count < 1) return _spec.Columns[4].FillValue;
        
        // Compute VWAP from available bars
        var barsToUse = Math.Min(_config.VwapBars, bars.Count);
        decimal volumeSum = 0;
        decimal volumePriceSum = 0;
        
        for (int i = bars.Count - barsToUse; i < bars.Count; i++)
        {
            var typicalPrice = (bars[i].High + bars[i].Low + bars[i].Close) / 3;
            var volume = bars[i].Volume;
            volumePriceSum += typicalPrice * volume;
            volumeSum += volume;
        }
        
        if (volumeSum == 0) return _spec.Columns[4].FillValue;
        
        var vwap = volumePriceSum / volumeSum;
        var lastClose = bars[^1].Close;
        
        if (vwap == 0) return _spec.Columns[4].FillValue;
        
        return (lastClose - vwap) / vwap;
    }

    private decimal ComputeBollingerWidth(List<Bar> bars)
    {
        if (bars.Count < _config.BollingerPeriod) return _spec.Columns[5].FillValue;
        
        // Compute Bollinger Bands
        decimal sum = 0;
        for (int i = bars.Count - _config.BollingerPeriod; i < bars.Count; i++)
        {
            sum += bars[i].Close;
        }
        var middle = sum / _config.BollingerPeriod;
        
        // Compute standard deviation
        decimal varianceSum = 0;
        for (int i = bars.Count - _config.BollingerPeriod; i < bars.Count; i++)
        {
            var diff = bars[i].Close - middle;
            varianceSum += diff * diff;
        }
        var stdDev = (decimal)Math.Sqrt((double)(varianceSum / _config.BollingerPeriod));
        
        var upper = middle + (2 * stdDev);
        var lower = middle - (2 * stdDev);
        
        if (middle == 0) return _spec.Columns[5].FillValue;
        
        return (upper - lower) / middle;
    }

    private decimal ComputeOrderbookImbalance(object? levels)
    {
        if (levels == null) return _spec.Columns[6].FillValue;
        
        // Try to extract bid/ask sizes from orderbook levels
        try
        {
            var levelsType = levels.GetType();
            var bidsProp = levelsType.GetProperty("Bids");
            var asksProp = levelsType.GetProperty("Asks");
            
            if (bidsProp != null && asksProp != null)
            {
                var bids = bidsProp.GetValue(levels);
                var asks = asksProp.GetValue(levels);
                
                // If orderbook data structure is not as expected, use fill value
                if (bids == null || asks == null)
                {
                    _logger.LogWarning("[FEATURE-BUILDER] Orderbook levels data structure unexpected. Using fill value.");
                    return _spec.Columns[6].FillValue;
                }
                
                // Calculate imbalance if both sides exist
                // This would need proper implementation based on actual orderbook structure
                // For now, return neutral (1.0) as orderbook processing requires full implementation
                return 1.0m;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Failed to compute orderbook imbalance: {ExceptionType}. Using fill value.", ex.GetType().Name);
        }
        
        return _spec.Columns[6].FillValue;
    }

    private decimal ComputeAdrPct(List<Bar> bars)
    {
        // Compute ADR percentage: (current range) / (average daily range over configured days)
        // This is used by S11 exhaustion strategy
        if (bars.Count < _config.AdrDays + 1) return _spec.Columns[7].FillValue;
        
        try
        {
            // Calculate current intraday range
            var recentBars = bars.TakeLast(_config.CurrentRangeBars).ToList();
            var currentHigh = recentBars.Max(b => b.High);
            var currentLow = recentBars.Min(b => b.Low);
            var currentRange = currentHigh - currentLow;
            
            // Calculate average daily range over configured period
            // Approximate by taking max-min over rolling windows
            decimal totalRange = 0;
            int validDays = 0;
            
            for (int i = Math.Max(0, bars.Count - _config.AdrDays * _config.MinutesPerDay); 
                 i < bars.Count - _config.MinutesPerDay; 
                 i += _config.MinutesPerDay)
            {
                var dayBars = bars.Skip(i).Take(_config.MinutesPerDay).ToList();
                if (dayBars.Count > 0)
                {
                    var dayHigh = dayBars.Max(b => b.High);
                    var dayLow = dayBars.Min(b => b.Low);
                    totalRange += (dayHigh - dayLow);
                    validDays++;
                }
            }
            
            if (validDays == 0) return _spec.Columns[7].FillValue;
            
            var avgDailyRange = totalRange / validDays;
            if (avgDailyRange == 0) return _spec.Columns[7].FillValue;
            
            return currentRange / avgDailyRange;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Failed to compute ADR percentage: {ExceptionType}. Using fill value.", ex.GetType().Name);
            return _spec.Columns[7].FillValue;
        }
    }

    private decimal ComputeHourFraction(DateTime currentTime)
    {
        return currentTime.Hour / (decimal)_config.HoursPerDay;
    }

    private decimal ComputeSessionFlag(DateTime currentTime)
    {
        // Map to EsNqTradingSchedule sessions (0-10)
        // AsianSession=0, EuropeanPreOpen=1, EuropeanOpen=2, LondonMorning=3, USPreMarket=4,
        // OpeningDrive=5, MorningTrend=6, LunchChop=7, AfternoonTrend=8, PowerHour=9, MarketClose=10
        
        try
        {
            var timeSpan = currentTime.TimeOfDay;
            var session = EsNqTradingSchedule.GetCurrentSession(timeSpan);
            
            if (session == null)
            {
                _logger.LogWarning("[FEATURE-BUILDER] No trading session found for time {Time}. Using fill value.", currentTime);
                return _spec.Columns[9].FillValue;
            }
            
            // Map session names to integers
            var sessionName = string.Empty;
            foreach (var kvp in EsNqTradingSchedule.Sessions)
            {
                if (kvp.Value == session)
                {
                    sessionName = kvp.Key;
                    break;
                }
            }
            
            return sessionName switch
            {
                "AsianSession" => 0,
                "EuropeanPreOpen" => 1,
                "EuropeanOpen" => 2,
                "LondonMorning" => 3,
                "USPreMarket" => 4,
                "OpeningDrive" => 5,
                "MorningTrend" => 6,
                "LunchChop" => 7,
                "AfternoonTrend" => 8,
                "PowerHour" => 9,
                "MarketClose" => 10,
                _ => _spec.Columns[9].FillValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Failed to compute session flag: {ExceptionType}. Using fill value.", ex.GetType().Name);
            return _spec.Columns[9].FillValue;
        }
    }

    private decimal ComputeS7Regime(string symbol)
    {
        // Get S7 regime signal: -1 (bearish), 0 (neutral), 1 (bullish)
        if (_s7Service == null) return _spec.Columns[11].FillValue;
        
        try
        {
            if (!_s7Service.IsReady())
            {
                _logger.LogDebug("[FEATURE-BUILDER] S7Service not ready for symbol {Symbol}. Using fill value.", symbol);
                return _spec.Columns[11].FillValue;
            }
            
            var featureTuple = _s7Service.GetFeatureTuple(symbol);
            
            // Determine regime based on S7 signal strength and coherence using configured thresholds
            if (featureTuple.IsSignalActive)
            {
                // Use the ZScore and coherence to determine regime
                if (featureTuple.ZScore > _config.S7ZScoreThresholdBullish && 
                    featureTuple.Coherence > _config.S7CoherenceThreshold)
                    return 1; // Bullish
                else if (featureTuple.ZScore < _config.S7ZScoreThresholdBearish && 
                         featureTuple.Coherence > _config.S7CoherenceThreshold)
                    return -1; // Bearish
            }
            
            return 0; // Neutral
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Failed to compute S7 regime for symbol {Symbol}: {ExceptionType}. Using fill value.", 
                symbol, ex.GetType().Name);
            return _spec.Columns[11].FillValue;
        }
    }
}
