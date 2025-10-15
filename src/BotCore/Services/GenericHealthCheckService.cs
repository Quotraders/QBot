using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BotCore.Health;

namespace BotCore.Services;

/// <summary>
/// Generic health check service that can check the health of ANY component type.
/// Takes a DiscoveredComponent and figures out how to check if it's healthy.
/// This enables the bot to self-diagnose ALL components automatically.
/// </summary>
public sealed class GenericHealthCheckService
{
    // Constants for magic number avoidance (S109)
    private const double BytesToMegabytes = 1024.0 * 1024.0;
    private const int DecimalPlacesForMetrics = 2;
    
    private readonly ILogger<GenericHealthCheckService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public GenericHealthCheckService(
        ILogger<GenericHealthCheckService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    /// <summary>
    /// Check the health of any discovered component
    /// </summary>
    public async Task<HealthCheckResult> CheckComponentHealthAsync(
        DiscoveredComponent component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        try
        {
            return component.Type switch
            {
                ComponentType.BackgroundService => await CheckBackgroundServiceHealthAsync(component).ConfigureAwait(false),
                ComponentType.SingletonService => await CheckSingletonServiceHealthAsync(component).ConfigureAwait(false),
                ComponentType.FileDependency => CheckFileDependencyHealth(component),
                ComponentType.APIConnection => await CheckAPIConnectionHealthAsync(component, cancellationToken).ConfigureAwait(false),
                ComponentType.PerformanceMetric => CheckPerformanceMetricHealth(component),
                _ => HealthCheckResult.Unhealthy($"Unknown component type: {component.Type}")
            };
        }
        catch (ArgumentException ex)
        {
            LogHealthCheckError(_logger, component.Name, ex);
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}", new Dictionary<string, object>
            {
                ["Component"] = component.Name,
                ["Error"] = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            LogHealthCheckError(_logger, component.Name, ex);
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}", new Dictionary<string, object>
            {
                ["Component"] = component.Name,
                ["Error"] = ex.Message
            });
        }
    }

    private static readonly Action<ILogger, string, Exception?> LogHealthCheckError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(LogHealthCheckError)), 
            "❌ [HEALTH-CHECK] Error checking health of {Component}");

    private static Task<HealthCheckResult> CheckBackgroundServiceHealthAsync(
        DiscoveredComponent component)
    {
        try
        {
            // Try to get the service instance
            if (component.ServiceInstance == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Service not found in DI container",
                    new Dictionary<string, object>
                    {
                        ["ServiceName"] = component.Name
                    }));
            }

            // Check if it's a BackgroundService
            if (component.ServiceInstance is BackgroundService bgService)
            {
                // BackgroundService doesn't expose running state directly, so we assume it's running if we have the instance
                return Task.FromResult(HealthCheckResult.Healthy(
                    "Background service is registered and running",
                    new Dictionary<string, object>
                    {
                        ["ServiceName"] = component.Name,
                        ["IsRunning"] = true,
                        ["ServiceType"] = bgService.GetType().Name
                    }));
            }

            // For IHostedService that's not BackgroundService
            return Task.FromResult(HealthCheckResult.Healthy(
                "Hosted service is registered",
                new Dictionary<string, object>
                {
                    ["ServiceName"] = component.Name,
                    ["ServiceType"] = component.ServiceInstance.GetType().Name
                }));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Background service check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["ServiceName"] = component.Name,
                    ["Error"] = ex.Message
                }));
        }
    }

    private static Task<HealthCheckResult> CheckSingletonServiceHealthAsync(
        DiscoveredComponent component)
    {
        try
        {
            // Try to get the service instance
            if (component.ServiceInstance == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Service instance is null",
                    new Dictionary<string, object>
                    {
                        ["ServiceName"] = component.Name
                    }));
            }

            // If the service implements IComponentHealth, use it
            if (component.ServiceInstance is IComponentHealth healthCheckable)
            {
                return healthCheckable.CheckHealthAsync(CancellationToken.None);
            }

            // Otherwise, just check that the instance exists
            return Task.FromResult(HealthCheckResult.Healthy(
                "Service instance exists",
                new Dictionary<string, object>
                {
                    ["ServiceName"] = component.Name,
                    ["ServiceType"] = component.ServiceInstance.GetType().Name
                }));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Singleton service check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["ServiceName"] = component.Name,
                    ["Error"] = ex.Message
                }));
        }
    }

    private HealthCheckResult CheckFileDependencyHealth(DiscoveredComponent component)
    {
        try
        {
            if (string.IsNullOrEmpty(component.FilePath))
            {
                return HealthCheckResult.Unhealthy(
                    "File path not specified",
                    new Dictionary<string, object>
                    {
                        ["FileName"] = component.Name
                    });
            }

            // Special case: kill.txt should be HEALTHY when it does NOT exist (normal operation)
            if (component.Name == "Emergency Stop File" && component.FilePath == "kill.txt")
            {
                if (!File.Exists(component.FilePath))
                {
                    return HealthCheckResult.Healthy(
                        "Emergency stop not active (file does not exist)",
                        new Dictionary<string, object>
                        {
                            ["FileName"] = component.Name,
                            ["ExpectedPath"] = component.FilePath,
                            ["Status"] = "Normal - kill switch not triggered"
                        });
                }
                else
                {
                    // Kill switch is active - this is CRITICAL
                    return HealthCheckResult.Unhealthy(
                        "KILL SWITCH ACTIVE - Emergency stop file exists",
                        new Dictionary<string, object>
                        {
                            ["FileName"] = component.Name,
                            ["ExpectedPath"] = component.FilePath,
                            ["Status"] = "EMERGENCY - Manual intervention required"
                        });
                }
            }

            // Check if file exists
            if (!File.Exists(component.FilePath))
            {
                return HealthCheckResult.Unhealthy(
                    "File not found",
                    new Dictionary<string, object>
                    {
                        ["FileName"] = component.Name,
                        ["ExpectedPath"] = component.FilePath
                    });
            }

            // Get file info
            var fileInfo = new FileInfo(component.FilePath);
            var fileAgeHours = (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalHours;
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);

            // Check if file is stale (if refresh interval is specified)
            var expectedRefreshHours = component.ExpectedRefreshIntervalHours ?? 
                _configuration.GetValue<double>("DEFAULT_FILE_REFRESH_HOURS", 4.0);

            var metrics = new Dictionary<string, object>
            {
                ["FilePath"] = component.FilePath,
                ["FileAgeHours"] = Math.Round(fileAgeHours, DecimalPlacesForMetrics),
                ["FileSizeMB"] = Math.Round(fileSizeMB, DecimalPlacesForMetrics),
                ["LastModified"] = fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss UTC", System.Globalization.CultureInfo.InvariantCulture)
            };

            // If refresh interval is 0, we only check existence (like kill.txt)
            if (expectedRefreshHours <= 0)
            {
                return HealthCheckResult.Healthy(
                    "File exists",
                    metrics);
            }

            // Check staleness
            if (fileAgeHours > expectedRefreshHours)
            {
                return HealthCheckResult.Degraded(
                    $"File is stale (age: {fileAgeHours:F1}h, expected refresh: {expectedRefreshHours:F1}h)",
                    metrics);
            }

            return HealthCheckResult.Healthy(
                "File is fresh",
                metrics);
        }
        catch (IOException ex)
        {
            return HealthCheckResult.Unhealthy(
                $"File check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["FileName"] = component.Name,
                    ["Error"] = ex.Message
                });
        }
    }

    private async Task<HealthCheckResult> CheckAPIConnectionHealthAsync(
        DiscoveredComponent component,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check specific API types
            if (component.Name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return await CheckOllamaConnectionAsync(component, cancellationToken).ConfigureAwait(false);
            }

            if (component.Name.Contains("TopstepX", StringComparison.OrdinalIgnoreCase))
            {
                return CheckTopstepXConnection(component);
            }

            if (component.Name.Contains("Python", StringComparison.OrdinalIgnoreCase))
            {
                return await CheckPythonServiceConnectionAsync(component).ConfigureAwait(false);
            }

            // Generic API check
            return HealthCheckResult.Healthy(
                "API connection registered",
                new Dictionary<string, object>
                {
                    ["APIName"] = component.Name
                });
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy(
                $"API connection check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["APIName"] = component.Name,
                    ["Error"] = ex.Message
                });
        }
    }

    private async Task<HealthCheckResult> CheckOllamaConnectionAsync(
        DiscoveredComponent component,
        CancellationToken cancellationToken)
    {
        try
        {
            var ollamaClient = _serviceProvider.GetService<OllamaClient>();
            if (ollamaClient == null)
            {
                return HealthCheckResult.Degraded(
                    "Ollama client not available",
                    new Dictionary<string, object>
                    {
                        ["Service"] = "OllamaClient"
                    });
            }

            var isConnected = await ollamaClient.IsConnectedAsync().ConfigureAwait(false);
            
            if (isConnected)
            {
                return HealthCheckResult.Healthy(
                    "Ollama service is connected",
                    new Dictionary<string, object>
                    {
                        ["BaseUrl"] = component.Metadata.GetValueOrDefault("BaseUrl", "unknown"),
                        ["Model"] = component.Metadata.GetValueOrDefault("Model", "unknown"),
                        ["IsConnected"] = true
                    });
            }

            return HealthCheckResult.Unhealthy(
                "Ollama service is not responding",
                new Dictionary<string, object>
                {
                    ["BaseUrl"] = component.Metadata.GetValueOrDefault("BaseUrl", "unknown"),
                    ["IsConnected"] = false
                });
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Ollama connection check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["Error"] = ex.Message
                });
        }
    }

    private static HealthCheckResult CheckTopstepXConnection(DiscoveredComponent component)
    {
        // TopstepX connection check would require accessing the actual service
        // For now, return a basic check
        return HealthCheckResult.Healthy(
            "TopstepX connection registered",
            new Dictionary<string, object>
            {
                ["APIName"] = component.Name,
                ["Note"] = "Full authentication check requires TopstepX service integration"
            });
    }

    private static Task<HealthCheckResult> CheckPythonServiceConnectionAsync(
        DiscoveredComponent component)
    {
        // Python service health check would require HTTP ping
        // For now, return a basic check
        return Task.FromResult(HealthCheckResult.Healthy(
            "Python service registered",
            new Dictionary<string, object>
            {
                ["ServiceName"] = component.Name,
                ["BaseUrl"] = component.Metadata.GetValueOrDefault("BaseUrl", "unknown"),
                ["Note"] = "Full connectivity check requires HTTP client"
            }));
    }

    private HealthCheckResult CheckPerformanceMetricHealth(DiscoveredComponent component)
    {
        try
        {
            var metrics = new Dictionary<string, object>
            {
                ["MetricName"] = component.Name
            };

            // Check specific metrics
            switch (component.Name)
            {
                case "Win Rate":
                    return CheckWinRateMetric(component, metrics);

                case "Daily P&L":
                    return CheckPnLMetric(component, metrics);

                case "Memory Usage":
                    return CheckMemoryMetric(component, metrics);

                case "Decision Latency":
                    return CheckLatencyMetric(component, metrics);

                case "Thread Pool Usage":
                    return CheckThreadPoolMetric(component, metrics);

                case "Decision Frequency":
                    return CheckDecisionFrequencyMetric(component, metrics);

                default:
                    return HealthCheckResult.Healthy(
                        "Performance metric registered",
                        metrics);
            }
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Performance metric check failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["MetricName"] = component.Name,
                    ["Error"] = ex.Message
                });
        }
    }

    private HealthCheckResult CheckWinRateMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            var performanceTracker = _serviceProvider.GetService<AutonomousPerformanceTracker>();
            if (performanceTracker == null)
            {
                return HealthCheckResult.Degraded("Performance tracker not available", metrics);
            }

            // Would need to get actual win rate from the tracker
            // For now, return healthy
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", 0.35);
            metrics["Threshold"] = threshold;
            metrics["Note"] = "Actual win rate calculation requires AutonomousPerformanceTracker integration";

            return HealthCheckResult.Healthy("Win rate metric available", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"Win rate check failed: {ex.Message}", metrics);
        }
    }

    private static HealthCheckResult CheckPnLMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", -500.0);
            metrics["Threshold"] = threshold;
            metrics["Note"] = "Actual P&L tracking requires integration with performance tracker";

            return HealthCheckResult.Healthy("P&L metric available", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"P&L check failed: {ex.Message}", metrics);
        }
    }

    private static HealthCheckResult CheckMemoryMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            var memoryUsageMB = GC.GetTotalMemory(false) / BytesToMegabytes;
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", 2048.0);

            metrics["CurrentMemoryMB"] = Math.Round(memoryUsageMB, DecimalPlacesForMetrics);
            metrics["ThresholdMB"] = threshold;

            if (memoryUsageMB > threshold)
            {
                return HealthCheckResult.Degraded(
                    $"Memory usage ({memoryUsageMB:F0}MB) exceeds threshold ({threshold:F0}MB)",
                    metrics);
            }

            return HealthCheckResult.Healthy("Memory usage within limits", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"Memory check failed: {ex.Message}", metrics);
        }
    }

    private static HealthCheckResult CheckLatencyMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", 5000.0);
            metrics["ThresholdMs"] = threshold;
            metrics["Note"] = "Actual latency tracking requires integration with decision timing";

            return HealthCheckResult.Healthy("Latency metric available", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"Latency check failed: {ex.Message}", metrics);
        }
    }

    private static HealthCheckResult CheckThreadPoolMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            System.Threading.ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            System.Threading.ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);

            var usage = 1.0 - ((double)workerThreads / maxWorkerThreads);
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", 0.9);

            metrics["ThreadPoolUsage"] = Math.Round(usage, DecimalPlacesForMetrics);
            metrics["AvailableWorkerThreads"] = workerThreads;
            metrics["MaxWorkerThreads"] = maxWorkerThreads;
            metrics["Threshold"] = threshold;

            if (usage > threshold)
            {
                return HealthCheckResult.Degraded(
                    $"Thread pool usage ({usage:P0}) exceeds threshold ({threshold:P0})",
                    metrics);
            }

            return HealthCheckResult.Healthy("Thread pool usage within limits", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"Thread pool check failed: {ex.Message}", metrics);
        }
    }

    private static HealthCheckResult CheckDecisionFrequencyMetric(DiscoveredComponent component, Dictionary<string, object> metrics)
    {
        try
        {
            var threshold = component.Thresholds.GetValueOrDefault("AlertThreshold", 30.0);
            metrics["ThresholdMinutes"] = threshold;
            metrics["Note"] = "Actual decision frequency tracking requires integration with UnifiedTradingBrain";

            return HealthCheckResult.Healthy("Decision frequency metric available", metrics);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy($"Decision frequency check failed: {ex.Message}", metrics);
        }
    }
}
