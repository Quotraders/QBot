using System.Collections.Generic;

namespace BotCore.StrategyDsl;

/// <summary>
/// DSL strategy definition loaded from YAML
/// </summary>
public class DslStrategy
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Bias { get; set; } = "both";
    public string Priority { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty; // Added for knowledge graph compatibility
    
    public IReadOnlyList<string> TelemetryTags { get; set; } = new List<string>();
    public DslConditions RegimeFilters { get; set; } = new();
    public DslConditions ZoneConditions { get; set; } = new();
    public DslConditions PatternConditions { get; set; } = new();
    public DslConditions MicroConditions { get; set; } = new();
    public DslWhen? When { get; set; } // Added for knowledge graph compatibility
    public IReadOnlyList<string>? Contra { get; set; } // Direct contraindications from YAML
    public IReadOnlyList<string> Confluence { get; set; } = new List<string>(); // Direct confluence from YAML
    public DslPlaybook? Playbook { get; set; } // Added for knowledge graph compatibility
    
    public DslRiskManagement RiskManagement { get; set; } = new();
    public DslConfidenceCalculation ConfidenceCalculation { get; set; } = new();
    public DslDirectionLogic DirectionLogic { get; set; } = new();
    public DslMetadata Metadata { get; set; } = new();
}

/// <summary>
/// DSL conditions container
/// </summary>
public class DslConditions
{
    public IReadOnlyList<string> Required { get; set; } = new List<string>();
    public IReadOnlyList<string> Preferred { get; set; } = new List<string>();
    public IReadOnlyList<string> Blocked { get; set; } = new List<string>();
    public IReadOnlyList<string> Confluence { get; set; } = new List<string>();
    public IReadOnlyList<string> Contraindications { get; set; } = new List<string>();
    public IReadOnlyList<string> EntryTriggers { get; set; } = new List<string>();
    public IReadOnlyList<string> Timing { get; set; } = new List<string>();
}

/// <summary>
/// DSL when condition for evaluation
/// </summary>
public class DslWhen
{
    public string Expression { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Weight { get; set; } = 1.0;
    public bool IsRequired { get; set; }
    
    // Knowledge graph compatibility properties
    public IReadOnlyList<string> Regime { get; init; } = new List<string>();
    public IReadOnlyList<string> Micro { get; init; } = new List<string>();
    public IReadOnlyList<string> MicroConditions { get; init; } = new List<string>();
    public IReadOnlyList<string> ContraIndications { get; init; } = new List<string>();
}

/// <summary>
/// DSL playbook containing multiple strategies
/// </summary>
public class DslPlaybook
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<DslStrategy> Strategies { get; init; } = new List<DslStrategy>();
    public IReadOnlyDictionary<string, object> Settings { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// DSL risk management configuration
/// </summary>
public class DslRiskManagement
{
    public DslPositionSizing PositionSizing { get; set; } = new();
    public DslStopLoss StopLoss { get; set; } = new();
    public DslTakeProfit TakeProfit { get; set; } = new();
}

/// <summary>
/// DSL position sizing configuration
/// </summary>
public class DslPositionSizing
{
    public int BaseSize { get; set; } = 1;
    public bool ConfidenceMultiplier { get; set; } = true;
    public bool TrendStrengthMultiplier { get; set; }
    public bool VolatilityAdjusted { get; set; }
    public int MaxSize { get; set; } = 5;
}

/// <summary>
/// DSL stop loss configuration
/// </summary>
public class DslStopLoss
{
    public string Type { get; set; } = "atr_based";
    public double DistanceAtr { get; set; } = 1.0;
    public double TrailDistanceAtr { get; set; } = 0.8;
    public int MinimumDistance { get; set; } = 4;
    public double DistanceBeyondBreakout { get; set; } = 0.25;
    public double DistanceInsideRectangle { get; set; } = 0.3;
    public double DistanceBelowPullbackLow { get; set; } = 0.4;
    public double DistanceAboveHandleLow { get; set; } = 0.3;
}

/// <summary>
/// DSL take profit configuration
/// </summary>
public class DslTakeProfit
{
    public string Type { get; set; } = "atr_based";
    public double InitialTargetAtr { get; set; } = 2.0;
    public double ExtensionTargetAtr { get; set; } = 3.0;
    public double TrailAfterProfit { get; set; } = 0.5;
    public IReadOnlyList<double> ExtensionLevels { get; init; } = new List<double>();
    public IReadOnlyList<double> PartialExitLevels { get; init; } = new List<double>();
    public bool PartialExitAt50Pct { get; set; }
    public double MinimumRMultiple { get; set; } = 1.5;
    public string TargetZone { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public double PartialExit { get; set; }
}

/// <summary>
/// DSL confidence calculation configuration
/// </summary>
public class DslConfidenceCalculation
{
    public double BaseConfidence { get; set; } = 0.65;
    public IReadOnlyDictionary<string, double> Boosters { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Penalties { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// DSL direction logic configuration
/// </summary>
public class DslDirectionLogic
{
    public IReadOnlyList<string> LongConditions { get; init; } = new List<string>();
    public IReadOnlyList<string> ShortConditions { get; init; } = new List<string>();
}

/// <summary>
/// DSL metadata
/// </summary>
public class DslMetadata
{
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = string.Empty;
    public string LastUpdated { get; set; } = string.Empty;
    public DslBacktestResults? BacktestResults { get; set; }
}

/// <summary>
/// DSL backtest results
/// </summary>
public class DslBacktestResults
{
    public double WinRate { get; set; }
    public double AvgRMultiple { get; set; }
    public double MaxDrawdown { get; set; }
}