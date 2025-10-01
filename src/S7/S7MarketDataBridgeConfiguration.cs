using System;

namespace TradingBot.S7
{
    /// <summary>
    /// Configuration for S7 Market Data Bridge
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class S7MarketDataBridgeConfiguration
    {
        /// <summary>
        /// Enable fail-closed behavior when enhanced market data service is missing
        /// </summary>
        public bool FailOnMissingEnhancedService { get; set; } = true;
        
        /// <summary>
        /// Enable telemetry when price extraction falls back to reflection
        /// </summary>
        public bool EnableReflectionFallbackTelemetry { get; set; } = true;
        
        /// <summary>
        /// Expected enhanced market data service type name
        /// </summary>
        public string EnhancedServiceTypeName { get; set; } = "IEnhancedMarketDataFlowService";
        
        /// <summary>
        /// Event name to subscribe to for market data
        /// </summary>
        public string MarketDataEventName { get; set; } = "OnMarketDataReceived";
        
        /// <summary>
        /// Telemetry prefix for market data bridge related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "s7_bridge";
        
        /// <summary>
        /// Enable configuration gating for enhanced market data service
        /// </summary>
        public bool EnableConfigurationGating { get; set; } = true;
        
        /// <summary>
        /// Timeout for market data processing operations (in milliseconds)
        /// </summary>
        public int ProcessingTimeoutMs { get; set; } = 5000;
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(EnhancedServiceTypeName))
                throw new ArgumentException("EnhancedServiceTypeName cannot be null or empty", nameof(EnhancedServiceTypeName));
                
            if (string.IsNullOrWhiteSpace(MarketDataEventName))
                throw new ArgumentException("MarketDataEventName cannot be null or empty", nameof(MarketDataEventName));
                
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new ArgumentException("TelemetryPrefix cannot be null or empty", nameof(TelemetryPrefix));
                
            if (ProcessingTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(ProcessingTimeoutMs), "ProcessingTimeoutMs must be greater than 0");
        }
    }
}