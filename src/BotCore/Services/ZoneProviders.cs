using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zones;

namespace BotCore.Services
{
    /// <summary>
    /// Hybrid zone provider interface supporting legacy and modern zone sources
    /// </summary>
    public interface IZoneProvider
    {
        Task<ZoneProviderResult> GetZoneSnapshotAsync(string symbol);
        ZoneProviderMetrics GetMetrics();
    }

    /// <summary>
    /// Result from zone provider with source information
    /// </summary>
    public sealed class ZoneProviderResult
    {
        public ZoneSnapshot? Snapshot { get; set; }
        public ZoneSource Source { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsStale { get; set; }
        public string? ErrorReason { get; set; }
        public bool IsSuccess => Snapshot != null && !IsStale && string.IsNullOrEmpty(ErrorReason);
    }

    /// <summary>
    /// Zone source enumeration
    /// </summary>
    public enum ZoneSource
    {
        Modern,
        Unavailable
    }

    /// <summary>
    /// Zone provider metrics for telemetry
    /// </summary>
    public sealed class ZoneProviderMetrics
    {
        public long ModernRequests { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Modern zone provider using the new fractal+ATR zone detection engine
    /// </summary>
    public sealed class ModernZoneProvider : IZoneProvider
    {
        // Latency averaging constants
        private const double LatencyAveragingFactor = 2.0;
        
        private readonly Zones.IZoneService _modernZoneService;
        private readonly ILogger<ModernZoneProvider> _logger;
        private readonly ZoneProviderMetrics _metrics = new();

        public ModernZoneProvider(Zones.IZoneService modernZoneService, ILogger<ModernZoneProvider> logger)
        {
            _modernZoneService = modernZoneService ?? throw new ArgumentNullException(nameof(modernZoneService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ZoneProviderResult> GetZoneSnapshotAsync(string symbol)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                _metrics.ModernRequests++;

                // Get snapshot from modern zone service (this is synchronous in the current implementation)
                var snapshot = await Task.FromResult(_modernZoneService.GetSnapshot(symbol)).ConfigureAwait(false);
                
                var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                UpdateMetrics(latency);

                return new ZoneProviderResult
                {
                    Snapshot = snapshot,
                    Source = ZoneSource.Modern,
                    Timestamp = startTime,
                    IsStale = IsModernDataStale(snapshot)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MODERN-ZONE-PROVIDER] Error getting modern zone data for {Symbol}", symbol);
                return new ZoneProviderResult
                {
                    Source = ZoneSource.Modern,
                    IsStale = true,
                    ErrorReason = ex.Message,
                    Timestamp = startTime
                };
            }
        }

        public ZoneProviderMetrics GetMetrics() => _metrics;

        private static bool IsModernDataStale(ZoneSnapshot snapshot)
        {
            // Consider data stale if it's older than 5 minutes (modern system should be more current)
            const int StaleMinutes = 5;
            return (DateTime.UtcNow - snapshot.Utc).TotalMinutes > StaleMinutes;
        }

        private void UpdateMetrics(double latencyMs)
        {
            _metrics.AverageLatencyMs = (_metrics.AverageLatencyMs + latencyMs) / LatencyAveragingFactor;
            _metrics.LastUpdate = DateTime.UtcNow;
        }
    }
}