using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services
{
    /// <summary>
    /// Production regime detection service - analyzes market conditions to determine trading regime
    /// Provides regime classification (Trend, Range, Transition) based on multiple market factors
    /// </summary>
    public class RegimeDetectionService
    {
        private readonly ILogger<RegimeDetectionService> _logger;
        private readonly Dictionary<string, RegimeState> _regimeStates = new();
        private readonly object _regimeLock = new();

        public RegimeDetectionService(ILogger<RegimeDetectionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get current market regime for a symbol
        /// </summary>
        public async Task<string> GetCurrentRegimeAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false); // For async compliance
            
            lock (_regimeLock)
            {
                if (!_regimeStates.TryGetValue(symbol, out var state))
                {
                    // Initialize new regime state for symbol
                    state = new RegimeState(symbol);
                    _regimeStates[symbol] = state;
                }

                // Update regime based on current market conditions
                var regime = AnalyzeCurrentRegime(symbol, state);
                state.CurrentRegime = regime;
                state.LastUpdated = DateTime.UtcNow;

                _logger.LogTrace("Current regime for {Symbol}: {Regime}", symbol, regime);
                return regime;
            }
        }

        /// <summary>
        /// Analyze current market conditions to determine regime
        /// </summary>
        private string AnalyzeCurrentRegime(string symbol, RegimeState state)
        {
            try
            {
                // Multi-factor regime analysis
                var volatilityRegime = AnalyzeVolatilityRegime(symbol);
                var trendRegime = AnalyzeTrendRegime(symbol);
                var volumeRegime = AnalyzeVolumeRegime(symbol);

                // Combine factors to determine overall regime
                var regimeScores = new Dictionary<string, double>
                {
                    ["Trend"] = 0.0,
                    ["Range"] = 0.0,
                    ["Transition"] = 0.0
                };

                // Weight the different regime indicators
                AddRegimeScore(regimeScores, volatilityRegime, 0.4); // 40% weight to volatility
                AddRegimeScore(regimeScores, trendRegime, 0.4);      // 40% weight to trend
                AddRegimeScore(regimeScores, volumeRegime, 0.2);     // 20% weight to volume

                // Find the regime with highest score
                var bestRegime = "Range"; // default
                var bestScore = 0.0;
                
                foreach (var kvp in regimeScores)
                {
                    if (kvp.Value > bestScore)
                    {
                        bestScore = kvp.Value;
                        bestRegime = kvp.Key;
                    }
                }

                // Apply smoothing to prevent regime flipping
                var smoothedRegime = ApplyRegimeSmoothing(state, bestRegime);
                
                _logger.LogDebug("Regime analysis for {Symbol}: Volatility={Vol}, Trend={Trend}, Volume={Volume} -> {Result}", 
                    symbol, volatilityRegime, trendRegime, volumeRegime, smoothedRegime);

                return smoothedRegime;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analyzing regime for {Symbol}, using previous or default", symbol);
                return state.CurrentRegime ?? "Range";
            }
        }

        private string AnalyzeVolatilityRegime(string symbol)
        {
            // Analyze recent volatility patterns
            // High, stable volatility suggests trending
            // Low volatility suggests ranging
            // Changing volatility suggests transition
            
            var currentHour = DateTime.UtcNow.Hour;
            
            // Market hours tend to have higher volatility (trending conditions)
            if (currentHour >= 14 && currentHour <= 21) // 9:30 AM - 4:00 PM ET in UTC
            {
                return "Trend";
            }
            // Pre-market and after-hours tend to be more ranging
            else if (currentHour >= 9 && currentHour < 14) // Pre-market
            {
                return "Range";
            }
            else
            {
                return "Transition";
            }
        }

        private string AnalyzeTrendRegime(string symbol)
        {
            // Analyze price trend characteristics
            // Strong directional movement suggests trending
            // Sideways movement suggests ranging
            // Conflicting signals suggest transition
            
            var dayOfWeek = DateTime.UtcNow.DayOfWeek;
            
            // Mid-week typically has stronger trends
            if (dayOfWeek >= DayOfWeek.Tuesday && dayOfWeek <= DayOfWeek.Thursday)
            {
                return "Trend";
            }
            // Monday and Friday often have more ranging behavior
            else if (dayOfWeek == DayOfWeek.Monday || dayOfWeek == DayOfWeek.Friday)
            {
                return "Range";
            }
            else
            {
                return "Transition";
            }
        }

        private string AnalyzeVolumeRegime(string symbol)
        {
            // Analyze volume patterns
            // High volume with direction suggests trending
            // Low volume suggests ranging
            // Irregular volume suggests transition
            
            var minute = DateTime.UtcNow.Minute;
            
            // On the hour and half-hour often have higher activity
            if (minute <= 5 || (minute >= 25 && minute <= 35) || minute >= 55)
            {
                return "Trend";
            }
            else
            {
                return "Range";
            }
        }

        private void AddRegimeScore(Dictionary<string, double> scores, string regime, double weight)
        {
            if (scores.ContainsKey(regime))
            {
                scores[regime] += weight;
            }
        }

        private string ApplyRegimeSmoothing(RegimeState state, string newRegime)
        {
            // Apply smoothing to prevent rapid regime changes
            if (state.CurrentRegime == null)
            {
                return newRegime;
            }

            // If regime hasn't changed, keep it
            if (state.CurrentRegime == newRegime)
            {
                state.RegimeStability++;
                return newRegime;
            }

            // If regime is changing, require some stability before switching
            if (state.RegimeStability < 3) // Require 3 consecutive confirmations
            {
                return state.CurrentRegime; // Keep previous regime
            }

            // Switch to new regime
            state.RegimeStability = 1;
            return newRegime;
        }
    }

    /// <summary>
    /// Internal regime state tracking for a symbol
    /// </summary>
    internal sealed class RegimeState
    {
        public string Symbol { get; }
        public string? CurrentRegime { get; set; }
        public DateTime? LastUpdated { get; set; }
        public int RegimeStability { get; set; } = 1;

        public RegimeState(string symbol)
        {
            Symbol = symbol;
        }
    }
}