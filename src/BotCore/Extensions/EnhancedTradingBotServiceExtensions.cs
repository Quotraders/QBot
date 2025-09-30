using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace TradingBot.BotCore.Extensions
{
    /// <summary>
    /// Service registration extensions for enhanced trading bot components
    /// Provides production-ready dependency injection configuration
    /// </summary>
    public static class EnhancedTradingBotServiceExtensions
    {
        /// <summary>
        /// Register all enhanced trading bot services
        /// </summary>
        public static IServiceCollection AddEnhancedTradingBotServices(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Enhanced services will be registered here when dependencies are resolved
            return services;
        }
    }
}