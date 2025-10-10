using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BotCore.Services;

namespace BotCore.Extensions;

/// <summary>
/// Service registration extensions for production-ready guardrail services
/// </summary>
public static class ProductionGuardrailExtensions
{
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, Exception?> LogValidatingGuardrails =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6001, nameof(LogValidatingGuardrails)),
            "🔍 [SETUP] Validating production guardrail setup...");
    
    private static readonly Action<ILogger, Exception?> LogGuardrailsValidated =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6002, nameof(LogGuardrailsValidated)),
            "✅ [SETUP] Production guardrails validated successfully");
    
    private static readonly Action<ILogger, Exception?> LogKillSwitchActive =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6003, nameof(LogKillSwitchActive)),
            "  • Kill switch monitoring: ACTIVE");
    
    private static readonly Action<ILogger, Exception?> LogOrderEvidenceActive =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6004, nameof(LogOrderEvidenceActive)),
            "  • Order evidence validation: ACTIVE");
    
    private static readonly Action<ILogger, Exception?> LogPriceValidationActive =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6005, nameof(LogPriceValidationActive)),
            "  • Price validation (ES/MES 0.25 tick): ACTIVE");
    
    private static readonly Action<ILogger, Exception?> LogRiskValidationActive =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6006, nameof(LogRiskValidationActive)),
            "  • Risk validation (reject ≤ 0): ACTIVE");
    
    private static readonly Action<ILogger, string, Exception?> LogCurrentMode =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(6007, nameof(LogCurrentMode)),
            "  • Current mode: {Mode}");
    
    private static readonly Action<ILogger, Exception?> LogKillSwitchTriggered =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(6008, nameof(LogKillSwitchTriggered)),
            "🔴 [SETUP] KILL SWITCH ACTIVE - All execution disabled");
    
    private static readonly Action<ILogger, Exception> LogGuardrailValidationFailed =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(6009, nameof(LogGuardrailValidationFailed)),
            "❌ [SETUP] Production guardrail validation FAILED");

    /// <summary>
    /// Add all production guardrail services following agent rules
    /// </summary>
    public static IServiceCollection AddProductionGuardrails(this IServiceCollection services)
    {
        // Core guardrail services
        services.AddSingleton<ProductionKillSwitchService>();
        services.AddScoped<ProductionOrderEvidenceService>();
        services.AddScoped<ProductionGuardrailOrchestrator>();
        
        // Register kill switch as hosted service for monitoring
        services.AddHostedService<ProductionKillSwitchService>(provider => 
            provider.GetRequiredService<ProductionKillSwitchService>());
        
        // Register orchestrator as hosted service
        services.AddHostedService<ProductionGuardrailOrchestrator>(provider =>
            provider.GetRequiredService<ProductionGuardrailOrchestrator>());

        return services;
    }

    /// <summary>
    /// Validate that all required production guardrails are active
    /// Call this after service configuration to verify setup
    /// </summary>
    public static void ValidateProductionGuardrails(this IServiceProvider serviceProvider, ILogger? logger = null)
    {
        if (logger != null)
        {
            LogValidatingGuardrails(logger, null);
        }

        try
        {
            // Verify kill switch service
            var killSwitchService = serviceProvider.GetService<ProductionKillSwitchService>();
            if (killSwitchService == null)
            {
                throw new InvalidOperationException("ProductionKillSwitchService not registered");
            }

            // Verify order evidence service  
            var orderEvidenceService = serviceProvider.GetService<ProductionOrderEvidenceService>();
            if (orderEvidenceService == null)
            {
                throw new InvalidOperationException("ProductionOrderEvidenceService not registered");
            }

            // Verify orchestrator
            var orchestrator = serviceProvider.GetService<ProductionGuardrailOrchestrator>();
            if (orchestrator == null)
            {
                throw new InvalidOperationException("ProductionGuardrailOrchestrator not registered");
            }

            // Check execution mode
            var isDryRun = ProductionKillSwitchService.IsDryRunMode();
            var killSwitchActive = ProductionKillSwitchService.IsKillSwitchActive();

            if (logger != null)
            {
                LogGuardrailsValidated(logger, null);
                LogKillSwitchActive(logger, null);
                LogOrderEvidenceActive(logger, null);
                LogPriceValidationActive(logger, null);
                LogRiskValidationActive(logger, null);
                LogCurrentMode(logger, isDryRun ? "DRY_RUN" : "LIVE", null);
                
                if (killSwitchActive)
                {
                    LogKillSwitchTriggered(logger, null);
                }
            }
        }
        catch (Exception ex)
        {
            if (logger != null)
            {
                LogGuardrailValidationFailed(logger, ex);
            }
            throw new InvalidOperationException("Production guardrail validation failed - cannot proceed with startup", ex);
        }
    }

    /// <summary>
    /// Quick setup method for simple applications
    /// </summary>
    public static IServiceCollection AddProductionTradingServices(this IServiceCollection services)
    {
        return services
            .AddProductionGuardrails()
            .AddLogging();
    }
}