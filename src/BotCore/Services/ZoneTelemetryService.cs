using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Zone telemetry service for emitting legacy metric names with new hybrid tags
    /// Maintains backward compatibility while adding hybrid zone awareness
    /// </summary>
    public interface IZoneTelemetryService
    {
        /// <summary>
        /// Emit legacy metric names for supervisors with hybrid source tags
        /// </summary>
        void EmitZoneMetrics(string symbol, ZoneProviderResult result);

        /// <summary>
        /// Emit zone disagreement metrics
        /// </summary>
        void EmitDisagreementMetrics(string symbol, double disagreementTicks);

        /// <summary>
        /// Emit zone freshness metrics
        /// </summary>
        void EmitFreshnessMetrics(string symbol, ZoneSource source, double freshnessSeconds);

        /// <summary>
        /// Emit rejected entry due to zone disagreement
        /// </summary>
        void EmitRejectedEntry(string symbol, string reason);
    }

    /// <summary>
    /// Production implementation of zone telemetry service
    /// </summary>
    public sealed class ZoneTelemetryService : IZoneTelemetryService
    {
        private readonly ILogger<ZoneTelemetryService> _logger;
        private readonly Dictionary<string, object> _recentMetrics = new();
        private readonly object _metricsLock = new();

        public ZoneTelemetryService(ILogger<ZoneTelemetryService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void EmitZoneMetrics(string symbol, ZoneProviderResult result)
        {
            if (result.Snapshot == null) return;

            try
            {
                var snapshot = result.Snapshot;
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = symbol,
                    ["zone.source"] = GetSourceTag(result.Source),
                    ["zone.freshness_s"] = (DateTime.UtcNow - result.Timestamp).TotalSeconds
                };

                // Legacy metric names for backward compatibility
                EmitMetric("zone.nearest_support", (double)(snapshot.NearestDemand?.Mid ?? 0), tags);
                EmitMetric("zone.nearest_resistance", (double)(snapshot.NearestSupply?.Mid ?? 0), tags);
                EmitMetric("zone.distance_to_support_atr", snapshot.DistToDemandAtr, tags);
                EmitMetric("zone.distance_to_resistance_atr", snapshot.DistToSupplyAtr, tags);

                // Additional zone metrics
                EmitMetric("zone.breakout_score", snapshot.BreakoutScore, tags);
                EmitMetric("zone.pressure", snapshot.ZonePressure, tags);

                // Zone count metrics
                var zoneCount = (snapshot.NearestDemand != null ? 1 : 0) + (snapshot.NearestSupply != null ? 1 : 0);
                EmitMetric("zone.active_zones", zoneCount, tags);

                _logger.LogTrace("[ZONE-TELEMETRY] Emitted zone metrics for {Symbol} from {Source}", 
                    symbol, result.Source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-TELEMETRY] Error emitting zone metrics for {Symbol}", symbol);
            }
        }

        public void EmitDisagreementMetrics(string symbol, double disagreementTicks)
        {
            try
            {
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = symbol,
                    ["zone.disagreement_ticks"] = disagreementTicks
                };

                EmitMetric("zone.disagreement", 1, tags);
                EmitMetric("zone.disagreement_magnitude", disagreementTicks, tags);

                _logger.LogInformation("[ZONE-TELEMETRY] Emitted disagreement metrics for {Symbol}: {DisagreementTicks} ticks", 
                    symbol, disagreementTicks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-TELEMETRY] Error emitting disagreement metrics for {Symbol}", symbol);
            }
        }

        public void EmitFreshnessMetrics(string symbol, ZoneSource source, double freshnessSeconds)
        {
            try
            {
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = symbol,
                    ["zone.source"] = GetSourceTag(source),
                    ["zone.freshness_s"] = freshnessSeconds
                };

                EmitMetric("zone.data_freshness", freshnessSeconds, tags);

                // Emit staleness indicator
                var isStale = freshnessSeconds > 300; // 5 minutes
                EmitMetric("zone.data_stale", isStale ? 1 : 0, tags);

                _logger.LogTrace("[ZONE-TELEMETRY] Emitted freshness metrics for {Symbol}: {FreshnessSeconds}s", 
                    symbol, freshnessSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-TELEMETRY] Error emitting freshness metrics for {Symbol}", symbol);
            }
        }

        public void EmitRejectedEntry(string symbol, string reason)
        {
            try
            {
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = symbol,
                    ["reason"] = reason
                };

                // Legacy metric name for backward compatibility
                EmitMetric("risk.rejected_entries", 1, tags);

                _logger.LogInformation("[ZONE-TELEMETRY] Emitted rejected entry for {Symbol}: {Reason}", 
                    symbol, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-TELEMETRY] Error emitting rejected entry metrics for {Symbol}", symbol);
            }
        }

        private void EmitMetric(string metricName, object value, Dictionary<string, object> tags)
        {
            lock (_metricsLock)
            {
                var key = $"{metricName}_{string.Join("_", tags.Values)}";
                _recentMetrics[key] = new { 
                    Name = metricName, 
                    Value = value, 
                    Tags = new Dictionary<string, object>(tags), 
                    Timestamp = DateTime.UtcNow 
                };

                // Keep only recent metrics (last 1000 entries)
                if (_recentMetrics.Count > 1000)
                {
                    var oldestKey = "";
                    var oldestTime = DateTime.MaxValue;
                    foreach (var kvp in _recentMetrics)
                    {
                        if (kvp.Value is { } metric)
                        {
                            var metricObj = metric as dynamic;
                            if (metricObj?.Timestamp < oldestTime)
                            {
                                oldestTime = metricObj.Timestamp;
                                oldestKey = kvp.Key;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(oldestKey))
                    {
                        _recentMetrics.Remove(oldestKey);
                    }
                }
            }

            // In production, this would emit to your metrics system (StatsD, DataDog, etc.)
            _logger.LogTrace("[ZONE-TELEMETRY] Metric: {MetricName}={Value} {Tags}", 
                metricName, value, string.Join(",", tags));
        }

        private static string GetSourceTag(ZoneSource source)
        {
            return source switch
            {
                ZoneSource.Modern => "modern", 
                ZoneSource.Unavailable => "unavailable",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Get recent metrics for debugging/testing
        /// </summary>
        public IReadOnlyDictionary<string, object> GetRecentMetrics()
        {
            lock (_metricsLock)
            {
                return new Dictionary<string, object>(_recentMetrics);
            }
        }
    }
}