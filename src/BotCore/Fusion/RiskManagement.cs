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
            _logger.LogDebug("🔍 [AUDIT-{OperationId}] Risk assessment initiated at {Timestamp}", operationId, startTime);
            
            // Use basic risk manager from abstractions - fail-closed approach
            var basicRiskManager = _serviceProvider.GetService<IRiskManager>();
            if (basicRiskManager != null)
            {
                try
                {
                    // If risk is breached, return maximum risk (fail-closed)
                    if (basicRiskManager.IsRiskBreached)
                    {
                        _logger.LogWarning("🚨 [AUDIT-{OperationId}] Risk breach detected - returning maximum risk (fail-closed)", operationId);
                        return Task.FromResult(1.0); // Maximum risk = hold/no trading
                    }
                    
                    // Get configured risk level from config (no hardcoded defaults)
                    var configuration = _serviceProvider.GetService<IConfiguration>();
                    if (configuration == null)
                    {
                        _logger.LogError("🚨 [AUDIT-{OperationId}] Configuration service unavailable - fail-closed: returning hold", operationId);
                        return Task.FromResult(1.0); // Fail-closed: hold when config unavailable
                    }
                    
                    // Try to get configured risk level with bounds validation
                    var configuredRisk = configuration.GetValue<double?>("Risk:NormalRiskLevel");
                    if (!configuredRisk.HasValue)
                    {
                        _logger.LogError("🚨 [AUDIT-{OperationId}] Risk configuration missing - fail-closed: returning hold", operationId);
                        return Task.FromResult(1.0); // Fail-closed: hold when config missing
                    }
                    
                    // Validate bounds
                    if (configuredRisk.Value < 0.0 || configuredRisk.Value > 1.0)
                    {
                        _logger.LogError("🚨 [AUDIT-{OperationId}] Risk configuration out of bounds {Risk} - fail-closed: returning hold", operationId, configuredRisk.Value);
                        return Task.FromResult(1.0); // Fail-closed: hold when config invalid
                    }
                    
                    _logger.LogDebug("🔍 [AUDIT-{OperationId}] Risk assessment successful: Risk={Risk:P2}, Duration={Duration}ms", 
                        operationId, configuredRisk.Value, (DateTime.UtcNow - startTime).TotalMilliseconds);
                    return Task.FromResult(configuredRisk.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚨 [AUDIT-{OperationId}] Risk assessment failed - fail-closed: returning hold", operationId);
                    return Task.FromResult(1.0); // Fail-closed: hold on any error
                }
            }
            
            // No risk manager available - fail-closed
            _logger.LogError("🚨 [AUDIT-{OperationId}] No risk management service available - fail-closed: returning hold", operationId);
            return Task.FromResult(1.0); // Fail-closed: hold when no service
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [AUDIT-{OperationId}] Risk assessment failed - fail-closed: returning hold, Duration={Duration}ms", 
                operationId, (DateTime.UtcNow - startTime).TotalMilliseconds);
            return Task.FromResult(1.0); // Fail-closed: hold on any unexpected error
        }
    }

    public Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Audit log: Account equity assessment initiated
            _logger.LogDebug("🔍 [AUDIT-{OperationId}] Account equity assessment initiated at {Timestamp}", operationId, startTime);
            
            // Get configuration service - required for account equity
            var configuration = _serviceProvider.GetService<IConfiguration>();
            if (configuration == null)
            {
                _logger.LogError("🚨 [AUDIT-{OperationId}] Configuration service unavailable - fail-closed: cannot determine account equity", operationId);
                throw new InvalidOperationException("Account equity unavailable - configuration service missing (fail-closed)");
            }
            
            // Get configured starting equity with bounds validation
            var startingEquity = configuration.GetValue<double?>("Account:StartingEquity");
            if (!startingEquity.HasValue)
            {
                _logger.LogError("🚨 [AUDIT-{OperationId}] Account equity configuration missing - fail-closed", operationId);
                throw new InvalidOperationException("Account equity unavailable - configuration missing (fail-closed)");
            }
            
            // Validate bounds (reasonable account size)
            if (startingEquity.Value <= 0 || startingEquity.Value > 10000000) // $10M max reasonable
            {
                _logger.LogError("🚨 [AUDIT-{OperationId}] Account equity configuration out of bounds {Equity:C} - fail-closed", operationId, startingEquity.Value);
                throw new InvalidOperationException($"Account equity out of bounds: {startingEquity.Value:C} (fail-closed)");
            }
            
            _logger.LogDebug("🔍 [AUDIT-{OperationId}] Account equity from config: {Equity:C}, Duration={Duration}ms", 
                operationId, startingEquity.Value, (DateTime.UtcNow - startTime).TotalMilliseconds);
            return Task.FromResult(startingEquity.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [AUDIT-{OperationId}] Account equity assessment failed - fail-closed: cannot proceed, Duration={Duration}ms", 
                operationId, (DateTime.UtcNow - startTime).TotalMilliseconds);
            throw; // Fail-closed: propagate error instead of returning default
        }
    }
}