using System;
using System.Collections.Generic;

namespace BotCore.Execution
{
    /// <summary>
    /// Microstructure snapshot for execution decision making
    /// Provides real-time market microstructure data for order type selection
    /// All data must be audit-clean with no safe defaults
    /// </summary>
    public sealed class MicrostructureSnapshot
    {
        // S109: Microstructure analysis constants
        private const int MidPriceDivisor = 2;                        // Divisor for mid-price calculation
        private const decimal BasisPointsMultiplier = 10000m;         // Multiplier to convert to basis points
        private const int DataStalenessMinutes = 5;                   // Maximum age for microstructure data
        private const decimal WideSpreadsThreshold = 2.0m;            // Spread threshold for maker orders (bps)
        private const double HighBookImbalanceThreshold = 0.3;        // Book imbalance threshold
        private const double HighUrgencyThreshold = 0.7;              // Urgency score threshold for aggressive execution
        private const double ZoneBreakoutThreshold = 0.8;             // Zone breakout probability threshold
        private const double MaxQueueEtaSeconds = 30.0;               // Maximum queue ETA for limit orders
        
        // Price and spread data
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal LastPrice { get; set; }
        public decimal MidPrice => (BidPrice + AskPrice) / MidPriceDivisor;
        public decimal SpreadBps => BidPrice > 0 ? ((AskPrice - BidPrice) / MidPrice) * BasisPointsMultiplier : 0m;
        
        // Order book depth
        public long BidSize { get; set; }
        public long AskSize { get; set; }
        public double BookImbalance => (BidSize + AskSize) > 0 ? (double)(BidSize - AskSize) / (BidSize + AskSize) : 0.0;
        
        // Volume and flow metrics
        public long RecentVolume { get; set; } // Volume in last N seconds
        public double VolumeImbalance { get; set; } // Buy vs sell volume imbalance
        public double OfiProxy { get; set; } // Order flow imbalance proxy
        
        // Zone context from zone provider
        public string ZoneRole { get; set; } = string.Empty; // DEMAND, SUPPLY, NEUTRAL
        public decimal? DistanceToZoneAtr { get; set; } // Distance to nearest significant zone in ATR units
        public double? ZoneBreakoutScore { get; set; } // Probability of zone breakout
        
        // Liquidity and execution metrics
        public double? EstimatedQueueEta { get; set; } // Estimated time to fill for limit orders (seconds)
        public double? HistoricalFillRate { get; set; } // Recent fill rate for this symbol
        public double? AverageSlippageBps { get; set; } // Recent average slippage
        
        // Market regime context
        public string MarketRegime { get; set; } = string.Empty; // TREND, RANGE, BREAKOUT
        public double VolatilityRank { get; set; } // Percentile rank of recent volatility
        public bool IsHighVolatility { get; set; }
        
        // Timestamp and metadata
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Symbol { get; set; } = string.Empty;
        public Dictionary<string, object> CustomMetrics { get; } = new();
        
        /// <summary>
        /// Validate microstructure snapshot for execution use
        /// Fail-closed approach - missing critical data throws exception
        /// </summary>
        public void ValidateForExecution()
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(Symbol))
                errors.Add("Symbol is required");
                
            if (BidPrice <= 0 || AskPrice <= 0)
                errors.Add("Valid bid/ask prices required");
                
            if (AskPrice <= BidPrice)
                errors.Add("Ask price must be greater than bid price");
                
            if (Timestamp < DateTime.UtcNow.AddMinutes(-DataStalenessMinutes))
                errors.Add($"Microstructure data is stale (older than {DataStalenessMinutes} minutes)");
                
            if (string.IsNullOrWhiteSpace(ZoneRole))
                errors.Add("ZoneRole is required for S7 execution");
                
            if (string.IsNullOrWhiteSpace(MarketRegime))
                errors.Add("MarketRegime is required for execution context");
            
            if (errors.Count > 0)
            {
                var errorMessage = $"[MICROSTRUCTURE-SNAPSHOT] [AUDIT-VIOLATION] Validation failed: {string.Join(", ", errors)}";
                throw new InvalidOperationException(errorMessage);
            }
        }
        
        /// <summary>
        /// Check if conditions favor maker orders (limit/post-only)
        /// Based on spread, zone context, and market regime
        /// </summary>
        public bool FavorsMakerOrders()
        {
            // Wide spreads favor maker orders
            if (SpreadBps > WideSpreadsThreshold) return true;
            
            // Inside zone bands with stable regime favors maker
            if (ZoneRole == "NEUTRAL" && MarketRegime == "RANGE") return true;
            
            // High book imbalance against our direction favors maker
            if (Math.Abs(BookImbalance) > HighBookImbalanceThreshold) return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if conditions require aggressive execution (IOC/market)
        /// Based on breakout conditions, urgency, and latency
        /// </summary>
        public bool RequiresAggressiveExecution(double urgencyScore)
        {
            // High urgency always requires aggressive execution
            if (urgencyScore > HighUrgencyThreshold) return true;
            
            // Strong breakout signals require aggressive execution
            if (ZoneBreakoutScore.HasValue && ZoneBreakoutScore.Value > ZoneBreakoutThreshold) return true;
            
            // High volatility with trend favors aggressive execution
            if (IsHighVolatility && MarketRegime == "BREAKOUT") return true;
            
            // Poor queue conditions require aggressive execution
            if (EstimatedQueueEta.HasValue && EstimatedQueueEta.Value > MaxQueueEtaSeconds) return true;
            
            return false;
        }
    }
}