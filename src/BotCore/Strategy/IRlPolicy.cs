namespace BotCore.Strategy;

/// <summary>
/// Interface for RL policy that predicts trading actions from feature vectors.
/// Used by S15_RL strategy to generate trading candidates using reinforcement learning.
/// </summary>
public interface IRlPolicy
{
    /// <summary>
    /// Predict trading action from feature vector.
    /// </summary>
    /// <param name="features">Standardized feature array from FeatureBuilder (length 12)</param>
    /// <returns>Trading action: -1 (short), 0 (flat/hold), 1 (long)</returns>
    int PredictAction(decimal[] features);

    /// <summary>
    /// Get confidence score for the prediction.
    /// Higher confidence indicates stronger conviction in the predicted action.
    /// </summary>
    /// <param name="features">Standardized feature array from FeatureBuilder (length 12)</param>
    /// <returns>Confidence score between 0.0 (no confidence) and 1.0 (maximum confidence)</returns>
    decimal GetConfidence(decimal[] features);
}
