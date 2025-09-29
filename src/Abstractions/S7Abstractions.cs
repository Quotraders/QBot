using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TradingBot.Abstractions
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
        
        // Enhanced fields for adaptive tuning and dispersion analysis
        public decimal AdaptiveThreshold { get; set; }
        public decimal MultiIndexDispersion { get; set; }
        public decimal AdvanceFraction { get; set; }
        public decimal DispersionAdjustedSizeTilt { get; set; } = 1.0m;
        public bool IsDispersionBoosted { get; set; }
        public bool IsDispersionBlocked { get; set; }
        
        public Dictionary<string, decimal> AdditionalMetrics { get; } = new();
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
        
        // Enhanced fields for knowledge graph and fusion tags
        public decimal GlobalDispersionIndex { get; set; }
        public decimal AdaptiveVolatilityMeasure { get; set; }
        public decimal SystemCoherenceScore { get; set; }
        public Dictionary<string, object> FusionTags { get; } = new();
        public Dictionary<string, decimal> FeatureBusData { get; } = new();
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
        
        // Enhanced features for adaptive tuning and dispersion
        public decimal AdaptiveThreshold { get; set; }
        public decimal MultiIndexDispersion { get; set; }
        public decimal AdvanceFraction { get; set; }
        public decimal DispersionAdjustedSizeTilt { get; set; }
        public bool IsDispersionBoosted { get; set; }
        public bool IsDispersionBlocked { get; set; }
        public decimal GlobalDispersionIndex { get; set; }
        public decimal AdaptiveVolatilityMeasure { get; set; }
        
        public Dictionary<string, object> ExtendedFeatures { get; } = new();
    }

    /// <summary>
    /// Configuration for S7 strategy parameters - ALL values must come from bounds validation
    /// </summary>
    public class S7Configuration
    {
        public bool Enabled { get; set; } = true;
        public IList<string> Symbols { get; } = new List<string>();
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

        // Coherence calculation weights - MUST come from config, no hardcoded values
        public decimal ZScoreAlignmentWeight { get; set; } = 0.4m;
        public decimal DirectionAlignmentWeight { get; set; } = 0.4m;
        public decimal TimeframeCoherenceWeight { get; set; } = 0.2m;
        
        // Leader threshold calculations - MUST come from config
        public decimal LeaderThreshold { get; set; } = 1.2m;
        public decimal TimeframeCountNormalizer { get; set; } = 3.0m;
        
        // Size tilt calculations - MUST come from config
        public decimal MaxSizeTiltMultiplier { get; set; } = 2.0m;
        
        // Breadth scoring parameters - MUST come from config
        public decimal BaseBreadthScore { get; set; } = 1.0m;
        public decimal AdvanceDeclineBonus { get; set; } = 0.1m;
        public decimal AdvanceDeclinePenalty { get; set; } = 0.1m;
        public decimal NewHighsLowsBonus { get; set; } = 0.05m;
        public decimal MinBreadthScore { get; set; } = 0.5m;
        public decimal MaxBreadthScore { get; set; } = 1.5m;
        
        // Multi-index dispersion and advance fraction parameters - MUST come from config
        public decimal DispersionThreshold { get; set; } = 0.3m;
        public decimal AdvanceFractionMin { get; set; } = 0.6m;
        public decimal DispersionSizeBoostFactor { get; set; } = 1.5m;
        public decimal DispersionSizeBlockFactor { get; set; } = 0.5m;
        public bool EnableDispersionAdjustments { get; set; } = true;
        
        // Adaptive parameter tuning configuration - MUST come from config  
        public bool EnableAdaptiveThresholds { get; set; } = true;
        public decimal AdaptiveThresholdMin { get; set; } = 1.5m;
        public decimal AdaptiveThresholdMax { get; set; } = 3.0m;
        public decimal AdaptiveSensitivity { get; set; } = 0.1m;
        public int AdaptiveLookbackPeriod { get; set; } = 20;
        public decimal AdaptiveVolatilityWeight { get; set; } = 0.3m;
        public decimal AdaptivePerformanceWeight { get; set; } = 0.7m;
        
        // Knowledge graph telemetry tags - MUST come from config
        public bool EnableFusionTags { get; set; } = true;
        public string FusionStateTagPrefix { get; set; } = "fusion.s7_state";
        public string FusionCoherenceTag { get; set; } = "fusion.s7_coherence";
        public string FusionDispersionTag { get; set; } = "fusion.s7_dispersion";
        public string FusionAdaptiveTag { get; set; } = "fusion.s7_adaptive";
        
        // Fail-closed configuration - unknown keys must trigger holds
        public bool FailOnUnknownKeys { get; set; } = true;
        public bool FailOnMissingData { get; set; } = true;
        public string TelemetryPrefix { get; set; } = "s7";
    }

    /// <summary>
    /// Event arguments for S7 feature updates
    /// </summary>
    public class S7FeatureUpdatedEventArgs : EventArgs
    {
        public S7FeatureTuple FeatureTuple { get; }

        public S7FeatureUpdatedEventArgs(S7FeatureTuple featureTuple)
        {
            FeatureTuple = featureTuple ?? throw new ArgumentNullException(nameof(featureTuple));
        }
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
        event EventHandler<S7FeatureUpdatedEventArgs> FeatureUpdated;
    }

    /// <summary>
    /// Breadth analysis configuration (optional add-on)
    /// </summary>
    public class BreadthConfiguration
    {
        public bool Enabled { get; set; }
        public decimal AdvanceDeclineThreshold { get; set; } = 0.75m;
        public decimal NewHighsLowsRatio { get; set; } = 2.0m;
        public decimal SectorRotationWeight { get; set; } = 0.25m;
        public int BreadthLookbackBars { get; set; } = 20;
        public string DataSource { get; set; } = "IndexBreadthFeed";
        public bool FailOnMissingData { get; set; }
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