namespace TradingBot.Abstractions;

/// <summary>
/// Runtime mode for reinforcement learning operations.
/// Controls whether training is allowed in the current environment.
/// </summary>
public enum RlRuntimeMode
{
    /// <summary>
    /// Inference only mode (production default).
    /// No training allowed - models can only be used for prediction.
    /// This is the safe default for production environments.
    /// </summary>
    InferenceOnly = 0,

    /// <summary>
    /// Collect only mode.
    /// Collect experiences for later training but don't train in real-time.
    /// Used for data gathering in production-like environments.
    /// </summary>
    CollectOnly = 1,

    /// <summary>
    /// Full training mode (development and CI only).
    /// Allows complete training operations including model updates.
    /// Should only be used in development and CI environments.
    /// </summary>
    Train = 2
}
