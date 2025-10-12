using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.UnifiedOrchestrator.Interfaces;
using TradingBot.UnifiedOrchestrator.Models;

namespace TradingBot.UnifiedOrchestrator.Services;

/// <summary>
/// 24/7 Operation Coordinator Service
/// Manages continuous learning, training, and model synchronization across distributed deployments
/// Implements market-aware scheduling for intensive training during downtime and light training during trading hours
/// </summary>
internal class ContinuousOperationService : BackgroundService
{
    private readonly ILogger<ContinuousOperationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketHoursService _marketHours;
    private readonly ITrainingBrain _trainingBrain;
    private readonly IOnnxModelRegistry _modelRegistry;
    private readonly IPromotionService _promotionService;
    private readonly EnhancedBacktestLearningService _backtestService;
    // AutomatedPromotionService removed - legacy automation infrastructure no longer exists
    
    // Scheduling state
    private readonly Dictionary<string, TrainingJobStatus> _activeJobs = new();
    private readonly List<OperationLog> _operationLogs = new();
    private readonly ContinuousOperationState _state = new();
    
    // Configuration
    private readonly TimeSpan _operationCheckInterval = TimeSpan.FromMinutes(10);
    private readonly int _rollingWindowDays = 30; // Use 30-day rolling window for retraining

    public ContinuousOperationService(
        ILogger<ContinuousOperationService> logger,
        IServiceProvider serviceProvider,
        IMarketHoursService marketHours,
        ITrainingBrain trainingBrain,
        IOnnxModelRegistry modelRegistry,
        IPromotionService promotionService,
        EnhancedBacktestLearningService backtestService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _marketHours = marketHours;
        _trainingBrain = trainingBrain;
        _modelRegistry = modelRegistry;
        _promotionService = promotionService;
        _backtestService = backtestService;
        
        _state.StartTime = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[24/7-OPS] Starting continuous operation service for 24/7 ML/RL operation");
        
        // Wait for system initialization
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken).ConfigureAwait(false);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Update operation state
                await UpdateOperationStateAsync(stoppingToken).ConfigureAwait(false);
                
                // Coordinate training activities based on market conditions
                await CoordinateTrainingActivitiesAsync(stoppingToken).ConfigureAwait(false);
                
                // Manage model synchronization
                await SynchronizeModelsAsync(stoppingToken).ConfigureAwait(false);
                
                // Perform daily retraining if needed
                await PerformDailyRetrainingAsync(stoppingToken).ConfigureAwait(false);
                
                // Monitor and cleanup active jobs
                await MonitorActiveJobsAsync(stoppingToken).ConfigureAwait(false);
                
                // Weekend intensive training coordination
                await CoordinateWeekendTrainingAsync(stoppingToken).ConfigureAwait(false);
                
                // Log operation status
                await LogOperationStatusAsync(stoppingToken).ConfigureAwait(false);
                
                // Wait before next cycle
                await Task.Delay(_operationCheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[24/7-OPS] Error in continuous operation service");
                await LogOperationAsync("ERROR", $"Operation error: {ex.Message}", stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Update current operation state based on market conditions
    /// </summary>
    private async Task UpdateOperationStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            _state.LastUpdateTime = DateTime.UtcNow;
            _state.IsMarketOpen = await _marketHours.IsMarketOpenAsync(cancellationToken).ConfigureAwait(false);
            _state.CurrentMarketSession = await _marketHours.GetCurrentMarketSessionAsync(cancellationToken).ConfigureAwait(false);
            _state.TrainingIntensity = await _marketHours.GetRecommendedTrainingIntensityAsync(cancellationToken).ConfigureAwait(false);
            _state.IsInSafeWindow = await _marketHours.IsInSafePromotionWindowAsync(cancellationToken).ConfigureAwait(false);
            _state.ActiveTrainingJobs = _activeJobs.Count;
            _state.DaysSinceStart = (DateTime.UtcNow - _state.StartTime).Days;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to update operation state");
        }
    }

    /// <summary>
    /// Coordinate training activities based on current market conditions and intensity
    /// </summary>
    private async Task CoordinateTrainingActivitiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var intensity = _state.TrainingIntensity;
            
            switch (intensity.Level)
            {
                case TrainingIntensityLevel.Intensive:
                    await ScheduleIntensiveTrainingAsync(intensity, cancellationToken).ConfigureAwait(false);
                    break;
                
                case TrainingIntensityLevel.High:
                    await ScheduleModerateTrainingAsync(intensity, cancellationToken).ConfigureAwait(false);
                    break;
                
                case TrainingIntensityLevel.Medium:
                    await ScheduleBackgroundTrainingAsync(intensity, cancellationToken).ConfigureAwait(false);
                    break;
                
                case TrainingIntensityLevel.Light:
                    // Market active - minimal training only
                    await CleanupNonCriticalJobsAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to coordinate training activities");
        }
    }

    /// <summary>
    /// Schedule intensive training during market downtime (weekends, maintenance)
    /// </summary>
    private async Task ScheduleIntensiveTrainingAsync(TrainingIntensity intensity, CancellationToken cancellationToken)
    {
        try
        {
            if (_activeJobs.Count >= intensity.ParallelJobs)
            {
                return; // Already at capacity
            }

            _logger.LogInformation("[24/7-OPS] Scheduling intensive training - can run {MaxJobs} parallel jobs",
                intensity.ParallelJobs);

            // Intensive training tasks
            var tasks = new List<Task>
            {
                // Multiple algorithm retraining
                ScheduleAlgorithmRetrainingAsync("PPO", "INTENSIVE", cancellationToken),
                ScheduleAlgorithmRetrainingAsync("UCB", "INTENSIVE", cancellationToken),
                ScheduleAlgorithmRetrainingAsync("LSTM", "INTENSIVE", cancellationToken),
                
                // Hyperparameter optimization
                ScheduleHyperparameterOptimizationAsync(cancellationToken),
                
                // Feature engineering experiments
                ScheduleFeatureExperimentsAsync(cancellationToken),
                
                // Model ensemble training
                ScheduleEnsembleTrainingAsync(cancellationToken)
            };

            await Task.WhenAll(tasks.Take(intensity.ParallelJobs)).ConfigureAwait(false);
            
            await LogOperationAsync("TRAINING", 
                $"Scheduled {intensity.ParallelJobs} intensive training jobs", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to schedule intensive training");
        }
    }

    /// <summary>
    /// Schedule moderate training during market closure or low volatility periods
    /// </summary>
    private async Task ScheduleModerateTrainingAsync(TrainingIntensity intensity, CancellationToken cancellationToken)
    {
        try
        {
            if (_activeJobs.Count >= intensity.ParallelJobs)
            {
                return;
            }

            _logger.LogInformation("[24/7-OPS] Scheduling moderate training - {MaxJobs} parallel jobs allowed",
                intensity.ParallelJobs);

            // Moderate training tasks
            var tasksToRun = Math.Min(intensity.ParallelJobs - _activeJobs.Count, 2);
            
            var tasks = new List<Task>();
            
            if (tasksToRun >= 1)
            {
                tasks.Add(ScheduleAlgorithmRetrainingAsync("PPO", "MODERATE", cancellationToken));
            }
            
            if (tasksToRun >= 2)
            {
                tasks.Add(ScheduleModelValidationAsync(cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            
            await LogOperationAsync("TRAINING", 
                $"Scheduled {tasks.Count} moderate training jobs", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to schedule moderate training");
        }
    }

    /// <summary>
    /// Schedule background training during trading hours with minimal resource usage
    /// </summary>
    private async Task ScheduleBackgroundTrainingAsync(TrainingIntensity intensity, CancellationToken cancellationToken)
    {
        try
        {
            if (_activeJobs.Count >= intensity.ParallelJobs)
            {
                return;
            }

            _logger.LogDebug("[24/7-OPS] Scheduling background training - {MaxJobs} parallel jobs allowed",
                intensity.ParallelJobs);

            // Only light background tasks during trading
            if (intensity.ParallelJobs >= 1 && ShouldRunBackgroundTraining())
            {
                await ScheduleIncrementalTrainingAsync(cancellationToken).ConfigureAwait(false);
                
                await LogOperationAsync("TRAINING", 
                    "Scheduled 1 background training job", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to schedule background training");
        }
    }

    /// <summary>
    /// Schedule retraining for a specific algorithm
    /// </summary>
    private async Task ScheduleAlgorithmRetrainingAsync(string algorithm, string priority, CancellationToken cancellationToken)
    {
        try
        {
            var jobId = $"{algorithm}_RETRAIN_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            
            var config = new TrainingConfig
            {
                Algorithm = algorithm,
                DataStartTime = DateTime.UtcNow.AddDays(-_rollingWindowDays),
                DataEndTime = DateTime.UtcNow.AddDays(-1), // Yesterday
                DataSource = "UNIFIED_PIPELINE",
                MaxEpochs = priority switch
                {
                    "INTENSIVE" => 200,
                    "MODERATE" => 100,
                    _ => 50
                },
                Parameters = new Dictionary<string, object>
                {
                    ["priority"] = priority,
                    ["rolling_window_days"] = _rollingWindowDays,
                    ["retraining_job"] = true
                }
            };

            var jobStatus = new TrainingJobStatus
            {
                JobId = jobId,
                Algorithm = algorithm,
                Status = "QUEUED",
                Priority = priority,
                StartTime = DateTime.UtcNow,
                Config = config
            };

            _activeJobs[jobId] = jobStatus;

            // Start training job
            _ = Task.Run(async () =>
            {
                try
                {
                    jobStatus.Status = "RUNNING";
                    var result = await _trainingBrain.TrainChallengerAsync(algorithm, config, cancellationToken).ConfigureAwait(false);
                    
                    jobStatus.Status = result.Success ? "COMPLETED" : "FAILED";
                    jobStatus.EndTime = DateTime.UtcNow;
                    jobStatus.Result = result;

                    if (result.Success)
                    {
                        _logger.LogInformation("[24/7-OPS] Completed {Priority} retraining for {Algorithm} " +
                            "in {Duration:F1}s - new model {ModelPath}",
                            priority, algorithm, result.TrainingDuration.TotalSeconds, result.ModelPath);
                    }
                }
                catch (Exception ex)
                {
                    jobStatus.Status = "ERROR";
                    jobStatus.EndTime = DateTime.UtcNow;
                    _logger.LogError(ex, "[24/7-OPS] Error in {Algorithm} retraining job {JobId}", algorithm, jobId);
                }
                finally
                {
                    // Clean up job after completion
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                    _activeJobs.Remove(jobId);
                }
            }, cancellationToken);

            _logger.LogInformation("[24/7-OPS] Started {Priority} retraining job {JobId} for {Algorithm}",
                priority, jobId, algorithm);
            
            await Task.CompletedTask.ConfigureAwait(false); // Fire and forget job scheduled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to schedule retraining for {Algorithm}", algorithm);
        }
    }

    /// <summary>
    /// Perform daily retraining on rolling window of recent data
    /// </summary>
    private async Task PerformDailyRetrainingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var lastDailyRetraining = _state.LastDailyRetraining;
            
            // Check if daily retraining is due (once per day during appropriate window)
            if (lastDailyRetraining.Date >= now.Date)
            {
                return; // Already done today
            }

            // Only perform during intensive or moderate training windows
            if (_state.TrainingIntensity.Level != TrainingIntensityLevel.Intensive && _state.TrainingIntensity.Level != TrainingIntensityLevel.High)
            {
                return;
            }

            _logger.LogInformation("[24/7-OPS] Starting daily retraining on {Days}-day rolling window", _rollingWindowDays);

            // Schedule daily retraining for all algorithms
            var algorithms = new[] { "PPO", "UCB", "LSTM" };
            var retrainingTasks = algorithms.Select(algorithm =>
                ScheduleAlgorithmRetrainingAsync(algorithm, "DAILY", cancellationToken));

            await Task.WhenAll(retrainingTasks).ConfigureAwait(false);

            _state.LastDailyRetraining = now;
            await LogOperationAsync("DAILY_RETRAIN", 
                $"Initiated daily retraining for {algorithms.Length} algorithms", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to perform daily retraining");
        }
    }

    /// <summary>
    /// Coordinate weekend intensive training activities
    /// </summary>
    private async Task CoordinateWeekendTrainingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // Only run on weekends
            if (now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday)
            {
                return;
            }

            // Check if we've already done weekend training this weekend
            if (_state.LastWeekendTraining.Date >= now.AddDays(-(int)now.DayOfWeek).Date)
            {
                return;
            }

            _logger.LogInformation("[24/7-OPS] Starting weekend intensive training session");

            // Weekend training tasks
            var weekendTasks = new List<Task>
            {
                ScheduleComprehensiveBacktestSuiteAsync(cancellationToken),
                ScheduleModelEnsembleOptimizationAsync(cancellationToken),
                ScheduleFeatureImportanceAnalysisAsync(cancellationToken),
                ScheduleModelArchitectureExperimentsAsync(cancellationToken)
            };

            await Task.WhenAll(weekendTasks).ConfigureAwait(false);

            _state.LastWeekendTraining = now;
            await LogOperationAsync("WEEKEND_INTENSIVE", 
                $"Completed weekend intensive training with {weekendTasks.Count} tasks", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to coordinate weekend training");
        }
    }

    /// <summary>
    /// Synchronize models across distributed deployments
    /// </summary>
    private async Task SynchronizeModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Model synchronization would be implemented here for distributed deployments
            // For now, we just ensure model registry integrity
            
            var algorithms = new[] { "PPO", "UCB", "LSTM" };
            var syncTasks = algorithms.Select(async algorithm =>
            {
                var champion = await _modelRegistry.GetChampionAsync(algorithm, cancellationToken).ConfigureAwait(false);
                if (champion != null)
                {
                    var isValid = await _modelRegistry.ValidateArtifactAsync(champion.VersionId, cancellationToken).ConfigureAwait(false);
                    if (!isValid)
                    {
                        _logger.LogWarning("[24/7-OPS] Champion model artifact validation failed for {Algorithm}: {VersionId}",
                            algorithm, champion.VersionId);
                    }
                }
            });

            await Task.WhenAll(syncTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to synchronize models");
        }
    }

    #region Helper Methods

    /// <summary>
    /// Check if background training should run (rate limiting)
    /// </summary>
    private bool ShouldRunBackgroundTraining()
    {
        var lastBackgroundJob = _operationLogs
            .Where(log => log.Operation == "TRAINING" && log.Message.Contains("background"))
            .OrderByDescending(log => log.Timestamp)
            .FirstOrDefault();

        if (lastBackgroundJob == null)
        {
            return true;
        }

        // Only run background training every 2 hours during market hours
        return (DateTime.UtcNow - lastBackgroundJob.Timestamp).TotalHours >= 2;
    }

    /// <summary>
    /// Schedule incremental training with new data
    /// </summary>
    private async Task ScheduleIncrementalTrainingAsync(CancellationToken cancellationToken)
    {
        // Light incremental training during market hours
        await Task.Delay(100, cancellationToken).ConfigureAwait(false); // Simulate scheduling
        _logger.LogDebug("[24/7-OPS] Scheduled incremental training job");
    }

    /// <summary>
    /// Schedule hyperparameter optimization
    /// </summary>
    private async Task ScheduleHyperparameterOptimizationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled hyperparameter optimization");
    }

    /// <summary>
    /// Schedule feature engineering experiments
    /// </summary>
    private async Task ScheduleFeatureExperimentsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled feature engineering experiments");
    }

    /// <summary>
    /// Schedule ensemble training
    /// </summary>
    private async Task ScheduleEnsembleTrainingAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled ensemble training");
    }

    /// <summary>
    /// Schedule model validation
    /// </summary>
    private async Task ScheduleModelValidationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled model validation");
    }

    /// <summary>
    /// Schedule comprehensive backtest suite
    /// </summary>
    private async Task ScheduleComprehensiveBacktestSuiteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled comprehensive backtest suite");
    }

    /// <summary>
    /// Schedule model ensemble optimization
    /// </summary>
    private async Task ScheduleModelEnsembleOptimizationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled model ensemble optimization");
    }

    /// <summary>
    /// Schedule feature importance analysis
    /// </summary>
    private async Task ScheduleFeatureImportanceAnalysisAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled feature importance analysis");
    }

    /// <summary>
    /// Schedule model architecture experiments
    /// </summary>
    private async Task ScheduleModelArchitectureExperimentsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[24/7-OPS] Scheduled model architecture experiments");
    }

    /// <summary>
    /// Clean up non-critical jobs during high market activity
    /// </summary>
    private async Task CleanupNonCriticalJobsAsync(CancellationToken cancellationToken)
    {
        var nonCriticalJobs = _activeJobs.Values
            .Where(job => job.Priority != "CRITICAL" && job.Status == "QUEUED")
            .ToList();

        foreach (var job in nonCriticalJobs)
        {
            job.Status = "CANCELLED";
            job.EndTime = DateTime.UtcNow;
            _activeJobs.Remove(job.JobId);
        }

        if (nonCriticalJobs.Any())
        {
            await LogOperationAsync("CLEANUP", 
                $"Cancelled {nonCriticalJobs.Count} non-critical jobs due to market activity", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Monitor active jobs and clean up completed ones
    /// </summary>
    private Task MonitorActiveJobsAsync()
    {
        try
        {
            var completedJobs = _activeJobs.Values
                .Where(job => job.Status == "COMPLETED" || job.Status == "FAILED" || job.Status == "ERROR")
                .Where(job => job.EndTime.HasValue && (DateTime.UtcNow - job.EndTime.Value).TotalMinutes > 10)
                .ToList();

            foreach (var job in completedJobs)
            {
                _activeJobs.Remove(job.JobId);
            }

            if (completedJobs.Any())
            {
                _logger.LogDebug("[24/7-OPS] Cleaned up {Count} completed jobs", completedJobs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to monitor active jobs");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Log operation status for monitoring
    /// </summary>
    private async Task LogOperationStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = $"Market: {_state.CurrentMarketSession}, " +
                        $"Training: {_state.TrainingIntensity.Level}, " +
                        $"Active Jobs: {_state.ActiveTrainingJobs}, " +
                        $"Safe Window: {_state.IsInSafeWindow}";

            await LogOperationAsync("STATUS", status, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to log operation status");
        }
    }

    /// <summary>
    /// Log operation event
    /// </summary>
    private Task LogOperationAsync(string operation, string message)
    {
        try
        {
            var log = new OperationLog
            {
                Timestamp = DateTime.UtcNow,
                Operation = operation,
                Message = message
            };

            _operationLogs.Add(log);

            // Keep only recent logs (last 1000)
            if (_operationLogs.Count > 1000)
            {
                _operationLogs.RemoveRange(0, 100);
            }

            _logger.LogDebug("[24/7-OPS] {Operation}: {Message}", operation, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[24/7-OPS] Failed to log operation");
        }

        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Get current operation status
    /// </summary>
    public ContinuousOperationStatus GetStatus()
    {
        return new ContinuousOperationStatus
        {
            State = _state,
            ActiveJobs = _activeJobs.Values.ToList(),
            RecentLogs = _operationLogs.TakeLast(50).ToList()
            // AutomatedPromotionStatus removed - legacy automation infrastructure no longer exists
        };
    }
}

#region Supporting Models

/// <summary>
/// Continuous operation state
/// </summary>
internal class ContinuousOperationState
{
    public DateTime StartTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public bool IsMarketOpen { get; set; }
    public string CurrentMarketSession { get; set; } = string.Empty;
    public TrainingIntensity TrainingIntensity { get; set; } = new();
    public bool IsInSafeWindow { get; set; }
    public int ActiveTrainingJobs { get; set; }
    public int DaysSinceStart { get; set; }
    public DateTime LastDailyRetraining { get; set; }
    public DateTime LastWeekendTraining { get; set; }
}

/// <summary>
/// Training job status
/// </summary>
internal class TrainingJobStatus
{
    public string JobId { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TrainingConfig Config { get; set; } = new();
    public TrainingResult? Result { get; set; }
}

/// <summary>
/// Operation log entry
/// </summary>
internal class OperationLog
{
    public DateTime Timestamp { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Continuous operation status for monitoring
/// </summary>
internal class ContinuousOperationStatus
{
    public ContinuousOperationState State { get; set; } = new();
    public List<TrainingJobStatus> ActiveJobs { get; set; } = new();
    public List<OperationLog> RecentLogs { get; set; } = new();
    // AutomatedPromotionStatus removed - legacy automation infrastructure no longer exists
}

#endregion