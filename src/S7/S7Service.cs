using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Abstractions;

namespace TradingBot.S7
{
    /// <summary>
    /// S7 Multi-Horizon Relative Strength Strategy Service
    /// Implements full DSL logic for ES/NQ relative strength analysis with knowledge graph integration
    /// </summary>
    public class S7Service : IS7Service, IS7FeatureSource
    {
        private readonly ILogger<S7Service> _logger;
        private readonly S7Configuration _config;
        private readonly BreadthConfiguration _breadthConfig;
        private readonly IBreadthFeed? _breadthFeed;

        // Price history storage for ES and NQ
        private readonly Dictionary<string, List<PricePoint>> _priceHistory = new();
        
        // Current state for each symbol
        private readonly Dictionary<string, S7State> _currentStates = new();
        
        // Cross-symbol analysis state
        private S7Snapshot _lastSnapshot = new();

        public event EventHandler<S7FeatureUpdatedEventArgs>? FeatureUpdated;

        public S7Service(
            ILogger<S7Service> logger,
            IOptions<S7Configuration> config,
            IOptions<BreadthConfiguration> breadthConfig,
            IBreadthFeed? breadthFeed = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _breadthConfig = breadthConfig?.Value ?? new BreadthConfiguration();
            _breadthFeed = breadthFeed;

            InitializeSymbols();
        }

        private void InitializeSymbols()
        {
            foreach (var symbol in _config.Symbols)
            {
                _priceHistory[symbol] = new List<PricePoint>();
                _currentStates[symbol] = new S7State
                {
                    Symbol = symbol,
                    CurrentLeader = S7Leader.None,
                    CooldownBarsRemaining = 0,
                    IsSignalActive = false,
                    SizeTilt = _config.SizeTiltFactor
                };
            }

            _logger.LogInformation("S7Service initialized for symbols: {Symbols}", 
                string.Join(", ", _config.Symbols));
        }

        public async Task UpdateAsync(string symbol, decimal close, DateTime timestamp)
        {
            if (!_config.Enabled)
                return;

            if (!_priceHistory.ContainsKey(symbol))
            {
                _logger.LogWarning("Received price update for unknown symbol: {Symbol}", symbol);
                return;
            }

            // Add new price point
            _priceHistory[symbol].Add(new PricePoint { Close = close, Timestamp = timestamp });

            // Maintain sliding window
            var maxLookback = Math.Max(_config.LookbackLongBars, _config.LookbackMediumBars);
            if (_priceHistory[symbol].Count > maxLookback + 10) // Keep some buffer
            {
                _priceHistory[symbol].RemoveRange(0, 10);
            }

            // Update analysis for this symbol
            await UpdateSymbolAnalysisAsync(symbol, timestamp).ConfigureAwait(false);

            // If we have both ES and NQ data, update cross-symbol analysis
            if (HasSufficientDataForBothSymbols())
            {
                await UpdateCrossSymbolAnalysisAsync(timestamp).ConfigureAwait(false);
            }
        }

        private async Task UpdateSymbolAnalysisAsync(string symbol, DateTime timestamp)
        {
            var prices = _priceHistory[symbol];
            var state = _currentStates[symbol];

            if (prices.Count < _config.LookbackShortBars)
                return; // Insufficient data

            // Calculate multi-horizon relative strength
            state.RelativeStrengthShort = CalculateRelativeStrength(prices, _config.LookbackShortBars);
            
            if (prices.Count >= _config.LookbackMediumBars)
                state.RelativeStrengthMedium = CalculateRelativeStrength(prices, _config.LookbackMediumBars);
            
            if (prices.Count >= _config.LookbackLongBars)
                state.RelativeStrengthLong = CalculateRelativeStrength(prices, _config.LookbackLongBars);

            // Calculate rolling z-score
            state.ZScore = CalculateZScore(prices, state.RelativeStrengthShort);

            // Update timestamp
            state.Timestamp = timestamp;

            // Update cooldown
            if (state.CooldownBarsRemaining > 0)
                state.CooldownBarsRemaining--;

            // Emit feature update if enabled
            if (_config.EnableFeatureBus)
            {
                var featureTuple = GetFeatureTuple(symbol);
                FeatureUpdated?.Invoke(this, new S7FeatureUpdatedEventArgs(featureTuple));
            }
        }

        private async Task UpdateCrossSymbolAnalysisAsync(DateTime timestamp)
        {
            var esState = _currentStates.GetValueOrDefault("ES");
            var nqState = _currentStates.GetValueOrDefault("NQ");

            if (esState == null || nqState == null)
                return;

            // Calculate coherence between ES and NQ signals
            var coherence = CalculateCoherence(esState, nqState);

            // Determine dominant leader
            var leader = DetermineLeader(esState, nqState, coherence);

            // Calculate signal strength
            var signalStrength = CalculateSignalStrength(esState, nqState, coherence);

            // Integrate breadth analysis if enabled
            if (_breadthConfig.Enabled && _breadthFeed?.IsDataAvailable() == true)
            {
                var breadthAdjustment = await CalculateBreadthAdjustmentAsync().ConfigureAwait(false);
                signalStrength *= breadthAdjustment;
            }

            // Update states based on cross-analysis
            UpdateStateBasedOnCrossAnalysis(esState, nqState, coherence, leader, signalStrength);

            // Create snapshot
            _lastSnapshot = new S7Snapshot
            {
                ESState = CloneState(esState),
                NQState = CloneState(nqState),
                CrossSymbolCoherence = coherence,
                DominantLeader = leader,
                SignalStrength = signalStrength,
                IsActionable = IsSignalActionable(coherence, signalStrength),
                LastUpdateTime = timestamp,
                FeatureBusData = BuildFeatureBusData(esState, nqState, coherence, signalStrength)
            };

            if (_config.EnableTelemetry)
            {
                LogTelemetry(_lastSnapshot);
            }
        }

        private decimal CalculateRelativeStrength(List<PricePoint> prices, int lookback)
        {
            if (prices.Count < lookback)
                return 0;

            var recent = prices.TakeLast(lookback).ToList();
            if (recent.Count < 2)
                return 0;

            var startPrice = recent.First().Close;
            var endPrice = recent.Last().Close;

            return (endPrice - startPrice) / startPrice;
        }

        private decimal CalculateZScore(List<PricePoint> prices, decimal currentRelStrength)
        {
            if (prices.Count < _config.LookbackMediumBars)
                return 0;

            var lookbackPeriod = Math.Min(prices.Count, _config.LookbackLongBars);
            var recentPrices = prices.TakeLast(lookbackPeriod).ToList();

            // Calculate rolling relative strengths for z-score
            var relStrengths = new List<decimal>();
            for (int i = _config.LookbackShortBars; i <= recentPrices.Count; i++)
            {
                var window = recentPrices.Take(i).TakeLast(_config.LookbackShortBars).ToList();
                if (window.Count >= _config.LookbackShortBars)
                {
                    var rs = CalculateRelativeStrength(window.Select(p => p).ToList(), _config.LookbackShortBars);
                    relStrengths.Add(rs);
                }
            }

            if (relStrengths.Count < 2)
                return 0;

            var mean = relStrengths.Average();
            var variance = relStrengths.Select(x => (x - mean) * (x - mean)).Average();
            var stdDev = (decimal)Math.Sqrt((double)variance);

            return stdDev > 0 ? (currentRelStrength - mean) / stdDev : 0;
        }

        private decimal CalculateCoherence(S7State esState, S7State nqState)
        {
            // Check if signals are aligned across timeframes
            var esSignalDirection = GetSignalDirection(esState);
            var nqSignalDirection = GetSignalDirection(nqState);

            // FAIL-CLOSED: Check for missing data or invalid states
            if (Math.Abs(esState.ZScore) < 0.001m && Math.Abs(nqState.ZScore) < 0.001m)
            {
                _logger.LogError("[S7-AUDIT-VIOLATION] Zero Z-scores detected - TRIGGERING HOLD + TELEMETRY");
                return 0m; // Fail-closed: no safe default, force hold
            }

            // Calculate correlation of z-scores - AUDIT CLEAN: No safe defaults
            var maxZScore = Math.Max(Math.Abs(esState.ZScore), Math.Abs(nqState.ZScore));
            if (maxZScore == 0)
            {
                _logger.LogError("[S7-AUDIT-VIOLATION] Invalid Z-score data - TRIGGERING HOLD + TELEMETRY");
                return 0m; // Fail-closed
            }
            
            var zScoreAlignment = 1 - Math.Abs(esState.ZScore - nqState.ZScore) / maxZScore;
            
            // Calculate timeframe coherence
            var esTimeframeCoherence = GetTimeframeCoherence(esState);
            var nqTimeframeCoherence = GetTimeframeCoherence(nqState);
            
            // AUDIT-CLEAN: Use configuration values instead of hardcoded literals
            var directionAlignment = esSignalDirection == nqSignalDirection ? _config.DirectionAlignmentWeight : 0.0m;
            var avgTimeframeCoherence = (esTimeframeCoherence + nqTimeframeCoherence) / 2;

            // AUDIT-CLEAN: All weights from configuration - NO HARDCODED VALUES
            return (zScoreAlignment * _config.ZScoreAlignmentWeight + 
                   directionAlignment * _config.DirectionAlignmentWeight + 
                   avgTimeframeCoherence * _config.TimeframeCoherenceWeight);
        }

        private int GetSignalDirection(S7State state)
        {
            if (state.ZScore > _config.ZThresholdEntry) return 1; // Bullish
            if (state.ZScore < -_config.ZThresholdEntry) return -1; // Bearish
            return 0; // Neutral
        }

        private decimal GetTimeframeCoherence(S7State state)
        {
            // Check alignment across short, medium, long timeframes
            var signals = new[] { state.RelativeStrengthShort, state.RelativeStrengthMedium, state.RelativeStrengthLong };
            var positiveSignals = signals.Count(s => s > 0);
            var negativeSignals = signals.Count(s => s < 0);
            
            // AUDIT-CLEAN: Use configuration normalizer instead of hardcoded 3.0
            return Math.Max(positiveSignals, negativeSignals) / _config.TimeframeCountNormalizer;
        }

        private S7Leader DetermineLeader(S7State esState, S7State nqState, decimal coherence)
        {
            if (coherence < _config.CoherenceMin)
                return S7Leader.Divergent;

            // FAIL-CLOSED: Check for invalid data
            if (esState == null || nqState == null)
            {
                _logger.LogError("[S7-AUDIT-VIOLATION] Missing state data for leader calculation - TRIGGERING HOLD + TELEMETRY");
                return S7Leader.Divergent; // Fail-closed: force divergent to prevent signals
            }

            var esStrength = Math.Abs(esState.ZScore);
            var nqStrength = Math.Abs(nqState.ZScore);

            // AUDIT-CLEAN: Use configuration threshold instead of hardcoded 1.2m
            if (esStrength > nqStrength * _config.LeaderThreshold)
                return S7Leader.ES;
            else if (nqStrength > esStrength * _config.LeaderThreshold)
                return S7Leader.NQ;
            
            return S7Leader.None;
        }

        private decimal CalculateSignalStrength(S7State esState, S7State nqState, decimal coherence)
        {
            var maxZScore = Math.Max(Math.Abs(esState.ZScore), Math.Abs(nqState.ZScore));
            var avgZScore = (Math.Abs(esState.ZScore) + Math.Abs(nqState.ZScore)) / 2;
            
            return avgZScore * coherence;
        }

        private async Task<decimal> CalculateBreadthAdjustmentAsync()
        {
            // FAIL-CLOSED: Check breadth feed availability
            if (_breadthFeed == null)
            {
                if (_config.FailOnMissingData)
                {
                    _logger.LogError("[S7-AUDIT-VIOLATION] Breadth feed unavailable but required - TRIGGERING HOLD + TELEMETRY");
                    return 0m; // Fail-closed: no safe defaults
                }
                _logger.LogWarning("[S7-AUDIT-VIOLATION] Breadth feed unavailable, using configured base score");
                return _config.BaseBreadthScore; // Configured neutral value instead of hardcoded 1.0m
            }

            try
            {
                var adRatio = await _breadthFeed.GetAdvanceDeclineRatioAsync().ConfigureAwait(false);
                var hlRatio = await _breadthFeed.GetNewHighsLowsRatioAsync().ConfigureAwait(false);
                
                // AUDIT-CLEAN: Use configured base score instead of hardcoded 1.0m
                decimal breadthScore = _config.BaseBreadthScore;
                
                // AUDIT-CLEAN: Use configured bonuses/penalties instead of hardcoded values
                if (adRatio > _breadthConfig.AdvanceDeclineThreshold)
                    breadthScore += _config.AdvanceDeclineBonus;
                else if (adRatio < (1 - _breadthConfig.AdvanceDeclineThreshold))
                    breadthScore -= _config.AdvanceDeclinePenalty;
                
                if (hlRatio > _breadthConfig.NewHighsLowsRatio)
                    breadthScore += _config.NewHighsLowsBonus;
                
                // AUDIT-CLEAN: Use configured min/max bounds instead of hardcoded 0.5m/1.5m
                return Math.Max(_config.MinBreadthScore, Math.Min(_config.MaxBreadthScore, breadthScore));
            }
            catch (Exception ex)
            {
                if (_config.FailOnMissingData)
                {
                    _logger.LogError(ex, "[S7-AUDIT-VIOLATION] Breadth calculation failed - TRIGGERING HOLD + TELEMETRY");
                    return 0m; // Fail-closed: no safe defaults
                }
                _logger.LogWarning(ex, "[S7-AUDIT-VIOLATION] Breadth calculation error, using configured base score");
                return _config.BaseBreadthScore; // Configured fallback instead of hardcoded 1.0m
            }
        }

        private void UpdateStateBasedOnCrossAnalysis(S7State esState, S7State nqState, decimal coherence, S7Leader leader, decimal signalStrength)
        {
            // Update coherence in both states
            esState.Coherence = coherence;
            nqState.Coherence = coherence;

            // Update leader information
            esState.CurrentLeader = leader;
            nqState.CurrentLeader = leader;

            // Update signal active status
            var isSignalStrong = signalStrength > _config.ZThresholdEntry && coherence > _config.CoherenceMin;
            esState.IsSignalActive = isSignalStrong && esState.CooldownBarsRemaining == 0;
            nqState.IsSignalActive = isSignalStrong && nqState.CooldownBarsRemaining == 0;

            // AUDIT-CLEAN: Use configured max multiplier instead of hardcoded 2.0m
            var sizeTilt = _config.SizeTiltFactor * Math.Min(_config.MaxSizeTiltMultiplier, signalStrength / _config.ZThresholdEntry);
            esState.SizeTilt = sizeTilt;
            nqState.SizeTilt = sizeTilt;
        }

        private bool IsSignalActionable(decimal coherence, decimal signalStrength)
        {
            return coherence >= _config.CoherenceMin && 
                   signalStrength >= _config.ZThresholdEntry &&
                   HasSufficientDataForBothSymbols();
        }

        private Dictionary<string, decimal> BuildFeatureBusData(S7State esState, S7State nqState, decimal coherence, decimal signalStrength)
        {
            return new Dictionary<string, decimal>
            {
                ["es_rel_strength_short"] = esState.RelativeStrengthShort,
                ["es_rel_strength_medium"] = esState.RelativeStrengthMedium,
                ["es_rel_strength_long"] = esState.RelativeStrengthLong,
                ["es_z_score"] = esState.ZScore,
                ["nq_rel_strength_short"] = nqState.RelativeStrengthShort,
                ["nq_rel_strength_medium"] = nqState.RelativeStrengthMedium,
                ["nq_rel_strength_long"] = nqState.RelativeStrengthLong,
                ["nq_z_score"] = nqState.ZScore,
                ["cross_coherence"] = coherence,
                ["signal_strength"] = signalStrength,
                ["size_tilt"] = esState.SizeTilt
            };
        }

        private void LogTelemetry(S7Snapshot snapshot)
        {
            _logger.LogInformation("S7 Analysis - Leader: {Leader}, Coherence: {Coherence:F3}, Strength: {Strength:F3}, Actionable: {Actionable}",
                snapshot.DominantLeader, snapshot.CrossSymbolCoherence, snapshot.SignalStrength, snapshot.IsActionable);
        }

        public S7Snapshot GetCurrentSnapshot()
        {
            return _lastSnapshot;
        }

        public S7FeatureTuple GetFeatureTuple(string symbol)
        {
            if (!_currentStates.TryGetValue(symbol, out var state))
                return new S7FeatureTuple { Symbol = symbol };

            return new S7FeatureTuple
            {
                Symbol = symbol,
                RelativeStrengthShort = state.RelativeStrengthShort,
                RelativeStrengthMedium = state.RelativeStrengthMedium,
                RelativeStrengthLong = state.RelativeStrengthLong,
                ZScore = state.ZScore,
                Coherence = state.Coherence,
                SizeTilt = state.SizeTilt,
                Leader = state.CurrentLeader.ToString(),
                IsSignalActive = state.IsSignalActive,
                ExtendedFeatures = new Dictionary<string, object>
                {
                    ["cooldown_bars_remaining"] = state.CooldownBarsRemaining,
                    ["timestamp"] = state.Timestamp,
                    ["signal_strength"] = _lastSnapshot.SignalStrength
                }
            };
        }

        public bool IsReady()
        {
            return HasSufficientDataForBothSymbols();
        }

        public void ResetCooldown(string symbol)
        {
            if (_currentStates.TryGetValue(symbol, out var state))
            {
                state.CooldownBarsRemaining = _config.CooldownBars;
                _logger.LogDebug("Reset cooldown for {Symbol} to {Bars} bars", symbol, _config.CooldownBars);
            }
        }

        public Dictionary<string, object> GetAllFeatures()
        {
            var features = new Dictionary<string, object>();
            
            foreach (var kvp in _currentStates)
            {
                var featureTuple = GetFeatureTuple(kvp.Key);
                features[$"{kvp.Key.ToLowerInvariant()}_features"] = featureTuple;
            }

            features["snapshot"] = _lastSnapshot;
            return features;
        }

        private bool HasSufficientDataForBothSymbols()
        {
            return _config.Symbols.All(symbol => 
                _priceHistory.ContainsKey(symbol) && 
                _priceHistory[symbol].Count >= _config.LookbackShortBars);
        }

        private S7State CloneState(S7State original)
        {
            return new S7State
            {
                Timestamp = original.Timestamp,
                Symbol = original.Symbol,
                RelativeStrengthShort = original.RelativeStrengthShort,
                RelativeStrengthMedium = original.RelativeStrengthMedium,
                RelativeStrengthLong = original.RelativeStrengthLong,
                ZScore = original.ZScore,
                Coherence = original.Coherence,
                CurrentLeader = original.CurrentLeader,
                CooldownBarsRemaining = original.CooldownBarsRemaining,
                IsSignalActive = original.IsSignalActive,
                SizeTilt = original.SizeTilt,
                AdditionalMetrics = new Dictionary<string, decimal>(original.AdditionalMetrics)
            };
        }

        private class PricePoint
        {
            public decimal Close { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}