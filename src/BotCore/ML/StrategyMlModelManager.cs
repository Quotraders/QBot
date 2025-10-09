using Microsoft.Extensions.Logging;
using BotCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace BotCore.ML
{
    /// <summary>
    /// Simple feature container for ML model input
    /// </summary>
    public class SimpleFeatureSnapshot
    {
        public string Symbol { get; set; } = "";
        public string Strategy { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public float Price { get; set; }
        public float Atr { get; set; }
        public float Volume { get; set; }
        public float Rsi { get; set; } = 50f;
        public float Ema20 { get; set; }
        public float Ema50 { get; set; }
        public float SignalStrength { get; set; }
        public float Volatility { get; set; }

        public Dictionary<string, float> ToDict()
        {
            return new Dictionary<string, float>
            {
                ["price"] = Price,
                ["atr"] = Atr,
                ["volume"] = Volume,
                ["rsi"] = Rsi,
                ["ema20"] = Ema20,
                ["ema50"] = Ema50,
                ["signal_strength"] = SignalStrength,
                ["volatility"] = Volatility
            };
        }
    }

    /// <summary>
    /// ML model manager that integrates ONNX models with strategy execution.
    /// Provides position sizing, signal filtering, and execution quality predictions.
    /// Enhanced with memory management capabilities.
    /// </summary>
    public sealed class StrategyMlModelManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IMLMemoryManager? _memoryManager;
        private readonly OnnxModelLoader? _onnxLoader;
        private bool _disposed;

        // Model file paths
        private readonly string _rlSizerPath;
        private readonly string _metaClassifierPath;
        private readonly string _execQualityPath;

        // S109: ML model thresholds and defaults
        private const decimal DefaultPositionSizeMultiplier = 1.0m;
        private const decimal MinimumQualityScore = 0.3m;
        private const decimal MinimumSignalScore = 0.5m;
        private const int MinimumVolume = 100;
        private const decimal DefaultExecutionQuality = 0.8m;
        private const decimal SpreadQualityThreshold = 0.001m; // 0.1% of price
        private const decimal SpreadQualityPenalty = 0.2m;
        private const int VolumeQualityThreshold = 1000;
        private const decimal VolumeQualityPenalty = 0.3m;
        private const decimal MinimumQualityClamp = 0.1m;
        private const decimal MaximumQualityClamp = 1.0m;

        // Structured logging delegates
        private static readonly Action<ILogger, bool, bool, Exception?> LogMlManagerInitialized =
            LoggerMessage.Define<bool, bool>(
                LogLevel.Information,
                new EventId(1, nameof(LogMlManagerInitialized)),
                "[ML-Manager] Initialized - RL enabled: {Enabled}, Memory management: {MemoryEnabled}");

        private static readonly Action<ILogger, string, string, bool, float, Exception?> LogOnnxSignalFilter =
            LoggerMessage.Define<string, string, bool, float>(
                LogLevel.Information,
                new EventId(2, nameof(LogOnnxSignalFilter)),
                "[ML-Manager] 🧠 REAL ONNX signal filter: {Strategy}-{Symbol} = {Accept} (prob: {Probability:F3})");

        private static readonly Action<ILogger, Exception?> LogMetaClassifierFileNotFound =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(3, nameof(LogMetaClassifierFileNotFound)),
                "[ML-Manager] Meta-classifier file not found, using basic rules");

        private static readonly Action<ILogger, Exception?> LogMetaClassifierInvalidOperation =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(4, nameof(LogMetaClassifierInvalidOperation)),
                "[ML-Manager] Invalid meta-classifier operation, using basic rules");

        private static readonly Action<ILogger, Exception?> LogMetaClassifierInvalidArgument =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(5, nameof(LogMetaClassifierInvalidArgument)),
                "[ML-Manager] Invalid meta-classifier argument, using basic rules");

        private static readonly Action<ILogger, string, string, Exception?> LogSignalFilterInvalidOperation =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(6, nameof(LogSignalFilterInvalidOperation)),
                "[ML-Manager] Invalid operation in signal filtering for {Strategy}-{Symbol}");

        private static readonly Action<ILogger, string, string, Exception?> LogSignalFilterInvalidArgument =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(7, nameof(LogSignalFilterInvalidArgument)),
                "[ML-Manager] Invalid argument in signal filtering for {Strategy}-{Symbol}");

        private static readonly Action<ILogger, string, Exception?> LogOnnxModelNotFound =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(8, nameof(LogOnnxModelNotFound)),
                "[ML-Manager] ONNX model not found at {Path}, using fallback");

        private static readonly Action<ILogger, Exception?> LogRlModelLoaderError =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(9, nameof(LogRlModelLoaderError)),
                "[ML-Manager] RL model loader error, using fallback");

        private static readonly Action<ILogger, Exception?> LogRlModelInferenceError =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(10, nameof(LogRlModelInferenceError)),
                "[ML-Manager] RL model inference error, using fallback");

        private static readonly Action<ILogger, Exception?> LogRlModelFileNotFound =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(11, nameof(LogRlModelFileNotFound)),
                "[ML-Manager] RL model file not found, using fallback");

        private static readonly Action<ILogger, string, Exception?> LogExecQualityModelNotFound =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(12, nameof(LogExecQualityModelNotFound)),
                "[ML-Manager] Execution quality model not found at {Path}, using fallback scoring");

        private static readonly Action<ILogger, Exception?> LogExecQualityModelError =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(13, nameof(LogExecQualityModelError)),
                "[ML-Manager] Error in execution quality model prediction, using fallback");

        private static readonly Action<ILogger, decimal, string, Exception?> LogExecutionQualityScore =
            LoggerMessage.Define<decimal, string>(
                LogLevel.Information,
                new EventId(14, nameof(LogExecutionQualityScore)),
                "[ML-Manager] Execution quality score: {Score:F3} for {Symbol}");

        private static readonly Action<ILogger, string, string, decimal, decimal, decimal, Exception?> LogOnnxPositionSizing =
            LoggerMessage.Define<string, string, decimal, decimal, decimal>(
                LogLevel.Information,
                new EventId(15, nameof(LogOnnxPositionSizing)),
                "[ML-Manager] 🧠 REAL ONNX position sizing: {Strategy}-{Symbol} = {Multiplier:F2} (qScore: {QScore:F2}, score: {Score:F2})");

        private static readonly Action<ILogger, Exception?> LogOnnxModelFileNotFound =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(16, nameof(LogOnnxModelFileNotFound)),
                "[ML-Manager] ONNX model file not found, using fallback");

        private static readonly Action<ILogger, Exception?> LogOnnxModelInvalidOperation =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(17, nameof(LogOnnxModelInvalidOperation)),
                "[ML-Manager] Invalid ONNX model operation, using fallback");

        private static readonly Action<ILogger, Exception?> LogOnnxModelInvalidArgument =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(18, nameof(LogOnnxModelInvalidArgument)),
                "[ML-Manager] Invalid ONNX model argument, using fallback");

        private static readonly Action<ILogger, Exception?> LogOnnxModelNotAvailable =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(19, nameof(LogOnnxModelNotAvailable)),
                "[ML-Manager] ONNX model not available, using fallback multiplier");

        private static readonly Action<ILogger, decimal, decimal, decimal, decimal, Exception?> LogOnnxExecutionQuality =
            LoggerMessage.Define<decimal, decimal, decimal, decimal>(
                LogLevel.Information,
                new EventId(20, nameof(LogOnnxExecutionQuality)),
                "[ML-Manager] 🧠 REAL ONNX execution quality: {Price} = {Quality:F3} (spread: {Spread}, volume: {Volume})");

        private static readonly Action<ILogger, Exception?> LogExecQualityFileNotFound =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(21, nameof(LogExecQualityFileNotFound)),
                "[ML-Manager] Execution quality model file not found, using fallback");

        private static readonly Action<ILogger, Exception?> LogExecQualityInvalidOperation =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(22, nameof(LogExecQualityInvalidOperation)),
                "[ML-Manager] Invalid execution quality model operation, using fallback");

        private static readonly Action<ILogger, Exception?> LogExecQualityInvalidArgument =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(23, nameof(LogExecQualityInvalidArgument)),
                "[ML-Manager] Invalid execution quality model argument, using fallback");

        private static readonly Action<ILogger, string, Exception?> LogExecutionQualityError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(24, nameof(LogExecutionQualityError)),
                "[ML-Manager] Error calculating execution quality for {Symbol}");

        private static readonly Action<ILogger, Exception?> LogMlManagerDisposed =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(25, nameof(LogMlManagerDisposed)),
                "[ML-Manager] Disposed");

        private static readonly Action<ILogger, string, string, Exception?> LogPositionSizeInvalidOperation =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(26, nameof(LogPositionSizeInvalidOperation)),
                "[ML-Manager] Invalid operation getting position size multiplier for {Strategy}-{Symbol}");

        private static readonly Action<ILogger, string, string, Exception?> LogPositionSizeInvalidArgument =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(27, nameof(LogPositionSizeInvalidArgument)),
                "[ML-Manager] Invalid argument getting position size multiplier for {Strategy}-{Symbol}");

        public static bool IsEnabled => Environment.GetEnvironmentVariable("RL_ENABLED") == "1";

        public StrategyMlModelManager(ILogger logger, IMLMemoryManager? memoryManager = null, OnnxModelLoader? onnxLoader = null)
        {
            _logger = logger;
            _memoryManager = memoryManager;
            _onnxLoader = onnxLoader;
            var modelsPath = Path.Combine(AppContext.BaseDirectory, "models");

            // Use your actual trained models instead of simulated "latest_" paths
            _rlSizerPath = Path.Combine(modelsPath, "rl", "cvar_ppo_agent.onnx");
            _metaClassifierPath = Path.Combine(modelsPath, "rl_model.onnx"); 
            _execQualityPath = Path.Combine(modelsPath, "rl", "test_cvar_ppo.onnx");

            LogMlManagerInitialized(_logger, IsEnabled, _memoryManager != null, null);
        }

        /// <summary>
        /// Get memory usage statistics from memory manager
        /// </summary>
        public MemorySnapshot? GetMemorySnapshot()
        {
            return _memoryManager?.GetMemorySnapshot();
        }

        /// <summary>
        /// Get ML-optimized position size multiplier for a strategy signal
        /// </summary>
        public async Task<decimal> GetPositionSizeMultiplierAsync(
            string strategyId,
            string symbol,
            decimal price,
            decimal atr,
            decimal score,
            decimal qScore,
            IList<Bar> bars)
        {
            try
            {
                if (!IsEnabled || !File.Exists(_rlSizerPath))
                {
                    return DefaultPositionSizeMultiplier; // Default multiplier
                }

                // 🚀 USE REAL ONNX MODEL FOR POSITION SIZING
                if (_onnxLoader != null)
                {
                    try
                    {
                        // Load model asynchronously with proper await
                        var session = await _onnxLoader.LoadModelAsync(_rlSizerPath, validateInference: false).ConfigureAwait(false);
                        if (session != null)
                        {
                            // Create simple feature array for the model
                            var features = new float[] { (float)price, (float)atr, (float)score, (float)qScore };
                            var inputTensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(features, new int[] { 1, features.Length });
                            
                            var inputs = new List<Microsoft.ML.OnnxRuntime.NamedOnnxValue>
                            {
                                Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("features", inputTensor)
                            };

                            // Run inference with real ML model
                            using var results = session.Run(inputs);
                            var resultsList = results.ToList();
                            var output = resultsList.Count > 0 ? resultsList[0].AsEnumerable<float>().FirstOrDefault() : 1.0f;
                            
                            // Convert to decimal and clamp for safety
                            decimal multiplier = Math.Clamp((decimal)output, 0.25m, 2.0m);

                            LogOnnxPositionSizing(_logger, strategyId, symbol, multiplier, qScore, score, null);

                            return multiplier;
                        }
                    }
                    catch (System.IO.FileNotFoundException modelEx)
                    {
                        LogOnnxModelFileNotFound(_logger, modelEx);
                    }
                    catch (InvalidOperationException modelEx)
                    {
                        LogOnnxModelInvalidOperation(_logger, modelEx);
                    }
                    catch (ArgumentException modelEx)
                    {
                        LogOnnxModelInvalidArgument(_logger, modelEx);
                    }
                }
                
                LogOnnxModelNotAvailable(_logger, null);
                return DefaultPositionSizeMultiplier;
            }
            catch (InvalidOperationException ex)
            {
                LogPositionSizeInvalidOperation(_logger, strategyId, symbol, ex);
                return DefaultPositionSizeMultiplier; // Fallback to default
            }
            catch (ArgumentException ex)
            {
                LogPositionSizeInvalidArgument(_logger, strategyId, symbol, ex);
                return DefaultPositionSizeMultiplier; // Fallback to default
            }
        }

        /// <summary>
        /// Check if a signal should be filtered out by ML meta-classifier
        /// </summary>
        public async Task<bool> ShouldAcceptSignalAsync(
            string strategyId,
            string symbol,
            decimal price,
            decimal score,
            decimal qScore,
            IList<Bar> bars)
        {
            ArgumentNullException.ThrowIfNull(bars);
            
            try
            {
                if (!IsEnabled)
                {
                    return true; // Accept all signals when ML disabled
                }

                // 🚀 USE REAL ONNX META-CLASSIFIER MODEL FOR SIGNAL FILTERING
                if (_onnxLoader != null && File.Exists(_metaClassifierPath))
                {
                    try
                    {
                        // Load meta-classifier model asynchronously
                        var session = await _onnxLoader.LoadModelAsync(_metaClassifierPath, validateInference: false).ConfigureAwait(false);
                        if (session != null)
                        {
                            // Create feature array for classification
                            var features = new float[] { (float)price, (float)score, (float)qScore, bars.Count > 0 ? (float)bars[bars.Count - 1].Volume : 0f };
                            var inputTensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(features, new int[] { 1, features.Length });
                            
                            var inputs = new List<Microsoft.ML.OnnxRuntime.NamedOnnxValue>
                            {
                                Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("features", inputTensor)
                            };

                            // Run real ML classification
                            using var results = session.Run(inputs);
                            var resultsList = results.ToList();
                            var probability = resultsList.Count > 0 ? resultsList[0].AsEnumerable<float>().FirstOrDefault() : 0.5f;
                            
                            bool shouldAccept = probability > 0.5f;

                            LogOnnxSignalFilter(_logger, strategyId, symbol, shouldAccept, probability, null);

                            return shouldAccept;
                        }
                    }
                    catch (System.IO.FileNotFoundException modelEx)
                    {
                        LogMetaClassifierFileNotFound(_logger, modelEx);
                    }
                    catch (InvalidOperationException modelEx)
                    {
                        LogMetaClassifierInvalidOperation(_logger, modelEx);
                    }
                    catch (ArgumentException modelEx)
                    {
                        LogMetaClassifierInvalidArgument(_logger, modelEx);
                    }
                }

                // Fallback to basic quality gates
                if (qScore < MinimumQualityScore) return false; // Very low quality signals
                if (score < MinimumSignalScore) return false; // Very low score signals

                // Volume validation
                if (bars.Any())
                {
                    var latest = bars[bars.Count - 1];
                    if (latest.Volume < MinimumVolume) return false; // Very low volume
                }

                return true;
            }
            catch (InvalidOperationException ex)
            {
                LogSignalFilterInvalidOperation(_logger, strategyId, symbol, ex);
                return true; // Default to accepting signal
            }
            catch (ArgumentException ex)
            {
                LogSignalFilterInvalidArgument(_logger, strategyId, symbol, ex);
                return true; // Default to accepting signal
            }
        }

        /// <summary>
        /// Get execution quality score for order routing decisions
        /// </summary>
        public async Task<decimal> GetExecutionQualityScoreAsync(
            string symbol,
            Side side,
            decimal price,
            decimal spread,
            decimal volume)
        {
            try
            {
                if (!IsEnabled)
                {
                    return DefaultExecutionQuality; // Default good execution quality
                }

                // 🚀 USE REAL ONNX EXECUTION QUALITY PREDICTOR
                if (_onnxLoader != null && File.Exists(_execQualityPath))
                {
                    try
                    {
                        // Load execution quality model asynchronously
                        var session = await _onnxLoader.LoadModelAsync(_execQualityPath, validateInference: false).ConfigureAwait(false);
                        if (session != null)
                        {
                            // Create feature array for quality prediction
                            var features = new float[] { (float)price, (float)spread, (float)volume, (float)(spread/price) };
                            var inputTensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(features, new int[] { 1, features.Length });
                            
                            var inputs = new List<Microsoft.ML.OnnxRuntime.NamedOnnxValue>
                            {
                                Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("features", inputTensor)
                            };

                            // Run real ML quality prediction
                            using var results = session.Run(inputs);
                            var resultsList = results.ToList();
                            var mlQualityScore = resultsList.Count > 0 ? resultsList[0].AsEnumerable<float>().FirstOrDefault() : 0.8f;
                            
                            decimal finalScore = Math.Clamp((decimal)mlQualityScore, 0.1m, 1.0m);

                            LogOnnxExecutionQuality(_logger, price, finalScore, spread, volume, null);

                            return finalScore;
                        }
                    }
                    catch (System.IO.FileNotFoundException modelEx)
                    {
                        LogExecQualityFileNotFound(_logger, modelEx);
                    }
                    catch (InvalidOperationException modelEx)
                    {
                        LogExecQualityInvalidOperation(_logger, modelEx);
                    }
                    catch (ArgumentException modelEx)
                    {
                        LogExecQualityInvalidArgument(_logger, modelEx);
                    }
                }

                // Fallback to rule-based scoring
                decimal qualityScore = MaximumQualityClamp;

                // Penalize wide spreads
                if (spread > price * SpreadQualityThreshold) // > 0.1%
                {
                    qualityScore -= SpreadQualityPenalty;
                }

                // Penalize low volume
                if (volume < VolumeQualityThreshold)
                {
                    qualityScore -= VolumeQualityPenalty;
                }

                return Math.Clamp(qualityScore, MinimumQualityClamp, MaximumQualityClamp);
            }
            catch (Exception ex)
            {
                LogExecutionQualityError(_logger, symbol, ex);
                return DefaultExecutionQuality; // Default score
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _memoryManager?.Dispose();
            LogMlManagerDisposed(_logger, null);
        }
    }

    /// <summary>
    /// Extension methods for strategy integration
    /// </summary>
    public static class StrategyMlExtensions
    {
        /// <summary>
        /// Get the ML strategy type for a given strategy ID
        /// </summary>
        public static MultiStrategyRlCollector.StrategyType GetStrategyType(string strategyId)
        {
            return Strategy.StrategyMlIntegration.GetStrategyType(strategyId);
        }
    }

    /// <summary>
    /// Extension methods for statistical calculations
    /// </summary>
    public static class StatisticsExtensions
    {
        public static double StandardDeviation(this IEnumerable<double> values)
        {
            var valueList = values.ToList();
            if (valueList.Count < 2) return 0.0;

            var mean = valueList.Average();
            var variance = valueList.Select(v => Math.Pow(v - mean, 2)).Average();
            return Math.Sqrt(variance);
        }
    }
}