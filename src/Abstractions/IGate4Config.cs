namespace TradingBot.Abstractions;

/// <summary>
/// Gate 4: Model Reload Safety configuration
/// </summary>
public interface IGate4Config
{
    int SanityTestVectors { get; }
    double MaxTotalVariation { get; }
    double MaxKLDivergence { get; }
    double MinProbability { get; }
    int SimulationBars { get; }
    double MaxDrawdownMultiplier { get; }
    bool FailOnNaNInfinity { get; }
}

/// <summary>
/// Gate 4 configuration implementation with environment variable support
/// </summary>
public class Gate4Config : IGate4Config
{
    public int SanityTestVectors { get; init; } = 200;
    public double MaxTotalVariation { get; init; } = 0.20;
    public double MaxKLDivergence { get; init; } = 0.25;
    public double MinProbability { get; init; } = 1e-10;
    public int SimulationBars { get; init; } = 5000;
    public double MaxDrawdownMultiplier { get; init; } = 2.0;
    public bool FailOnNaNInfinity { get; init; } = true;

    public static Gate4Config LoadFromEnvironment()
    {
        return new Gate4Config
        {
            SanityTestVectors = int.TryParse(Environment.GetEnvironmentVariable("GATE4_SANITY_TEST_VECTORS"), out var stv) ? stv : 200,
            MaxTotalVariation = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_TOTAL_VARIATION"), out var mtv) ? mtv : 0.20,
            MaxKLDivergence = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_KL_DIVERGENCE"), out var mkl) ? mkl : 0.25,
            MinProbability = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MIN_PROBABILITY"), out var mp) ? mp : 1e-10,
            SimulationBars = int.TryParse(Environment.GetEnvironmentVariable("GATE4_SIMULATION_BARS"), out var sb) ? sb : 5000,
            MaxDrawdownMultiplier = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_DRAWDOWN_MULTIPLIER"), out var mdm) ? mdm : 2.0,
            FailOnNaNInfinity = bool.TryParse(Environment.GetEnvironmentVariable("GATE4_FAIL_ON_NAN_INFINITY"), out var fon) ? fon : true
        };
    }
}
