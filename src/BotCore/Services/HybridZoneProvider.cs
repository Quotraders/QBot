using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zones;

namespace BotCore.Services
{
    /// <summary>
    /// Hybrid zone provider that intelligently selects between legacy and modern zone sources
    /// Implements fallback logic and disagreement detection as specified
    /// </summary>
    public sealed class HybridZoneProvider : IZoneProvider
    {
        private readonly LegacyZoneProvider _legacyProvider;
        private readonly ModernZoneProvider _modernProvider;
        private readonly ILogger<HybridZoneProvider> _logger;
        private readonly HybridZoneConfiguration _config;
        private readonly ZoneProviderMetrics _metrics = new();

        public HybridZoneProvider(
            LegacyZoneProvider legacyProvider,
            ModernZoneProvider modernProvider,
            IConfiguration configuration,
            ILogger<HybridZoneProvider> logger)
        {
            _legacyProvider = legacyProvider ?? throw new ArgumentNullException(nameof(legacyProvider));
            _modernProvider = modernProvider ?? throw new ArgumentNullException(nameof(modernProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = LoadConfiguration(configuration);
        }

        public async Task<ZoneProviderResult> GetZoneSnapshotAsync(string symbol)
        {
            var startTime = DateTime.UtcNow;
            _metrics.HybridRequests++;

            try
            {
                // Get data from both providers concurrently
                var legacyTask = _legacyProvider.GetZoneSnapshotAsync(symbol);
                var modernTask = _modernProvider.GetZoneSnapshotAsync(symbol);

                await Task.WhenAll(legacyTask, modernTask).ConfigureAwait(false);

                var legacyResult = await legacyTask.ConfigureAwait(false);
                var modernResult = await modernTask.ConfigureAwait(false);

                // Apply hybrid selection logic
                var result = SelectBestProvider(legacyResult, modernResult, symbol);
                
                var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                UpdateMetrics(latency);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HYBRID-ZONE-PROVIDER] Error in hybrid zone provider for {Symbol}", symbol);
                return new ZoneProviderResult
                {
                    Source = ZoneSource.Unavailable,
                    IsStale = true,
                    ErrorReason = ex.Message,
                    Timestamp = startTime
                };
            }
        }

        public ZoneProviderMetrics GetMetrics()
        {
            // Combine metrics from all providers
            var legacyMetrics = _legacyProvider.GetMetrics();
            var modernMetrics = _modernProvider.GetMetrics();
            
            return new ZoneProviderMetrics
            {
                LegacyRequests = legacyMetrics.LegacyRequests,
                ModernRequests = modernMetrics.ModernRequests,
                HybridRequests = _metrics.HybridRequests,
                DisagreementCount = _metrics.DisagreementCount,
                AverageLatencyMs = _metrics.AverageLatencyMs,
                LastUpdate = _metrics.LastUpdate
            };
        }

        private ZoneProviderResult SelectBestProvider(ZoneProviderResult legacyResult, ZoneProviderResult modernResult, string symbol)
        {
            // Rule 1: When legacy is fresh → Hybrid returns legacy
            if (legacyResult.IsSuccess && !legacyResult.IsStale)
            {
                _logger.LogDebug("[HYBRID-ZONE] Using fresh legacy data for {Symbol}", symbol);
                return legacyResult with { Source = ZoneSource.Hybrid };
            }

            // Rule 2: When legacy is stale → Hybrid returns modern
            if (legacyResult.IsStale && modernResult.IsSuccess)
            {
                _logger.LogDebug("[HYBRID-ZONE] Legacy stale, using modern data for {Symbol}", symbol);
                return modernResult with { Source = ZoneSource.Hybrid };
            }

            // Rule 3: Check for disagreement when both are available
            if (legacyResult.IsSuccess && modernResult.IsSuccess)
            {
                var disagreement = DetectDisagreement(legacyResult.Snapshot!, modernResult.Snapshot!, symbol);
                
                if (disagreement.HasDisagreement)
                {
                    _metrics.DisagreementCount++;
                    
                    if (_config.FailClosed)
                    {
                        // Rule 3.1: When both disagree beyond AGREE_TICKS and FAIL_CLOSED=1 → policy holds entry
                        _logger.LogWarning("[HYBRID-ZONE] Zone disagreement detected for {Symbol}, failing closed. Disagreement: {DisagreementTicks} ticks", 
                            symbol, disagreement.DisagreementTicks);
                        
                        return new ZoneProviderResult
                        {
                            Source = ZoneSource.Disagree,
                            IsStale = false,
                            ErrorReason = $"Zone disagreement: {disagreement.DisagreementTicks:F2} ticks",
                            Timestamp = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        // Use modern data when disagreement but not failing closed
                        _logger.LogInformation("[HYBRID-ZONE] Zone disagreement detected for {Symbol}, using modern data. Disagreement: {DisagreementTicks} ticks", 
                            symbol, disagreement.DisagreementTicks);
                        return modernResult with { Source = ZoneSource.Hybrid };
                    }
                }

                // No disagreement, prefer legacy for consistency
                return legacyResult with { Source = ZoneSource.Hybrid };
            }

            // Fallback: use whichever is available
            if (modernResult.IsSuccess)
            {
                return modernResult with { Source = ZoneSource.Hybrid };
            }
            
            if (legacyResult.IsSuccess)
            {
                return legacyResult with { Source = ZoneSource.Hybrid };
            }

            // Both failed
            return new ZoneProviderResult
            {
                Source = ZoneSource.Unavailable,
                IsStale = true,
                ErrorReason = "Both legacy and modern providers failed",
                Timestamp = DateTime.UtcNow
            };
        }

        private DisagreementResult DetectDisagreement(ZoneSnapshot legacySnapshot, ZoneSnapshot modernSnapshot, string symbol)
        {
            const decimal DefaultTickSize = 0.25m; // ES/NQ tick size
            var tickSize = GetTickSize(symbol);

            double maxDisagreement = 0.0;

            // Check supply zone disagreement
            if (legacySnapshot.NearestSupply != null && modernSnapshot.NearestSupply != null)
            {
                var legacySupplyPrice = legacySnapshot.NearestSupply.Mid;
                var modernSupplyPrice = modernSnapshot.NearestSupply.Mid;
                var supplyDisagreement = Math.Abs((double)(legacySupplyPrice - modernSupplyPrice) / (double)tickSize);
                maxDisagreement = Math.Max(maxDisagreement, supplyDisagreement);
            }

            // Check demand zone disagreement  
            if (legacySnapshot.NearestDemand != null && modernSnapshot.NearestDemand != null)
            {
                var legacyDemandPrice = legacySnapshot.NearestDemand.Mid;
                var modernDemandPrice = modernSnapshot.NearestDemand.Mid;
                var demandDisagreement = Math.Abs((double)(legacyDemandPrice - modernDemandPrice) / (double)tickSize);
                maxDisagreement = Math.Max(maxDisagreement, demandDisagreement);
            }

            return new DisagreementResult
            {
                HasDisagreement = maxDisagreement > _config.AgreementTickThreshold,
                DisagreementTicks = maxDisagreement
            };
        }

        private static decimal GetTickSize(string symbol)
        {
            return symbol switch
            {
                "ES" => 0.25m,
                "NQ" => 0.25m,
                "YM" => 1.00m,
                _ => 0.25m
            };
        }

        private void UpdateMetrics(double latencyMs)
        {
            _metrics.AverageLatencyMs = (_metrics.AverageLatencyMs + latencyMs) / 2.0;
            _metrics.LastUpdate = DateTime.UtcNow;
        }

        private static HybridZoneConfiguration LoadConfiguration(IConfiguration configuration)
        {
            const int DefaultTtlSeconds = 300; // 5 minutes
            const double DefaultAgreeTickThreshold = 2.0; // 2 ticks
            const bool DefaultFailClosed = true;

            return new HybridZoneConfiguration
            {
                Mode = GetZoneMode(configuration),
                TtlSeconds = configuration.GetValue("ZONES_TTL_SEC", DefaultTtlSeconds),
                AgreementTickThreshold = configuration.GetValue("ZONES_AGREE_TICKS", DefaultAgreeTickThreshold),
                FailClosed = configuration.GetValue("ZONES_FAIL_CLOSED", DefaultFailClosed)
            };
        }

        private static ZoneMode GetZoneMode(IConfiguration configuration)
        {
            var mode = configuration.GetValue("ZONES_MODE", "hybrid");
            return mode.ToLowerInvariant() switch
            {
                "legacy" => ZoneMode.Legacy,
                "modern" => ZoneMode.Modern,
                "hybrid" => ZoneMode.Hybrid,
                _ => ZoneMode.Hybrid
            };
        }

        private sealed class DisagreementResult
        {
            public bool HasDisagreement { get; set; }
            public double DisagreementTicks { get; set; }
        }
    }

    /// <summary>
    /// Configuration for hybrid zone provider
    /// </summary>
    public sealed class HybridZoneConfiguration
    {
        public ZoneMode Mode { get; set; }
        public int TtlSeconds { get; set; }
        public double AgreementTickThreshold { get; set; }
        public bool FailClosed { get; set; }
    }

    /// <summary>
    /// Zone provider mode
    /// </summary>
    public enum ZoneMode
    {
        Legacy,
        Modern, 
        Hybrid
    }
}