using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TradingBot.Backtest.ExecutionSimulators;
using TradingBot.Safety.Analysis;
using TradingBot.Safety.Explainability;
using TradingBot.Monitoring.Alerts;

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
            // Register core enhanced services
            services.AddBookAwareSimulation(configuration);
            services.AddCounterfactualReplay(configuration);
            services.AddExplainabilityServices(configuration);
            services.AddEnhancedAlerting(configuration);

            return services;
        }

        /// <summary>
        /// Register book-aware execution simulation services
        /// </summary>
        public static IServiceCollection AddBookAwareSimulation(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Configure book-aware simulator
            services.Configure<BookAwareSimulatorConfig>(
                configuration.GetSection("BookAwareSimulator"));

            // Register book-aware execution simulator
            services.AddScoped<BookAwareExecutionSimulator>();
            
            // Override IExecutionSimulator to use book-aware version when enabled
            services.AddScoped<IExecutionSimulator>(provider =>
            {
                var config = configuration.GetSection("BookAwareSimulator");
                var enabled = config.GetValue<bool>("Enabled", false);
                
                if (enabled)
                {
                    return provider.GetRequiredService<BookAwareExecutionSimulator>();
                }
                else
                {
                    return provider.GetRequiredService<SimpleExecutionSimulator>();
                }
            });

            return services;
        }

        /// <summary>
        /// Register counterfactual replay analysis services
        /// </summary>
        public static IServiceCollection AddCounterfactualReplay(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Configure counterfactual replay
            services.Configure<CounterfactualReplayConfig>(
                configuration.GetSection("CounterfactualReplay"));

            // Register counterfactual replay service as hosted service
            services.AddHostedService<CounterfactualReplayService>();
            
            // Also register for dependency injection
            services.AddSingleton<CounterfactualReplayService>();

            return services;
        }

        /// <summary>
        /// Register explainability services
        /// </summary>
        public static IServiceCollection AddExplainabilityServices(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Configure explainability
            services.Configure<ExplainabilityConfig>(
                configuration.GetSection("Explainability"));

            // Register explainability stamp service
            services.AddScoped<IExplainabilityStampService, ExplainabilityStampService>();

            return services;
        }

        /// <summary>
        /// Register enhanced alerting services
        /// </summary>
        public static IServiceCollection AddEnhancedAlerting(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Configure enhanced alerting
            services.Configure<EnhancedAlertingConfig>(
                configuration.GetSection("EnhancedAlerting"));

            // Register enhanced alerting service as hosted service
            services.AddHostedService<EnhancedAlertingService>();
            
            // Also register for dependency injection
            services.AddSingleton<EnhancedAlertingService>();

            return services;
        }

        /// <summary>
        /// Configure enhanced trading bot settings with production defaults
        /// </summary>
        public static IServiceCollection ConfigureEnhancedTradingBotDefaults(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Set production-ready defaults for enhanced components
            services.PostConfigure<BookAwareSimulatorConfig>(config =>
            {
                config.HistoricalDataDays = Math.Max(config.HistoricalDataDays, 7);
                config.MaxSlippageMultiplier = Math.Max(config.MaxSlippageMultiplier, 1.0m);
                config.CommissionPerContract = Math.Max(config.CommissionPerContract, 0.0m);
                config.TrainingDatasetPath = string.IsNullOrEmpty(config.TrainingDatasetPath) 
                    ? "data/training/execution" 
                    : config.TrainingDatasetPath;
            });

            services.PostConfigure<CounterfactualReplayConfig>(config =>
            {
                config.GateLogsPath = string.IsNullOrEmpty(config.GateLogsPath) 
                    ? "state/gates" 
                    : config.GateLogsPath;
                config.NightlyRunHour = Math.Max(0, Math.Min(config.NightlyRunHour, 23));
                config.HistoricalDataDays = Math.Max(config.HistoricalDataDays, 1);
            });

            services.PostConfigure<ExplainabilityConfig>(config =>
            {
                config.ExplainabilityPath = string.IsNullOrEmpty(config.ExplainabilityPath) 
                    ? "state/explain" 
                    : config.ExplainabilityPath;
                config.MaxStampAgeHours = Math.Max(config.MaxStampAgeHours, 24);
            });

            services.PostConfigure<EnhancedAlertingConfig>(config =>
            {
                config.CheckIntervalSeconds = Math.Max(config.CheckIntervalSeconds, 10);
                config.PatternPromotedThreshold = Math.Max(config.PatternPromotedThreshold, 0.1);
                config.ModelRollbackThreshold = Math.Max(config.ModelRollbackThreshold, 0.1);
                config.FeatureDriftThreshold = Math.Max(config.FeatureDriftThreshold, 0.1);
                config.QueueEtaBreachThreshold = Math.Max(config.QueueEtaBreachThreshold, 0.1);
            });

            return services;
        }
    }
}