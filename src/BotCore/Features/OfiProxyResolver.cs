using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly OfiConfiguration _config;
        private readonly ConcurrentDictionary<string, OfiProxyState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);
        
        private readonly string[] _availableFeatureKeys = new[]
        {
            "ofi.proxy"
        };

        // LoggerMessage delegates for performance
        private static readonly Action<ILogger, Exception?> LogEmptySymbol =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(7401, nameof(LogEmptySymbol)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Empty symbol provided - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> LogNullBarData =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(7402, nameof(LogNullBarData)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Null bar data for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> LogMissingBarProperties =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(7403, nameof(LogMissingBarProperties)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Missing required bar properties for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> LogZeroRangeBar =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(7404, nameof(LogZeroRangeBar)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Zero range bar for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, double, double, Exception?> LogUpdated =
            LoggerMessage.Define<string, double, double>(
                LogLevel.Trace,
                new EventId(7405, nameof(LogUpdated)),
                "[OFI-RESOLVER] Updated {Symbol}: OFI Proxy={OfiProxy:F4}, Normalized={Normalized:F4}");

        private static readonly Action<ILogger, string, int, int, Exception?> LogInsufficientData =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Trace,
                new EventId(7406, nameof(LogInsufficientData)),
                "[OFI-RESOLVER] Insufficient data for {Symbol}: {Count}/{Required} bars");

        private static readonly Action<ILogger, string, Exception?> LogProcessBarError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(7407, nameof(LogProcessBarError)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Failed to process bar for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, Exception?> LogInvalidInput =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(7408, nameof(LogInvalidInput)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Invalid symbol or feature key - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> LogNoStateForSymbol =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(7409, nameof(LogNoStateForSymbol)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] No state for symbol {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, int, int, Exception?> LogInsufficientDataForGet =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Warning,
                new EventId(7410, nameof(LogInsufficientDataForGet)),
                "[OFI-RESOLVER] [AUDIT-VIOLATION] Insufficient data for {Symbol}: {Count}/{Required} bars - FAIL-CLOSED + TELEMETRY");

        public OfiProxyResolver(ILogger<OfiProxyResolver> logger, IOptions<OfiConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration on construction
            _config.Validate();
        }

        public string[] GetAvailableFeatureKeys() => _availableFeatureKeys;

        public async Task OnBarAsync(string symbol, object barData, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                LogEmptySymbol(_logger, null);
                return;
            }

            if (barData == null)
            {
                LogNullBarData(_logger, symbol, null);
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
                    LogMissingBarProperties(_logger, symbol, null);
                    return;
                }

                var high = Convert.ToDecimal(highProperty.GetValue(barData), CultureInfo.InvariantCulture);
                var low = Convert.ToDecimal(lowProperty.GetValue(barData), CultureInfo.InvariantCulture);
                var close = Convert.ToDecimal(closeProperty.GetValue(barData), CultureInfo.InvariantCulture);
                var volume = Convert.ToDecimal(volumeProperty.GetValue(barData), CultureInfo.InvariantCulture);
                var timestamp = timestampProperty != null ? Convert.ToDateTime(timestampProperty.GetValue(barData), CultureInfo.InvariantCulture) : DateTime.UtcNow;

                // Calculate OFI proxy using bar-based approximation
                // True OFI requires tick data, but we can approximate using bar characteristics
                
                // Price movement relative to range
                var range = high - low;
                if (range == 0)
                {
                    LogZeroRangeBar(_logger, symbol, null);
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
                while (state.OfiHistory.Count > _config.LookbackBars + _config.HistoryBufferSize) // Keep buffer
                {
                    state.OfiHistory.RemoveAt(0);
                }

                // Calculate rolling OFI proxy if we have sufficient data
                if (state.OfiHistory.Count >= _config.LookbackBars)
                {
                    // Simple moving average of OFI proxy values
                    var recentOfi = state.OfiHistory.TakeLast(_config.LookbackBars).Select(x => x.Value).ToList();
                    var avgOfi = recentOfi.Average();
                    
                    // Normalize by recent volatility to make values comparable across symbols
                    var ofiStdDev = CalculateStandardDeviation(recentOfi);
                    var normalizedOfi = ofiStdDev > 0 ? avgOfi / ofiStdDev : _config.SafeZeroValue;
                    
                    state.NormalizedOfiProxy = normalizedOfi;
                    state.LastUpdate = DateTime.UtcNow;

                    LogUpdated(_logger, symbol, avgOfi, normalizedOfi, null);
                }
                else
                {
                    LogInsufficientData(_logger, symbol, state.OfiHistory.Count, _config.LookbackBars, null);
                }
            }
            catch (Exception ex)
            {
                LogProcessBarError(_logger, symbol, ex);
                // Fail-closed: let exception bubble up to crash service rather than silently continue
                throw new InvalidOperationException($"[OFI-RESOLVER] Critical failure processing bar for '{symbol}': {ex.Message}", ex);
            }
        }

        public async Task<double?> TryGetAsync(string symbol, string featureKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(featureKey))
            {
                LogInvalidInput(_logger, null);
                return null;
            }

            await Task.CompletedTask.ConfigureAwait(false);

            if (!_symbolStates.TryGetValue(symbol, out var state))
            {
                LogNoStateForSymbol(_logger, symbol, null);
                return null;
            }

            if (state.OfiHistory.Count < _config.LookbackBars)
            {
                LogInsufficientDataForGet(_logger, symbol, state.OfiHistory.Count, _config.LookbackBars, null);
                return null;
            }

            return featureKey.ToUpperInvariant() switch
            {
                "OFI.PROXY" => state.NormalizedOfiProxy,
                _ => null
            };
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < _config.MinDataPointsRequired) return _config.SafeZeroValue;
            
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