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
    }
}
