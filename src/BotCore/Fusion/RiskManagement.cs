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
            var riskManager = _serviceProvider.GetService<BotCore.Services.EnhancedRiskManager>();
            if (riskManager != null)
            {
                // Get risk from the real risk management system
                var currentRisk = await riskManager.CalculateCurrentRiskAsync(cancellationToken).ConfigureAwait(false);
                
                _logger.LogTrace("Current risk retrieved: {Risk:P2}", currentRisk);
                return currentRisk;
            }

            // Fallback to default risk level
            const double defaultRisk = 0.02; // 2% default risk
            _logger.LogWarning("EnhancedRiskManager not available, using default risk: {Risk:P2}", defaultRisk);
            return defaultRisk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current risk");
            // Fail-safe: return conservative risk level
            return 0.01; // 1% conservative risk
        }
    }

    public async Task<double> GetAccountEquityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var riskManager = _serviceProvider.GetService<BotCore.Services.EnhancedRiskManager>();
            if (riskManager != null)
            {
                // Get account equity from the real risk management system
                var equity = await riskManager.GetAccountEquityAsync(cancellationToken).ConfigureAwait(false);
                
                _logger.LogTrace("Account equity retrieved: {Equity:C}", equity);
                return equity;
            }

            // Fallback to estimated equity
            const double defaultEquity = 50000.0; // $50K default
            _logger.LogWarning("EnhancedRiskManager not available, using default equity: {Equity:C}", defaultEquity);
            return defaultEquity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account equity");
            // Fail-safe: return conservative equity estimate
            return 25000.0; // $25K conservative estimate
        }
    }
}