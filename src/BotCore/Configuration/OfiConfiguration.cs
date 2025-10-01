using System;

namespace BotCore.Configuration
{
    /// <summary>
    /// Configuration for Order Flow Imbalance (OFI) Proxy Resolver
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class OfiConfiguration
    {
        /// <summary>
        /// Number of bars to look back for OFI calculation - moved from hardcoded constant
        /// </summary>
        public int LookbackBars { get; set; } = 20;
        
        /// <summary>
        /// Minimum number of data points required for valid calculations
        /// </summary>
        public int MinDataPointsRequired { get; set; } = 2;
        
        /// <summary>
        /// Safe zero value used when calculations result in invalid values
        /// </summary>
        public double SafeZeroValue { get; set; }
        
        /// <summary>
        /// Buffer size for history management and memory efficiency
        /// </summary>
        public int HistoryBufferSize { get; set; } = 5;
        
        /// <summary>
        /// Enable fail-closed behavior for missing data
        /// </summary>
        public bool FailOnMissingData { get; set; } = true;
        
        /// <summary>
        /// Telemetry prefix for OFI-related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "ofi";
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (LookbackBars <= 0)
                throw new ArgumentOutOfRangeException(nameof(LookbackBars), "LookbackBars must be greater than 0");
                
            if (MinDataPointsRequired <= 0)
                throw new ArgumentOutOfRangeException(nameof(MinDataPointsRequired), "MinDataPointsRequired must be greater than 0");
                
            if (HistoryBufferSize < 0)
                throw new ArgumentOutOfRangeException(nameof(HistoryBufferSize), "HistoryBufferSize must be non-negative");
                
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new ArgumentException("TelemetryPrefix cannot be null or empty", nameof(TelemetryPrefix));
        }
    }
}