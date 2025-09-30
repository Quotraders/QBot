using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Models;

namespace BotCore.Services
{
    /// <summary>
    /// Production execution analytics service - tracks execution performance metrics
    /// Provides real-time slippage and fill rate analysis for trading operations
    /// </summary>
    public class ExecutionAnalyticsService
    {
        private readonly ILogger<ExecutionAnalyticsService> _logger;
        private readonly List<ExecutionRecord> _executionHistory = new();
        private readonly object _executionLock = new();

        public ExecutionAnalyticsService(ILogger<ExecutionAnalyticsService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get average execution slippage for a symbol over recent trades
        /// </summary>
        public async Task<double> GetAverageSlippageAsync(string symbol, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            lock (_executionLock)
            {
                var recentExecutions = _executionHistory
                    .Where(e => e.Symbol == symbol && e.Timestamp > DateTime.UtcNow.AddDays(-1))
                    .ToList();

                if (recentExecutions.Count == 0)
                {
                    _logger.LogWarning("No recent execution data for {Symbol}, using estimated slippage", symbol);
                    // Return market-based estimate for the symbol
                    return symbol.StartsWith("ES") ? 0.25 : 0.05; // ES has higher slippage than typical stocks
                }

                var avgSlippage = recentExecutions.Average(e => e.SlippageBasisPoints);
                _logger.LogTrace("Average slippage for {Symbol}: {Slippage} bps from {Count} executions", 
                    symbol, avgSlippage, recentExecutions.Count);
                
                return avgSlippage;
            }
        }

        /// <summary>
        /// Get fill rate for a symbol based on recent order attempts
        /// </summary>
        public async Task<double> GetFillRateAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            lock (_executionLock)
            {
                var recentOrders = _executionHistory
                    .Where(e => e.Symbol == symbol && e.Timestamp > DateTime.UtcNow.AddDays(-1))
                    .ToList();

                if (recentOrders.Count == 0)
                {
                    _logger.LogWarning("No recent order data for {Symbol}, using market estimate", symbol);
                    // Return market-based estimate
                    return 0.94; // 94% fill rate is typical for liquid markets
                }

                var fillRate = (double)recentOrders.Count(e => e.WasFilled) / recentOrders.Count;
                _logger.LogTrace("Fill rate for {Symbol}: {FillRate:P2} from {Count} orders", 
                    symbol, fillRate, recentOrders.Count);
                
                return fillRate;
            }
        }

        /// <summary>
        /// Record an execution for analytics tracking
        /// </summary>
        public void RecordExecution(string symbol, decimal executedPrice, decimal requestedPrice, bool wasFilled)
        {
            var slippage = wasFilled ? Math.Abs((double)((executedPrice - requestedPrice) / requestedPrice * 10000)) : 0;
            
            var record = new ExecutionRecord
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow,
                ExecutedPrice = executedPrice,
                RequestedPrice = requestedPrice,
                SlippageBasisPoints = slippage,
                WasFilled = wasFilled
            };

            lock (_executionLock)
            {
                _executionHistory.Add(record);
                
                // Keep only last 1000 records to prevent memory growth
                if (_executionHistory.Count > 1000)
                {
                    _executionHistory.RemoveAt(0);
                }
            }

            _logger.LogDebug("Recorded execution: {Symbol} {Price:F2} slippage={Slippage:F2}bps filled={Filled}", 
                symbol, executedPrice, slippage, wasFilled);
        }
    }

    /// <summary>
    /// Execution record for performance tracking
    /// </summary>
    internal sealed class ExecutionRecord
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal ExecutedPrice { get; set; }
        public decimal RequestedPrice { get; set; }
        public double SlippageBasisPoints { get; set; }
        public bool WasFilled { get; set; }
    }
}