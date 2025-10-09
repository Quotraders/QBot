namespace BotCore.Features;

/// <summary>
/// Configuration for feature computation parameters.
/// All values are validated against bounds and must come from configuration.
/// </summary>
public class FeatureComputationConfig
{
    // Default threshold constants
    private const decimal DefaultZScoreThresholdBullish = 1.0m;
    private const decimal DefaultZScoreThresholdBearish = -1.0m;
    
    // Validation bounds for period-based parameters
    private const int MIN_PERIOD = 2;
    private const int MAX_PERIOD = 100;
    
    // Validation bounds for bar/window counts
    private const int MIN_BAR_COUNT = 1;
    private const int MAX_BAR_COUNT = 1000;
    
    // Time-based validation bounds
    private const int MIN_MINUTES_PER_DAY = 60;
    private const int MAX_MINUTES_PER_DAY = 1440;  // 24 hours
    private const int HOURS_PER_DAY = 24;
    
    // Z-Score validation bounds
    private const decimal MIN_ZSCORE_THRESHOLD = 0.1m;
    private const decimal MAX_ZSCORE_THRESHOLD = 10.0m;
    private const decimal MIN_ZSCORE_THRESHOLD_BEARISH = -10.0m;
    private const decimal MAX_ZSCORE_THRESHOLD_BEARISH = -0.1m;
    
    // Coherence validation bounds
    private const decimal MIN_COHERENCE_THRESHOLD = 0.0m;
    private const decimal MAX_COHERENCE_THRESHOLD = 1.0m;
    
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
    /// ZScore threshold for bullish S7 regime (default: 1.00)
    /// </summary>
    public decimal S7ZScoreThresholdBullish { get; init; } = DefaultZScoreThresholdBullish;

    /// <summary>
    /// ZScore threshold for bearish S7 regime (default: -1.0)
    /// </summary>
    public decimal S7ZScoreThresholdBearish { get; init; } = DefaultZScoreThresholdBearish;

    /// <summary>
    /// Coherence threshold for S7 regime activation (default: 0.6)
    /// </summary>
    public decimal S7CoherenceThreshold { get; init; } = 0.6m;

    /// <summary>
    /// Validate configuration bounds
    /// </summary>
    public void Validate()
    {
        if (RsiPeriod < MIN_PERIOD || RsiPeriod > MAX_PERIOD)
            throw new ArgumentException($"RsiPeriod must be between {MIN_PERIOD} and {MAX_PERIOD}");
        
        if (AtrPeriod < MIN_PERIOD || AtrPeriod > MAX_PERIOD)
            throw new ArgumentException($"AtrPeriod must be between {MIN_PERIOD} and {MAX_PERIOD}");
        
        if (BollingerPeriod < MIN_PERIOD || BollingerPeriod > MAX_PERIOD)
            throw new ArgumentException($"BollingerPeriod must be between {MIN_PERIOD} and {MAX_PERIOD}");
        
        if (VwapBars < MIN_BAR_COUNT || VwapBars > MAX_BAR_COUNT)
            throw new ArgumentException($"VwapBars must be between {MIN_BAR_COUNT} and {MAX_BAR_COUNT}");
        
        if (AdrDays < MIN_BAR_COUNT || AdrDays > MAX_PERIOD)
            throw new ArgumentException($"AdrDays must be between {MIN_BAR_COUNT} and {MAX_PERIOD}");
        
        if (MinutesPerDay < MIN_MINUTES_PER_DAY || MinutesPerDay > MAX_MINUTES_PER_DAY)
            throw new ArgumentException($"MinutesPerDay must be between {MIN_MINUTES_PER_DAY} and {MAX_MINUTES_PER_DAY}");
        
        if (CurrentRangeBars < MIN_BAR_COUNT || CurrentRangeBars > MAX_BAR_COUNT)
            throw new ArgumentException($"CurrentRangeBars must be between {MIN_BAR_COUNT} and {MAX_BAR_COUNT}");
        
        if (HoursPerDay != HOURS_PER_DAY)
            throw new ArgumentException($"HoursPerDay must be {HOURS_PER_DAY}");
        
        if (S7ZScoreThresholdBullish < MIN_ZSCORE_THRESHOLD || S7ZScoreThresholdBullish > MAX_ZSCORE_THRESHOLD)
            throw new ArgumentException($"S7ZScoreThresholdBullish must be between {MIN_ZSCORE_THRESHOLD} and {MAX_ZSCORE_THRESHOLD}");
        
        if (S7ZScoreThresholdBearish > MAX_ZSCORE_THRESHOLD_BEARISH || S7ZScoreThresholdBearish < MIN_ZSCORE_THRESHOLD_BEARISH)
            throw new ArgumentException($"S7ZScoreThresholdBearish must be between {MIN_ZSCORE_THRESHOLD_BEARISH} and {MAX_ZSCORE_THRESHOLD_BEARISH}");
        
        if (S7CoherenceThreshold < MIN_COHERENCE_THRESHOLD || S7CoherenceThreshold > MAX_COHERENCE_THRESHOLD)
            throw new ArgumentException($"S7CoherenceThreshold must be between {MIN_COHERENCE_THRESHOLD} and {MAX_COHERENCE_THRESHOLD}");
    }
}
