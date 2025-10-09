using System;

namespace BotCore.Models
{
    /// <summary>
    /// Tracks position management state for active positions
    /// Used by UnifiedPositionManagementService to apply trailing stops, breakeven, etc.
    /// </summary>
    public sealed class PositionManagementState
    {
        /// <summary>
        /// Unique position identifier
        /// </summary>
        public string PositionId { get; set; } = string.Empty;
        
        /// <summary>
        /// Symbol/contract being traded
        /// </summary>
        public string Symbol { get; set; } = string.Empty;
        
        /// <summary>
        /// Strategy that opened this position (S2, S3, S6, S11)
        /// </summary>
        public string Strategy { get; set; } = string.Empty;
        
        /// <summary>
        /// Entry price for the position
        /// </summary>
        public decimal EntryPrice { get; set; }
        
        /// <summary>
        /// Current stop loss price
        /// </summary>
        public decimal CurrentStopPrice { get; set; }
        
        /// <summary>
        /// Target profit price (original/static target)
        /// </summary>
        public decimal TargetPrice { get; set; }
        
        /// <summary>
        /// Dynamic target price (adjusted based on regime changes)
        /// </summary>
        public decimal DynamicTargetPrice { get; set; }
        
        /// <summary>
        /// Market regime at entry time (Trend, Range, Transition)
        /// </summary>
        public string EntryRegime { get; set; } = "UNKNOWN";
        
        /// <summary>
        /// Current market regime (updated periodically)
        /// </summary>
        public string CurrentRegime { get; set; } = "UNKNOWN";
        
        /// <summary>
        /// Last time regime was checked
        /// </summary>
        public DateTime LastRegimeCheck { get; set; }
        
        /// <summary>
        /// FEATURE 3: Entry regime confidence (0.0-1.0)
        /// </summary>
        public decimal EntryRegimeConfidence { get; set; }
        
        /// <summary>
        /// FEATURE 3: Current regime confidence (0.0-1.0, updated periodically)
        /// </summary>
        public decimal CurrentRegimeConfidence { get; set; }
        
        /// <summary>
        /// FEATURE 3: List of regime changes during trade lifetime
        /// </summary>
        private readonly System.Collections.Generic.List<RegimeChangeRecord> _regimeChanges = new();
        
        /// <summary>
        /// FEATURE 3: Read-only view of regime changes during trade lifetime
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<RegimeChangeRecord> RegimeChanges => _regimeChanges;
        
        /// <summary>
        /// FEATURE 4: Entry confidence from AutonomousDecisionEngine (0.0-1.0)
        /// </summary>
        public decimal EntryConfidence { get; set; }
        
        /// <summary>
        /// FEATURE 5: Last time progressive tightening was applied
        /// </summary>
        public DateTime LastProgressiveTighteningCheck { get; set; }
        
        /// <summary>
        /// FEATURE 5: Current progressive tightening tier (0 = no tightening yet)
        /// </summary>
        public int ProgressiveTighteningTier { get; set; }
        
        /// <summary>
        /// FEATURE 5: Peak profit in ticks achieved (for progressive exit thresholds)
        /// </summary>
        public decimal PeakProfitTicks { get; set; }
        
        /// <summary>
        /// Position size (signed: + for long, - for short)
        /// </summary>
        public int Quantity { get; set; }
        
        /// <summary>
        /// When the position was opened
        /// </summary>
        public DateTime EntryTime { get; set; }
        
        /// <summary>
        /// Highest price reached (for longs) or lowest (for shorts)
        /// Used for max favorable excursion tracking
        /// </summary>
        public decimal MaxFavorablePrice { get; set; }
        
        /// <summary>
        /// Worst price reached (for longs) or highest (for shorts)
        /// Used for max adverse excursion tracking
        /// </summary>
        public decimal MaxAdversePrice { get; set; }
        
        /// <summary>
        /// Whether breakeven protection has been activated
        /// </summary>
        public bool BreakevenActivated { get; set; }
        
        /// <summary>
        /// Whether trailing stop is currently active
        /// </summary>
        public bool TrailingStopActive { get; set; }
        
        /// <summary>
        /// Breakeven trigger distance in ticks (from ParameterBundle)
        /// </summary>
        public int BreakevenAfterTicks { get; set; }
        
        /// <summary>
        /// Trailing stop distance in ticks (from ParameterBundle)
        /// </summary>
        public int TrailTicks { get; set; }
        
        /// <summary>
        /// Maximum hold time in minutes (strategy specific)
        /// </summary>
        public int MaxHoldMinutes { get; set; }
        
        /// <summary>
        /// Last time position was checked by management service
        /// </summary>
        public DateTime LastCheckTime { get; set; }
        
        /// <summary>
        /// Number of times stop has been modified
        /// </summary>
        public int StopModificationCount { get; set; }
        
        /// <summary>
        /// PHASE 4: Dynamic properties for tracking partial exits and other state
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<string, object> _dynamicProperties = new();
        
        /// <summary>
        /// MAE CORRELATION: Time-stamped MAE snapshots for correlation analysis
        /// </summary>
        private readonly System.Collections.Generic.List<MaeSnapshot> _maeSnapshots = new();
        
        /// <summary>
        /// MAE CORRELATION: Read-only view of MAE snapshots for correlation analysis
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<MaeSnapshot> MaeSnapshots => _maeSnapshots;
        
        /// <summary>
        /// Check if a dynamic property exists
        /// </summary>
        public bool HasProperty(string key)
        {
            return _dynamicProperties.ContainsKey(key);
        }
        
        /// <summary>
        /// Get a dynamic property value
        /// </summary>
        public object? GetProperty(string key)
        {
            return _dynamicProperties.TryGetValue(key, out var value) ? value : null;
        }
        
        /// <summary>
        /// Set a dynamic property value
        /// </summary>
        public void SetProperty(string key, object value)
        {
            _dynamicProperties[key] = value;
        }
        
        /// <summary>
        /// Add a regime change record to the position state
        /// </summary>
        public void AddRegimeChange(RegimeChangeRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            _regimeChanges.Add(record);
        }
        
        /// <summary>
        /// Add an MAE snapshot to the position state
        /// </summary>
        public void AddMaeSnapshot(MaeSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _maeSnapshots.Add(snapshot);
        }
    }
    
    /// <summary>
    /// MAE CORRELATION: Time-stamped MAE snapshot for tracking early adverse movement
    /// </summary>
    public sealed class MaeSnapshot
    {
        public DateTime Timestamp { get; set; }
        public decimal MaeValue { get; set; }
        public int ElapsedSeconds { get; set; }
    }
    
    /// <summary>
    /// FEATURE 3: Records a regime change event during position lifetime
    /// </summary>
    public sealed class RegimeChangeRecord
    {
        public DateTime Timestamp { get; set; }
        public string FromRegime { get; set; } = string.Empty;
        public string ToRegime { get; set; } = string.Empty;
        public decimal FromConfidence { get; set; }
        public decimal ToConfidence { get; set; }
        public decimal PnLAtChange { get; set; }
    }
}
