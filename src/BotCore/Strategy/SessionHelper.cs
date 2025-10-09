using System;

namespace BotCore.Strategy
{
    /// <summary>
    /// Granular trading session types for session-specific learning
    /// Used for position management optimization
    /// </summary>
    public enum GranularSessionType
    {
        PreMarket,      // 4:00 AM - 9:30 AM ET
        LondonSession,  // 2:00 AM - 5:00 AM ET (European market hours)
        NYOpen,         // 9:30 AM - 11:00 AM ET (NY market open - high volatility)
        Lunch,          // 11:00 AM - 1:00 PM ET (lunch hour - typically choppy)
        Afternoon,      // 1:00 PM - 3:00 PM ET (afternoon trading)
        PowerHour,      // 3:00 PM - 4:00 PM ET (last hour - high volume)
        PostRTH,        // 4:00 PM - 6:00 PM ET (after hours)
        Overnight       // 6:00 PM - 4:00 AM ET (overnight session)
    }

    /// <summary>
    /// Helper class for trading session detection and utilities.
    /// Provides shared functionality for session-aware parameter loading across all strategies.
    /// SESSION-SPECIFIC LEARNING: Now supports granular session detection
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
            catch (TimeZoneNotFoundException)
            {
                // Fallback to RTH if Eastern timezone not found
                return "RTH";
            }
            catch (InvalidTimeZoneException)
            {
                // Fallback to RTH if timezone data is invalid
                return "RTH";
            }
            catch (ArgumentException)
            {
                // Fallback to RTH if conversion arguments are invalid
                return "RTH";
            }
        }
        
        /// <summary>
        /// SESSION-SPECIFIC LEARNING: Get granular trading session for detailed analysis
        /// Returns specific session type (LondonSession, NYOpen, Lunch, etc.)
        /// </summary>
        /// <param name="utcNow">Current UTC time</param>
        /// <returns>GranularSessionType enum value</returns>
        public static GranularSessionType GetGranularSession(DateTime utcNow)
        {
            try
            {
                var etNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Et);
                var timeOfDay = etNow.TimeOfDay;
                
                // 6:00 PM to 2:00 AM (next day) = Overnight
                if (timeOfDay >= new TimeSpan(18, 0, 0) || timeOfDay < new TimeSpan(2, 0, 0))
                    return GranularSessionType.Overnight;
                
                // 2:00 AM to 5:00 AM = London Session (European market hours overlap)
                if (timeOfDay >= new TimeSpan(2, 0, 0) && timeOfDay < new TimeSpan(5, 0, 0))
                    return GranularSessionType.LondonSession;
                
                // 5:00 AM to 9:30 AM = PreMarket
                if (timeOfDay >= new TimeSpan(5, 0, 0) && timeOfDay < new TimeSpan(9, 30, 0))
                    return GranularSessionType.PreMarket;
                
                // 9:30 AM to 11:00 AM = NY Open (high volatility period)
                if (timeOfDay >= new TimeSpan(9, 30, 0) && timeOfDay < new TimeSpan(11, 0, 0))
                    return GranularSessionType.NYOpen;
                
                // 11:00 AM to 1:00 PM = Lunch (typically choppy)
                if (timeOfDay >= new TimeSpan(11, 0, 0) && timeOfDay < new TimeSpan(13, 0, 0))
                    return GranularSessionType.Lunch;
                
                // 1:00 PM to 3:00 PM = Afternoon
                if (timeOfDay >= new TimeSpan(13, 0, 0) && timeOfDay < new TimeSpan(15, 0, 0))
                    return GranularSessionType.Afternoon;
                
                // 3:00 PM to 4:00 PM = Power Hour (last hour - high volume)
                if (timeOfDay >= new TimeSpan(15, 0, 0) && timeOfDay < new TimeSpan(16, 0, 0))
                    return GranularSessionType.PowerHour;
                
                // 4:00 PM to 6:00 PM = PostRTH
                if (timeOfDay >= new TimeSpan(16, 0, 0) && timeOfDay < new TimeSpan(18, 0, 0))
                    return GranularSessionType.PostRTH;
                
                // Default fallback
                return GranularSessionType.Afternoon;
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback to Afternoon if Eastern timezone not found
                return GranularSessionType.Afternoon;
            }
            catch (InvalidTimeZoneException)
            {
                // Fallback to Afternoon if timezone data is invalid
                return GranularSessionType.Afternoon;
            }
            catch (ArgumentException)
            {
                // Fallback to Afternoon if conversion arguments are invalid
                return GranularSessionType.Afternoon;
            }
        }
        
        /// <summary>
        /// SESSION-SPECIFIC LEARNING: Get granular session name as string
        /// </summary>
        public static string GetGranularSessionName(DateTime utcNow)
        {
            return GetGranularSession(utcNow).ToString();
        }
    }
}
