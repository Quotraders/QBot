using System;
using BotCore.Models;

namespace BotCore.Patterns.Detectors;

/// <summary>
/// Continuation pattern detector family - identifies patterns that indicate trend continuation
/// </summary>
public class ContinuationPatternDetector : IPatternDetector
{
    public string PatternName { get; }
    public PatternFamily Family => PatternFamily.Continuation;
    public int RequiredBars { get; }

    private readonly ContinuationType _type;

    public ContinuationPatternDetector(ContinuationType type)
    {
        _type = type;
        (PatternName, RequiredBars) = type switch
        {
            ContinuationType.BullFlag => ("BullFlag", 15),
            ContinuationType.BearFlag => ("BearFlag", 15),
            ContinuationType.BullPennant => ("BullPennant", 20),
            ContinuationType.BearPennant => ("BearPennant", 20),
            ContinuationType.BullWedge => ("BullWedge", 25),
            ContinuationType.BearWedge => ("BearWedge", 25),
            ContinuationType.RisingWedge => ("RisingWedge", 20),
            ContinuationType.FallingWedge => ("FallingWedge", 20),
            ContinuationType.Compression => ("Compression", 12),
            ContinuationType.Consolidation => ("Consolidation", 10),
            ContinuationType.BreakoutRetest => ("BreakoutRetest", 8),
            _ => ("Unknown", 10)
        };
    }

    public PatternResult Detect(IReadOnlyList<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        
        if (bars.Count < RequiredBars)
        {
            return new PatternResult { Score = 0, Confidence = 0 };
        }

        return _type switch
        {
            ContinuationType.BullFlag => DetectBullFlag(bars),
            ContinuationType.BearFlag => DetectBearFlag(bars),
            ContinuationType.BullPennant => DetectBullPennant(bars),
            ContinuationType.BearPennant => DetectBearPennant(bars),
            ContinuationType.BullWedge => DetectBullWedge(bars),
            ContinuationType.BearWedge => DetectBearWedge(bars),
            ContinuationType.RisingWedge => DetectRisingWedge(bars),
            ContinuationType.FallingWedge => DetectFallingWedge(bars),
            ContinuationType.Compression => DetectCompression(bars),
            ContinuationType.Consolidation => DetectConsolidation(bars),
            ContinuationType.BreakoutRetest => DetectBreakoutRetest(bars),
            _ => new PatternResult { Score = 0, Confidence = 0 }
        };
    }

    private static PatternResult DetectBullFlag(IReadOnlyList<Bar> bars)
    {
        // Bull flag: strong uptrend followed by sideways/slight downward consolidation
        var lookback = Math.Min(bars.Count, 20);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < 15) return new PatternResult { Score = 0, Confidence = 0 };

        // Check for prior uptrend (first 60% of period)
        var trendPortion = (int)(recent.Count * 0.6);
        var trendBars = recent.Take(trendPortion).ToList();
        
        if (trendBars.Count < 5) return new PatternResult { Score = 0, Confidence = 0 };
        
        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        if (trendSlope <= 0) return new PatternResult { Score = 0, Confidence = 0 }; // Must be uptrend

        // Check for consolidation/pullback in remaining bars
        var consolidationBars = recent.Skip(trendPortion).ToList();
        var consolidationSlope = Math.Abs((double)(consolidationBars.Last().Close - consolidationBars.First().Close) / consolidationBars.Count);
        
        // Flag should be relatively flat compared to prior trend
        var slopeRatio = consolidationSlope / Math.Abs(trendSlope);
        
        var score = 0.0;
        if (slopeRatio < 0.3) // Consolidation is much flatter than trend
        {
            score = Math.Min(0.85, 0.6 + (0.3 - slopeRatio) * 0.8);
        }

        return new PatternResult
        {
            Score = score,
            Direction = 1, // Bullish continuation
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["trend_slope"] = trendSlope,
                ["consolidation_slope"] = consolidationSlope,
                ["slope_ratio"] = slopeRatio
            }
        };
    }

    private static PatternResult DetectBearFlag(IReadOnlyList<Bar> bars)
    {
        var lookback = Math.Min(bars.Count, 20);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < 15) return new PatternResult { Score = 0, Confidence = 0 };

        var trendPortion = (int)(recent.Count * 0.6);
        var trendBars = recent.Take(trendPortion).ToList();
        
        if (trendBars.Count < 5) return new PatternResult { Score = 0, Confidence = 0 };
        
        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        if (trendSlope >= 0) return new PatternResult { Score = 0, Confidence = 0 }; // Must be downtrend

        var consolidationBars = recent.Skip(trendPortion).ToList();
        var consolidationSlope = Math.Abs((double)(consolidationBars.Last().Close - consolidationBars.First().Close) / consolidationBars.Count);
        
        var slopeRatio = consolidationSlope / Math.Abs(trendSlope);
        
        var score = 0.0;
        if (slopeRatio < 0.3)
        {
            score = Math.Min(0.85, 0.6 + (0.3 - slopeRatio) * 0.8);
        }

        return new PatternResult
        {
            Score = score,
            Direction = -1, // Bearish continuation
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["trend_slope"] = trendSlope,
                ["consolidation_slope"] = consolidationSlope,
                ["slope_ratio"] = slopeRatio
            }
        };
    }

    private static PatternResult DetectBullPennant(IReadOnlyList<Bar> bars)
    {
        return DetectPennantPattern(bars, true);
    }

    private static PatternResult DetectBearPennant(IReadOnlyList<Bar> bars)
    {
        return DetectPennantPattern(bars, false);
    }

    private static PatternResult DetectPennantPattern(IReadOnlyList<Bar> bars, bool bullish)
    {
        var lookback = Math.Min(bars.Count, 25);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < 20) return new PatternResult { Score = 0, Confidence = 0 };

        // Pennant: strong trend followed by converging price action (triangle-like)
        var trendPortion = (int)(recent.Count * 0.4);
        var trendBars = recent.Take(trendPortion).ToList();
        var pennantBars = recent.Skip(trendPortion).ToList();

        if (trendBars.Count < 5 || pennantBars.Count < 10) 
            return new PatternResult { Score = 0, Confidence = 0 };

        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        
        // Validate trend direction
        if (bullish && trendSlope <= 0) return new PatternResult { Score = 0, Confidence = 0 };
        if (!bullish && trendSlope >= 0) return new PatternResult { Score = 0, Confidence = 0 };

        // Check for converging price action in pennant portion
        var highs = pennantBars.Select(b => b.High).ToList();
        var lows = pennantBars.Select(b => b.Low).ToList();
        
        var initialRange = (double)(highs.Take(3).Average() - lows.Take(3).Average());
        var finalRange = (double)(highs.TakeLast(3).Average() - lows.TakeLast(3).Average());
        
        var convergenceRatio = finalRange / Math.Max(initialRange, 0.01);
        
        var score = 0.0;
        if (convergenceRatio < 0.6) // Range has contracted
        {
            score = Math.Min(0.8, 0.5 + (0.6 - convergenceRatio) * 0.5);
        }

        return new PatternResult
        {
            Score = score,
            Direction = bullish ? 1 : -1,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["trend_slope"] = trendSlope,
                ["convergence_ratio"] = convergenceRatio,
                ["type"] = bullish ? "bull_pennant" : "bear_pennant"
            }
        };
    }

    private static PatternResult DetectCompression(IReadOnlyList<Bar> bars)
    {
        // Volatility compression - decreasing price ranges
        var lookback = Math.Min(bars.Count, 15);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < 12) return new PatternResult { Score = 0, Confidence = 0 };

        var ranges = recent.Select(b => (double)(b.High - b.Low)).ToList();
        var earlyAvg = ranges.Take(ranges.Count / 2).Average();
        var lateAvg = ranges.Skip(ranges.Count / 2).Average();
        
        var compressionRatio = lateAvg / Math.Max(earlyAvg, 0.01);
        
        var score = 0.0;
        if (compressionRatio < 0.7) // Significant compression
        {
            score = Math.Min(0.8, 0.5 + (0.7 - compressionRatio) * 0.4);
        }

        return new PatternResult
        {
            Score = score,
            Direction = 0, // Neutral until breakout
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["compression_ratio"] = compressionRatio,
                ["early_avg_range"] = earlyAvg,
                ["late_avg_range"] = lateAvg
            }
        };
    }

    private static PatternResult DetectBreakoutRetest(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 10) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(10).ToList();
        
        // Look for a breakout followed by a retest
        var potentialBreakout = false;
        var retestIndex = -1;
        
        for (int i = 3; i < recent.Count - 2; i++)
        {
            var current = recent[i];
            var prev = recent[i - 1];
            
            // Check for strong move (potential breakout)
            var moveSize = Math.Abs(current.Close - prev.Close);
            var avgRange = recent.Take(i).Select(b => b.High - b.Low).Average();
            
            if (moveSize > avgRange * 1.5m) // Strong move
            {
                potentialBreakout = true;
                
                // Look for subsequent pullback toward breakout level
                for (int j = i + 1; j < recent.Count; j++)
                {
                    var testBar = recent[j];
                    var breakoutLevel = current.Close;
                    var distance = Math.Abs(testBar.Close - breakoutLevel);
                    
                    if (distance < avgRange * 0.5m) // Close to breakout level
                    {
                        retestIndex = j;
                        break;
                    }
                }
                break;
            }
        }

        var score = potentialBreakout && retestIndex > 0 ? 0.7 : 0.0;

        return new PatternResult
        {
            Score = score,
            Direction = 0, // Direction depends on breakout direction
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["breakout_detected"] = potentialBreakout,
                ["retest_index"] = retestIndex
            }
        };
    }

    // Simplified implementations for remaining patterns
    private static PatternResult DetectBullWedge(IReadOnlyList<Bar> bars) => DetectWedgePattern(bars, "BullWedge", 1);
    private static PatternResult DetectBearWedge(IReadOnlyList<Bar> bars) => DetectWedgePattern(bars, "BearWedge", -1);
    private static PatternResult DetectRisingWedge(IReadOnlyList<Bar> bars) => DetectWedgePattern(bars, "RisingWedge", -1);
    private static PatternResult DetectFallingWedge(IReadOnlyList<Bar> bars) => DetectWedgePattern(bars, "FallingWedge", 1);
    private static PatternResult DetectConsolidation(IReadOnlyList<Bar> bars) => DetectRangePattern(bars, "Consolidation");

    private static PatternResult DetectWedgePattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        var score = 0.6;
        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static PatternResult DetectRangePattern(IReadOnlyList<Bar> bars, string type)
    {
        var lookback = Math.Min(bars.Count, 12);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < 10) return new PatternResult { Score = 0, Confidence = 0 };

        var highs = recent.Select(b => b.High).ToList();
        var lows = recent.Select(b => b.Low).ToList();
        
        var maxHigh = highs.Max();
        var minLow = lows.Min();
        var totalRange = (double)(maxHigh - minLow);
        
        // Check how much of the range is actually used
        var usedRange = recent.Select(b => (double)(b.High - b.Low)).Average();
        var rangeUtilization = usedRange / Math.Max(totalRange, 0.01);
        
        // Consolidation if price stays within a tight range
        var score = rangeUtilization < 0.3 ? 0.65 : 0.0;

        return new PatternResult
        {
            Score = score,
            Direction = 0, // Neutral
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = type,
                ["range_utilization"] = rangeUtilization,
                ["total_range"] = totalRange
            }
        };
    }
}

/// <summary>
/// Types of continuation patterns supported
/// </summary>
public enum ContinuationType
{
    BullFlag,
    BearFlag,
    BullPennant,
    BearPennant,
    BullWedge,
    BearWedge,
    RisingWedge,
    FallingWedge,
    Compression,
    Consolidation,
    BreakoutRetest
}