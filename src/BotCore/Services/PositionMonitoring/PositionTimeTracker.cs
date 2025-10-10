#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Position time tracking with complete lifecycle history
    /// Tracks session attribution and position holding periods
    /// </summary>
    public class PositionTimeTracker : IPositionTimeTracker
    {
        private readonly ILogger<PositionTimeTracker> _logger;
        private readonly SessionDetectionService _sessionDetection;
        private readonly ConcurrentDictionary<string, PositionLifecycle> _lifecycles = new();
        
        public PositionTimeTracker(ILogger<PositionTimeTracker> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionDetection = new SessionDetectionService();
        }
        
        /// <summary>
        /// Get time-based exposure for session (native + inherited)
        /// </summary>
        public async Task<double> GetSessionTimeExposureAsync(List<Position> positions, string session)
        {
            if (positions == null || positions.Count == 0)
                return 0.0;
                
            double nativeExposure = 0.0;
            double inheritedExposure = 0.0;
            
            foreach (var position in positions)
            {
                // Ensure position is tracked
                var posId = GetPositionId(position);
                TrackPosition(position);
                
                if (!_lifecycles.TryGetValue(posId, out var lifecycle))
                    continue;
                    
                // Calculate base exposure
                var exposure = Math.Abs(position.Quantity) * (double)position.AveragePrice;
                
                // Native: position opened in this session
                if (lifecycle.EntrySession.Equals(session, StringComparison.OrdinalIgnoreCase))
                {
                    nativeExposure += exposure;
                }
                // Inherited: position carried over from earlier session
                else if (lifecycle.SessionsHeld.Contains(session))
                {
                    // Apply holding period risk multiplier
                    var holdingMultiplier = GetHoldingPeriodMultiplier(lifecycle);
                    inheritedExposure += exposure * holdingMultiplier;
                }
            }
            
            var totalExposure = nativeExposure + inheritedExposure;
            
            _logger.LogDebug(
                "[TIME-TRACKER] Session {Session}: Native={Native:F2}, Inherited={Inherited:F2}, Total={Total:F2}",
                session, nativeExposure, inheritedExposure, totalExposure);
            
            await Task.CompletedTask;
            return totalExposure;
        }
        
        /// <summary>
        /// Get full lifecycle for a position
        /// </summary>
        public async Task<PositionLifecycle?> GetPositionLifecycleAsync(string positionId)
        {
            await Task.CompletedTask;
            return _lifecycles.TryGetValue(positionId, out var lifecycle) ? lifecycle : null;
        }
        
        /// <summary>
        /// Get session attribution for a position
        /// </summary>
        public async Task<List<string>> GetSessionAttributionAsync(string positionId)
        {
            await Task.CompletedTask;
            return _lifecycles.TryGetValue(positionId, out var lifecycle) 
                ? lifecycle.SessionsHeld 
                : new List<string>();
        }
        
        /// <summary>
        /// Get intraday positions (opened and closed same day)
        /// </summary>
        public async Task<List<string>> GetIntraDayPositionsAsync()
        {
            var today = DateTime.UtcNow.Date;
            var result = _lifecycles.Values
                .Where(p => p.EntryTime.Date == today && 
                           p.ExitTime.HasValue && 
                           p.ExitTime.Value.Date == today)
                .Select(p => p.PositionId)
                .ToList();
                
            await Task.CompletedTask;
            return result;
        }
        
        /// <summary>
        /// Get overnight positions
        /// </summary>
        public async Task<List<string>> GetOvernightPositionsAsync()
        {
            var today = DateTime.UtcNow.Date;
            var result = _lifecycles.Values
                .Where(p => p.EntryTime.Date < today && 
                           p.Status == "Open")
                .Select(p => p.PositionId)
                .ToList();
                
            await Task.CompletedTask;
            return result;
        }
        
        /// <summary>
        /// Track position lifecycle
        /// </summary>
        private void TrackPosition(Position position)
        {
            var posId = GetPositionId(position);
            
            if (_lifecycles.ContainsKey(posId))
            {
                // Update existing lifecycle
                UpdatePositionSessions(posId);
                return;
            }
            
            // Create new lifecycle
            var entrySession = _sessionDetection.GetSessionForTimestamp(position.OpenTime.UtcDateTime);
            var lifecycle = new PositionLifecycle
            {
                PositionId = posId,
                Symbol = position.Symbol,
                EntryTime = position.OpenTime.UtcDateTime,
                EntrySession = entrySession,
                EntryPrice = position.AveragePrice,
                Quantity = position.Quantity,
                SessionsHeld = new List<string> { entrySession },
                Status = "Open"
            };
            
            _lifecycles[posId] = lifecycle;
        }
        
        /// <summary>
        /// Update sessions held list when position crosses session boundaries
        /// </summary>
        private void UpdatePositionSessions(string positionId)
        {
            if (!_lifecycles.TryGetValue(positionId, out var lifecycle))
                return;
                
            var currentSession = _sessionDetection.GetCurrentSession(DateTime.UtcNow);
            
            if (!lifecycle.SessionsHeld.Contains(currentSession))
            {
                lifecycle.SessionsHeld.Add(currentSession);
                _logger.LogDebug(
                    "[TIME-TRACKER] Position {PositionId} entered session {Session}",
                    positionId, currentSession);
            }
        }
        
        /// <summary>
        /// Calculate holding period risk multiplier
        /// Intraday: 1.0x, Swing: 1.15x, Position: 1.3x, Overnight: 1.4x
        /// </summary>
        private static double GetHoldingPeriodMultiplier(PositionLifecycle lifecycle)
        {
            var age = DateTime.UtcNow - lifecycle.EntryTime;
            var days = age.TotalDays;
            
            if (days < 1.0)
                return 1.0;   // Intraday
            if (days < 3.0)
                return 1.15;  // Swing
            if (days < 7.0)
                return 1.3;   // Position
            return 1.4;       // Extended holding
        }
        
        /// <summary>
        /// Generate position ID from position data
        /// </summary>
        private static string GetPositionId(Position position)
        {
            return $"{position.Symbol}_{position.OpenTime:yyyyMMddHHmmss}";
        }
    }
}
