using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of event tempering configuration
    /// Replaces hardcoded event handling and lockout parameters
    /// </summary>
    public class EventTemperingConfigService : IEventTemperingConfig
    {
        private readonly IConfiguration _config;

        // Default event tempering constants
        private const int DefaultNewsLockoutMinutesBefore = 5;
        private const int DefaultNewsLockoutMinutesAfter = 2;
        private const int DefaultHighImpactEventLockoutMinutes = 15;
        private const double DefaultEventPositionSizeReduction = 0.5;
        private const int DefaultPreMarketTradingMinutes = 30;

        public EventTemperingConfigService(IConfiguration config, ILogger<EventTemperingConfigService> logger)
        {
            _config = config;
        }

        public int GetNewsLockoutMinutesBefore() => 
            _config.GetValue("EventTempering:NewsLockoutMinutesBefore", DefaultNewsLockoutMinutesBefore);

        public int GetNewsLockoutMinutesAfter() => 
            _config.GetValue("EventTempering:NewsLockoutMinutesAfter", DefaultNewsLockoutMinutesAfter);

        public int GetHighImpactEventLockoutMinutes() => 
            _config.GetValue("EventTempering:HighImpactEventLockoutMinutes", DefaultHighImpactEventLockoutMinutes);

        public bool ReducePositionSizesBeforeEvents() => 
            _config.GetValue("EventTempering:ReducePositionSizesBeforeEvents", true);

        public double GetEventPositionSizeReduction() => 
            _config.GetValue("EventTempering:EventPositionSizeReduction", DefaultEventPositionSizeReduction);

        public bool EnableHolidayTradingLockout() => 
            _config.GetValue("EventTempering:EnableHolidayTradingLockout", true);

        public bool EnableEarningsLockout() => 
            _config.GetValue("EventTempering:EnableEarningsLockout", true);

        public bool EnableFomcLockout() => 
            _config.GetValue("EventTempering:EnableFomcLockout", true);

        public int GetPreMarketTradingMinutes() => 
            _config.GetValue("EventTempering:PreMarketTradingMinutes", DefaultPreMarketTradingMinutes);
    }
}