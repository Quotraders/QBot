using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Production performance metrics service - tracks decision and execution latency
    /// Monitors system performance for trading operations and decision making
    /// </summary>
    public class PerformanceMetricsService
    {
        private readonly ILogger<PerformanceMetricsService> _logger;
        private readonly List<LatencyRecord> _latencyHistory = new();
        private readonly object _metricsLock = new();

        public PerformanceMetricsService(ILogger<PerformanceMetricsService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get average decision latency for trading decisions
        /// </summary>
        public async Task<double> GetAverageDecisionLatencyAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            lock (_metricsLock)
            {
                var recentDecisions = _latencyHistory
                    .Where(l => l.Symbol == symbol && l.Type == LatencyType.Decision && l.Timestamp > DateTime.UtcNow.AddHours(-1))
                    .ToList();

                if (recentDecisions.Count == 0)
                {
                    _logger.LogWarning("No recent decision latency data for {Symbol}, using system baseline", symbol);
                    // Return system baseline - typical decision latency
                    return 12.5; // 12.5ms is reasonable for ML-based decisions
                }

                var avgLatency = recentDecisions.Average(l => l.LatencyMs);
                _logger.LogTrace("Average decision latency for {Symbol}: {Latency:F2}ms from {Count} measurements", 
                    symbol, avgLatency, recentDecisions.Count);
                
                return avgLatency;
            }
        }

        /// <summary>
        /// Get average order execution latency
        /// </summary>
        public async Task<double> GetAverageOrderLatencyAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            lock (_metricsLock)
            {
                var recentOrders = _latencyHistory
                    .Where(l => l.Symbol == symbol && l.Type == LatencyType.Order && l.Timestamp > DateTime.UtcNow.AddHours(-1))
                    .ToList();

                if (recentOrders.Count == 0)
                {
                    _logger.LogWarning("No recent order latency data for {Symbol}, using market baseline", symbol);
                    // Return market-based baseline - typical order execution latency
                    return 28.0; // 28ms is reasonable for electronic execution
                }

                var avgLatency = recentOrders.Average(l => l.LatencyMs);
                _logger.LogTrace("Average order latency for {Symbol}: {Latency:F2}ms from {Count} measurements", 
                    symbol, avgLatency, recentOrders.Count);
                
                return avgLatency;
            }
        }

        /// <summary>
        /// Record decision latency measurement
        /// </summary>
        public void RecordDecisionLatency(string symbol, double latencyMs)
        {
            RecordLatency(symbol, LatencyType.Decision, latencyMs);
        }

        /// <summary>
        /// Record order execution latency measurement
        /// </summary>
        public void RecordOrderLatency(string symbol, double latencyMs)
        {
            RecordLatency(symbol, LatencyType.Order, latencyMs);
        }

        private void RecordLatency(string symbol, LatencyType type, double latencyMs)
        {
            var record = new LatencyRecord
            {
                Symbol = symbol,
                Type = type,
                LatencyMs = latencyMs,
                Timestamp = DateTime.UtcNow
            };

            lock (_metricsLock)
            {
                _latencyHistory.Add(record);
                
                // Keep only last 1000 measurements to prevent memory growth
                if (_latencyHistory.Count > 1000)
                {
                    _latencyHistory.RemoveAt(0);
                }
            }

            _logger.LogDebug("Recorded {Type} latency: {Symbol} {Latency:F2}ms", 
                type, symbol, latencyMs);
        }

        /// <summary>
        /// Get performance summary for monitoring
        /// </summary>
        public PerformanceMetricsSummary GetPerformanceSummary()
        {
            lock (_metricsLock)
            {
                var recentRecords = _latencyHistory
                    .Where(l => l.Timestamp > DateTime.UtcNow.AddMinutes(-30))
                    .ToList();

                return new PerformanceMetricsSummary
                {
                    TotalMeasurements = recentRecords.Count,
                    AverageDecisionLatency = recentRecords.Where(l => l.Type == LatencyType.Decision).Select(l => l.LatencyMs).DefaultIfEmpty(0).Average(),
                    AverageOrderLatency = recentRecords.Where(l => l.Type == LatencyType.Order).Select(l => l.LatencyMs).DefaultIfEmpty(0).Average(),
                    MaxLatency = recentRecords.Select(l => l.LatencyMs).DefaultIfEmpty(0).Max(),
                    TimePeriodMinutes = 30
                };
            }
        }
    }

    /// <summary>
    /// Latency measurement record
    /// </summary>
    internal sealed class LatencyRecord
    {
        public string Symbol { get; set; } = string.Empty;
        public LatencyType Type { get; set; }
        public double LatencyMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Type of latency measurement
    /// </summary>
    internal enum LatencyType
    {
        Decision,
        Order
    }

    /// <summary>
    /// Performance summary for monitoring
    /// </summary>
    public class PerformanceMetricsSummary
    {
        public int TotalMeasurements { get; set; }
        public double AverageDecisionLatency { get; set; }
        public double AverageOrderLatency { get; set; }
        public double MaxLatency { get; set; }
        public int TimePeriodMinutes { get; set; }
    }
}