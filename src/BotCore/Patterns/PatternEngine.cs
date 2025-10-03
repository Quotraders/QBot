using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    
    // Pattern analysis constants
    private const double MIN_PATTERN_SCORE_THRESHOLD = 0.1;
    private const double ACTIVE_PATTERN_THRESHOLD = 0.3;
    private const double NEUTRAL_PATTERN_DIRECTION = 0.5;
    private const double CONFIDENCE_BOOST = 0.1;
    private const double NEUTRAL_SCORE = 0.5;
    private const double MINIMUM_SCORE_FOR_ANALYSIS = 0.01;

    public PatternEngine(ILogger<PatternEngine> logger, IFeatureBus featureBus, IEnumerable<IPatternDetector> detectors, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _featureBus = featureBus;
        _detectors = detectors.ToList();
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Analyze bars and return aggregated pattern scores
    /// </summary>
    public PatternScores GetScores(string symbol, IReadOnlyList<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        
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
                if (result.Score > MINIMUM_SCORE_FOR_ANALYSIS) // Only include meaningful detections
                {
                    results.Add(new PatternDetectionResult
                    {
                        PatternName = detector.PatternName,
                        Family = detector.Family,
                        Result = result
                    });

                    // Publish individual pattern metrics to feature bus
                    _featureBus.Publish(symbol, now, $"pattern.kind::{detector.PatternName}", (decimal)result.Score);
                    _featureBus.Publish(symbol, now, $"pattern.direction::{detector.PatternName}", (decimal)result.Direction);
                    _featureBus.Publish(symbol, now, $"pattern.confidence::{detector.PatternName}", (decimal)result.Confidence);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Pattern detector {PatternName} invalid operation for {Symbol}", detector.PatternName, symbol);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Pattern detector {PatternName} invalid argument for {Symbol}", detector.PatternName, symbol);
            }
        }

        // Aggregate scores
        var patternScores = AggregateScores(results);

        // Publish summary scores to feature bus  
        _featureBus.Publish(symbol, now, "pattern.bull_score", (decimal)patternScores.BullScore);
        _featureBus.Publish(symbol, now, "pattern.bear_score", (decimal)patternScores.BearScore);
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
        try
        {
            // Get recent bars from real market data
            var recentBars = await GetRecentBarsForAnalysisAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            if (recentBars == null || recentBars.Count == 0)
            {
                _logger.LogWarning("No recent bars available for pattern analysis of {Symbol}, using neutral scores", symbol);
                return new PatternScoresWithDetails
                {
                    BullScore = NEUTRAL_SCORE,
                    BearScore = NEUTRAL_SCORE,
                    OverallConfidence = 0.0,
                    DetectedPatterns = new List<PatternDetail>()
                };
            }

            // Use the existing synchronous GetScores method with real bar data
            var patternScores = GetScores(symbol, recentBars);
            
            // Convert to detailed format for async interface
            var detailsResult = new PatternScoresWithDetails
            {
                BullScore = patternScores.BullScore,
                BearScore = patternScores.BearScore,
                OverallConfidence = CalculateOverallConfidence(patternScores),
                DetectedPatterns = ConvertPatternFlagsToDetails(patternScores.PatternFlags)
            };
            
            // Publish pattern scores to feature bus
            var timestamp = DateTime.UtcNow;
            _featureBus.Publish(symbol, timestamp, "pattern.bull_score", (decimal)detailsResult.BullScore);
            _featureBus.Publish(symbol, timestamp, "pattern.bear_score", (decimal)detailsResult.BearScore);
            _featureBus.Publish(symbol, timestamp, "pattern.confidence", (decimal)detailsResult.OverallConfidence);

            return detailsResult;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation getting pattern scores for {Symbol}", symbol);
            
            // Return neutral scores on error
            return new PatternScoresWithDetails
            {
                BullScore = NEUTRAL_SCORE,
                BearScore = NEUTRAL_SCORE,
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
    
    /// <summary>
    /// Get recent bars for pattern analysis from real market data
    /// </summary>
    private async Task<IReadOnlyList<Bar>?> GetRecentBarsForAnalysisAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Get bars from real bar aggregator services
            var barAggregators = _serviceProvider.GetServices<BotCore.Market.BarAggregator>();
            foreach (var aggregator in barAggregators)
            {
                var history = aggregator.GetHistory(symbol);
                if (history.Count > 0)
                {
                    // Convert from Market.Bar to Models.Bar
                    var bars = history.Select(marketBar => new Bar
                    {
                        Start = marketBar.Start,
                        Ts = new DateTimeOffset(marketBar.Start).ToUnixTimeMilliseconds(),
                        Symbol = symbol,
                        Open = marketBar.Open,
                        High = marketBar.High,
                        Low = marketBar.Low,
                        Close = marketBar.Close,
                        Volume = (int)Math.Min(marketBar.Volume, int.MaxValue)
                    }).ToList();
                    
                    _logger.LogDebug("Retrieved {Count} real bars for pattern analysis of {Symbol}", bars.Count, symbol);
                    
                    await Task.CompletedTask.ConfigureAwait(false);
                    return bars;
                }
            }
            
            // Try to get from trading system bar consumer
            var barConsumer = _serviceProvider.GetService<BotCore.Services.TradingSystemBarConsumer>();
            if (barConsumer != null)
            {
                // The bar consumer doesn't expose a query interface, but we can try other sources
                _logger.LogDebug("Bar consumer available but no query interface for {Symbol}", symbol);
            }
            
            // If no bars available, we cannot perform pattern analysis
            _logger.LogWarning("No bar data available for pattern analysis of {Symbol}", symbol);
            return null;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid symbol argument for pattern analysis: {Symbol}", symbol);
            throw new InvalidOperationException($"Pattern analysis failed for symbol '{symbol}' due to invalid symbol argument", ex);
        }
        catch (InvalidOperationException ex) 
        {
            _logger.LogError(ex, "Pattern analysis service unavailable for {Symbol}", symbol);
            throw new InvalidOperationException($"Pattern analysis failed for symbol '{symbol}' due to service unavailability", ex);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout retrieving bar data for pattern analysis of {Symbol}", symbol);
            throw new InvalidOperationException($"Pattern analysis failed for symbol '{symbol}' due to data retrieval timeout", ex);
        }
    }
    
    /// <summary>
    /// Calculate overall confidence from pattern scores
    /// </summary>
    private static double CalculateOverallConfidence(PatternScores scores)
    {
        if (scores.PatternFlags.Count == 0)
            return 0.0;
            
        var averageScore = scores.PatternFlags.Values.Average();
        var scoreVariance = scores.PatternFlags.Values.Select(s => Math.Pow(s - averageScore, 2)).Average();
        var confidence = Math.Max(0.0, Math.Min(1.0, averageScore * (1.0 - Math.Sqrt(scoreVariance))));
        
        return confidence;
    }
    
    /// <summary>
    /// Convert pattern flags dictionary to detailed pattern list
    /// </summary>
    private static List<PatternDetail> ConvertPatternFlagsToDetails(Dictionary<string, double> patternFlags)
    {
        var details = new List<PatternDetail>();
        
        foreach (var kvp in patternFlags)
        {
            var score = kvp.Value;
            if (score > MIN_PATTERN_SCORE_THRESHOLD) // Only include meaningful patterns
            {
                details.Add(new PatternDetail
                {
                    Name = kvp.Key,
                    Score = score,
                    IsActive = score > ACTIVE_PATTERN_THRESHOLD,
                    Direction = score > NEUTRAL_PATTERN_DIRECTION ? 1 : -1, // Simplified direction mapping
                    Confidence = Math.Min(1.0, score + CONFIDENCE_BOOST)
                });
            }
        }
        
        return details;
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
    
    /// <summary>
    /// True if this pattern indicates bullish direction
    /// </summary>
    public bool IsBullish => Direction > 0;
    
    /// <summary>
    /// True if this pattern indicates bearish direction
    /// </summary>
    public bool IsBearish => Direction < 0;
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