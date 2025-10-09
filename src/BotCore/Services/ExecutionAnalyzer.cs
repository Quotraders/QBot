using System.Text.Json;
using BotCore.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BotCore.Services;

/// <summary>
/// Service for analyzing execution quality and providing feedback to Intelligence pipeline.
/// Tracks slippage, fill quality, and zone test results.
/// </summary>
public class ExecutionAnalyzer
{
    // Precision and conversion constants
    private const decimal PercentToDecimalConversion = 100m;    // Convert percentage to decimal (divide by 100)
    private const int PriceRoundingDecimals = 2;                // Price rounding to 2 decimal places
    private const int SuccessRateRoundingDecimals = 3;          // Success rate rounding to 3 decimal places

    private readonly ILogger<ExecutionAnalyzer> _logger;
    private readonly string _feedbackPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExecutionAnalyzer(ILogger<ExecutionAnalyzer> logger, string? feedbackPath = null)
    {
        _logger = logger;
        _feedbackPath = feedbackPath ?? "data/zones/feedback";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // Ensure feedback directory exists
        Directory.CreateDirectory(_feedbackPath);
    }

    /// <summary>
    /// Track fill quality metrics for execution analysis
    /// </summary>
    public async Task TrackFillQualityAsync(string symbol, decimal entryPrice, decimal fillPrice,
        int quantity, string strategy, DateTime timestamp)
    {
        try
        {
            var slippage = Math.Abs(fillPrice - entryPrice);
            var slippagePercent = entryPrice != 0 ? (slippage / entryPrice) * 100 : 0;

            var fillQuality = new
            {
                Symbol = symbol,
                Strategy = strategy,
                Timestamp = timestamp.ToString("O", CultureInfo.InvariantCulture),
                EntryPrice = entryPrice,
                FillPrice = fillPrice,
                Quantity = quantity,
                Slippage = Math.Round(slippage, 4),
                SlippagePercent = Math.Round(slippagePercent, 4),
                Quality = DetermineQuality(slippagePercent)
            };

            string DetermineQuality(decimal slippagePct)
            {
                const decimal ExcellentSlippageThreshold = 0.02m;
                const decimal GoodSlippageThreshold = 0.05m;
                const decimal FairSlippageThreshold = 0.1m;
                
                if (slippagePct < ExcellentSlippageThreshold) return "excellent";
                if (slippagePct < GoodSlippageThreshold) return "good";
                if (slippagePct < FairSlippageThreshold) return "fair";
                return "poor";
            }

            _logger.LogInformation("[EXEC_QUALITY] {Symbol} {Strategy} slippage={SlippagePercent:P2} quality={Quality}",
                symbol, strategy, slippagePercent / PercentToDecimalConversion, fillQuality.Quality);

            // Save to execution quality log
            await SaveExecutionMetricsAsync(fillQuality).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error tracking fill quality for {Symbol}", symbol);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied tracking fill quality for {Symbol}", symbol);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error tracking fill quality for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument tracking fill quality for {Symbol}", symbol);
        }
    }

    /// <summary>
    /// Provide feedback on supply/demand zone tests
    /// </summary>
    public async Task ZoneFeedbackAsync(string symbol, decimal zoneLevel, string zoneType,
        bool successful, decimal entryPrice, decimal exitPrice, string reason)
    {
        try
        {
            var pnlPercent = entryPrice != 0 ? ((exitPrice - entryPrice) / entryPrice) * 100 : 0;

            // Feedback object prepared for future zone test tracking
            _ = new
            {
                Symbol = symbol,
                ZoneLevel = Math.Round(zoneLevel, PriceRoundingDecimals),
                ZoneType = zoneType, // "supply" or "demand"
                TestTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Successful = successful,
                EntryPrice = Math.Round(entryPrice, PriceRoundingDecimals),
                ExitPrice = Math.Round(exitPrice, PriceRoundingDecimals),
                PnLPercent = Math.Round(pnlPercent, PriceRoundingDecimals),
                Reason = reason
            };

            _logger.LogInformation("[ZONE_FEEDBACK] {Symbol} {ZoneType}@{ZoneLevel} success={Successful} pnl={PnLPercent:P2}",
                symbol, zoneType, zoneLevel, successful, pnlPercent / PercentToDecimalConversion);

            // Update zone feedback data
            await UpdateZoneFeedbackAsync(symbol, zoneLevel, zoneType, successful).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error providing zone feedback for {Symbol}", symbol);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied providing zone feedback for {Symbol}", symbol);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error providing zone feedback for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument providing zone feedback for {Symbol}", symbol);
        }
    }

    /// <summary>
    /// Track pattern outcome (success/failure) for pattern recognition improvement
    /// </summary>
    public async Task PatternOutcomeAsync(string symbol, string patternType, bool successful,
        decimal confidence, string details)
    {
        try
        {
            var outcome = new
            {
                Symbol = symbol,
                PatternType = patternType,
                Timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Successful = successful,
                Confidence = Math.Round(confidence, 3),
                Details = details,
                OutcomeQuality = DetermineOutcomeQuality(successful, confidence)
            };

            string DetermineOutcomeQuality(bool isSuccessful, decimal conf)
            {
                const decimal HighConfidenceThreshold = 0.7m;
                const decimal MediumConfidenceThreshold = 0.4m;
                
                if (isSuccessful)
                {
                    if (conf > HighConfidenceThreshold) return "high_confidence_success";
                    if (conf > MediumConfidenceThreshold) return "medium_confidence_success";
                    return "low_confidence_success";
                }
                else
                {
                    if (conf > HighConfidenceThreshold) return "high_confidence_failure";
                    return "failed";
                }
            }

            _logger.LogInformation("[PATTERN_OUTCOME] {Symbol} {PatternType} success={Successful} conf={Confidence:P1}",
                symbol, patternType, successful, confidence);

            await SavePatternOutcomeAsync(outcome).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error tracking pattern outcome for {Symbol}", symbol);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied tracking pattern outcome for {Symbol}", symbol);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error tracking pattern outcome for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument tracking pattern outcome for {Symbol}", symbol);
        }
    }

    /// <summary>
    /// Get execution quality metrics for the day
    /// </summary>
    public async Task<ExecutionMetrics?> GetDailyExecutionMetricsAsync()
    {
        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var metricsFile = Path.Combine(_feedbackPath, $"execution_metrics_{today}.json");

            if (!File.Exists(metricsFile))
                return null;

            var json = await File.ReadAllTextAsync(metricsFile).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ExecutionMetrics>(json, _jsonOptions);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error getting daily execution metrics");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied getting daily execution metrics");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error getting daily execution metrics");
            return null;
        }
    }

    private async Task SaveExecutionMetricsAsync(object fillQuality)
    {
        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var metricsFile = Path.Combine(_feedbackPath, $"execution_metrics_{today}.json");

            // Load existing metrics or create new
            var metrics = new List<object>();
            if (File.Exists(metricsFile))
            {
                var json = await File.ReadAllTextAsync(metricsFile).ConfigureAwait(false);
                var existing = JsonSerializer.Deserialize<List<object>>(json);
                if (existing != null) metrics = existing;
            }

            metrics.Add(fillQuality);

            await File.WriteAllTextAsync(metricsFile,
                JsonSerializer.Serialize(metrics, _jsonOptions)).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error saving execution metrics");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied saving execution metrics");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error saving execution metrics");
        }
    }

    private async Task UpdateZoneFeedbackAsync(string symbol, decimal zoneLevel, string zoneType, bool successful)
    {
        try
        {
            var feedbackFile = Path.Combine(_feedbackPath, $"{symbol}_feedback.json");
            var zoneKey = $"{zoneType}_{zoneLevel}";

            // Load existing feedback
            var feedback = new Dictionary<string, object>();
            if (File.Exists(feedbackFile))
            {
                var json = await File.ReadAllTextAsync(feedbackFile).ConfigureAwait(false);
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (existing != null) feedback = existing;
            }

            // Update zone feedback
            if (!feedback.TryGetValue(zoneKey, out var zoneValue))
            {
                zoneValue = new { success_count = 0, test_count = 0, success_rate = 0.0 };
                feedback[zoneKey] = zoneValue;
            }

            var zoneData = JsonSerializer.Deserialize<Dictionary<string, object>>(
                zoneValue.ToString() ?? "{}");

            var successCount = zoneData?.GetValueOrDefault("success_count", 0);
            var testCount = zoneData?.GetValueOrDefault("test_count", 0);

            var newSuccessCount = Convert.ToInt32(successCount, System.Globalization.CultureInfo.InvariantCulture) + (successful ? 1 : 0);
            var newTestCount = Convert.ToInt32(testCount, System.Globalization.CultureInfo.InvariantCulture) + 1;
            var newSuccessRate = newTestCount > 0 ? (double)newSuccessCount / newTestCount : 0;

            feedback[zoneKey] = new
            {
                success_count = newSuccessCount,
                test_count = newTestCount,
                success_rate = Math.Round(newSuccessRate, SuccessRateRoundingDecimals),
                last_test = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await File.WriteAllTextAsync(feedbackFile,
                JsonSerializer.Serialize(feedback, _jsonOptions)).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error updating zone feedback");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied updating zone feedback");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error updating zone feedback");
        }
    }

    private async Task SavePatternOutcomeAsync(object outcome)
    {
        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var outcomeFile = Path.Combine(_feedbackPath, $"pattern_outcomes_{today}.json");

            var outcomes = new List<object>();
            if (File.Exists(outcomeFile))
            {
                var json = await File.ReadAllTextAsync(outcomeFile).ConfigureAwait(false);
                var existing = JsonSerializer.Deserialize<List<object>>(json);
                if (existing != null) outcomes = existing;
            }

            outcomes.Add(outcome);

            await File.WriteAllTextAsync(outcomeFile,
                JsonSerializer.Serialize(outcomes, _jsonOptions)).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File system error saving pattern outcome");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied saving pattern outcome");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error saving pattern outcome");
        }
    }
}

/// <summary>
/// Execution metrics model
/// </summary>
public class ExecutionMetrics
{
    public double AverageSlippagePercent { get; set; }
    public int TotalFills { get; set; }
    public int ExcellentFills { get; set; }
    public int GoodFills { get; set; }
    public int FairFills { get; set; }
    public int PoorFills { get; set; }
    public double QualityScore { get; set; }
}