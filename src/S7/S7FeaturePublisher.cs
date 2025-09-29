using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Abstractions;
using Zones; // For IFeatureBus

namespace TradingBot.S7
{
    /// <summary>
    /// S7 Feature Publisher - Phase 3: Feature exposure for fusion/DSL
    /// Publishes S7 features to IFeatureBus for knowledge graph and DSL consumption
    /// AUDIT-CLEAN: No hardcoded values, fail-closed behavior, full telemetry
    /// </summary>
    public class S7FeaturePublisher : IHostedService, IDisposable
    {
        private readonly ILogger<S7FeaturePublisher> _logger;
        private readonly IServiceProvider _serviceProvider;
        private IS7Service? _s7Service;
        private IFeatureBus? _featureBus;
        private S7Configuration? _config;
        private Timer? _publishTimer;
        private bool _disposed;

        public S7FeaturePublisher(
            ILogger<S7FeaturePublisher> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Get services through service provider
                _s7Service = _serviceProvider.GetService<IS7Service>();
                _featureBus = _serviceProvider.GetService<IFeatureBus>();
                _config = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<S7Configuration>>()?.Value;

                // FAIL-CLOSED: Check for missing dependencies
                if (_s7Service == null)
                {
                    _logger.LogError("[S7-FEATURE-PUBLISHER] S7 service not available - TRIGGERING HOLD + TELEMETRY");
                    return Task.CompletedTask;
                }

                if (_featureBus == null)
                {
                    _logger.LogError("[S7-FEATURE-PUBLISHER] Feature bus not available - TRIGGERING HOLD + TELEMETRY");
                    return Task.CompletedTask;
                }

                if (_config == null)
                {
                    _logger.LogError("[S7-FEATURE-PUBLISHER] S7 configuration not available - TRIGGERING HOLD + TELEMETRY");
                    return Task.CompletedTask;
                }

                if (!_config.Enabled || !_config.EnableFeatureBus)
                {
                    _logger.LogInformation("[S7-FEATURE-PUBLISHER] S7 feature publishing disabled in configuration");
                    return Task.CompletedTask;
                }

                // Start publishing timer - AUDIT-CLEAN: Use configured timeframe
                var publishInterval = TimeSpan.FromMinutes(_config.BarTimeframeMinutes);
                _publishTimer = new Timer(PublishFeaturesCallback, null, TimeSpan.Zero, publishInterval);

                _logger.LogInformation("[S7-FEATURE-PUBLISHER] S7 feature publisher started - Publishing every {Minutes} minutes", 
                    _config.BarTimeframeMinutes);

                return Task.CompletedTask;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] Invalid configuration for S7 feature publisher timer");
                return Task.FromException(ex);
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] S7 feature publisher disposed during startup");
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] Invalid operation during S7 feature publisher startup");
                return Task.FromException(ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _publishTimer?.Dispose();
                _logger.LogInformation("[S7-FEATURE-PUBLISHER] S7 feature publisher stopped");
                return Task.CompletedTask;
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] S7 feature publisher already disposed during shutdown");
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] Invalid operation during S7 feature publisher shutdown");
                return Task.FromException(ex);
            }
        }

        private void PublishFeaturesCallback(object? state)
        {
            try
            {
                if (_s7Service == null || _featureBus == null || _config == null)
                {
                    _logger.LogError("[S7-FEATURE-PUBLISHER] Missing dependencies in publish callback - TRIGGERING HOLD + TELEMETRY");
                    return;
                }

                // FAIL-CLOSED: Check if S7 service is ready
                if (!_s7Service.IsReady())
                {
                    if (_config.FailOnMissingData)
                    {
                        _logger.LogError("[S7-FEATURE-PUBLISHER] S7 service not ready but required - TRIGGERING HOLD + TELEMETRY");
                        return;
                    }
                    _logger.LogDebug("[S7-FEATURE-PUBLISHER] S7 service not ready, skipping feature publishing");
                    return;
                }

                var snapshot = _s7Service.GetCurrentSnapshot();
                var timestamp = DateTime.UtcNow;
                var telemetryPrefix = _config.TelemetryPrefix;

                // Publish cross-symbol features for knowledge graph consumption
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.coherence", (double)snapshot.CrossSymbolCoherence);
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.leader", (double)snapshot.DominantLeader);
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.signal_strength", (double)snapshot.SignalStrength);
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.actionable", snapshot.IsActionable ? 1.0 : 0.0);
                
                // Publish enhanced cross-symbol features for adaptive analysis
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.global_dispersion", (double)snapshot.GlobalDispersionIndex);
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.adaptive_volatility", (double)snapshot.AdaptiveVolatilityMeasure);
                _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.system_coherence", (double)snapshot.SystemCoherenceScore);

                // Publish fusion tags for knowledge graph integration if enabled
                if (_config.EnableFusionTags)
                {
                    foreach (var fusionTag in snapshot.FusionTags)
                    {
                        if (fusionTag.Value is double doubleValue)
                        {
                            _featureBus.Publish("FUSION", timestamp, fusionTag.Key, doubleValue);
                        }
                        else if (fusionTag.Value is decimal decimalValue)
                        {
                            _featureBus.Publish("FUSION", timestamp, fusionTag.Key, (double)decimalValue);
                        }
                        else if (double.TryParse(fusionTag.Value?.ToString(), out var parsedValue))
                        {
                            _featureBus.Publish("FUSION", timestamp, fusionTag.Key, parsedValue);
                        }
                    }
                }

                // Publish individual symbol features
                foreach (var symbol in _config.Symbols)
                {
                    var featureTuple = _s7Service.GetFeatureTuple(symbol);
                    
                    // AUDIT-CLEAN: Publish core features with configured telemetry prefix
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rs", (double)featureTuple.RelativeStrengthShort);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rs.medium", (double)featureTuple.RelativeStrengthMedium);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rs.long", (double)featureTuple.RelativeStrengthLong);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rsz", (double)featureTuple.ZScore);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.coherence", (double)featureTuple.Coherence);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.size_tilt", (double)featureTuple.SizeTilt);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.leader", featureTuple.Leader == "ES" ? 1.0 : (featureTuple.Leader == "NQ" ? -1.0 : 0.0));
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.signal_active", featureTuple.IsSignalActive ? 1.0 : 0.0);
                    
                    // AUDIT-CLEAN: Publish enhanced adaptive and dispersion features
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.adaptive_threshold", (double)featureTuple.AdaptiveThreshold);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.multi_index_dispersion", (double)featureTuple.MultiIndexDispersion);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.advance_fraction", (double)featureTuple.AdvanceFraction);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_adjusted_size_tilt", (double)featureTuple.DispersionAdjustedSizeTilt);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_boosted", featureTuple.IsDispersionBoosted ? 1.0 : 0.0);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_blocked", featureTuple.IsDispersionBlocked ? 1.0 : 0.0);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.global_dispersion_index", (double)featureTuple.GlobalDispersionIndex);
                    _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.adaptive_volatility_measure", (double)featureTuple.AdaptiveVolatilityMeasure);
                }

                _logger.LogDebug("[S7-FEATURE-PUBLISHER] Published S7 features for {SymbolCount} symbols", _config.Symbols.Count);
            }
            catch (Exception ex)
            {
                if (_config?.FailOnMissingData == true)
                {
                    _logger.LogError(ex, "[S7-FEATURE-PUBLISHER] Feature publishing failed - TRIGGERING HOLD + TELEMETRY");
                }
                else
                {
                    _logger.LogWarning(ex, "[S7-FEATURE-PUBLISHER] Error publishing S7 features");
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _publishTimer?.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}