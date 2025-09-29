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

    public Task<double> GetCurrentRiskAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get real risk management service
            var riskManagerService = _serviceProvider.GetService<BotCore.Services.IRiskManager>();
            if (riskManagerService != null)
            {
                var riskState = riskManagerService.GetCurrentRiskState();
                if (riskState != null)
                {
                    var currentRisk = (double)riskState.RiskLevel;
                    _logger.LogTrace("Current risk retrieved from service: {Risk:P2}", currentRisk);
                    return Task.FromResult(currentRisk);
                }
            }
            
            // Try enhanced risk manager
            var enhancedRiskService = _serviceProvider.GetService<TradingBot.Abstractions.IRiskManager>();
            if (enhancedRiskService != null)
            {
                var riskMetrics = enhancedRiskService.GetRiskMetrics();
                if (riskMetrics != null)
                {
                    var risk = Math.Max(riskMetrics.CurrentDrawdown, riskMetrics.DailyRisk);
                    _logger.LogTrace("Current risk from enhanced service: {Risk:P2}", risk);
                    return Task.FromResult(risk);
                }
            }
            
            // Fail fast if no real risk management service is available
            _logger.LogError("No risk management service available - real integration required");
            throw new InvalidOperationException("Risk management service not available or not configured");
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error retrieving current risk");
            throw new InvalidOperationException($"Failed to get current risk: {ex.Message}", ex);
        }
    }

    public Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get real risk/account management service
            var accountService = _serviceProvider.GetService<BotCore.Services.IAccountService>();
            if (accountService != null)
            {
                var accountInfo = accountService.GetAccountInfo();
                if (accountInfo != null && accountInfo.Equity > 0)
                {
                    var equity = (double)accountInfo.Equity;
                    _logger.LogTrace("Account equity retrieved from service: {Equity:C}", equity);
                    return Task.FromResult(equity);
                }
            }
            
            // Try enhanced risk manager for account equity
            var enhancedRiskService = _serviceProvider.GetService<TradingBot.Abstractions.IRiskManager>();
            if (enhancedRiskService != null)
            {
                var riskMetrics = enhancedRiskService.GetRiskMetrics();
                if (riskMetrics != null && riskMetrics.AccountValue > 0)
                {
                    var equity = riskMetrics.AccountValue;
                    _logger.LogTrace("Account equity from enhanced service: {Equity:C}", equity);
                    return Task.FromResult(equity);
                }
            }
            
            // Fail fast if no real account service is available
            _logger.LogError("No account service available - real integration required");
            throw new InvalidOperationException("Account service not available or not configured");
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error retrieving account equity");
            throw new InvalidOperationException($"Failed to get account equity: {ex.Message}", ex);
        }
    }
}