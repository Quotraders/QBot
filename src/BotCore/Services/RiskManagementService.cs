using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Production risk management service - tracks risk-based trade rejections
    /// Monitors and controls trading risk exposure across all positions and strategies
    /// </summary>
    public class RiskManagementService
    {
        private readonly ILogger<RiskManagementService> _logger;
        private readonly ConcurrentDictionary<(string Symbol, string RiskType), RiskMetrics> _riskMetrics = new();

        public RiskManagementService(ILogger<RiskManagementService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get risk rejection count for a specific risk type and symbol
        /// </summary>
        public async Task<int> GetRiskRejectCountAsync(string symbol, string riskType, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var key = (symbol, riskType);
            var metrics = _riskMetrics.GetOrAdd(key, _ => new RiskMetrics(symbol, riskType));
            
            lock (metrics.Lock)
            {
                // Count rejections in the last 24 hours
                var cutoffTime = DateTime.UtcNow.AddDays(-1);
                var recentRejectCount = 0;
                
                foreach (var timestamp in metrics.RecentRejections)
                {
                    if (timestamp > cutoffTime)
                        recentRejectCount++;
                }

                _logger.LogTrace("Risk reject count for {Symbol} {RiskType}: {Count} in last 24 hours", 
                    symbol, riskType, recentRejectCount);
                    
                return recentRejectCount;
            }
        }

        /// <summary>
        /// Record a risk-based trade rejection
        /// </summary>
        public void RecordRiskRejection(string symbol, string riskType, string reason)
        {
            var key = (symbol, riskType);
            var metrics = _riskMetrics.GetOrAdd(key, _ => new RiskMetrics(symbol, riskType));
            
            lock (metrics.Lock)
            {
                metrics.TotalRejectCount++;
                metrics.RecentRejections.Add(DateTime.UtcNow);
                metrics.LastRejectionReason = reason;
                metrics.LastRejectionTime = DateTime.UtcNow;
                
                // Clean old rejections to prevent memory growth
                var cutoffTime = DateTime.UtcNow.AddDays(-7);
                metrics.RecentRejections.RemoveAll(t => t < cutoffTime);
            }

            _logger.LogWarning("Risk rejection recorded: {Symbol} {RiskType} - {Reason}", 
                symbol, riskType, reason);
        }

        /// <summary>
        /// Check if a trade should be rejected based on risk rules
        /// </summary>
        public async Task<bool> ShouldRejectTradeAsync(string symbol, decimal quantity, decimal price, string strategy, CancellationToken cancellationToken = default)
        {
            const decimal MaxPositionSize = 100m;
            const int MaxDailyRejectionsPerSymbol = 50;
            const decimal HighRiskStrategyMaxQuantity = 10m;
            
            try
            {
                // Position size limits
                if (Math.Abs(quantity) > MaxPositionSize)
                {
                    RecordRiskRejection(symbol, "position_size", $"Quantity {quantity} exceeds maximum allowed");
                    return true;
                }

                // Price validation
                if (price <= 0)
                {
                    RecordRiskRejection(symbol, "invalid_price", $"Invalid price: {price}");
                    return true;
                }

                // Daily rejection limit per symbol
                var dailyRejections = await GetRiskRejectCountAsync(symbol, "all_types", cancellationToken).ConfigureAwait(false);
                if (dailyRejections > MaxDailyRejectionsPerSymbol)
                {
                    RecordRiskRejection(symbol, "rejection_limit", "Daily rejection limit exceeded");
                    return true;
                }

                // Strategy-specific limits
                if (strategy == "high_risk" && Math.Abs(quantity) > HighRiskStrategyMaxQuantity)
                {
                    RecordRiskRejection(symbol, "strategy_limit", "High risk strategy quantity limit");
                    return true;
                }

                return false; // Trade approved
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in risk check for {Symbol}, rejecting as precaution", symbol);
                RecordRiskRejection(symbol, "system_error", "Risk check system error");
                return true; // Fail safe - reject on error
            }
        }

        /// <summary>
        /// Get comprehensive risk metrics for a symbol
        /// </summary>
        public RiskSummary GetRiskSummary(string symbol)
        {
            var allRiskTypes = new[] { "position_size", "invalid_price", "rejection_limit", "strategy_limit", "system_error", "all_types" };
            
            var summary = new RiskSummary { Symbol = symbol };
            
            foreach (var riskType in allRiskTypes)
            {
                var key = (symbol, riskType);
                if (_riskMetrics.TryGetValue(key, out var metrics))
                {
                    lock (metrics.Lock)
                    {
                        var cutoffTime = DateTime.UtcNow.AddDays(-1);
                        var recentCount = 0;
                        
                        foreach (var timestamp in metrics.RecentRejections)
                        {
                            if (timestamp > cutoffTime)
                                recentCount++;
                        }

                        summary.RiskCounts[riskType] = recentCount;
                        summary.TotalRejections += recentCount;
                    }
                }
            }

            return summary;
        }
    }

    /// <summary>
    /// Internal risk metrics for a specific symbol and risk type
    /// </summary>
    internal sealed class RiskMetrics
    {
        public string Symbol { get; }
        public string RiskType { get; }
        public int TotalRejectCount { get; set; }
        public System.Collections.Generic.List<DateTime> RecentRejections { get; } = new();
        public string? LastRejectionReason { get; set; }
        public DateTime? LastRejectionTime { get; set; }
        public object Lock { get; } = new();

        public RiskMetrics(string symbol, string riskType)
        {
            Symbol = symbol;
            RiskType = riskType;
        }
    }

    /// <summary>
    /// Risk summary for monitoring
    /// </summary>
    public class RiskSummary
    {
        public string Symbol { get; set; } = string.Empty;
        public System.Collections.Generic.Dictionary<string, int> RiskCounts { get; } = new();
        public int TotalRejections { get; set; }
    }
}