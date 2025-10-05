using BotCore.Market;

namespace BotCore.Features;

/// <summary>
/// Builds standardized feature vectors from market data for ML model inference.
/// Features are computed according to the FeatureSpec and include returns, technical indicators,
/// orderbook data, and session information.
/// </summary>
public class FeatureBuilder
{
    private readonly FeatureSpec _spec;

    public FeatureBuilder(FeatureSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
    }

    /// <summary>
    /// Build feature vector from market data.
    /// Returns a standardized array of features matching the spec.
    /// </summary>
    /// <param name="symbol">Trading symbol (e.g., "ES", "NQ")</param>
    /// <param name="bars">Historical bars (must have at least 20 bars for indicators)</param>
    /// <param name="currentPos">Current position (-1 short, 0 flat, 1 long)</param>
    /// <param name="env">Optional environment data for ATR</param>
    /// <param name="levels">Optional order book levels for imbalance</param>
    /// <returns>Standardized feature array of length 10</returns>
    public decimal[] BuildFeatures(
        string symbol,
        List<Bar> bars,
        int currentPos,
        object? env = null,
        object? levels = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        
        var features = new decimal[_spec.Columns.Count];
        
        try
        {
            // Compute raw features
            features[0] = ComputeReturn1m(bars);
            features[1] = ComputeReturn5m(bars);
            features[2] = ComputeAtr14(bars, env);
            features[3] = ComputeRsi14(bars);
            features[4] = ComputeVwapDist(bars);
            features[5] = ComputeBollingerWidth(bars);
            features[6] = ComputeOrderbookImbalance(levels);
            features[7] = ComputeHourFraction();
            features[8] = currentPos;
            features[9] = ComputeSessionFlag();
            
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

    private decimal ComputeHourFraction()
    {
        var now = DateTime.UtcNow;
        return now.Hour / 24.0m;
    }

    private decimal ComputeSessionFlag()
    {
        // Simplified session detection
        // ES/NQ main session is roughly 9:30 AM - 4:00 PM ET (13:30 - 20:00 UTC)
        var now = DateTime.UtcNow;
        var hour = now.Hour;
        
        // Return 1 if in main session, 0 otherwise
        if (hour >= 13 && hour < 20)
        {
            return 1;
        }
        
        return 0;
    }
}
