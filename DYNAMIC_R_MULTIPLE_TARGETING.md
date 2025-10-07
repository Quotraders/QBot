# Dynamic R-Multiple Targeting

## Overview

Dynamic R-Multiple Targeting is an adaptive position management feature that adjusts profit targets based on current market regime. Instead of using fixed profit targets, the bot dynamically calculates targets based on whether the market is trending, ranging, or in transition.

## Problem Statement

Traditional fixed targets leave significant profit on the table:
- **In Trending Markets**: Fixed 1.2R targets exit too early when trends could run to 3.0R
- **In Ranging Markets**: Fixed 1.5R targets hold too long when quick 0.8R scalps are optimal
- **In Volatile Markets**: Fixed targets don't account for unpredictable price action

## Solution

The bot now:
1. **Captures Entry Regime**: Records market regime (Trend/Range/Transition) at position entry
2. **Calculates Dynamic Target**: Uses regime-specific R-multiples to set profit targets
3. **Monitors Regime Changes**: Checks regime every 60 seconds during position lifetime
4. **Adjusts Targets**: Updates targets if regime shifts significantly

## Configuration

Add to `.env`:

```bash
# Dynamic R-Multiple Targeting Configuration
BOT_DYNAMIC_TARGETS_ENABLED=true
BOT_REGIME_CHECK_INTERVAL_SECONDS=60
BOT_TARGET_ADJUSTMENT_THRESHOLD=0.3

# Strategy-Specific R-Multiples
S2_TARGET_TRENDING=2.5
S2_TARGET_RANGING=1.0
S3_TARGET_TRENDING=3.0
S3_TARGET_RANGING=1.2
S6_TARGET_TRENDING=2.0
S6_TARGET_RANGING=1.0
S11_TARGET_TRENDING=2.5
S11_TARGET_RANGING=1.5
```

## How It Works

### Step 1: Entry Regime Detection

When a position is registered, the bot:
```csharp
// Capture entry regime
var regimeService = _serviceProvider.GetService<RegimeDetectionService>();
var entryRegime = await regimeService.GetCurrentRegimeAsync(symbol);

// Store in position state
state.EntryRegime = entryRegime;
state.CurrentRegime = entryRegime;
```

### Step 2: Dynamic Target Calculation

```csharp
// Calculate target based on regime and strategy
var risk = Math.Abs(entryPrice - stopPrice);
var rMultiple = GetRegimeBasedRMultiple(strategy, regime);
var reward = risk * rMultiple;
var dynamicTarget = isLong ? entryPrice + reward : entryPrice - reward;
```

### Step 3: Real-Time Regime Monitoring

Every 60 seconds (configurable):
```csharp
// Check for regime change
var newRegime = await regimeService.GetCurrentRegimeAsync(symbol);

if (newRegime != state.CurrentRegime)
{
    // Recalculate target
    var newTarget = CalculateDynamicTarget(state, entryPrice, stopPrice);
    
    // Apply if change is significant (>30% by default)
    if (changePercent >= _targetAdjustmentThreshold)
    {
        state.DynamicTargetPrice = newTarget;
        _logger.LogInformation("Target adjusted: {Old} → {New} ({Regime})",
            oldTarget, newTarget, newRegime);
    }
}
```

## Strategy-Specific Behavior

### S2 Strategy (Scalping)
- **Trending Markets**: Target 2.5R (ride strong momentum)
- **Ranging Markets**: Target 1.0R (quick in-and-out)
- **Logic**: Scalp strategy benefits most from regime adaptation

### S3 Strategy (Swing Trading)
- **Trending Markets**: Target 3.0R (capture full trend move)
- **Ranging Markets**: Target 1.2R (limited upside in ranges)
- **Logic**: Swing strategy needs room in trends, exits fast in chop

### S6 Strategy (Momentum)
- **Trending Markets**: Target 2.0R (momentum extension)
- **Ranging Markets**: Target 1.0R (reduced hold time)
- **Logic**: Already momentum-based, enhance with regime context

### S11 Strategy (Pattern-Based)
- **Trending Markets**: Target 2.5R (pattern + trend = powerful)
- **Ranging Markets**: Target 1.5R (pattern alone is weaker)
- **Logic**: Patterns work better with trend confirmation

## Safety Features

1. **Minimum Threshold**: Only adjusts if change exceeds 30% (configurable)
2. **Never Tighten Too Much**: Won't reduce target below break-even
3. **Regime Smoothing**: Uses RegimeDetectionService with hysteresis to prevent flip-flopping
4. **Fallback Defaults**: If regime service unavailable, uses static targets

## Expected Benefits

Based on the problem statement:
- **Trending Markets**: Capture 2-3x more profit by riding trends longer
- **Ranging Markets**: Exit 30-40% faster, avoiding false breakouts
- **Overall Win Rate**: Improve by 5-10% through better regime alignment

## Monitoring

The bot logs regime changes and target adjustments:

```
📊 [POSITION-MGMT] Regime change detected for POS-12345: Range → Trend
🎯 [POSITION-MGMT] Target adjusted for POS-12345: 5850.00 → 5862.50 (Trend)
```

## Integration with ML/RL

The `PositionManagementOptimizer` tracks outcomes by regime:
- "S2 trades in trending regime with 2.5R target: 75% success rate, avg profit $450"
- "S2 trades in ranging regime with 1.0R target: 85% success rate, avg profit $180"

Over time, the optimizer suggests optimal R-multiples per strategy per regime based on actual performance data.

## Disabling the Feature

Set in `.env`:
```bash
BOT_DYNAMIC_TARGETS_ENABLED=false
```

When disabled, the bot reverts to static targets from `TradingOrchestratorService`.
