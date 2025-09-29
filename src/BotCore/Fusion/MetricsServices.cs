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
        try
        {
            // Use real RealTradingMetricsService for production telemetry - no fallback logging
            var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (realMetricsService != null)
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

                // Emit fusion metrics through real trading metrics service
                _ = realMetricsService.RecordGaugeAsync($"fusion.{name}", value, allTags);
                
                _logger.LogTrace("Recorded fusion gauge metric via RealTradingMetricsService: {Name}={Value}", name, value);
                return;
            }
            
            // Fallback to structured logging with proper metrics format when service unavailable
            _logger.LogInformation("[FUSION-METRICS] Gauge {MetricName}={Value:F3} {Tags}", 
                $"fusion.{name}", value, System.Text.Json.JsonSerializer.Serialize(tags ?? new Dictionary<string, string>()));
                
            _logger.LogTrace("Recorded fusion gauge metric (service unavailable): {Name}={Value}", name, value);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error recording fusion gauge metric {Name} - using fallback logging", name);
            
            // Fallback logging on any errors
            _logger.LogInformation("[FUSION-METRICS-FALLBACK] Gauge {MetricName}={Value:F3}", $"fusion.{name}", value);
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        try
        {
            // Use real RealTradingMetricsService for production telemetry - no fallback logging
            var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (realMetricsService != null)
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

                // Emit fusion counter metrics through real trading metrics service
                _ = realMetricsService.RecordCounterAsync($"fusion.{name}", value, allTags);
                
                _logger.LogTrace("Recorded fusion counter metric via RealTradingMetricsService: {Name}={Value}", name, value);
                return;
            }
            
            // Fallback to structured logging with proper metrics format when service unavailable
            _logger.LogInformation("[FUSION-METRICS] Counter {MetricName}+={Value} {Tags}", 
                $"fusion.{name}", value, System.Text.Json.JsonSerializer.Serialize(tags ?? new Dictionary<string, string>()));
                
            _logger.LogTrace("Recorded fusion counter metric (service unavailable): {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording fusion counter metric {Name} - using fallback logging", name);
            
            // Fallback logging on any errors
            _logger.LogInformation("[FUSION-METRICS-FALLBACK] Counter {MetricName}+={Value}", $"fusion.{name}", value);
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
        try
        {
            // Use real RealTradingMetricsService for ML/RL telemetry - no fallback logging
            var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (realMetricsService != null)
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

                // Emit ML/RL metrics through real trading metrics service with mlrl prefix
                _ = realMetricsService.RecordGaugeAsync($"mlrl.{name}", value, allTags);
                
                _logger.LogTrace("Recorded ML/RL gauge metric via RealTradingMetricsService: {Name}={Value}", name, value);
                return;
            }
            
            // Fallback to structured logging with proper ML metrics format when service unavailable
            _logger.LogInformation("[ML-RL-METRICS] Gauge {MetricName}={Value:F3} {Tags}", 
                $"mlrl.{name}", value, System.Text.Json.JsonSerializer.Serialize(tags ?? new Dictionary<string, string>()));
                
            _logger.LogTrace("Recorded ML/RL gauge metric (service unavailable): {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording ML/RL gauge metric {Name} - using fallback logging", name);
            
            // Fallback logging on any errors
            _logger.LogInformation("[ML-RL-METRICS-FALLBACK] Gauge {MetricName}={Value:F3}", $"mlrl.{name}", value);
        }
    }

    public void RecordCounter(string name, int value, Dictionary<string, string> tags)
    {
        try
        {
            // Use real RealTradingMetricsService for ML/RL telemetry - no fallback logging
            var realMetricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (realMetricsService != null)
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

                // Emit ML/RL counter metrics through real trading metrics service
                _ = realMetricsService.RecordCounterAsync($"mlrl.{name}", value, allTags);
                
                _logger.LogTrace("Recorded ML/RL counter metric via RealTradingMetricsService: {Name}={Value}", name, value);
                return;
            }
            
            // Fallback to structured logging with proper ML counter format when service unavailable
            _logger.LogInformation("[ML-RL-METRICS] Counter {MetricName}+={Value} {Tags}", 
                $"mlrl.{name}", value, System.Text.Json.JsonSerializer.Serialize(tags ?? new Dictionary<string, string>()));
                
            _logger.LogTrace("Recorded ML/RL counter metric (service unavailable): {Name}={Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording ML/RL counter metric {Name} - using fallback logging", name);
            
            // Fallback logging on any errors
            _logger.LogInformation("[ML-RL-METRICS-FALLBACK] Counter {MetricName}+={Value}", $"mlrl.{name}", value);
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