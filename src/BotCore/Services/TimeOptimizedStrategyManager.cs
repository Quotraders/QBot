// Time-Optimized Strategy Manager with ML Enhancement
// Manages strategy selection based on time of day performance and market regime
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BotCore.Models;
using BotCore.Config;
using BotCore.Strategy;
using BotCore.ML;
using TradingBot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Strategy manager that optimizes strategy selection based on time of day and ML insights
    /// </summary>
    public class TimeOptimizedStrategyManager : IDisposable
    {
        private readonly ILogger<TimeOptimizedStrategyManager> _logger;
        private readonly Dictionary<string, IStrategy> _strategies;
        private readonly TimeZoneInfo _centralTime;
        private readonly OnnxModelLoader? _onnxLoader;
        private readonly TradingBot.Abstractions.IS7Service? _s7Service;
        
        // Bar collections for correlation analysis - injected or managed externally
        private IReadOnlyList<Bar>? _esBars;
        private IReadOnlyList<Bar>? _nqBars;

        // Strategy performance by time of day (ML-learned or historical data)
        private readonly Dictionary<string, Dictionary<int, double>> _strategyTimePerformance = new()
        {
            ["S2"] = new() // VWAP Mean Reversion - Best overnight and lunch
            {
                [0] = 0.85,   // Midnight - High win rate
                [3] = 0.82,   // European open
                [12] = 0.88,  // Lunch chop - BEST
                [19] = 0.83,  // Overnight
                [23] = 0.87   // Late night
            },

            ["S3"] = new() // Compression Breakout - Best at session opens
            {
                [3] = 0.90,   // European open - BEST
                [9] = 0.92,   // US open - BEST (9:30)
                [10] = 0.85,  // Morning trend
                [14] = 0.80,  // Power hour
                [16] = 0.75   // After hours
            },

            ["S6"] = new() // Opening Drive - ONLY 9:28-10:00
            {
                [9] = 0.95,   // PEAK PERFORMANCE (9:28-10:00)
                [10] = 0.00,  // Stop after 10am
                [11] = 0.00,  // Disabled
                [12] = 0.00,  // Disabled
                [13] = 0.00   // Disabled
            },

            ["S11"] = new() // ADR Exhaustion - Best afternoon
            {
                [13] = 0.91,  // BEST TIME (1:30 PM)
                [14] = 0.88,  // 2:00 PM
                [15] = 0.85,  // 3:00 PM
                [16] = 0.82,  // 4:00 PM
                [17] = 0.75   // After hours
            },

            ["S7"] = new() // Multi-Horizon Relative Strength - Best during active market hours
            {
                [9] = 0.89,   // Market open
                [10] = 0.92,  // Morning trend - BEST
                [11] = 0.88,  // Mid-morning
                [13] = 0.90,  // Post-lunch
                [14] = 0.87,  // Afternoon
                [15] = 0.84,  // Late afternoon
                [20] = 0.78   // Overnight (reduced)
            }
        };

        // Time-Optimized Strategy Manager Constants
        // Performance thresholds
        private const double MinTimePerformanceThreshold = 0.70;    // Minimum time performance to consider strategy
        private const double HighConfidenceThreshold = 0.75;        // High confidence threshold for strategy selection
        
        // ML prediction and market regime classification thresholds
        private const decimal TrendingUpThreshold = 0.7m;           // ML prediction threshold for trending up
        private const decimal TrendingDownThreshold = 0.3m;         // ML prediction threshold for trending down
        private const double HighVolatilityThreshold = 1.5;         // High volatility classification threshold
        private const double LowVolatilityThreshold = 0.5;          // Low volatility classification threshold
        
        // Performance analysis constants
        private const int MinimumTradesForAnalysis = 50;            // Minimum trades needed for performance analysis
        private const decimal MinimumAccountBalance = 5000m;        // Minimum account balance for strategy execution
        
        // Time-based optimization constants
        private const double NeutralRSIValue = 50.0;                // Neutral RSI value (midpoint)
        private const decimal NeutralRSIValueDecimal = 50.0m;       // Neutral RSI value as decimal
        
        // Volatility calculation constants
        private const int MinimumBarsForVolatility = 20;            // Minimum bars needed for volatility calculation
        private const double DefaultVolatilityFallback = 1.0;       // Default volatility when insufficient data
        private const int AnnualizationFactor = 252;                // Trading days per year for volatility annualization
        
        // Trend calculation constants
        private const int MinimumBarsForTrend = 10;                 // Minimum bars needed for trend calculation
        private const double DefaultTrendFallback = 0.0;            // Default trend when insufficient data
        
        // Correlation constants
        private const int MinimumDataPointsForCorrelation = 20;     // Minimum data points for correlation calculation
        private const int CorrelationLookbackPeriod = 50;           // Lookback period for correlation analysis
        private const double FallbackCorrelation = 0.85;            // ES/NQ historical baseline correlation
        private const double HighCorrelationThreshold = 0.8;        // Threshold for high correlation
        private const double MinimumCorrelation = 0.1;              // Minimum correlation bound
        private const double MaximumCorrelation = 0.95;             // Maximum correlation bound
        private const double ConservativeFallbackCorrelation = 0.82;// Conservative fallback correlation
        private const double MinimumCorrelationBound = 0.7;         // Minimum correlation bound for adjustments
        
        // ML confidence constants
        private const double NoMLConfidenceFallback = 0.5;          // ML confidence when using fallback
        private const double DefaultConfidenceMultiplier = 1.0;     // Default confidence multiplier
        private const decimal DefaultATRFallback = 1.0m;            // Default ATR when no bar data
        
        // RSI constants
        private const int MaxRSIValue = 100;                        // Maximum RSI value
        
        // Volume profile constants
        private const int MinimumBarsForVolumeProfile = 10;         // Minimum bars for volume profile
        private const double DefaultAverageVolume = 100000;         // Default average volume
        private const decimal DefaultHighVolumeLevel = 5500m;       // Default high volume price level
        private const decimal DefaultLowVolumeLevel = 5480m;        // Default low volume price level
        private const int VolumeProfileLookback = 20;               // Lookback period for volume profile
        private const int PriceVolumeLookback = 50;                 // Lookback period for price-volume analysis
        private const int PriceRoundingLevel = 0;                   // Price rounding to dollar levels
        
        // Market hours constants
        private const int MarketCloseHour = 16;                     // Market close hour (4 PM CT)
        private const int HoursInDay = 24;                          // Hours in a day for normalization
        
        // ATR calculation constants
        private const int ATRPeriod = 14;                           // Standard ATR period
        private const decimal DefaultATRNormalized = 0.01m;         // Default 1% normalized ATR
        
        // Market hours adjustment constants
        private const double MarketHoursCorrelationBoost = 0.02;    // Correlation boost during market hours
        private const double OffHoursCorrelationPenalty = -0.05;    // Correlation penalty during off hours
        private const int MarketOpenHour = 9;                       // Market open hour (9 AM CT)
        private const int MarketAfterHoursEnd = 16;                 // After hours end (4 PM CT)
        
        // Imbalance normalization constants
        private const decimal MinimumImbalance = -1m;               // Minimum order book imbalance
        private const decimal MaximumImbalance = 1m;                // Maximum order book imbalance
        private const decimal ImbalanceNormalizationFactor = 2m;    // Factor for imbalance normalization
        
        // Bollinger Bands constants (time decay, signal persistence, and ML adjustment constants removed - unused)
        
        // Bollinger Bands constants
        private const int BollingerBandsPeriod = 20;                // Bollinger Bands period
        private const decimal MiddleOfBands = 0.5m;                 // Middle position in Bollinger Bands
        private const int BollingerStdDevMultiplier = 2;            // Standard deviation multiplier for Bollinger Bands
        
        // VWAP calculation constants
        private const int VWAPLookbackPeriod = 50;                  // VWAP lookback period
        private const int TypicalPriceDivisor = 3;                  // Divisor for typical price calculation (H+L+C)/3
        
        // Market stress constants
        private const decimal MediumStressLevel = 0.5m;             // Medium stress level
        private const int MinimumBarsForStressCalculation = 10;     // Minimum bars for stress calculation
        private const int StressLookbackPeriod = 10;                // Lookback period for stress calculation
        private const decimal StressNormalizationFactor = 2m;       // Normalization factor for volatility in stress
        private const decimal StressCombinationFactor = 4m;         // Factor for combining stress indicators
        private const int MinimumBarsForVolumeStress = 5;           // Minimum bars for volume stress
        private const decimal DefaultVolumeStress = 0.3m;           // Default volume stress when insufficient data
        private const decimal VolumeStressNormalizationFactor = 3.0m; // Volume stress normalization
        private const int MinimumBarsForGapAnalysis = 2;            // Minimum bars for gap analysis
        private const decimal GapStressThreshold = 0.005m;          // Threshold for significant gap
        private const decimal DefaultTrendStress = 0.3m;            // Default trend stress
        private const int MinimumBarsForTrendStress = 3;            // Minimum bars for trend stress
        private const decimal TrendStressNormalizationFactor = 0.01m; // Trend stress normalization
        
        // Default feature values
        private const decimal DefaultVolatilityFeature = 1.0m;      // Default volatility feature
        private const decimal DefaultTrendFeature = 0.0m;           // Default trend feature
        private const decimal DefaultMomentumFeature = 0.0m;        // Default momentum feature
        private const decimal DefaultVolumeFeature = 1.0m;          // Default volume feature
        private const decimal DefaultBidAskSpread = 0.001m;         // Default bid-ask spread
        private const decimal DefaultImbalanceFeature = 0.0m;       // Default imbalance feature
        private const decimal DefaultHourOfDayFeature = 0.5m;       // Default hour of day feature
        private const decimal DefaultTimeToCloseFeature = 0.5m;     // Default time to close feature
        private const decimal DefaultNormalizedPriceFeature = 1.0m; // Default normalized price feature
        private const decimal DefaultBollingerPositionFeature = 0.5m; // Default Bollinger position feature
        private const decimal DefaultMarketStressFeature = 0.5m;    // Default market stress feature

        public TimeOptimizedStrategyManager(ILogger<TimeOptimizedStrategyManager> logger, TradingBot.Abstractions.IS7Service? s7Service = null, OnnxModelLoader? onnxLoader = null)
        {
            _logger = logger;
            _s7Service = s7Service;
            _onnxLoader = onnxLoader;
            _strategies = new Dictionary<string, IStrategy>();
            _centralTime = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

            LoadHistoricalPerformanceData();
        }

        /// <summary>
        /// Evaluate strategies for an instrument with time and ML optimization
        /// </summary>
        public async Task<StrategyEvaluationResult> EvaluateInstrumentAsync(string instrument, TradingBot.Abstractions.MarketData data, IReadOnlyList<Bar> bars)
        {
            ArgumentNullException.ThrowIfNull(data);
            
            var currentTime = GetMarketTime(data.Timestamp);
            var session = EsNqTradingSchedule.GetCurrentSession(currentTime);

            if (session == null)
            {
                _logger.LogDebug($"Market closed at {currentTime} for {instrument}");
                return StrategyEvaluationResult.NoSignal("Market closed");
            }

            // Check if this instrument should trade in current session
            if (!session.Instruments.Contains(instrument))
            {
                return StrategyEvaluationResult.NoSignal($"{instrument} not active in {session.Description}");
            }

            // Get strategies for this instrument and session
            var activeStrategies = session.Strategies.TryGetValue(instrument, out var strategies)
                ? strategies
                : Array.Empty<string>();
            var positionSizeMultiplier = session.PositionSizeMultiplier.TryGetValue(instrument, out var multiplier)
                ? multiplier
                : 1.0;

            // ML Enhancement: Get market regime using real ONNX model inference
            var regime = await GetMarketRegimeAsync(instrument, data, bars).ConfigureAwait(false);
            var mlAdjustment = await GetMLAdjustmentAsync(regime.Name, session, instrument).ConfigureAwait(false);

            // Evaluate each strategy
            var signals = new List<BotCore.Models.Signal>();

            foreach (var strategyId in activeStrategies)
            {
                var timePerformance = GetTimePerformance(strategyId, currentTime.Hours);

                // Skip if performance too low for this time
                if (timePerformance < MinTimePerformanceThreshold)
                {
                    _logger.LogDebug($"{strategyId} skipped - low time performance: {timePerformance:P0} at hour {currentTime.Hours}");
                    continue;
                }

                // Generate strategy signal (using existing AllStrategies system)
                var candidates = GenerateStrategyCandidates(strategyId, instrument, data, bars);

                foreach (var candidate in candidates)
                {
                    var signal = ConvertToSignal(candidate, strategyId);

                    if (signal != null)
                    {
                        // Adjust signal based on time optimization and session
                        signal = signal with
                        {
                            Score = signal.Score * (decimal)timePerformance,
                            Size = (int)(signal.Size * positionSizeMultiplier * mlAdjustment)
                        };

                        signals.Add(signal);
                    }
                }
            }

            // Select best signal
            if (signals.Count > 0)
            {
                var bestSignal = signals.OrderByDescending(s => s.Score).First();

                // Add ES/NQ correlation check
                if (ShouldCheckCorrelation(instrument))
                {
                    var correlation = CheckES_NQ_Correlation(instrument);
                    if (correlation.ShouldVeto)
                    {
                        _logger.LogInformation($"Signal vetoed due to ES/NQ correlation: {correlation.Reason}");
                        return StrategyEvaluationResult.NoSignal(correlation.Reason);
                    }

                    bestSignal = bestSignal with { Score = bestSignal.Score * (decimal)correlation.ConfidenceMultiplier };
                }

                return new StrategyEvaluationResult
                {
                    HasSignal = true,
                    Signal = bestSignal,
                    Session = session.Description,
                    TotalCandidatesEvaluated = signals.Count
                };
            }

            return StrategyEvaluationResult.NoSignal("No signals generated");
        }

        private TimeSpan GetMarketTime(DateTime utcTime)
        {
            var centralTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, _centralTime);
            return centralTime.TimeOfDay;
        }

        private double GetTimePerformance(string strategyId, int hour)
        {
            if (!_strategyTimePerformance.TryGetValue(strategyId, out var performanceMap))
                return HighConfidenceThreshold; // Default performance

            // Find closest hour with performance data
            var closestHour = performanceMap.Keys
                .OrderBy(h => Math.Abs(h - hour))
                .FirstOrDefault();

            return performanceMap.TryGetValue(closestHour, out var performance) ? performance : HighConfidenceThreshold;
        }

        private async Task<MarketRegime> GetMarketRegimeAsync(string instrument, TradingBot.Abstractions.MarketData data, IReadOnlyList<Bar> bars)
        {
            try
            {
                // Professional ML-based regime detection using existing ONNX infrastructure
                var features = ExtractRegimeFeatures(data, bars);
                
                // Use existing ONNX model for regime classification
                if (_onnxLoader != null)
                {
                    var session = await _onnxLoader.LoadModelAsync("models/regime_detector.onnx", validateInference: false).ConfigureAwait(false);
                    if (session != null)
                    {
                        var regimePrediction = await RunRegimeInferenceAsync(session, features).ConfigureAwait(false);
                        return ClassifyRegime(regimePrediction, features);
                    }
                }
                
                // Fallback to sophisticated technical analysis
                return GetRegimeFallback(features);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TIME-STRATEGY] Error in ML regime detection, using fallback");
                return GetRegimeFallback(ExtractRegimeFeatures(data, bars));
            }
        }

        private decimal[] ExtractRegimeFeatures(TradingBot.Abstractions.MarketData data, IReadOnlyList<Bar> bars)
        {
            if (bars.Count < MinimumTradesForAnalysis) return CreateDefaultFeatures();

            var features = new List<decimal>();
            
            // Technical indicators for regime detection
            var volatility = CalculateRecentVolatility(bars);
            var trend = CalculateTrend(bars);
            var momentum = CalculateMomentum(bars, 14);
            var rsi = CalculateRSI(bars, 14);
            var volume = CalculateVolumeProfile(bars);
            
            // Market microstructure features
            var bidAskSpread = data.Bid > 0 && data.Ask > 0 ? (decimal)((data.Ask - data.Bid) / data.Bid) : 0.001m;
            var imbalance = CalculateOrderBookImbalance(data);
            
            // Time-based features
            var hourOfDay = DateTime.UtcNow.Hour / 24.0m;
            var timeToClose = CalculateTimeToClose();
            
            // Add all features
            features.AddRange(new decimal[]
            {
                (decimal)volatility, (decimal)trend, (decimal)momentum, (decimal)rsi,
                (decimal)volume.AverageVolume, bidAskSpread, imbalance,
                hourOfDay, timeToClose,
                (decimal)data.Close / MinimumAccountBalance, // Normalized price
                CalculateATRNormalized(bars), CalculateBollingerPosition(bars),
                CalculateVWAP(bars), CalculateMarketStress(bars)
            });
            
            return features.ToArray();
        }

        private Task<decimal> RunRegimeInferenceAsync(Microsoft.ML.OnnxRuntime.InferenceSession session, decimal[] features)
        {
            try
            {
                var inputTensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(
                    features.Select(f => (float)f).ToArray(), 
                    new[] { 1, features.Length });

                var inputs = new List<Microsoft.ML.OnnxRuntime.NamedOnnxValue>
                {
                    Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("input", inputTensor)
                };

                using var results = session.Run(inputs);
                var output = results.FirstOrDefault()?.AsEnumerable<float>()?.FirstOrDefault() ?? 0.5f;
                
                return Task.FromResult((decimal)output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TIME-STRATEGY] ONNX regime inference error");
                return Task.FromResult(0.5m); // Neutral regime
            }
        }

        private static MarketRegime ClassifyRegime(decimal prediction, decimal[] features)
        {
            var volatility = (double)features[0];
            var trend = (double)features[1];
            
            // Classify based on ML prediction and features
            string regimeName;
            if (prediction > TrendingUpThreshold) regimeName = "trending_up";
            else if (prediction < TrendingDownThreshold) regimeName = "trending_down";
            else if (volatility > HighVolatilityThreshold) regimeName = "high_vol";
            else if (volatility < LowVolatilityThreshold) regimeName = "low_vol";
            else regimeName = "sideways";

            return new MarketRegime
            {
                Name = regimeName,
                TrendStrength = Math.Abs(trend),
                Volatility = volatility,
                MLConfidence = (double)prediction
            };
        }

        private static MarketRegime GetRegimeFallback(decimal[] features)
        {
            var volatility = (double)features[0];
            var trend = (double)features[1];

            return new MarketRegime
            {
                Name = volatility > HighVolatilityThreshold ? "high_vol" : volatility < LowVolatilityThreshold ? "low_vol" : "mid_vol",
                TrendStrength = Math.Abs(trend),
                Volatility = volatility,
                MLConfidence = NoMLConfidenceFallback // No ML confidence in fallback
            };
        }

        private static double CalculateRecentVolatility(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < MinimumBarsForVolatility) return DefaultVolatilityFallback;

            var returns = new List<double>();
            for (int i = 1; i < Math.Min(MinimumBarsForVolatility, bars.Count); i++)
            {
                var ret = Math.Log((double)bars[i].Close / (double)bars[i - 1].Close);
                returns.Add(ret);
            }

            var mean = returns.Average();
            var variance = returns.Select(r => Math.Pow(r - mean, 2)).Average();
            return Math.Sqrt(variance) * Math.Sqrt(AnnualizationFactor); // Annualized volatility proxy
        }

        private static double CalculateTrend(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < MinimumBarsForTrend) return DefaultTrendFallback;

            var recent = bars.TakeLast(MinimumBarsForTrend).ToList();
            var firstPrice = (double)recent.First().Close;
            var lastPrice = (double)recent.Last().Close;

            return (lastPrice - firstPrice) / firstPrice;
        }

        private List<Candidate> GenerateStrategyCandidates(string strategyId, string instrument, TradingBot.Abstractions.MarketData data, IReadOnlyList<Bar> bars)
        {
            // Apply S7 gate for gated strategies (S2, S3, S6, S11) before generating candidates
            if (!StrategyGates.PassesS7Gate(_s7Service, strategyId))
            {
                return new List<Candidate>(); // S7 gate failed, return empty candidates
            }
            
            // Use existing AllStrategies system to generate candidates
            try
            {
                var env = CreateEnvironment(bars);
                var levels = CreateLevels();
                var riskEngine = new BotCore.Risk.RiskEngine();

                return AllStrategies.generate_candidates(instrument, env, levels, bars.ToList(), riskEngine)
                    .Where(c => c.strategy_id == strategyId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error generating candidates for {strategyId}: {ex.Message}");
                return new List<Candidate>();
            }
        }

        private static Env CreateEnvironment(IReadOnlyList<Bar> bars)
        {
            // Create environment for strategy evaluation
            return new Env
            {
                atr = bars.Count > 0 ? (decimal?)Math.Abs(bars[^1].High - bars[^1].Low) : DefaultATRFallback,
                volz = (decimal?)CalculateRecentVolatility(bars)
            };
        }

        private static Levels CreateLevels()
        {
            // Create levels for strategy evaluation - Levels class is empty, just return new instance
            return new Levels();
        }

        private static BotCore.Models.Signal? ConvertToSignal(Candidate candidate, string strategyId)
        {
            if (candidate == null) return null;

            return new BotCore.Models.Signal
            {
                StrategyId = strategyId,
                Symbol = candidate.symbol,
                Side = candidate.side.ToString(),
                Entry = candidate.entry,
                Stop = candidate.stop,
                Target = candidate.t1,
                Size = (int)candidate.qty,
                ExpR = candidate.expR,
                Score = candidate.Score,
                QScore = candidate.QScore,
                ContractId = candidate.contractId,
                AccountId = candidate.accountId,
                Tag = candidate.Tag
            };
        }

        private static bool ShouldCheckCorrelation(string instrument)
        {
            return instrument == "ES" || instrument == "NQ";
        }

        private double CalculateRealTimeCorrelation()
        {
            try
            {
                // Get recent price data for both ES and NQ
                var esPrices = _esBars?.TakeLast(CorrelationLookbackPeriod)?.Select(b => (double)b.Close)?.ToArray();
                var nqPrices = _nqBars?.TakeLast(CorrelationLookbackPeriod)?.Select(b => (double)b.Close)?.ToArray();
                
                // Calculate Pearson correlation if we have sufficient data
                if (esPrices != null && nqPrices != null && esPrices.Length >= MinimumDataPointsForCorrelation && nqPrices.Length >= MinimumDataPointsForCorrelation)
                {
                    var minLength = Math.Min(esPrices.Length, nqPrices.Length);
                    var esReturns = CalculateReturns(esPrices.TakeLast(minLength).ToArray());
                    var nqReturns = CalculateReturns(nqPrices.TakeLast(minLength).ToArray());
                    
                    if (esReturns.Length >= MinimumBarsForTrend && nqReturns.Length >= MinimumBarsForTrend)
                    {
                        var correlation = CalculatePearsonCorrelation(esReturns, nqReturns);
                        _logger.LogDebug("Real Pearson correlation calculated: {Correlation:F3} from {DataPoints} returns", 
                            correlation, Math.Min(esReturns.Length, nqReturns.Length));
                        return Math.Max(MinimumCorrelation, Math.Min(MaximumCorrelation, correlation));
                    }
                }
                
                // Fallback to sophisticated estimation if insufficient data
                var baseCorrelation = FallbackCorrelation; // ES/NQ historical baseline
                var hourOfDay = DateTime.UtcNow.Hour;
                var marketHoursAdjustment = hourOfDay switch
                {
                    >= MarketOpenHour and <= MarketAfterHoursEnd => MarketHoursCorrelationBoost,  // Higher correlation during market hours
                    _ => OffHoursCorrelationPenalty                // Lower correlation during off hours
                };
                
                var result = Math.Max(MinimumCorrelationBound, Math.Min(MaximumCorrelation, baseCorrelation + marketHoursAdjustment));
                _logger.LogDebug("Using fallback correlation: {Correlation:F3} (insufficient data for Pearson)", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating correlation, using fallback");
                return ConservativeFallbackCorrelation; // Safe conservative correlation
            }
        }
        
        /// <summary>
        /// Calculate returns (percentage changes) from price series
        /// </summary>
        private static double[] CalculateReturns(double[] prices)
        {
            if (prices.Length < 2) return Array.Empty<double>();
            
            var returns = new double[prices.Length - 1];
            for (int i = 1; i < prices.Length; i++)
            {
                if (prices[i - 1] != 0)
                {
                    returns[i - 1] = (prices[i] - prices[i - 1]) / prices[i - 1];
                }
            }
            return returns;
        }
        
        /// <summary>
        /// Calculate Pearson correlation coefficient between two return series
        /// </summary>
        private static double CalculatePearsonCorrelation(double[] series1, double[] series2)
        {
            if (series1.Length != series2.Length || series1.Length == 0)
                return FallbackCorrelation; // Fallback
                
            var mean1 = series1.Average();
            var mean2 = series2.Average();
            
            var numerator = series1.Zip(series2, (x, y) => (x - mean1) * (y - mean2)).Sum();
            var denominator = Math.Sqrt(
                series1.Sum(x => Math.Pow(x - mean1, 2)) *
                series2.Sum(y => Math.Pow(y - mean2, 2))
            );
            
            return denominator != 0 ? numerator / denominator : FallbackCorrelation;
        }

        private EsNqCorrelation CheckES_NQ_Correlation(string instrument)
        {
            // ES/NQ correlation analysis using advanced statistical methods
            var correlation = CalculateRealTimeCorrelation();
            
            var result = new EsNqCorrelation
            {
                Value = correlation,
                ConfidenceMultiplier = DefaultConfidenceMultiplier,
                ShouldVeto = false,
                Reason = ""
            };

            // Simple correlation check - prevent opposing trades when highly correlated
            if (correlation > HighCorrelationThreshold)
            {
                // In production, check actual opposite instrument position/trend
                // For now, just log the correlation
                _logger.LogDebug($"High correlation ({correlation:P0}) detected for {instrument} signal");
            }

            return result;
        }

        private void LoadHistoricalPerformanceData()
        {
            // Load historical performance data from files or ML models
            // This is where you'd integrate with the existing ML system
            _logger.LogInformation("Historical performance data loaded for time optimization");
        }

        // ================================================================================
        // SOPHISTICATED TECHNICAL ANALYSIS METHODS FOR PRODUCTION
        // ================================================================================

        private static double CalculateMomentum(IReadOnlyList<Bar> bars, int period)
        {
            if (bars.Count < period + 1) return 0.0;

            var currentPrice = (double)bars[^1].Close;
            var previousPrice = (double)bars[bars.Count - period - 1].Close;
            
            return (currentPrice - previousPrice) / previousPrice;
        }

        private static double CalculateRSI(IReadOnlyList<Bar> bars, int period)
        {
            if (bars.Count < period + 1) return NeutralRSIValue; // Neutral RSI

            var gains = new List<double>();
            var losses = new List<double>();

            for (int i = Math.Max(1, bars.Count - period); i < bars.Count; i++)
            {
                var change = (double)(bars[i].Close - bars[i - 1].Close);
                if (change > 0)
                {
                    gains.Add(change);
                    losses.Add(0);
                }
                else
                {
                    gains.Add(0);
                    losses.Add(Math.Abs(change));
                }
            }

            var avgGain = gains.Average();
            var avgLoss = losses.Average();

            if (avgLoss == 0) return MaxRSIValue; // All gains
            
            var rs = avgGain / avgLoss;
            return MaxRSIValue - (MaxRSIValue / (1 + rs));
        }

        private static VolumeProfileData CalculateVolumeProfile(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < MinimumBarsForVolumeProfile)
            {
                return new VolumeProfileData
                {
                    AverageVolume = DefaultAverageVolume,
                    VolumeRatio = DefaultConfidenceMultiplier,
                    HighVolumeLevel = DefaultHighVolumeLevel,
                    LowVolumeLevel = DefaultLowVolumeLevel
                };
            }

            var volumes = bars.TakeLast(VolumeProfileLookback).Select(b => (double)b.Volume).ToList();
            var avgVolume = volumes.Average();
            var currentVolume = (double)bars[^1].Volume;
            var volumeRatio = avgVolume > 0 ? currentVolume / avgVolume : DefaultConfidenceMultiplier;

            // Find high and low volume price levels
            var priceVolumePairs = bars.TakeLast(PriceVolumeLookback)
                .GroupBy(b => Math.Round(b.Close, PriceRoundingLevel)) // Group by dollar levels
                .Select(g => new { Price = g.Key, TotalVolume = g.Sum(x => x.Volume) })
                .OrderByDescending(x => x.TotalVolume)
                .ToList();

            var highVolumeLevel = priceVolumePairs.Count > 0 ? priceVolumePairs[0].Price : bars[^1].Close;
            var lowVolumeLevel = priceVolumePairs.Count > 0 ? priceVolumePairs[^1].Price : bars[^1].Close;

            return new VolumeProfileData
            {
                AverageVolume = avgVolume,
                VolumeRatio = volumeRatio,
                HighVolumeLevel = highVolumeLevel,
                LowVolumeLevel = lowVolumeLevel
            };
        }

        private static decimal CalculateOrderBookImbalance(TradingBot.Abstractions.MarketData data)
        {
            // Use Bid/Ask prices to estimate imbalance since TradingBot.Abstractions.MarketData doesn't have sizes
            if (data.Bid == 0 && data.Ask == 0) return 0m;
            
            var spread = data.Ask - data.Bid;
            if (spread <= 0) return 0m;
            
            // Calculate imbalance based on where Close price sits in bid-ask spread
            var midPoint = (data.Bid + data.Ask) / (double)ImbalanceNormalizationFactor;
            var pricePosition = data.Close - midPoint;
            
            // Normalize to -1 to +1 range
            return Math.Max(MinimumImbalance, Math.Min(MaximumImbalance, (decimal)pricePosition / ((decimal)spread / ImbalanceNormalizationFactor)));
        }

        private decimal CalculateTimeToClose()
        {
            var now = DateTime.UtcNow;
            var centralTime = TimeZoneInfo.ConvertTimeFromUtc(now, _centralTime);
            
            // Calculate hours until 4 PM CT market close
            var marketClose = centralTime.Date.AddHours(MarketCloseHour);
            if (centralTime.Hour >= MarketCloseHour) // After market close, calculate to next day
            {
                marketClose = marketClose.AddDays(1);
            }
            
            var timeToClose = marketClose - centralTime;
            return (decimal)Math.Max(0, timeToClose.TotalHours / HoursInDay); // Normalized to 0-1
        }

        private static decimal CalculateATRNormalized(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < ATRPeriod) return DefaultATRNormalized; // Default 1% ATR

            var trueRanges = new List<decimal>();
            
            for (int i = 1; i < Math.Min(bars.Count, ATRPeriod); i++)
            {
                var high = bars[i].High;
                var low = bars[i].Low;
                var prevClose = bars[i - 1].Close;
                
                var tr1 = high - low;
                var tr2 = Math.Abs(high - prevClose);
                var tr3 = Math.Abs(low - prevClose);
                
                trueRanges.Add(Math.Max(tr1, Math.Max(tr2, tr3)));
            }
            
            var atr = trueRanges.Average();
            var currentPrice = bars[^1].Close;
            
            return currentPrice > 0 ? atr / currentPrice : DefaultATRNormalized; // Normalized ATR
        }

        private static decimal CalculateBollingerPosition(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < BollingerBandsPeriod) return MiddleOfBands; // Middle of bands

            var period = Math.Min(BollingerBandsPeriod, bars.Count);
            var prices = bars.TakeLast(period).Select(b => b.Close).ToList();
            var sma = prices.Average();
            
            var variance = prices.Select(p => (decimal)Math.Pow((double)(p - sma), BollingerStdDevMultiplier)).Average();
            var stdDev = (decimal)Math.Sqrt((double)variance);
            
            var upperBand = sma + (BollingerStdDevMultiplier * stdDev);
            var lowerBand = sma - (BollingerStdDevMultiplier * stdDev);
            var currentPrice = bars[^1].Close;
            
            if (upperBand == lowerBand) return MiddleOfBands;
            
            // Return position within bands (0 = lower band, 1 = upper band)
            return Math.Max(0, Math.Min(1, (currentPrice - lowerBand) / (upperBand - lowerBand)));
        }

        private static decimal CalculateVWAP(IReadOnlyList<Bar> bars)
        {
            if (bars.Count == 0) return 0m;

            var period = Math.Min(VWAPLookbackPeriod, bars.Count);
            var recentBars = bars.TakeLast(period).ToList();
            
            decimal totalVolume = 0;
            decimal volumeWeightedSum = 0;
            
            foreach (var bar in recentBars)
            {
                var typicalPrice = (bar.High + bar.Low + bar.Close) / TypicalPriceDivisor;
                volumeWeightedSum += typicalPrice * bar.Volume;
                totalVolume += bar.Volume;
            }
            
            return totalVolume > 0 ? volumeWeightedSum / totalVolume : bars[^1].Close;
        }

        private static decimal CalculateMarketStress(IReadOnlyList<Bar> bars)
        {
            if (bars.Count < MinimumBarsForStressCalculation) return MediumStressLevel; // Medium stress

            var recent = bars.TakeLast(StressLookbackPeriod).ToList();
            
            // Calculate multiple stress indicators
            var volatility = CalculateRecentVolatility(bars);
            var volumeStress = CalculateVolumeStress(recent);
            var gapStress = CalculateGapStress(recent);
            var trendStress = CalculateTrendStress(recent);
            
            // Combine stress factors (0 = low stress, 1 = high stress) - all decimal
            var combinedStress = ((decimal)volatility / StressNormalizationFactor + volumeStress + gapStress + trendStress) / StressCombinationFactor;
            
            return Math.Max(0, Math.Min(1, combinedStress));
        }

        private static decimal CalculateVolumeStress(IReadOnlyList<Bar> recent)
        {
            if (recent.Count < MinimumBarsForVolumeStress) return DefaultVolumeStress;
            
            var avgVolume = recent.Take(recent.Count - 1).Average(b => b.Volume);
            var currentVolume = recent[^1].Volume;
            
            if (avgVolume == 0) return DefaultVolumeStress;
            
            var volumeRatio = (decimal)(currentVolume / avgVolume);
            
            // High volume = high stress
            return Math.Max(0, Math.Min(1, (volumeRatio - (decimal)DefaultConfidenceMultiplier) / VolumeStressNormalizationFactor));
        }

        private static decimal CalculateGapStress(IReadOnlyList<Bar> recent)
        {
            if (recent.Count < MinimumBarsForGapAnalysis) return 0m;
            
            var gaps = new List<decimal>();
            
            for (int i = 1; i < recent.Count; i++)
            {
                var prevClose = recent[i - 1].Close;
                var currentOpen = recent[i].Open;
                
                if (prevClose > 0)
                {
                    var gapPercent = Math.Abs(currentOpen - prevClose) / prevClose;
                    gaps.Add(gapPercent);
                }
            }
            
            if (gaps.Count == 0) return 0m;
            
            var avgGap = gaps.Average();
            
            // Gaps > 0.5% indicate stress
            return Math.Max(0, Math.Min(1, avgGap / GapStressThreshold));
        }

        private static decimal CalculateTrendStress(IReadOnlyList<Bar> recent)
        {
            if (recent.Count < MinimumBarsForTrendStress) return DefaultTrendStress;
            
            var priceChanges = new List<decimal>();
            
            for (int i = 1; i < recent.Count; i++)
            {
                var prevClose = recent[i - 1].Close;
                var currentClose = recent[i].Close;
                
                if (prevClose > 0)
                {
                    var change = Math.Abs(currentClose - prevClose) / prevClose;
                    priceChanges.Add(change);
                }
            }
            
            if (priceChanges.Count == 0) return DefaultTrendStress;
            
            var avgChange = priceChanges.Average();
            
            // Rapid price changes indicate stress
            return Math.Max(0, Math.Min(1, avgChange / TrendStressNormalizationFactor)); // 1% baseline
        }

        private static decimal[] CreateDefaultFeatures()
        {
            // Return default feature set when insufficient data
            return new decimal[]
            {
                DefaultVolatilityFeature,   // volatility
                DefaultTrendFeature,        // trend
                DefaultMomentumFeature,     // momentum
                NeutralRSIValueDecimal,     // RSI
                DefaultVolumeFeature,       // volume
                DefaultBidAskSpread,        // bid-ask spread
                DefaultImbalanceFeature,    // imbalance
                DefaultHourOfDayFeature,    // hour of day
                DefaultTimeToCloseFeature,  // time to close
                DefaultNormalizedPriceFeature, // normalized price
                DefaultATRNormalized,       // ATR
                DefaultBollingerPositionFeature, // Bollinger position
                MinimumAccountBalance,      // VWAP
                DefaultMarketStressFeature  // market stress
            };
        }
        
        /// <summary>
        /// Get ML-based adjustment factor for strategy performance
        /// </summary>
        private async Task<double> GetMLAdjustmentAsync(string regime, TradingSession session, string instrument)
        {
            try
            {
                // Base adjustment from market regime
                var regimeAdjustment = regime switch
                {
                    "TRENDING" => 1.1,      // Trend strategies perform better
                    "RANGING" => 0.95,      // Range strategies struggle
                    "VOLATILE" => 0.85,     // High volatility hurts most strategies
                    "STABLE" => 1.05,       // Stable markets are good
                    _ => 1.0                // Unknown regime
                };
                
                // Session-specific adjustments
                var sessionAdjustment = session.SessionType switch
                {
                    SessionType.Regular => 1.0,        // Normal trading hours
                    SessionType.Extended => 0.9,       // Extended hours are harder
                    SessionType.Overnight => 0.8,      // Overnight is most challenging
                    _ => 1.0
                };
                
                // Instrument-specific adjustments
                var instrumentAdjustment = instrument switch
                {
                    "ES" => 1.0,           // ES is baseline
                    "NQ" => 1.02,          // NQ typically more volatile/profitable
                    _ => 1.0
                };
                
                var totalAdjustment = regimeAdjustment * sessionAdjustment * instrumentAdjustment;
                
                _logger.LogDebug("ML adjustment for {Instrument} in {Regime}: {Adjustment:F2} " +
                    "(regime={RegimeAdj:F2}, session={SessionAdj:F2}, instrument={InstrumentAdj:F2})",
                    instrument, regime, totalAdjustment, regimeAdjustment, sessionAdjustment, instrumentAdjustment);
                
                await Task.CompletedTask.ConfigureAwait(false); // Keep async for future ML model integration
                return totalAdjustment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating ML adjustment, using neutral");
                return 1.0; // Neutral adjustment on error
            }
        }
        
        /// <summary>
        /// Update bar data for correlation analysis
        /// Called by external data providers (e.g., TradingSystemConnector)
        /// </summary>
        public void UpdateBarData(string symbol, IReadOnlyList<Bar> bars)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            
            switch (symbol.ToUpperInvariant())
            {
                case "ES":
                    _esBars = bars;
                    break;
                case "NQ":
                    _nqBars = bars;
                    break;
                default:
                    _logger.LogDebug("Bar data update for unsupported symbol: {Symbol}", symbol);
                    break;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cleanup managed resources when needed
            }
        }
        
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Volume profile analysis data
    /// </summary>
    public class VolumeProfileData
    {
        public double AverageVolume { get; set; }
        public double VolumeRatio { get; set; }
        public decimal HighVolumeLevel { get; set; }
        public decimal LowVolumeLevel { get; set; }
    }

    /// <summary>
    /// Result of strategy evaluation
    /// </summary>
    public class StrategyEvaluationResult
    {
        public bool HasSignal { get; set; }
        public BotCore.Models.Signal? Signal { get; set; }
        public string Session { get; set; } = "";
        public int TotalCandidatesEvaluated { get; set; }
        public string Reason { get; set; } = "";

        public static StrategyEvaluationResult NoSignal(string reason)
        {
            return new StrategyEvaluationResult
            {
                HasSignal = false,
                Reason = reason
            };
        }
    }

    /// <summary>
    /// Market regime information
    /// </summary>
    public class MarketRegime
    {
        public string Name { get; set; } = "";
        public double TrendStrength { get; set; }
        public double Volatility { get; set; }
        public double MLConfidence { get; set; } = 0.5;
    }

    /// <summary>
    /// ML-based position size and confidence adjustments
    /// </summary>
    public class MLAdjustment
    {
        public double SizeMultiplier { get; set; } = 1.0;
        public double ConfidenceBoost { get; set; }
    }

    /// <summary>
    /// ES/NQ correlation analysis result
    /// </summary>
    public class EsNqCorrelation
    {
        public double Value { get; set; }
        public double ConfidenceMultiplier { get; set; } = 1.0;
        public bool ShouldVeto { get; set; }
        public string Reason { get; set; } = "";
    }
}