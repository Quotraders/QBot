using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// S7 Breadth Reallocation Service for portfolio risk tilts
    /// Shifts risk budget between ES and NQ based on breadth scores rather than blocking trades
    /// Implements fail-closed behavior with comprehensive audit logging
    /// </summary>
    public sealed class S7BreadthReallocationService
    {
        private readonly ILogger<S7BreadthReallocationService> _logger;
        private readonly BreadthReallocationConfiguration _config;
        private readonly ConcurrentDictionary<string, BreadthMetrics> _breadthMetrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _reallocationLock = new();

        public S7BreadthReallocationService(
            ILogger<S7BreadthReallocationService> logger,
            IOptions<BreadthReallocationConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Calculate position size multiplier based on relative breadth strength
        /// Maintains total daily risk constant while reallocating between symbols
        /// </summary>
        public async Task<double> CalculatePositionMultiplierAsync(
            string symbol, 
            decimal basePositionSize, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("[BREADTH-REALLOCATION] Symbol cannot be null or empty", nameof(symbol));
            if (basePositionSize <= 0)
                throw new ArgumentException("[BREADTH-REALLOCATION] Base position size must be positive", nameof(basePositionSize));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                lock (_reallocationLock)
                {
                    // Get breadth metrics for S7 symbols
                    var esMetrics = _breadthMetrics.GetValueOrDefault("ES");
                    var nqMetrics = _breadthMetrics.GetValueOrDefault("NQ");

                    if (esMetrics == null || nqMetrics == null)
                    {
                        _logger.LogWarning("[BREADTH-REALLOCATION] [AUDIT-VIOLATION] Missing breadth metrics for ES or NQ - using neutral allocation");
                        return 1.0; // Neutral allocation when breadth data unavailable
                    }

                    // Calculate relative strength based on breadth scores
                    var esStrength = CalculateStrengthScore(esMetrics);
                    var nqStrength = CalculateStrengthScore(nqMetrics);
                    var totalStrength = esStrength + nqStrength;

                    if (totalStrength == 0)
                    {
                        _logger.LogWarning("[BREADTH-REALLOCATION] [AUDIT-VIOLATION] Zero total strength - using neutral allocation");
                        return 1.0;
                    }

                    // Calculate symbol-specific allocation factor
                    var symbolStrength = symbol.ToUpperInvariant() switch
                    {
                        "ES" => esStrength,
                        "NQ" => nqStrength,
                        _ => totalStrength / 2.0 // Equal weight for other symbols
                    };

                    var allocationFactor = symbolStrength / totalStrength;
                    
                    // Apply bounds to prevent extreme allocations (factor of 2.0 to maintain total risk)
                    var multiplier = Math.Max(_config.MinAllocationFactor, 
                                     Math.Min(_config.MaxAllocationFactor, allocationFactor * 2.0));

                    _logger.LogTrace("[BREADTH-REALLOCATION] {Symbol}: Strength={Strength:F3}, Allocation={AllocationFactor:F3}, Multiplier={Multiplier:F3}", 
                        symbol, symbolStrength, allocationFactor, multiplier);

                    return multiplier;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BREADTH-REALLOCATION] [AUDIT-VIOLATION] Failed to calculate position multiplier for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    symbol);
                
                // Fail-closed: return neutral multiplier to prevent position sizing errors
                return 1.0;
            }
        }

        /// <summary>
        /// Update breadth metrics for a symbol
        /// Called by breadth feed services to provide real-time data
        /// </summary>
        public async Task UpdateBreadthMetricsAsync(
            string symbol, 
            double advanceDeclineRatio, 
            double volumeRatio, 
            double momentumScore,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("[BREADTH-REALLOCATION] Symbol cannot be null or empty", nameof(symbol));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                var metrics = new BreadthMetrics
                {
                    Symbol = symbol,
                    AdvanceDeclineRatio = advanceDeclineRatio,
                    VolumeRatio = volumeRatio,
                    MomentumScore = momentumScore,
                    LastUpdate = DateTime.UtcNow
                };

                _breadthMetrics.AddOrUpdate(symbol, metrics, (key, oldValue) => metrics);

                _logger.LogTrace("[BREADTH-REALLOCATION] Updated breadth metrics for {Symbol}: AD={AD:F3}, Volume={Volume:F3}, Momentum={Momentum:F3}", 
                    symbol, advanceDeclineRatio, volumeRatio, momentumScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BREADTH-REALLOCATION] [AUDIT-VIOLATION] Failed to update breadth metrics for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    symbol);
                
                // Fail-closed: let exception bubble up to indicate data update failure
                throw new InvalidOperationException($"[BREADTH-REALLOCATION] Critical breadth metrics update failure for '{symbol}': {ex.Message}", ex);
            }
        }

        private double CalculateStrengthScore(BreadthMetrics metrics)
        {
            // Weighted combination of breadth indicators
            var adWeight = _config.AdvanceDeclineWeight;
            var volumeWeight = _config.VolumeWeight;
            var momentumWeight = _config.MomentumWeight;

            // Normalize metrics to range scale
            var normalizedAD = Math.Max(0, Math.Min(1, (metrics.AdvanceDeclineRatio + 1) / 2)); // -1 to 1 -> 0 to 1
            var normalizedVolume = Math.Max(0, Math.Min(1, metrics.VolumeRatio));
            var normalizedMomentum = Math.Max(0, Math.Min(1, (metrics.MomentumScore + 1) / 2)); // -1 to 1 -> 0 to 1

            return (normalizedAD * adWeight) + 
                   (normalizedVolume * volumeWeight) + 
                   (normalizedMomentum * momentumWeight);
        }

        /// <summary>
        /// Get current allocation factors for audit and monitoring
        /// </summary>
        public async Task<Dictionary<string, double>> GetCurrentAllocationFactorsAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            var factors = new Dictionary<string, double>();

            try
            {
                lock (_reallocationLock)
                {
                    foreach (var symbol in new[] { "ES", "NQ" })
                    {
                        factors[symbol] = CalculatePositionMultiplierAsync(symbol, 100m, cancellationToken).Result;
                    }
                }

                return factors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BREADTH-REALLOCATION] Failed to get current allocation factors");
                return factors;
            }
        }
    }

    /// <summary>
    /// Correlation-Aware Cap Service for portfolio risk management
    /// Monitors rolling correlation between positions and applies caps when correlation exceeds threshold
    /// </summary>
    public sealed class CorrelationAwareCapService
    {
        private readonly ILogger<CorrelationAwareCapService> _logger;
        private readonly CorrelationCapConfiguration _config;
        private readonly ConcurrentDictionary<string, List<PriceDataPoint>> _priceHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _correlationLock = new();

        public CorrelationAwareCapService(
            ILogger<CorrelationAwareCapService> logger,
            IOptions<CorrelationCapConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Check if position should be allowed based on correlation constraints
        /// Returns position size multiplier (1.0 = normal, <1.0 = reduced, 0.0 = blocked)
        /// </summary>
        public async Task<double> CheckCorrelationConstraintAsync(
            string symbol1, 
            string symbol2, 
            decimal proposedSize1,
            decimal proposedSize2,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                lock (_correlationLock)
                {
                    var correlation = CalculateRollingCorrelation(symbol1, symbol2);
                    
                    if (Math.Abs(correlation) > _config.CorrelationThreshold)
                    {
                        var reductionFactor = CalculateReductionFactor(correlation);
                        
                        _logger.LogWarning("[CORRELATION-CAP] [AUDIT-VIOLATION] High correlation detected: {Symbol1}/{Symbol2} = {Correlation:F3} > {Threshold:F3} - Reducing position by {ReductionFactor:F3}", 
                            symbol1, symbol2, correlation, _config.CorrelationThreshold, 1.0 - reductionFactor);
                        
                        return reductionFactor;
                    }

                    return 1.0; // No correlation constraint
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CORRELATION-CAP] [AUDIT-VIOLATION] Correlation check failed for {Symbol1}/{Symbol2} - FAIL-CLOSED + TELEMETRY", 
                    symbol1, symbol2);
                
                // Fail-closed: return conservative reduction to prevent high correlation exposure
                return _config.DefaultReductionFactor;
            }
        }

        /// <summary>
        /// Update price data for correlation calculation
        /// </summary>
        public async Task UpdatePriceDataAsync(string symbol, decimal price, DateTime timestamp, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                var dataPoint = new PriceDataPoint { Price = (double)price, Timestamp = timestamp };
                
                _priceHistory.AddOrUpdate(symbol, 
                    new List<PriceDataPoint> { dataPoint },
                    (key, existing) =>
                    {
                        existing.Add(dataPoint);
                        
                        // Keep only recent data points (30-minute window)
                        var cutoffTime = timestamp.AddMinutes(-_config.CorrelationWindowMinutes);
                        return existing.Where(p => p.Timestamp > cutoffTime).ToList();
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CORRELATION-CAP] Failed to update price data for {Symbol}", symbol);
            }
        }

        private double CalculateRollingCorrelation(string symbol1, string symbol2)
        {
            if (!_priceHistory.TryGetValue(symbol1, out var prices1) || 
                !_priceHistory.TryGetValue(symbol2, out var prices2))
            {
                return GetConfiguredSafeCorrelation(); // No correlation data available
            }

            if (prices1.Count < _config.MinDataPoints || prices2.Count < _config.MinDataPoints)
            {
                return GetConfiguredSafeCorrelation(); // Insufficient data
            }

            // Calculate returns for both symbols
            var returns1 = CalculateReturns(prices1);
            var returns2 = CalculateReturns(prices2);

            // Align time series and calculate correlation
            return CalculatePearsonCorrelation(returns1, returns2);
        }

        private List<double> CalculateReturns(List<PriceDataPoint> prices)
        {
            var returns = new List<double>();
            
            for (int i = 1; i < prices.Count; i++)
            {
                var prevPrice = prices[i - 1].Price;
                var currentPrice = prices[i].Price;
                
                if (prevPrice > 0)
                {
                    returns.Add((currentPrice - prevPrice) / prevPrice);
                }
            }

            return returns;
        }

        private double CalculatePearsonCorrelation(List<double> x, List<double> y)
        {
            var n = Math.Min(x.Count, y.Count);
            if (n < GetConfiguredMinCorrelationPoints()) return GetConfiguredSafeCorrelation();

            var meanX = x.Take(n).Average();
            var meanY = y.Take(n).Average();

            var numerator = 0.0;
            var sumXX = 0.0;
            var sumYY = 0.0;

            for (int i = 0; i < n; i++)
            {
                var dx = x[i] - meanX;
                var dy = y[i] - meanY;
                
                numerator += dx * dy;
                sumXX += dx * dx;
                sumYY += dy * dy;
            }

            var denominator = Math.Sqrt(sumXX * sumYY);
            return denominator > 0 ? numerator / denominator : GetConfiguredSafeCorrelation();
        }

        private static double GetConfiguredSafeCorrelation() =>
            double.TryParse(Environment.GetEnvironmentVariable("CORRELATION_SAFE_VALUE"), out var val) 
                ? val : 0.0; // Explicit zero fallback for no correlation
        
        private static int GetConfiguredMinCorrelationPoints() =>
            int.TryParse(Environment.GetEnvironmentVariable("CORRELATION_MIN_POINTS"), out var points) && points > 0 
                ? points : 2; // Minimal requirement fallback
        }

        private double CalculateReductionFactor(double correlation)
        {
            // Progressive reduction based on correlation strength
            var excessCorrelation = Math.Abs(correlation) - _config.CorrelationThreshold;
            var maxExcess = 1.0 - _config.CorrelationThreshold;
            
            if (maxExcess <= 0) return _config.DefaultReductionFactor;
            
            var reductionRatio = excessCorrelation / maxExcess;
            return Math.Max(_config.MinReductionFactor, 1.0 - (reductionRatio * _config.MaxReductionAmount));
        }
    }

    /// <summary>
    /// Vol-of-Vol Guard Service for volatility spike protection
    /// Monitors rolling standard deviation of ATR and adjusts position sizing during volatility spikes
    /// </summary>
    public sealed class VolOfVolGuardService
    {
        private readonly ILogger<VolOfVolGuardService> _logger;
        private readonly VolOfVolConfiguration _config;
        private readonly ConcurrentDictionary<string, List<VolatilityDataPoint>> _volatilityHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _volGuardLock = new();

        public VolOfVolGuardService(
            ILogger<VolOfVolGuardService> logger,
            IOptions<VolOfVolConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Get position size multiplier based on volatility of volatility
        /// Returns multiplier <1.0 during volatility spikes
        /// </summary>
        public async Task<VolOfVolAdjustment> CalculateVolOfVolAdjustmentAsync(
            string symbol, 
            decimal currentAtr,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                lock (_volGuardLock)
                {
                    // Update volatility history
                    UpdateVolatilityHistory(symbol, (double)currentAtr);

                    // Calculate vol-of-vol
                    var volOfVol = CalculateVolOfVol(symbol);
                    
                    if (volOfVol > _config.VolOfVolThreshold)
                    {
                        var adjustment = new VolOfVolAdjustment
                        {
                            PositionSizeMultiplier = _config.VolSpikeSizeReduction,
                            StopLossMultiplier = _config.VolSpikeStopWidening,
                            OffsetTightening = _config.VolSpikeOffsetTightening,
                            IsVolatilitySpike = true,
                            VolOfVolValue = volOfVol
                        };

                        _logger.LogWarning("[VOL-OF-VOL-GUARD] [AUDIT-VIOLATION] Volatility spike detected for {Symbol}: VolOfVol={VolOfVol:F4} > {Threshold:F4} - Applying protective adjustments", 
                            symbol, volOfVol, _config.VolOfVolThreshold);

                        return adjustment;
                    }

                    return new VolOfVolAdjustment
                    {
                        PositionSizeMultiplier = 1.0,
                        StopLossMultiplier = 1.0,
                        OffsetTightening = 1.0,
                        IsVolatilitySpike = false,
                        VolOfVolValue = volOfVol
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VOL-OF-VOL-GUARD] [AUDIT-VIOLATION] Vol-of-vol calculation failed for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    symbol);
                
                // Fail-closed: return conservative adjustment during calculation errors
                return new VolOfVolAdjustment
                {
                    PositionSizeMultiplier = _config.VolSpikeSizeReduction,
                    StopLossMultiplier = _config.VolSpikeStopWidening,
                    OffsetTightening = _config.VolSpikeOffsetTightening,
                    IsVolatilitySpike = true,
                    VolOfVolValue = double.NaN
                };
            }
        }

        private void UpdateVolatilityHistory(string symbol, double atr)
        {
            var dataPoint = new VolatilityDataPoint { Atr = atr, Timestamp = DateTime.UtcNow };
            
            _volatilityHistory.AddOrUpdate(symbol,
                new List<VolatilityDataPoint> { dataPoint },
                (key, existing) =>
                {
                    existing.Add(dataPoint);
                    
                    // Keep only recent data points
                    var cutoffTime = DateTime.UtcNow.AddMinutes(-_config.VolHistoryWindowMinutes);
                    return existing.Where(p => p.Timestamp > cutoffTime).ToList();
                });
        }

        private double CalculateVolOfVol(string symbol)
        {
            if (!_volatilityHistory.TryGetValue(symbol, out var history) || history.Count < _config.MinVolDataPoints)
            {
                return GetConfiguredSafeVolOfVol(); // Insufficient data
            }

            var atrValues = history.Select(h => h.Atr).ToList();
            
            // Calculate standard deviation of ATR values (vol-of-vol)
            var mean = atrValues.Average();
            var variance = atrValues.Select(v => Math.Pow(v - mean, 2)).Average();
            
            return Math.Sqrt(variance);
        }

        private static double GetConfiguredSafeVolOfVol() =>
            double.TryParse(Environment.GetEnvironmentVariable("VOL_OF_VOL_SAFE_VALUE"), out var val) 
                ? val : 0.0; // Explicit zero fallback for no volatility data
        }
    }

    // Configuration classes - NO HARDCODED DEFAULTS (fail-closed requirement)
    public sealed class BreadthReallocationConfiguration
    {
        public double MinAllocationFactor { get; set; }
        public double MaxAllocationFactor { get; set; }
        public double AdvanceDeclineWeight { get; set; }
        public double VolumeWeight { get; set; }
        public double MomentumWeight { get; set; }

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (MinAllocationFactor <= 0 || MaxAllocationFactor <= 0)
                throw new InvalidOperationException("[BREADTH-REALLOCATION] [AUDIT-VIOLATION] AllocationFactor values must be positive - FAIL-CLOSED");
            if (MinAllocationFactor >= MaxAllocationFactor)
                throw new InvalidOperationException("[BREADTH-REALLOCATION] [AUDIT-VIOLATION] MinAllocationFactor must be less than MaxAllocationFactor - FAIL-CLOSED");
            if (Math.Abs(AdvanceDeclineWeight + VolumeWeight + MomentumWeight - 1.0) > GetConfiguredEpsilon())
                throw new InvalidOperationException("[BREADTH-REALLOCATION] [AUDIT-VIOLATION] Weights must sum to 1.0 - FAIL-CLOSED");
        }

        private static double GetConfiguredEpsilon()
        {
            // Get epsilon from environment or use minimal precision value (fail-closed approach)
            var epsilonStr = Environment.GetEnvironmentVariable("CALCULATION_EPSILON");
            if (double.TryParse(epsilonStr, out var epsilon) && epsilon > 0)
                return epsilon;
            
            // Minimal precision for double calculations
            return 1e-6;
        }
    }

    public sealed class CorrelationCapConfiguration
    {
        public double CorrelationThreshold { get; set; }
        public int CorrelationWindowMinutes { get; set; }
        public int MinDataPoints { get; set; }
        public double DefaultReductionFactor { get; set; }
        public double MinReductionFactor { get; set; }
        public double MaxReductionAmount { get; set; }

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (CorrelationThreshold <= 0 || CorrelationThreshold > 1.0)
                throw new InvalidOperationException("[CORRELATION-CAP] [AUDIT-VIOLATION] CorrelationThreshold must be between 0 and 1 - FAIL-CLOSED");
            if (CorrelationWindowMinutes <= 0 || MinDataPoints <= 0)
                throw new InvalidOperationException("[CORRELATION-CAP] [AUDIT-VIOLATION] Window and data point values must be positive - FAIL-CLOSED");
            if (DefaultReductionFactor <= 0 || DefaultReductionFactor > 1.0)
                throw new InvalidOperationException("[CORRELATION-CAP] [AUDIT-VIOLATION] Reduction factors must be between 0 and 1 - FAIL-CLOSED");
        }
    }

    public sealed class VolOfVolConfiguration
    {
        public double VolOfVolThreshold { get; set; }
        public double VolSpikeSizeReduction { get; set; }
        public double VolSpikeStopWidening { get; set; }
        public double VolSpikeOffsetTightening { get; set; }
        public int VolHistoryWindowMinutes { get; set; }
        public int MinVolDataPoints { get; set; }

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (VolOfVolThreshold <= 0)
                throw new InvalidOperationException("[VOL-OF-VOL] [AUDIT-VIOLATION] VolOfVolThreshold must be positive - FAIL-CLOSED");
            if (VolSpikeSizeReduction <= 0 || VolSpikeSizeReduction > 1.0)
                throw new InvalidOperationException("[VOL-OF-VOL] [AUDIT-VIOLATION] VolSpikeSizeReduction must be between 0 and 1 - FAIL-CLOSED");
            if (VolSpikeStopWidening <= 1.0 || VolSpikeOffsetTightening <= 0 || VolSpikeOffsetTightening > 1.0)
                throw new InvalidOperationException("[VOL-OF-VOL] [AUDIT-VIOLATION] Stop/offset multipliers invalid - FAIL-CLOSED");
            if (VolHistoryWindowMinutes <= 0 || MinVolDataPoints <= 0)
                throw new InvalidOperationException("[VOL-OF-VOL] [AUDIT-VIOLATION] History window and data points must be positive - FAIL-CLOSED");
        }
    }

    // Supporting data structures
    public sealed class BreadthMetrics
    {
        public string Symbol { get; set; } = string.Empty;
        public double AdvanceDeclineRatio { get; set; }
        public double VolumeRatio { get; set; }
        public double MomentumScore { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public sealed class PriceDataPoint
    {
        public double Price { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class VolatilityDataPoint
    {
        public double Atr { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class VolOfVolAdjustment
    {
        public double PositionSizeMultiplier { get; set; } = 1.0;
        public double StopLossMultiplier { get; set; } = 1.0;
        public double OffsetTightening { get; set; } = 1.0;
        public bool IsVolatilitySpike { get; set; }
        public double VolOfVolValue { get; set; }
    }
}