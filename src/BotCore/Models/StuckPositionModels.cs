using System;
using System.Collections.Generic;

namespace BotCore.Models
{
    /// <summary>
    /// Classification of why a position is stuck
    /// </summary>
    public enum StuckPositionClassification
    {
        /// <summary>
        /// Position is healthy, no recovery needed
        /// </summary>
        Healthy,
        
        /// <summary>
        /// Exit order failed and position still open
        /// </summary>
        StuckExit,
        
        /// <summary>
        /// Position held longer than maximum allowed time
        /// </summary>
        AgedOut,
        
        /// <summary>
        /// Unrealized P&L below emergency threshold
        /// </summary>
        RunawayLoss,
        
        /// <summary>
        /// Ghost position - broker has it but bot doesn't know about it
        /// </summary>
        GhostPosition
    }
    
    /// <summary>
    /// Recovery escalation level
    /// </summary>
    public enum RecoveryLevel
    {
        None = 0,
        SmartRetry = 1,
        FreshStart = 2,
        MarketOrder = 3,
        HumanEscalation = 4,
        SystemShutdown = 5
    }
    
    /// <summary>
    /// Alert for a stuck position that needs recovery
    /// </summary>
    public class StuckPositionAlert
    {
        public string PositionId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public bool IsLong { get; set; }
        public DateTime EntryTimestamp { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public StuckPositionClassification Classification { get; set; }
        public DateTime DetectionTimestamp { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<ExitAttempt> ExitAttempts { get; set; } = new();
    }
    
    /// <summary>
    /// Record of an exit attempt
    /// </summary>
    public class ExitAttempt
    {
        public DateTime Timestamp { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
    }
    
    /// <summary>
    /// Recovery state for a position undergoing emergency exit
    /// </summary>
    public class PositionRecoveryState
    {
        public string PositionId { get; set; } = string.Empty;
        public StuckPositionAlert Alert { get; set; } = new();
        public RecoveryLevel CurrentLevel { get; set; }
        public DateTime RecoveryStartTime { get; set; }
        public DateTime LastEscalationTime { get; set; }
        public int AttemptCount { get; set; }
        public List<RecoveryAction> Actions { get; set; } = new();
        public bool IsResolved { get; set; }
        public DateTime? ResolvedTime { get; set; }
        public decimal? FinalExitPrice { get; set; }
        public decimal? SlippageCost { get; set; }
    }
    
    /// <summary>
    /// Record of a recovery action taken
    /// </summary>
    public class RecoveryAction
    {
        public DateTime Timestamp { get; set; }
        public RecoveryLevel Level { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string? OrderId { get; set; }
        public decimal? Price { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
    
    /// <summary>
    /// Incident report for database/logging
    /// </summary>
    public class RecoveryIncident
    {
        public string IncidentId { get; set; } = Guid.NewGuid().ToString();
        public string PositionId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public int Quantity { get; set; }
        public DateTime DetectionTimestamp { get; set; }
        public StuckPositionClassification Classification { get; set; }
        public List<RecoveryAction> AllActions { get; set; } = new();
        public string FinalOutcome { get; set; } = string.Empty;
        public double TotalRecoveryTimeSeconds { get; set; }
        public decimal? SlippageCost { get; set; }
        public RecoveryLevel MaxLevelReached { get; set; }
        public bool RequiredHumanIntervention { get; set; }
    }
    
    /// <summary>
    /// Position reconciliation result
    /// </summary>
    public class PositionReconciliationResult
    {
        public DateTime Timestamp { get; set; }
        public int BrokerPositionCount { get; set; }
        public int BotPositionCount { get; set; }
        public int DiscrepancyCount { get; set; }
        public List<PositionDiscrepancy> Discrepancies { get; set; } = new();
        public List<string> ActionsTaken { get; set; } = new();
    }
    
    /// <summary>
    /// Discrepancy between broker and bot position state
    /// </summary>
    public class PositionDiscrepancy
    {
        public string Symbol { get; set; } = string.Empty;
        public string DiscrepancyType { get; set; } = string.Empty; // "BrokerOnly", "BotOnly", "QuantityMismatch"
        public int? BrokerQuantity { get; set; }
        public decimal? BrokerAvgPrice { get; set; }
        public int? BotQuantity { get; set; }
        public decimal? BotAvgPrice { get; set; }
        public string Resolution { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Broker position information from TopstepX
    /// </summary>
    public class BrokerPosition
    {
        public string Symbol { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}
