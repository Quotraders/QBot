using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Feature Drift Monitor Service for data hygiene and drift defenses
    /// Monitors KS/PSI statistics on raw features and model inputs with fail-closed behavior
    /// Implements missing-key kill switches and comprehensive audit logging
    /// </summary>
    public sealed class FeatureDriftMonitorService
    {
        private readonly ILogger<FeatureDriftMonitorService> _logger;
        private readonly DriftMonitorConfiguration _config;
        private readonly ConcurrentDictionary<string, FeatureDriftState> _featureDriftStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastDriftChecks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _driftLock = new();

        // Drift detection counters
        private long _driftChecksPerformed;
        private long _driftViolationsDetected;
        private long _killSwitchTriggers;

        public FeatureDriftMonitorService(
            ILogger<FeatureDriftMonitorService> logger,
            IOptions<DriftMonitorConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Check for feature drift and return fail-closed decision
        /// Returns true if trading should continue, false to trigger HOLD
        /// </summary>
        public async Task<FeatureDriftResult> CheckFeatureDriftAsync(
            Dictionary<string, double> currentFeatures,
            CancellationToken cancellationToken = default)
        {
            if (currentFeatures == null)
                throw new ArgumentNullException(nameof(currentFeatures));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                lock (_driftLock)
                {
                    Interlocked.Increment(ref _driftChecksPerformed);

                    var result = new FeatureDriftResult
                    {
                        AllowTrading = true,
                        DriftViolations = new List<string>(),
                        MissingFeatures = new List<string>(),
                        CheckTimestamp = DateTime.UtcNow
                    };

                    // Check for missing critical features (kill switch)
                    CheckMissingFeatures(currentFeatures, result);

                    // Check drift statistics for each feature
                    CheckFeatureDriftStatistics(currentFeatures, result);

                    // Apply fail-closed logic
                    if (result.DriftViolations.Count > _config.MaxDriftViolations || 
                        result.MissingFeatures.Count > 0)
                    {
                        result.AllowTrading = false;
                        Interlocked.Increment(ref _killSwitchTriggers);

                        _logger.LogError("[FEATURE-DRIFT] [AUDIT-VIOLATION] KILL SWITCH TRIGGERED - Drift violations: {DriftCount}, Missing features: {MissingCount} - FAIL-CLOSED + TELEMETRY", 
                            result.DriftViolations.Count, result.MissingFeatures.Count);
                    }

                    if (result.DriftViolations.Count > 0)
                    {
                        Interlocked.Increment(ref _driftViolationsDetected);
                    }

                    _logger.LogTrace("[FEATURE-DRIFT] Drift check completed: Allow={Allow}, Violations={Violations}, Missing={Missing}", 
                        result.AllowTrading, result.DriftViolations.Count, result.MissingFeatures.Count);

                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FEATURE-DRIFT] [AUDIT-VIOLATION] Drift check failed - FAIL-CLOSED + TELEMETRY");
                
                // Fail-closed: return do not allow trading on drift check failure
                return new FeatureDriftResult
                {
                    AllowTrading = false,
                    DriftViolations = new List<string> { "DRIFT_CHECK_FAILURE" },
                    MissingFeatures = new List<string>(),
                    CheckTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Update baseline statistics for a feature
        /// Called during model training or initialization to establish baseline
        /// </summary>
        public async Task UpdateFeatureBaselineAsync(
            string featureName, 
            IEnumerable<double> baselineValues,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                throw new ArgumentException("[FEATURE-DRIFT] Feature name cannot be null or empty", nameof(featureName));
            if (baselineValues == null)
                throw new ArgumentNullException(nameof(baselineValues));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                var values = baselineValues.ToList();
                if (values.Count < _config.MinBaselineDataPoints)
                {
                    throw new ArgumentException($"[FEATURE-DRIFT] Insufficient baseline data points: {values.Count} < {_config.MinBaselineDataPoints}");
                }

                var statistics = CalculateFeatureStatistics(values);
                
                var driftState = new FeatureDriftState
                {
                    FeatureName = featureName,
                    BaselineStatistics = statistics,
                    RecentValues = new List<double>(),
                    LastDriftCheck = DateTime.UtcNow,
                    DriftScore = 0.0,
                    IsBaselineSet = true
                };

                _featureDriftStates.AddOrUpdate(featureName, driftState, (key, existing) =>
                {
                    existing.BaselineStatistics = statistics;
                    existing.IsBaselineSet = true;
                    existing.LastDriftCheck = DateTime.UtcNow;
                    return existing;
                });

                _logger.LogInformation("[FEATURE-DRIFT] Updated baseline for feature {FeatureName}: Mean={Mean:F4}, StdDev={StdDev:F4}, Count={Count}", 
                    featureName, statistics.Mean, statistics.StandardDeviation, values.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FEATURE-DRIFT] [AUDIT-VIOLATION] Failed to update baseline for feature {FeatureName} - FAIL-CLOSED + TELEMETRY", 
                    featureName);
                throw;
            }
        }

        /// <summary>
        /// Add feature value to drift monitoring
        /// Called on each inference to track feature evolution
        /// </summary>
        public async Task AddFeatureValueAsync(string featureName, double value, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                throw new ArgumentException("[FEATURE-DRIFT] Feature name cannot be null or empty", nameof(featureName));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                _featureDriftStates.AddOrUpdate(featureName,
                    new FeatureDriftState
                    {
                        FeatureName = featureName,
                        RecentValues = new List<double> { value },
                        LastUpdate = DateTime.UtcNow
                    },
                    (key, existing) =>
                    {
                        existing.RecentValues.Add(value);
                        existing.LastUpdate = DateTime.UtcNow;

                        // Keep only recent values within window
                        var cutoffTime = DateTime.UtcNow.AddMinutes(-_config.DriftWindowMinutes);
                        existing.RecentValues = existing.RecentValues.TakeLast(_config.MaxRecentValues).ToList();

                        return existing;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FEATURE-DRIFT] Failed to add feature value for {FeatureName}", featureName);
            }
        }

        private void CheckMissingFeatures(Dictionary<string, double> currentFeatures, FeatureDriftResult result)
        {
            foreach (var criticalFeature in _config.CriticalFeatures)
            {
                if (!currentFeatures.ContainsKey(criticalFeature))
                {
                    result.MissingFeatures.Add(criticalFeature);
                    _logger.LogError("[FEATURE-DRIFT] [AUDIT-VIOLATION] Missing critical feature: {FeatureName} - KILL SWITCH TRIGGER", 
                        criticalFeature);
                }
            }
        }

        private void CheckFeatureDriftStatistics(Dictionary<string, double> currentFeatures, FeatureDriftResult result)
        {
            foreach (var feature in currentFeatures)
            {
                if (_featureDriftStates.TryGetValue(feature.Key, out var driftState) && driftState.IsBaselineSet)
                {
                    // Add current value to recent values
                    AddFeatureValueAsync(feature.Key, feature.Value, CancellationToken.None).Wait();

                    // Check if enough recent data for drift calculation
                    if (driftState.RecentValues.Count >= _config.MinDriftDataPoints)
                    {
                        var currentStats = CalculateFeatureStatistics(driftState.RecentValues);
                        
                        // Calculate KS statistic (simplified)
                        var ksStatistic = CalculateKSStatistic(driftState.BaselineStatistics, currentStats);
                        
                        // Calculate PSI (Population Stability Index)
                        var psiStatistic = CalculatePSIStatistic(driftState.BaselineStatistics, currentStats);

                        driftState.DriftScore = Math.Max(ksStatistic, psiStatistic);
                        driftState.LastDriftCheck = DateTime.UtcNow;

                        // Check drift thresholds
                        if (ksStatistic > _config.KSThreshold || psiStatistic > _config.PSIThreshold)
                        {
                            result.DriftViolations.Add(feature.Key);
                            
                            _logger.LogWarning("[FEATURE-DRIFT] [AUDIT-VIOLATION] Drift detected for {FeatureName}: KS={KS:F4}, PSI={PSI:F4} - DRIFT VIOLATION", 
                                feature.Key, ksStatistic, psiStatistic);
                        }
                    }
                }
            }
        }

        private FeatureStatistics CalculateFeatureStatistics(List<double> values)
        {
            if (values.Count == 0)
                return new FeatureStatistics();

            var mean = values.Average();
            var variance = values.Sum(x => Math.Pow(x - mean, 2)) / values.Count;
            var stdDev = Math.Sqrt(variance);

            var sortedValues = values.OrderBy(x => x).ToList();
            var median = sortedValues.Count % 2 == 0
                ? (sortedValues[sortedValues.Count / 2 - 1] + sortedValues[sortedValues.Count / 2]) / 2.0
                : sortedValues[sortedValues.Count / 2];

            return new FeatureStatistics
            {
                Mean = mean,
                StandardDeviation = stdDev,
                Median = median,
                Min = values.Min(),
                Max = values.Max(),
                Count = values.Count
            };
        }

        private double CalculateKSStatistic(FeatureStatistics baseline, FeatureStatistics current)
        {
            // Simplified KS statistic based on mean and standard deviation differences
            var meanDiff = Math.Abs(current.Mean - baseline.Mean);
            var stdDevDiff = Math.Abs(current.StandardDeviation - baseline.StandardDeviation);
            
            // Normalize by baseline standard deviation
            var normalizedMeanDiff = baseline.StandardDeviation > 0 ? meanDiff / baseline.StandardDeviation : 0;
            var normalizedStdDevDiff = baseline.StandardDeviation > 0 ? stdDevDiff / baseline.StandardDeviation : 0;
            
            return Math.Max(normalizedMeanDiff, normalizedStdDevDiff);
        }

        private double CalculatePSIStatistic(FeatureStatistics baseline, FeatureStatistics current)
        {
            // Simplified PSI calculation based on distribution changes
            var meanShift = Math.Abs(current.Mean - baseline.Mean);
            var scaleChange = baseline.StandardDeviation > 0 ? 
                Math.Abs(Math.Log(current.StandardDeviation / baseline.StandardDeviation)) : 0;
            
            return meanShift + scaleChange;
        }

        /// <summary>
        /// Get drift monitoring statistics for health monitoring
        /// </summary>
        public async Task<DriftMonitoringStats> GetMonitoringStatsAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            return new DriftMonitoringStats
            {
                TotalDriftChecks = _driftChecksPerformed,
                TotalDriftViolations = _driftViolationsDetected,
                TotalKillSwitchTriggers = _killSwitchTriggers,
                MonitoredFeatureCount = _featureDriftStates.Count,
                LastCheckTime = _lastDriftChecks.Values.DefaultIfEmpty(DateTime.MinValue).Max()
            };
        }
    }

    /// <summary>
    /// Configuration for drift monitoring behavior - NO HARDCODED DEFAULTS (fail-closed requirement)
    /// </summary>
    public sealed class DriftMonitorConfiguration
    {
        public double KSThreshold { get; set; }
        public double PSIThreshold { get; set; }
        public int MaxDriftViolations { get; set; }
        public int MinBaselineDataPoints { get; set; }
        public int MinDriftDataPoints { get; set; }
        public int MaxRecentValues { get; set; }
        public int DriftWindowMinutes { get; set; }
        public List<string> CriticalFeatures { get; set; } = new();

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (KSThreshold <= 0 || PSIThreshold <= 0)
                throw new InvalidOperationException("[FEATURE-DRIFT] [AUDIT-VIOLATION] Threshold values must be positive - FAIL-CLOSED");
            if (MaxDriftViolations <= 0 || MinBaselineDataPoints <= 0 || MinDriftDataPoints <= 0)
                throw new InvalidOperationException("[FEATURE-DRIFT] [AUDIT-VIOLATION] Count values must be positive - FAIL-CLOSED");
            if (MaxRecentValues <= 0 || DriftWindowMinutes <= 0)
                throw new InvalidOperationException("[FEATURE-DRIFT] [AUDIT-VIOLATION] Window and buffer values must be positive - FAIL-CLOSED");
            if (CriticalFeatures.Count == 0)
                throw new InvalidOperationException("[FEATURE-DRIFT] [AUDIT-VIOLATION] CriticalFeatures cannot be empty - FAIL-CLOSED");
        }
    }

    /// <summary>
    /// Feature drift state tracking
    /// </summary>
    public sealed class FeatureDriftState
    {
        public string FeatureName { get; set; } = string.Empty;
        public FeatureStatistics BaselineStatistics { get; set; } = new();
        public List<double> RecentValues { get; set; } = new();
        public DateTime LastUpdate { get; set; }
        public DateTime LastDriftCheck { get; set; }
        public double DriftScore { get; set; }
        public bool IsBaselineSet { get; set; }
    }

    /// <summary>
    /// Feature statistics for drift calculation
    /// </summary>
    public sealed class FeatureStatistics
    {
        public double Mean { get; set; }
        public double StandardDeviation { get; set; }
        public double Median { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Result of feature drift check
    /// </summary>
    public sealed class FeatureDriftResult
    {
        public bool AllowTrading { get; set; } = true;
        public List<string> DriftViolations { get; set; } = new();
        public List<string> MissingFeatures { get; set; } = new();
        public DateTime CheckTimestamp { get; set; }
    }

    /// <summary>
    /// Drift monitoring statistics
    /// </summary>
    public sealed class DriftMonitoringStats
    {
        public long TotalDriftChecks { get; set; }
        public long TotalDriftViolations { get; set; }
        public long TotalKillSwitchTriggers { get; set; }
        public int MonitoredFeatureCount { get; set; }
        public DateTime LastCheckTime { get; set; }
    }
}