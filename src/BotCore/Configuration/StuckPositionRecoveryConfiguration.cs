using System;

namespace BotCore.Configuration
{
    /// <summary>
    /// Configuration for the three-layer stuck position recovery system
    /// </summary>
    public class StuckPositionRecoveryConfiguration
    {
        /// <summary>
        /// How often to run position reconciliation checks (Layer 1)
        /// </summary>
        public int ReconciliationIntervalSeconds { get; set; } = 60;
        
        /// <summary>
        /// How often to check for stuck positions (Layer 2)
        /// </summary>
        public int MonitorCheckIntervalSeconds { get; set; } = 30;
        
        /// <summary>
        /// Time before escalating from Level 1 to Level 2
        /// </summary>
        public int Level1TimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// Time before escalating from Level 2 to Level 3
        /// </summary>
        public int Level2TimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// Time before escalating from Level 3 to Level 4
        /// </summary>
        public int Level3TimeoutSeconds { get; set; } = 60;
        
        /// <summary>
        /// Time before escalating from Level 4 to Level 5
        /// </summary>
        public int Level4TimeoutSeconds { get; set; } = 180;
        
        /// <summary>
        /// Maximum position age in hours before forced close (strategy-specific override)
        /// </summary>
        public int MaxPositionAgeHours { get; set; } = 4;
        
        /// <summary>
        /// Runaway loss threshold in USD (emergency close if exceeded)
        /// </summary>
        public decimal RunawayLossThresholdUsd { get; set; } = -500m;
        
        /// <summary>
        /// Maximum number of market order attempts before giving up
        /// </summary>
        public int MaxRecoveryAttempts { get; set; } = 10;
        
        /// <summary>
        /// Email address for critical alerts
        /// </summary>
        public string? EmergencyEmailAddress { get; set; }
        
        /// <summary>
        /// Slack webhook URL for critical alerts
        /// </summary>
        public string? SlackWebhookUrl { get; set; }
        
        /// <summary>
        /// Enable SMS alerts for Level 4+ escalations
        /// </summary>
        public bool EnableSmsAlerts { get; set; } = false;
        
        /// <summary>
        /// Consider exit order "stuck" if this many minutes old without fill
        /// </summary>
        public int StuckExitMinutesThreshold { get; set; } = 5;
        
        /// <summary>
        /// Require this many minutes of no exit attempts to classify as stuck
        /// </summary>
        public int MinutesSinceLastExitAttempt { get; set; } = 2;
        
        /// <summary>
        /// Enable the stuck position recovery system
        /// </summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Enable logging of recovery incidents to database
        /// </summary>
        public bool EnableIncidentLogging { get; set; } = true;
        
        /// <summary>
        /// Directory for recovery incident logs
        /// </summary>
        public string IncidentLogDirectory { get; set; } = "state/recovery_incidents";
        
        /// <summary>
        /// Validate configuration values
        /// </summary>
        public void Validate()
        {
            if (ReconciliationIntervalSeconds <= 0)
                throw new InvalidOperationException("ReconciliationIntervalSeconds must be positive");
            
            if (MonitorCheckIntervalSeconds <= 0)
                throw new InvalidOperationException("MonitorCheckIntervalSeconds must be positive");
            
            if (Level1TimeoutSeconds <= 0 || Level2TimeoutSeconds <= 0 || 
                Level3TimeoutSeconds <= 0 || Level4TimeoutSeconds <= 0)
                throw new InvalidOperationException("All escalation timeouts must be positive");
            
            if (MaxPositionAgeHours <= 0)
                throw new InvalidOperationException("MaxPositionAgeHours must be positive");
            
            if (RunawayLossThresholdUsd >= 0)
                throw new InvalidOperationException("RunawayLossThresholdUsd must be negative");
            
            if (MaxRecoveryAttempts <= 0)
                throw new InvalidOperationException("MaxRecoveryAttempts must be positive");
        }
    }
}
