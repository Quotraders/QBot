using System;
using System.Collections.Generic;

namespace TradingBot.S7
{
    /// <summary>
    /// Current state of S7 multi-horizon relative strength analysis
    /// </summary>
    public class S7State
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal RelativeStrengthShort { get; set; }
        public decimal RelativeStrengthMedium { get; set; }
        public decimal RelativeStrengthLong { get; set; }
        public decimal ZScore { get; set; }
        public decimal Coherence { get; set; }
        public S7Leader CurrentLeader { get; set; }
        public int CooldownBarsRemaining { get; set; }
        public bool IsSignalActive { get; set; }
        public decimal SizeTilt { get; set; }
        public Dictionary<string, decimal> AdditionalMetrics { get; set; } = new();
    }

    /// <summary>
    /// Identifies which symbol is showing relative strength leadership
    /// </summary>
    public enum S7Leader
    {
        None,
        ES,
        NQ,
        Divergent  // When signals are conflicting
    }

    /// <summary>
    /// Complete snapshot of S7 analysis for knowledge graph integration
    /// </summary>
    public class S7Snapshot
    {
        public S7State ESState { get; set; } = new();
        public S7State NQState { get; set; } = new();
        public decimal CrossSymbolCoherence { get; set; }
        public S7Leader DominantLeader { get; set; }
        public decimal SignalStrength { get; set; }
        public bool IsActionable { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public Dictionary<string, decimal> FeatureBusData { get; set; } = new();
    }

    /// <summary>
    /// Feature tuple for ML integration and knowledge graph
    /// </summary>
    public class S7FeatureTuple
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal RelativeStrengthShort { get; set; }
        public decimal RelativeStrengthMedium { get; set; }
        public decimal RelativeStrengthLong { get; set; }
        public decimal ZScore { get; set; }
        public decimal Coherence { get; set; }
        public decimal SizeTilt { get; set; }
        public string Leader { get; set; } = string.Empty;
        public bool IsSignalActive { get; set; }
        public Dictionary<string, object> ExtendedFeatures { get; set; } = new();
    }

    /// <summary>
    /// Configuration for S7 strategy parameters
    /// </summary>
    public class S7Configuration
    {
        public bool Enabled { get; set; } = true;
        public string[] Symbols { get; set; } = Array.Empty<string>();
        public int BarTimeframeMinutes { get; set; } = 5;
        public int LookbackShortBars { get; set; } = 10;
        public int LookbackMediumBars { get; set; } = 30;
        public int LookbackLongBars { get; set; } = 60;
        public decimal ZThresholdEntry { get; set; } = 2.0m;
        public decimal ZThresholdExit { get; set; } = 1.0m;
        public decimal CoherenceMin { get; set; } = 0.75m;
        public int CooldownBars { get; set; } = 5;
        public decimal SizeTiltFactor { get; set; } = 1.0m;
        public decimal AtrMultiplierStop { get; set; } = 2.0m;
        public decimal AtrMultiplierTarget { get; set; } = 4.0m;
        public decimal MinAtrThreshold { get; set; } = 0.7m;
        public bool EnableFeatureBus { get; set; } = true;
        public bool EnableTelemetry { get; set; } = true;
    }

    /// <summary>
    /// Core S7 service interface for dependency injection
    /// </summary>
    public interface IS7Service
    {
        /// <summary>
        /// Update the S7 analysis with new bar data
        /// </summary>
        Task UpdateAsync(string symbol, decimal close, DateTime timestamp);

        /// <summary>
        /// Get current S7 snapshot for both ES and NQ
        /// </summary>
        S7Snapshot GetCurrentSnapshot();

        /// <summary>
        /// Get feature tuple for ML integration
        /// </summary>
        S7FeatureTuple GetFeatureTuple(string symbol);

        /// <summary>
        /// Check if S7 is ready for signal generation (sufficient data)
        /// </summary>
        bool IsReady();

        /// <summary>
        /// Reset cooldown state (used after successful trade execution)
        /// </summary>
        void ResetCooldown(string symbol);
    }

    /// <summary>
    /// Interface for feature source integration with knowledge graph
    /// </summary>
    public interface IS7FeatureSource
    {
        /// <summary>
        /// Get all available features for knowledge graph consumption
        /// </summary>
        Dictionary<string, object> GetAllFeatures();

        /// <summary>
        /// Subscribe to feature updates
        /// </summary>
        event EventHandler<S7FeatureTuple> FeatureUpdated;
    }

    /// <summary>
    /// Breadth analysis configuration (optional add-on)
    /// </summary>
    public class BreadthConfiguration
    {
        public bool Enabled { get; set; } = false;
        public decimal AdvanceDeclineThreshold { get; set; } = 0.75m;
        public decimal NewHighsLowsRatio { get; set; } = 2.0m;
        public decimal SectorRotationWeight { get; set; } = 0.25m;
        public int BreadthLookbackBars { get; set; } = 20;
        public string DataSource { get; set; } = "IndexBreadthFeed";
    }

    /// <summary>
    /// Breadth feed interface (optional integration)
    /// </summary>
    public interface IBreadthFeed
    {
        Task<decimal> GetAdvanceDeclineRatioAsync();
        Task<decimal> GetNewHighsLowsRatioAsync();
        Task<Dictionary<string, decimal>> GetSectorRotationDataAsync();
        bool IsDataAvailable();
    }
}