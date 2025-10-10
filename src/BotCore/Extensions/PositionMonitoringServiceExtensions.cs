using Microsoft.Extensions.DependencyInjection;
using BotCore.Services.PositionMonitoring;

namespace BotCore.Extensions
{
    /// <summary>
    /// Service registration extensions for position monitoring components
    /// Registers real-time position monitoring, session exposure calculation, and time tracking
    /// </summary>
    public static class PositionMonitoringServiceExtensions
    {
        /// <summary>
        /// Register all position monitoring services
        /// Adds real-time monitoring, session exposure calculator, and position time tracker
        /// </summary>
        public static IServiceCollection AddPositionMonitoringServices(
            this IServiceCollection services)
        {
            // Register real-time position monitor
            services.AddSingleton<IRealTimePositionMonitor, RealTimePositionMonitor>();
            
            // Register session exposure calculator
            services.AddSingleton<ISessionExposureCalculator, SessionExposureCalculator>();
            
            // Register position time tracker
            services.AddSingleton<IPositionTimeTracker, PositionTimeTracker>();
            
            // Register session detection service
            services.AddSingleton<SessionDetectionService>();

            return services;
        }
    }
}
