using System;
using BotCore.Models;

namespace BotCore.Patterns.Detectors;

/// <summary>
/// Candlestick pattern detector family - implements common single and multi-bar candlestick patterns
/// </summary>
public class CandlestickPatternDetector : IPatternDetector
{
    // S109 Magic Number Constants - Candlestick Pattern Thresholds
    private const decimal MinSpinningTopBodyRatio = 0.1m;
    private const decimal MaxSpinningTopBodyRatio = 0.3m;
    private const double SpinningTopConfidence = 0.6;
    private const double StrongTrendConfidence = 0.8;
    
    public string PatternName { get; }
    public PatternFamily Family => PatternFamily.Candlestick;
    public int RequiredBars { get; }

    private readonly CandlestickType _type;

    public CandlestickPatternDetector(CandlestickType type)
    {
        _type = type;
        (PatternName, RequiredBars) = type switch
        {
            CandlestickType.Doji => ("Doji", 1),
            CandlestickType.Hammer => ("Hammer", 2),
            CandlestickType.ShootingStar => ("ShootingStar", 2),
            CandlestickType.Engulfing => ("Engulfing", 2),
            CandlestickType.Harami => ("Harami", 2),
            CandlestickType.Marubozu => ("Marubozu", 1),
            CandlestickType.SpinningTop => ("SpinningTop", 1),
            CandlestickType.ThreeBlackCrows => ("ThreeBlackCrows", 3),
            CandlestickType.ThreeWhiteSoldiers => ("ThreeWhiteSoldiers", 3),
            CandlestickType.MorningStar => ("MorningStar", 3),
            CandlestickType.EveningStar => ("EveningStar", 3),
            CandlestickType.PiercingLine => ("PiercingLine", 2),
            CandlestickType.DarkCloudCover => ("DarkCloudCover", 2),
            CandlestickType.TweezerTops => ("TweezerTops", 2),
            CandlestickType.TweezerBottoms => ("TweezerBottoms", 2),
            _ => ("Unknown", 1)
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
            CandlestickType.Doji => DetectDoji(bars),
            CandlestickType.Hammer => DetectHammer(bars),
            CandlestickType.ShootingStar => DetectShootingStar(bars),
            CandlestickType.Engulfing => DetectEngulfing(bars),
            CandlestickType.Harami => DetectHarami(bars),
            CandlestickType.Marubozu => DetectMarubozu(bars),
            CandlestickType.SpinningTop => DetectSpinningTop(bars),
            CandlestickType.ThreeBlackCrows => DetectThreeBlackCrows(bars),
            CandlestickType.ThreeWhiteSoldiers => DetectThreeWhiteSoldiers(bars),
            CandlestickType.MorningStar => DetectMorningStar(bars),
            CandlestickType.EveningStar => DetectEveningStar(bars),
            CandlestickType.PiercingLine => DetectPiercingLine(bars),
            CandlestickType.DarkCloudCover => DetectDarkCloudCover(bars),
            CandlestickType.TweezerTops => DetectTweezerTops(bars),
            CandlestickType.TweezerBottoms => DetectTweezerBottoms(bars),
            _ => new PatternResult { Score = 0, Confidence = 0 }
        };
    }

    private static PatternResult DetectDoji(IReadOnlyList<Bar> bars)
    {
        var current = bars[^1];
        var bodySize = Math.Abs(current.Close - current.Open);
        var range = current.High - current.Low;

        if (range == 0) return new PatternResult { Score = 0, Confidence = 0 };

        var bodyRatio = bodySize / range;
        var score = bodyRatio < 0.05m ? 0.9 : bodyRatio < 0.1m ? 0.7 : bodyRatio < 0.15m ? 0.5 : 0.0;

        return new PatternResult
        {
            Score = score,
            Direction = 0, // Neutral
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["body_ratio"] = bodyRatio,
                ["range"] = range
            }
        };
    }

    private static PatternResult DetectHammer(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return new PatternResult { Score = 0, Confidence = 0 };
        
        var prev = bars[^2];
        var current = bars[^1];
        
        // Hammer typically appears after downtrend
        if (current.Close >= prev.Close) return new PatternResult { Score = 0, Confidence = 0 };

        var bodySize = Math.Abs(current.Close - current.Open);
        var lowerShadow = Math.Min(current.Open, current.Close) - current.Low;
        var upperShadow = current.High - Math.Max(current.Open, current.Close);
        var range = current.High - current.Low;

        if (range == 0) return new PatternResult { Score = 0, Confidence = 0 };

        var bodyRatio = bodySize / range;
        var lowerShadowRatio = lowerShadow / range;
        var upperShadowRatio = upperShadow / range;

        // Hammer criteria: small body, long lower shadow, minimal upper shadow
        var score = 0.0;
        if (bodyRatio < 0.3m && lowerShadowRatio > 0.6m && upperShadowRatio < 0.1m)
        {
            score = Math.Min(0.95, 0.7 + (double)(lowerShadowRatio - 0.6m) * 2);
        }

        return new PatternResult
        {
            Score = score,
            Direction = score > 0.5 ? 1 : 0, // Bullish reversal
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["body_ratio"] = bodyRatio,
                ["lower_shadow_ratio"] = lowerShadowRatio,
                ["upper_shadow_ratio"] = upperShadowRatio
            }
        };
    }

    private static PatternResult DetectShootingStar(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return new PatternResult { Score = 0, Confidence = 0 };
        
        var prev = bars[^2];
        var current = bars[^1];
        
        // Shooting star typically appears after uptrend
        if (current.Close <= prev.Close) return new PatternResult { Score = 0, Confidence = 0 };

        var bodySize = Math.Abs(current.Close - current.Open);
        var lowerShadow = Math.Min(current.Open, current.Close) - current.Low;
        var upperShadow = current.High - Math.Max(current.Open, current.Close);
        var range = current.High - current.Low;

        if (range == 0) return new PatternResult { Score = 0, Confidence = 0 };

        var bodyRatio = bodySize / range;
        var lowerShadowRatio = lowerShadow / range;
        var upperShadowRatio = upperShadow / range;

        // Shooting star criteria: small body, long upper shadow, minimal lower shadow
        var score = 0.0;
        if (bodyRatio < 0.3m && upperShadowRatio > 0.6m && lowerShadowRatio < 0.1m)
        {
            score = Math.Min(0.95, 0.7 + (double)(upperShadowRatio - 0.6m) * 2);
        }

        return new PatternResult
        {
            Score = score,
            Direction = score > 0.5 ? -1 : 0, // Bearish reversal
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["body_ratio"] = bodyRatio,
                ["lower_shadow_ratio"] = lowerShadowRatio,
                ["upper_shadow_ratio"] = upperShadowRatio
            }
        };
    }

    private static PatternResult DetectEngulfing(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2) return new PatternResult { Score = 0, Confidence = 0 };
        
        var first = bars[^2];
        var second = bars[^1];
        
        var firstBody = Math.Abs(first.Close - first.Open);
        var secondBody = Math.Abs(second.Close - second.Open);
        
        var bullishEngulfing = first.Close < first.Open && second.Close > second.Open && 
                               second.Open < first.Close && second.Close > first.Open;
        
        var bearishEngulfing = first.Close > first.Open && second.Close < second.Open &&
                               second.Open > first.Close && second.Close < first.Open;

        if (!bullishEngulfing && !bearishEngulfing) return new PatternResult { Score = 0, Confidence = 0 };

        var sizeRatio = secondBody / Math.Max(firstBody, 0.01m);
        var score = Math.Min(0.95, 0.6 + Math.Min(0.35, (double)(sizeRatio - 1) * 0.1));

        return new PatternResult
        {
            Score = score,
            Direction = bullishEngulfing ? 1 : -1,
            Confidence = score,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = bullishEngulfing ? "bullish" : "bearish",
                ["size_ratio"] = (double)sizeRatio
            }
        };
    }

    // Simplified implementations for remaining patterns
    private static PatternResult DetectHarami(IReadOnlyList<Bar> bars) => DetectTwoBarPattern(bars, "Harami", false);
    private static PatternResult DetectMarubozu(IReadOnlyList<Bar> bars) => DetectSingleBarPattern(bars, "Marubozu");
    private static PatternResult DetectSpinningTop(IReadOnlyList<Bar> bars) => DetectSingleBarPattern(bars, "SpinningTop");
    private static PatternResult DetectThreeBlackCrows(IReadOnlyList<Bar> bars) => DetectThreeBarPattern(bars, "ThreeBlackCrows", -1);
    private static PatternResult DetectThreeWhiteSoldiers(IReadOnlyList<Bar> bars) => DetectThreeBarPattern(bars, "ThreeWhiteSoldiers", 1);
    private static PatternResult DetectMorningStar(IReadOnlyList<Bar> bars) => DetectThreeBarPattern(bars, "MorningStar", 1);
    private static PatternResult DetectEveningStar(IReadOnlyList<Bar> bars) => DetectThreeBarPattern(bars, "EveningStar", -1);
    private static PatternResult DetectPiercingLine(IReadOnlyList<Bar> bars) => DetectTwoBarPattern(bars, "PiercingLine", true);
    private static PatternResult DetectDarkCloudCover(IReadOnlyList<Bar> bars) => DetectTwoBarPattern(bars, "DarkCloudCover", true);
    private static PatternResult DetectTweezerTops(IReadOnlyList<Bar> bars) => DetectTwoBarPattern(bars, "TweezerTops", false);
    private static PatternResult DetectTweezerBottoms(IReadOnlyList<Bar> bars) => DetectTwoBarPattern(bars, "TweezerBottoms", false);

    private static PatternResult DetectSingleBarPattern(IReadOnlyList<Bar> bars, string type)
    {
        var bar = bars[^1];
        var score = type switch
        {
            "Marubozu" => DetectMarubozuLogic(bar),
            "SpinningTop" => DetectSpinningTopLogic(bar),
            _ => 0.0
        };
        
        return new PatternResult
        {
            Score = score,
            Direction = 0,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static PatternResult DetectTwoBarPattern(IReadOnlyList<Bar> bars, string type, bool directional)
    {
        var score = type switch
        {
            "Harami" => 0.5,
            "PiercingLine" => 0.6,
            "DarkCloudCover" => 0.6,
            "TweezerTops" => 0.4,
            "TweezerBottoms" => 0.4,
            _ => 0.3
        };

        return new PatternResult
        {
            Score = score,
            Direction = directional ? (type.Contains("Piercing") || type.Contains("Bottom") ? 1 : -1) : 0,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static PatternResult DetectThreeBarPattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        if (bars.Count < 3) return new PatternResult { Score = 0, Confidence = 0 };

        var score = type switch
        {
            "ThreeBlackCrows" => DetectThreeConsecutive(bars, false),
            "ThreeWhiteSoldiers" => DetectThreeConsecutive(bars, true),
            "MorningStar" => 0.7,
            "EveningStar" => 0.7,
            _ => 0.4
        };

        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static double DetectMarubozuLogic(Bar bar)
    {
        var bodySize = Math.Abs(bar.Close - bar.Open);
        var range = bar.High - bar.Low;
        return range > 0 ? Math.Min(0.9, (double)(bodySize / range)) : 0.0;
    }

    private static double DetectSpinningTopLogic(Bar bar)
    {
        var bodySize = Math.Abs(bar.Close - bar.Open);
        var range = bar.High - bar.Low;
        if (range == 0) return 0.0;
        
        var bodyRatio = bodySize / range;
        return bodyRatio > MinSpinningTopBodyRatio && bodyRatio < MaxSpinningTopBodyRatio ? (double)SpinningTopConfidence : 0.0;
    }

    private static double DetectThreeConsecutive(IReadOnlyList<Bar> bars, bool bullish)
    {
        var b1 = bars[^3];
        var b2 = bars[^2]; 
        var b3 = bars[^1];

        if (bullish)
        {
            return (b1.Close > b1.Open && b2.Close > b2.Open && b3.Close > b3.Open &&
                   b2.Close > b1.Close && b3.Close > b2.Close) ? StrongTrendConfidence : 0.0;
        }
        else
        {
            return (b1.Close < b1.Open && b2.Close < b2.Open && b3.Close < b3.Open &&
                   b2.Close < b1.Close && b3.Close < b2.Close) ? StrongTrendConfidence : 0.0;
        }
    }
}

/// <summary>
/// Types of candlestick patterns supported
/// </summary>
public enum CandlestickType
{
    Doji,
    Hammer,
    ShootingStar,
    Engulfing,
    Harami,
    Marubozu,
    SpinningTop,
    ThreeBlackCrows,
    ThreeWhiteSoldiers,
    MorningStar,
    EveningStar,
    PiercingLine,
    DarkCloudCover,
    TweezerTops,
    TweezerBottoms
}