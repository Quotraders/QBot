using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BotCore.Services
{
    /// <summary>
    /// PHASE 1: Order Execution Metrics - Tracks execution performance
    /// Monitors latency, slippage, and fill statistics for order execution
    /// </summary>
    public class OrderExecutionMetrics
    {
        private readonly ILogger<OrderExecutionMetrics> _logger;
        private readonly ConcurrentQueue<ExecutionLatencyRecord> _latencyRecords = new();
        private readonly ConcurrentQueue<SlippageRecord> _slippageRecords = new();
        private readonly ConcurrentDictionary<string, FillStatistics> _fillStats = new();
        private readonly object _metricsLock = new();
        
        // Configuration
        private const int MaxRecordsToKeep = 1000;
        private const int StatisticsWindowMinutes = 60;
        private const double PercentageConversionFactor = 100.0; // Convert decimal to percentage
        private const double Percentile95 = 95.0; // 95th percentile for latency calculations
        
        public OrderExecutionMetrics(ILogger<OrderExecutionMetrics> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        // ========================================================================
        // LATENCY TRACKING
        // ========================================================================
        
        /// <summary>
        /// Record order execution latency (time from order placement to fill)
        /// </summary>
        public void RecordExecutionLatency(string orderId, string symbol, DateTime orderPlacedTime, DateTime fillTime)
        {
            var latencyMs = (fillTime - orderPlacedTime).TotalMilliseconds;
            
            var record = new ExecutionLatencyRecord
            {
                OrderId = orderId,
                Symbol = symbol,
                OrderPlacedTime = orderPlacedTime,
                FillTime = fillTime,
                LatencyMs = latencyMs,
                Timestamp = DateTime.UtcNow
            };
            
            _latencyRecords.Enqueue(record);
            TrimOldRecords();
            
            _logger.LogDebug("[METRICS] Execution latency recorded: {OrderId} {Symbol} {Latency:F2}ms", 
                orderId, symbol, latencyMs);
        }
        
        /// <summary>
        /// Get average execution latency for a symbol
        /// </summary>
        public double GetAverageLatencyMs(string symbol)
        {
            var recentRecords = _latencyRecords
                .Where(r => r.Symbol == symbol && r.Timestamp > DateTime.UtcNow.AddMinutes(-StatisticsWindowMinutes))
                .ToList();
            
            if (recentRecords.Count == 0)
            {
                return 0.0;
            }
            
            return recentRecords.Average(r => r.LatencyMs);
        }
        
        /// <summary>
        /// Get latency percentile (e.g., 95th percentile)
        /// </summary>
        public double GetLatencyPercentile(string symbol, double percentile)
        {
            var recentRecords = _latencyRecords
                .Where(r => r.Symbol == symbol && r.Timestamp > DateTime.UtcNow.AddMinutes(-StatisticsWindowMinutes))
                .OrderBy(r => r.LatencyMs)
                .ToList();
            
            if (recentRecords.Count == 0)
            {
                return 0.0;
            }
            
            var index = (int)Math.Ceiling(percentile / 100.0 * recentRecords.Count) - 1;
            index = Math.Max(0, Math.Min(index, recentRecords.Count - 1));
            
            return recentRecords[index].LatencyMs;
        }
        
        // ========================================================================
        // SLIPPAGE TRACKING
        // ========================================================================
        
        /// <summary>
        /// Record slippage (difference between expected and actual fill price)
        /// </summary>
        public void RecordSlippage(string orderId, string symbol, decimal expectedPrice, decimal actualFillPrice, int quantity)
        {
            var slippageTicks = Math.Abs(actualFillPrice - expectedPrice);
            var slippagePercent = expectedPrice != 0 ? (double)((actualFillPrice - expectedPrice) / expectedPrice * 100) : 0;
            
            var record = new SlippageRecord
            {
                OrderId = orderId,
                Symbol = symbol,
                ExpectedPrice = expectedPrice,
                ActualFillPrice = actualFillPrice,
                SlippageTicks = slippageTicks,
                SlippagePercent = slippagePercent,
                Quantity = quantity,
                Timestamp = DateTime.UtcNow
            };
            
            _slippageRecords.Enqueue(record);
            TrimOldRecords();
            
            _logger.LogDebug("[METRICS] Slippage recorded: {OrderId} {Symbol} expected=${Expected:F2} actual=${Actual:F2} slippage={Slippage:F4}%",
                orderId, symbol, expectedPrice, actualFillPrice, slippagePercent);
        }
        
        /// <summary>
        /// Get average slippage for a symbol (in percentage)
        /// </summary>
        public double GetAverageSlippagePercent(string symbol)
        {
            var recentRecords = _slippageRecords
                .Where(r => r.Symbol == symbol && r.Timestamp > DateTime.UtcNow.AddMinutes(-StatisticsWindowMinutes))
                .ToList();
            
            if (recentRecords.Count == 0)
            {
                return 0.0;
            }
            
            return recentRecords.Average(r => r.SlippagePercent);
        }
        
        // ========================================================================
        // FILL STATISTICS TRACKING
        // ========================================================================
        
        /// <summary>
        /// Record order placement
        /// </summary>
        public void RecordOrderPlaced(string symbol)
        {
            var stats = _fillStats.GetOrAdd(symbol, _ => new FillStatistics { Symbol = symbol });
            
            lock (_metricsLock)
            {
                stats.TotalOrders++;
            }
            
            _logger.LogTrace("[METRICS] Order placed count updated: {Symbol} total={Total}", symbol, stats.TotalOrders);
        }
        
        /// <summary>
        /// Record order fill
        /// </summary>
        public void RecordOrderFilled(string symbol, bool isPartialFill)
        {
            var stats = _fillStats.GetOrAdd(symbol, _ => new FillStatistics { Symbol = symbol });
            
            lock (_metricsLock)
            {
                stats.TotalFills++;
                if (isPartialFill)
                {
                    stats.PartialFills++;
                }
                stats.LastFillTime = DateTime.UtcNow;
            }
            
            _logger.LogTrace("[METRICS] Fill count updated: {Symbol} fills={Fills} partial={Partial}", 
                symbol, stats.TotalFills, stats.PartialFills);
        }
        
        /// <summary>
        /// Record order rejection
        /// </summary>
        public void RecordOrderRejected(string symbol, string reason)
        {
            var stats = _fillStats.GetOrAdd(symbol, _ => new FillStatistics { Symbol = symbol });
            
            lock (_metricsLock)
            {
                stats.TotalRejections++;
                stats.LastRejectionTime = DateTime.UtcNow;
                stats.LastRejectionReason = reason;
            }
            
            _logger.LogTrace("[METRICS] Rejection count updated: {Symbol} rejections={Rejections} reason={Reason}",
                symbol, stats.TotalRejections, reason);
        }
        
        /// <summary>
        /// Get fill statistics for a symbol
        /// </summary>
        public FillStatistics? GetFillStatistics(string symbol)
        {
            return _fillStats.TryGetValue(symbol, out var stats) ? stats : null;
        }
        
        /// <summary>
        /// Get comprehensive metrics summary
        /// </summary>
        public OrderExecutionMetricsSummary GetMetricsSummary(string symbol)
        {
            var stats = GetFillStatistics(symbol) ?? new FillStatistics { Symbol = symbol };
            
            return new OrderExecutionMetricsSummary
            {
                Symbol = symbol,
                TotalOrders = stats.TotalOrders,
                TotalFills = stats.TotalFills,
                PartialFills = stats.PartialFills,
                TotalRejections = stats.TotalRejections,
                FillRate = stats.TotalOrders > 0 ? (double)stats.TotalFills / stats.TotalOrders * PercentageConversionFactor : 0,
                AverageLatencyMs = GetAverageLatencyMs(symbol),
                Latency95thPercentileMs = GetLatencyPercentile(symbol, Percentile95),
                AverageSlippagePercent = GetAverageSlippagePercent(symbol),
                LastFillTime = stats.LastFillTime,
                LastRejectionTime = stats.LastRejectionTime,
                LastRejectionReason = stats.LastRejectionReason
            };
        }
        
        // ========================================================================
        // HELPER METHODS
        // ========================================================================
        
        private void TrimOldRecords()
        {
            // Trim latency records
            while (_latencyRecords.Count > MaxRecordsToKeep)
            {
                _latencyRecords.TryDequeue(out _);
            }
            
            // Trim slippage records
            while (_slippageRecords.Count > MaxRecordsToKeep)
            {
                _slippageRecords.TryDequeue(out _);
            }
        }
    }
    
    // ========================================================================
    // DATA CLASSES
    // ========================================================================
    
    /// <summary>
    /// Execution latency record
    /// </summary>
    internal sealed class ExecutionLatencyRecord
    {
        public string OrderId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public DateTime OrderPlacedTime { get; set; }
        public DateTime FillTime { get; set; }
        public double LatencyMs { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Slippage record
    /// </summary>
    internal sealed class SlippageRecord
    {
        public string OrderId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public decimal ExpectedPrice { get; set; }
        public decimal ActualFillPrice { get; set; }
        public decimal SlippageTicks { get; set; }
        public double SlippagePercent { get; set; }
        public int Quantity { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Fill statistics for a symbol
    /// </summary>
    public class FillStatistics
    {
        public string Symbol { get; set; } = string.Empty;
        public int TotalOrders { get; set; }
        public int TotalFills { get; set; }
        public int PartialFills { get; set; }
        public int TotalRejections { get; set; }
        public DateTime? LastFillTime { get; set; }
        public DateTime? LastRejectionTime { get; set; }
        public string LastRejectionReason { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Comprehensive metrics summary
    /// </summary>
    public class OrderExecutionMetricsSummary
    {
        public string Symbol { get; set; } = string.Empty;
        public int TotalOrders { get; set; }
        public int TotalFills { get; set; }
        public int PartialFills { get; set; }
        public int TotalRejections { get; set; }
        public double FillRate { get; set; }
        public double AverageLatencyMs { get; set; }
        public double Latency95thPercentileMs { get; set; }
        public double AverageSlippagePercent { get; set; }
        public DateTime? LastFillTime { get; set; }
        public DateTime? LastRejectionTime { get; set; }
        public string LastRejectionReason { get; set; } = string.Empty;
    }
}
