using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using BotCore.Features;

namespace BotCore.Strategy;

/// <summary>
/// ONNX-based RL policy implementation for S15_RL strategy.
/// Loads a trained ONNX model and performs inference to predict trading actions.
/// </summary>
public sealed class OnnxRlPolicy : IRlPolicy, IDisposable
{
    private readonly InferenceSession _session;
    private readonly FeatureSpec _featureSpec;
    private bool _disposed;

    /// <summary>
    /// Initialize ONNX RL policy with model path and feature specification.
    /// </summary>
    /// <param name="modelPath">Path to ONNX model file</param>
    /// <param name="featureSpec">Feature specification for logit-to-action mapping</param>
    /// <exception cref="ArgumentNullException">If modelPath or featureSpec is null</exception>
    /// <exception cref="OnnxRuntimeException">If model fails to load</exception>
    public OnnxRlPolicy(string modelPath, FeatureSpec featureSpec)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(featureSpec);

        _featureSpec = featureSpec;

        try
        {
            // Load ONNX model
            _session = new InferenceSession(modelPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load ONNX model from {modelPath}", ex);
        }
    }

    /// <summary>
    /// Predict trading action from feature vector using ONNX model inference.
    /// </summary>
    /// <param name="features">Standardized feature array (length 12)</param>
    /// <returns>Trading action: -1 (short), 0 (flat), 1 (long)</returns>
    public int PredictAction(decimal[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OnnxRlPolicy));
        }

        try
        {
            // Convert decimal array to float array for ONNX
            var floatFeatures = features.Select(f => (float)f).ToArray();

            // Create input tensor with shape [1, feature_count]
            var inputTensor = new DenseTensor<float>(floatFeatures, new[] { 1, features.Length });

            // Create named input
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("obs", inputTensor)
            };

            // Run inference
            using var results = _session.Run(inputs);

            // Get logits output tensor (shape: [1, 3])
            var logitsOutput = results.FirstOrDefault(r => r.Name == "logits");
            if (logitsOutput == null)
            {
                throw new InvalidOperationException("Model output 'logits' not found");
            }

            var logitsTensor = logitsOutput.AsTensor<float>();
            var logits = logitsTensor.ToArray();

            if (logits.Length < 3)
            {
                throw new InvalidOperationException($"Expected 3 logits, got {logits.Length}");
            }

            // Find argmax
            var maxIdx = 0;
            var maxValue = logits[0];
            for (int i = 1; i < 3; i++)
            {
                if (logits[i] > maxValue)
                {
                    maxValue = logits[i];
                    maxIdx = i;
                }
            }

            // Map argmax to action using feature spec
            if (_featureSpec.Inference.LogitToAction.TryGetValue(maxIdx, out var action))
            {
                return action;
            }

            // Fallback to flat if mapping not found
            return 0;
        }
        catch (Exception)
        {
            // On any error, return flat/hold action (0)
            return 0;
        }
    }

    /// <summary>
    /// Get confidence score using softmax probability of the predicted action.
    /// </summary>
    /// <param name="features">Standardized feature array (length 12)</param>
    /// <returns>Confidence score between 0.0 and 1.0</returns>
    public decimal GetConfidence(decimal[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OnnxRlPolicy));
        }

        try
        {
            // Convert decimal array to float array for ONNX
            var floatFeatures = features.Select(f => (float)f).ToArray();

            // Create input tensor with shape [1, feature_count]
            var inputTensor = new DenseTensor<float>(floatFeatures, new[] { 1, features.Length });

            // Create named input
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("obs", inputTensor)
            };

            // Run inference
            using var results = _session.Run(inputs);

            // Get logits output tensor (shape: [1, 3])
            var logitsOutput = results.FirstOrDefault(r => r.Name == "logits");
            if (logitsOutput == null)
            {
                return 0m; // No confidence if output not found
            }

            var logitsTensor = logitsOutput.AsTensor<float>();
            var logits = logitsTensor.ToArray();

            if (logits.Length < 3)
            {
                return 0m;
            }

            // Apply softmax to get probabilities
            var maxLogit = logits.Max();
            var expLogits = logits.Select(l => Math.Exp(l - maxLogit)).ToArray();
            var sumExp = expLogits.Sum();
            var probabilities = expLogits.Select(e => e / sumExp).ToArray();

            // Return max probability as confidence
            var maxProb = probabilities.Max();
            return (decimal)maxProb;
        }
        catch (Exception)
        {
            // On any error, return zero confidence
            return 0m;
        }
    }

    /// <summary>
    /// Dispose of ONNX inference session resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session?.Dispose();
        _disposed = true;
    }
}
