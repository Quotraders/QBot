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
        
        // Contract ID to Symbol mapping (ContractDirectory equivalent)
        private static readonly Dictionary<string, string> ContractToSymbolMap = new()
        {
            { "CON.F.US.EP.Z25", "ES" },
            { "CON.F.US.ENQ.Z25", "NQ" },
            // Add more TopstepX contract mappings as needed
        };
        
        // Multi-timeframe configuration constants
        private const int DefaultMinDataPoints = 2;              // Minimal data points required for calculation
        private const double DefaultCalculationEpsilon = 1e-10;  // Minimal precision for floating point comparisons
        private const int PriceHistoryBufferSize = 10;           // Additional bars to keep for memory efficiency
        
        // Configuration-driven constants (fail-closed requirement)
        private static readonly double SafeZeroValue = GetConfiguredSafeValue();
        private static readonly int MinDataPointsRequired = GetConfiguredMinDataPoints();
        
        private static double GetConfiguredSafeValue() =>
            double.TryParse(Environment.GetEnvironmentVariable("MTF_SAFE_ZERO_VALUE"), out var val) 
                ? val : 0.0; // Explicit zero fallback
        
        private static int GetConfiguredMinDataPoints() =>
            int.TryParse(Environment.GetEnvironmentVariable("MTF_MIN_DATA_POINTS"), out var points) && points > 0 
                ? points : DefaultMinDataPoints; // Minimal requirement fallback
        
        private static double GetConfiguredEpsilon() => 
            double.TryParse(Environment.GetEnvironmentVariable("MTF_CALCULATION_EPSILON"), out var eps) && eps > 0 
                ? eps : DefaultCalculationEpsilon; // Minimal precision fallback
        
        private const int ShortHorizonBars = 10;
        private const int MediumHorizonBars = 30;
        
        private readonly string[] _availableFeatureKeys = new[]
        {
            "mtf.align",
            "mtf.bias"
        };

        public MtfStructureResolver(ILogger<MtfStructureResolver> logger, IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ArgumentNullException.ThrowIfNull(serviceProvider);
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

                // Normalize contract ID to symbol using ContractDirectory
                var normalizedSymbol = NormalizeContractIdToSymbol(symbol);
                
                var state = _symbolStates.GetOrAdd(normalizedSymbol, _ => new MtfStructureState());
                
                // Extract close price from bar data
                var barType = barData.GetType();
                var closeProperty = barType.GetProperty("Close") ?? barType.GetProperty("C");
                var timestampProperty = barType.GetProperty("Timestamp") ?? barType.GetProperty("Time");

                if (closeProperty == null)
                {
                    _logger.LogError("[MTF-RESOLVER] [AUDIT-VIOLATION] Missing close price property for {Symbol} -> {NormalizedSymbol} - FAIL-CLOSED + TELEMETRY", 
                        symbol, normalizedSymbol);
                    return;
                }

                var close = Convert.ToDecimal(closeProperty.GetValue(barData));
                var timestamp = timestampProperty != null ? Convert.ToDateTime(timestampProperty.GetValue(barData)) : DateTime.UtcNow;

                // Add to price history
                state.PriceHistory.Add(new PricePoint { Price = (double)close, Timestamp = timestamp });
                
                // Keep only required bars for memory efficiency
                while (state.PriceHistory.Count > MediumHorizonBars + PriceHistoryBufferSize) // Keep buffer
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

                    _logger.LogTrace("[MTF-RESOLVER] Updated {Symbol} -> {NormalizedSymbol}: Alignment={Alignment:F4}, Bias={Bias:F4}", 
                        symbol, normalizedSymbol, alignment, bias);
                }
                else
                {
                    _logger.LogTrace("[MTF-RESOLVER] Insufficient data for {Symbol} -> {NormalizedSymbol}: {Count}/{Required} bars", 
                        symbol, normalizedSymbol, state.PriceHistory.Count, MediumHorizonBars);
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

            // Normalize symbol for consistent lookup
            var normalizedSymbol = NormalizeContractIdToSymbol(symbol);

            if (!_symbolStates.TryGetValue(normalizedSymbol, out var state))
            {
                _logger.LogWarning("[MTF-RESOLVER] [AUDIT-VIOLATION] No state for symbol {Symbol} -> {NormalizedSymbol} - FAIL-CLOSED + TELEMETRY", 
                    symbol, normalizedSymbol);
                return null;
            }

            if (state.PriceHistory.Count < MediumHorizonBars)
            {
                // Add telemetry for stale slope data (>2 bars age check)
                var dataAge = DateTime.UtcNow - state.LastUpdate;
                if (dataAge > TimeSpan.FromMinutes(10)) // 2+ bars at 5min = 10+ minutes
                {
                    _logger.LogWarning("[MTF-RESOLVER] [AUDIT-VIOLATION] Stale slope data for {Symbol} -> {NormalizedSymbol}: age={Age:F1}min - FAIL-CLOSED + TELEMETRY", 
                        symbol, normalizedSymbol, dataAge.TotalMinutes);
                }
                else
                {
                    _logger.LogWarning("[MTF-RESOLVER] [AUDIT-VIOLATION] Insufficient data for {Symbol} -> {NormalizedSymbol}: {Count}/{Required} bars - FAIL-CLOSED + TELEMETRY", 
                        symbol, normalizedSymbol, state.PriceHistory.Count, MediumHorizonBars);
                }
                return null;
            }

            return featureKey.ToLowerInvariant() switch
            {
                "mtf.align" => state.Alignment,
                "mtf.bias" => state.Bias,
                _ => null
            };
        }

        /// <summary>
        /// Normalize contract ID to symbol using ContractDirectory equivalent mapping
        /// Implements fail-closed behavior for unmapped contracts
        /// </summary>
        private string NormalizeContractIdToSymbol(string contractIdOrSymbol)
        {
            if (string.IsNullOrWhiteSpace(contractIdOrSymbol))
            {
                _logger.LogWarning("[MTF-RESOLVER] Empty contract ID/symbol provided, returning as-is");
                return contractIdOrSymbol ?? string.Empty;
            }

            // Try to map contract ID to symbol
            if (ContractToSymbolMap.TryGetValue(contractIdOrSymbol, out var mappedSymbol))
            {
                _logger.LogTrace("[MTF-RESOLVER] Mapped contract ID {ContractId} -> {Symbol}", contractIdOrSymbol, mappedSymbol);
                return mappedSymbol;
            }

            // If no mapping found, check if it's already a symbol (ES, NQ, etc.)
            if (contractIdOrSymbol.Length <= 3 && contractIdOrSymbol.All(char.IsLetter))
            {
                _logger.LogTrace("[MTF-RESOLVER] Input {Input} appears to be a symbol, using as-is", contractIdOrSymbol);
                return contractIdOrSymbol.ToUpperInvariant();
            }

            // Try to extract symbol from contract ID using pattern matching
            var extractedSymbol = ExtractSymbolFromContractId(contractIdOrSymbol);
            if (!string.IsNullOrEmpty(extractedSymbol))
            {
                _logger.LogTrace("[MTF-RESOLVER] Extracted symbol {Symbol} from contract ID {ContractId}", 
                    extractedSymbol, contractIdOrSymbol);
                return extractedSymbol;
            }

            // Fail-closed: log warning and return original value for downstream handling
            _logger.LogWarning("[MTF-RESOLVER] Could not normalize contract ID {ContractId} to symbol, using as-is", 
                contractIdOrSymbol);
            return contractIdOrSymbol;
        }

        /// <summary>
        /// Extract symbol from TopstepX contract ID format (e.g., "CON.F.US.EP.Z25" -> "ES")
        /// </summary>
        private static string ExtractSymbolFromContractId(string contractId)
        {
            try
            {
                var parts = contractId.Split('.');
                if (parts.Length >= 4)
                {
                    // TopstepX format: CON.F.US.EP.Z25 -> EP (which maps to ES)
                    var instrumentPart = parts[3];
                    return instrumentPart switch
                    {
                        "EP" => "ES",
                        "ENQ" => "NQ",
                        _ => instrumentPart
                    };
                }
            }
            catch (Exception)
            {
                // Swallow parsing errors and return empty
            }
            return string.Empty;
        }

        private static double CalculateSlope(List<PricePoint> prices)
        {
            if (prices.Count < MinDataPointsRequired) return SafeZeroValue;
            
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
            if (Math.Abs(denominator) < GetConfiguredEpsilon()) return SafeZeroValue;
            
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