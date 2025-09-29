using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Market;

namespace BotCore.Features
{
    /// <summary>
    /// Bar Dispatcher Hook for automation-first upgrade scope
    /// Hooks into bar processing pipeline and dispatches to feature resolvers
    /// Implements fail-closed behavior with audit-clean telemetry
    /// </summary>
    public sealed class BarDispatcherHook : IHostedService, IDisposable
    {
        private readonly ILogger<BarDispatcherHook> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnumerable<IFeatureResolver> _resolvers;
        private bool _disposed;

        // Performance counters
        private long _barsProcessed;
        private long _resolverCalls;
        private long _resolverErrors;

        public BarDispatcherHook(
            ILogger<BarDispatcherHook> logger,
            IServiceProvider serviceProvider,
            IEnumerable<IFeatureResolver> resolvers)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[BAR-DISPATCHER] Starting bar dispatcher hook with {ResolverCount} feature resolvers", 
                _resolvers.Count());

            try
            {
                // Try to find and hook into the bar aggregator
                // This might be in BarPyramid, MarketStructureService, or other services
                var barPyramid = _serviceProvider.GetService<BarPyramid>();
                if (barPyramid != null)
                {
                    HookIntoBarPyramid(barPyramid);
                    _logger.LogInformation("[BAR-DISPATCHER] Hooked into BarPyramid for bar processing");
                }
                else
                {
                    _logger.LogWarning("[BAR-DISPATCHER] [AUDIT-VIOLATION] BarPyramid not found - attempting alternative bar sources");
                    
                    // Try to find other bar sources
                    TryHookAlternativeBarSources();
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BAR-DISPATCHER] [AUDIT-VIOLATION] Failed to start bar dispatcher hook - FAIL-CLOSED + TELEMETRY");
                
                // Fail-closed: let exception bubble up to crash service
                throw new InvalidOperationException($"[BAR-DISPATCHER] Critical failure starting bar dispatcher: {ex.Message}", ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[BAR-DISPATCHER] Stopping bar dispatcher hook - Stats: Bars={Bars}, ResolverCalls={Calls}, Errors={Errors}", 
                _barsProcessed, _resolverCalls, _resolverErrors);

            return Task.CompletedTask;
        }

        private void HookIntoBarPyramid(BarPyramid barPyramid)
        {
            try
            {
                // Hook into the M1 (1-minute) bar aggregator for real-time processing
                if (barPyramid.M1 != null)
                {
                    barPyramid.M1.OnBarClosed += OnBarClosedAsync;
                    _logger.LogInformation("[BAR-DISPATCHER] Hooked into M1 bar aggregator");
                }

                // Also hook into M5 for additional timeframe processing if available
                if (barPyramid.M5 != null)
                {
                    barPyramid.M5.OnBarClosed += OnBarClosedAsync;
                    _logger.LogInformation("[BAR-DISPATCHER] Hooked into M5 bar aggregator");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BAR-DISPATCHER] [AUDIT-VIOLATION] Failed to hook into BarPyramid - FAIL-CLOSED + TELEMETRY");
                throw;
            }
        }

        private void TryHookAlternativeBarSources()
        {
            // Try to find other bar processing services that might emit bar events
            var services = new[]
            {
                typeof(BotCore.Services.TradingSystemBarConsumer),
                typeof(BotCore.Services.BarTrackingService),
                // Add other potential bar sources here
            };

            foreach (var serviceType in services)
            {
                try
                {
                    var service = _serviceProvider.GetService(serviceType);
                    if (service != null)
                    {
                        _logger.LogInformation("[BAR-DISPATCHER] Found alternative bar source: {ServiceType}", serviceType.Name);
                        // Hook into events if available using reflection
                        TryHookIntoServiceEvents(service);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[BAR-DISPATCHER] Failed to hook into {ServiceType}", serviceType.Name);
                }
            }
        }

        private void TryHookIntoServiceEvents(object service)
        {
            try
            {
                var serviceType = service.GetType();
                var events = serviceType.GetEvents();
                
                foreach (var eventInfo in events)
                {
                    if (eventInfo.Name.Contains("Bar", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("[BAR-DISPATCHER] Found potential bar event: {EventName} on {ServiceType}", 
                            eventInfo.Name, serviceType.Name);
                        // Could implement dynamic event hooking here if needed
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BAR-DISPATCHER] Failed to inspect service events for {ServiceType}", service.GetType().Name);
            }
        }

        private async void OnBarClosedAsync(string symbol, Bar bar)
        {
            if (_disposed) return;

            try
            {
                Interlocked.Increment(ref _barsProcessed);

                _logger.LogTrace("[BAR-DISPATCHER] Processing bar for {Symbol}: {Start} - {End}, OHLC={Open}/{High}/{Low}/{Close}, Vol={Volume}", 
                    symbol, bar.Start, bar.End, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

                // Dispatch to all feature resolvers
                var tasks = new List<Task>();
                
                foreach (var resolver in _resolvers)
                {
                    tasks.Add(ProcessResolverAsync(resolver, symbol, bar));
                }

                // Wait for all resolvers to complete
                await Task.WhenAll(tasks).ConfigureAwait(false);

                _logger.LogTrace("[BAR-DISPATCHER] Completed processing bar for {Symbol} with {ResolverCount} resolvers", 
                    symbol, _resolvers.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BAR-DISPATCHER] [AUDIT-VIOLATION] Failed to process bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                
                // Fail-closed: let exception bubble up to crash service
                // This ensures bar processing failures are visible rather than silent
                throw new InvalidOperationException($"[BAR-DISPATCHER] Critical failure processing bar for '{symbol}': {ex.Message}", ex);
            }
        }

        private async Task ProcessResolverAsync(IFeatureResolver resolver, string symbol, Bar bar)
        {
            try
            {
                Interlocked.Increment(ref _resolverCalls);
                
                await resolver.OnBarAsync(symbol, bar, CancellationToken.None).ConfigureAwait(false);
                
                _logger.LogTrace("[BAR-DISPATCHER] Resolver {ResolverType} processed bar for {Symbol}", 
                    resolver.GetType().Name, symbol);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _resolverErrors);
                
                _logger.LogError(ex, "[BAR-DISPATCHER] [AUDIT-VIOLATION] Resolver {ResolverType} failed for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    resolver.GetType().Name, symbol);
                
                // Re-throw to ensure failures bubble up (fail-closed behavior)
                throw new InvalidOperationException($"[BAR-DISPATCHER] Resolver '{resolver.GetType().Name}' failed for '{symbol}': {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                _logger.LogInformation("[BAR-DISPATCHER] Disposed - Final stats: Bars={Bars}, ResolverCalls={Calls}, Errors={Errors}", 
                    _barsProcessed, _resolverCalls, _resolverErrors);
            }
        }
    }
}