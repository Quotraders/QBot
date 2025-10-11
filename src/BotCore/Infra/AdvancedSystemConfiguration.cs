using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BotCore.ML;
using BotCore.Market;

namespace BotCore.Infra;

/// <summary>
/// Configuration service for integrating ML memory management and workflow orchestration
/// </summary>
public static class AdvancedSystemConfiguration
{
    /// <summary>
    /// Register ML memory management services
    /// </summary>
    public static IServiceCollection AddMLMemoryManagement(this IServiceCollection services)
    {
        services.AddSingleton<IMLMemoryManager, MLMemoryManager>();
        services.AddSingleton<RedundantDataFeedManager>();
        
        return services;
    }
    
    /// <summary>
    /// Register economic event management services
    /// </summary>
    public static IServiceCollection AddEconomicEventManagement(this IServiceCollection services)
    {
        // Ensure HttpClient is registered
        services.AddHttpClient<IEconomicEventManager, EconomicEventManager>();
        
        return services;
    }
    
    /// <summary>
    /// Register enhanced ML model manager with memory management
    /// </summary>
    public static IServiceCollection AddEnhancedMLModelManager(this IServiceCollection services)
    {
        services.AddSingleton<StrategyMlModelManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<StrategyMlModelManager>>();
            var memoryManager = provider.GetService<IMLMemoryManager>();
            return new StrategyMlModelManager(logger, memoryManager);
        });
        
        return services;
    }
    
    /// <summary>
    /// Initialize and start advanced system components
    /// </summary>
    public static async Task InitializeAdvancedSystemAsync(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(AdvancedSystemConfiguration));
        logger.LogInformation("[Advanced-System] Initializing advanced system components");
        
        // Initialize ML memory management
        var memoryManager = serviceProvider.GetService<IMLMemoryManager>();
        if (memoryManager != null)
        {
            await memoryManager.InitializeMemoryManagementAsync().ConfigureAwait(false);
            logger.LogInformation("[Advanced-System] ML memory management initialized");
        }
        
        // Initialize data feed management
        var dataFeedManager = serviceProvider.GetService<RedundantDataFeedManager>();
        if (dataFeedManager != null)
        {
            await dataFeedManager.InitializeDataFeedsAsync().ConfigureAwait(false);
            logger.LogInformation("[Advanced-System] Redundant data feeds initialized");
        }
        
        // Initialize economic event management
        var economicEventManager = serviceProvider.GetService<IEconomicEventManager>();
        if (economicEventManager != null)
        {
            await economicEventManager.InitializeAsync().ConfigureAwait(false);
            logger.LogInformation("[Advanced-System] Economic event management initialized");
        }
        
        logger.LogInformation("[Advanced-System] Advanced system components initialized successfully");
    }
}