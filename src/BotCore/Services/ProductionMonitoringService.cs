using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace BotCore.Services;

/// <summary>
/// Production-grade monitoring and health check service for ML/RL/Cloud trading system
/// Tracks model performance, API health, and system metrics
/// </summary>
public class ProductionMonitoringService : IHealthCheck
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    // Monitoring thresholds and constants
    private const int PeriodicLoggingInterval = 10; // Log every N predictions
    private const double FailureRateWarningThreshold = 0.1; // 10% failure rate
    private const int MinimumCallsForAlert = 10;
    private const int Gen0GarbageCollection = 0;
    private const int Gen1GarbageCollection = 1;
    private const int Gen2GarbageCollection = 2;
    private const int MemoryWarningThresholdMb = 2048; // 2GB memory limit
    
    private readonly ILogger<ProductionMonitoringService> _logger;
    private readonly ProductionTradingConfig _config;
    private readonly Dictionary<string, HealthMetric> _healthMetrics = new();
    private readonly Dictionary<string, PerformanceMetric> _performanceMetrics = new();
    private readonly object _metricsLock = new();

    public ProductionMonitoringService(ILogger<ProductionMonitoringService> logger, IOptions<ProductionTradingConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        _logger = logger;
        _config = config.Value;
        InitializeMetrics();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthChecks = new List<(string Name, bool IsHealthy, string Message)>();

            // Check model health
            var modelHealth = await CheckModelHealthAsync().ConfigureAwait(false);
            healthChecks.Add(("Models", modelHealth.IsHealthy, modelHealth.Message));

            // Check GitHub API connectivity
            var githubHealth = await CheckGitHubConnectivityAsync(cancellationToken).ConfigureAwait(false);
            healthChecks.Add(("GitHub", githubHealth.IsHealthy, githubHealth.Message));

            // Check system resources
            var systemHealth = CheckSystemResourcesHealth();
            healthChecks.Add(("System", systemHealth.IsHealthy, systemHealth.Message));

            // Check trading performance
            var performanceHealth = CheckTradingPerformanceHealth();
            healthChecks.Add(("Performance", performanceHealth.IsHealthy, performanceHealth.Message));

            var overallHealthy = healthChecks.TrueForAll(h => h.IsHealthy);
            var status = overallHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;
            
            var data = healthChecks.ToDictionary(h => h.Name, h => (object)new { IsHealthy = h.IsHealthy, Message = h.Message });
            
            _logger.LogInformation("🏥 [HEALTH] System health check: {Status} - {HealthyCount}/{TotalCount} systems healthy", 
                status, healthChecks.Count(h => h.IsHealthy), healthChecks.Count);

            return new HealthCheckResult(status, description: "ML/RL Trading System Health", data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [HEALTH] Health check failed: {Error}", ex.Message);
            return HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }

    /// <summary>
    /// Track model prediction performance
    /// </summary>
    public void TrackModelPrediction(string modelName, double confidence, bool wasCorrect, TimeSpan predictionTime)
    {
        lock (_metricsLock)
        {
            var key = $"model_{modelName}";
            if (!_performanceMetrics.TryGetValue(key, out var metric))
            {
                metric = new PerformanceMetric(modelName);
                _performanceMetrics[key] = metric;
            }

            metric.TotalPredictions++;
            metric.TotalConfidence += confidence;
            metric.TotalPredictionTime += predictionTime;
            
            if (wasCorrect)
            {
                metric.CorrectPredictions++;
            }

            metric.LastUpdated = DateTime.UtcNow;

            // Log performance statistics periodically
            if (metric.TotalPredictions % PeriodicLoggingInterval == 0)
            {
                var accuracy = (double)metric.CorrectPredictions / metric.TotalPredictions;
                var avgConfidence = metric.TotalConfidence / metric.TotalPredictions;
                var avgTime = metric.TotalPredictionTime.TotalMilliseconds / metric.TotalPredictions;

                _logger.LogInformation("📊 [MONITORING] Model {ModelName} performance: {Accuracy:P1} accuracy, {AvgConfidence:F2} avg confidence, {AvgTime:F1}ms avg time",
                    modelName, accuracy, avgConfidence, avgTime);
            }
        }
    }

    /// <summary>
    /// Track API operation metrics
    /// </summary>
    public void TrackApiOperation(string operationName, TimeSpan duration, bool successful, string? errorMessage = null)
    {
        lock (_metricsLock)
        {
            var key = $"api_{operationName}";
            if (!_healthMetrics.TryGetValue(key, out var metric))
            {
                metric = new HealthMetric(operationName);
                _healthMetrics[key] = metric;
            }

            metric.TotalCalls++;
            metric.TotalDuration += duration;
            
            if (successful)
            {
                metric.SuccessfulCalls++;
            }
            else
            {
                metric.FailedCalls++;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    metric.LastError = errorMessage;
                }
            }

            metric.LastUpdated = DateTime.UtcNow;

            // Alert on high failure rate
            var failureRate = (double)metric.FailedCalls / metric.TotalCalls;
            if (failureRate > FailureRateWarningThreshold && metric.TotalCalls >= MinimumCallsForAlert)
            {
                _logger.LogWarning("⚠️ [MONITORING] High failure rate for {Operation}: {FailureRate:P1} ({Failed}/{Total})",
                    operationName, failureRate, metric.FailedCalls, metric.TotalCalls);
            }
        }
    }

    /// <summary>
    /// Track trading decision metrics
    /// </summary>
    public void TrackTradingDecision(string strategy, decimal confidence, decimal positionSize, bool enhanced)
    {
        lock (_metricsLock)
        {
            var key = $"trading_{strategy}";
            if (!_performanceMetrics.TryGetValue(key, out var metric))
            {
                metric = new PerformanceMetric(strategy);
                _performanceMetrics[key] = metric;
            }

            metric.TotalPredictions++;
            metric.TotalConfidence += (double)confidence;
            metric.LastUpdated = DateTime.UtcNow;

            if (enhanced)
            {
                metric.CorrectPredictions++; // Use as "enhanced decisions" counter
            }

            _logger.LogDebug("📈 [MONITORING] Trading decision: {Strategy} confidence={Confidence:P1}, size={Size}, enhanced={Enhanced}",
                strategy, confidence, positionSize, enhanced);
        }
    }

    /// <summary>
    /// Get comprehensive system metrics
    /// </summary>
    public SystemMetrics GetSystemMetrics()
    {
        lock (_metricsLock)
        {
            var process = Process.GetCurrentProcess();
            
            var metrics = new SystemMetrics
            {
                Timestamp = DateTime.UtcNow,
                MemoryUsageMB = process.WorkingSet64 / 1024 / 1024,
                CpuTimeMs = process.TotalProcessorTime.TotalMilliseconds,
                UptimeHours = (DateTime.UtcNow - process.StartTime).TotalHours,
                ThreadCount = process.Threads.Count
            };
            
            // Populate read-only collections
            foreach (var health in _healthMetrics.Values)
            {
                metrics.HealthMetricsInternal.Add(health);
            }
            
            foreach (var performance in _performanceMetrics.Values)
            {
                metrics.PerformanceMetricsInternal.Add(performance);
            }
            
            metrics.GCCollections[Gen0GarbageCollection] = GC.CollectionCount(Gen0GarbageCollection);
            metrics.GCCollections[Gen1GarbageCollection] = GC.CollectionCount(Gen1GarbageCollection);
            metrics.GCCollections[Gen2GarbageCollection] = GC.CollectionCount(Gen2GarbageCollection);
            
            return metrics;
        }
    }

    /// <summary>
    /// Export metrics in JSON format for external monitoring systems
    /// </summary>
    public string ExportMetricsAsJson()
    {
        var metrics = GetSystemMetrics();
        return JsonSerializer.Serialize(metrics, s_jsonOptions);
    }

    private void InitializeMetrics()
    {
        _logger.LogInformation("📊 [MONITORING] Production monitoring service initialized");
        
        // Initialize basic health metrics
        var systemMetric = new HealthMetric("system")
        {
            LastUpdated = DateTime.UtcNow
        };
        _healthMetrics["system"] = systemMetric;
    }

    private Task<(bool IsHealthy, string Message)> CheckModelHealthAsync()
    {
        try
        {
            lock (_metricsLock)
            {
                var modelMetrics = _performanceMetrics.Where(kvp => kvp.Key.StartsWith("model_", StringComparison.Ordinal)).ToList();
                
                if (modelMetrics.Count == 0)
                {
                    return Task.FromResult((false, "No model metrics available"));
                }

                var unhealthyModels = modelMetrics.Where(kvp =>
                {
                    var metric = kvp.Value;
                    var accuracy = metric.TotalPredictions > 0 ? (double)metric.CorrectPredictions / metric.TotalPredictions : 0;
                    return accuracy < _config.Performance.AccuracyThreshold && metric.TotalPredictions >= _config.Performance.MinTradesForEvaluation;
                }).ToList();

                if (unhealthyModels.Count > 0)
                {
                    var unhealthyNames = string.Join(", ", unhealthyModels.Select(kvp => kvp.Value.Name));
                    return Task.FromResult((false, $"Models below threshold: {unhealthyNames}"));
                }

                return Task.FromResult((true, $"{modelMetrics.Count} models healthy"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Model health check failed: {ex.Message}"));
        }
    }

    // GitHub API used as external connectivity health check endpoint
    private const string GitHubHealthCheckUrl = "https://api.github.com/";

    private static async Task<(bool IsHealthy, string Message)> CheckGitHubConnectivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "TradingBot-HealthCheck");
            
            var response = await httpClient.GetAsync(new Uri(GitHubHealthCheckUrl), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode 
                ? (true, "GitHub API accessible") 
                : (false, $"GitHub API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"GitHub connectivity failed: {ex.Message}");
        }
    }

    private static (bool IsHealthy, string Message) CheckSystemResourcesHealth()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / 1024 / 1024;
            
            // Alert if memory usage is excessive
            if (memoryMB > MemoryWarningThresholdMb) // 2GB threshold
            {
                return (false, $"High memory usage: {memoryMB}MB");
            }

            // Check if process is responsive
            if (process.Responding)
            {
                return (true, $"System healthy - {memoryMB}MB memory");
            }
            else
            {
                return (false, "Process not responding");
            }
        }
        catch (Exception ex)
        {
            return (false, $"System health check failed: {ex.Message}");
        }
    }

    private (bool IsHealthy, string Message) CheckTradingPerformanceHealth()
    {
        try
        {
            lock (_metricsLock)
            {
                var tradingMetrics = _performanceMetrics.Where(kvp => kvp.Key.StartsWith("trading_", StringComparison.Ordinal)).ToList();
                
                if (tradingMetrics.Count == 0)
                {
                    return (true, "No trading activity yet");
                }

                var recentMetrics = tradingMetrics.Where(kvp => 
                    DateTime.UtcNow - kvp.Value.LastUpdated < _config.Performance.EvaluationWindow).ToList();

                if (recentMetrics.Count == 0)
                {
                    return (false, "No recent trading activity");
                }

                var totalDecisions = recentMetrics.Sum(kvp => kvp.Value.TotalPredictions);
                var enhancedDecisions = recentMetrics.Sum(kvp => kvp.Value.CorrectPredictions);
                var enhancementRate = totalDecisions > 0 ? (double)enhancedDecisions / totalDecisions : 0;

                return (true, $"{totalDecisions} decisions, {enhancementRate:P1} enhanced");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Trading performance check failed: {ex.Message}");
        }
    }
}

#region Data Models

public class HealthMetric
{
    public string Name { get; set; }
    public int TotalCalls { get; set; }
    public int SuccessfulCalls { get; set; }
    public int FailedCalls { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? LastError { get; set; }

    public HealthMetric(string name)
    {
        Name = name;
        LastUpdated = DateTime.UtcNow;
    }

    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;
    public double AvgDurationMs => TotalCalls > 0 ? TotalDuration.TotalMilliseconds / TotalCalls : 0;
}

public class PerformanceMetric
{
    public string Name { get; set; }
    public int TotalPredictions { get; set; }
    public int CorrectPredictions { get; set; }
    public double TotalConfidence { get; set; }
    public TimeSpan TotalPredictionTime { get; set; }
    public DateTime LastUpdated { get; set; }

    public PerformanceMetric(string name)
    {
        Name = name;
        LastUpdated = DateTime.UtcNow;
    }

    public double Accuracy => TotalPredictions > 0 ? (double)CorrectPredictions / TotalPredictions : 0;
    public double AvgConfidence => TotalPredictions > 0 ? TotalConfidence / TotalPredictions : 0;
    public double AvgPredictionTimeMs => TotalPredictions > 0 ? TotalPredictionTime.TotalMilliseconds / TotalPredictions : 0;
}

public class SystemMetrics
{
    public DateTime Timestamp { get; set; }
    public long MemoryUsageMB { get; set; }
    private readonly List<HealthMetric> _healthMetrics = new();
    private readonly List<PerformanceMetric> _performanceMetrics = new();
    
    public double CpuTimeMs { get; set; }
    public double UptimeHours { get; set; }
    public int ThreadCount { get; set; }
    public Dictionary<int, long> GCCollections { get; } = new();
    public IReadOnlyList<HealthMetric> HealthMetrics => _healthMetrics;
    internal List<HealthMetric> HealthMetricsInternal => _healthMetrics;
    public IReadOnlyList<PerformanceMetric> PerformanceMetrics => _performanceMetrics;
    internal List<PerformanceMetric> PerformanceMetricsInternal => _performanceMetrics;
}

#endregion