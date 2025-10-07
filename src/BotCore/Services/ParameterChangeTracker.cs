using System;
using System.Collections.Generic;
using System.Linq;

namespace BotCore.Services;

/// <summary>
/// Record of a parameter change with context
/// </summary>
public sealed class ParameterChange
{
    public DateTime Timestamp { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal? OutcomePnl { get; set; }
    public bool? WasCorrect { get; set; }
    public string? MarketSnapshotId { get; set; }
}

/// <summary>
/// Tracks parameter changes in strategies with a ring buffer
/// Records what changed, why, and tracks outcomes
/// </summary>
public sealed class ParameterChangeTracker
{
    private readonly Ring<ParameterChange> _changes;
    private readonly object _lock = new();

    public ParameterChangeTracker(int capacity = 100)
    {
        _changes = new Ring<ParameterChange>(capacity);
    }

    /// <summary>
    /// Record a parameter change
    /// </summary>
    public void RecordChange(
        string strategyName,
        string parameterName,
        string oldValue,
        string newValue,
        string reason,
        decimal? outcomePnl = null,
        bool? wasCorrect = null,
        string? marketSnapshotId = null)
    {
        lock (_lock)
        {
            var change = new ParameterChange
            {
                Timestamp = DateTime.UtcNow,
                StrategyName = strategyName,
                ParameterName = parameterName,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason,
                OutcomePnl = outcomePnl,
                WasCorrect = wasCorrect,
                MarketSnapshotId = marketSnapshotId
            };
            
            _changes.Add(change);
        }
    }

    /// <summary>
    /// Get recent parameter changes
    /// </summary>
    public IReadOnlyList<ParameterChange> GetRecentChanges(int count = 10)
    {
        lock (_lock)
        {
            return _changes.ToList()
                .OrderByDescending(c => c.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Get changes for a specific strategy
    /// </summary>
    public IReadOnlyList<ParameterChange> GetChangesForStrategy(string strategyName, int count = 10)
    {
        lock (_lock)
        {
            return _changes.ToList()
                .Where(c => c.StrategyName.Equals(strategyName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Get all changes within a time window
    /// </summary>
    public IReadOnlyList<ParameterChange> GetChangesInWindow(TimeSpan window)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - window;
            return _changes.ToList()
                .Where(c => c.Timestamp >= cutoff)
                .OrderByDescending(c => c.Timestamp)
                .ToList();
        }
    }
}

/// <summary>
/// Simple ring buffer implementation for fixed-size history
/// </summary>
internal sealed class Ring<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public Ring(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive");
        }
        
        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
        {
            _count++;
        }
    }

    public List<T> ToList()
    {
        var result = new List<T>(_count);
        
        if (_count == 0)
        {
            return result;
        }

        if (_count < _buffer.Length)
        {
            // Buffer not full yet
            for (var i = 0; i < _count; i++)
            {
                result.Add(_buffer[i]);
            }
        }
        else
        {
            // Buffer is full, need to unwrap from head
            for (var i = 0; i < _buffer.Length; i++)
            {
                var idx = (_head + i) % _buffer.Length;
                result.Add(_buffer[idx]);
            }
        }

        return result;
    }
}
