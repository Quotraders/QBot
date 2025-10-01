using System;
using System.Collections.Generic;

namespace BotCore.Configuration
{
    /// <summary>
    /// Configuration for Bar Dispatcher Hook
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class BarDispatcherConfiguration
    {
        /// <summary>
        /// Enable fail-closed behavior when bar sources are not available
        /// </summary>
        public bool FailOnMissingBarSources { get; set; } = true;
        
        /// <summary>
        /// Expected bar source service types for configuration-driven discovery
        /// </summary>
        public IList<string> ExpectedBarSources { get; } = new List<string>
        {
            "BotCore.Services.TradingSystemBarConsumer",
            "BotCore.Services.BarTrackingService"
        };
        
        /// <summary>
        /// Primary bar source type name
        /// </summary>
        public string PrimaryBarSource { get; set; } = "BarPyramid";
        
        /// <summary>
        /// Enable explicit holds when alternative bar sources fail
        /// </summary>
        public bool EnableExplicitHolds { get; set; } = true;
        
        /// <summary>
        /// Telemetry prefix for bar dispatcher related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "bar_dispatcher";
        
        /// <summary>
        /// Enable performance monitoring and counters
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(PrimaryBarSource))
                throw new InvalidOperationException("PrimaryBarSource cannot be null or empty");
                
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new InvalidOperationException("TelemetryPrefix cannot be null or empty");
                
            if (ExpectedBarSources.Count == 0)
                throw new InvalidOperationException("ExpectedBarSources must contain at least one entry");
        }
    }
}