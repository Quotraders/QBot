namespace BotCore.Features;

/// <summary>
/// Configuration for feature computation parameters.
/// All values are validated against bounds and must come from configuration.
/// </summary>
public class FeatureComputationConfig
{
    /// <summary>
    /// Period for RSI calculation (default: 14)
    /// </summary>
    public int RsiPeriod { get; init; } = 14;

    /// <summary>
    /// Period for ATR calculation (default: 14)
    /// </summary>
    public int AtrPeriod { get; init; } = 14;

    /// <summary>
    /// Period for Bollinger Bands calculation (default: 20)
    /// </summary>
    public int BollingerPeriod { get; init; } = 20;

    /// <summary>
    /// Number of bars to use for VWAP calculation (default: 20)
    /// </summary>
    public int VwapBars { get; init; } = 20;

    /// <summary>
    /// Number of days for ADR calculation (default: 14)
    /// </summary>
    public int AdrDays { get; init; } = 14;

    /// <summary>
    /// Approximate minutes per trading day (default: 390)
    /// </summary>
    public int MinutesPerDay { get; init; } = 390;

    /// <summary>
    /// Number of bars for current range calculation (default: 20)
    /// </summary>
    public int CurrentRangeBars { get; init; } = 20;

    /// <summary>
    /// Hours in a day for hour fraction calculation (default: 24)
    /// </summary>
    public int HoursPerDay { get; init; } = 24;

    /// <summary>
    /// ZScore threshold for bullish S7 regime (default: 1.0)
    /// </summary>
    public decimal S7ZScoreThresholdBullish { get; init; } = 1.0m;

    /// <summary>
    /// ZScore threshold for bearish S7 regime (default: -1.0)
    /// </summary>
    public decimal S7ZScoreThresholdBearish { get; init; } = -1.0m;

    /// <summary>
    /// Coherence threshold for S7 regime activation (default: 0.6)
    /// </summary>
    public decimal S7CoherenceThreshold { get; init; } = 0.6m;

    /// <summary>
    /// Validate configuration bounds
    /// </summary>
    public void Validate()
    {
        if (RsiPeriod < 2 || RsiPeriod > 100)
            throw new ArgumentOutOfRangeException(nameof(RsiPeriod), "Must be between 2 and 100");
        
        if (AtrPeriod < 2 || AtrPeriod > 100)
            throw new ArgumentOutOfRangeException(nameof(AtrPeriod), "Must be between 2 and 100");
        
        if (BollingerPeriod < 2 || BollingerPeriod > 100)
            throw new ArgumentOutOfRangeException(nameof(BollingerPeriod), "Must be between 2 and 100");
        
        if (VwapBars < 1 || VwapBars > 1000)
            throw new ArgumentOutOfRangeException(nameof(VwapBars), "Must be between 1 and 1000");
        
        if (AdrDays < 1 || AdrDays > 100)
            throw new ArgumentOutOfRangeException(nameof(AdrDays), "Must be between 1 and 100");
        
        if (MinutesPerDay < 60 || MinutesPerDay > 1440)
            throw new ArgumentOutOfRangeException(nameof(MinutesPerDay), "Must be between 60 and 1440");
        
        if (CurrentRangeBars < 1 || CurrentRangeBars > 1000)
            throw new ArgumentOutOfRangeException(nameof(CurrentRangeBars), "Must be between 1 and 1000");
        
        if (HoursPerDay != 24)
            throw new ArgumentOutOfRangeException(nameof(HoursPerDay), "Must be 24");
        
        if (S7ZScoreThresholdBullish < 0.1m || S7ZScoreThresholdBullish > 10.0m)
            throw new ArgumentOutOfRangeException(nameof(S7ZScoreThresholdBullish), "Must be between 0.1 and 10.0");
        
        if (S7ZScoreThresholdBearish > -0.1m || S7ZScoreThresholdBearish < -10.0m)
            throw new ArgumentOutOfRangeException(nameof(S7ZScoreThresholdBearish), "Must be between -10.0 and -0.1");
        
        if (S7CoherenceThreshold < 0.0m || S7CoherenceThreshold > 1.0m)
            throw new ArgumentOutOfRangeException(nameof(S7CoherenceThreshold), "Must be between 0.0 and 1.0");
    }
}
