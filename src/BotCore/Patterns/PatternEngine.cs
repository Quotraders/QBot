using BotCore.Models;
using BotCore.Services;
using Microsoft.Extensions.Logging;
using Zones;

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

    /// <summary>
    /// Get current pattern scores asynchronously for feature probe integration
    /// </summary>
    public async Task<PatternScoresWithDetails> GetCurrentScoresAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // In production, this would get recent bars from the bar registry or market data service
        // For now, simulate with basic pattern detection
        try
        {
            var patternScores = new PatternScoresWithDetails
            {
                BullScore = 0.5,
                BearScore = 0.5,
                OverallConfidence = 0.7,
                DetectedPatterns = new List<PatternDetail>()
            };

            // Simulate pattern detection results
            var random = new Random();
            var patternNames = new[] { "Doji", "Hammer", "DoubleTop", "BullFlag", "KeyReversal" };
            
            foreach (var patternName in patternNames)
            {
                if (random.NextDouble() > 0.6) // 40% chance each pattern is detected
                {
                    var score = Math.Max(0.1, random.NextDouble());
                    var direction = random.NextDouble() > 0.5 ? 1 : -1;
                    
                    patternScores.DetectedPatterns.Add(new PatternDetail
                    {
                        Name = patternName,
                        Score = score,
                        IsActive = score > 0.3,
                        Direction = direction,
                        Confidence = Math.Min(1.0, score + 0.2)
                    });

                    // Update aggregate scores
                    if (direction > 0)
                        patternScores.BullScore += score * 0.2;
                    else
                        patternScores.BearScore += score * 0.2;
                }
            }

            // Normalize scores
            var maxScore = Math.Max(patternScores.BullScore, patternScores.BearScore);
            if (maxScore > 1.0)
            {
                patternScores.BullScore /= maxScore;
                patternScores.BearScore /= maxScore;
            }

            return await Task.FromResult(patternScores);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current pattern scores for {Symbol}", symbol);
            
            // Return neutral scores on error
            return new PatternScoresWithDetails
            {
                BullScore = 0.5,
                BearScore = 0.5,
                OverallConfidence = 0.0,
                DetectedPatterns = new List<PatternDetail>()
            };
        }
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
/// Extended pattern scores with detailed pattern information for feature probe
/// </summary>
public sealed class PatternScoresWithDetails
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
    /// Overall confidence in pattern detection
    /// </summary>
    public double OverallConfidence { get; set; }
    
    /// <summary>
    /// Detailed information about detected patterns
    /// </summary>
    public List<PatternDetail> DetectedPatterns { get; set; } = new();
}

/// <summary>
/// Detailed information about a single detected pattern
/// </summary>
public sealed class PatternDetail
{
    public string Name { get; set; } = string.Empty;
    public double Score { get; set; }
    public bool IsActive { get; set; }
    public int Direction { get; set; } // 1 = bullish, -1 = bearish, 0 = neutral
    public double Confidence { get; set; }
}

/// <summary>
/// Internal pattern detection result
/// </summary>
internal sealed class PatternDetectionResult
{
    public string PatternName { get; set; } = string.Empty;
    public PatternFamily Family { get; set; }
    public PatternResult Result { get; set; } = new();
}