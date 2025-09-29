using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Abstractions;

namespace TradingBot.S7
{
    /// <summary>
    /// Simple S7 service update bridge - demonstrates S7 service functionality
    /// In production, this would connect to real market data feeds
    /// </summary>
    public class S7MarketDataBridge : IHostedService, IDisposable
    {
        private readonly ILogger<S7MarketDataBridge> _logger;
        private readonly IServiceProvider _serviceProvider;
        private IS7Service? _s7Service;
        private S7Configuration? _config;
        private Timer? _updateTimer;
        private bool _disposed;
        private readonly Random _random = new();

        public S7MarketDataBridge(
            ILogger<S7MarketDataBridge> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Get services (they might not be available during DI construction)
                _s7Service = _serviceProvider.GetService<IS7Service>();
                _config = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<S7Configuration>>()?.Value;

                if (_s7Service == null)
                {
                    _logger.LogWarning("[S7-BRIDGE] S7 service not available - S7 data feed disabled");
                    return Task.CompletedTask;
                }

                if (_config == null || !_config.Enabled)
                {
                    _logger.LogInformation("[S7-BRIDGE] S7 service disabled in configuration");
                    return Task.CompletedTask;
                }

                // Start a timer to simulate market data updates for demonstration
                // In production, this would connect to real market data feeds
                _updateTimer = new Timer(UpdateMarketDataCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(_config.BarTimeframeMinutes * 60));

                _logger.LogInformation("[S7-BRIDGE] S7 market data bridge started successfully");
                _logger.LogInformation("[S7-BRIDGE] Monitoring symbols: {Symbols}", string.Join(", ", _config.Symbols));
                _logger.LogInformation("[S7-BRIDGE] Update interval: {Minutes} minutes", _config.BarTimeframeMinutes);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Failed to start S7 market data bridge");
                return Task.FromException(ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _updateTimer?.Dispose();
                _logger.LogInformation("[S7-BRIDGE] S7 market data bridge stopped");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Error stopping S7 market data bridge");
                return Task.FromException(ex);
            }
        }

        private async void UpdateMarketDataCallback(object? state)
        {
            try
            {
                if (_s7Service == null || _config == null)
                    return;

                var timestamp = DateTime.UtcNow;

                // Simulate price updates for configured symbols
                foreach (var symbol in _config.Symbols)
                {
                    // Generate realistic price movements (simplified simulation)
                    var basePrice = symbol == "ES" ? 4500m : 15000m; // Typical ES and NQ price levels
                    var priceVariation = (decimal)(_random.NextDouble() - 0.5) * 10; // ±5 points variation
                    var simulatedPrice = basePrice + priceVariation;

                    // Update S7 service with simulated price data
                    await _s7Service.UpdateAsync(symbol, simulatedPrice, timestamp).ConfigureAwait(false);

                    _logger.LogDebug("[S7-BRIDGE] Updated S7 service: {Symbol} @ {Price} ({Timestamp})", 
                        symbol, simulatedPrice, timestamp);
                }

                // Log S7 status periodically
                if (_s7Service.IsReady())
                {
                    var snapshot = _s7Service.GetCurrentSnapshot();
                    _logger.LogInformation("[S7-BRIDGE] S7 Snapshot - Leader: {Leader}, Coherence: {Coherence:F3}, Actionable: {Actionable}",
                        snapshot.DominantLeader, snapshot.CrossSymbolCoherence, snapshot.IsActionable);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Error in market data update callback");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer?.Dispose();
                _disposed = true;
            }
        }
    }
}