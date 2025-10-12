#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Position = TradingBot.Abstractions.Position;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Real-time position monitoring system that tracks session exposure
    /// Provides live exposure calculations per trading session
    /// </summary>
    public interface IRealTimePositionMonitor
    {
        /// <summary>
        /// Get dollar exposure for a specific trading session
        /// </summary>
        /// <param name="session">Session name (Asian, European, USMorning, USAfternoon, Evening)</param>
        /// <param name="positions">List of positions to analyze</param>
        Task<double> GetSessionExposureAsync(string session, List<TradingBot.Abstractions.Position> positions);
        
        /// <summary>
        /// Get exposure breakdown for all sessions
        /// </summary>
        Task<Dictionary<string, double>> GetAllSessionExposuresAsync(List<TradingBot.Abstractions.Position> positions);
        
        /// <summary>
        /// Subscribe to real-time exposure updates
        /// </summary>
        void SubscribeToExposureUpdates(Action<string, double> callback);
    }
}