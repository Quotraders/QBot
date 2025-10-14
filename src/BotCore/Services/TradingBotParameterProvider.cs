using Microsoft.Extensions.DependencyInjection;
using TradingBot.BotCore.Services;
using TradingBot.BotCore.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Modern replacement for OrchestratorAgent.Configuration.MLParameterProvider
    /// Provides ML configuration parameters for production trading
    /// All values are configuration-driven with no hardcoded business logic
    /// </summary>
    public static class TradingBotParameterProvider
    {
        // Trading Parameter Fallback Constants
        private const double DefaultAIConfidenceThreshold = 0.75;
        private const double DefaultPositionSizeMultiplier = 2.0;
        private const double DefaultMinimumConfidence = 0.65;
        
        private static IServiceProvider? _serviceProvider;
        
        /// <summary>
        /// Initialize the provider with the service provider
        /// </summary>
        public static void Initialize(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            _serviceProvider = serviceProvider;
        }
        
        /// <summary>
        /// Get AI confidence threshold - replaces hardcoded values in legacy code
        /// </summary>
        public static double GetAIConfidenceThreshold()
        {
            return ServiceProviderHelper.ExecuteWithService<TradingBot.Abstractions.IMLConfigurationService, double>(
                _serviceProvider,
                service => service.GetAIConfidenceThreshold(),
                DefaultAIConfidenceThreshold // Conservative fallback
            );
        }
        
        /// <summary>
        /// Get position size multiplier - replaces hardcoded values in legacy code
        /// </summary>
        public static double GetPositionSizeMultiplier()
        {
            return ServiceProviderHelper.ExecuteWithService<TradingBot.Abstractions.IMLConfigurationService, double>(
                _serviceProvider,
                service => service.GetPositionSizeMultiplier(),
                DefaultPositionSizeMultiplier // Conservative fallback
            );
        }
        
        /// <summary>
        /// Get fallback confidence - replaces hardcoded values in error scenarios
        /// </summary>
        public static double GetFallbackConfidence()
        {
            return ServiceProviderHelper.ExecuteWithService<TradingBot.Abstractions.IMLConfigurationService, double>(
                _serviceProvider,
                service => service.GetMinimumConfidence(),
                DefaultMinimumConfidence // Conservative fallback
            );
        }

        /// <summary>
        /// Get regime detection threshold - replaces hardcoded values
        /// </summary>
        public static double GetRegimeDetectionThreshold()
        {
            return ServiceProviderHelper.ExecuteWithService<TradingBot.Abstractions.IMLConfigurationService, double>(
                _serviceProvider,
                service => service.GetRegimeDetectionThreshold(),
                1.0 // Conservative fallback
            );
        }
    }
}