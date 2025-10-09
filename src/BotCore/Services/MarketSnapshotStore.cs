using System;
using System.Collections.Generic;
using System.Linq;
using Zones;
using BotCore.Patterns;

namespace BotCore.Services;

/// <summary>
/// Represents a complete market snapshot at a point in time for historical pattern matching
/// </summary>
public sealed class TradingMarketSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // Market state
    public decimal Vix { get; set; }
    public string Trend { get; set; } = string.Empty;
    public string Session { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public string Symbol { get; set; } = string.Empty;
    
    // Zone state
    public decimal DemandDistanceAtr { get; set; }
    public decimal SupplyDistanceAtr { get; set; }
    public decimal ZonePressure { get; set; }
    public decimal BreakoutScore { get; set; }
    public int DemandTouches { get; set; }
    public int SupplyTouches { get; set; }
    
    // Pattern state
    public double BullScore { get; set; }
    public double BearScore { get; set; }
    public double OverallConfidence { get; set; }
    public System.Collections.Generic.IReadOnlyList<string> DetectedPatterns { get; init; } = System.Array.Empty<string>();
    
    // Decision
    public string Strategy { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public int Size { get; set; }
    
    // Outcome (filled later)
    public decimal? OutcomePnl { get; set; }
    public bool? WasCorrect { get; set; }
    public TimeSpan? HoldTime { get; set; }
}

/// <summary>
/// Stores market snapshots in a ring buffer for historical comparison
/// </summary>
public sealed class MarketSnapshotStore
{
    private readonly Ring<TradingMarketSnapshot> _snapshots;
    private readonly object _lock = new();

    public MarketSnapshotStore(int capacity = 500)
    {
        _snapshots = new Ring<TradingMarketSnapshot>(capacity);
    }

    /// <summary>
    /// Store a new market snapshot
    /// </summary>
    public void StoreSnapshot(TradingMarketSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Update an existing snapshot with outcome
    /// </summary>
    public void UpdateSnapshotOutcome(string snapshotId, decimal pnl, bool wasCorrect, TimeSpan holdTime)
    {
        lock (_lock)
        {
            var snapshot = FindSnapshotById(snapshotId);
            if (snapshot != null)
            {
                snapshot.OutcomePnl = pnl;
                snapshot.WasCorrect = wasCorrect;
                snapshot.HoldTime = holdTime;
            }
        }
    }

    /// <summary>
    /// Find a snapshot by ID
    /// </summary>
    public TradingMarketSnapshot? FindSnapshotById(string snapshotId)
    {
        lock (_lock)
        {
            return _snapshots.ToList()
                .FirstOrDefault(s => s.Id == snapshotId);
        }
    }

    /// <summary>
    /// Find snapshots within a time window
    /// </summary>
    public IReadOnlyList<TradingMarketSnapshot> FindSnapshotsInWindow(TimeSpan window)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - window;
            return _snapshots.ToList()
                .Where(s => s.Timestamp >= cutoff)
                .OrderByDescending(s => s.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// Get all completed snapshots (with outcomes)
    /// </summary>
    public IReadOnlyList<TradingMarketSnapshot> GetCompletedSnapshots(int maxCount = 100)
    {
        lock (_lock)
        {
            return _snapshots.ToList()
                .Where(s => s.OutcomePnl.HasValue)
                .OrderByDescending(s => s.Timestamp)
                .Take(maxCount)
                .ToList();
        }
    }

    /// <summary>
    /// Get recent snapshots for similarity matching
    /// </summary>
    public IReadOnlyList<TradingMarketSnapshot> GetRecentSnapshots(int count = 100)
    {
        lock (_lock)
        {
            return _snapshots.ToList()
                .OrderByDescending(s => s.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Create a snapshot from current market conditions
    /// </summary>
    public static TradingMarketSnapshot CreateSnapshot(
        string symbol,
        decimal currentPrice,
        decimal vix,
        string trend,
        string session,
        ZoneSnapshot zoneSnapshot,
        PatternScoresWithDetails patternScores,
        string strategy,
        string direction,
        decimal confidence,
        int size)
    {
        ArgumentNullException.ThrowIfNull(zoneSnapshot);
        ArgumentNullException.ThrowIfNull(patternScores);
        
        return new TradingMarketSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Symbol = symbol,
            CurrentPrice = currentPrice,
            Vix = vix,
            Trend = trend,
            Session = session,
            
            // Zone data
            DemandDistanceAtr = zoneSnapshot.DistToDemandAtr,
            SupplyDistanceAtr = zoneSnapshot.DistToSupplyAtr,
            ZonePressure = zoneSnapshot.ZonePressure,
            BreakoutScore = zoneSnapshot.BreakoutScore,
            DemandTouches = zoneSnapshot.NearestDemand?.TouchCount ?? 0,
            SupplyTouches = zoneSnapshot.NearestSupply?.TouchCount ?? 0,
            
            // Pattern data
            BullScore = patternScores.BullScore,
            BearScore = patternScores.BearScore,
            OverallConfidence = patternScores.OverallConfidence,
            DetectedPatterns = patternScores.DetectedPatterns
                .Select(p => p.Name)
                .ToArray(),
            
            // Decision
            Strategy = strategy,
            Direction = direction,
            Confidence = confidence,
            Size = size
        };
    }
}
