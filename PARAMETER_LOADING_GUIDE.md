# Strategy Parameter Loading Guide

## Overview

All active strategies (S2, S3, S6, S11) now support session-aware parameter loading from optimized JSON files. This enables weekly Bayesian optimization to benefit all strategies equally, achieving 100% learning effectiveness across the trading system.

## Architecture

### Parameter Loading Flow

```
┌──────────────────────────────────────┐
│  Strategy Execution (e.g., S3)      │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  GetSessionName(DateTime.UtcNow)     │
│  Returns: "Overnight", "RTH", or     │
│           "PostRTH"                   │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  S{N}Parameters.LoadOptimal()        │
│  - Checks hourly cache               │
│  - Loads from JSON if cache expired  │
│  - Validates parameters              │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  LoadOptimalForSession(sessionName)  │
│  - Returns session-specific params   │
│  - Falls back to defaults if missing │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  Strategy uses parameters            │
│  Fallback: S{N}RuntimeConfig         │
└──────────────────────────────────────┘
```

### Session Definitions

Sessions are determined by Eastern Time (ET):

- **Overnight**: 18:00 (6 PM) to 08:30 (8:30 AM) next day
- **RTH** (Regular Trading Hours): 09:30 to 16:00
- **PostRTH**: 16:00 to 18:00

Default fallback: **RTH**

## File Structure

### Location

Parameter files are stored in:
```
artifacts/current/parameters/
├── S2_parameters.json
├── S3_parameters.json
├── S6_parameters.json
└── S11_parameters.json
```

### JSON Format

```json
{
  "default_parameters": {
    "StopAtrMult": 1.5,
    "TargetAtrMult": 3.0,
    "MinVolume": 3000,
    "NewsBlockBeforeMin": 2,
    "NewsBlockAfterMin": 3
  },
  "session_overrides": {
    "RTH": {
      "StopAtrMult": 1.5,
      "TargetAtrMult": 3.0,
      "MinVolume": 3000
    },
    "Overnight": {
      "StopAtrMult": 2.0,
      "TargetAtrMult": 3.5,
      "MinVolume": 2000
    },
    "PostRTH": {
      "StopAtrMult": 1.8,
      "TargetAtrMult": 3.2,
      "MinVolume": 2500
    }
  }
}
```

## Implementation Details

### S2 Strategy (VWAP Mean Reversion)

**File**: `src/BotCore/Strategy/AllStrategies.cs`

**Parameters Loaded**:
- `MinVolume` - Minimum bar volume required
- `StopAtrMult` - Stop loss ATR multiplier (via parameters)
- `TargetAtrMult` - Target ATR multiplier (via parameters)

**Code Pattern**:
```csharp
TradingBot.Abstractions.StrategyParameters.S2Parameters? sessionParams = null;
try
{
    var sessionName = GetSessionName(DateTime.UtcNow);
    var baseParams = TradingBot.Abstractions.StrategyParameters.S2Parameters.LoadOptimal();
    sessionParams = baseParams.LoadOptimalForSession(sessionName);
}
catch (Exception)
{
    sessionParams = null;
}

var minVolume = sessionParams?.MinVolume ?? S2RuntimeConfig.MinVolume;
```

### S3 Strategy (Compression Breakout)

**File**: `src/BotCore/Strategy/S3Strategy.cs`

**Parameters Loaded**:
- `MinVolume` - Minimum bar volume required
- `NewsBlockBeforeMin` - Minutes to block before news
- `NewsBlockAfterMin` - Minutes to block after news

**Additional Features**:
- Includes `GetSessionName()` helper method
- Session-aware parameter loading before strategy execution

### S6 Strategy (Opening Drive)

**File**: `src/BotCore/Strategy/S6_S11_Bridge.cs`

**Parameters Loaded**:
- `StopAtrMult` - Stop loss ATR multiplier (converted to double)

**Notes**:
- Uses type conversion: `sessionParams?.StopAtrMult ?? (double)S6RuntimeConfig.StopAtrMult`
- Operates during 09:28-10:00 ET window

### S11 Strategy (ADR/IB Exhaustion Fade)

**File**: `src/BotCore/Strategy/S6_S11_Bridge.cs`

**Parameters Loaded**:
- `StopAtrMult` - Stop loss ATR multiplier (converted to double)

**Notes**:
- Shares `GetSessionName()` helper with S6
- Operates during 13:30-15:30 ET window

## Caching Behavior

- **Cache Duration**: 1 hour (3600 seconds)
- **Cache Key**: Per-strategy (S2, S3, S6, S11)
- **Reload Trigger**: Hourly check via `DateTime.UtcNow`
- **Thread Safety**: Static cached parameters with timestamp check

### Cache Invalidation

The cache automatically reloads when:
1. More than 1 hour has passed since last load
2. First access after application start
3. No manual cache invalidation needed

## Fallback Behavior

Parameter loading gracefully degrades through multiple levels:

1. **Primary**: Session-specific parameters from JSON file
2. **Secondary**: Default parameters from JSON file
3. **Tertiary**: Hard-coded parameters in `S{N}Parameters` class
4. **Quaternary**: Runtime configuration from `S{N}RuntimeConfig`

### Example Fallback Chain

```csharp
// Level 1: Try session-specific from JSON
var params = LoadOptimalForSession("RTH");

// Level 2: Already included in LoadOptimal() - uses default_parameters

// Level 3: Hard-coded defaults in S3Parameters class
public int MinVolume { get; set; } = 3000;

// Level 4: RuntimeConfig fallback in strategy code
var minVolume = sessionParams?.MinVolume ?? S3RuntimeConfig.MinVolume;
```

## Validation

### Parameter Validation

Each strategy validates loaded parameters:

**S2Parameters.Validate()**:
- `TargetAtrMult` > `StopAtrMult + 0.5`
- `StopAtrMult` in range [1.0, 4.0]
- `TargetAtrMult` in range [2.0, 6.0]

**S3Parameters.Validate()**:
- Width rank: [0.05, 0.4]
- Squeeze bars: [3, 15]
- Stop ATR: [0.5, 3.0]
- Target R: [1.0, 4.0]

**S6Parameters.Validate()**:
- ADX: [10.0, 40.0]
- RVOL: [0.5, 5.0]
- Stop ATR: [0.5, 3.0]
- Target ADR fraction: [0.05, 0.5]

**S11Parameters.Validate()**:
- Max ADX: [15.0, 30.0]
- Min RVOL: [0.5, 5.0]
- Stop ATR: [0.5, 3.0]
- Target ADR fraction: [0.05, 0.3]

### JSON Validation

Use Python to validate JSON structure:
```bash
python3 -m json.tool artifacts/current/parameters/S2_parameters.json
```

## Training Integration

### Weekly Optimization Workflow

1. **Friday 17:00 ET**: Market closes
2. **Saturday**: Training orchestrator runs Bayesian optimization
   - Script: `src/Training/training_orchestrator.py`
   - Strategies: S2, S3, S6, S11
   - Sessions: Overnight, RTH, PostRTH
3. **Saturday Evening**: Optimized parameters written to JSON files
4. **Sunday 18:00 ET**: Market opens, strategies load new parameters

### Training Orchestrator

**File**: `src/Training/training_orchestrator.py`

**Strategies Trained**:
```python
STRATEGIES = ['S2', 'S3', 'S6', 'S11']  # All active strategies
```

**Output Files**:
```python
STAGE_DIR = 'artifacts/stage/parameters'
CURRENT_DIR = 'artifacts/current/parameters'
```

### Parameter Promotion

After validation, parameters are promoted:
```bash
cp artifacts/stage/parameters/S2_parameters.json artifacts/current/parameters/
cp artifacts/stage/parameters/S3_parameters.json artifacts/current/parameters/
cp artifacts/stage/parameters/S6_parameters.json artifacts/current/parameters/
cp artifacts/stage/parameters/S11_parameters.json artifacts/current/parameters/
```

## Testing

### Unit Test Pattern

```csharp
[Fact]
public void S3_LoadsSessionParameters_Successfully()
{
    // Arrange
    var sessionName = "RTH";
    var baseParams = S3Parameters.LoadOptimal();
    
    // Act
    var sessionParams = baseParams.LoadOptimalForSession(sessionName);
    
    // Assert
    Assert.NotNull(sessionParams);
    Assert.True(sessionParams.Validate());
}
```

### Integration Test

```bash
# Create test parameter files
./test_parameter_loading.sh

# Build and verify no compilation errors
dotnet build src/BotCore/BotCore.csproj --no-restore

# Run strategy with test parameters
# (Parameters will be loaded automatically)
```

## Monitoring

### Log Messages

Look for these log patterns in production:

**Successful Load**:
```
[S3-STRATEGY] Loaded optimized parameters for session: RTH
[S3-STRATEGY] MinVolume: 3000, StopAtrMult: 1.1
```

**Fallback to Defaults**:
```
[S3-STRATEGY] Parameter loading failed, using RuntimeConfig defaults
```

### Metrics to Track

1. **Parameter Load Success Rate**
   - Target: >99%
   - Alert: <95%

2. **Cache Hit Rate**
   - Expected: ~99% (hourly reload)
   - Alert: <90%

3. **Strategy Performance Improvement**
   - Baseline: Pre-optimization Sharpe ratio
   - Target: +10-20% improvement per strategy

## Troubleshooting

### Problem: Parameters not loading

**Symptoms**: Strategies use hard-coded defaults

**Solutions**:
1. Check file exists: `ls -la artifacts/current/parameters/S3_parameters.json`
2. Validate JSON: `python3 -m json.tool artifacts/current/parameters/S3_parameters.json`
3. Check file permissions: `chmod 644 artifacts/current/parameters/*.json`
4. Review logs for exceptions

### Problem: Invalid parameters loaded

**Symptoms**: Strategy validation fails, falls back to defaults

**Solutions**:
1. Check parameter ranges in validation methods
2. Verify JSON structure matches expected format
3. Test parameters in staging environment first

### Problem: Session not detected correctly

**Symptoms**: Wrong session parameters used

**Solutions**:
1. Verify server timezone: `timedatectl`
2. Check Eastern Time conversion in logs
3. Validate session time boundaries in `GetSessionName()`

## Best Practices

1. **Always validate parameters** after optimization before deployment
2. **Test in staging** with new parameters for at least 1 day
3. **Monitor performance metrics** after parameter updates
4. **Keep backups** of working parameter sets
5. **Document parameter changes** in version control
6. **Use gradual rollout** for significant parameter changes

## Future Enhancements

### Planned Features

1. **Real-time Parameter Updates**
   - Hot reload without strategy restart
   - WebSocket-based parameter push

2. **A/B Testing Framework**
   - Run multiple parameter sets simultaneously
   - Compare performance in real-time

3. **Adaptive Learning**
   - Intraday parameter adjustments
   - Market regime detection

4. **Extended Parameters**
   - All strategy parameters loadable from JSON
   - Complete RuntimeConfig replacement

## References

- **Parameter Files**: `src/Abstractions/StrategyParameters/`
- **Strategy Implementations**: `src/BotCore/Strategy/`
- **Training Orchestrator**: `src/Training/training_orchestrator.py`
- **Configuration Guide**: `PHASE_1_2_3_IMPLEMENTATION_SUMMARY.md`
