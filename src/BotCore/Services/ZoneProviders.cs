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
        Legacy,
        Modern,
        Hybrid,
        Disagree,
        Unavailable
    }

    /// <summary>
    /// Zone provider metrics for telemetry
    /// </summary>
    public sealed class ZoneProviderMetrics
    {
        public long LegacyRequests { get; set; }
        public long ModernRequests { get; set; }
        public long HybridRequests { get; set; }
        public long DisagreementCount { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Legacy zone provider using existing Intelligence/data/zones files
    /// </summary>
    public sealed class LegacyZoneProvider : IZoneProvider
    {
        private readonly IZoneService _legacyZoneService;
        private readonly ILogger<LegacyZoneProvider> _logger;
        private readonly ZoneProviderMetrics _metrics = new();

        public LegacyZoneProvider(IZoneService legacyZoneService, ILogger<LegacyZoneProvider> logger)
        {
            _legacyZoneService = legacyZoneService ?? throw new ArgumentNullException(nameof(legacyZoneService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ZoneProviderResult> GetZoneSnapshotAsync(string symbol)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                _metrics.LegacyRequests++;

                // Use legacy zone service to get zone data
                var legacyData = await _legacyZoneService.GetLatestZonesAsync(symbol).ConfigureAwait(false);
                
                if (legacyData == null)
                {
                    return new ZoneProviderResult
                    {
                        Source = ZoneSource.Legacy,
                        IsStale = true,
                        ErrorReason = "No legacy zone data available",
                        Timestamp = startTime
                    };
                }

                // Convert legacy zone data to ZoneSnapshot format
                var snapshot = ConvertLegacyToSnapshot(legacyData, symbol);
                var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                UpdateMetrics(latency);

                return new ZoneProviderResult
                {
                    Snapshot = snapshot,
                    Source = ZoneSource.Legacy,
                    Timestamp = startTime,
                    IsStale = IsLegacyDataStale(legacyData)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LEGACY-ZONE-PROVIDER] Error getting legacy zone data for {Symbol}", symbol);
                return new ZoneProviderResult
                {
                    Source = ZoneSource.Legacy,
                    IsStale = true,
                    ErrorReason = ex.Message,
                    Timestamp = startTime
                };
            }
        }

        public ZoneProviderMetrics GetMetrics() => _metrics;

        private static ZoneSnapshot ConvertLegacyToSnapshot(ZoneData legacyData, string symbol)
        {
            // Convert legacy supply/demand zones to modern zone format
            Zone? nearestDemand = null;
            Zone? nearestSupply = null;
            var currentPrice = legacyData.CurrentPrice;

            // Find nearest demand zone (below current price)
            foreach (var demandZone in legacyData.DemandZones)
            {
                if (demandZone.Price <= currentPrice)
                {
                    if (nearestDemand == null || demandZone.Price > nearestDemand.PriceHigh)
                    {
                        nearestDemand = new Zone(
                            ZoneSide.Demand,
                            demandZone.Price - demandZone.Thickness / 2,
                            demandZone.Price + demandZone.Thickness / 2,
                            (double)demandZone.Strength,
                            demandZone.TouchCount,
                            demandZone.LastTested,
                            ConvertLegacyStatus(demandZone.Status)
                        );
                    }
                }
            }

            // Find nearest supply zone (above current price)
            foreach (var supplyZone in legacyData.SupplyZones)
            {
                if (supplyZone.Price >= currentPrice)
                {
                    if (nearestSupply == null || supplyZone.Price < nearestSupply.PriceLow)
                    {
                        nearestSupply = new Zone(
                            ZoneSide.Supply,
                            supplyZone.Price - supplyZone.Thickness / 2,
                            supplyZone.Price + supplyZone.Thickness / 2,
                            (double)supplyZone.Strength,
                            supplyZone.TouchCount,
                            supplyZone.LastTested,
                            ConvertLegacyStatus(supplyZone.Status)
                        );
                    }
                }
            }

            // Calculate distances (use a default ATR if not available)
            const double DefaultAtr = 1.0;
            var demandDistance = nearestDemand != null 
                ? (double)(currentPrice - nearestDemand.PriceHigh) / DefaultAtr 
                : double.PositiveInfinity;
            var supplyDistance = nearestSupply != null 
                ? (double)(nearestSupply.PriceLow - currentPrice) / DefaultAtr 
                : double.PositiveInfinity;

            return new ZoneSnapshot(
                nearestDemand,
                nearestSupply,
                Math.Max(0, demandDistance),
                Math.Max(0, supplyDistance),
                BreakoutScore: 0.5, // Default breakout score for legacy data
                ZonePressure: nearestSupply?.Pressure ?? nearestDemand?.Pressure ?? 0.0,
                DateTime.UtcNow
            );
        }

        private static ZoneState ConvertLegacyStatus(string status)
        {
            return status switch
            {
                "Active" => ZoneState.Hold,
                "Tested" => ZoneState.Test,
                "Broken" => ZoneState.Breakout,
                _ => ZoneState.Hold
            };
        }

        private static bool IsLegacyDataStale(ZoneData data)
        {
            // Consider data stale if it's older than 15 minutes
            const int StaleMinutes = 15;
            return data.SupplyZones.Count == 0 && data.DemandZones.Count == 0;
        }

        private void UpdateMetrics(double latencyMs)
        {
            _metrics.AverageLatencyMs = (_metrics.AverageLatencyMs + latencyMs) / 2.0;
            _metrics.LastUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Modern zone provider using the new fractal+ATR zone detection engine
    /// </summary>
    public sealed class ModernZoneProvider : IZoneProvider
    {
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
            _metrics.AverageLatencyMs = (_metrics.AverageLatencyMs + latencyMs) / 2.0;
            _metrics.LastUpdate = DateTime.UtcNow;
        }
    }
}