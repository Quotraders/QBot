using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Production market time service - provides accurate market timing information
    /// Handles market sessions, open/close times, and holiday scheduling
    /// </summary>
    public class MarketTimeService
    {
        // Market holiday constants
        private const int JanuaryMonth = 1;
        private const int NewYearsDay = 1;
        private const int JulyMonth = 7;
        private const int IndependenceDayDate = 4;
        private const int DecemberMonth = 12;
        private const int ChristmasDay = 25;
        
        private readonly ILogger<MarketTimeService> _logger;
        private readonly TimeZoneInfo _easternTimeZone;

        public MarketTimeService(ILogger<MarketTimeService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }

        /// <summary>
        /// Get current market session for a symbol
        /// </summary>
        public async Task<string> GetCurrentSessionAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var now = DateTime.UtcNow;
            var easternTime = TimeZoneInfo.ConvertTimeFromUtc(now, _easternTimeZone);
            
            var session = DetermineMarketSession(easternTime, symbol);
            
            _logger.LogTrace("Market session for {Symbol} at {Time} ET: {Session}", 
                symbol, easternTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), session);
                
            return session;
        }

        /// <summary>
        /// Get minutes since market open
        /// </summary>
        public async Task<double> GetMinutesSinceOpenAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var now = DateTime.UtcNow;
            var easternTime = TimeZoneInfo.ConvertTimeFromUtc(now, _easternTimeZone);
            
            var marketOpen = GetMarketOpenTime(easternTime, symbol);
            
            if (easternTime.Date != marketOpen.Date)
            {
                // Different day, return 0
                return 0.0;
            }
            
            var minutesSinceOpen = easternTime > marketOpen 
                ? (easternTime - marketOpen).TotalMinutes 
                : -1.0; // Negative if before open
            
            var result = Math.Max(0.0, minutesSinceOpen);
            
            _logger.LogTrace("Minutes since open for {Symbol}: {Minutes:F1} (ET: {Time})", 
                symbol, result, easternTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                
            return result;
        }

        /// <summary>
        /// Get minutes until market close
        /// </summary>
        public async Task<double> GetMinutesUntilCloseAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            var now = DateTime.UtcNow;
            var easternTime = TimeZoneInfo.ConvertTimeFromUtc(now, _easternTimeZone);
            
            var marketClose = GetMarketCloseTime(easternTime, symbol);
            
            if (easternTime.Date != marketClose.Date)
            {
                // Different day, return 0
                return 0.0;
            }
            
            var minutesUntilClose = marketClose > easternTime 
                ? (marketClose - easternTime).TotalMinutes 
                : 0.0; // Zero if after close
            
            var result = Math.Max(0.0, minutesUntilClose);
            
            _logger.LogTrace("Minutes until close for {Symbol}: {Minutes:F1} (ET: {Time})", 
                symbol, result, easternTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                
            return result;
        }

        /// <summary>
        /// Check if market is currently open
        /// </summary>
        public bool IsMarketOpen(string symbol)
        {
            var now = DateTime.UtcNow;
            var easternTime = TimeZoneInfo.ConvertTimeFromUtc(now, _easternTimeZone);
            var session = DetermineMarketSession(easternTime, symbol);
            
            return session == "Open";
        }

        private static string DetermineMarketSession(DateTime easternTime, string symbol)
        {
            // Handle weekends
            if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
            {
                return "Closed";
            }

            var time = easternTime.TimeOfDay;
            
            // Market session times (Eastern Time)
            var preMarketStart = new TimeSpan(4, 0, 0);    // 4:00 AM
            var marketOpen = new TimeSpan(9, 30, 0);       // 9:30 AM  
            var marketClose = new TimeSpan(16, 0, 0);      // 4:00 PM
            var postMarketEnd = new TimeSpan(20, 0, 0);    // 8:00 PM

            if (time < preMarketStart)
            {
                return "Closed";
            }
            else if (time < marketOpen)
            {
                return "PreMarket";
            }
            else if (time < marketClose)
            {
                return "Open";
            }
            else if (time < postMarketEnd)
            {
                return "PostMarket";
            }
            else
            {
                return "Closed";
            }
        }

        private static DateTime GetMarketOpenTime(DateTime easternTime, string symbol)
        {
            // Standard market open time: 9:30 AM ET
            return new DateTime(easternTime.Year, easternTime.Month, easternTime.Day, 9, 30, 0);
        }

        private static DateTime GetMarketCloseTime(DateTime easternTime, string symbol)
        {
            // Standard market close time: 4:00 PM ET
            return new DateTime(easternTime.Year, easternTime.Month, easternTime.Day, 16, 0, 0);
        }

        /// <summary>
        /// Check if today is a market holiday
        /// </summary>
        public bool IsMarketHoliday(DateTime date)
        {
            // Basic holiday detection - can be enhanced with full holiday calendar
            var easternDate = TimeZoneInfo.ConvertTimeFromUtc(date, _easternTimeZone).Date;
            
            // New Year's Day
            if (easternDate.Month == JanuaryMonth && easternDate.Day == NewYearsDay)
                return true;
                
            // Independence Day
            if (easternDate.Month == JulyMonth && easternDate.Day == IndependenceDayDate)
                return true;
                
            // Christmas Day
            if (easternDate.Month == DecemberMonth && easternDate.Day == ChristmasDay)
                return true;
            
            // Note: This is a simplified implementation
            // A full implementation would include all market holidays and early closes
            return false;
        }
    }
}