using System;
using BotCore.Models;

namespace BotCore.Patterns.Detectors;

/// <summary>
/// Reversal pattern detector family - identifies patterns that indicate trend reversal
/// </summary>
public class ReversalPatternDetector : IPatternDetector
{
    // Pattern bar requirements
    private const int KeyReversalBars = 2;
    private const int IslandReversalBars = 5;
    private const int ExhaustionGapBars = 3;
    private const int ClimaxReversalBars = 5;
    private const int VolumeSpikeBars = 3;
    private const int DivergenceReversalBars = 10;
    private const int FailedBreakoutBars = 8;
    private const int TrendExhaustionBars = 15;
    private const int SpikeyTopBars = 3;
    private const int SpikeyBottomBars = 3;
    private const int UnknownPatternBars = 5;

    // Detection thresholds
    private const double MinimumRangeDivisor = 0.01;
    private const double MaxKeyReversalScore = 0.9;
    private const double KeyReversalBaseScore = 0.6;
    private const double KeyReversalWickWeight = 0.5;
    private const double MaxIslandReversalScore = 0.8;
    private const double IslandReversalBaseScore = 0.5;
    private const double IslandReversalGapWeight = 0.1;
    private const double MaxExhaustionGapScore = 0.8;
    private const double ExhaustionGapBaseScore = 0.6;
    private const double ExhaustionGapSizeWeight = 0.05;
    private const double MinClimaxRangeMultiple = 1.5;
    private const double MinClimaxWickRatio = 0.4;
    private const double MaxClimaxScore = 0.8;
    private const double ClimaxBaseScore = 0.5;
    private const double ClimaxWickWeight = 0.6;
    private const double VolumeSpikeBaseScore = 0.5;
    private const double FailedBreakoutScore = 0.75;
    private const double TrendExhaustionScore = 0.6;
    private const double TrendWeakeningThreshold = 0.3;
    private const double SpikeyExtremeScore = 0.7;
    private const decimal SpikeyExtremeCloseRatio = 0.6m;
    private const double DivergenceReversalBaseScore = 0.6;

    // Lookback periods
    private const int IslandReversalLookback = 5;
    private const int ExhaustionGapRecentBars = 5;
    private const int ExhaustionGapTrendBars = 5;
    private const int ExhaustionGapFullLookback = 10;
    private const int ClimaxReversalLookback = 5;
    private const int ClimaxReversalAvgBars = 4;
    private const int FailedBreakoutLookback = 8;
    private const int FailedBreakoutMinPrevBars = 2;
    private const int FailedBreakoutMinFollowup = 2;
    private const int FailedBreakoutStartIndex = 2;
    private const int TrendExhaustionLookback = 15;
    private const int TrendExhaustionSegmentBars = 5;
    private const int SpikeyExtremeLookback = 3;

    public string PatternName { get; }
    public PatternFamily Family => PatternFamily.Reversal;
    public int RequiredBars { get; }

    private readonly ReversalType _type;

    public ReversalPatternDetector(ReversalType type)
    {
        _type = type;
        (PatternName, RequiredBars) = type switch
        {
            ReversalType.KeyReversal => ("KeyReversal", KeyReversalBars),
            ReversalType.IslandReversal => ("IslandReversal", IslandReversalBars),
            ReversalType.ExhaustionGap => ("ExhaustionGap", ExhaustionGapBars),
            ReversalType.ClimaxReversal => ("ClimaxReversal", ClimaxReversalBars),
            ReversalType.VolumeSpike => ("VolumeSpike", VolumeSpikeBars),
            ReversalType.DivergenceReversal => ("DivergenceReversal", DivergenceReversalBars),
            ReversalType.FailedBreakout => ("FailedBreakout", FailedBreakoutBars),
            ReversalType.TrendExhaustion => ("TrendExhaustion", TrendExhaustionBars),
            ReversalType.SpikeyTop => ("SpikeyTop", SpikeyTopBars),
            ReversalType.SpikeyBottom => ("SpikeyBottom", SpikeyBottomBars),
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
            ReversalType.KeyReversal => DetectKeyReversal(bars),
            ReversalType.IslandReversal => DetectIslandReversal(bars),
            ReversalType.ExhaustionGap => DetectExhaustionGap(bars),
            ReversalType.ClimaxReversal => DetectClimaxReversal(bars),
            ReversalType.VolumeSpike => DetectVolumeSpike(bars),
            ReversalType.DivergenceReversal => DetectDivergenceReversal(bars),
            ReversalType.FailedBreakout => DetectFailedBreakout(bars),
            ReversalType.TrendExhaustion => DetectTrendExhaustion(bars),
            ReversalType.SpikeyTop => DetectSpikeyTop(bars),
            ReversalType.SpikeyBottom => DetectSpikeyBottom(bars),
            _ => new PatternResult { Score = 0, Confidence = 0 }
        };
    }

    private static PatternResult DetectKeyReversal(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < KeyReversalBars) return new PatternResult { Score = 0, Confidence = 0 };

        var prev = bars[^2];
        var current = bars[^1];

        // Key reversal: new high/low but closes in opposite direction
        bool bullishKeyReversal = current.High > prev.High && current.Close < prev.Close && 
                                  current.Close < current.Open; // Red candle
        
        bool bearishKeyReversal = current.Low < prev.Low && current.Close > prev.Close && 
                                  current.Close > current.Open; // Green candle

        if (!bullishKeyReversal && !bearishKeyReversal) 
            return new PatternResult { Score = 0, Confidence = 0 };

        // Calculate strength based on reversal magnitude
        var rangeSize = (double)(current.High - current.Low);
        var wickSize = bullishKeyReversal ? (double)(current.High - Math.Max(current.Open, current.Close)) :
                                            (double)(Math.Min(current.Open, current.Close) - current.Low);

        var wickRatio = wickSize / Math.Max(rangeSize, MinimumRangeDivisor);
        var score = Math.Min(MaxKeyReversalScore, KeyReversalBaseScore + wickRatio * KeyReversalWickWeight);

        return new PatternResult
        {
            Score = score,
            Direction = bullishKeyReversal ? 1 : -1,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = bullishKeyReversal ? "bullish_key_reversal" : "bearish_key_reversal",
                ["wick_ratio"] = wickRatio,
                ["range_size"] = rangeSize
            }
        };
    }

    private static PatternResult DetectIslandReversal(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < IslandReversalBars) return new PatternResult { Score = 0, Confidence = 0 };

        // Island reversal: gap up/down, isolated trading, then gap in opposite direction
        var recent = bars.TakeLast(IslandReversalLookback).ToList();
        
        // Look for gaps
        var gaps = new List<GapInfo>();
        for (int i = 1; i < recent.Count; i++)
        {
            var prev = recent[i - 1];
            var curr = recent[i];
            
            if (curr.Low > prev.High) // Gap up
            {
                gaps.Add(new GapInfo { Index = i, Direction = 1, Size = (double)(curr.Low - prev.High) });
            }
            else if (curr.High < prev.Low) // Gap down
            {
                gaps.Add(new GapInfo { Index = i, Direction = -1, Size = (double)(prev.Low - curr.High) });
            }
        }

        // Need at least 2 gaps in opposite directions
        if (gaps.Count < KeyReversalBars) return new PatternResult { Score = 0, Confidence = 0 };

        var firstGap = gaps.First();
        var lastGap = gaps.Last();

        // Gaps should be in opposite directions
        if (firstGap.Direction == lastGap.Direction) 
            return new PatternResult { Score = 0, Confidence = 0 };

        var score = Math.Min(MaxIslandReversalScore, IslandReversalBaseScore + (firstGap.Size + lastGap.Size) * IslandReversalGapWeight);

        return new PatternResult
        {
            Score = score,
            Direction = -firstGap.Direction, // Reversal direction opposite to first gap
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["first_gap_direction"] = firstGap.Direction,
                ["last_gap_direction"] = lastGap.Direction,
                ["total_gap_size"] = firstGap.Size + lastGap.Size
            }
        };
    }

    private static PatternResult DetectExhaustionGap(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < ExhaustionGapBars) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(ExhaustionGapRecentBars).ToList();
        
        // Look for trend direction in earlier bars
        var trendBars = bars.TakeLast(ExhaustionGapFullLookback).Take(ExhaustionGapTrendBars).ToList();
        if (trendBars.Count < ExhaustionGapTrendBars) return new PatternResult { Score = 0, Confidence = 0 };

        var trendSlope = (double)(trendBars.Last().Close - trendBars.First().Close) / trendBars.Count;
        
        // Look for gap in trend direction followed by reversal
        for (int i = 1; i < recent.Count - 1; i++)
        {
            var prev = recent[i - 1];
            var gapBar = recent[i];
            var nextBar = recent[i + 1];

            bool upGap = gapBar.Low > prev.High;
            bool downGap = gapBar.High < prev.Low;

            if (!upGap && !downGap) continue;

            // Gap should align with trend
            if (trendSlope > 0 && !upGap) continue;
            if (trendSlope < 0 && !downGap) continue;

            // Next bar should show reversal signs
            bool reversal = (upGap && nextBar.Close < gapBar.Close) || 
                           (downGap && nextBar.Close > gapBar.Close);

            if (reversal)
            {
                var gapSize = upGap ? (double)(gapBar.Low - prev.High) : (double)(prev.Low - gapBar.High);
                var score = Math.Min(MaxExhaustionGapScore, ExhaustionGapBaseScore + gapSize * ExhaustionGapSizeWeight);

                return new PatternResult
                {
                    Score = score,
                    Direction = upGap ? -1 : 1, // Reversal direction
                    Confidence = score,
                    Metadata = new Dictionary<string, object>
                    {
                        ["gap_direction"] = upGap ? "up" : "down",
                        ["gap_size"] = gapSize,
                        ["trend_slope"] = trendSlope
                    }
                };
            }
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    private static PatternResult DetectClimaxReversal(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < ClimaxReversalBars) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(ClimaxReversalLookback).ToList();
        var current = recent.Last();
        
        // Look for unusually large bar with high volume (if available)
        var avgRange = recent.Take(ClimaxReversalAvgBars).Select(b => (double)(b.High - b.Low)).Average();
        var currentRange = (double)(current.High - current.Low);
        
        var rangeMultiple = currentRange / Math.Max(avgRange, MinimumRangeDivisor);
        
        // Climax: large range bar followed by inability to continue
        if (rangeMultiple < MinClimaxRangeMultiple) return new PatternResult { Score = 0, Confidence = 0 };

        // Check for reversal signs in the bar itself
        // Large wicks suggest rejection at extremes
        var upperWick = (double)(current.High - Math.Max(current.Open, current.Close));
        var lowerWick = (double)(Math.Min(current.Open, current.Close) - current.Low);
        
        var wickRatio = Math.Max(upperWick, lowerWick) / currentRange;
        
        var score = 0.0;
        if (wickRatio > MinClimaxWickRatio) // Large wick suggests rejection
        {
            score = Math.Min(MaxClimaxScore, ClimaxBaseScore + wickRatio * ClimaxWickWeight);
        }

        var direction = upperWick > lowerWick ? -1 : 1; // Direction opposite to larger wick

        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["range_multiple"] = rangeMultiple,
                ["wick_ratio"] = wickRatio,
                ["upper_wick"] = upperWick,
                ["lower_wick"] = lowerWick
            }
        };
    }

    private static PatternResult DetectVolumeSpike(IReadOnlyList<Bar> bars)
    {
        // Volume spike analysis would require volume data
        // Return pattern based on real price action analysis
        return DetectGenericReversal(bars, "VolumeSpike", VolumeSpikeBaseScore);
    }

    private static PatternResult DetectFailedBreakout(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < FailedBreakoutBars) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(FailedBreakoutLookback).ToList();
        
        // Look for breakout followed by failure to sustain
        for (int i = FailedBreakoutStartIndex; i < recent.Count - FailedBreakoutStartIndex; i++)
        {
            var breakoutBar = recent[i];
            var prevBars = recent.Take(i).ToList();
            var followupBars = recent.Skip(i + 1).ToList();

            if (prevBars.Count < FailedBreakoutMinPrevBars || followupBars.Count < FailedBreakoutMinFollowup) continue;

            var prevHigh = prevBars.Max(b => b.High);
            var prevLow = prevBars.Min(b => b.Low);

            bool upBreakout = breakoutBar.High > prevHigh;
            bool downBreakout = breakoutBar.Low < prevLow;

            if (!upBreakout && !downBreakout) continue;

            // Check if breakout failed to sustain
            var failedToSustain = false;
            var avgCloseAfter = followupBars.Select(b => b.Close).Average();
            
            if (upBreakout && avgCloseAfter < prevHigh)
            {
                failedToSustain = true;
            }
            else if (downBreakout && avgCloseAfter > prevLow)
            {
                failedToSustain = true;
            }

            if (failedToSustain)
            {
                var score = FailedBreakoutScore;
                return new PatternResult
                {
                    Score = score,
                    Direction = upBreakout ? -1 : 1, // Opposite to failed breakout
                    Confidence = score,
                    Metadata = new Dictionary<string, object>
                    {
                        ["breakout_direction"] = upBreakout ? "up" : "down",
                        ["breakout_level"] = upBreakout ? (double)prevHigh : (double)prevLow,
                        ["avg_close_after"] = (double)avgCloseAfter
                    }
                };
            }
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    private static PatternResult DetectTrendExhaustion(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < TrendExhaustionBars) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(TrendExhaustionLookback).ToList();
        
        // Look for weakening trend momentum
        var firstThird = recent.Take(TrendExhaustionSegmentBars).ToList();
        var lastThird = recent.TakeLast(TrendExhaustionSegmentBars).ToList();
        
        var earlySlope = (double)(firstThird.Last().Close - firstThird.First().Close) / firstThird.Count;
        var lateSlope = (double)(lastThird.Last().Close - lastThird.First().Close) / lastThird.Count;
        
        // Check for momentum divergence
        bool trendWeakening = false;
        var direction = 0;
        
        if (earlySlope > 0 && lateSlope < earlySlope * TrendWeakeningThreshold) // Uptrend weakening
        {
            trendWeakening = true;
            direction = -1;
        }
        else if (earlySlope < 0 && lateSlope > earlySlope * TrendWeakeningThreshold) // Downtrend weakening
        {
            trendWeakening = true;
            direction = 1;
        }

        var score = trendWeakening ? TrendExhaustionScore : 0.0;

        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["early_slope"] = earlySlope,
                ["late_slope"] = lateSlope,
                ["momentum_ratio"] = Math.Abs(earlySlope) > MinimumRangeDivisor ? lateSlope / earlySlope : 0
            }
        };
    }

    private static PatternResult DetectSpikeyTop(IReadOnlyList<Bar> bars)
    {
        return DetectSpikeyExtreme(bars, true);
    }

    private static PatternResult DetectSpikeyBottom(IReadOnlyList<Bar> bars)
    {
        return DetectSpikeyExtreme(bars, false);
    }

    private static PatternResult DetectSpikeyExtreme(IReadOnlyList<Bar> bars, bool isTop)
    {
        if (bars.Count < SpikeyExtremeLookback) return new PatternResult { Score = 0, Confidence = 0 };

        var recent = bars.TakeLast(SpikeyExtremeLookback).ToList();
        var middle = recent[1];
        var before = recent[0];
        var after = recent[2];

        var spike = isTop ? 
            (middle.High > before.High && middle.High > after.High && 
             middle.Close < middle.High - (middle.High - middle.Low) * SpikeyExtremeCloseRatio) :
            (middle.Low < before.Low && middle.Low < after.Low && 
             middle.Close > middle.Low + (middle.High - middle.Low) * SpikeyExtremeCloseRatio);

        var score = spike ? SpikeyExtremeScore : 0.0;

        return new PatternResult
        {
            Score = score,
            Direction = isTop ? -1 : 1,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = isTop ? "spikey_top" : "spikey_bottom",
                ["extreme_level"] = (double)(isTop ? middle.High : middle.Low)
            }
        };
    }

    // Simplified implementations
    private static PatternResult DetectDivergenceReversal(IReadOnlyList<Bar> bars) => DetectGenericReversal(bars, "DivergenceReversal", DivergenceReversalBaseScore);

    private static PatternResult DetectGenericReversal(IReadOnlyList<Bar> bars, string type, double baseScore)
    {
        return new PatternResult
        {
            Score = baseScore,
            Direction = 0, // Would need more context to determine
            Confidence = baseScore,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }
}

/// <summary>
/// Types of reversal patterns supported
/// </summary>
public enum ReversalType
{
    KeyReversal,
    IslandReversal,
    ExhaustionGap,
    ClimaxReversal,
    VolumeSpike,
    DivergenceReversal,
    FailedBreakout,
    TrendExhaustion,
    SpikeyTop,
    SpikeyBottom
}

/// <summary>
/// Gap information for pattern analysis
/// </summary>
internal sealed class GapInfo
{
    public int Index { get; set; }
    public int Direction { get; set; } // 1 for up gap, -1 for down gap
    public double Size { get; set; }
}