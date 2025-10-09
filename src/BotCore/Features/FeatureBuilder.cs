using BotCore.Market;
using BotCore.Config;
using BotCore.Services;
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
    // Feature array indices (13 features total)
    private const int Return1mIndex = 0;
    private const int Return5mIndex = 1;
    private const int Atr14Index = 2;
    private const int Rsi14Index = 3;
    private const int VwapDistIndex = 4;
    private const int BollingerWidthIndex = 5;
    private const int OrderbookImbalanceIndex = 6;
    private const int AdrPctIndex = 7;
    private const int HourFractionIndex = 8;
    private const int SessionFlagIndex = 9;
    private const int SessionTypeIndex = 10;
    private const int CurrentPosIndex = 11;
    private const int S7RegimeIndex = 12;
    
    // RSI calculation constants
    private const decimal RsiMaxValue = 100m;
    
    // Bollinger Bands constants
    private const int BollingerStdDevMultiplier = 2;
    private const int VwapTypicalPriceDivisor = 3;
    
    // Orderbook imbalance constants
    private const decimal NeutralOrderbookImbalance = 1.0m;
    
    // ADR (Average Daily Range) constants
    private const decimal MaxReasonableAdrRatio = 10m;
    
    // Session mapping constants (ES/NQ trading schedule: 11 sessions indexed 0-10)
    private const int AsianSessionIndex = 0;
    private const int EuropeanPreOpenIndex = 1;
    private const int EuropeanOpenIndex = 2;
    private const int LondonMorningIndex = 3;
    private const int USPreMarketIndex = 4;
    private const int OpeningDriveIndex = 5;
    private const int MorningTrendIndex = 6;
    private const int LunchChopIndex = 7;
    private const int AfternoonTrendIndex = 8;
    private const int PowerHourIndex = 9;
    private const int MarketCloseIndex = 10;
    
    // Session type constants (coarse-grained: 3 types indexed 0-2)
    private const int OvernightSessionType = 0;
    private const int RthSessionType = 1;
    private const int PostRthSessionType = 2;
    
    // Time-of-day thresholds for session type fallback logic
    private const int OvernightEndHour = 9;
    private const int OvernightEndMinute = 30;
    private const int PostRthStartHour = 16;
    private const int OvernightStartHour = 18;
    
    private readonly FeatureSpec _spec;
    private readonly IS7Service? _s7Service;
    private readonly MarketTimeService? _marketTimeService;
    private readonly FeatureComputationConfig _config;
    private readonly ILogger<FeatureBuilder> _logger;

    public FeatureBuilder(
        FeatureSpec spec, 
        FeatureComputationConfig config,
        ILogger<FeatureBuilder> logger,
        IS7Service? s7Service = null,
        MarketTimeService? marketTimeService = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        
        _spec = spec;
        _config = config;
        _logger = logger;
        _s7Service = s7Service;
        _marketTimeService = marketTimeService;
        
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
    /// <returns>Standardized feature array of length 13</returns>
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
            // Compute raw features (13 features optimized for S2/S3/S6/S11 + session_type)
            features[Return1mIndex] = ComputeReturn1m(bars);
            features[Return5mIndex] = ComputeReturn5m(bars);
            features[Atr14Index] = ComputeAtr14(bars, env);
            features[Rsi14Index] = ComputeRsi14(bars);
            features[VwapDistIndex] = ComputeVwapDist(bars);
            features[BollingerWidthIndex] = ComputeBollingerWidth(bars);
            features[OrderbookImbalanceIndex] = ComputeOrderbookImbalance(levels);
            features[AdrPctIndex] = ComputeAdrPct(bars);
            features[HourFractionIndex] = ComputeHourFraction(currentTime);
            features[SessionFlagIndex] = ComputeSessionFlag(currentTime);
            features[SessionTypeIndex] = ComputeSessionType(symbol, currentTime);
            features[CurrentPosIndex] = currentPos;
            features[S7RegimeIndex] = ComputeS7Regime(symbol);
            
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
        catch (DivideByZeroException ex)
        {
            // Fail-closed: Log error with telemetry and return fill values
            _logger.LogError(ex, "[FEATURE-BUILDER] Division by zero in feature computation for {Symbol}. Returning fill values.", 
                symbol);
            
            // Fill with default values as fallback
            for (int i = 0; i < features.Length; i++)
            {
                features[i] = _spec.Columns[i].FillValue;
            }
            
            return features;
        }
        catch (InvalidOperationException ex)
        {
            // Fail-closed: Log error with telemetry and return fill values
            _logger.LogError(ex, "[FEATURE-BUILDER] Invalid operation in feature computation for {Symbol}. Returning fill values.", 
                symbol);
            
            // Fill with default values as fallback
            for (int i = 0; i < features.Length; i++)
            {
                features[i] = _spec.Columns[i].FillValue;
            }
            
            return features;
        }
        catch (ArgumentException ex)
        {
            // Fail-closed: Log error with telemetry and return fill values
            _logger.LogError(ex, "[FEATURE-BUILDER] Invalid argument in feature computation for {Symbol}. Returning fill values.", 
                symbol);
            
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
        if (bars.Count < _config.AtrPeriod + 1) return _spec.Columns[Atr14Index].FillValue;
        
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
        if (bars.Count < _config.RsiPeriod + 1) return _spec.Columns[Rsi14Index].FillValue;
        
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
        
        if (avgLoss == 0) return RsiMaxValue;
        
        var rs = avgGain / avgLoss;
        var rsi = RsiMaxValue - (RsiMaxValue / (1 + rs));
        
        return rsi;
    }

    private decimal ComputeVwapDist(List<Bar> bars)
    {
        if (bars.Count < 1) return _spec.Columns[VwapDistIndex].FillValue;
        
        // Compute VWAP from available bars
        var barsToUse = Math.Min(_config.VwapBars, bars.Count);
        decimal volumeSum = 0;
        decimal volumePriceSum = 0;
        
        for (int i = bars.Count - barsToUse; i < bars.Count; i++)
        {
            var typicalPrice = (bars[i].High + bars[i].Low + bars[i].Close) / VwapTypicalPriceDivisor;
            var volume = bars[i].Volume;
            volumePriceSum += typicalPrice * volume;
            volumeSum += volume;
        }
        
        if (volumeSum == 0) return _spec.Columns[VwapDistIndex].FillValue;
        
        var vwap = volumePriceSum / volumeSum;
        var lastClose = bars[^1].Close;
        
        if (vwap == 0) return _spec.Columns[VwapDistIndex].FillValue;
        
        return (lastClose - vwap) / vwap;
    }

    private decimal ComputeBollingerWidth(List<Bar> bars)
    {
        if (bars.Count < _config.BollingerPeriod) return _spec.Columns[BollingerWidthIndex].FillValue;
        
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
        
        var upper = middle + (BollingerStdDevMultiplier * stdDev);
        var lower = middle - (BollingerStdDevMultiplier * stdDev);
        
        if (middle == 0) return _spec.Columns[BollingerWidthIndex].FillValue;
        
        return (upper - lower) / middle;
    }

    private decimal ComputeOrderbookImbalance(object? levels)
    {
        if (levels == null) return _spec.Columns[OrderbookImbalanceIndex].FillValue;
        
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
                    return _spec.Columns[OrderbookImbalanceIndex].FillValue;
                }
                
                // Calculate imbalance if both sides exist
                // This would need proper implementation based on actual orderbook structure
                // For now, return neutral as orderbook processing requires full implementation
                return NeutralOrderbookImbalance;
            }
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Reflection error computing orderbook imbalance. Using fill value.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Invalid operation computing orderbook imbalance. Using fill value.");
        }
        catch (InvalidCastException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Type cast error computing orderbook imbalance. Using fill value.");
        }
        
        return _spec.Columns[OrderbookImbalanceIndex].FillValue;
    }

    private decimal ComputeAdrPct(List<Bar> bars)
    {
        // Compute ADR percentage: (current range) / (average daily range over configured days)
        // This is used by S11 exhaustion strategy
        if (bars == null || bars.Count == 0)
        {
            _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: bars list is null or empty. Using fill value.");
            return _spec.Columns[AdrPctIndex].FillValue;
        }
        
        // Validate config (fail fast to surface bad config)
        if (_config.AdrDays < 1)
        {
            _logger.LogError("[FEATURE-BUILDER] ComputeAdrPct: Invalid config AdrDays={AdrDays}. Using fill value.", _config.AdrDays);
            return _spec.Columns[AdrPctIndex].FillValue;
        }
        
        if (_config.CurrentRangeBars < 1)
        {
            _logger.LogError("[FEATURE-BUILDER] ComputeAdrPct: Invalid config CurrentRangeBars={CurrentRangeBars}. Using fill value.", _config.CurrentRangeBars);
            return _spec.Columns[AdrPctIndex].FillValue;
        }
        
        try
        {
            // Calculate current intraday range using indexed slicing (avoid TakeLast allocation)
            var startIdx = Math.Max(0, bars.Count - _config.CurrentRangeBars);
            decimal currentHigh = bars[startIdx].High;
            decimal currentLow = bars[startIdx].Low;
            
            for (int i = startIdx; i < bars.Count; i++)
            {
                if (bars[i].High > currentHigh) currentHigh = bars[i].High;
                if (bars[i].Low < currentLow) currentLow = bars[i].Low;
            }
            
            var currentRange = currentHigh - currentLow;
            if (currentRange < 0)
            {
                _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: Current range is negative ({CurrentRange}). Using fill value.", currentRange);
                return _spec.Columns[AdrPctIndex].FillValue;
            }
            
            // Group bars by trading date using bar timestamps (handles different bar resolutions)
            // Use bar.Start for grouping to ensure consistent trading day boundaries
            var barsByDate = new Dictionary<DateTime, List<Bar>>();
            
            foreach (var bar in bars)
            {
                // Use date only (ignores time) to group by trading day
                var tradingDate = bar.Start.Date;
                if (!barsByDate.ContainsKey(tradingDate))
                {
                    barsByDate[tradingDate] = new List<Bar>();
                }
                barsByDate[tradingDate].Add(bar);
            }
            
            // Get the last N trading days
            var sortedDates = barsByDate.Keys.OrderByDescending(d => d).Take(_config.AdrDays).ToList();
            
            if (sortedDates.Count == 0)
            {
                _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: No trading days found. Using fill value.");
                return _spec.Columns[AdrPctIndex].FillValue;
            }
            
            // Calculate average daily range over the available trading days
            decimal totalRange = 0;
            int validDays = 0;
            
            foreach (var date in sortedDates)
            {
                var dayBars = barsByDate[date];
                if (dayBars.Count > 0)
                {
                    decimal dayHigh = dayBars[0].High;
                    decimal dayLow = dayBars[0].Low;
                    
                    foreach (var bar in dayBars)
                    {
                        if (bar.High > dayHigh) dayHigh = bar.High;
                        if (bar.Low < dayLow) dayLow = bar.Low;
                    }
                    
                    var dayRange = dayHigh - dayLow;
                    if (dayRange >= 0) // Only count valid ranges
                    {
                        totalRange += dayRange;
                        validDays++;
                    }
                }
            }
            
            if (validDays == 0)
            {
                _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: No valid trading days found. Using fill value.");
                return _spec.Columns[AdrPctIndex].FillValue;
            }
            
            var avgDailyRange = totalRange / validDays;
            
            // Guard against divide-by-zero
            if (avgDailyRange <= 0)
            {
                _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: Average daily range is {AvgDailyRange}. Using fill value.", avgDailyRange);
                return _spec.Columns[AdrPctIndex].FillValue;
            }
            
            var adrPct = currentRange / avgDailyRange;
            
            // Sanity check: ADR percentage should be reasonable (0 to MaxReasonableAdrRatio)
            if (adrPct < 0 || adrPct > MaxReasonableAdrRatio)
            {
                _logger.LogWarning("[FEATURE-BUILDER] ComputeAdrPct: ADR percentage {AdrPct} is out of reasonable range [0, {MaxRatio}]. Using fill value.", adrPct, MaxReasonableAdrRatio);
                return _spec.Columns[AdrPctIndex].FillValue;
            }
            
            return adrPct;
        }
        catch (DivideByZeroException ex)
        {
            _logger.LogError(ex, "[FEATURE-BUILDER] ComputeAdrPct: Division by zero. Using fill value.");
            return _spec.Columns[AdrPctIndex].FillValue;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "[FEATURE-BUILDER] ComputeAdrPct: Invalid operation. Using fill value.");
            return _spec.Columns[AdrPctIndex].FillValue;
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
                return _spec.Columns[SessionFlagIndex].FillValue;
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
                "AsianSession" => AsianSessionIndex,
                "EuropeanPreOpen" => EuropeanPreOpenIndex,
                "EuropeanOpen" => EuropeanOpenIndex,
                "LondonMorning" => LondonMorningIndex,
                "USPreMarket" => USPreMarketIndex,
                "OpeningDrive" => OpeningDriveIndex,
                "MorningTrend" => MorningTrendIndex,
                "LunchChop" => LunchChopIndex,
                "AfternoonTrend" => AfternoonTrendIndex,
                "PowerHour" => PowerHourIndex,
                "MarketClose" => MarketCloseIndex,
                _ => _spec.Columns[SessionFlagIndex].FillValue
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Invalid operation computing session flag. Using fill value.");
            return _spec.Columns[SessionFlagIndex].FillValue;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Invalid argument computing session flag. Using fill value.");
            return _spec.Columns[SessionFlagIndex].FillValue;
        }
    }

    private decimal ComputeSessionType(string symbol, DateTime currentTime)
    {
        // Compute session type for parameter optimization: Overnight=0, RTH/Open=1, PostRTH=2
        // This is different from session_flag which maps to 11 fine-grained trading sessions
        
        try
        {
            if (_marketTimeService == null)
            {
                // If no market time service available, fall back to simple time-of-day logic
                var hour = currentTime.Hour;
                // Rough approximation based on configured thresholds
                if (hour >= OvernightStartHour || hour < OvernightEndHour || (hour == OvernightEndHour && currentTime.Minute < OvernightEndMinute))
                    return OvernightSessionType;
                else if (hour >= PostRthStartHour)
                    return PostRthSessionType;
                else
                    return RthSessionType;
            }
            
            // Use MarketTimeService to get accurate session
            var sessionTask = _marketTimeService.GetCurrentSessionAsync(symbol);
            sessionTask.Wait(); // Synchronous wait for async method
            var sessionName = sessionTask.Result;
            
            // Map MarketTimeService session names to categorical values
            return sessionName switch
            {
                "Overnight" => OvernightSessionType,
                "PreMarket" => OvernightSessionType,  // Treat pre-market as overnight
                "Open" => RthSessionType,       // RTH open
                "RTH" => RthSessionType,        // Regular trading hours
                "PostMarket" => PostRthSessionType, // Post-market
                "Closed" => OvernightSessionType,     // Treat closed as overnight
                _ => _spec.Columns[SessionTypeIndex].FillValue
            };
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Task error computing session type for {Symbol}. Using fill value.", 
                symbol);
            return _spec.Columns[SessionTypeIndex].FillValue;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Invalid operation computing session type for {Symbol}. Using fill value.", 
                symbol);
            return _spec.Columns[SessionTypeIndex].FillValue;
        }
    }

    private decimal ComputeS7Regime(string symbol)
    {
        // Get S7 regime signal: -1 (bearish), 0 (neutral), 1 (bullish)
        if (_s7Service == null) return _spec.Columns[S7RegimeIndex].FillValue;
        
        try
        {
            if (!_s7Service.IsReady())
            {
                _logger.LogDebug("[FEATURE-BUILDER] S7Service not ready for symbol {Symbol}. Using fill value.", symbol);
                return _spec.Columns[S7RegimeIndex].FillValue;
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Invalid operation computing S7 regime for {Symbol}. Using fill value.", 
                symbol);
            return _spec.Columns[S7RegimeIndex].FillValue;
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "[FEATURE-BUILDER] Key not found computing S7 regime for {Symbol}. Using fill value.", 
                symbol);
            return _spec.Columns[S7RegimeIndex].FillValue;
        }
    }
}
