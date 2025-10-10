using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of session configuration
    /// Replaces hardcoded timezone and trading session parameters
    /// </summary>
    public class SessionConfigService : ISessionConfig
    {
        // Session Configuration Constants
        private const int DefaultMaintenanceWindowDurationMinutes = 30;
        
        private readonly IConfiguration _config;

        public SessionConfigService(IConfiguration config)
        {
            _config = config;
        }

        public string GetPrimaryTimezone() => 
            _config.GetValue("Session:PrimaryTimezone", "America/Chicago");

        public TimeSpan GetRthStartTime() => 
            TimeSpan.ParseExact(_config.GetValue("Session:RthStartTime", "09:30"), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

        public TimeSpan GetRthEndTime() => 
            TimeSpan.ParseExact(_config.GetValue("Session:RthEndTime", "16:00"), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

        public bool AllowExtendedHours() => 
            _config.GetValue("Session:AllowExtendedHours", false);

        public TimeSpan GetOvernightStartTime() => 
            TimeSpan.ParseExact(_config.GetValue("Session:OvernightStartTime", "20:00"), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

        public TimeSpan GetOvernightEndTime() => 
            TimeSpan.ParseExact(_config.GetValue("Session:OvernightEndTime", "02:00"), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

        public TimeSpan GetMaintenanceWindowStartUtc() => 
            TimeSpan.ParseExact(_config.GetValue("Session:MaintenanceWindowStartUtc", "05:00"), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

        public int GetMaintenanceWindowDurationMinutes() => 
            _config.GetValue("Session:MaintenanceWindowDurationMinutes", DefaultMaintenanceWindowDurationMinutes);

        public bool AllowWeekendTrading() => 
            _config.GetValue("Session:AllowWeekendTrading", false);
    }
}