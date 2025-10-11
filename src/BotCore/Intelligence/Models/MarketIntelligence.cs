namespace BotCore.Intelligence.Models;

/// <summary>
/// Data structure to hold LLM-synthesized market intelligence
/// Used by trading brain for decision augmentation
/// </summary>
public class MarketIntelligence
{
    /// <summary>
    /// Current market regime description (e.g., "Trending bullish with increasing volatility")
    /// </summary>
    public string RegimeAnalysis { get; set; } = string.Empty;

    /// <summary>
    /// Directional recommendation from LLM (Bullish, Bearish, Neutral)
    /// </summary>
    public MarketBias RecommendedBias { get; set; } = MarketBias.Neutral;

    /// <summary>
    /// LLM conviction strength (0-100)
    /// </summary>
    public decimal ConfidenceLevel { get; set; }

    /// <summary>
    /// Risk factors identified by LLM
    /// Examples: "CPI report tomorrow", "VIX contango suggests complacency"
    /// </summary>
    public List<string> RiskFactors { get; set; } = new();

    /// <summary>
    /// Upcoming ForexFactory events that could cause volatility
    /// </summary>
    public List<string> EventRisks { get; set; } = new();

    /// <summary>
    /// Key market metrics: SPX, VIX, DXY, TNX, Fed assets
    /// </summary>
    public Dictionary<string, object> KeyMetrics { get; set; } = new();

    /// <summary>
    /// Full text response from Ollama for logging and debugging
    /// </summary>
    public string RawLlmResponse { get; set; } = string.Empty;

    /// <summary>
    /// Data quality based on how many files were readable
    /// </summary>
    public DataQuality DataQuality { get; set; } = DataQuality.Insufficient;

    /// <summary>
    /// When this analysis was generated
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this came from cache or fresh generation
    /// </summary>
    public bool CacheHit { get; set; }
}

/// <summary>
/// Market bias recommendation
/// </summary>
public enum MarketBias
{
    Bullish,
    Bearish,
    Neutral
}

/// <summary>
/// Data quality indicator
/// </summary>
public enum DataQuality
{
    Complete,   // All data sources available
    Partial,    // Some data missing
    Insufficient // Most data missing
}
