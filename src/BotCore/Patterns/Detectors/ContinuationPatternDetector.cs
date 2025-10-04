using System;
using BotCore.Models;

namespace BotCore.Patterns.Detectors;

/// <summary>
/// Continuation pattern detector family - identifies patterns that indicate trend continuation
/// </summary>
public class ContinuationPatternDetector : IPatternDetector
{
    // Pattern bar requirements
    private const int FlagBars = 15;
    private const int PennantBars = 20;
    private const int WedgeBars = 25;
    private const int CompressionBars = 12;
    private const int ConsolidationBars = 10;
    private const int BreakoutRetestBars = 8;
    private const int UnknownPatternBars = 10;

    // Flag pattern thresholds
    private const int FlagLookback = 20;
    private const int FlagMinBars = 15;
    private const double FlagTrendPortionRatio = 0.6;
    private const int FlagMinTrendBars = 5;
    private const double FlagMaxSlopeRatio = 0.3;
    private const double FlagMaxScore = 0.85;
    private const double FlagBaseScore = 0.6;
    private const double FlagSlopeWeight = 0.8;

    // Pennant pattern thresholds
    private const int PennantLookback = 25;
    private const int PennantMinBars = 20;
    private const double PennantTrendPortionRatio = 0.4;
    private const int PennantMinTrendBars = 5;
    private const int PennantMinConsolidationBars = 10;
    private const int PennantRangeSampleSize = 3;
    private const decimal PennantMinRangeDivisor = 0.01m;
    private const double PennantMaxConvergenceRatio = 0.6;
    private const double PennantMaxScore = 0.8;
    private const double PennantBaseScore = 0.5;
    private const double PennantConvergenceWeight = 0.5;

    // Compression pattern thresholds
    private const int CompressionLookback = 15;
    private const int CompressionMinBars = 12;
    private const double CompressionMaxRatio = 0.7;
    private const double CompressionMaxScore = 0.8;
    private const double CompressionBaseScore = 0.5;
    private const double CompressionWeight = 0.4;
    private const decimal CompressionMinAvgDivisor = 0.01m;

    // Breakout retest thresholds
    private const int BreakoutRetestMinBars = 10;
    private const int BreakoutRetestStartIndex = 3;
    private const int BreakoutRetestEndOffset = 2;
    private const decimal BreakoutMoveMultiplier = 1.5m;
    private const decimal RetestDistanceMultiplier = 0.5m;
    private const double BreakoutRetestScore = 0.7;

    // Wedge pattern thresholds
    private const double WedgeScore = 0.6;

    // Consolidation pattern thresholds
    private const int ConsolidationLookback = 12;
    private const int ConsolidationMinBars = 10;
    private const double ConsolidationMaxUtilization = 0.3;
    private const double ConsolidationScore = 0.65;
    private const double ConsolidationMinRangeDivisor = 0.01;

    public string PatternName { get; }
    public PatternFamily Family => PatternFamily.Continuation;
    public int RequiredBars { get; }

    private readonly ContinuationType _type;

    public ContinuationPatternDetector(ContinuationType type)
    {
        _type = type;
        (PatternName, RequiredBars) = type switch
        {
            ContinuationType.BullFlag => ("BullFlag", FlagBars),
            ContinuationType.BearFlag => ("BearFlag", FlagBars),
            ContinuationType.BullPennant => ("BullPennant", PennantBars),
            ContinuationType.BearPennant => ("BearPennant", PennantBars),
            ContinuationType.BullWedge => ("BullWedge", WedgeBars),
            ContinuationType.BearWedge => ("BearWedge", WedgeBars),
            ContinuationType.RisingWedge => ("RisingWedge", PennantBars),
            ContinuationType.FallingWedge => ("FallingWedge", PennantBars),
            ContinuationType.Compression => ("Compression", CompressionBars),
            ContinuationType.Consolidation => ("Consolidation", ConsolidationBars),
            ContinuationType.BreakoutRetest => ("BreakoutRetest", BreakoutRetestBars),
            _ => ("Unknown", UnknownPatternBars)
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
        var lookback = Math.Min(bars.Count, FlagLookback);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < FlagMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        // Check for prior uptrend (first 60% of period)
        var trendPortion = (int)(recent.Count * FlagTrendPortionRatio);
        var trendBars = recent.Take(trendPortion).ToList();
        
        if (trendBars.Count < FlagMinTrendBars) return new PatternResult { Score = 0, Confidence = 0 };
        
        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        if (trendSlope <= 0) return new PatternResult { Score = 0, Confidence = 0 }; // Must be uptrend

        // Check for consolidation/pullback in remaining bars
        var consolidationBars = recent.Skip(trendPortion).ToList();
        var consolidationSlope = Math.Abs((double)(consolidationBars.Last().Close - consolidationBars.First().Close) / consolidationBars.Count);
        
        // Flag should be relatively flat compared to prior trend
        var slopeRatio = consolidationSlope / Math.Abs(trendSlope);
        
        var score = 0.0;
        if (slopeRatio < FlagMaxSlopeRatio) // Consolidation is much flatter than trend
        {
            score = Math.Min(FlagMaxScore, FlagBaseScore + (FlagMaxSlopeRatio - slopeRatio) * FlagSlopeWeight);
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
        var lookback = Math.Min(bars.Count, FlagLookback);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < FlagMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        var trendPortion = (int)(recent.Count * FlagTrendPortionRatio);
        var trendBars = recent.Take(trendPortion).ToList();
        
        if (trendBars.Count < FlagMinTrendBars) return new PatternResult { Score = 0, Confidence = 0 };
        
        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        if (trendSlope >= 0) return new PatternResult { Score = 0, Confidence = 0 }; // Must be downtrend

        var consolidationBars = recent.Skip(trendPortion).ToList();
        var consolidationSlope = Math.Abs((double)(consolidationBars.Last().Close - consolidationBars.First().Close) / consolidationBars.Count);
        
        var slopeRatio = consolidationSlope / Math.Abs(trendSlope);
        
        var score = 0.0;
        if (slopeRatio < FlagMaxSlopeRatio)
        {
            score = Math.Min(FlagMaxScore, FlagBaseScore + (FlagMaxSlopeRatio - slopeRatio) * FlagSlopeWeight);
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
        var lookback = Math.Min(bars.Count, PennantLookback);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < PennantMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        // Pennant: strong trend followed by converging price action (triangle-like)
        var trendPortion = (int)(recent.Count * PennantTrendPortionRatio);
        var trendBars = recent.Take(trendPortion).ToList();
        var pennantBars = recent.Skip(trendPortion).ToList();

        if (trendBars.Count < PennantMinTrendBars || pennantBars.Count < PennantMinConsolidationBars) 
            return new PatternResult { Score = 0, Confidence = 0 };

        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        
        // Validate trend direction
        if (bullish && trendSlope <= 0) return new PatternResult { Score = 0, Confidence = 0 };
        if (!bullish && trendSlope >= 0) return new PatternResult { Score = 0, Confidence = 0 };

        // Check for converging price action in pennant portion
        var highs = pennantBars.Select(b => b.High).ToList();
        var lows = pennantBars.Select(b => b.Low).ToList();
        
        var initialRange = (double)(highs.Take(PennantRangeSampleSize).Average() - lows.Take(PennantRangeSampleSize).Average());
        var finalRange = (double)(highs.TakeLast(PennantRangeSampleSize).Average() - lows.TakeLast(PennantRangeSampleSize).Average());
        
        var convergenceRatio = finalRange / Math.Max(initialRange, (double)PennantMinRangeDivisor);
        
        var score = 0.0;
        if (convergenceRatio < PennantMaxConvergenceRatio) // Range has contracted
        {
            score = Math.Min(PennantMaxScore, PennantBaseScore + (PennantMaxConvergenceRatio - convergenceRatio) * PennantConvergenceWeight);
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
        var lookback = Math.Min(bars.Count, CompressionLookback);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < CompressionMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        var ranges = recent.Select(b => (double)(b.High - b.Low)).ToList();
        var earlyAvg = ranges.Take(ranges.Count / 2).Average();
        var lateAvg = ranges.Skip(ranges.Count / 2).Average();
        
        var compressionRatio = lateAvg / Math.Max(earlyAvg, (double)CompressionMinAvgDivisor);
        
        var score = 0.0;
        if (compressionRatio < CompressionMaxRatio) // Significant compression
        {
            score = Math.Min(CompressionMaxScore, CompressionBaseScore + (CompressionMaxRatio - compressionRatio) * CompressionWeight);
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
        if (bars.Count < BreakoutRetestMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(BreakoutRetestMinBars).ToList();
        
        // Look for a breakout followed by a retest
        var potentialBreakout = false;
        var retestIndex = -1;
        
        for (int i = BreakoutRetestStartIndex; i < recent.Count - BreakoutRetestEndOffset; i++)
        {
            var current = recent[i];
            var prev = recent[i - 1];
            
            // Check for strong move (potential breakout)
            var moveSize = Math.Abs(current.Close - prev.Close);
            var avgRange = recent.Take(i).Select(b => b.High - b.Low).Average();
            
            if (moveSize > avgRange * BreakoutMoveMultiplier) // Strong move
            {
                potentialBreakout = true;
                
                // Look for subsequent pullback toward breakout level
                for (int j = i + 1; j < recent.Count; j++)
                {
                    var testBar = recent[j];
                    var breakoutLevel = current.Close;
                    var distance = Math.Abs(testBar.Close - breakoutLevel);
                    
                    if (distance < avgRange * RetestDistanceMultiplier) // Close to breakout level
                    {
                        retestIndex = j;
                        break;
                    }
                }
                break;
            }
        }

        var score = potentialBreakout && retestIndex > 0 ? BreakoutRetestScore : 0.0;

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
        var score = WedgeScore;
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
        var lookback = Math.Min(bars.Count, ConsolidationLookback);
        var recent = bars.TakeLast(lookback).ToList();
        
        if (recent.Count < ConsolidationMinBars) return new PatternResult { Score = 0, Confidence = 0 };

        var highs = recent.Select(b => b.High).ToList();
        var lows = recent.Select(b => b.Low).ToList();
        
        var maxHigh = highs.Max();
        var minLow = lows.Min();
        var totalRange = (double)(maxHigh - minLow);
        
        // Check how much of the range is actually used
        var usedRange = recent.Select(b => (double)(b.High - b.Low)).Average();
        var rangeUtilization = usedRange / Math.Max(totalRange, ConsolidationMinRangeDivisor);
        
        // Consolidation if price stays within a tight range
        var score = rangeUtilization < ConsolidationMaxUtilization ? ConsolidationScore : 0.0;

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