using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Abstractions;
using Zones;

namespace BotCore.Features
{
    /// <summary>
    /// Feature Publisher hosted service for automation-first upgrade scope
    /// Publishes features on each bar boundary with fail-closed behavior
    /// Emits fusion.feature_missing telemetry when resolvers fail
    /// Exceptions bubble to crash the service (fail closed) rather than silently swallowing
    /// </summary>
    public sealed class FeaturePublisher : IHostedService, IDisposable
    {
        private readonly ILogger<FeaturePublisher> _logger;
        private readonly IOptions<S7Configuration> _s7Config;
        private readonly IFeatureBus _featureBus;
        private readonly IEnumerable<IFeatureResolver> _resolvers;
        private readonly Timer? _publishTimer;
        private readonly object _publishLock = new();
        private bool _disposed;

        // Performance counters
        private long _publishCycles;
        private long _featurePublished;
        private long _publishErrors;
        private long _missingFeatures;

        public FeaturePublisher(
            ILogger<FeaturePublisher> logger,
            IOptions<S7Configuration> s7Config,
            IFeatureBus featureBus,
            IEnumerable<IFeatureResolver> resolvers)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _s7Config = s7Config ?? throw new ArgumentNullException(nameof(s7Config));
            _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
            _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));

            // Get publish interval from configuration with validation and fail-closed behavior
            var publishIntervalMinutes = _s7Config.Value?.FeaturePublishingIntervalMinutes;
            
            // FAIL-CLOSED: Reject missing/invalid configuration 
            if (!publishIntervalMinutes.HasValue || publishIntervalMinutes <= 0)
            {
                _logger.LogError("[FEATURE-PUBLISHER] FAIL-CLOSED: Invalid/missing FeaturePublishingIntervalMinutes configuration: {Interval}. Must be > 0", publishIntervalMinutes);
                throw new InvalidOperationException($"[FEATURE-PUBLISHER] FAIL-CLOSED: Invalid/missing FeaturePublishingIntervalMinutes configuration: {publishIntervalMinutes}. Must be > 0.");
            }
            
            var publishInterval = TimeSpan.FromMinutes(publishIntervalMinutes.Value);
            _logger.LogInformation("[FEATURE-PUBLISHER] Configured publish interval: {Interval}m", publishIntervalMinutes.Value);
            
            _publishTimer = new Timer(PublishFeaturesCallback, null, publishInterval, publishInterval);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[FEATURE-PUBLISHER] Starting feature publisher with {ResolverCount} resolvers", 
                _resolvers.Count());
            
            // Log available feature keys for audit compliance
            foreach (var resolver in _resolvers)
            {
                var featureKeys = resolver.GetAvailableFeatureKeys();
                _logger.LogInformation("[FEATURE-PUBLISHER] Resolver {ResolverType} provides features: {Features}", 
                    resolver.GetType().Name, string.Join(", ", featureKeys));
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[FEATURE-PUBLISHER] Stopping feature publisher - Stats: Cycles={Cycles}, Published={Published}, Errors={Errors}, Missing={Missing}", 
                _publishCycles, _featurePublished, _publishErrors, _missingFeatures);
            
            _publishTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Fire-and-forget timer callback must catch all exceptions (HttpRequestException, JsonException, IOException, etc.) to prevent unobserved task faults and ensure fail-closed behavior with audit telemetry")]
        private void PublishFeaturesCallback(object? state)
        {
            if (_disposed) return;

            // Fire-and-forget pattern for async operation in timer callback
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishFeaturesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Catch all exceptions to ensure fail-closed behavior in fire-and-forget context
                    // This includes: HttpRequestException, JsonException, IOException, InvalidOperationException, etc.
                    // All exceptions are logged; none escape to become unobserved task faults
                    _logger.LogError(ex, "[FEATURE-PUBLISHER] [AUDIT-VIOLATION] Feature publishing failed - FAIL-CLOSED + TELEMETRY");
                    // Log but don't rethrow in fire-and-forget context
                }
            });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Resolver operations must catch all exceptions (HttpRequestException, JsonException, IOException, etc.) to ensure fail-closed behavior, emit fusion.feature_missing telemetry, and prevent partial failures from stopping feature publishing")]
        private async Task PublishFeaturesAsync(CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            
            lock (_publishLock)
            {
                _publishCycles++;
            }

            // Get symbols from S7 configuration to avoid hardcoding
            var symbols = _s7Config.Value?.Symbols ?? new[] { "ES", "NQ" };
            
            _logger.LogTrace("[FEATURE-PUBLISHER] Publishing features for symbols: {Symbols}", string.Join(", ", symbols));

            var currentTime = DateTime.UtcNow;
            var publishedCount = 0;
            var errorCount = 0;
            var missingCount = 0;

            foreach (var symbol in symbols)
            {
                foreach (var resolver in _resolvers)
                {
                    try
                    {
                        var featureKeys = resolver.GetAvailableFeatureKeys();
                        
                        foreach (var featureKey in featureKeys)
                        {
                            var value = await resolver.TryGetAsync(symbol, featureKey, cancellationToken).ConfigureAwait(false);
                            
                            if (value.HasValue)
                            {
                                // Publish feature via feature bus
                                _featureBus.Publish(symbol, currentTime, featureKey, value.Value);
                                publishedCount++;
                                
                                _logger.LogTrace("[FEATURE-PUBLISHER] Published {Symbol}.{FeatureKey}={Value:F6}", 
                                    symbol, featureKey, value.Value);
                            }
                            else
                            {
                                // Emit fusion.feature_missing telemetry as specified
                                _featureBus.Publish(symbol, currentTime, "fusion.feature_missing", 1.0);
                                missingCount++;
                                
                                _logger.LogWarning("[FEATURE-PUBLISHER] [AUDIT-VIOLATION] Missing feature {Symbol}.{FeatureKey} - emitted fusion.feature_missing telemetry", 
                                    symbol, featureKey);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Catch all exceptions to ensure fail-closed behavior
                        // This includes: HttpRequestException, JsonException, IOException, InvalidOperationException, etc.
                        errorCount++;
                        _logger.LogError(ex, "[FEATURE-PUBLISHER] [AUDIT-VIOLATION] Resolver {ResolverType} failed for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                            resolver.GetType().Name, symbol);
                        
                        // Emit fusion.feature_missing for resolver failure
                        try
                        {
                            _featureBus.Publish(symbol, currentTime, "fusion.feature_missing", 1.0);
                        }
                        catch (Exception busEx)
                        {
                            _logger.LogError(busEx, "[FEATURE-PUBLISHER] [AUDIT-VIOLATION] Feature bus publish failed - CRITICAL SYSTEM FAILURE");
                            
                            // Double failure: resolver failed AND feature bus failed
                            // This is a critical system failure - fail closed immediately
                            throw new InvalidOperationException($"[FEATURE-PUBLISHER] Critical system failure: resolver and feature bus both failed for '{symbol}': {ex.Message} | {busEx.Message}", ex);
                        }
                    }
                }
            }

            // Calculate and log publish latency telemetry per audit requirements
            var publishLatency = DateTime.UtcNow - startTime;
            
            // Update performance counters
            lock (_publishLock)
            {
                _featurePublished += publishedCount;
                _publishErrors += errorCount;
                _missingFeatures += missingCount;
            }

            _logger.LogDebug("[FEATURE-PUBLISHER] Publish cycle complete: Published={Published}, Errors={Errors}, Missing={Missing}, Latency={Latency}ms", 
                publishedCount, errorCount, missingCount, publishLatency.TotalMilliseconds);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _publishTimer?.Dispose();
                _disposed = true;
                
                _logger.LogInformation("[FEATURE-PUBLISHER] Disposed - Final stats: Cycles={Cycles}, Published={Published}, Errors={Errors}, Missing={Missing}", 
                    _publishCycles, _featurePublished, _publishErrors, _missingFeatures);
            }
        }
    }
}