using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Null breadth data source for diagnostics when real breadth feed is unavailable
    /// Implements fail-closed behavior with hourly telemetry logging
    /// 
    /// Environment Variables:
    /// - BreadthFeed:Enabled=false (controls whether real breadth feed should be active)
    /// 
    /// This implementation:
    /// 1. Always returns false for IsDataAvailable() 
    /// 2. Throws InvalidOperationException for any data requests (fail-closed)
    /// 3. Logs "breadth unavailable" once per hour for telemetry
    /// 4. Prevents consumption of CSV substitute data in ProductionBreadthFeedService
    /// </summary>
    public class NullBreadthDataSource : IBreadthFeed
    {
        private readonly ILogger<NullBreadthDataSource> _logger;
        private DateTime _lastLogTime = DateTime.MinValue;
        private readonly TimeSpan _logInterval = TimeSpan.FromHours(1);

        public NullBreadthDataSource(ILogger<NullBreadthDataSource> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Always returns false - breadth data is intentionally unavailable
        /// </summary>
        public bool IsDataAvailable()
        {
            LogBreadthUnavailable();
            return false;
        }

        /// <summary>
        /// Throws InvalidOperationException - breadth data intentionally disabled
        /// </summary>
        public async Task<decimal> GetAdvanceDeclineRatioAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            
            LogBreadthUnavailable();
            _logger.LogError("[BREADTH-NULL] [AUDIT-VIOLATION] GetAdvanceDeclineRatioAsync called on disabled breadth feed - TRIGGERING HOLD + TELEMETRY");
            throw new InvalidOperationException("[BREADTH-NULL] [AUDIT-VIOLATION] Breadth feed intentionally disabled - no advance/decline data available");
        }

        /// <summary>
        /// Throws InvalidOperationException - breadth data intentionally disabled
        /// </summary>
        public async Task<decimal> GetNewHighsLowsRatioAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            
            LogBreadthUnavailable();
            _logger.LogError("[BREADTH-NULL] [AUDIT-VIOLATION] GetNewHighsLowsRatioAsync called on disabled breadth feed - TRIGGERING HOLD + TELEMETRY");
            throw new InvalidOperationException("[BREADTH-NULL] [AUDIT-VIOLATION] Breadth feed intentionally disabled - no highs/lows data available");
        }

        /// <summary>
        /// Returns empty dictionary - breadth data intentionally disabled
        /// </summary>
        public async Task<Dictionary<string, decimal>> GetSectorRotationDataAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            
            LogBreadthUnavailable();
            _logger.LogError("[BREADTH-NULL] [AUDIT-VIOLATION] GetSectorRotationDataAsync called on disabled breadth feed - TRIGGERING HOLD + TELEMETRY");
            return new Dictionary<string, decimal>(); // Return empty to avoid exceptions but still fail-closed
        }

        /// <summary>
        /// Log breadth unavailable message once per hour to keep telemetry alive
        /// </summary>
        private void LogBreadthUnavailable()
        {
            var now = DateTime.UtcNow;
            if (now - _lastLogTime >= _logInterval)
            {
                _logger.LogWarning("[BREADTH-NULL] Breadth feed intentionally disabled - awaiting real market breadth subscription");
                _lastLogTime = now;
            }
        }
    }
}