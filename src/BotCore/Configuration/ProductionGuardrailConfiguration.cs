using System;

namespace BotCore.Configuration
{
    /// <summary>
    /// Configuration for Production Guardrail system
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class ProductionGuardrailConfiguration
    {
        /// <summary>
        /// Explicit configuration gate for live trading (default false for safety)
        /// </summary>
        public bool AllowLiveTrading { get; set; } = false;
        
        /// <summary>
        /// Enable order evidence validation requirements
        /// </summary>
        public bool EnableOrderEvidenceValidation { get; set; } = true;
        
        /// <summary>
        /// Enable real-time trading metrics service for posture changes
        /// </summary>
        public bool EnableRealTradingMetrics { get; set; } = true;
        
        /// <summary>
        /// Telemetry prefix for guardrail-related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "guardrails";
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new ArgumentException("TelemetryPrefix cannot be null or empty");
        }
    }
    
    /// <summary>
    /// Configuration for Kill Switch Service
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class KillSwitchConfiguration
    {
        /// <summary>
        /// Path to the kill file (supports relative and absolute paths)
        /// </summary>
        public string FilePath { get; set; } = "state/kill.txt";
        
        /// <summary>
        /// Interval for periodic kill file checks (in milliseconds)
        /// </summary>
        public int CheckIntervalMs { get; set; } = 1000;
        
        /// <summary>
        /// Create DRY_RUN marker file when kill switch is activated
        /// </summary>
        public bool CreateDryRunMarker { get; set; } = true;
        
        /// <summary>
        /// Path for DRY_RUN marker file
        /// </summary>
        public string DryRunMarkerPath { get; set; } = "state/dry_run.lock";
        
        /// <summary>
        /// Telemetry prefix for kill switch related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "killswitch";
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                throw new ArgumentException("FilePath cannot be null or empty");
                
            if (CheckIntervalMs <= 0)
                throw new ArgumentOutOfRangeException("CheckIntervalMs", "CheckIntervalMs must be greater than 0");
                
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new ArgumentException("TelemetryPrefix cannot be null or empty");
                
            if (string.IsNullOrWhiteSpace(DryRunMarkerPath))
                throw new ArgumentException("DryRunMarkerPath cannot be null or empty");
        }
    }
    
    /// <summary>
    /// Configuration for Emergency Stop System
    /// AUDIT-CLEAN: All values must come from configuration with validation
    /// </summary>
    public class EmergencyStopConfiguration
    {
        /// <summary>
        /// Monitoring interval for emergency conditions (in milliseconds)
        /// </summary>
        public int MonitoringIntervalMs { get; set; } = 1000;
        
        /// <summary>
        /// Enable emergency log file creation
        /// </summary>
        public bool EnableEmergencyLogging { get; set; } = true;
        
        /// <summary>
        /// Directory for emergency log files
        /// </summary>
        public string EmergencyLogDirectory { get; set; } = "state/emergency";
        
        /// <summary>
        /// Telemetry prefix for emergency stop related logs and metrics
        /// </summary>
        public string TelemetryPrefix { get; set; } = "emergency";
        
        /// <summary>
        /// Validation method to ensure configuration values are within acceptable bounds
        /// </summary>
        public void Validate()
        {
            if (MonitoringIntervalMs <= 0)
                throw new ArgumentOutOfRangeException("MonitoringIntervalMs", "MonitoringIntervalMs must be greater than 0");
                
            if (string.IsNullOrWhiteSpace(TelemetryPrefix))
                throw new ArgumentException("TelemetryPrefix cannot be null or empty");
                
            if (string.IsNullOrWhiteSpace(EmergencyLogDirectory))
                throw new ArgumentException("EmergencyLogDirectory cannot be null or empty");
        }
    }
}