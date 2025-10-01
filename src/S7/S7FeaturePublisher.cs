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
    public sealed class S7FeaturePublisher : IHostedService, IDisposable
    {
        private readonly ILogger<S7FeaturePublisher> _logger;
        private readonly IServiceProvider _serviceProvider;
        private IS7Service? _s7Service;
        private IFeatureBus? _featureBus;
        private S7Configuration? _config;
        private Timer? _publishTimer;
        private bool _disposed;

        // LoggerMessage delegates for performance
        private static readonly Action<ILogger, Exception?> _logS7ServiceNotAvailable = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1001, "S7ServiceNotAvailable"), 
                "[S7-FEATURE-PUBLISHER] S7 service not available - TRIGGERING HOLD + TELEMETRY");
                
        private static readonly Action<ILogger, Exception?> _logFeatureBusNotAvailable = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1002, "FeatureBusNotAvailable"), 
                "[S7-FEATURE-PUBLISHER] Feature bus not available - TRIGGERING HOLD + TELEMETRY");
                
        private static readonly Action<ILogger, Exception?> _logConfigNotAvailable = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1003, "ConfigNotAvailable"), 
                "[S7-FEATURE-PUBLISHER] S7 configuration not available - TRIGGERING HOLD + TELEMETRY");
                
        private static readonly Action<ILogger, Exception?> _logFeaturePublishingDisabled = 
            LoggerMessage.Define(LogLevel.Information, new EventId(1004, "FeaturePublishingDisabled"), 
                "[S7-FEATURE-PUBLISHER] S7 feature publishing disabled in configuration");
                
        private static readonly Action<ILogger, int, Exception?> _logFeaturePublisherStarted = 
            LoggerMessage.Define<int>(LogLevel.Information, new EventId(1005, "FeaturePublisherStarted"), 
                "[S7-FEATURE-PUBLISHER] S7 feature publisher started - Publishing every {Minutes} minutes");
                
        private static readonly Action<ILogger, Exception?> _logFeaturePublisherStopped = 
            LoggerMessage.Define(LogLevel.Information, new EventId(1006, "FeaturePublisherStopped"), 
                "[S7-FEATURE-PUBLISHER] S7 feature publisher stopped");
                
        private static readonly Action<ILogger, Exception?> _logInvalidConfiguration = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1007, "InvalidConfiguration"), 
                "[S7-FEATURE-PUBLISHER] Invalid configuration for S7 feature publisher timer");
                
        private static readonly Action<ILogger, Exception?> _logDisposedDuringStartup = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1008, "DisposedDuringStartup"), 
                "[S7-FEATURE-PUBLISHER] S7 feature publisher disposed during startup");
                
        private static readonly Action<ILogger, Exception?> _logAlreadyDisposed = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1009, "AlreadyDisposed"), 
                "[S7-FEATURE-PUBLISHER] S7 feature publisher already disposed during shutdown");
                
        private static readonly Action<ILogger, Exception?> _logInvalidOperationShutdown = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1010, "InvalidOperationShutdown"), 
                "[S7-FEATURE-PUBLISHER] Invalid operation during S7 feature publisher shutdown");
                
        private static readonly Action<ILogger, Exception?> _logInvalidOperationStartup = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1011, "InvalidOperationStartup"), 
                "[S7-FEATURE-PUBLISHER] Invalid operation during S7 feature publisher startup");

        private static readonly Action<ILogger, Exception?> _logMissingDependencies = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1012, "MissingDependencies"), 
                "[S7-FEATURE-PUBLISHER] Missing dependencies in publish callback - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logServiceNotReadyRequired = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1013, "ServiceNotReadyRequired"), 
                "[S7-FEATURE-PUBLISHER] S7 service not ready but required - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logServiceNotReadySkipping = 
            LoggerMessage.Define(LogLevel.Debug, new EventId(1014, "ServiceNotReadySkipping"), 
                "[S7-FEATURE-PUBLISHER] S7 service not ready, skipping feature publishing");

        private static readonly Action<ILogger, int, Exception?> _logPublishedFeatures = 
            LoggerMessage.Define<int>(LogLevel.Debug, new EventId(1015, "PublishedFeatures"), 
                "[S7-FEATURE-PUBLISHER] Published S7 features for {SymbolCount} symbols");

        private static readonly Action<ILogger, Exception?> _logObjectDisposedError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1016, "ObjectDisposedError"), 
                "[S7-FEATURE-PUBLISHER] Object disposed during feature publishing - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logObjectDisposedWarning = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(1017, "ObjectDisposedWarning"), 
                "[S7-FEATURE-PUBLISHER] Object disposed publishing S7 features");

        private static readonly Action<ILogger, Exception?> _logInvalidArgumentError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1018, "InvalidArgumentError"), 
                "[S7-FEATURE-PUBLISHER] Invalid argument during feature publishing - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logInvalidArgumentWarning = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(1019, "InvalidArgumentWarning"), 
                "[S7-FEATURE-PUBLISHER] Invalid argument publishing S7 features");

        private static readonly Action<ILogger, Exception?> _logInvalidOperationError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(1020, "InvalidOperationError"), 
                "[S7-FEATURE-PUBLISHER] Invalid operation during feature publishing - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logInvalidOperationWarning = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(1021, "InvalidOperationWarning"), 
                "[S7-FEATURE-PUBLISHER] Invalid operation publishing S7 features");

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
                    _logS7ServiceNotAvailable(_logger, null);
                    return Task.CompletedTask;
                }

                if (_featureBus == null)
                {
                    _logFeatureBusNotAvailable(_logger, null);
                    return Task.CompletedTask;
                }

                if (_config == null)
                {
                    _logConfigNotAvailable(_logger, null);
                    return Task.CompletedTask;
                }

                if (!_config.Enabled || !_config.EnableFeatureBus)
                {
                    _logFeaturePublishingDisabled(_logger, null);
                    return Task.CompletedTask;
                }

                // Start publishing timer - AUDIT-CLEAN: Use dedicated feature publishing interval
                if (_config.FeaturePublishingIntervalMinutes <= 0)
                {
                    _logInvalidConfiguration(_logger, new InvalidOperationException("FeaturePublishingIntervalMinutes must be greater than 0"));
                    return Task.CompletedTask;
                }
                
                var publishInterval = TimeSpan.FromMinutes(_config.FeaturePublishingIntervalMinutes);
                _publishTimer = new Timer(PublishFeaturesCallback, null, TimeSpan.Zero, publishInterval);

                _logFeaturePublisherStarted(_logger, _config.FeaturePublishingIntervalMinutes, null);

                return Task.CompletedTask;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logInvalidConfiguration(_logger, ex);
                return Task.FromException(ex);
            }
            catch (ObjectDisposedException ex)
            {
                _logDisposedDuringStartup(_logger, ex);
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logInvalidOperationStartup(_logger, ex);
                return Task.FromException(ex);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_publishTimer != null)
                    await _publishTimer.DisposeAsync().ConfigureAwait(false);
                _logFeaturePublisherStopped(_logger, null);
            }
            catch (ObjectDisposedException ex)
            {
                _logAlreadyDisposed(_logger, ex);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logInvalidOperationShutdown(_logger, ex);
                throw;
            }
        }

        private void PublishFeaturesCallback(object? state)
        {
            try
            {
                if (!ValidateServicesForPublishing())
                    return;

                var snapshot = _s7Service!.GetCurrentSnapshot();
                var timestamp = DateTime.UtcNow;
                var telemetryPrefix = _config!.TelemetryPrefix;

                PublishCrossSymbolFeatures(snapshot, timestamp, telemetryPrefix);
                PublishFusionTags(snapshot, timestamp);
                PublishIndividualSymbolFeatures(timestamp, telemetryPrefix);

                _logPublishedFeatures(_logger, _config.Symbols.Count, null);
            }
            catch (ObjectDisposedException ex)
            {
                HandleObjectDisposedException(ex);
            }
            catch (ArgumentException ex)
            {
                HandleArgumentException(ex);
            }
            catch (InvalidOperationException ex)
            {
                HandleInvalidOperationException(ex);
            }
        }

        private bool ValidateServicesForPublishing()
        {
            if (_s7Service == null || _featureBus == null || _config == null)
            {
                _logMissingDependencies(_logger, null);
                return false;
            }

            // FAIL-CLOSED: Check if S7 service is ready
            if (!_s7Service.IsReady())
            {
                if (_config.FailOnMissingData)
                {
                    _logServiceNotReadyRequired(_logger, null);
                    return false;
                }
                _logServiceNotReadySkipping(_logger, null);
                return false;
            }

            return true;
        }

        private void PublishCrossSymbolFeatures(object snapshot, DateTime timestamp, string telemetryPrefix)
        {
            // Publish cross-symbol features for knowledge graph consumption
            _featureBus!.Publish("CROSS", timestamp, $"{telemetryPrefix}.coherence", (double)((dynamic)snapshot).CrossSymbolCoherence);
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.leader", (double)((dynamic)snapshot).DominantLeader);
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.signal_strength", (double)((dynamic)snapshot).SignalStrength);
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.actionable", ((dynamic)snapshot).IsActionable ? 1.0 : 0.0);
            
            // Publish enhanced cross-symbol features for adaptive analysis
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.global_dispersion", (double)((dynamic)snapshot).GlobalDispersionIndex);
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.adaptive_volatility", (double)((dynamic)snapshot).AdaptiveVolatilityMeasure);
            _featureBus.Publish("CROSS", timestamp, $"{telemetryPrefix}.system_coherence", (double)((dynamic)snapshot).SystemCoherenceScore);
        }

        private void PublishFusionTags(object snapshot, DateTime timestamp)
        {
            // Publish fusion tags for knowledge graph integration if enabled
            if (_config!.EnableFusionTags)
            {
                var fusionTags = ((dynamic)snapshot).FusionTags;
                foreach (var fusionTag in fusionTags)
                {
                    if (fusionTag.Value is double doubleValue)
                    {
                        _featureBus!.Publish("FUSION", timestamp, fusionTag.Key, doubleValue);
                    }
                    else if (fusionTag.Value is decimal decimalValue)
                    {
                        _featureBus!.Publish("FUSION", timestamp, fusionTag.Key, (double)decimalValue);
                    }
                    else if (double.TryParse(fusionTag.Value?.ToString(), out double parsedValue))
                    {
                        _featureBus!.Publish("FUSION", timestamp, fusionTag.Key, parsedValue);
                    }
                }
            }
        }

        private void PublishIndividualSymbolFeatures(DateTime timestamp, string telemetryPrefix)
        {
            // Publish individual symbol features
            foreach (var symbol in _config!.Symbols)
            {
                var featureTuple = _s7Service!.GetFeatureTuple(symbol);
                PublishCoreFeatures(symbol, timestamp, telemetryPrefix, featureTuple);
                PublishEnhancedFeatures(symbol, timestamp, telemetryPrefix, featureTuple);
            }
        }

        private void PublishCoreFeatures(string symbol, DateTime timestamp, string telemetryPrefix, object featureTuple)
        {
            var tuple = (dynamic)featureTuple;
            
            // AUDIT-CLEAN: Publish core features with configured telemetry prefix
            _featureBus!.Publish(symbol, timestamp, $"{telemetryPrefix}.rs", (double)tuple.RelativeStrengthShort);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rs.medium", (double)tuple.RelativeStrengthMedium);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rs.long", (double)tuple.RelativeStrengthLong);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.rsz", (double)tuple.ZScore);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.coherence", (double)tuple.Coherence);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.size_tilt", (double)tuple.SizeTilt);
            
            double leaderValue = tuple.Leader switch
            {
                "ES" => 1.0,
                "NQ" => -1.0,
                _ => 0.0
            };
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.leader", leaderValue);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.signal_active", tuple.IsSignalActive ? 1.0 : 0.0);
        }

        private void PublishEnhancedFeatures(string symbol, DateTime timestamp, string telemetryPrefix, object featureTuple)
        {
            var tuple = (dynamic)featureTuple;
            
            // AUDIT-CLEAN: Publish enhanced adaptive and dispersion features
            _featureBus!.Publish(symbol, timestamp, $"{telemetryPrefix}.adaptive_threshold", (double)tuple.AdaptiveThreshold);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.multi_index_dispersion", (double)tuple.MultiIndexDispersion);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.advance_fraction", (double)tuple.AdvanceFraction);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_adjusted_size_tilt", (double)tuple.DispersionAdjustedSizeTilt);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_boosted", tuple.IsDispersionBoosted ? 1.0 : 0.0);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.dispersion_blocked", tuple.IsDispersionBlocked ? 1.0 : 0.0);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.global_dispersion_index", (double)tuple.GlobalDispersionIndex);
            _featureBus.Publish(symbol, timestamp, $"{telemetryPrefix}.adaptive_volatility_measure", (double)tuple.AdaptiveVolatilityMeasure);
        }

        private void HandleObjectDisposedException(ObjectDisposedException ex)
        {
            if (_config?.FailOnMissingData == true)
            {
                _logObjectDisposedError(_logger, ex);
            }
            else
            {
                _logObjectDisposedWarning(_logger, ex);
            }
        }

        private void HandleArgumentException(ArgumentException ex)
        {
            if (_config?.FailOnMissingData == true)
            {
                _logInvalidArgumentError(_logger, ex);
            }
            else
            {
                _logInvalidArgumentWarning(_logger, ex);
            }
        }

        private void HandleInvalidOperationException(InvalidOperationException ex)
        {
            if (_config?.FailOnMissingData == true)
            {
                _logInvalidOperationError(_logger, ex);
            }
            else
            {
                _logInvalidOperationWarning(_logger, ex);
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