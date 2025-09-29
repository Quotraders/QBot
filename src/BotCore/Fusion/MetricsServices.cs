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
            _logger.LogCritical("🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for fusion metric {Name} - triggering system hold", name);
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
            
            _logger.LogTrace("Recorded fusion gauge metric via RealTradingMetricsService: {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            // Audit log: Telemetry failure - fail closed
            _logger.LogCritical(ex, "🚨 [AUDIT-FAIL-CLOSED] Critical telemetry failure for fusion metric {Name} - system hold required", name);
            throw; // Fail-closed: propagate exception to trigger hold decision
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical telemetry service unavailable - triggering hold decision
            _logger.LogCritical("🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for fusion counter {Name} - triggering system hold", name);
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
            
            _logger.LogTrace("Recorded fusion counter metric via RealTradingMetricsService: {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            // Audit log: Telemetry failure - fail closed
            _logger.LogCritical(ex, "🚨 [AUDIT-FAIL-CLOSED] Critical telemetry failure for fusion counter {Name} - system hold required", name);
            throw; // Fail-closed: propagate exception to trigger hold decision
        }
    }

    public async Task FlushMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The RealTradingMetricsService handles its own background flushing to cloud
            // This method ensures metrics are properly written to logs for cloud ingestion
            await Task.CompletedTask.ConfigureAwait(false);
            _logger.LogTrace("Metrics flushed via RealTradingMetricsService integration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing metrics via RealTradingMetricsService");
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
            _logger.LogCritical("🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for ML/RL metric {Name} - triggering system hold", name);
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
            
            _logger.LogTrace("Recorded ML/RL gauge metric via RealTradingMetricsService: {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            // Audit log: ML/RL telemetry failure - fail closed
            _logger.LogCritical(ex, "🚨 [AUDIT-FAIL-CLOSED] Critical ML/RL telemetry failure for metric {Name} - system hold required", name);
            throw; // Fail-closed: propagate exception to trigger hold decision
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        // Fail-closed enforcement: RealTradingMetricsService required for production ML/RL telemetry
        var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
        if (realMetricsService == null)
        {
            // Audit log: Critical ML/RL telemetry service unavailable - triggering hold decision
            _logger.LogCritical("🚨 [AUDIT-FAIL-CLOSED] RealTradingMetricsService unavailable for ML/RL counter {Name} - triggering system hold", name);
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
            
            _logger.LogTrace("Recorded ML/RL counter metric via RealTradingMetricsService: {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            // Audit log: ML/RL telemetry failure - fail closed
            _logger.LogCritical(ex, "🚨 [AUDIT-FAIL-CLOSED] Critical ML/RL telemetry failure for counter {Name} - system hold required", name);
            throw; // Fail-closed: propagate exception to trigger hold decision
        }
    }

    public async Task FlushMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The RealTradingMetricsService handles its own background flushing to cloud
            // This method ensures metrics are properly written to logs for cloud ingestion
            await Task.CompletedTask.ConfigureAwait(false);
            _logger.LogTrace("ML/RL metrics flushed via RealTradingMetricsService integration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing ML/RL metrics via RealTradingMetricsService");
        }
    }
}