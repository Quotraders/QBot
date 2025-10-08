# Learned Parameters Export Guide

## Overview

The Position Management Optimizer automatically exports learned parameter recommendations to JSON files for review and analysis. This allows traders to see what the bot has learned from trading outcomes and make informed decisions about parameter adjustments.

## Export Location

Learned parameters are exported to:
```
artifacts/learned_parameters/
├── S2_learned_params.json
├── S3_learned_params.json
├── S6_learned_params.json
└── S11_learned_params.json
```

**Note**: The `artifacts/` directory is in `.gitignore` to prevent committing environment-specific learned parameters to the repository.

## Export Schedule

- **Automatic Export**: Every 24 hours via the Position Management Optimizer background service
- **Manual Export**: Call `PositionManagementOptimizer.ExportLearnedParameters()` anytime
- **First Export**: Triggered on first optimization cycle after 24 hours of service uptime

## JSON Structure

Each strategy export file contains:

```json
{
  "strategyName": "S2",
  "exportTimestamp": "2024-10-08T09:00:00.000Z",
  "totalTradesAnalyzed": 45,
  "parameters": [
    {
      "parameterName": "BreakevenAfterTicks",
      "currentConfiguredValue": "N/A",
      "learnedOptimalValue": "8",
      "tradesAnalyzed": 35,
      "confidenceScore": "Medium",
      "performanceImprovement": "N/A",
      "sampleWinRate": 71.4
    },
    {
      "parameterName": "TrailingStopMultiplier",
      "currentConfiguredValue": "N/A",
      "learnedOptimalValue": "1.5",
      "tradesAnalyzed": 45,
      "confidenceScore": "Medium",
      "performanceImprovement": "N/A",
      "sampleWinRate": 68.9
    },
    {
      "parameterName": "MaxHoldTimeMinutes",
      "currentConfiguredValue": "N/A",
      "learnedOptimalValue": "38",
      "tradesAnalyzed": 42,
      "confidenceScore": "Medium",
      "performanceImprovement": "N/A",
      "sampleWinRate": 69.0
    }
  ]
}
```

## Field Descriptions

### Top Level
- **strategyName**: Strategy identifier (S2, S3, S6, S11)
- **exportTimestamp**: UTC timestamp when export was generated
- **totalTradesAnalyzed**: Total number of trades recorded for this strategy
- **parameters**: Array of learned parameter recommendations

### Parameter Fields
- **parameterName**: Name of the parameter being analyzed
- **currentConfiguredValue**: Current value in strategy configuration (placeholder for future)
- **learnedOptimalValue**: Optimal value learned from trade outcomes
- **tradesAnalyzed**: Number of trades used to learn this parameter
- **confidenceScore**: Confidence level based on sample size
  - `Low`: < 30 trades
  - `Medium`: 30-100 trades
  - `High`: > 100 trades
- **performanceImprovement**: Performance gain percentage (placeholder for future)
- **sampleWinRate**: Win rate for trades with this parameter value

## Parameters Tracked

### BreakevenAfterTicks
The number of ticks in profit before moving stop loss to breakeven.

**Learning Logic**: Groups trades by breakeven trigger distance, calculates average PnL for each, recommends the value with highest average PnL.

**Typical Range**: 4-16 ticks

### TrailingStopMultiplier
Multiplier applied to ATR for trailing stop distance.

**Learning Logic**: Groups trades by trailing multiplier, calculates average PnL for each, recommends the value with highest average PnL.

**Typical Range**: 0.8-2.0x

### MaxHoldTimeMinutes
Maximum time to hold a position before exiting (even if not at target).

**Learning Logic**: Analyzes trade durations for non-timed-out trades, recommends 1.5x the average duration to provide buffer while preventing excessive holding.

**Typical Range**: 15-120 minutes

## Using Exported Parameters

### Review Workflow

1. **Check Export Files**: Review files in `artifacts/learned_parameters/`
2. **Validate Confidence**: Only trust recommendations with Medium or High confidence
3. **Compare to Current**: Compare learned values to your current strategy configuration
4. **Analyze Win Rates**: Look for parameters with significantly higher win rates
5. **Gradual Adoption**: Test learned parameters in staging before production

### Example Review

```bash
# View S2 learned parameters
cat artifacts/learned_parameters/S2_learned_params.json | jq '.'

# Check all strategies at once
for f in artifacts/learned_parameters/*.json; do
  echo "=== $(basename $f) ==="
  cat $f | jq '.strategyName, .totalTradesAnalyzed, .parameters[].confidenceScore'
done
```

### Warning Signs

- **Low Sample Size**: Confidence score "Low" with < 30 trades - don't trust recommendations yet
- **Extreme Values**: Learned parameters way outside typical ranges may indicate data quality issues
- **100% Win Rate**: Likely insufficient data or unrealistic market conditions
- **Zero Trades**: Empty parameters array means no data collected yet

## Safety Features

### Isolation
Export failures never crash the trading system. All errors are caught and logged.

### Error Handling
- File write failures logged as errors
- JSON serialization errors handled gracefully
- Directory creation failures don't stop trading
- Individual strategy export failures don't prevent other strategies from exporting

### Logging
- Export start: `📤 [PM-OPTIMIZER] Starting learned parameters export...`
- Per-strategy success: `✅ [PM-OPTIMIZER] Exported {Strategy} learned parameters`
- Export complete: `📦 [PM-OPTIMIZER] Export complete. Successfully exported {Count}/{Total} strategies`
- Errors: `❌ [PM-OPTIMIZER] Error writing export file for {Strategy}`

## Manual Export

To trigger an export manually from code:

```csharp
// Get the optimizer service from DI
var optimizer = serviceProvider.GetRequiredService<PositionManagementOptimizer>();

// Trigger export
optimizer.ExportLearnedParameters();
```

## Empty Data Handling

When no trades have been recorded yet:

```json
{
  "strategyName": "S11",
  "exportTimestamp": "2024-10-08T09:00:00.000Z",
  "totalTradesAnalyzed": 0,
  "parameters": []
}
```

This is normal during initial startup or for strategies that haven't traded yet.

## Troubleshooting

### Problem: Export files not created

**Solutions**:
1. Check directory permissions: `ls -la artifacts/learned_parameters/`
2. Check logs for export errors: `grep "PM-OPTIMIZER" logs/*.log`
3. Verify optimizer service is running: Check for startup log `🧠 [PM-OPTIMIZER] Position Management Optimizer starting`

### Problem: Empty parameters array

**Solutions**:
1. Wait for more trades to be recorded (need at least 10 samples per parameter)
2. Check that trades are being recorded: Look for `RecordOutcome` calls in logs
3. Verify strategy name matches exactly (case-sensitive: S2, S3, S6, S11)

### Problem: All confidence scores "Low"

**Solutions**:
1. Normal for < 30 trades - wait for more data
2. Consider longer data collection period before trusting recommendations
3. Check if trading frequency is sufficient to generate adequate samples

## Integration with Training System

Learned parameters from live trading complement the ML/RL training system:

1. **Live Learning**: Real-time parameter optimization from actual trades
2. **Training Data**: Export files can be used as input for historical analysis
3. **Validation**: Compare live learned parameters with trained model recommendations
4. **Continuous Improvement**: Blend live learning with offline training results

## Next Steps

After reviewing learned parameters:

1. **Stage Testing**: Test learned parameters in `artifacts/stage/parameters/`
2. **Validation**: Run backtest with learned parameters to validate improvement
3. **Promotion**: Copy validated parameters to `artifacts/current/parameters/`
4. **Monitoring**: Watch performance after applying learned parameters

See `PARAMETER_LOADING_GUIDE.md` for details on parameter file management.
