#nullable enable
using System;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Service for detecting current trading session based on UTC time
    /// Maps time ranges to session names
    /// </summary>
    public class SessionDetectionService
    {
        /// <summary>
        /// Get current trading session from UTC time
        /// Sessions: Asian (00:00-08:00), European (08:00-13:00), USMorning (13:00-18:00), USAfternoon (18:00-21:00), Evening (21:00-00:00)
        /// </summary>
        public string GetCurrentSession(DateTime utcNow)
        {
            var hour = utcNow.Hour;
            
            if (hour >= 0 && hour < 8)
                return "Asian";
            if (hour >= 8 && hour < 13)
                return "European";
            if (hour >= 13 && hour < 18)
                return "USMorning";
            if (hour >= 18 && hour < 21)
                return "USAfternoon";
            if (hour >= 21 && hour < 24)
                return "Evening";
                
            return "Asian"; // Default fallback
        }
        
        /// <summary>
        /// Determine which session a given timestamp falls into
        /// </summary>
        public string GetSessionForTimestamp(DateTime utcTimestamp)
        {
            return GetCurrentSession(utcTimestamp);
        }
    }
}
