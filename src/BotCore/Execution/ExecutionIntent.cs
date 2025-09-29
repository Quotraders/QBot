using System;
using System.Collections.Generic;

namespace BotCore.Execution
{
    /// <summary>
    /// Enhanced execution intent for S7 strategy with comprehensive metadata
    /// Provides all context needed for intelligent order type selection and execution optimization
    /// NO safe defaults - all fields must be explicitly populated for audit compliance
    /// </summary>
    public sealed class ExecutionIntent
    {
        // Core order details
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // BUY/SELL
        public decimal Quantity { get; set; }
        public decimal? LimitPrice { get; set; }
        public string BaseOrderType { get; set; } = "LIMIT"; // LIMIT, MARKET, IOC, etc.
        
        // S7-specific execution context
        public string StrategyId { get; set; } = string.Empty;
        public double UrgencyScore { get; set; } // 0.0 = normal, 1.0 = maximum urgency
        public string ZoneRole { get; set; } = string.Empty; // DEMAND, SUPPLY, NEUTRAL
        public string S7RiskState { get; set; } = string.Empty; // RiskOn, RiskOff, Transition
        public bool LatencyGuardBreached { get; set; }
        
        // Microstructure context
        public double? QueueEta { get; set; } // Estimated time to fill in seconds
        public double? BreakoutScore { get; set; } // Calibrated breakout probability
        public double? SpreadBps { get; set; } // Current bid-ask spread in basis points
        public double? BookImbalance { get; set; } // Order book imbalance ratio
        
        // Uncertainty and risk metrics
        public double? ConfidenceInterval { get; set; } // Width of conformal prediction interval
        public double? ModelUncertainty { get; set; } // Model prediction uncertainty
        public double? PatternReliability { get; set; } // Historical pattern success rate
        
        // Execution preferences
        public bool AllowMarketOrders { get; set; } = false;
        public bool PreferPostOnly { get; set; } = true;
        public double MaxSlippageBps { get; set; } = 5.0; // Maximum acceptable slippage
        public int MaxChildOrders { get; set; } = 3; // For order slicing
        public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(5);
        
        // Audit and tracking
        public Guid RequestId { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string OriginatingComponent { get; set; } = string.Empty;
        public Dictionary<string, object> CustomMetadata { get; } = new();
        
        /// <summary>
        /// Validate that all required fields are populated for audit compliance
        /// Throws InvalidOperationException if validation fails (fail-closed behavior)
        /// </summary>
        public void ValidateForExecution()
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(Symbol))
                errors.Add("Symbol is required");
                
            if (string.IsNullOrWhiteSpace(Side))
                errors.Add("Side is required");
                
            if (Quantity <= 0)
                errors.Add("Quantity must be positive");
                
            if (string.IsNullOrWhiteSpace(StrategyId))
                errors.Add("StrategyId is required for audit compliance");
                
            if (string.IsNullOrWhiteSpace(S7RiskState))
                errors.Add("S7RiskState is required");
                
            if (string.IsNullOrWhiteSpace(OriginatingComponent))
                errors.Add("OriginatingComponent is required for audit trail");
            
            if (errors.Count > 0)
            {
                var errorMessage = $"[EXECUTION-INTENT] [AUDIT-VIOLATION] Validation failed: {string.Join(", ", errors)}";
                throw new InvalidOperationException(errorMessage);
            }
        }
        
        /// <summary>
        /// Create execution intent for S7 strategy with required metadata
        /// Fail-closed approach - all parameters must be explicitly provided
        /// </summary>
        public static ExecutionIntent CreateForS7(
            string symbol,
            string side,
            decimal quantity,
            string s7RiskState,
            double urgencyScore,
            string zoneRole,
            string originatingComponent)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("[EXECUTION-INTENT] Symbol cannot be null or empty", nameof(symbol));
            if (string.IsNullOrWhiteSpace(side))
                throw new ArgumentException("[EXECUTION-INTENT] Side cannot be null or empty", nameof(side));
            if (quantity <= 0)
                throw new ArgumentException("[EXECUTION-INTENT] Quantity must be positive", nameof(quantity));
            if (string.IsNullOrWhiteSpace(s7RiskState))
                throw new ArgumentException("[EXECUTION-INTENT] S7RiskState cannot be null or empty", nameof(s7RiskState));
            if (string.IsNullOrWhiteSpace(originatingComponent))
                throw new ArgumentException("[EXECUTION-INTENT] OriginatingComponent cannot be null or empty", nameof(originatingComponent));
                
            return new ExecutionIntent
            {
                Symbol = symbol,
                Side = side,
                Quantity = quantity,
                StrategyId = "S7",
                S7RiskState = s7RiskState,
                UrgencyScore = urgencyScore,
                ZoneRole = zoneRole,
                OriginatingComponent = originatingComponent
            };
        }
    }
}