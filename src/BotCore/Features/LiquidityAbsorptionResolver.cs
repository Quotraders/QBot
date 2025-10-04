using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Features
{
    /// <summary>
    /// Liquidity Absorption Resolver - measuring wick/body ratios and volume-per-range
    /// Implements fail-closed behavior with audit-clean telemetry
    /// NO safe defaults - missing data results in hold + telemetry
    /// </summary>
    public sealed class LiquidityAbsorptionResolver : IFeatureResolver
    {
        private readonly ILogger<LiquidityAbsorptionResolver> _logger;
        private readonly ConcurrentDictionary<string, LiquidityAbsorptionState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);
        
        private readonly string[] _availableFeatureKeys = new[]
        {
            "liquidity.absorb_bull",
            "liquidity.absorb_bear", 
            "liquidity.vpr"
        };

        public LiquidityAbsorptionResolver(ILogger<LiquidityAbsorptionResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string[] GetAvailableFeatureKeys() => _availableFeatureKeys;

        public async Task OnBarAsync(string symbol, object barData, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogError("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Empty symbol provided - FAIL-CLOSED + TELEMETRY");
                return;
            }

            if (barData == null)
            {
                _logger.LogError("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Null bar data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return;
            }

            try
            {
                await Task.CompletedTask.ConfigureAwait(false);

                var state = _symbolStates.GetOrAdd(symbol, _ => new LiquidityAbsorptionState());
                
                // Extract bar data using reflection (fail-closed approach)
                var barType = barData.GetType();
                var openProperty = barType.GetProperty("Open") ?? barType.GetProperty("O");
                var highProperty = barType.GetProperty("High") ?? barType.GetProperty("H"); 
                var lowProperty = barType.GetProperty("Low") ?? barType.GetProperty("L");
                var closeProperty = barType.GetProperty("Close") ?? barType.GetProperty("C");
                var volumeProperty = barType.GetProperty("Volume") ?? barType.GetProperty("V");

                if (openProperty == null || highProperty == null || lowProperty == null || 
                    closeProperty == null || volumeProperty == null)
                {
                    _logger.LogError("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Missing required bar properties for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                    return;
                }

                var open = Convert.ToDecimal(openProperty.GetValue(barData));
                var high = Convert.ToDecimal(highProperty.GetValue(barData));
                var low = Convert.ToDecimal(lowProperty.GetValue(barData));
                var close = Convert.ToDecimal(closeProperty.GetValue(barData));
                var volume = Convert.ToDecimal(volumeProperty.GetValue(barData));

                // Calculate liquidity absorption metrics
                var bodySize = Math.Abs(close - open);
                var range = high - low;
                
                if (range == 0)
                {
                    _logger.LogWarning("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Zero range bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                    return;
                }

                var upperWick = high - Math.Max(open, close);
                var lowerWick = Math.Min(open, close) - low;
                
                // Volume per range calculation (audit-clean, no hardcoded values)
                var vpr = volume / range;
                
                // Bull absorption: large lower wick relative to body (buying pressure)
                var bullAbsorption = bodySize > 0 ? lowerWick / bodySize : 0m;
                
                // Bear absorption: large upper wick relative to body (selling pressure)
                var bearAbsorption = bodySize > 0 ? upperWick / bodySize : 0m;

                // Update state with audit logging
                state.BullAbsorption = (double)bullAbsorption;
                state.BearAbsorption = (double)bearAbsorption;
                state.VolumePerRange = (double)vpr;
                state.LastUpdate = DateTime.UtcNow;

                _logger.LogTrace("[LIQUIDITY-RESOLVER] Updated {Symbol}: Bull={BullAbsorption:F4}, Bear={BearAbsorption:F4}, VPR={VPR:F2}", 
                    symbol, bullAbsorption, bearAbsorption, vpr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Failed to process bar for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                // Fail-closed: let exception bubble up to crash service rather than silently continue
                throw new InvalidOperationException($"[LIQUIDITY-RESOLVER] Critical failure processing bar for '{symbol}': {ex.Message}", ex);
            }
        }

        public async Task<double?> TryGetAsync(string symbol, string featureKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(featureKey))
            {
                _logger.LogError("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] Invalid symbol or feature key - FAIL-CLOSED + TELEMETRY");
                return null;
            }

            await Task.CompletedTask.ConfigureAwait(false);

            if (!_symbolStates.TryGetValue(symbol, out var state))
            {
                _logger.LogWarning("[LIQUIDITY-RESOLVER] [AUDIT-VIOLATION] No state for symbol {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
                return null;
            }

            return featureKey.ToUpperInvariant() switch
            {
                "LIQUIDITY.ABSORB_BULL" => state.BullAbsorption,
                "LIQUIDITY.ABSORB_BEAR" => state.BearAbsorption,
                "LIQUIDITY.VPR" => state.VolumePerRange,
                _ => null
            };
        }

        private sealed class LiquidityAbsorptionState
        {
            public double BullAbsorption { get; set; }
            public double BearAbsorption { get; set; }
            public double VolumePerRange { get; set; }
            public DateTime LastUpdate { get; set; }
        }
    }
}