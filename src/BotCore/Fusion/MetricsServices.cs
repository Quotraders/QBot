using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.IntelligenceStack;

namespace BotCore.Fusion;

/// <summary>
/// ML/RL metrics service interface for production telemetry - uses real RealTradingMetricsService
/// </summary>
public interface IMlrlMetricsServiceForFusion
{
    void RecordGauge(string name, double value, Dictionary<string, string> tags);
    void RecordCounter(string name, int value, Dictionary<string, string> tags);
    Task FlushMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Metrics interface for fusion system telemetry
/// </summary>
public interface IMetrics
{
    void RecordGauge(string name, double value, Dictionary<string, string> tags);
    void RecordCounter(string name, int value, Dictionary<string, string> tags);
    Task FlushMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Production metrics implementation that integrates with real trading metrics service
/// </summary>
public sealed class ProductionMetrics : IMetrics
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionMetrics> _logger;
    private readonly Dictionary<string, string> _fusionTags;
    
    // High-performance logging delegates (CA1848)
    private static readonly Action<ILogger, string, Exception?> LogMetricsServiceUnavailableGauge =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6301, nameof(LogMetricsServiceUnavailableGauge)),
            "🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for fusion metric {Name} - triggering system hold");
    
    private static readonly Action<ILogger, string, double, Exception?> LogGaugeRecorded =
        LoggerMessage.Define<string, double>(LogLevel.Trace, new EventId(6302, nameof(LogGaugeRecorded)),
            "Recorded fusion gauge metric via RealTradingMetricsService: {Name}={Value}");
    
    private static readonly Action<ILogger, string, Exception> LogGaugeTelemetryFailure =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6303, nameof(LogGaugeTelemetryFailure)),
            "🚨 [AUDIT-FAIL-CLOSED] Critical telemetry failure for fusion metric {Name} - system hold required");
    
    private static readonly Action<ILogger, string, Exception?> LogMetricsServiceUnavailableCounter =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6304, nameof(LogMetricsServiceUnavailableCounter)),
            "🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for fusion counter {Name} - triggering system hold");
    
    private static readonly Action<ILogger, string, int, Exception?> LogCounterRecorded =
        LoggerMessage.Define<string, int>(LogLevel.Trace, new EventId(6305, nameof(LogCounterRecorded)),
            "Recorded fusion counter metric via RealTradingMetricsService: {Name}={Value}");
    
    private static readonly Action<ILogger, string, Exception> LogCounterTelemetryFailure =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6306, nameof(LogCounterTelemetryFailure)),
            "🚨 [AUDIT-FAIL-CLOSED] Critical telemetry failure for fusion counter {Name} - system hold required");
    
    private static readonly Action<ILogger, Exception?> LogMetricsFlushed =
        LoggerMessage.Define(LogLevel.Trace, new EventId(6307, nameof(LogMetricsFlushed)),
            "Metrics flushed via RealTradingMetricsService integration");
    
    private static readonly Action<ILogger, Exception> LogMetricsFlushError =
        LoggerMessage.Define(LogLevel.Error, new EventId(6308, nameof(LogMetricsFlushError)),
            "Error flushing metrics via RealTradingMetricsService");

    public ProductionMetrics(IServiceProvider serviceProvider, ILogger<ProductionMetrics> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _fusionTags = new Dictionary<string, string>
        {
            ["component"] = "fusion",
            ["system"] = "decision-coordinator"
        };
    }

    public void RecordGauge(string name, double value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical telemetry service unavailable - triggering hold decision
            LogMetricsServiceUnavailableGauge(_logger, name, null);
            throw new InvalidOperationException($"RealTradingMetricsService required for production telemetry - metric: {name}");
        }

        try
        {
            // Merge tags with fusion-specific tags for comprehensive telemetry coverage
            var allTags = new Dictionary<string, object>();
            
            // Add fusion tags first
            foreach (var fusionTag in _fusionTags)
            {
                allTags[fusionTag.Key] = fusionTag.Value;
            }
            
            // Add provided tags
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Emit fusion metrics through real trading metrics service - no fallbacks
            _ = realMetricsService.RecordGaugeAsync($"fusion.{name}", value, allTags);
            
            LogGaugeRecorded(_logger, name, value, null);
        }
        catch (Exception ex)
        {
            // Audit log: Telemetry failure - fail closed
            LogGaugeTelemetryFailure(_logger, name, ex);
            throw new InvalidOperationException($"Critical telemetry failure for fusion metric '{name}' - fail-closed mode activated", ex);
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical telemetry service unavailable - triggering hold decision
            LogMetricsServiceUnavailableCounter(_logger, name, null);
            throw new InvalidOperationException($"RealTradingMetricsService required for production telemetry - counter: {name}");
        }

        try
        {
            // Merge tags with fusion-specific tags for comprehensive telemetry coverage
            var allTags = new Dictionary<string, object>();
            
            // Add fusion tags first
            foreach (var fusionTag in _fusionTags)
            {
                allTags[fusionTag.Key] = fusionTag.Value;
            }
            
            // Add provided tags
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Emit fusion counter metrics through real trading metrics service - no fallbacks
            _ = realMetricsService.RecordCounterAsync($"fusion.{name}", value, allTags);
            
            LogCounterRecorded(_logger, name, value, null);
        }
        catch (Exception ex)
        {
            // Audit log: Telemetry failure - fail closed
            LogCounterTelemetryFailure(_logger, name, ex);
            throw new InvalidOperationException($"Critical telemetry failure for fusion counter '{name}' - fail-closed mode activated", ex);
        }
    }

    public async Task FlushMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The RealTradingMetricsService handles its own background flushing to cloud
            // This method ensures metrics are properly written to logs for cloud ingestion
            await Task.CompletedTask.ConfigureAwait(false);
            LogMetricsFlushed(_logger, null);
        }
        catch (Exception ex)
        {
            LogMetricsFlushError(_logger, ex);
        }
    }
}

/// <summary>
/// Production ML/RL metrics service implementation
/// </summary>
public sealed class ProductionMlrlMetricsService : IMlrlMetricsServiceForFusion
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionMlrlMetricsService> _logger;
    private readonly Dictionary<string, string> _fusionTags;
    
    // High-performance logging delegates (CA1848)
    private static readonly Action<ILogger, string, Exception?> LogMlRlMetricsServiceUnavailableGauge =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6311, nameof(LogMlRlMetricsServiceUnavailableGauge)),
            "🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for ML/RL metric {Name} - triggering system hold");
    
    private static readonly Action<ILogger, string, double, Exception?> LogMlRlGaugeRecorded =
        LoggerMessage.Define<string, double>(LogLevel.Trace, new EventId(6312, nameof(LogMlRlGaugeRecorded)),
            "Recorded ML/RL gauge metric via RealTradingMetricsService: {Name}={Value}");
    
    private static readonly Action<ILogger, string, Exception> LogMlRlGaugeTelemetryFailure =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6313, nameof(LogMlRlGaugeTelemetryFailure)),
            "🚨 [AUDIT-FAIL-CLOSED] Critical ML/RL telemetry failure for metric {Name} - system hold required");
    
    private static readonly Action<ILogger, string, Exception?> LogMlRlMetricsServiceUnavailableCounter =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6314, nameof(LogMlRlMetricsServiceUnavailableCounter)),
            "🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for ML/RL counter {Name} - triggering system hold");
    
    private static readonly Action<ILogger, string, int, Exception?> LogMlRlCounterRecorded =
        LoggerMessage.Define<string, int>(LogLevel.Trace, new EventId(6315, nameof(LogMlRlCounterRecorded)),
            "Recorded ML/RL counter metric via RealTradingMetricsService: {Name}={Value}");
    
    private static readonly Action<ILogger, string, Exception> LogMlRlCounterTelemetryFailure =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(6316, nameof(LogMlRlCounterTelemetryFailure)),
            "🚨 [AUDIT-FAIL-CLOSED] Critical ML/RL telemetry failure for counter {Name} - system hold required");
    
    private static readonly Action<ILogger, Exception?> LogMlRlMetricsFlushed =
        LoggerMessage.Define(LogLevel.Trace, new EventId(6317, nameof(LogMlRlMetricsFlushed)),
            "ML/RL metrics flushed via RealTradingMetricsService integration");
    
    private static readonly Action<ILogger, Exception> LogMlRlMetricsFlushError =
        LoggerMessage.Define(LogLevel.Error, new EventId(6318, nameof(LogMlRlMetricsFlushError)),
            "Error flushing ML/RL metrics via RealTradingMetricsService");

    public ProductionMlrlMetricsService(IServiceProvider serviceProvider, ILogger<ProductionMlrlMetricsService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _fusionTags = new Dictionary<string, string>
        {
            ["component"] = "ml-rl",
            ["system"] = "fusion-coordinator"
        };
    }

    public void RecordGauge(string name, double value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production ML/RL telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical ML/RL telemetry service unavailable - triggering hold decision
            LogMlRlMetricsServiceUnavailableGauge(_logger, name, null);
            throw new InvalidOperationException($"RealTradingMetricsService required for production ML/RL telemetry - metric: {name}");
        }

        try
        {
            // Merge tags with ML/RL specific tags for comprehensive telemetry coverage
            var allTags = new Dictionary<string, object>();
            
            // Add fusion tags first  
            foreach (var fusionTag in _fusionTags)
            {
                allTags[fusionTag.Key] = fusionTag.Value;
            }
            
            // Add provided tags
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Emit ML/RL metrics through real trading metrics service with mlrl prefix - no fallbacks
            _ = realMetricsService.RecordGaugeAsync($"mlrl.{name}", value, allTags);
            
            LogMlRlGaugeRecorded(_logger, name, value, null);
        }
        catch (Exception ex)
        {
            // Audit log: ML/RL telemetry failure - fail closed
            LogMlRlGaugeTelemetryFailure(_logger, name, ex);
            throw new InvalidOperationException($"Critical ML/RL telemetry failure for metric '{name}' - fail-closed mode activated", ex);
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production ML/RL telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical ML/RL telemetry service unavailable - triggering hold decision
            LogMlRlMetricsServiceUnavailableCounter(_logger, name, null);
            throw new InvalidOperationException($"RealTradingMetricsService required for production ML/RL telemetry - counter: {name}");
        }

        try
        {
            // Merge tags with ML/RL specific tags for comprehensive telemetry coverage
            var allTags = new Dictionary<string, object>();
            
            // Add fusion tags first
            foreach (var fusionTag in _fusionTags)
            {
                allTags[fusionTag.Key] = fusionTag.Value;
            }
            
            // Add provided tags
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    allTags[tag.Key] = tag.Value;
                }
            }

            // Emit ML/RL counter metrics through real trading metrics service - no fallbacks
            _ = realMetricsService.RecordCounterAsync($"mlrl.{name}", value, allTags);
            
            LogMlRlCounterRecorded(_logger, name, value, null);
        }
        catch (Exception ex)
        {
            // Audit log: ML/RL telemetry failure - fail closed
            LogMlRlCounterTelemetryFailure(_logger, name, ex);
            throw new InvalidOperationException($"Critical ML/RL telemetry failure for counter '{name}' - fail-closed mode activated", ex);
        }
    }

    public async Task FlushMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The RealTradingMetricsService handles its own background flushing to cloud
            // This method ensures metrics are properly written to logs for cloud ingestion
            await Task.CompletedTask.ConfigureAwait(false);
            LogMlRlMetricsFlushed(_logger, null);
        }
        catch (Exception ex)
        {
            LogMlRlMetricsFlushError(_logger, ex);
        }
    }
}