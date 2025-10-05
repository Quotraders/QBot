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
    private const int DefaultSanityTestVectors = 200;
    private const double DefaultMaxTotalVariation = 0.20;
    private const double DefaultMaxKLDivergence = 0.25;
    private const double DefaultMinProbability = 1e-10;
    private const int DefaultSimulationBars = 5000;
    private const double DefaultMaxDrawdownMultiplier = 2.0;
    private const bool DefaultFailOnNaNInfinity = true;

    public int SanityTestVectors { get; init; } = DefaultSanityTestVectors;
    public double MaxTotalVariation { get; init; } = DefaultMaxTotalVariation;
    public double MaxKLDivergence { get; init; } = DefaultMaxKLDivergence;
    public double MinProbability { get; init; } = DefaultMinProbability;
    public int SimulationBars { get; init; } = DefaultSimulationBars;
    public double MaxDrawdownMultiplier { get; init; } = DefaultMaxDrawdownMultiplier;
    public bool FailOnNaNInfinity { get; init; } = DefaultFailOnNaNInfinity;

    public static Gate4Config LoadFromEnvironment()
    {
        return new Gate4Config
        {
            SanityTestVectors = int.TryParse(Environment.GetEnvironmentVariable("GATE4_SANITY_TEST_VECTORS"), out var stv) ? stv : DefaultSanityTestVectors,
            MaxTotalVariation = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_TOTAL_VARIATION"), out var mtv) ? mtv : DefaultMaxTotalVariation,
            MaxKLDivergence = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_KL_DIVERGENCE"), out var mkl) ? mkl : DefaultMaxKLDivergence,
            MinProbability = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MIN_PROBABILITY"), out var mp) ? mp : DefaultMinProbability,
            SimulationBars = int.TryParse(Environment.GetEnvironmentVariable("GATE4_SIMULATION_BARS"), out var sb) ? sb : DefaultSimulationBars,
            MaxDrawdownMultiplier = double.TryParse(Environment.GetEnvironmentVariable("GATE4_MAX_DRAWDOWN_MULTIPLIER"), out var mdm) ? mdm : DefaultMaxDrawdownMultiplier,
            FailOnNaNInfinity = bool.TryParse(Environment.GetEnvironmentVariable("GATE4_FAIL_ON_NAN_INFINITY"), out var fon) ? fon : DefaultFailOnNaNInfinity
        };
    }
}
