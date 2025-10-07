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
    public List<HistoricalMatch> Matches { get; set; } = new();
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
        var recentSnapshots = _snapshotStore.GetCompletedSnapshots(100);
        
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
        var analysis = new HistoricalPatternAnalysis
        {
            Matches = matches,
            WinCount = matches.Count(m => m.Snapshot.WasCorrect == true),
            LossCount = matches.Count(m => m.Snapshot.WasCorrect == false),
            AveragePnl = matches
                .Where(m => m.Snapshot.OutcomePnl.HasValue)
                .Average(m => m.Snapshot.OutcomePnl!.Value),
            AverageHoldTime = TimeSpan.FromMinutes(matches
                .Where(m => m.Snapshot.HoldTime.HasValue)
                .Average(m => m.Snapshot.HoldTime!.Value.TotalMinutes))
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
        // VIX typically ranges from 10 to 40, normalize to 0-1
        return Math.Clamp((double)vix / 40.0, 0.0, 1.0);
    }

    private static double NormalizeTrend(string trend)
    {
        return trend.ToLowerInvariant() switch
        {
            "bullish" => 1.0,
            "bearish" => -1.0,
            _ => 0.0
        };
    }

    private static double NormalizeDistance(double distanceAtr)
    {
        // Distances typically 0-3 ATR, normalize to 0-1
        return Math.Clamp(distanceAtr / 3.0, 0.0, 1.0);
    }

    private static double NormalizeTouches(int touches)
    {
        // Touches typically 0-10, normalize to 0-1
        return Math.Clamp(touches / 10.0, 0.0, 1.0);
    }
}
