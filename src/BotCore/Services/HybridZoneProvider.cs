using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BotCore.Services
{
    /// <summary>
    /// Modern-only zone provider - simplified from hybrid implementation
    /// </summary>
    public sealed class HybridZoneProvider : IZoneProvider
    {
        // Moving average smoothing factor
        private const double MovingAverageSmoothingFactor = 2.0;
        
        private readonly ModernZoneProvider _modernProvider;
        private readonly ILogger<HybridZoneProvider> _logger;
        private readonly ZoneProviderMetrics _metrics = new();

        // LoggerMessage delegates for high-performance logging
        private static readonly Action<ILogger, string, Exception?> LogModernZoneProviderError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(1, nameof(GetZoneSnapshotAsync)),
                "[MODERN-ZONE-PROVIDER] Error in modern zone provider for {Symbol}");

        public HybridZoneProvider(
            ModernZoneProvider modernProvider,
            ILogger<HybridZoneProvider> logger)
        {
            _modernProvider = modernProvider ?? throw new ArgumentNullException(nameof(modernProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ZoneProviderResult> GetZoneSnapshotAsync(string symbol)
        {
            var startTime = DateTime.UtcNow;
            _metrics.ModernRequests++;

            try
            {
                // Use only the modern provider
                var result = await _modernProvider.GetZoneSnapshotAsync(symbol).ConfigureAwait(false);
                
                var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                UpdateMetrics(latency);

                return result;
            }
            catch (Exception ex)
            {
                LogModernZoneProviderError(_logger, symbol, ex);
                return new ZoneProviderResult
                {
                    Source = ZoneSource.Unavailable,
                    IsStale = true,
                    ErrorReason = ex.Message,
                    Timestamp = startTime
                };
            }
        }

        public ZoneProviderMetrics GetMetrics() => _metrics;

        private void UpdateMetrics(double latencyMs)
        {
            _metrics.AverageLatencyMs = (_metrics.AverageLatencyMs + latencyMs) / MovingAverageSmoothingFactor;
            _metrics.LastUpdate = DateTime.UtcNow;
        }
    }
}