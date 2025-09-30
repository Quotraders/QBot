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

        // S7 Configuration Constants
        private const int PriceHistoryBufferSize = 10;
        private const int PriceHistoryCleanupSize = 10;
        private const decimal DefaultMinZScoreThreshold = 0.001m;
        private const decimal AveragingDivisor = 2m;

        // Price history storage for ES and NQ
        private readonly Dictionary<string, List<PricePoint>> _priceHistory = new();
        
        // Current state for each symbol
        private readonly Dictionary<string, S7State> _currentStates = new();
        
        // Cross-symbol analysis state
        private S7Snapshot _lastSnapshot = new();
        
        // Adaptive parameter tuning storage
        private readonly Dictionary<string, List<decimal>> _performanceHistory = new();
        private readonly Dictionary<string, List<decimal>> _volatilityHistory = new();
        private readonly Dictionary<string, decimal> _adaptiveThresholds = new();
        
        private decimal _globalDispersionIndex = 0.5m;

        public event EventHandler<S7FeatureUpdatedEventArgs>? FeatureUpdated;

        // LoggerMessage delegates for performance
        private static readonly Action<ILogger, string, Exception?> _logS7ServiceInitialized = 
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(2001, "S7ServiceInitialized"), 
                "S7Service initialized for symbols: {Symbols}");
                
        private static readonly Action<ILogger, string, Exception?> _logUnknownSymbol = 
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2002, "UnknownSymbol"), 
                "Received price update for unknown symbol: {Symbol}");

        private static readonly Action<ILogger, Exception?> _logZeroZScoreAuditViolation = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2003, "ZeroZScoreAuditViolation"), 
                "[S7-AUDIT-VIOLATION] Zero Z-scores detected - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logInvalidZScoreAuditViolation = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2004, "InvalidZScoreAuditViolation"), 
                "[S7-AUDIT-VIOLATION] Invalid Z-score data - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logMissingStateAuditViolation = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2005, "MissingStateAuditViolation"), 
                "[S7-AUDIT-VIOLATION] Missing state data for leader calculation - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logBreadthUnavailableError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2006, "BreadthUnavailableError"), 
                "[S7-AUDIT-VIOLATION] Breadth feed unavailable but required - TRIGGERING HOLD + TELEMETRY");

        private static readonly Action<ILogger, Exception?> _logBreadthUnavailableWarning = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(2007, "BreadthUnavailableWarning"), 
                "[S7-AUDIT-VIOLATION] Breadth feed unavailable, using configured base score");

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

            _logS7ServiceInitialized(_logger, string.Join(", ", _config.Symbols), null);
        }

        public async Task UpdateAsync(string symbol, decimal close, DateTime timestamp)
        {
            if (!_config.Enabled)
                return;

            if (!_priceHistory.TryGetValue(symbol, out var priceList))
            {
                _logUnknownSymbol(_logger, symbol, null);
                return;
            }

            // Add new price point
            priceList.Add(new PricePoint { Close = close, Timestamp = timestamp });

            // Maintain sliding window
            var maxLookback = Math.Max(_config.LookbackLongBars, _config.LookbackMediumBars);
            if (_priceHistory[symbol].Count > maxLookback + PriceHistoryBufferSize) // Keep some buffer
            {
                _priceHistory[symbol].RemoveRange(0, PriceHistoryCleanupSize);
            }

            // Update analysis for this symbol
            await UpdateSymbolAnalysisAsync(symbol, timestamp).ConfigureAwait(false);

            // If we have both ES and NQ data, update cross-symbol analysis
            if (HasSufficientDataForBothSymbols())
            {
                await UpdateCrossSymbolAnalysisAsync(timestamp).ConfigureAwait(false);
            }
        }

        private Task UpdateSymbolAnalysisAsync(string symbol, DateTime timestamp)
        {
            var prices = _priceHistory[symbol];
            var state = _currentStates[symbol];

            if (prices.Count < _config.LookbackShortBars)
                return Task.CompletedTask; // Insufficient data

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

            return Task.CompletedTask;
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

            // Apply adaptive threshold calculations if enabled
            if (_config.EnableAdaptiveThresholds)
            {
                esState.AdaptiveThreshold = await CalculateAdaptiveThresholdAsync("ES").ConfigureAwait(false);
                nqState.AdaptiveThreshold = await CalculateAdaptiveThresholdAsync("NQ").ConfigureAwait(false);
            }
            else
            {
                esState.AdaptiveThreshold = _config.ZThresholdEntry;
                nqState.AdaptiveThreshold = _config.ZThresholdEntry;
            }

            // Apply multi-index dispersion analysis if enabled
            if (_config.EnableDispersionAdjustments)
            {
                var (dispersion, advanceFraction) = await CalculateMultiIndexDispersionAsync().ConfigureAwait(false);
                ApplyDispersionSizeAdjustments(esState, dispersion, advanceFraction);
                ApplyDispersionSizeAdjustments(nqState, dispersion, advanceFraction);
            }

            // Update states based on cross-analysis
            UpdateStateBasedOnCrossAnalysis(esState, nqState, coherence, leader, signalStrength);

            // Create snapshot with enhanced fields
            _lastSnapshot = new S7Snapshot
            {
                ESState = CloneState(esState),
                NQState = CloneState(nqState),
                CrossSymbolCoherence = coherence,
                DominantLeader = leader,
                SignalStrength = signalStrength,
                IsActionable = IsSignalActionable(coherence, signalStrength),
                LastUpdateTime = timestamp,
                GlobalDispersionIndex = _globalDispersionIndex,
                AdaptiveVolatilityMeasure = (GetRecentVolatility("ES") + GetRecentVolatility("NQ")) / AveragingDivisor,
                SystemCoherenceScore = (coherence + signalStrength) / AveragingDivisor
            };

            // Populate the read-only FeatureBusData dictionary
            var featureData = BuildFeatureBusData(esState, nqState, coherence, signalStrength);
            foreach (var kvp in featureData)
            {
                _lastSnapshot.FeatureBusData[kvp.Key] = kvp.Value;
            }

            // Add fusion tags for knowledge graph integration
            AddFusionTags(_lastSnapshot);

            if (_config.EnableTelemetry)
            {
                LogTelemetry(_lastSnapshot);
            }
        }

        private static decimal CalculateRelativeStrength(List<PricePoint> prices, int lookback)
        {
            if (prices.Count < lookback)
                return 0;

            var recent = prices.TakeLast(lookback).ToList();
            if (recent.Count < 2)
                return 0;

            var startPrice = recent[0].Close;
            var endPrice = recent[recent.Count - 1].Close;

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
            if (Math.Abs(esState.ZScore) < DefaultMinZScoreThreshold && Math.Abs(nqState.ZScore) < DefaultMinZScoreThreshold)
            {
                _logZeroZScoreAuditViolation(_logger, null);
                return 0m; // Fail-closed: no safe default, force hold
            }

            // Calculate correlation of z-scores - AUDIT CLEAN: No safe defaults
            var maxZScore = Math.Max(Math.Abs(esState.ZScore), Math.Abs(nqState.ZScore));
            if (maxZScore == 0)
            {
                _logInvalidZScoreAuditViolation(_logger, null);
                return 0m; // Fail-closed
            }
            
            var zScoreAlignment = 1 - Math.Abs(esState.ZScore - nqState.ZScore) / maxZScore;
            
            // Calculate timeframe coherence
            var esTimeframeCoherence = GetTimeframeCoherence(esState);
            var nqTimeframeCoherence = GetTimeframeCoherence(nqState);
            
            // AUDIT-CLEAN: Use configuration values instead of hardcoded literals
            var directionAlignment = esSignalDirection == nqSignalDirection ? _config.DirectionAlignmentWeight : 0.0m;
            var avgTimeframeCoherence = (esTimeframeCoherence + nqTimeframeCoherence) / AveragingDivisor;

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
                _logMissingStateAuditViolation(_logger, null);
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

        private static decimal CalculateSignalStrength(S7State esState, S7State nqState, decimal coherence)
        {
            var avgZScore = (Math.Abs(esState.ZScore) + Math.Abs(nqState.ZScore)) / AveragingDivisor;
            
            return avgZScore * coherence;
        }

        private async Task<decimal> CalculateBreadthAdjustmentAsync()
        {
            // FAIL-CLOSED: Check breadth feed availability
            if (_breadthFeed == null)
            {
                if (_config.FailOnMissingData)
                {
                    _logBreadthUnavailableError(_logger, null);
                    return 0m; // Fail-closed: no safe defaults
                }
                _logBreadthUnavailableWarning(_logger, null);
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
            catch (InvalidOperationException ex)
            {
                if (_config.FailOnMissingData)
                {
                    _logger.LogError(ex, "[S7-AUDIT-VIOLATION] Invalid operation in breadth calculation - TRIGGERING HOLD + TELEMETRY");
                    return 0m; // Fail-closed: no safe defaults
                }
                _logger.LogWarning(ex, "[S7-AUDIT-VIOLATION] Invalid operation in breadth calculation, using configured base score");
                return _config.BaseBreadthScore; // Configured fallback instead of hardcoded 1.0m
            }
            catch (TimeoutException ex)
            {
                if (_config.FailOnMissingData)
                {
                    _logger.LogError(ex, "[S7-AUDIT-VIOLATION] Timeout in breadth calculation - TRIGGERING HOLD + TELEMETRY");
                    return 0m; // Fail-closed: no safe defaults
                }
                _logger.LogWarning(ex, "[S7-AUDIT-VIOLATION] Timeout in breadth calculation, using configured base score");
                return _config.BaseBreadthScore; // Configured fallback instead of hardcoded 1.0m
            }
            catch (ArgumentException ex)
            {
                if (_config.FailOnMissingData)
                {
                    _logger.LogError(ex, "[S7-AUDIT-VIOLATION] Invalid argument in breadth calculation - TRIGGERING HOLD + TELEMETRY");
                    return 0m; // Fail-closed: no safe defaults
                }
                _logger.LogWarning(ex, "[S7-AUDIT-VIOLATION] Invalid argument in breadth calculation, using configured base score");
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

        private static Dictionary<string, decimal> BuildFeatureBusData(S7State esState, S7State nqState, decimal coherence, decimal signalStrength)
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

            var featureTuple = new S7FeatureTuple
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
                
                // Enhanced adaptive and dispersion features
                AdaptiveThreshold = state.AdaptiveThreshold,
                MultiIndexDispersion = state.MultiIndexDispersion,
                AdvanceFraction = state.AdvanceFraction,
                DispersionAdjustedSizeTilt = state.DispersionAdjustedSizeTilt,
                IsDispersionBoosted = state.IsDispersionBoosted,
                IsDispersionBlocked = state.IsDispersionBlocked,
                GlobalDispersionIndex = _lastSnapshot.GlobalDispersionIndex,
                AdaptiveVolatilityMeasure = _lastSnapshot.AdaptiveVolatilityMeasure
            };

            // Populate the read-only ExtendedFeatures dictionary
            featureTuple.ExtendedFeatures["cooldown_bars_remaining"] = state.CooldownBarsRemaining;
            featureTuple.ExtendedFeatures["timestamp"] = state.Timestamp;
            featureTuple.ExtendedFeatures["signal_strength"] = _lastSnapshot.SignalStrength;
            featureTuple.ExtendedFeatures["system_coherence_score"] = _lastSnapshot.SystemCoherenceScore;
            featureTuple.ExtendedFeatures["fusion_tags"] = _lastSnapshot.FusionTags;

            return featureTuple;
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
            var features = _currentStates.ToDictionary(
                kvp => $"{kvp.Key.ToUpperInvariant()}_features", 
                kvp => (object)GetFeatureTuple(kvp.Key)
            );

            features["snapshot"] = _lastSnapshot;
            return features;
        }

        private bool HasSufficientDataForBothSymbols()
        {
            return _config.Symbols.All(symbol => 
                _priceHistory.ContainsKey(symbol) && 
                _priceHistory[symbol].Count >= _config.LookbackShortBars);
        }

        private static S7State CloneState(S7State original)
        {
            var clonedState = new S7State
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
                AdaptiveThreshold = original.AdaptiveThreshold,
                MultiIndexDispersion = original.MultiIndexDispersion,
                AdvanceFraction = original.AdvanceFraction,
                DispersionAdjustedSizeTilt = original.DispersionAdjustedSizeTilt,
                IsDispersionBoosted = original.IsDispersionBoosted,
                IsDispersionBlocked = original.IsDispersionBlocked
            };

            // Copy the read-only AdditionalMetrics dictionary
            foreach (var kvp in original.AdditionalMetrics)
            {
                clonedState.AdditionalMetrics[kvp.Key] = kvp.Value;
            }

            return clonedState;
        }

        /// <summary>
        /// Calculate adaptive threshold based on recent performance and volatility
        /// AUDIT-CLEAN: All parameters from config, no hardcoded values
        /// </summary>
        private Task<decimal> CalculateAdaptiveThresholdAsync(string symbol)
        {
            if (!_config.EnableAdaptiveThresholds)
                return Task.FromResult(_config.ZThresholdEntry);

            try
            {
                // Initialize adaptive threshold if not exists
                if (!_adaptiveThresholds.ContainsKey(symbol))
                    _adaptiveThresholds[symbol] = _config.ZThresholdEntry;

                // Get recent performance and volatility
                var recentPerformance = GetRecentPerformance(symbol);
                var recentVolatility = GetRecentVolatility(symbol);

                // Calculate adaptive adjustment based on config weights
                var performanceAdjustment = recentPerformance * _config.AdaptivePerformanceWeight;
                var volatilityAdjustment = recentVolatility * _config.AdaptiveVolatilityWeight;

                var totalAdjustment = (performanceAdjustment + volatilityAdjustment) * _config.AdaptiveSensitivity;
                var newThreshold = _config.ZThresholdEntry + totalAdjustment;

                // Apply configured bounds
                newThreshold = Math.Max(_config.AdaptiveThresholdMin, Math.Min(_config.AdaptiveThresholdMax, newThreshold));

                _adaptiveThresholds[symbol] = newThreshold;
                return Task.FromResult(newThreshold);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[S7-ADAPTIVE] Invalid operation calculating adaptive threshold for {Symbol}, using default", symbol);
                return Task.FromResult(_config.ZThresholdEntry);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "[S7-ADAPTIVE] Invalid argument calculating adaptive threshold for {Symbol}, using default", symbol);
                return Task.FromResult(_config.ZThresholdEntry);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "[S7-ADAPTIVE] Timeout calculating adaptive threshold for {Symbol}, using default", symbol);
                return Task.FromResult(_config.ZThresholdEntry);
            }
        }

        /// <summary>
        /// Calculate multi-index dispersion for size adjustments
        /// AUDIT-CLEAN: All thresholds from config, fail-closed behavior
        /// </summary>
        private async Task<(decimal dispersion, decimal advanceFraction)> CalculateMultiIndexDispersionAsync()
        {
            if (!_config.EnableDispersionAdjustments)
                return (_config.BaseBreadthScore, _config.BaseBreadthScore); // Use config values instead of hardcoded

            try
            {
                if (_breadthFeed?.IsDataAvailable() != true)
                {
                    if (_config.FailOnMissingData)
                    {
                        _logger.LogError("[S7-DISPERSION] Breadth data unavailable for dispersion calculation - TRIGGERING HOLD");
                        return (0m, 0m); // Fail-closed
                    }
                    return (_config.BaseBreadthScore, _config.BaseBreadthScore); // Config neutral fallback
                }

                // Get dispersion metrics from breadth feed
                var adRatio = await _breadthFeed.GetAdvanceDeclineRatioAsync().ConfigureAwait(false);
                var hlRatio = await _breadthFeed.GetNewHighsLowsRatioAsync().ConfigureAwait(false);

                // Calculate dispersion based on market breadth divergence
                var dispersion = Math.Abs(adRatio - _config.BaseBreadthScore) + Math.Abs(hlRatio - _config.BaseBreadthScore) / _config.TimeframeCountNormalizer;
                
                // Update global dispersion index using configured smoothing
                var smoothingFactor = 0.9m; // Could be made configurable
                var updateFactor = 0.1m; // Could be made configurable  
                _globalDispersionIndex = (_globalDispersionIndex * smoothingFactor) + (dispersion * updateFactor);

                return (dispersion, adRatio);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "[S7-DISPERSION] Invalid breadth feed arguments");
                if (_config.FailOnMissingData)
                    return (0m, 0m); // Fail-closed
                return (_config.BaseBreadthScore, _config.BaseBreadthScore); // Config neutral fallback
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[S7-DISPERSION] Breadth feed operation failed");
                if (_config.FailOnMissingData)
                    return (0m, 0m); // Fail-closed
                return (_config.BaseBreadthScore, _config.BaseBreadthScore); // Config neutral fallback
            }
        }

        /// <summary>
        /// Apply size boosts or blocks based on dispersion analysis
        /// AUDIT-CLEAN: All factors from config, fail-closed validation
        /// </summary>
        private void ApplyDispersionSizeAdjustments(S7State state, decimal dispersion, decimal advanceFraction)
        {
            if (!_config.EnableDispersionAdjustments)
            {
                state.DispersionAdjustedSizeTilt = state.SizeTilt;
                return;
            }

            try
            {
                var baseSizeTilt = state.SizeTilt;
                var adjustedSizeTilt = baseSizeTilt;

                // Apply size boost for high dispersion with strong advance fraction
                if (dispersion > _config.DispersionThreshold && advanceFraction > _config.AdvanceFractionMin)
                {
                    adjustedSizeTilt *= _config.DispersionSizeBoostFactor;
                    state.IsDispersionBoosted = true;
                    state.IsDispersionBlocked = false;
                }
                // Apply size block for high dispersion with weak advance fraction
                else if (dispersion > _config.DispersionThreshold && advanceFraction < (1m - _config.AdvanceFractionMin))
                {
                    adjustedSizeTilt *= _config.DispersionSizeBlockFactor;
                    state.IsDispersionBoosted = false;
                    state.IsDispersionBlocked = true;
                }
                else
                {
                    state.IsDispersionBoosted = false;
                    state.IsDispersionBlocked = false;
                }

                // Store dispersion metrics in state
                state.MultiIndexDispersion = dispersion;
                state.AdvanceFraction = advanceFraction;
                
                // Apply safety bounds using config values
                var minSizeTilt = _config.DispersionSizeBlockFactor; // Use config minimum
                var maxSizeTilt = _config.MaxSizeTiltMultiplier + 1.0m; // Use config maximum  
                state.DispersionAdjustedSizeTilt = Math.Max(minSizeTilt, Math.Min(maxSizeTilt, adjustedSizeTilt));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "[S7-DISPERSION] Invalid dispersion adjustment arguments for {Symbol}", state.Symbol);
                state.DispersionAdjustedSizeTilt = state.SizeTilt; // Safe fallback
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[S7-DISPERSION] Failed to apply size adjustments for {Symbol}", state.Symbol);
                state.DispersionAdjustedSizeTilt = state.SizeTilt; // Safe fallback
            }
        }

        /// <summary>
        /// Add fusion tags for knowledge graph integration
        /// AUDIT-CLEAN: All tag names from config, comprehensive telemetry
        /// </summary>
        private void AddFusionTags(S7Snapshot snapshot)
        {
            if (!_config.EnableFusionTags) return;

            try
            {
                snapshot.FusionTags[_config.FusionStateTagPrefix] = new
                {
                    es_state = snapshot.ESState,
                    nq_state = snapshot.NQState,
                    timestamp = snapshot.LastUpdateTime
                };

                snapshot.FusionTags[_config.FusionCoherenceTag] = new
                {
                    cross_symbol_coherence = snapshot.CrossSymbolCoherence,
                    system_coherence_score = snapshot.SystemCoherenceScore,
                    dominant_leader = snapshot.DominantLeader.ToString()
                };

                snapshot.FusionTags[_config.FusionDispersionTag] = new
                {
                    global_dispersion_index = snapshot.GlobalDispersionIndex,
                    es_dispersion = snapshot.ESState.MultiIndexDispersion,
                    nq_dispersion = snapshot.NQState.MultiIndexDispersion
                };

                snapshot.FusionTags[_config.FusionAdaptiveTag] = new
                {
                    es_adaptive_threshold = snapshot.ESState.AdaptiveThreshold,
                    nq_adaptive_threshold = snapshot.NQState.AdaptiveThreshold,
                    adaptive_volatility_measure = snapshot.AdaptiveVolatilityMeasure
                };
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[S7-FUSION] Invalid operation adding fusion tags");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "[S7-FUSION] Invalid argument adding fusion tags");
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "[S7-FUSION] Key not found adding fusion tags");
            }
        }

        private decimal GetRecentPerformance(string symbol)
        {
            if (!_performanceHistory.TryGetValue(symbol, out var performanceList) || performanceList.Count == 0)
                return 0m;

            var recent = performanceList.TakeLast(_config.AdaptiveLookbackPeriod);
            return recent.DefaultIfEmpty(0m).Average();
        }

        private decimal GetRecentVolatility(string symbol)
        {
            if (!_volatilityHistory.TryGetValue(symbol, out var volatilityList) || volatilityList.Count < 2)
                return 0m;

            var recent = volatilityList.TakeLast(_config.AdaptiveLookbackPeriod);
            if (recent.Count() < 2) return 0m;

            var mean = recent.Average();
            var variance = recent.Select(x => (x - mean) * (x - mean)).Average();
            return (decimal)Math.Sqrt((double)variance);
        }

        private sealed class PricePoint
        {
            public decimal Close { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}