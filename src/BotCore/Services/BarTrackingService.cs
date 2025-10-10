using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Models;

namespace BotCore.Services
{
    /// <summary>
    /// Production bar tracking service - tracks bar processing metrics and counts
    /// Monitors bar ingestion and processing for trading system health
    /// </summary>
    public class BarTrackingService
    {
        // Metrics calculation constants
        private const double ProcessingRateWindowMinutes = 30.0;
        
        private readonly ILogger<BarTrackingService> _logger;
        private readonly ConcurrentDictionary<string, BarMetrics> _symbolMetrics = new();

        public BarTrackingService(ILogger<BarTrackingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get count of recently processed bars for a symbol
        /// </summary>
        public async Task<int> GetRecentBarCountAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var metrics = _symbolMetrics.GetOrAdd(symbol, s => new BarMetrics(s));
            
            lock (metrics.Lock)
            {
                // Count bars processed in the last 30 minutes
                var cutoffTime = DateTime.UtcNow.AddMinutes(-30);
                var recentCount = 0;
                
                foreach (var timestamp in metrics.RecentBarTimestamps)
                {
                    if (timestamp > cutoffTime)
                        recentCount++;
                }

                _logger.LogTrace("Recent bar count for {Symbol}: {Count} bars in last 30 minutes", symbol, recentCount);
                return recentCount;
            }
        }

        /// <summary>
        /// Get total count of processed bars for a symbol
        /// </summary>
        public async Task<int> GetProcessedBarCountAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var metrics = _symbolMetrics.GetOrAdd(symbol, s => new BarMetrics(s));
            
            lock (metrics.Lock)
            {
                _logger.LogTrace("Total processed bar count for {Symbol}: {Count}", symbol, metrics.TotalProcessedCount);
                return metrics.TotalProcessedCount;
            }
        }

        /// <summary>
        /// Record that a bar has been processed
        /// </summary>
        public void RecordBarProcessed(string symbol, Bar bar)
        {
            var metrics = _symbolMetrics.GetOrAdd(symbol, s => new BarMetrics(s));
            
            lock (metrics.Lock)
            {
                metrics.TotalProcessedCount++;
                metrics.RecentBarTimestamps.Add(DateTime.UtcNow);
                
                // Keep only recent timestamps to prevent memory growth
                var cutoffTime = DateTime.UtcNow.AddHours(-2);
                metrics.RecentBarTimestamps.RemoveAll(t => t < cutoffTime);
                
                metrics.LastBarProcessed = bar;
                metrics.LastProcessedTime = DateTime.UtcNow;
            }

            _logger.LogDebug("Recorded bar processing for {Symbol}: total={Total}", symbol, metrics.TotalProcessedCount);
        }

        /// <summary>
        /// Record that multiple bars have been processed (batch processing)
        /// </summary>
        public void RecordBarsProcessed(string symbol, int count)
        {
            var metrics = _symbolMetrics.GetOrAdd(symbol, s => new BarMetrics(s));
            
            lock (metrics.Lock)
            {
                metrics.TotalProcessedCount += count;
                
                // Add timestamps for each bar in the batch
                var now = DateTime.UtcNow;
                for (int i = 0; i < count; i++)
                {
                    metrics.RecentBarTimestamps.Add(now);
                }
                
                // Keep only recent timestamps to prevent memory growth
                var cutoffTime = DateTime.UtcNow.AddHours(-2);
                metrics.RecentBarTimestamps.RemoveAll(t => t < cutoffTime);
                
                metrics.LastProcessedTime = now;
            }

            _logger.LogDebug("Recorded batch bar processing for {Symbol}: {Count} bars, total={Total}", 
                symbol, count, metrics.TotalProcessedCount);
        }

        /// <summary>
        /// Get comprehensive bar processing metrics for a symbol
        /// </summary>
        public BarProcessingMetrics GetBarProcessingMetrics(string symbol)
        {
            var metrics = _symbolMetrics.GetOrAdd(symbol, s => new BarMetrics(s));
            
            lock (metrics.Lock)
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-30);
                var recentCount = 0;
                
                foreach (var timestamp in metrics.RecentBarTimestamps)
                {
                    if (timestamp > cutoffTime)
                        recentCount++;
                }

                return new BarProcessingMetrics
                {
                    Symbol = symbol,
                    TotalProcessed = metrics.TotalProcessedCount,
                    RecentCount = recentCount,
                    LastProcessedTime = metrics.LastProcessedTime,
                    ProcessingRate = recentCount / ProcessingRateWindowMinutes // bars per minute over last 30 minutes
                };
            }
        }
    }

    /// <summary>
    /// Internal metrics tracking for a symbol
    /// </summary>
    internal sealed class BarMetrics
    {
        public string Symbol { get; }
        public int TotalProcessedCount { get; set; }
        public System.Collections.Generic.List<DateTime> RecentBarTimestamps { get; } = new();
        public Bar? LastBarProcessed { get; set; }
        public DateTime? LastProcessedTime { get; set; }
        public object Lock { get; } = new();

        public BarMetrics(string symbol)
        {
            Symbol = symbol;
        }
    }

    /// <summary>
    /// Bar processing metrics summary
    /// </summary>
    public class BarProcessingMetrics
    {
        public string Symbol { get; set; } = string.Empty;
        public int TotalProcessed { get; set; }
        public int RecentCount { get; set; }
        public DateTime? LastProcessedTime { get; set; }
        public double ProcessingRate { get; set; } // bars per minute
    }
}