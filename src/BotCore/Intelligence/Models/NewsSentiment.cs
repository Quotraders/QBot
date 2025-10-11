namespace BotCore.Intelligence.Models;

/// <summary>
/// Quick sentiment structure for 5-minute updates
/// Lightweight model for fast news sentiment checks
/// </summary>
public class NewsSentiment
{
    /// <summary>
    /// Bullish sentiment percentage (0-100)
    /// </summary>
    public decimal BullishPercentage { get; set; }

    /// <summary>
    /// Bearish sentiment percentage (0-100)
    /// </summary>
    public decimal BearishPercentage { get; set; }

    /// <summary>
    /// Neutral sentiment percentage (0-100)
    /// </summary>
    public decimal NeutralPercentage { get; set; }

    /// <summary>
    /// Top 3 most important news items
    /// </summary>
    public List<string> TopHeadlines { get; set; } = new();

    /// <summary>
    /// Sentiment shift compared to previous reading
    /// </summary>
    public SentimentShift SentimentShift { get; set; } = SentimentShift.Unchanged;

    /// <summary>
    /// When sentiment was calculated
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Sentiment shift indicator
/// </summary>
public enum SentimentShift
{
    MoreBullish,
    MoreBearish,
    Unchanged
}
