#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Position = TradingBot.Abstractions.Position;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Real-time position monitoring with session-based exposure tracking and time decay
    /// </summary>
    public class RealTimePositionMonitor : IRealTimePositionMonitor
    {
        private readonly ILogger<RealTimePositionMonitor> _logger;
        private readonly SessionDetectionService _sessionDetection;
        private readonly ConcurrentDictionary<string, PositionEntry> _positionEntries = new();
        private readonly List<Action<string, double>> _subscribers = new();
        
        public RealTimePositionMonitor(ILogger<RealTimePositionMonitor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionDetection = new SessionDetectionService();
        }
        
        /// <summary>
        /// Get session exposure with time-decay weighting
        /// </summary>
        public async Task<double> GetSessionExposureAsync(string session, List<TradingBot.Abstractions.Position> positions)
        {
            if (positions == null || positions.Count == 0)
                return 0.0;
                
            double totalExposure = 0.0;
            var now = DateTime.UtcNow;
            
            foreach (var position in positions)
            {
                // Track position entry if not already tracked
                var posKey = $"{position.Symbol}_{position.OpenTime:yyyyMMddHHmmss}";
                if (!_positionEntries.ContainsKey(posKey))
                {
                    var entrySession = _sessionDetection.GetSessionForTimestamp(position.OpenTime.UtcDateTime);
                    _positionEntries[posKey] = new PositionEntry
                    {
                        PositionId = posKey,
                        Symbol = position.Symbol,
                        EntryTime = position.OpenTime.UtcDateTime,
                        EntrySession = entrySession,
                        Quantity = position.Quantity,
                        EntryPrice = position.AveragePrice
                    };
                }
                
                var entry = _positionEntries[posKey];
                
                // Only include positions from the requested session
                if (!entry.EntrySession.Equals(session, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Calculate time decay weight
                var age = now - entry.EntryTime;
                var timeDecayWeight = CalculateTimeDecayWeight(age);
                
                // Calculate exposure: Quantity × AveragePrice × TimeDecayWeight
                var exposure = Math.Abs(position.Quantity) * (double)position.AveragePrice * timeDecayWeight;
                totalExposure += exposure;
            }
            
            _logger.LogDebug("[REALTIME-MONITOR] Session {Session} exposure: {Exposure:F2}", session, totalExposure);
            
            await Task.CompletedTask;
            return totalExposure;
        }
        
        /// <summary>
        /// Get all session exposures
        /// </summary>
        public async Task<Dictionary<string, double>> GetAllSessionExposuresAsync(List<TradingBot.Abstractions.Position> positions)
        {
            var sessions = new[] { "Asian", "European", "USMorning", "USAfternoon", "Evening" };
            var result = new Dictionary<string, double>();
            
            foreach (var session in sessions)
            {
                result[session] = await GetSessionExposureAsync(session, positions).ConfigureAwait(false);
            }
            
            return result;
        }
        
        /// <summary>
        /// Subscribe to exposure updates
        /// </summary>
        public void SubscribeToExposureUpdates(Action<string, double> callback)
        {
            if (callback != null)
            {
                _subscribers.Add(callback);
            }
        }
        
        /// <summary>
        /// Calculate time decay weight based on position age
        /// Fresh (< 1h): 1.0, Aging (1-4h): 0.8, Old (4-8h): 0.5, Stale (> 8h): 0.3
        /// </summary>
        private static double CalculateTimeDecayWeight(TimeSpan age)
        {
            var hours = age.TotalHours;
            
            if (hours < 1.0)
                return 1.0;  // Fresh position
            if (hours < 4.0)
                return 0.8;  // Aging position
            if (hours < 8.0)
                return 0.5;  // Old position
            return 0.3;      // Stale position
        }
        
        private class PositionEntry
        {
            public string PositionId { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public DateTime EntryTime { get; set; }
            public string EntrySession { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public decimal EntryPrice { get; set; }
        }
    }
}
