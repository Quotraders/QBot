using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Execution Analytics and Performance Resolvers
public sealed class ExecutionSlippageResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionSlippageResolver> _logger;
    
    public ExecutionSlippageResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ExecutionSlippageResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: ExecutionAnalyticsService must exist - fail closed if not available
            var executionAnalytics = _serviceProvider.GetRequiredService<BotCore.Services.ExecutionAnalyticsService>();
            var avgSlippage = await executionAnalytics.GetAverageSlippageAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Average execution slippage for {Symbol}: {Slippage:F4} bps", symbol, avgSlippage * 10000);
            return avgSlippage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve execution slippage for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production execution slippage resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class ExecutionFillRateResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionFillRateResolver> _logger;
    
    public ExecutionFillRateResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ExecutionFillRateResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: ExecutionAnalyticsService must exist - fail closed if not available
            var executionAnalytics = _serviceProvider.GetRequiredService<BotCore.Services.ExecutionAnalyticsService>();
            var fillRate = await executionAnalytics.GetFillRateAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Execution fill rate for {Symbol}: {FillRate:P2}", symbol, fillRate);
            return fillRate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve execution fill rate for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production execution fill rate resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class DecisionLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DecisionLatencyResolver> _logger;
    
    public DecisionLatencyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<DecisionLatencyResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: PerformanceMetricsService must exist - fail closed if not available
            var performanceMetrics = _serviceProvider.GetRequiredService<BotCore.Services.PerformanceMetricsService>();
            var avgLatency = await performanceMetrics.GetAverageDecisionLatencyAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Average decision latency for {Symbol}: {Latency:F1} ms", symbol, avgLatency);
            return avgLatency;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve decision latency for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production decision latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class OrderLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderLatencyResolver> _logger;
    
    public OrderLatencyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<OrderLatencyResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: PerformanceMetricsService must exist - fail closed if not available
            var performanceMetrics = _serviceProvider.GetRequiredService<BotCore.Services.PerformanceMetricsService>();
            var avgLatency = await performanceMetrics.GetAverageOrderLatencyAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Average order latency for {Symbol}: {Latency:F1} ms", symbol, avgLatency);
            return avgLatency;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve order latency for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production order latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RecentBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecentBarCountResolver> _logger;
    
    public RecentBarCountResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<RecentBarCountResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: BarTrackingService must exist - fail closed if not available
            var barTracker = _serviceProvider.GetRequiredService<BotCore.Services.BarTrackingService>();
            var recentBarCount = await barTracker.GetRecentBarCountAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Recent bar count for {Symbol}: {Count}", symbol, recentBarCount);
            return recentBarCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve recent bar count for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production recent bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class ProcessedBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessedBarCountResolver> _logger;
    
    public ProcessedBarCountResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ProcessedBarCountResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: BarTrackingService must exist - fail closed if not available
            var barTracker = _serviceProvider.GetRequiredService<BotCore.Services.BarTrackingService>();
            var processedBarCount = await barTracker.GetProcessedBarCountAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Total processed bar count for {Symbol}: {Count}", symbol, processedBarCount);
            return processedBarCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve processed bar count for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production processed bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}