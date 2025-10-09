using BotCore.Models;

namespace BotCore.Patterns;

/// <summary>
/// Interface for pattern detection implementations
/// Each detector analyzes price/volume data and returns confidence scores
/// </summary>
public interface IPatternDetector
{
    /// <summary>
    /// Pattern name for identification
    /// </summary>
    string PatternName { get; }
    
    /// <summary>
    /// Pattern family (candlestick, structural, continuation, reversal)
    /// </summary>
    PatternFamily Family { get; }
    
    /// <summary>
    /// Number of bars required for this pattern detection
    /// </summary>
    int RequiredBars { get; }
    
    /// <summary>
    /// Detect pattern in the provided bars
    /// </summary>
    /// <param name="bars">Historical price/volume bars</param>
    /// <returns>Pattern detection result with score and metadata</returns>
    PatternResult Detect(IReadOnlyList<Bar> bars);
}

/// <summary>
/// Pattern family classifications
/// </summary>
public enum PatternFamily
{
    Candlestick,
    Structural, 
    Continuation,
    Reversal
}

/// <summary>
/// Pattern detection result
/// </summary>
public class PatternResult
{
    /// <summary>
    /// Pattern confidence score 0.0 to 1.0
    /// </summary>
    public double Score { get; set; }
    
    /// <summary>
    /// Direction bias: 1 for bullish, -1 for bearish, 0 for neutral
    /// </summary>
    public int Direction { get; set; }
    
    /// <summary>
    /// Pattern confidence level
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// Additional metadata for the pattern
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// Timestamp when pattern was detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Bar index where pattern completes
    /// </summary>
    public int CompletionIndex { get; set; }
}