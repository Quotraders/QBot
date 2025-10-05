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
    public int MinTrades { get; init; } = 50;
    public int MinMinutes { get; init; } = 60;
    public int MaxMinutes { get; init; } = 90;
    public double WinRateDropThreshold { get; init; } = 0.15;
    public double MaxDrawdownDollars { get; init; } = 500.0;
    public double SharpeDropThreshold { get; init; } = 0.30;
    public double CatastrophicWinRateThreshold { get; init; } = 0.30;
    public double CatastrophicDrawdownDollars { get; init; } = 1000.0;
    public bool Enabled { get; init; } = true;

    public static Gate5Config LoadFromEnvironment()
    {
        return new Gate5Config
        {
            MinTrades = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MIN_TRADES"), out var mt) ? mt : 50,
            MinMinutes = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MIN_MINUTES"), out var mm) ? mm : 60,
            MaxMinutes = int.TryParse(Environment.GetEnvironmentVariable("GATE5_MAX_MINUTES"), out var maxm) ? maxm : 90,
            WinRateDropThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_WIN_RATE_DROP_THRESHOLD"), out var wrd) ? wrd : 0.15,
            MaxDrawdownDollars = double.TryParse(Environment.GetEnvironmentVariable("GATE5_MAX_DRAWDOWN_DOLLARS"), out var mdd) ? mdd : 500.0,
            SharpeDropThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_SHARPE_DROP_THRESHOLD"), out var sdt) ? sdt : 0.30,
            CatastrophicWinRateThreshold = double.TryParse(Environment.GetEnvironmentVariable("GATE5_CATASTROPHIC_WIN_RATE"), out var cwr) ? cwr : 0.30,
            CatastrophicDrawdownDollars = double.TryParse(Environment.GetEnvironmentVariable("GATE5_CATASTROPHIC_DRAWDOWN"), out var cdd) ? cdd : 1000.0,
            Enabled = bool.TryParse(Environment.GetEnvironmentVariable("GATE5_ENABLED"), out var en) ? en : true
        };
    }
}
