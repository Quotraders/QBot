using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using BotCore.Models;
using BotCore.Services;
using BotCore.Health;
using Microsoft.Extensions.Logging;
using Zones;

namespace BotCore.Patterns;

/// <summary>
/// Pattern detection engine that manages all pattern detectors and aggregates their results
/// </summary>
public class PatternEngine : IComponentHealth
{
    private readonly ILogger<PatternEngine> _logger;
    private readonly IFeatureBus _featureBus;
    private readonly List<IPatternDetector> _detectors;
    private readonly IServiceProvider _serviceProvider;
    
    public string ComponentName => "PatternEngine";
    
    // Pattern analysis constants
    private const double MIN_PATTERN_SCORE_THRESHOLD = 0.1;
    private const double ACTIVE_PATTERN_THRESHOLD = 0.3;
    private const double NEUTRAL_PATTERN_DIRECTION = 0.5;
    private const double CONFIDENCE_BOOST = 0.1;
    private const double NEUTRAL_SCORE = 0.5;
    private const double MINIMUM_SCORE_FOR_ANALYSIS = 0.01;

    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, string, Exception> LogPatternDetectorWarning =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(6201, nameof(LogPatternDetectorWarning)),
            "Pattern detector {PatternName} invalid operation for {Symbol}");
    
    private static readonly Action<ILogger, string, string, Exception> LogPatternDetectorArgumentError =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(6202, nameof(LogPatternDetectorArgumentError)),
            "Pattern detector {PatternName} invalid argument for {Symbol}");
    
    private static readonly Action<ILogger, string, double, double, int, Exception?> LogPatternAnalysis =
        LoggerMessage.Define<string, double, double, int>(
            LogLevel.Debug,
            new EventId(6203, nameof(LogPatternAnalysis)),
            "Pattern analysis for {Symbol}: Bull={BullScore:F3}, Bear={BearScore:F3}, Patterns={Count}");
    
    private static readonly Action<ILogger, string, Exception?> LogNoBarsAvailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6204, nameof(LogNoBarsAvailable)),
            "No recent bars available for pattern analysis of {Symbol}, using neutral scores");
    
    private static readonly Action<ILogger, string, Exception> LogGetScoresError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6205, nameof(LogGetScoresError)),
            "Invalid operation getting pattern scores for {Symbol}");
    
    private static readonly Action<ILogger, int, string, Exception?> LogRetrievedBars =
        LoggerMessage.Define<int, string>(
            LogLevel.Debug,
            new EventId(6206, nameof(LogRetrievedBars)),
            "Retrieved {Count} real bars for pattern analysis of {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogBarConsumerNoQuery =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(6207, nameof(LogBarConsumerNoQuery)),
            "Bar consumer available but no query interface for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogNoBarDataAvailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6208, nameof(LogNoBarDataAvailable)),
            "No bar data available for pattern analysis of symbol '{Symbol}'");
    
    private static readonly Action<ILogger, string, Exception> LogInvalidSymbolArgument =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6209, nameof(LogInvalidSymbolArgument)),
            "Invalid symbol argument for pattern analysis: {Symbol}");
    
    private static readonly Action<ILogger, string, Exception> LogServiceUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6210, nameof(LogServiceUnavailable)),
            "Pattern analysis service unavailable for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception> LogTimeout =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6211, nameof(LogTimeout)),
            "Timeout retrieving bar data for pattern analysis of {Symbol}");

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
                LogPatternDetectorWarning(_logger, detector.PatternName, symbol, ex);
            }
            catch (ArgumentException ex)
            {
                LogPatternDetectorArgumentError(_logger, detector.PatternName, symbol, ex);
            }
        }

        // Aggregate scores
        var patternScores = AggregateScores(results);

        // Publish summary scores to feature bus  
        _featureBus.Publish(symbol, now, "pattern.bull_score", (decimal)patternScores.BullScore);
        _featureBus.Publish(symbol, now, "pattern.bear_score", (decimal)patternScores.BearScore);
        _featureBus.Publish(symbol, now, "pattern.total_count", (decimal)patternScores.PatternFlags.Count);

        LogPatternAnalysis(_logger, symbol, patternScores.BullScore, patternScores.BearScore, results.Count, null);

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
                LogNoBarsAvailable(_logger, symbol, null);
                var neutralResult = new PatternScoresWithDetails
                {
                    BullScore = NEUTRAL_SCORE,
                    BearScore = NEUTRAL_SCORE,
                    OverallConfidence = 0.0
                };
                neutralResult.SetDetectedPatterns(System.Array.Empty<PatternDetail>());
                return neutralResult;
            }

            // Use the existing synchronous GetScores method with real bar data
            var patternScores = GetScores(symbol, recentBars);
            
            // Convert to detailed format for async interface
            var detailsResult = new PatternScoresWithDetails
            {
                BullScore = patternScores.BullScore,
                BearScore = patternScores.BearScore,
                OverallConfidence = CalculateOverallConfidence(patternScores)
            };
            detailsResult.SetDetectedPatterns(ConvertPatternFlagsToDetails(patternScores.PatternFlags));
            
            // Publish pattern scores to feature bus
            var timestamp = DateTime.UtcNow;
            _featureBus.Publish(symbol, timestamp, "pattern.bull_score", detailsResult.BullScore);
            _featureBus.Publish(symbol, timestamp, "pattern.bear_score", detailsResult.BearScore);
            _featureBus.Publish(symbol, timestamp, "pattern.confidence", detailsResult.OverallConfidence);

            return detailsResult;
        }
        catch (InvalidOperationException ex)
        {
            LogGetScoresError(_logger, symbol, ex);
            
            // Return neutral scores on error
            var errorResult = new PatternScoresWithDetails
            {
                BullScore = NEUTRAL_SCORE,
                BearScore = NEUTRAL_SCORE,
                OverallConfidence = 0.0
            };
            errorResult.SetDetectedPatterns(System.Array.Empty<PatternDetail>());
            return errorResult;
        }
    }

    private static PatternScores AggregateScores(List<PatternDetectionResult> results)
    {
        var scores = new PatternScores();
        
        foreach (var result in results)
        {
            // Add to pattern flags
            scores.SetPatternFlag(result.PatternName, result.Result.Score);
            
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
                    
                    LogRetrievedBars(_logger, bars.Count, symbol, null);
                    
                    await Task.CompletedTask.ConfigureAwait(false);
                    return bars;
                }
            }
            
            // Try to get from trading system bar consumer
            var barConsumer = _serviceProvider.GetService<BotCore.Services.TradingSystemBarConsumer>();
            if (barConsumer != null)
            {
                // The bar consumer doesn't expose a query interface, but we can try other sources
                LogBarConsumerNoQuery(_logger, symbol, null);
            }
            
            // If no bars available, we cannot perform pattern analysis
            LogNoBarDataAvailable(_logger, symbol, null);
            return null;
        }
        catch (ArgumentException ex)
        {
            LogInvalidSymbolArgument(_logger, symbol, ex);
            throw new InvalidOperationException($"Pattern analysis failed for symbol '{symbol}' due to invalid symbol argument", ex);
        }
        catch (InvalidOperationException ex) 
        {
            LogServiceUnavailable(_logger, symbol, ex);
            throw new InvalidOperationException($"Pattern analysis failed for symbol '{symbol}' due to service unavailability", ex);
        }
        catch (TimeoutException ex)
        {
            LogTimeout(_logger, symbol, ex);
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
    private static List<PatternDetail> ConvertPatternFlagsToDetails(System.Collections.Generic.IReadOnlyDictionary<string, double> patternFlags)
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

    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var detectorCount = _detectors.Count;
            var detectorTypes = _detectors.Select(d => d.GetType().Name).ToList();
            
            if (detectorCount == 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "No pattern detectors registered",
                    new Dictionary<string, object>
                    {
                        ["DetectorCount"] = 0
                    }));
            }
            
            return Task.FromResult(HealthCheckResult.Healthy(
                "Pattern engine operating normally",
                new Dictionary<string, object>
                {
                    ["DetectorCount"] = detectorCount,
                    ["Detectors"] = string.Join(", ", detectorTypes)
                }));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Invalid operation during health check: {ex.Message}",
                new Dictionary<string, object> { ["Exception"] = ex.GetType().Name }));
        }
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
    
    private readonly Dictionary<string, double> _patternFlags = new();
    
    /// <summary>
    /// Individual pattern flags (pattern name -> score)
    /// </summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, double> PatternFlags => _patternFlags;
    
    /// <summary>
    /// Set a pattern flag score
    /// </summary>
    public void SetPatternFlag(string patternName, double score)
    {
        ArgumentNullException.ThrowIfNull(patternName);
        _patternFlags[patternName] = score;
    }
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
    
    private readonly List<PatternDetail> _detectedPatterns = new();
    
    /// <summary>
    /// Detailed information about detected patterns
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<PatternDetail> DetectedPatterns => _detectedPatterns;
    
    /// <summary>
    /// Set the detected patterns list
    /// </summary>
    public void SetDetectedPatterns(System.Collections.Generic.IEnumerable<PatternDetail> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _detectedPatterns.Clear();
        _detectedPatterns.AddRange(patterns);
    }
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