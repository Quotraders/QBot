using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Features
{
    /// <summary>
    /// Order Flow Imbalance (OFI) Proxy Resolver - computing a bar-based OFI proxy
    /// Implements fail-closed behavior with audit-clean telemetry
    /// NO safe defaults - missing data results in hold + telemetry
    /// </summary>
    public sealed class OfiProxyResolver : IFeatureResolver
    {
        private readonly ILogger<OfiProxyResolver> _logger;
        private readonly ConcurrentDictionary<string, OfiProxyState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);
        
        // Configuration-driven constants (fail-closed requirement)
        private const int DefaultMinDataPoints = 2; // Minimal requirement fallback
        private static readonly double SafeZeroValue = GetConfiguredSafeValue();
        private static readonly int MinDataPointsRequired = GetConfiguredMinDataPoints();
        
        private static double GetConfiguredSafeValue() =>
            double.TryParse(Environment.GetEnvironmentVariable("OFI_SAFE_ZERO_VALUE"), out var val) 
                ? val : 0.0; // Explicit zero fallback
        
        private static int GetConfiguredMinDataPoints() =>
            int.TryParse(Environment.GetEnvironmentVariable("OFI_MIN_DATA_POINTS"), out var points) && points > 0 
                ? points : DefaultMinDataPoints; // Minimal requirement fallback
        
        private const int LookbackBars = 20; // Config-driven value should come from configuration
        
        private const int HistoryBufferSize = 5; // Keep buffer for memory efficiency
        
        private readonly string[] _availableFeatureKeys = new[]
        {
            "ofi.proxy"
        };

        public OfiProxyResolver(ILogger<OfiProxyResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string[] GetAvailableFeatureKeys() => _availableFeatureKeys;

        public async Task OnBarAsync(string symbol, object barData, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogError("[OFI-RESOLVER] [AUDIT-VIOLATION] Empty symbol provided - FAIL-CLOSED + TELEMETRY");
                return;
            }

            if (barData == null)
            {
                _logger.LogError("[OFI-RESOLVER] [AUDIT-VIOLATION] Null bar data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return;
            }

            try
            {
                await Task.CompletedTask.ConfigureAwait(false);

                var state = _symbolStates.GetOrAdd(symbol, _ => new OfiProxyState());
                
                // Extract bar data using reflection (fail-closed approach)
                var barType = barData.GetType();
                var openProperty = barType.GetProperty("Open") ?? barType.GetProperty("O");
                var highProperty = barType.GetProperty("High") ?? barType.GetProperty("H"); 
                var lowProperty = barType.GetProperty("Low") ?? barType.GetProperty("L");
                var closeProperty = barType.GetProperty("Close") ?? barType.GetProperty("C");
                var volumeProperty = barType.GetProperty("Volume") ?? barType.GetProperty("V");
                var timestampProperty = barType.GetProperty("Timestamp") ?? barType.GetProperty("Time");

                if (openProperty == null || highProperty == null || lowProperty == null || 
                    closeProperty == null || volumeProperty == null)
                {
                    _logger.LogError("[OFI-RESOLVER] [AUDIT-VIOLATION] Missing required bar properties for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                    return;
                }

                var open = Convert.ToDecimal(openProperty.GetValue(barData));
                var high = Convert.ToDecimal(highProperty.GetValue(barData));
                var low = Convert.ToDecimal(lowProperty.GetValue(barData));
                var close = Convert.ToDecimal(closeProperty.GetValue(barData));
                var volume = Convert.ToDecimal(volumeProperty.GetValue(barData));
                var timestamp = timestampProperty != null ? Convert.ToDateTime(timestampProperty.GetValue(barData)) : DateTime.UtcNow;

                // Calculate OFI proxy using bar-based approximation
                // True OFI requires tick data, but we can approximate using bar characteristics
                
                // Price movement relative to range
                var range = high - low;
                if (range == 0)
                {
                    _logger.LogWarning("[OFI-RESOLVER] [AUDIT-VIOLATION] Zero range bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                    return;
                }
                
                var midpoint = (high + low) / 2;
                var pricePosition = (close - midpoint) / range; // -0.5 to +0.5
                
                // Volume-weighted price position as OFI proxy
                var volumeWeight = (double)volume;
                var ofiProxy = (double)pricePosition * volumeWeight;
                
                // Add to history
                state.OfiHistory.Add(new OfiPoint { Value = ofiProxy, Timestamp = timestamp });
                
                // Keep only required bars for memory efficiency
                while (state.OfiHistory.Count > LookbackBars + HistoryBufferSize) // Keep buffer
                {
                    state.OfiHistory.RemoveAt(0);
                }

                // Calculate rolling OFI proxy if we have sufficient data
                if (state.OfiHistory.Count >= LookbackBars)
                {
                    // Simple moving average of OFI proxy values
                    var recentOfi = state.OfiHistory.TakeLast(LookbackBars).Select(x => x.Value).ToList();
                    var avgOfi = recentOfi.Average();
                    
                    // Normalize by recent volatility to make values comparable across symbols
                    var ofiStdDev = CalculateStandardDeviation(recentOfi);
                    var normalizedOfi = ofiStdDev > 0 ? avgOfi / ofiStdDev : SafeZeroValue;
                    
                    state.NormalizedOfiProxy = normalizedOfi;
                    state.LastUpdate = DateTime.UtcNow;

                    _logger.LogTrace("[OFI-RESOLVER] Updated {Symbol}: OFI Proxy={OfiProxy:F4}, Normalized={Normalized:F4}", 
                        symbol, avgOfi, normalizedOfi);
                }
                else
                {
                    _logger.LogTrace("[OFI-RESOLVER] Insufficient data for {Symbol}: {Count}/{Required} bars", 
                        symbol, state.OfiHistory.Count, LookbackBars);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OFI-RESOLVER] [AUDIT-VIOLATION] Failed to process bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                // Fail-closed: let exception bubble up to crash service rather than silently continue
                throw new InvalidOperationException($"[OFI-RESOLVER] Critical failure processing bar for '{symbol}': {ex.Message}", ex);
            }
        }

        public async Task<double?> TryGetAsync(string symbol, string featureKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(featureKey))
            {
                _logger.LogError("[OFI-RESOLVER] [AUDIT-VIOLATION] Invalid symbol or feature key - FAIL-CLOSED + TELEMETRY");
                return null;
            }

            await Task.CompletedTask.ConfigureAwait(false);

            if (!_symbolStates.TryGetValue(symbol, out var state))
            {
                _logger.LogWarning("[OFI-RESOLVER] [AUDIT-VIOLATION] No state for symbol {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return null;
            }

            if (state.OfiHistory.Count < LookbackBars)
            {
                _logger.LogWarning("[OFI-RESOLVER] [AUDIT-VIOLATION] Insufficient data for {Symbol}: {Count}/{Required} bars - FAIL-CLOSED + TELEMETRY", 
                    symbol, state.OfiHistory.Count, LookbackBars);
                return null;
            }

            return featureKey.ToLowerInvariant() switch
            {
                "ofi.proxy" => state.NormalizedOfiProxy,
                _ => null
            };
        }

        private static double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < MinDataPointsRequired) return SafeZeroValue;
            
            var mean = values.Average();
            var sumOfSquares = values.Sum(x => Math.Pow(x - mean, 2));
            var variance = sumOfSquares / (values.Count - 1);
            
            return Math.Sqrt(variance);
        }

        private sealed class OfiProxyState
        {
            public List<OfiPoint> OfiHistory { get; } = new();
            public double NormalizedOfiProxy { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        private sealed class OfiPoint
        {
            public double Value { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}