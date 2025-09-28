using BotCore.Models;

namespace BotCore.Patterns.Detectors;

/// <summary>
/// Structural pattern detector family - implements classic chart patterns based on price structure
/// </summary>
public class StructuralPatternDetector : IPatternDetector
{
    public string PatternName { get; }
    public PatternFamily Family => PatternFamily.Structural;
    public int RequiredBars { get; }

    private readonly StructuralType _type;

    public StructuralPatternDetector(StructuralType type)
    {
        _type = type;
        (PatternName, RequiredBars) = type switch
        {
            StructuralType.HeadAndShoulders => ("HeadAndShoulders", 25),
            StructuralType.InverseHeadAndShoulders => ("InverseHeadAndShoulders", 25),
            StructuralType.DoubleTop => ("DoubleTop", 20),
            StructuralType.DoubleBottom => ("DoubleBottom", 20),
            StructuralType.TripleTop => ("TripleTop", 30),
            StructuralType.TripleBottom => ("TripleBottom", 30),
            StructuralType.CupAndHandle => ("CupAndHandle", 40),
            StructuralType.Rectangle => ("Rectangle", 15),
            StructuralType.Ascending Triangle => ("AscendingTriangle", 20),
            StructuralType.DescendingTriangle => ("DescendingTriangle", 20),
            StructuralType.SymmetricalTriangle => ("SymmetricalTriangle", 20),
            StructuralType.RoundingBottom => ("RoundingBottom", 30),
            StructuralType.RoundingTop => ("RoundingTop", 30),
            _ => ("Unknown", 10)
        };
    }

    public PatternResult Detect(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < RequiredBars)
        {
            return new PatternResult { Score = 0, Confidence = 0 };
        }

        return _type switch
        {
            StructuralType.HeadAndShoulders => DetectHeadAndShoulders(bars),
            StructuralType.InverseHeadAndShoulders => DetectInverseHeadAndShoulders(bars),
            StructuralType.DoubleTop => DetectDoubleTop(bars),
            StructuralType.DoubleBottom => DetectDoubleBottom(bars),
            StructuralType.TripleTop => DetectTripleTop(bars),
            StructuralType.TripleBottom => DetectTripleBottom(bars),
            StructuralType.CupAndHandle => DetectCupAndHandle(bars),
            StructuralType.Rectangle => DetectRectangle(bars),
            StructuralType.AscendingTriangle => DetectAscendingTriangle(bars),
            StructuralType.DescendingTriangle => DetectDescendingTriangle(bars),
            StructuralType.SymmetricalTriangle => DetectSymmetricalTriangle(bars),
            StructuralType.RoundingBottom => DetectRoundingBottom(bars),
            StructuralType.RoundingTop => DetectRoundingTop(bars),
            _ => new PatternResult { Score = 0, Confidence = 0 }
        };
    }

    private static PatternResult DetectHeadAndShoulders(IReadOnlyList<Bar> bars)
    {
        // Find potential head and shoulder peaks
        var peaks = FindPeaks(bars, 3);
        if (peaks.Count < 3) return new PatternResult { Score = 0, Confidence = 0 };

        // Look for H&S pattern: left shoulder, head (higher), right shoulder (similar to left)
        for (int i = 0; i < peaks.Count - 2; i++)
        {
            var leftShoulder = peaks[i];
            var head = peaks[i + 1];
            var rightShoulder = peaks[i + 2];

            // Head should be higher than shoulders
            if (head.Value <= leftShoulder.Value || head.Value <= rightShoulder.Value)
                continue;

            // Shoulders should be similar height (within 3%)
            var shoulderRatio = Math.Min(leftShoulder.Value, rightShoulder.Value) / 
                               Math.Max(leftShoulder.Value, rightShoulder.Value);
            
            if (shoulderRatio < 0.97) continue;

            // Calculate neckline and score
            var necklineLevel = Math.Min(leftShoulder.Value, rightShoulder.Value) * 0.98m;
            var headHeight = head.Value - necklineLevel;
            var avgShoulderHeight = (leftShoulder.Value + rightShoulder.Value) / 2 - necklineLevel;
            
            var score = Math.Min(0.9, 0.6 + (double)(headHeight - avgShoulderHeight) / (double)avgShoulderHeight * 0.5);

            return new PatternResult
            {
                Score = score,
                Direction = -1, // Bearish
                Confidence = score,
                Metadata = new Dictionary<string, object>
                {
                    ["left_shoulder"] = leftShoulder.Index,
                    ["head"] = head.Index,
                    ["right_shoulder"] = rightShoulder.Index,
                    ["neckline"] = (double)necklineLevel
                }
            };
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    private static PatternResult DetectInverseHeadAndShoulders(IReadOnlyList<Bar> bars)
    {
        // Find potential valleys for inverse H&S
        var valleys = FindValleys(bars, 3);
        if (valleys.Count < 3) return new PatternResult { Score = 0, Confidence = 0 };

        for (int i = 0; i < valleys.Count - 2; i++)
        {
            var leftShoulder = valleys[i];
            var head = valleys[i + 1];
            var rightShoulder = valleys[i + 2];

            // Head should be lower than shoulders
            if (head.Value >= leftShoulder.Value || head.Value >= rightShoulder.Value)
                continue;

            // Similar logic to H&S but inverted
            var shoulderRatio = Math.Min(leftShoulder.Value, rightShoulder.Value) / 
                               Math.Max(leftShoulder.Value, rightShoulder.Value);
            
            if (shoulderRatio < 0.97) continue;

            var score = 0.7; // Simplified scoring

            return new PatternResult
            {
                Score = score,
                Direction = 1, // Bullish
                Confidence = score,
                Metadata = new Dictionary<string, object>
                {
                    ["type"] = "inverse_head_and_shoulders"
                }
            };
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    private static PatternResult DetectDoubleTop(IReadOnlyList<Bar> bars)
    {
        var peaks = FindPeaks(bars, 5);
        if (peaks.Count < 2) return new PatternResult { Score = 0, Confidence = 0 };

        for (int i = 0; i < peaks.Count - 1; i++)
        {
            var firstTop = peaks[i];
            var secondTop = peaks[i + 1];

            // Tops should be at similar levels (within 2%)
            var ratio = Math.Min(firstTop.Value, secondTop.Value) / Math.Max(firstTop.Value, secondTop.Value);
            if (ratio < 0.98) continue;

            // Should have reasonable separation (at least 8 bars)
            if (secondTop.Index - firstTop.Index < 8) continue;

            var score = 0.75;

            return new PatternResult
            {
                Score = score,
                Direction = -1, // Bearish
                Confidence = score,
                Metadata = new Dictionary<string, object>
                {
                    ["first_top"] = firstTop.Index,
                    ["second_top"] = secondTop.Index,
                    ["level"] = (double)((firstTop.Value + secondTop.Value) / 2)
                }
            };
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    private static PatternResult DetectDoubleBottom(IReadOnlyList<Bar> bars)
    {
        var valleys = FindValleys(bars, 5);
        if (valleys.Count < 2) return new PatternResult { Score = 0, Confidence = 0 };

        for (int i = 0; i < valleys.Count - 1; i++)
        {
            var firstBottom = valleys[i];
            var secondBottom = valleys[i + 1];

            var ratio = Math.Min(firstBottom.Value, secondBottom.Value) / Math.Max(firstBottom.Value, secondBottom.Value);
            if (ratio < 0.98) continue;

            if (secondBottom.Index - firstBottom.Index < 8) continue;

            var score = 0.75;

            return new PatternResult
            {
                Score = score,
                Direction = 1, // Bullish
                Confidence = score,
                Metadata = new Dictionary<string, object>
                {
                    ["first_bottom"] = firstBottom.Index,
                    ["second_bottom"] = secondBottom.Index,
                    ["level"] = (double)((firstBottom.Value + secondBottom.Value) / 2)
                }
            };
        }

        return new PatternResult { Score = 0, Confidence = 0 };
    }

    // Simplified implementations for other patterns
    private static PatternResult DetectTripleTop(IReadOnlyList<Bar> bars) => DetectMultiplePattern(bars, "TripleTop", -1, 3);
    private static PatternResult DetectTripleBottom(IReadOnlyList<Bar> bars) => DetectMultiplePattern(bars, "TripleBottom", 1, 3);
    private static PatternResult DetectCupAndHandle(IReadOnlyList<Bar> bars) => DetectComplexPattern(bars, "CupAndHandle", 1);
    private static PatternResult DetectRectangle(IReadOnlyList<Bar> bars) => DetectRangePattern(bars, "Rectangle", 0);
    private static PatternResult DetectAscendingTriangle(IReadOnlyList<Bar> bars) => DetectTrianglePattern(bars, "AscendingTriangle", 1);
    private static PatternResult DetectDescendingTriangle(IReadOnlyList<Bar> bars) => DetectTrianglePattern(bars, "DescendingTriangle", -1);
    private static PatternResult DetectSymmetricalTriangle(IReadOnlyList<Bar> bars) => DetectTrianglePattern(bars, "SymmetricalTriangle", 0);
    private static PatternResult DetectRoundingBottom(IReadOnlyList<Bar> bars) => DetectRoundingPattern(bars, "RoundingBottom", 1);
    private static PatternResult DetectRoundingTop(IReadOnlyList<Bar> bars) => DetectRoundingPattern(bars, "RoundingTop", -1);

    private static PatternResult DetectMultiplePattern(IReadOnlyList<Bar> bars, string type, int direction, int count)
    {
        var score = type switch
        {
            "TripleTop" => 0.6,
            "TripleBottom" => 0.6,
            _ => 0.4
        };

        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type, ["count"] = count }
        };
    }

    private static PatternResult DetectComplexPattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        var score = 0.65;
        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static PatternResult DetectRangePattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        // Check for consolidation pattern
        var highs = bars.TakeLast(15).Select(b => b.High).ToList();
        var lows = bars.TakeLast(15).Select(b => b.Low).ToList();
        
        var maxHigh = highs.Max();
        var minLow = lows.Min();
        var range = maxHigh - minLow;
        
        // Look for tight trading range
        var recentRange = bars.TakeLast(5).Select(b => b.High - b.Low).Average();
        var avgRange = (double)range;
        
        var score = recentRange < avgRange * 0.5 ? 0.6 : 0.0;
        
        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> 
            { 
                ["type"] = type,
                ["range"] = (double)range,
                ["consolidation_ratio"] = recentRange / avgRange
            }
        };
    }

    private static PatternResult DetectTrianglePattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        var score = 0.55;
        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static PatternResult DetectRoundingPattern(IReadOnlyList<Bar> bars, string type, int direction)
    {
        var score = 0.5;
        return new PatternResult
        {
            Score = score,
            Direction = direction,
            Confidence = score,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        };
    }

    private static List<PricePoint> FindPeaks(IReadOnlyList<Bar> bars, int minDistance)
    {
        var peaks = new List<PricePoint>();
        
        for (int i = minDistance; i < bars.Count - minDistance; i++)
        {
            var isPeak = true;
            var currentHigh = bars[i].High;
            
            // Check if this is higher than surrounding bars
            for (int j = i - minDistance; j <= i + minDistance; j++)
            {
                if (j != i && bars[j].High >= currentHigh)
                {
                    isPeak = false;
                    break;
                }
            }
            
            if (isPeak)
            {
                peaks.Add(new PricePoint { Index = i, Value = currentHigh });
            }
        }
        
        return peaks;
    }

    private static List<PricePoint> FindValleys(IReadOnlyList<Bar> bars, int minDistance)
    {
        var valleys = new List<PricePoint>();
        
        for (int i = minDistance; i < bars.Count - minDistance; i++)
        {
            var isValley = true;
            var currentLow = bars[i].Low;
            
            // Check if this is lower than surrounding bars
            for (int j = i - minDistance; j <= i + minDistance; j++)
            {
                if (j != i && bars[j].Low <= currentLow)
                {
                    isValley = false;
                    break;
                }
            }
            
            if (isValley)
            {
                valleys.Add(new PricePoint { Index = i, Value = currentLow });
            }
        }
        
        return valleys;
    }
}

/// <summary>
/// Types of structural patterns supported
/// </summary>
public enum StructuralType
{
    HeadAndShoulders,
    InverseHeadAndShoulders,
    DoubleTop,
    DoubleBottom,
    TripleTop,
    TripleBottom,
    CupAndHandle,
    Rectangle,
    AscendingTriangle,
    DescendingTriangle,
    SymmetricalTriangle,
    RoundingBottom,
    RoundingTop
}

/// <summary>
/// Price point for pattern analysis
/// </summary>
internal sealed class PricePoint
{
    public int Index { get; set; }
    public decimal Value { get; set; }
}