using System;

namespace BotCore.Strategy
{
    /// <summary>
    /// Helper class for trading session detection and utilities.
    /// Provides shared functionality for session-aware parameter loading across all strategies.
    /// </summary>
    public static class SessionHelper
    {
        private static readonly TimeZoneInfo Et = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        /// <summary>
        /// Get current trading session name for parameter loading.
        /// Maps time ranges to session names: Overnight, RTH, PostRTH
        /// </summary>
        /// <param name="utcNow">Current UTC time</param>
        /// <returns>Session name: "Overnight", "RTH", or "PostRTH"</returns>
        public static string GetSessionName(DateTime utcNow)
        {
            try
            {
                var etNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Et);
                var timeOfDay = etNow.TimeOfDay;
                
                // 18:00 (6 PM) to 08:30 (8:30 AM) next day = Overnight
                if (timeOfDay >= new TimeSpan(18, 0, 0) || timeOfDay < new TimeSpan(8, 30, 0))
                    return "Overnight";
                
                // 09:30 to 16:00 = RTH (Regular Trading Hours)
                if (timeOfDay >= new TimeSpan(9, 30, 0) && timeOfDay < new TimeSpan(16, 0, 0))
                    return "RTH";
                
                // 16:00 to 18:00 = PostRTH
                if (timeOfDay >= new TimeSpan(16, 0, 0) && timeOfDay < new TimeSpan(18, 0, 0))
                    return "PostRTH";
                
                return "RTH"; // Default fallback
            }
            catch (Exception)
            {
                // Fallback to RTH if timezone conversion fails
                return "RTH";
            }
        }
    }
}
