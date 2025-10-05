using BotCore.Market;
using BotCore.Config;
using TradingBot.Abstractions;

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

    public FeatureBuilder(FeatureSpec spec, IS7Service? s7Service = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
        _s7Service = s7Service;
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
        catch
        {
            // On any exception, return features filled with default fill values
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
        if (bars.Count < 15) return _spec.Columns[2].FillValue;
        
        decimal sum = 0;
        for (int i = bars.Count - 14; i < bars.Count; i++)
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
        
        return sum / 14;
    }

    private decimal ComputeRsi14(List<Bar> bars)
    {
        if (bars.Count < 15) return _spec.Columns[3].FillValue;
        
        decimal gainSum = 0;
        decimal lossSum = 0;
        
        for (int i = bars.Count - 14; i < bars.Count; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            if (change > 0)
                gainSum += change;
            else
                lossSum += Math.Abs(change);
        }
        
        var avgGain = gainSum / 14;
        var avgLoss = lossSum / 14;
        
        if (avgLoss == 0) return 100;
        
        var rs = avgGain / avgLoss;
        var rsi = 100 - (100 / (1 + rs));
        
        return rsi;
    }

    private decimal ComputeVwapDist(List<Bar> bars)
    {
        if (bars.Count < 1) return _spec.Columns[4].FillValue;
        
        // Compute VWAP from available bars (simplified - use last 20 bars)
        var barsToUse = Math.Min(20, bars.Count);
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
        if (bars.Count < 20) return _spec.Columns[5].FillValue;
        
        // Compute 20-period Bollinger Bands
        decimal sum = 0;
        for (int i = bars.Count - 20; i < bars.Count; i++)
        {
            sum += bars[i].Close;
        }
        var middle = sum / 20;
        
        // Compute standard deviation
        decimal varianceSum = 0;
        for (int i = bars.Count - 20; i < bars.Count; i++)
        {
            var diff = bars[i].Close - middle;
            varianceSum += diff * diff;
        }
        var stdDev = (decimal)Math.Sqrt((double)(varianceSum / 20));
        
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
                
                // Simplified: assume we have bid/ask volumes
                // In reality, you'd sum up the sizes from the orderbook levels
                return 1.0m; // Placeholder - neutral imbalance
            }
        }
        catch
        {
            // Fall through to default
        }
        
        return _spec.Columns[6].FillValue;
    }

    private decimal ComputeAdrPct(List<Bar> bars)
    {
        // Compute ADR percentage: (current range) / (average daily range over 14 days)
        // This is used by S11 exhaustion strategy
        if (bars.Count < 15) return _spec.Columns[7].FillValue;
        
        try
        {
            // Calculate current intraday range
            var recentBars = bars.TakeLast(20).ToList();
            var currentHigh = recentBars.Max(b => b.High);
            var currentLow = recentBars.Min(b => b.Low);
            var currentRange = currentHigh - currentLow;
            
            // Calculate average daily range over last 14 days
            // Approximate by taking max-min over rolling windows
            decimal totalRange = 0;
            int validDays = 0;
            
            for (int i = Math.Max(0, bars.Count - 14 * 390); i < bars.Count - 390; i += 390) // ~390 minutes per trading day
            {
                var dayBars = bars.Skip(i).Take(390).ToList();
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
        catch
        {
            return _spec.Columns[7].FillValue;
        }
    }

    private decimal ComputeHourFraction(DateTime currentTime)
    {
        return currentTime.Hour / 24.0m;
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
            
            if (session == null) return _spec.Columns[9].FillValue;
            
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
        catch
        {
            return _spec.Columns[9].FillValue;
        }
    }

    private decimal ComputeS7Regime(string symbol)
    {
        // Get S7 regime signal: -1 (bearish), 0 (neutral), 1 (bullish)
        if (_s7Service == null) return _spec.Columns[11].FillValue;
        
        try
        {
            if (!_s7Service.IsReady()) return _spec.Columns[11].FillValue;
            
            var featureTuple = _s7Service.GetFeatureTuple(symbol);
            
            // Determine regime based on S7 signal strength and coherence
            // Use relative strength and signal active status
            if (featureTuple.IsSignalActive)
            {
                // Use the ZScore and coherence to determine regime
                if (featureTuple.ZScore > 1.0m && featureTuple.Coherence > 0.6m)
                    return 1; // Bullish
                else if (featureTuple.ZScore < -1.0m && featureTuple.Coherence > 0.6m)
                    return -1; // Bearish
            }
            
            return 0; // Neutral
        }
        catch
        {
            return _spec.Columns[11].FillValue;
        }
    }
}
