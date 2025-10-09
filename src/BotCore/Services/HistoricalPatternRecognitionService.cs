using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BotCore.Services;

/// <summary>
/// Represents a historical match with similarity score
/// </summary>
public sealed class HistoricalMatch
{
    public TradingMarketSnapshot Snapshot { get; set; } = null!;
    public double SimilarityScore { get; set; }
}

/// <summary>
/// Result of historical pattern matching
/// </summary>
public sealed class HistoricalPatternAnalysis
{
    public System.Collections.Generic.IReadOnlyList<HistoricalMatch> Matches { get; init; } = System.Array.Empty<HistoricalMatch>();
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public decimal AveragePnl { get; set; }
    public string BestStrategy { get; set; } = string.Empty;
    public TimeSpan AverageHoldTime { get; set; }
}

/// <summary>
/// Finds similar historical market conditions and analyzes their outcomes
/// </summary>
public sealed class HistoricalPatternRecognitionService
{
    private const int MaxSnapshotsToSearch = 100;
    private const double MaxVixForNormalization = 40.0;
    private const double MaxDistanceAtrForNormalization = 3.0;
    private const double MaxTouchesForNormalization = 10.0;
    private const double BullishTrendValue = 1.0;
    private const double BearishTrendValue = -1.0;
    private const double NeutralTrendValue = 0.0;
    private const double MinNormalizedValue = 0.0;
    private const double MaxNormalizedValue = 1.0;
    
    private readonly ILogger<HistoricalPatternRecognitionService> _logger;
    private readonly MarketSnapshotStore _snapshotStore;
    private readonly OllamaClient? _ollamaClient;

    public HistoricalPatternRecognitionService(
        ILogger<HistoricalPatternRecognitionService> logger,
        MarketSnapshotStore snapshotStore,
        OllamaClient? ollamaClient = null)
    {
        _logger = logger;
        _snapshotStore = snapshotStore;
        _ollamaClient = ollamaClient;
    }

    /// <summary>
    /// Find similar historical conditions and analyze outcomes
    /// </summary>
    public HistoricalPatternAnalysis FindSimilarConditions(
        TradingMarketSnapshot currentSnapshot,
        int maxMatches = 5,
        double similarityThreshold = 0.85)
    {
        var recentSnapshots = _snapshotStore.GetCompletedSnapshots(MaxSnapshotsToSearch);
        
        if (recentSnapshots.Count == 0)
        {
            return new HistoricalPatternAnalysis();
        }

        // Calculate similarity scores
        var matches = recentSnapshots
            .Select(s => new HistoricalMatch
            {
                Snapshot = s,
                SimilarityScore = CalculateSimilarity(currentSnapshot, s)
            })
            .Where(m => m.SimilarityScore >= similarityThreshold)
            .OrderByDescending(m => m.SimilarityScore)
            .Take(maxMatches)
            .ToList();

        if (matches.Count == 0)
        {
            return new HistoricalPatternAnalysis();
        }

        // Analyze outcomes
        var matchesWithPnl = matches.Where(m => m.Snapshot.OutcomePnl.HasValue).ToList();
        var matchesWithHoldTime = matches.Where(m => m.Snapshot.HoldTime.HasValue).ToList();
        
        var analysis = new HistoricalPatternAnalysis
        {
            Matches = matches,
            WinCount = matches.Count(m => m.Snapshot.WasCorrect == true),
            LossCount = matches.Count(m => m.Snapshot.WasCorrect == false),
            AveragePnl = matchesWithPnl.Count > 0 
                ? matchesWithPnl.Average(m => m.Snapshot.OutcomePnl!.Value)
                : 0m,
            AverageHoldTime = matchesWithHoldTime.Count > 0
                ? TimeSpan.FromMinutes(matchesWithHoldTime.Average(m => m.Snapshot.HoldTime!.Value.TotalMinutes))
                : TimeSpan.Zero
        };

        // Find best performing strategy
        var strategyGroups = matches
            .GroupBy(m => m.Snapshot.Strategy)
            .Select(g => new
            {
                Strategy = g.Key,
                WinRate = g.Count(m => m.Snapshot.WasCorrect == true) / (double)g.Count()
            })
            .OrderByDescending(g => g.WinRate);

        analysis.BestStrategy = strategyGroups.FirstOrDefault()?.Strategy ?? string.Empty;

        return analysis;
    }

    /// <summary>
    /// Generate natural language explanation of similar conditions
    /// </summary>
    public async Task<string> ExplainSimilarConditionsAsync(HistoricalPatternAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        
        if (_ollamaClient == null || analysis.Matches.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            var winRate = analysis.WinCount + analysis.LossCount > 0
                ? analysis.WinCount / (double)(analysis.WinCount + analysis.LossCount)
                : 0.0;

            var matchDescriptions = string.Join("\n", analysis.Matches.Select(m =>
                $"  - {m.Snapshot.Timestamp:yyyy-MM-dd}: {(m.Snapshot.WasCorrect == true ? "WIN" : "LOSS")} " +
                $"${m.Snapshot.OutcomePnl:F2} using {m.Snapshot.Strategy} (similarity: {m.SimilarityScore:P0})"
            ));

            var prompt = $@"I am a trading bot. I found similar market conditions from the past:

Matches: {analysis.Matches.Count}
Win rate: {winRate:P0}
Average P&L: ${analysis.AveragePnl:F2}
Best strategy: {analysis.BestStrategy}

Details:
{matchDescriptions}

Explain what I should learn from these past experiences in 2-3 sentences. Speak as ME (the bot).";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [HISTORICAL-PATTERN] Error explaining similar conditions");
            return string.Empty;
        }
    }

    /// <summary>
    /// Calculate cosine similarity between two market snapshots
    /// </summary>
    private static double CalculateSimilarity(TradingMarketSnapshot current, TradingMarketSnapshot historical)
    {
        // Convert snapshots to feature vectors
        var currentVec = ToFeatureVector(current);
        var historicalVec = ToFeatureVector(historical);

        // Calculate cosine similarity
        var dotProduct = 0.0;
        var magCurrent = 0.0;
        var magHistorical = 0.0;

        for (var i = 0; i < currentVec.Length; i++)
        {
            dotProduct += currentVec[i] * historicalVec[i];
            magCurrent += currentVec[i] * currentVec[i];
            magHistorical += historicalVec[i] * historicalVec[i];
        }

        magCurrent = Math.Sqrt(magCurrent);
        magHistorical = Math.Sqrt(magHistorical);

        if (magCurrent == 0 || magHistorical == 0)
        {
            return 0.0;
        }

        return dotProduct / (magCurrent * magHistorical);
    }

    /// <summary>
    /// Convert market snapshot to normalized feature vector
    /// </summary>
    private static double[] ToFeatureVector(TradingMarketSnapshot snapshot)
    {
        return new[]
        {
            NormalizeVix(snapshot.Vix),
            NormalizeTrend(snapshot.Trend),
            NormalizeDistance((double)snapshot.DemandDistanceAtr),
            NormalizeDistance((double)snapshot.SupplyDistanceAtr),
            (double)snapshot.ZonePressure,
            (double)snapshot.BreakoutScore,
            snapshot.BullScore,
            snapshot.BearScore,
            snapshot.OverallConfidence,
            NormalizeTouches(snapshot.DemandTouches),
            NormalizeTouches(snapshot.SupplyTouches)
        };
    }

    private static double NormalizeVix(decimal vix)
    {
        return Math.Clamp((double)vix / MaxVixForNormalization, MinNormalizedValue, MaxNormalizedValue);
    }

    private static double NormalizeTrend(string trend)
    {
        return trend.ToLowerInvariant() switch
        {
            "bullish" => BullishTrendValue,
            "bearish" => BearishTrendValue,
            _ => NeutralTrendValue
        };
    }

    private static double NormalizeDistance(double distanceAtr)
    {
        return Math.Clamp(distanceAtr / MaxDistanceAtrForNormalization, MinNormalizedValue, MaxNormalizedValue);
    }

    private static double NormalizeTouches(int touches)
    {
        return Math.Clamp(touches / MaxTouchesForNormalization, MinNormalizedValue, MaxNormalizedValue);
    }
}
