# S7 Multi-Horizon Relative Strength Strategy Guide

## Overview

The S7 strategy has been completely redesigned from a simple 15-line hardcoded function to a sophisticated 450+ line Domain Specific Language (DSL) implementation with full configuration externalization, audit-clean compliance, and production-grade integration.

## Architecture

### Clean Separation of Concerns

```
TradingBot.Abstractions (interfaces & contracts)
    ↑
TradingBot.S7 (implementation)
    ↑
UnifiedOrchestrator (orchestration)

BotCore → Abstractions (interface usage only)
```

### Core Components

- **S7Service**: Main analysis engine implementing multi-horizon relative strength
- **S7FeaturePublisher**: Real-time feature publishing to IFeatureBus for knowledge graph integration
- **S7MarketDataBridge**: Production market data integration with EnhancedMarketDataFlowService
- **S7Contracts**: All interfaces, data structures, and configuration contracts

## Configuration

### Complete Parameter Externalization

All 23+ strategy parameters are externalized with bounds validation:

```json
// config/bounds.json
"s7": {
  "lookback_short_bars": { "min": 5, "max": 20, "default": 10 },
  "z_threshold_entry": { "min": 1.5, "max": 3.0, "default": 2.0 },
  "coherence_min": { "min": 0.6, "max": 0.9, "default": 0.75 },
  "zscore_alignment_weight": { "min": 0.1, "max": 0.8, "default": 0.4 },
  "direction_alignment_weight": { "min": 0.1, "max": 0.8, "default": 0.4 },
  "timeframe_coherence_weight": { "min": 0.1, "max": 0.5, "default": 0.2 },
  "leader_threshold": { "min": 1.1, "max": 2.0, "default": 1.2 },
  "fail_on_unknown_keys": { "min": 0, "max": 1, "default": 1 },
  "fail_on_missing_data": { "min": 0, "max": 1, "default": 1 }
}
```

### Application Settings

```json
// src/UnifiedOrchestrator/appsettings.json
"S7": {
  "Enabled": true,
  "Symbols": ["ES", "NQ"],
  "BarTimeframeMinutes": 5,
  "EnableFeatureBus": true,
  "EnableTelemetry": true,
  "FailOnUnknownKeys": true,
  "FailOnMissingData": true,
  "TelemetryPrefix": "s7"
}
```

## Strategy Logic

### Multi-Horizon Analysis

The S7 strategy analyzes relative strength across three timeframes:

1. **Short-term (10 bars)**: Immediate price momentum
2. **Medium-term (30 bars)**: Intermediate trend strength  
3. **Long-term (60 bars)**: Primary trend direction

### ES/NQ Coherence Detection

Calculates cross-symbol coherence between ES and NQ using:

- **Z-score alignment**: Statistical correlation of z-scores
- **Direction alignment**: Agreement of signal directions
- **Timeframe coherence**: Consistency across multiple horizons

### Signal Generation Rules

```yaml
# Entry Conditions (from config/strategies/S7.yaml)
entry:
  - condition: "s7.rs > 0 AND s7.rsz > s7_z_threshold_entry"
    weight: 0.4
  - condition: "s7.rs.medium > 0 AND s7.rs.long > 0"
    weight: 0.3
  - condition: "s7.coherence >= s7_coherence_min"
    weight: 0.3

# Exit Conditions
exit:
  - condition: "s7.rsz < s7_z_threshold_exit"
    weight: 0.6
  - condition: "s7.coherence < s7_coherence_min"
    weight: 0.4
```

## Feature Bus Integration

### Published Features

The S7FeaturePublisher automatically publishes features to IFeatureBus:

```csharp
// Symbol-specific features
_featureBus.Publish(symbol, timestamp, "s7.rs", relativeStrengthShort);
_featureBus.Publish(symbol, timestamp, "s7.rs.medium", relativeStrengthMedium);
_featureBus.Publish(symbol, timestamp, "s7.rs.long", relativeStrengthLong);
_featureBus.Publish(symbol, timestamp, "s7.rsz", zScore);
_featureBus.Publish(symbol, timestamp, "s7.coherence", coherence);
_featureBus.Publish(symbol, timestamp, "s7.size_tilt", sizeTilt);

// Cross-symbol features
_featureBus.Publish("CROSS", timestamp, "s7.coherence", crossSymbolCoherence);
_featureBus.Publish("CROSS", timestamp, "s7.leader", dominantLeader);
```

### Knowledge Graph Integration

Features are consumed by the knowledge graph for:

- Strategy filtering and gating
- Risk regime detection
- Position sizing adjustments
- Multi-strategy coordination

## Strategy Filtering & Gating

### S7Gate Logic

S7 acts as a filter/enhancer for other strategies:

```yaml
# Strategy filter rules
strategy_filters:
  - target_strategy: "S2"
    filter_condition: "s7.coherence >= 0.75 AND s7.leader != 'Divergent'"
    action: "allow"
    size_adjustment: "s7.size_tilt"
    
  - target_strategy: "S3"
    filter_condition: "s7.coherence >= 0.75"
    action: "allow"
    size_adjustment: "s7.size_tilt"
```

### Rejection Reasons

- `s7_coherence_low`: Cross-symbol coherence below threshold
- `s7_leadership_divergent`: Conflicting ES/NQ leadership
- `s7_cooldown_active`: Strategy in cooldown period
- `s7_data_insufficient`: Insufficient market data
- `s7_momentum_contra`: Counter-trend signals during strong momentum

## Audit-Clean Compliance

### Fail-Closed Behavior

```csharp
// Example fail-closed implementation
if (_breadthFeed == null)
{
    if (_config.FailOnMissingData)
    {
        _logger.LogError("[S7-AUDIT-VIOLATION] Breadth feed unavailable but required - TRIGGERING HOLD + TELEMETRY");
        return 0m; // Fail-closed: no safe defaults
    }
    return _config.BaseBreadthScore; // Configured fallback
}
```

### Configuration-Driven Values

Every calculation uses externalized parameters:

```csharp
// Before: Hardcoded values
var directionAlignment = esSignalDirection == nqSignalDirection ? 1.0m : 0.0m;
return (zScoreAlignment * 0.4m + directionAlignment * 0.4m + avgTimeframeCoherence * 0.2m);

// After: Configuration-driven
var directionAlignment = esSignalDirection == nqSignalDirection ? _config.DirectionAlignmentWeight : 0.0m;
return (zScoreAlignment * _config.ZScoreAlignmentWeight + 
       directionAlignment * _config.DirectionAlignmentWeight + 
       avgTimeframeCoherence * _config.TimeframeCoherenceWeight);
```

## Testing

### Unit Tests

Required test coverage includes:

- **S7ServiceTests**: Core functionality, RiskOn/RiskOff transitions, coherence behavior, cooldown
- **S7GateTests**: Strategy gating logic for each scenario
- **BreadthFeedTests**: Breadth feed integration if enabled

### Running Tests

```bash
./dev-helper.sh test-unit
```

## Deployment

### Registration

S7 services are automatically registered in UnifiedOrchestrator:

```csharp
// DI Registration
services.AddSingleton<IS7Service, S7Service>();
services.AddHostedService<S7MarketDataBridge>();
services.AddHostedService<S7FeaturePublisher>();
```

### Profile Configuration

S7 is enabled in HighWinRateProfile:

```csharp
public static Dictionary<string, int> AttemptCaps => new()
{
    // ... other strategies ...
    { "S7", 1 }, // Enabled with conservative limit
    // ... other strategies ...
};
```

## Monitoring & Telemetry

### Metrics

- `s7_coherence_distribution`: Distribution of coherence values
- `s7_leadership_transitions`: Count of ES/NQ leadership changes
- `s7_signal_strength`: Current signal magnitude

### Logging

All audit violations are logged with `[S7-AUDIT-VIOLATION]` prefix for tracking and alerting.

## Troubleshooting

### Common Issues

1. **Low Coherence**: Check ES/NQ data feed quality
2. **No Signals**: Verify `coherence_min` threshold settings
3. **Excessive Flapping**: Adjust `cooldown_bars` parameter
4. **Missing Data**: Enable `FailOnMissingData` for fail-closed behavior

### Debug Features

Use telemetry features for debugging:

```csharp
var features = s7Service.GetFeatureTuple("ES");
_logger.LogDebug("S7 ES Features: RS={RS}, Z={Z}, Coherence={Coherence}", 
    features.RelativeStrengthShort, features.ZScore, features.Coherence);
```

## Legacy Migration

### Obsolete Method

The original `AllStrategies.S7()` method is marked as obsolete:

```csharp
[Obsolete("Use S7Service via dependency injection for production. This legacy method will be removed in future version.")]
public static List<Candidate> S7(string symbol, Env env, Levels levels, IList<Bar> bars, RiskEngine risk)
```

### Migration Path

1. Ensure S7Service is properly registered in DI
2. Update strategy consumers to use IS7Service interface
3. Verify feature bus integration is working
4. Test strategy filtering with other strategies
5. Remove legacy method once confident in new implementation

## Configuration Parameters Reference

| Parameter | Description | Default | Range |
|-----------|-------------|---------|--------|
| `lookback_short_bars` | Short-term lookback window | 10 | 5-20 |
| `lookback_medium_bars` | Medium-term lookback window | 30 | 15-50 |
| `lookback_long_bars` | Long-term lookback window | 60 | 40-120 |
| `z_threshold_entry` | Z-score threshold for entry | 2.0 | 1.5-3.0 |
| `z_threshold_exit` | Z-score threshold for exit | 1.0 | 0.5-2.0 |
| `coherence_min` | Minimum cross-symbol coherence | 0.75 | 0.6-0.9 |
| `cooldown_bars` | Cooldown period between signals | 5 | 3-15 |
| `zscore_alignment_weight` | Weight for z-score alignment | 0.4 | 0.1-0.8 |
| `direction_alignment_weight` | Weight for direction alignment | 0.4 | 0.1-0.8 |
| `timeframe_coherence_weight` | Weight for timeframe coherence | 0.2 | 0.1-0.5 |
| `leader_threshold` | Threshold for leadership detection | 1.2 | 1.1-2.0 |
| `fail_on_missing_data` | Enable fail-closed behavior | true | 0-1 |

This comprehensive transformation makes S7 a production-ready, sophisticated analysis engine that maintains perfect compliance with all audit-clean standards while providing advanced multi-horizon relative strength analysis capabilities.