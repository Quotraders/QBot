namespace TradingBot.Abstractions;

/// <summary>
/// Gate 5: Live First-Hour Auto-Rollback configuration
/// </summary>
public interface IGate5Config
{
    int MinTrades { get; }
    int MinMinutes { get; }
    int MaxMinutes { get; }
    double WinRateDropThreshold { get; }
    double MaxDrawdownDollars { get; }
    double SharpeDropThreshold { get; }
    double CatastrophicWinRateThreshold { get; }
    double CatastrophicDrawdownDollars { get; }
    bool Enabled { get; }
}

/// <summary>
/// Gate 5 configuration implementation with environment variable support
/// </summary>
public class Gate5Config : IGate5Config
{
    private const int DefaultMinTrades = 50;
    private const int DefaultMinMinutes = 60;
    private const int DefaultMaxMinutes = 90;
    private const double DefaultWinRateDropThreshold = 0.15;
    private const double DefaultMaxDrawdownDollars = 500.0;
    private const double DefaultSharpeDropThreshold = 0.30;
    private const double DefaultCatastrophicWinRateThreshold = 0.30;
    private const double DefaultCatastrophicDrawdownDollars = 1000.0;
    private const bool DefaultEnabled = true;

    public int MinTrades { get; init; } = DefaultMinTrades;
    public int MinMinutes { get; init; } = DefaultMinMinutes;
    public int MaxMinutes { get; init; } = DefaultMaxMinutes;
    public double WinRateDropThreshold { get; init; } = DefaultWinRateDropThreshold;
    public double MaxDrawdownDollars { get; init; } = DefaultMaxDrawdownDollars;
    public double SharpeDropThreshold { get; init; } = DefaultSharpeDropThreshold;
    public double CatastrophicWinRateThreshold { get; init; } = DefaultCatastrophicWinRateThreshold;
    public double CatastrophicDrawdownDollars { get; init; } = DefaultCatastrophicDrawdownDollars;
    public bool Enabled { get; init; } = DefaultEnabled;

    public static Gate5Config LoadFromEnvironment()
    {
        return new Gate5Config
        {
            MinTrades = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MIN_TRADES"), out var mt) ? mt : DefaultMinTrades,
            MinMinutes = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MIN_MINUTES"), out var mm) ? mm : DefaultMinMinutes,
            MaxMinutes = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MAX_MINUTES"), out var maxm) ? maxm : DefaultMaxMinutes,
            WinRateDropThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_WIN_RATE_DROP_THRESHOLD"), out var wrd) ? wrd : DefaultWinRateDropThreshold,
            MaxDrawdownDollars = double.TryParse(Environment.GetEnvironmentVariable("GATE5_MAX_DRAWDOWN_DOLLARS"), out var mdd) ? mdd : DefaultMaxDrawdownDollars,
            SharpeDropThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_SHARPE_DROP_THRESHOLD"), out var sdt) ? sdt : DefaultSharpeDropThreshold,
            CatastrophicWinRateThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_CATASTROPHIC_WIN_RATE"), out var cwr) ? cwr : DefaultCatastrophicWinRateThreshold,
            CatastrophicDrawdownDollars = double.TryParse(Environment.GetEnvironmentVariable("GATE5_CATASTROPHIC_DRAWDOWN"), out var cdd) ? cdd : DefaultCatastrophicDrawdownDollars,
            Enabled = bool.TryParse(Environment.GetEnvironmentVariable("GATE5_ENABLED"), out var en) ? en : DefaultEnabled
        };
    }
}
