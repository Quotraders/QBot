#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Position time tracking for complete lifecycle history
    /// Tracks which sessions each position has been active in
    /// </summary>
    public interface IPositionTimeTracker
    {
        /// <summary>
        /// Get time-based exposure for a session (native + inherited positions)
        /// </summary>
        Task<double> GetSessionTimeExposureAsync(List<TradingBot.Abstractions.Position> positions, string session);
        
        /// <summary>
        /// Get full lifecycle history for a position
        /// </summary>
        Task<PositionLifecycle?> GetPositionLifecycleAsync(string positionId);
        
        /// <summary>
        /// Get session attribution showing all sessions a position has been active in
        /// </summary>
        Task<List<string>> GetSessionAttributionAsync(string positionId);
        
        /// <summary>
        /// Get positions that were opened today and closed today
        /// </summary>
        Task<List<string>> GetIntraDayPositionsAsync();
        
        /// <summary>
        /// Get positions held overnight
        /// </summary>
        Task<List<string>> GetOvernightPositionsAsync();
    }
    
    /// <summary>
    /// Complete position lifecycle data
    /// </summary>
    public class PositionLifecycle
    {
        public string PositionId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public DateTime EntryTime { get; set; }
        public string EntrySession { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public int Quantity { get; set; }
        public DateTime? ExitTime { get; set; }
        public decimal? ExitPrice { get; set; }
        public List<string> SessionsHeld { get; set; } = new();
        public string Status { get; set; } = "Open";
    }
}
