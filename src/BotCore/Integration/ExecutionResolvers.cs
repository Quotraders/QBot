using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Execution Analytics and Performance Resolvers
public sealed class ExecutionSlippageResolver : IFeatureResolver
{
    // Trading metrics constants
    private const double BasisPointsMultiplier = 10000.0;
    
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionSlippageResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogSlippageValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6420, nameof(LogSlippageValue)),
            "Average execution slippage for {Symbol}: {Slippage:F4} bps");
    
    private static readonly Action<ILogger, string, Exception> LogSlippageResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6421, nameof(LogSlippageResolutionFailed)),
            "Failed to resolve execution slippage for symbol {Symbol}");
    
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
            
            LogSlippageValue(_logger, symbol, avgSlippage * BasisPointsMultiplier, null);
            return avgSlippage;
        }
        catch (Exception ex)
        {
            LogSlippageResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production execution slippage resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class ExecutionFillRateResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionFillRateResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogFillRateValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6422, nameof(LogFillRateValue)),
            "Execution fill rate for {Symbol}: {FillRate:P2}");
    
    private static readonly Action<ILogger, string, Exception> LogFillRateResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6423, nameof(LogFillRateResolutionFailed)),
            "Failed to resolve execution fill rate for symbol {Symbol}");
    
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
            
            LogFillRateValue(_logger, symbol, fillRate, null);
            return fillRate;
        }
        catch (Exception ex)
        {
            LogFillRateResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production execution fill rate resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class DecisionLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DecisionLatencyResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogLatencyValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6424, nameof(LogLatencyValue)),
            "Decision latency for {Symbol}: {Latency:F2}ms");
    
    private static readonly Action<ILogger, string, Exception> LogLatencyResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6425, nameof(LogLatencyResolutionFailed)),
            "Failed to resolve decision latency for symbol {Symbol}");
    
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
            
            LogLatencyValue(_logger, symbol, avgLatency, null);
            return avgLatency;
        }
        catch (Exception ex)
        {
            LogLatencyResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production decision latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class OrderLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderLatencyResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogOrderLatencyValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6426, nameof(LogOrderLatencyValue)),
            "Average order latency for {Symbol}: {Latency:F1} ms");
    
    private static readonly Action<ILogger, string, Exception> LogOrderLatencyResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6427, nameof(LogOrderLatencyResolutionFailed)),
            "Failed to resolve order latency for symbol {Symbol}");
    
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
            
            LogOrderLatencyValue(_logger, symbol, avgLatency, null);
            return avgLatency;
        }
        catch (Exception ex)
        {
            LogOrderLatencyResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production order latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RecentBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecentBarCountResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogRecentBarCount =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6428, nameof(LogRecentBarCount)),
            "Recent bar count for {Symbol}: {Count}");
    
    private static readonly Action<ILogger, string, Exception> LogRecentBarCountResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6429, nameof(LogRecentBarCountResolutionFailed)),
            "Failed to resolve recent bar count for symbol {Symbol}");
    
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
            
            LogRecentBarCount(_logger, symbol, recentBarCount, null);
            return recentBarCount;
        }
        catch (Exception ex)
        {
            LogRecentBarCountResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production recent bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class ProcessedBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessedBarCountResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogProcessedBarCount =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6430, nameof(LogProcessedBarCount)),
            "Total processed bar count for {Symbol}: {Count}");
    
    private static readonly Action<ILogger, string, Exception> LogProcessedBarCountResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6431, nameof(LogProcessedBarCountResolutionFailed)),
            "Failed to resolve processed bar count for symbol {Symbol}");
    
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
            
            LogProcessedBarCount(_logger, symbol, processedBarCount, null);
            return processedBarCount;
        }
        catch (Exception ex)
        {
            LogProcessedBarCountResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production processed bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}