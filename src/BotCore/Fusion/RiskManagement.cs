using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BotCore.Services;

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

    public async Task<double> GetCurrentRiskAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Audit log: Risk assessment initiated
            _logger.LogDebug("🔍 [AUDIT-{OperationId}] Risk assessment initiated at {Timestamp}", operationId, startTime);
            
            // Use the real EnhancedRiskManager service for production risk assessment
            var enhancedRiskManager = _serviceProvider.GetService<Trading.Safety.IEnhancedRiskManager>();
            if (enhancedRiskManager != null)
            {
                var riskState = await enhancedRiskManager.GetCurrentRiskStateAsync().ConfigureAwait(false);
                
                // Calculate current risk as percentage of daily loss limit
                var currentRisk = Math.Max(
                    Math.Abs(riskState.CurrentPnL / riskState.DailyLossLimit),
                    Math.Abs(riskState.DrawdownFromPeak / riskState.MaxDrawdownLimit)
                );
                
                // Audit log: Enhanced risk manager result
                _logger.LogDebug("🔍 [AUDIT-{OperationId}] Enhanced risk assessment: Risk={Risk:P2}, PnL={PnL:C}, Drawdown={Drawdown:P2}, Duration={Duration}ms", 
                    operationId, currentRisk, riskState.CurrentPnL, riskState.DrawdownFromPeak, (DateTime.UtcNow - startTime).TotalMilliseconds);
                    
                return currentRisk;
            }

            // Fallback to basic risk manager from abstractions
            var basicRiskManager = _serviceProvider.GetService<TradingBot.Abstractions.IRiskManager>();
            if (basicRiskManager != null)
            {
                // Use risk breach status as simple risk level
                var riskLevel = basicRiskManager.IsRiskBreached ? GetConfigValue("Risk:BreachedRiskLevel", 0.8) : GetConfigValue("Risk:NormalRiskLevel", 0.2);
                
                // Audit log: Basic risk manager result
                _logger.LogDebug("🔍 [AUDIT-{OperationId}] Basic risk assessment: Risk={Risk:P2}, Breached={Breached}, Duration={Duration}ms", 
                    operationId, riskLevel, basicRiskManager.IsRiskBreached, (DateTime.UtcNow - startTime).TotalMilliseconds);
                    
                return riskLevel;
            }
            
            // Conservative default with audit logging
            var conservativeRisk = GetConfigValue("Risk:ConservativeDefault", 0.1);
            _logger.LogWarning("🔍 [AUDIT-{OperationId}] No risk management service available - using conservative default: {Risk:P2}", operationId, conservativeRisk);
            return conservativeRisk;
        }
        catch (Exception ex)
        {
            var safeRisk = GetConfigValue("Risk:SafeDefault", 0.05);
            _logger.LogError(ex, "🚨 [AUDIT-{OperationId}] Risk assessment failed - using safe default: {Risk:P2}, Duration={Duration}ms", 
                operationId, safeRisk, (DateTime.UtcNow - startTime).TotalMilliseconds);
            return safeRisk;
        }
    }

    private double GetConfigValue(string key, double defaultValue)
    {
        var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        return configuration?.GetValue<double>(key) ?? defaultValue;
    }

    public async Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Audit log: Account equity assessment initiated
            _logger.LogDebug("🔍 [AUDIT-{OperationId}] Account equity assessment initiated at {Timestamp}", operationId, startTime);
            
            // Use the real EnhancedRiskManager service for production account equity
            var enhancedRiskManager = _serviceProvider.GetService<Trading.Safety.IEnhancedRiskManager>();
            if (enhancedRiskManager != null)
            {
                var riskState = await enhancedRiskManager.GetCurrentRiskStateAsync().ConfigureAwait(false);
                
                // Calculate current account equity (starting equity + current P&L)
                var currentEquity = riskState.StartingCapital + riskState.CurrentPnL;
                
                // Audit log: Enhanced account equity result
                _logger.LogDebug("🔍 [AUDIT-{OperationId}] Enhanced account equity: Equity={Equity:C}, Starting={Starting:C}, PnL={PnL:C}, Duration={Duration}ms", 
                    operationId, currentEquity, riskState.StartingCapital, riskState.CurrentPnL, (DateTime.UtcNow - startTime).TotalMilliseconds);
                    
                return (double)currentEquity;
            }

            // Fallback to configuration-based account equity
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration != null)
            {
                var configuredEquity = configuration.GetValue<double>("Account:StartingEquity", GetConfigValue("Account:DefaultEquity", 100000.0));
                
                // Audit log: Configuration-based equity result
                _logger.LogDebug("🔍 [AUDIT-{OperationId}] Configuration-based account equity: Equity={Equity:C}, Duration={Duration}ms", 
                    operationId, configuredEquity, (DateTime.UtcNow - startTime).TotalMilliseconds);
                return configuredEquity;
            }
            
            // Conservative default for demo/test environments
            var conservativeEquity = GetConfigValue("Account:ConservativeDefault", 50000.0);
            _logger.LogWarning("🔍 [AUDIT-{OperationId}] No account service available - using conservative default: {Equity:C}", operationId, conservativeEquity);
            return conservativeEquity;
        }
        catch (Exception ex)
        {
            var safeEquity = GetConfigValue("Account:SafeDefault", 25000.0);
            _logger.LogError(ex, "🚨 [AUDIT-{OperationId}] Account equity assessment failed - using safe default: {Equity:C}, Duration={Duration}ms", 
                operationId, safeEquity, (DateTime.UtcNow - startTime).TotalMilliseconds);
            return safeEquity;
        }
    }
}