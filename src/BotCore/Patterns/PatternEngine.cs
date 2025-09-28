using BotCore.Models;
using BotCore.Services;
using Microsoft.Extensions.Logging;

namespace BotCore.Patterns;

/// <summary>
/// Pattern detection engine that manages all pattern detectors and aggregates their results
/// </summary>
public class PatternEngine
{
    private readonly ILogger<PatternEngine> _logger;
    private readonly IFeatureBus _featureBus;
    private readonly List<IPatternDetector> _detectors;

    public PatternEngine(ILogger<PatternEngine> logger, IFeatureBus featureBus, IEnumerable<IPatternDetector> detectors)
    {
        _logger = logger;
        _featureBus = featureBus;
        _detectors = detectors.ToList();
    }

    /// <summary>
    /// Analyze bars and return aggregated pattern scores
    /// </summary>
    public PatternScores GetScores(string symbol, IReadOnlyList<Bar> bars)
    {
        if (bars.Count == 0)
        {
            return new PatternScores();
        }

        var results = new List<PatternDetectionResult>();
        var now = DateTime.UtcNow;

        // Run all pattern detectors
        foreach (var detector in _detectors)
        {
            try
            {
                if (bars.Count < detector.RequiredBars)
                {
                    continue;
                }

                var result = detector.Detect(bars);
                if (result.Score > 0.01) // Only include meaningful detections
                {
                    results.Add(new PatternDetectionResult
                    {
                        PatternName = detector.PatternName,
                        Family = detector.Family,
                        Result = result
                    });

                    // Publish individual pattern metrics to feature bus
                    _featureBus.Publish(symbol, now, $"pattern.kind::{detector.PatternName}", result.Score);
                    _featureBus.Publish(symbol, now, $"pattern.direction::{detector.PatternName}", result.Direction);
                    _featureBus.Publish(symbol, now, $"pattern.confidence::{detector.PatternName}", result.Confidence);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pattern detector {PatternName} failed for {Symbol}", detector.PatternName, symbol);
            }
        }

        // Aggregate scores
        var patternScores = AggregateScores(results);

        // Publish summary scores to feature bus  
        _featureBus.Publish(symbol, now, "pattern.bull_score", patternScores.BullScore);
        _featureBus.Publish(symbol, now, "pattern.bear_score", patternScores.BearScore);
        _featureBus.Publish(symbol, now, "pattern.total_count", patternScores.PatternFlags.Count);

        _logger.LogDebug("Pattern analysis for {Symbol}: Bull={BullScore:F3}, Bear={BearScore:F3}, Patterns={Count}",
            symbol, patternScores.BullScore, patternScores.BearScore, results.Count);

        return patternScores;
    }

    private static PatternScores AggregateScores(List<PatternDetectionResult> results)
    {
        var scores = new PatternScores();
        
        foreach (var result in results)
        {
            // Add to pattern flags
            scores.PatternFlags[result.PatternName] = result.Result.Score;
            
            // Aggregate directional scores
            if (result.Result.Direction > 0) // Bullish
            {
                scores.BullScore += result.Result.Score * result.Result.Confidence;
            }
            else if (result.Result.Direction < 0) // Bearish
            {
                scores.BearScore += result.Result.Score * result.Result.Confidence;
            }
        }

        // Normalize scores to 0-1 range
        var maxScore = Math.Max(scores.BullScore, scores.BearScore);
        if (maxScore > 1.0)
        {
            scores.BullScore /= maxScore;
            scores.BearScore /= maxScore;
        }

        return scores;
    }
}

/// <summary>
/// Aggregated pattern analysis results
/// </summary>
public class PatternScores
{
    /// <summary>
    /// Aggregated bullish pattern score
    /// </summary>
    public double BullScore { get; set; }
    
    /// <summary>
    /// Aggregated bearish pattern score
    /// </summary>
    public double BearScore { get; set; }
    
    /// <summary>
    /// Individual pattern flags (pattern name -> score)
    /// </summary>
    public Dictionary<string, double> PatternFlags { get; set; } = new();
}

/// <summary>
/// Internal pattern detection result
/// </summary>
internal class PatternDetectionResult
{
    public string PatternName { get; set; } = string.Empty;
    public PatternFamily Family { get; set; }
    public PatternResult Result { get; set; } = new();
}