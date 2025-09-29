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
        try
        {
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
                
                _logger.LogTrace("Current risk retrieved from EnhancedRiskManager: {Risk:P2} (PnL: {PnL:C}, Drawdown: {DD:P2})", 
                    currentRisk, riskState.CurrentPnL, riskState.DrawdownFromPeak);
                    
                return currentRisk;
            }

            // Fallback to basic risk manager from abstractions
            var basicRiskManager = _serviceProvider.GetService<TradingBot.Abstractions.IRiskManager>();
            if (basicRiskManager != null)
            {
                // Use risk breach status as simple risk level
                var riskLevel = basicRiskManager.IsRiskBreached ? 0.8 : 0.2; // 80% risk if breached, 20% if normal
                
                _logger.LogTrace("Current risk retrieved from basic IRiskManager: {Risk:P2} (breached: {Breached})", 
                    riskLevel, basicRiskManager.IsRiskBreached);
                    
                return riskLevel;
            }
            
            // If no risk managers available, return conservative default
            _logger.LogWarning("No risk management service available - using conservative default risk level");
            return 0.1; // 10% conservative risk level when no service available
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current risk - using safe default");
            return 0.05; // 5% very conservative risk level on errors
        }
    }

    public async Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the real EnhancedRiskManager service for production account equity
            var enhancedRiskManager = _serviceProvider.GetService<Trading.Safety.IEnhancedRiskManager>();
            if (enhancedRiskManager != null)
            {
                var riskState = await enhancedRiskManager.GetCurrentRiskStateAsync().ConfigureAwait(false);
                
                // Calculate current account equity (starting equity + current P&L)
                var currentEquity = riskState.StartingCapital + riskState.CurrentPnL;
                
                _logger.LogTrace("Account equity retrieved from EnhancedRiskManager: {Equity:C} (starting: {Starting:C}, PnL: {PnL:C})", 
                    currentEquity, riskState.StartingCapital, riskState.CurrentPnL);
                    
                return (double)currentEquity;
            }

            // Fallback to configuration-based account equity
            var configuration = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration != null)
            {
                var configuredEquity = configuration.GetValue<double>("Account:StartingEquity", 100000.0);
                
                _logger.LogTrace("Account equity retrieved from configuration: {Equity:C}", configuredEquity);
                return configuredEquity;
            }
            
            // Conservative default for demo/test environments
            _logger.LogWarning("No account service available - using conservative default equity");
            return 50000.0; // $50K conservative default
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account equity - using safe default");
            return 25000.0; // $25K very conservative default on errors
        }
    }
}