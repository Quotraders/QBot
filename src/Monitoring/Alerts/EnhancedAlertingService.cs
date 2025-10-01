using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TradingBot.Monitoring.Alerts
{
    /// <summary>
    /// Enhanced alerting system that monitors critical trading bot metrics and events
    /// Wires alert thresholds for pattern promotion, model rollback, feature drift, and execution queue breaches
    /// </summary>
    public class EnhancedAlertingService : BackgroundService
    {
        private readonly ILogger<EnhancedAlertingService> _logger;
        private readonly IAlertService _alertService;
        private readonly EnhancedAlertingConfig _config;
        private readonly Dictionary<string, AlertRule> _alertRules;
        private readonly Dictionary<string, AlertState> _alertStates;
        private readonly Timer _monitoringTimer;

        public EnhancedAlertingService(
            ILogger<EnhancedAlertingService> logger,
            IAlertService alertService,
            IOptions<EnhancedAlertingConfig> config)
        {
            _logger = logger;
            _alertService = alertService;
            _config = config.Value;
            _alertRules = new Dictionary<string, AlertRule>();
            _alertStates = new Dictionary<string, AlertState>();
            
            InitializeAlertRules();
            
            // Start monitoring timer
            _monitoringTimer = new Timer(CheckAlerts, null, TimeSpan.Zero, TimeSpan.FromSeconds(_config.CheckIntervalSeconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[ENHANCED_ALERTS] Enhanced alerting service started with {RuleCount} alert rules", _alertRules.Count);
            
            // Keep service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Initialize all alert rules as specified in requirements
        /// </summary>
        private void InitializeAlertRules()
        {
            // Pattern promoted transformer events
            _alertRules["pattern_promoted_transformer"] = new AlertRule
            {
                Name = "Pattern Promoted Transformer",
                MetricName = "pattern.promoted_transformer",
                Threshold = _config.PatternPromotedThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.PatternPromotedWindowMinutes),
                Severity = AlertSeverity.Medium,
                Description = "Pattern transformer has been promoted to live trading",
                Tags = new Dictionary<string, string> { ["category"] = "pattern", ["criticality"] = "medium" }
            };

            // Model rollback events
            _alertRules["model_rollback"] = new AlertRule
            {
                Name = "Model Rollback",
                MetricName = "model.rollback",
                Threshold = _config.ModelRollbackThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.ModelRollbackWindowMinutes),
                Severity = AlertSeverity.High,
                Description = "Model has been rolled back due to performance degradation",
                Tags = new Dictionary<string, string> { ["category"] = "model", ["criticality"] = "high" }
            };

            // Feature drift detection
            _alertRules["feature_drift_detected"] = new AlertRule
            {
                Name = "Feature Drift Detected",
                MetricName = "feature.drift_detected",
                Threshold = _config.FeatureDriftThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.FeatureDriftWindowMinutes),
                Severity = AlertSeverity.High,
                Description = "Significant feature drift detected in model inputs",
                Tags = new Dictionary<string, string> { ["category"] = "feature_drift", ["criticality"] = "high" }
            };

            // Execution queue ETA breach
            _alertRules["execution_queue_eta_breach"] = new AlertRule
            {
                Name = "Execution Queue ETA Breach",
                MetricName = "execution.queue_eta_breach",
                Threshold = _config.QueueEtaBreachThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.QueueEtaBreachWindowMinutes),
                Severity = AlertSeverity.Critical,
                Description = "Execution queue ETA has been breached, indicating potential execution delays",
                Tags = new Dictionary<string, string> { ["category"] = "execution", ["criticality"] = "critical" }
            };

            // Liquidity degradation
            _alertRules["liquidity_degradation"] = new AlertRule
            {
                Name = "Liquidity Degradation",
                MetricName = "liquidity.score",
                Threshold = _config.LiquidityDegradationThreshold,
                Comparison = AlertComparison.LessThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.LiquidityDegradationWindowMinutes),
                Severity = AlertSeverity.Medium,
                Description = "Market liquidity has degraded below acceptable levels",
                Tags = new Dictionary<string, string> { ["category"] = "liquidity", ["criticality"] = "medium" }
            };

            // OFI proxy anomaly
            _alertRules["ofi_proxy_anomaly"] = new AlertRule
            {
                Name = "OFI Proxy Anomaly",
                MetricName = "ofi.proxy_deviation",
                Threshold = _config.OfiProxyAnomalyThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.OfiProxyAnomalyWindowMinutes),
                Severity = AlertSeverity.Medium,
                Description = "Order Flow Imbalance proxy showing significant anomaly",
                Tags = new Dictionary<string, string> { ["category"] = "ofi", ["criticality"] = "medium" }
            };

            // Missing critical features
            _alertRules["critical_features_missing"] = new AlertRule
            {
                Name = "Critical Features Missing",
                MetricName = "fusion.critical_features_missing",
                Threshold = _config.CriticalFeaturesMissingThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.CriticalFeaturesMissingWindowMinutes),
                Severity = AlertSeverity.High,
                Description = "Critical features are missing from fusion engine",
                Tags = new Dictionary<string, string> { ["category"] = "fusion", ["criticality"] = "high" }
            };

            // Model tranche performance degradation
            _alertRules["tranche_performance_degradation"] = new AlertRule
            {
                Name = "Tranche Performance Degradation",
                MetricName = "model.tranche_performance_drop",
                Threshold = _config.TranchePerformanceDegradationThreshold,
                Comparison = AlertComparison.LessThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.TranchePerformanceDegradationWindowMinutes),
                Severity = AlertSeverity.High,
                Description = "Model tranche performance has degraded significantly",
                Tags = new Dictionary<string, string> { ["category"] = "model_tranche", ["criticality"] = "high" }
            };

            // AUDIT-CLEAN: Add alerts for kill-switch activation, analyzer failures, and DRY_RUN toggles per audit requirements
            
            // Kill-switch activation alert
            _alertRules["kill_switch_activated"] = new AlertRule
            {
                Name = "Kill Switch Activated",
                MetricName = "guardrail.kill_switch_activated",
                Threshold = _config.KillSwitchActivatedThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromSeconds(_config.KillSwitchActivatedWindowSeconds),
                Severity = AlertSeverity.Critical,
                Description = "Emergency kill switch has been activated - all trading operations halted",
                Tags = new Dictionary<string, string> { ["category"] = "guardrail", ["criticality"] = "critical", ["type"] = "kill_switch" }
            };

            // Analyzer failures alert
            _alertRules["analyzer_failure"] = new AlertRule
            {
                Name = "Analyzer Failure",
                MetricName = "guardrail.analyzer_failure",
                Threshold = _config.AnalyzerFailureThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromMinutes(_config.AnalyzerFailureWindowMinutes),
                Severity = AlertSeverity.High,
                Description = "Code analyzer failure detected - build or quality gate failed",
                Tags = new Dictionary<string, string> { ["category"] = "guardrail", ["criticality"] = "high", ["type"] = "analyzer" }
            };

            // DRY_RUN toggle alert
            _alertRules["dry_run_toggle"] = new AlertRule
            {
                Name = "DRY_RUN Mode Toggle",
                MetricName = "guardrail.dry_run_toggle",
                Threshold = _config.DryRunToggleThreshold,
                Comparison = AlertComparison.GreaterThan,
                EvaluationWindow = TimeSpan.FromSeconds(_config.DryRunToggleWindowSeconds),
                Severity = AlertSeverity.Medium,
                Description = "DRY_RUN mode has been toggled - trading mode changed",
                Tags = new Dictionary<string, string> { ["category"] = "guardrail", ["criticality"] = "medium", ["type"] = "dry_run" }
            };

            // Initialize alert states
            foreach (var rule in _alertRules.Values)
            {
                _alertStates[rule.Name] = new AlertState
                {
                    RuleName = rule.Name,
                    IsActive = false,
                    LastEvaluated = DateTime.UtcNow,
                    LastTriggered = null,
                    TriggerCount = 0
                };
            }

            _logger.LogInformation("[ENHANCED_ALERTS] Initialized {RuleCount} alert rules", _alertRules.Count);
        }

        /// <summary>
        /// Check all alert rules against current metrics
        /// </summary>
        private async void CheckAlerts(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var alertsTriggered = 0;

                foreach (var rule in _alertRules.Values)
                {
                    var alertState = _alertStates[rule.Name];
                    var shouldTrigger = await EvaluateAlertRuleAsync(rule, now).ConfigureAwait(false);

                    if (shouldTrigger && !alertState.IsActive)
                    {
                        // New alert triggered
                        await TriggerAlertAsync(rule, alertState, now).ConfigureAwait(false);
                        alertsTriggered++;
                    }
                    else if (!shouldTrigger && alertState.IsActive)
                    {
                        // Alert resolved
                        await ResolveAlertAsync(rule, alertState, now).ConfigureAwait(false);
                    }

                    alertState.LastEvaluated = now;
                }

                if (alertsTriggered > 0)
                {
                    _logger.LogInformation("[ENHANCED_ALERTS] Triggered {AlertCount} alerts during evaluation cycle", alertsTriggered);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ENHANCED_ALERTS] Error during alert evaluation cycle");
            }
        }

        /// <summary>
        /// Evaluate a specific alert rule against current metrics
        /// </summary>
        private async Task<bool> EvaluateAlertRuleAsync(AlertRule rule, DateTime evaluationTime)
        {
            try
            {
                // Get metric values within the evaluation window
                var windowStart = evaluationTime - rule.EvaluationWindow;
                var metricValues = await GetMetricValuesAsync(rule.MetricName, windowStart, evaluationTime).ConfigureAwait(false);

                if (metricValues.Count == 0)
                {
                    // No data available for evaluation
                    return false;
                }

                // Calculate the evaluation value based on rule type
                var evaluationValue = CalculateEvaluationValue(metricValues, rule);

                // Compare against threshold
                return rule.Comparison switch
                {
                    AlertComparison.GreaterThan => evaluationValue > rule.Threshold,
                    AlertComparison.LessThan => evaluationValue < rule.Threshold,
                    AlertComparison.Equal => Math.Abs(evaluationValue - rule.Threshold) < 0.001,
                    AlertComparison.NotEqual => Math.Abs(evaluationValue - rule.Threshold) >= 0.001,
                    _ => false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ENHANCED_ALERTS] Error evaluating alert rule {RuleName}", rule.Name);
                return false;
            }
        }

        /// <summary>
        /// Trigger an alert and notify relevant systems
        /// </summary>
        private async Task TriggerAlertAsync(AlertRule rule, AlertState alertState, DateTime triggerTime)
        {
            try
            {
                alertState.IsActive = true;
                alertState.LastTriggered = triggerTime;
                alertState.TriggerCount++;

                var alert = new Alert
                {
                    Id = Guid.NewGuid().ToString(),
                    RuleName = rule.Name,
                    Severity = rule.Severity,
                    Title = rule.Name,
                    Description = rule.Description,
                    TriggeredAt = triggerTime,
                    Tags = rule.Tags,
                    MetricName = rule.MetricName,
                    Threshold = rule.Threshold,
                    ActualValue = await GetLatestMetricValueAsync(rule.MetricName).ConfigureAwait(false)
                };

                await _alertService.SendAlertAsync(alert).ConfigureAwait(false);

                _logger.LogWarning("[ENHANCED_ALERTS] ALERT TRIGGERED: {AlertName} - {Description} (Severity: {Severity})",
                    rule.Name, rule.Description, rule.Severity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ENHANCED_ALERTS] Error triggering alert {RuleName}", rule.Name);
            }
        }

        /// <summary>
        /// Resolve an alert when conditions return to normal
        /// </summary>
        private async Task ResolveAlertAsync(AlertRule rule, AlertState alertState, DateTime resolveTime)
        {
            try
            {
                alertState.IsActive = false;
                var duration = alertState.LastTriggered.HasValue
                    ? resolveTime - alertState.LastTriggered.Value
                    : TimeSpan.Zero;

                var resolution = new AlertResolution
                {
                    Id = Guid.NewGuid().ToString(),
                    RuleName = rule.Name,
                    ResolvedAt = resolveTime,
                    Duration = duration,
                    ActualValue = await GetLatestMetricValueAsync(rule.MetricName).ConfigureAwait(false)
                };

                await _alertService.ResolveAlertAsync(resolution).ConfigureAwait(false);

                _logger.LogInformation("[ENHANCED_ALERTS] ALERT RESOLVED: {AlertName} after {Duration}",
                    rule.Name, duration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ENHANCED_ALERTS] Error resolving alert {RuleName}", rule.Name);
            }
        }

        /// <summary>
        /// Calculate evaluation value from metric values based on rule configuration
        /// </summary>
        private double CalculateEvaluationValue(List<double> metricValues, AlertRule rule)
        {
            return rule.AggregationType switch
            {
                AlertAggregationType.Average => metricValues.Average(),
                AlertAggregationType.Maximum => metricValues.Max(),
                AlertAggregationType.Minimum => metricValues.Min(),
                AlertAggregationType.Sum => metricValues.Sum(),
                AlertAggregationType.Count => metricValues.Count,
                AlertAggregationType.Latest => metricValues.Last(),
                _ => metricValues.Average()
            };
        }

        /// <summary>
        /// Get metric values for a specific time window
        /// </summary>
        private async Task<List<double>> GetMetricValuesAsync(string metricName, DateTime startTime, DateTime endTime)
        {
            // Implementation would integrate with metrics collection system
            // For now, return sample data
            await Task.CompletedTask;
            return new List<double>();
        }

        /// <summary>
        /// Get the latest value for a specific metric
        /// </summary>
        private async Task<double> GetLatestMetricValueAsync(string metricName)
        {
            // Implementation would integrate with metrics collection system
            // For now, return sample data
            await Task.CompletedTask;
            return 0.0;
        }

        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Configuration for enhanced alerting system
    /// </summary>
    public class EnhancedAlertingConfig
    {
        public int CheckIntervalSeconds { get; set; } = 30;

        // Pattern promoted transformer thresholds
        public double PatternPromotedThreshold { get; set; } = 1.0;
        public int PatternPromotedWindowMinutes { get; set; } = 5;

        // Model rollback thresholds
        public double ModelRollbackThreshold { get; set; } = 1.0;
        public int ModelRollbackWindowMinutes { get; set; } = 1;

        // Feature drift thresholds
        public double FeatureDriftThreshold { get; set; } = 0.3;
        public int FeatureDriftWindowMinutes { get; set; } = 15;

        // Execution queue ETA breach thresholds
        public double QueueEtaBreachThreshold { get; set; } = 1.0;
        public int QueueEtaBreachWindowMinutes { get; set; } = 1;

        // Liquidity degradation thresholds
        public double LiquidityDegradationThreshold { get; set; } = 0.6;
        public int LiquidityDegradationWindowMinutes { get; set; } = 10;

        // OFI proxy anomaly thresholds
        public double OfiProxyAnomalyThreshold { get; set; } = 2.0;
        public int OfiProxyAnomalyWindowMinutes { get; set; } = 5;

        // Critical features missing thresholds
        public double CriticalFeaturesMissingThreshold { get; set; } = 3.0;
        public int CriticalFeaturesMissingWindowMinutes { get; set; } = 5;

        // Tranche performance degradation thresholds
        public double TranchePerformanceDegradationThreshold { get; set; } = -0.2;
        public int TranchePerformanceDegradationWindowMinutes { get; set; } = 30;

        // AUDIT-CLEAN: Guardrail alert thresholds per audit requirements
        
        // Kill switch activation alert configuration
        public double KillSwitchActivatedThreshold { get; set; } = 0.5;
        public int KillSwitchActivatedWindowSeconds { get; set; } = 30;

        // Analyzer failure alert configuration
        public double AnalyzerFailureThreshold { get; set; } = 0.5;
        public int AnalyzerFailureWindowMinutes { get; set; } = 1;

        // DRY_RUN toggle alert configuration
        public double DryRunToggleThreshold { get; set; } = 0.5;
        public int DryRunToggleWindowSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Alert rule definition
    /// </summary>
    public class AlertRule
    {
        public string Name { get; set; } = string.Empty;
        public string MetricName { get; set; } = string.Empty;
        public double Threshold { get; set; }
        public AlertComparison Comparison { get; set; }
        public TimeSpan EvaluationWindow { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public AlertAggregationType AggregationType { get; set; } = AlertAggregationType.Average;
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Alert state tracking
    /// </summary>
    public class AlertState
    {
        public string RuleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime LastEvaluated { get; set; }
        public DateTime? LastTriggered { get; set; }
        public int TriggerCount { get; set; }
    }

    /// <summary>
    /// Alert instance
    /// </summary>
    public class Alert
    {
        public string Id { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime TriggeredAt { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
        public string MetricName { get; set; } = string.Empty;
        public double Threshold { get; set; }
        public double ActualValue { get; set; }
    }

    /// <summary>
    /// Alert resolution
    /// </summary>
    public class AlertResolution
    {
        public string Id { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public DateTime ResolvedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public double ActualValue { get; set; }
    }

    /// <summary>
    /// Interface for alert service
    /// </summary>
    public interface IAlertService
    {
        Task SendAlertAsync(Alert alert);
        Task ResolveAlertAsync(AlertResolution resolution);
    }

    // Enums
    public enum AlertComparison
    {
        GreaterThan,
        LessThan,
        Equal,
        NotEqual
    }

    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum AlertAggregationType
    {
        Average,
        Maximum,
        Minimum,
        Sum,
        Count,
        Latest
    }
}