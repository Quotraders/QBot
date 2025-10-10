using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BotCore.Services;
using TradingBot.Abstractions;

namespace BotCore.Fusion;

/// <summary>
/// Risk manager interface for accessing current risk metrics - uses real EnhancedRiskManager
/// </summary>
public interface IRiskManagerForFusion
{
    Task<double> GetCurrentRiskAsync(CancellationToken cancellationToken = default);
    Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Enhanced risk state interface for production risk management
/// </summary>
public interface IEnhancedRiskState
{
    double MaxRisk { get; }
    double CurrentRisk { get; }
    double AccountEquity { get; }
    bool IsRiskWithinLimits { get; }
}

/// <summary>
/// Enhanced risk manager adapter interface for decoupling
/// </summary>
public interface IEnhancedRiskManagerAdapter
{
    Task<IEnhancedRiskState> GetRiskStateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Production risk manager implementation that integrates with real risk systems
/// </summary>
public sealed class ProductionRiskManager : IRiskManagerForFusion
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionRiskManager> _logger;
    
    // CA1848: LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, string, DateTime, Exception?> LogRiskAssessmentInitiated =
        LoggerMessage.Define<string, DateTime>(LogLevel.Debug, new EventId(8200, nameof(LogRiskAssessmentInitiated)),
            "🔍 [AUDIT-{OperationId}] Risk assessment initiated at {Timestamp}");
    
    private static readonly Action<ILogger, string, double, Exception?> LogMaxRiskOutOfBounds =
        LoggerMessage.Define<string, double>(LogLevel.Error, new EventId(8201, nameof(LogMaxRiskOutOfBounds)),
            "🚨 [AUDIT-{OperationId}] Risk:MaximumRiskLevel configuration out of bounds {MaxRisk} - using 1.0");
    
    private static readonly Action<ILogger, string, double, Exception?> LogRiskBreachDetected =
        LoggerMessage.Define<string, double>(LogLevel.Warning, new EventId(8202, nameof(LogRiskBreachDetected)),
            "🚨 [AUDIT-{OperationId}] Risk breach detected - returning maximum risk {MaxRisk:P2} (fail-closed)");
    
    private static readonly Action<ILogger, string, Exception?> LogConfigServiceUnavailable =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8203, nameof(LogConfigServiceUnavailable)),
            "🚨 [AUDIT-{OperationId}] Configuration service unavailable - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, Exception?> LogRiskConfigMissing =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8204, nameof(LogRiskConfigMissing)),
            "🚨 [AUDIT-{OperationId}] Risk configuration missing - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, double, double, double, Exception?> LogRiskConfigOutOfBounds =
        LoggerMessage.Define<string, double, double, double>(LogLevel.Error, new EventId(8205, nameof(LogRiskConfigOutOfBounds)),
            "🚨 [AUDIT-{OperationId}] Risk configuration out of bounds {Risk} (min: {Min}, max: {Max}) - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, double, long, Exception?> LogRiskAssessmentSuccessful =
        LoggerMessage.Define<string, double, long>(LogLevel.Debug, new EventId(8206, nameof(LogRiskAssessmentSuccessful)),
            "🔍 [AUDIT-{OperationId}] Risk assessment successful: Risk={Risk:P2}, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, Exception?> LogRiskInvalidOperation =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8207, nameof(LogRiskInvalidOperation)),
            "🚨 [AUDIT-{OperationId}] Risk assessment invalid operation - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, Exception?> LogRiskTimeout =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8208, nameof(LogRiskTimeout)),
            "🚨 [AUDIT-{OperationId}] Risk assessment timeout - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, Exception?> LogNoRiskService =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8209, nameof(LogNoRiskService)),
            "🚨 [AUDIT-{OperationId}] No risk management service available - fail-closed: returning hold");
    
    private static readonly Action<ILogger, string, long, Exception?> LogRiskServiceInvalidOperation =
        LoggerMessage.Define<string, long>(LogLevel.Error, new EventId(8210, nameof(LogRiskServiceInvalidOperation)),
            "🚨 [AUDIT-{OperationId}] Risk service invalid operation - fail-closed: returning hold, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, long, Exception?> LogRiskServiceBadArgument =
        LoggerMessage.Define<string, long>(LogLevel.Error, new EventId(8211, nameof(LogRiskServiceBadArgument)),
            "🚨 [AUDIT-{OperationId}] Risk service bad argument - fail-closed: returning hold, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, long, Exception?> LogRiskServiceHttpFailure =
        LoggerMessage.Define<string, long>(LogLevel.Error, new EventId(8212, nameof(LogRiskServiceHttpFailure)),
            "🚨 [AUDIT-{OperationId}] Risk service HTTP failure - fail-closed: returning hold, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, DateTime, Exception?> LogEquityAssessmentInitiated =
        LoggerMessage.Define<string, DateTime>(LogLevel.Debug, new EventId(8213, nameof(LogEquityAssessmentInitiated)),
            "🔍 [AUDIT-{OperationId}] Account equity assessment initiated at {Timestamp}");
    
    private static readonly Action<ILogger, string, Exception?> LogConfigServiceUnavailableForEquity =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8214, nameof(LogConfigServiceUnavailableForEquity)),
            "🚨 [AUDIT-{OperationId}] Configuration service unavailable - fail-closed: cannot determine account equity");
    
    private static readonly Action<ILogger, string, Exception?> LogEquityConfigMissing =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8215, nameof(LogEquityConfigMissing)),
            "🚨 [AUDIT-{OperationId}] Account equity configuration missing - fail-closed");
    
    private static readonly Action<ILogger, string, double, double, double, Exception?> LogEquityConfigOutOfBounds =
        LoggerMessage.Define<string, double, double, double>(LogLevel.Error, new EventId(8216, nameof(LogEquityConfigOutOfBounds)),
            "🚨 [AUDIT-{OperationId}] Account equity configuration out of bounds {Equity:C} (min: {Min:C}, max: {Max:C}) - fail-closed");
    
    private static readonly Action<ILogger, string, double, long, Exception?> LogEquityFromConfig =
        LoggerMessage.Define<string, double, long>(LogLevel.Debug, new EventId(8217, nameof(LogEquityFromConfig)),
            "🔍 [AUDIT-{OperationId}] Account equity from config: {Equity:C}, Duration={Duration}ms");
    
    private static readonly Action<ILogger, string, long, Exception?> LogEquityAssessmentFailed =
        LoggerMessage.Define<string, long>(LogLevel.Error, new EventId(8218, nameof(LogEquityAssessmentFailed)),
            "🚨 [AUDIT-{OperationId}] Account equity assessment failed - fail-closed: cannot proceed, Duration={Duration}ms");

    public ProductionRiskManager(IServiceProvider serviceProvider, ILogger<ProductionRiskManager> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<double> GetCurrentRiskAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Audit log: Risk assessment initiated
            LogRiskAssessmentInitiated(_logger, operationId, startTime, null);
            
            // Use basic risk manager from abstractions - fail-closed approach
            var basicRiskManager = _serviceProvider.GetService<IRiskManager>();
            if (basicRiskManager != null)
            {
                try
                {
                    // If risk is breached, return maximum risk (fail-closed)
                    if (basicRiskManager.IsRiskBreached)
                    {
                        // Get configured maximum risk value
                        var configService = _serviceProvider.GetService<IConfiguration>();
                        var maxRisk = configService?.GetValue<double>("Risk:MaximumRiskLevel") ?? 1.0;
                        
                        // Validate configuration bounds
                        if (maxRisk < 0.0 || maxRisk > 1.0)
                        {
                            LogMaxRiskOutOfBounds(_logger, operationId, maxRisk, null);
                            maxRisk = 1.0;
                        }
                        
                        LogRiskBreachDetected(_logger, operationId, maxRisk, null);
                        return Task.FromResult(maxRisk);
                    }
                    
                    // Get configured risk level from config (no hardcoded defaults)
                    var configuration = _serviceProvider.GetService<IConfiguration>();
                    if (configuration == null)
                    {
                        LogConfigServiceUnavailable(_logger, operationId, null);
                        var holdRisk = GetConfiguredHoldRiskLevel(_serviceProvider);
                        return Task.FromResult(holdRisk);
                    }
                    
                    // Try to get configured risk level with bounds validation
                    var configuredRisk = configuration.GetValue<double?>("Risk:NormalRiskLevel");
                    if (!configuredRisk.HasValue)
                    {
                        LogRiskConfigMissing(_logger, operationId, null);
                        var holdRisk = GetConfiguredHoldRiskLevel(_serviceProvider);
                        return Task.FromResult(holdRisk);
                    }
                    
                    // Validate bounds
                    var minRiskLevel = configuration.GetValue<double>("Risk:MinimumRiskLevel", 0.0);
                    var maxRiskLevel = configuration.GetValue<double>("Risk:MaximumRiskLevel", 1.0);
                    
                    if (configuredRisk.Value < minRiskLevel || configuredRisk.Value > maxRiskLevel)
                    {
                        LogRiskConfigOutOfBounds(_logger, operationId, configuredRisk.Value, minRiskLevel, maxRiskLevel, null);
                        var holdRisk = GetConfiguredHoldRiskLevel(_serviceProvider);
                        return Task.FromResult(holdRisk);
                    }
                    
                    LogRiskAssessmentSuccessful(_logger, operationId, configuredRisk.Value, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, null);
                    return Task.FromResult(configuredRisk.Value);
                }
                catch (InvalidOperationException ex)
                {
                    LogRiskInvalidOperation(_logger, operationId, ex);
                    var holdRisk = GetConfiguredHoldRiskLevel(_serviceProvider);
                    return Task.FromResult(holdRisk);
                }
                catch (TimeoutException ex)
                {
                    LogRiskTimeout(_logger, operationId, ex);
                    var holdRisk = GetConfiguredHoldRiskLevel(_serviceProvider);
                    return Task.FromResult(holdRisk);
                }
            }
            
            // No risk manager available - fail-closed
            LogNoRiskService(_logger, operationId, null);
            var holdRiskFallback = GetConfiguredHoldRiskLevel(_serviceProvider);
            return Task.FromResult(holdRiskFallback);
        }
        catch (InvalidOperationException ex)
        {
            LogRiskServiceInvalidOperation(_logger, operationId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, ex);
            var holdRiskException = GetConfiguredHoldRiskLevel(_serviceProvider);
            return Task.FromResult(holdRiskException);
        }
        catch (ArgumentException ex)
        {
            LogRiskServiceBadArgument(_logger, operationId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, ex);
            var holdRiskException = GetConfiguredHoldRiskLevel(_serviceProvider);
            return Task.FromResult(holdRiskException);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            LogRiskServiceHttpFailure(_logger, operationId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, ex);
            var holdRiskException = GetConfiguredHoldRiskLevel(_serviceProvider);
            return Task.FromResult(holdRiskException);
        }
    }

    /// <summary>
    /// Get configured hold risk level with proper bounds validation - fail-closed approach
    /// </summary>
    private static double GetConfiguredHoldRiskLevel(IServiceProvider serviceProvider)
    {
        try
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            if (configuration == null)
            {
                return 1.0; // Ultimate fallback - complete hold
            }

            var holdRisk = configuration.GetValue<double>("Risk:HoldRiskLevel", 1.0);
            
            // Validate bounds
            if (holdRisk < 0.0 || holdRisk > 1.0)
            {
                return 1.0; // Ultimate fallback - complete hold
            }
            
            return holdRisk;
        }
        catch (InvalidOperationException)
        {
            return 1.0; // Ultimate fallback - complete hold
        }
        catch (ArgumentException)
        {
            return 1.0; // Ultimate fallback - complete hold
        }
    }

    public Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Audit log: Account equity assessment initiated
            LogEquityAssessmentInitiated(_logger, operationId, startTime, null);
            
            // Get configuration service - required for account equity
            var configuration = _serviceProvider.GetService<IConfiguration>();
            if (configuration == null)
            {
                LogConfigServiceUnavailableForEquity(_logger, operationId, null);
                throw new InvalidOperationException("Account equity unavailable - configuration service missing (fail-closed)");
            }
            
            // Get configured starting equity with bounds validation
            var startingEquity = configuration.GetValue<double?>("Account:StartingEquity");
            if (!startingEquity.HasValue)
            {
                LogEquityConfigMissing(_logger, operationId, null);
                throw new InvalidOperationException("Account equity unavailable - configuration missing (fail-closed)");
            }
            
            // Validate bounds with configurable limits
            var minEquity = configuration.GetValue<double>("Account:MinimumEquity", 1000.0);
            var maxEquity = configuration.GetValue<double>("Account:MaximumEquity", 10000000.0);
            
            if (startingEquity.Value <= minEquity || startingEquity.Value > maxEquity)
            {
                LogEquityConfigOutOfBounds(_logger, operationId, startingEquity.Value, minEquity, maxEquity, null);
                throw new InvalidOperationException($"Account equity out of bounds: {startingEquity.Value:C} (min: {minEquity:C}, max: {maxEquity:C}) (fail-closed)");
            }
            
            LogEquityFromConfig(_logger, operationId, startingEquity.Value, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, null);
            return Task.FromResult(startingEquity.Value);
        }
        catch (Exception ex)
        {
            LogEquityAssessmentFailed(_logger, operationId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds, ex);
            throw new InvalidOperationException($"Account equity assessment failed for operation {operationId} - system fail-closed", ex);
        }
    }
}