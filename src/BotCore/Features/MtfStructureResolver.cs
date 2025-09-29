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
    /// Multi-Timeframe Structure Resolver - captures slope alignment between short and medium horizons
    /// Implements fail-closed behavior with audit-clean telemetry
    /// NO safe defaults - missing data results in hold + telemetry
    /// </summary>
    public sealed class MtfStructureResolver : IFeatureResolver
    {
        private readonly ILogger<MtfStructureResolver> _logger;
        private readonly ConcurrentDictionary<string, MtfStructureState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);
        
        private const int ShortHorizonBars = 10;
        private const int MediumHorizonBars = 30;
        
        private readonly string[] _availableFeatureKeys = new[]
        {
            "mtf.align",
            "mtf.bias"
        };

        public MtfStructureResolver(ILogger<MtfStructureResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string[] GetAvailableFeatureKeys() => _availableFeatureKeys;

        public async Task OnBarAsync(string symbol, object barData, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogError("[MTF-RESOLVER] [AUDIT-VIOLATION] Empty symbol provided - FAIL-CLOSED + TELEMETRY");
                return;
            }

            if (barData == null)
            {
                _logger.LogError("[MTF-RESOLVER] [AUDIT-VIOLATION] Null bar data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return;
            }

            try
            {
                await Task.CompletedTask.ConfigureAwait(false);

                var state = _symbolStates.GetOrAdd(symbol, _ => new MtfStructureState());
                
                // Extract close price from bar data
                var barType = barData.GetType();
                var closeProperty = barType.GetProperty("Close") ?? barType.GetProperty("C");
                var timestampProperty = barType.GetProperty("Timestamp") ?? barType.GetProperty("Time");

                if (closeProperty == null)
                {
                    _logger.LogError("[MTF-RESOLVER] [AUDIT-VIOLATION] Missing close price property for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                    return;
                }

                var close = Convert.ToDecimal(closeProperty.GetValue(barData));
                var timestamp = timestampProperty != null ? Convert.ToDateTime(timestampProperty.GetValue(barData)) : DateTime.UtcNow;

                // Add to price history
                state.PriceHistory.Add(new PricePoint { Price = (double)close, Timestamp = timestamp });
                
                // Keep only required bars for memory efficiency
                while (state.PriceHistory.Count > MediumHorizonBars + 10) // Keep buffer
                {
                    state.PriceHistory.RemoveAt(0);
                }

                // Calculate multi-timeframe alignment if we have sufficient data
                if (state.PriceHistory.Count >= MediumHorizonBars)
                {
                    var shortSlope = CalculateSlope(state.PriceHistory.TakeLast(ShortHorizonBars).ToList());
                    var mediumSlope = CalculateSlope(state.PriceHistory.TakeLast(MediumHorizonBars).ToList());
                    
                    // Alignment: correlation between short and medium slopes
                    // 1.0 = perfect alignment, 0.0 = no correlation, -1.0 = opposing
                    var alignment = CalculateAlignment(shortSlope, mediumSlope);
                    
                    // Bias: overall directional bias (positive = bullish, negative = bearish)
                    var bias = (shortSlope + mediumSlope) / 2.0;
                    
                    state.Alignment = alignment;
                    state.Bias = bias;
                    state.LastUpdate = DateTime.UtcNow;

                    _logger.LogTrace("[MTF-RESOLVER] Updated {Symbol}: Alignment={Alignment:F4}, Bias={Bias:F4}", 
                        symbol, alignment, bias);
                }
                else
                {
                    _logger.LogTrace("[MTF-RESOLVER] Insufficient data for {Symbol}: {Count}/{Required} bars", 
                        symbol, state.PriceHistory.Count, MediumHorizonBars);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MTF-RESOLVER] [AUDIT-VIOLATION] Failed to process bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                // Fail-closed: let exception bubble up to crash service rather than silently continue
                throw new InvalidOperationException($"[MTF-RESOLVER] Critical failure processing bar for '{symbol}': {ex.Message}", ex);
            }
        }

        public async Task<double?> TryGetAsync(string symbol, string featureKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(featureKey))
            {
                _logger.LogError("[MTF-RESOLVER] [AUDIT-VIOLATION] Invalid symbol or feature key - FAIL-CLOSED + TELEMETRY");
                return null;
            }

            await Task.CompletedTask.ConfigureAwait(false);

            if (!_symbolStates.TryGetValue(symbol, out var state))
            {
                _logger.LogWarning("[MTF-RESOLVER] [AUDIT-VIOLATION] No state for symbol {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return null;
            }

            if (state.PriceHistory.Count < MediumHorizonBars)
            {
                _logger.LogWarning("[MTF-RESOLVER] [AUDIT-VIOLATION] Insufficient data for {Symbol}: {Count}/{Required} bars - FAIL-CLOSED + TELEMETRY", 
                    symbol, state.PriceHistory.Count, MediumHorizonBars);
                return null;
            }

            return featureKey.ToLowerInvariant() switch
            {
                "mtf.align" => state.Alignment,
                "mtf.bias" => state.Bias,
                _ => null
            };
        }

        private static double CalculateSlope(List<PricePoint> prices)
        {
            if (prices.Count < 2) return 0.0;
            
            var n = prices.Count;
            var xSum = 0.0;
            var ySum = 0.0;
            var xySum = 0.0;
            var xxSum = 0.0;
            
            for (int i = 0; i < n; i++)
            {
                var x = i; // Time index
                var y = prices[i].Price;
                
                xSum += x;
                ySum += y;
                xySum += x * y;
                xxSum += x * x;
            }
            
            var denominator = n * xxSum - xSum * xSum;
            if (Math.Abs(denominator) < 1e-10) return 0.0;
            
            return (n * xySum - xSum * ySum) / denominator;
        }

        private static double CalculateAlignment(double shortSlope, double mediumSlope)
        {
            // Normalize slopes to prevent extreme values
            var normalizedShort = Math.Tanh(shortSlope * 1000); // Scale factor for price slopes
            var normalizedMedium = Math.Tanh(mediumSlope * 1000);
            
            // Correlation-like measure: positive when slopes align, negative when opposing
            return normalizedShort * normalizedMedium;
        }

        private sealed class MtfStructureState
        {
            public List<PricePoint> PriceHistory { get; } = new();
            public double Alignment { get; set; }
            public double Bias { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        private sealed class PricePoint
        {
            public double Price { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}