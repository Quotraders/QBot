# MAE/MFE Optimal Stop Placement Learning

## Overview

MAE/MFE Learning is a machine learning feature that optimizes stop placement by analyzing historical trade excursions. It learns "How far against me do winning trades typically go?" and "How much profit do I give back before exit?" to optimize stop loss and trailing stop distances.

## Problem Statement

Current fixed stops are suboptimal:
- **Fixed Stop Loss**: All S2 trades use 1.5x ATR stop, but:
  - 90% of winning trades never go more than 6 ticks against entry
  - 80% of losing trades go 10+ ticks against entry
  - We're waiting too long to exit losers
  
- **Fixed Trailing Stops**: Trails at 10 ticks, but:
  - Average MFE is +18 ticks, average exit is +12 ticks
  - We're giving back 6 ticks on average after reaching peak
  - Trailing could be tightened to lock in more profit

## Solution

The bot now:
1. **Collects MAE/MFE Data**: Tracks Max Adverse Excursion and Max Favorable Excursion for every trade
2. **Statistical Analysis**: Calculates percentile distributions for winning vs losing trades
3. **Learns Optimal Thresholds**: Determines optimal early exit and trailing distances
4. **Applies Learning**: Uses learned thresholds with safety guardrails

## Configuration

Add to `.env`:

```bash
# MAE/MFE Learning Configuration
BOT_MAE_LEARNING_ENABLED=true
BOT_MAE_MINIMUM_SAMPLES=50
BOT_MAE_ANALYSIS_INTERVAL_MINUTES=5
BOT_MAE_SAFETY_BUFFER_TICKS=2
BOT_MFE_LEARNING_ENABLED=true
```

## How It Works

### Feature 1: MAE-Based Early Exit

#### Data Collection
Every position exit records:
```csharp
optimizer.RecordOutcome(
    strategy: "S2",
    finalPnL: 12.5m,
    maxFavorableExcursion: 18.0m,  // Peak profit
    maxAdverseExcursion: -5.5m,    // Worst drawdown
    targetHit: true,
    stoppedOut: false,
    marketRegime: "Trend"
);
```

#### Statistical Analysis
After 50+ trades:
```csharp
// Get winning trades only
var winningTrades = outcomes.Where(o => o.TargetHit);

// Calculate MAE distribution
var maeValues = winningTrades.Select(o => Math.Abs(o.MaxAdverseExcursion));

// Get 95th percentile
var mae95th = Percentile(maeValues, 0.95);  // e.g., -8 ticks

// Add safety buffer
var optimalThreshold = mae95th + 2;  // -10 ticks
```

**Key Insight**: If 95% of winning trades never go more than -8 ticks against entry, we can safely exit at -10 ticks (with 2 tick buffer). Trades going beyond -10 ticks are statistically likely to be losers.

#### Application
During position monitoring:
```csharp
// Calculate current adverse excursion
var currentMAE = Math.Abs((maxAdversePrice - entryPrice) / tickSize);

// Get learned threshold
var threshold = optimizer.GetOptimalEarlyExitThreshold(strategy, regime);

// Exit early if exceeded
if (currentMAE > threshold)
{
    _logger.LogWarning("Early exit: MAE {Current} exceeds threshold {Threshold}",
        currentMAE, threshold);
    await ClosePositionAsync(state, ExitReason.StopLoss);
}
```

### Feature 2: MFE-Based Trailing Optimization

#### Data Collection
Tracks profit giveback:
```csharp
// For each profitable trade:
var giveback = maxFavorableExcursion - Math.Abs(finalPnL);
// Example: MFE = +18 ticks, exit = +12 ticks, giveback = 6 ticks
```

#### Statistical Analysis
```csharp
// Calculate average giveback
var avgGiveback = profitableTrades.Average(o => o.MFE - o.FinalPnL);

// Calculate median giveback (more robust than mean)
var medianGiveback = Median(givebacks);

// Optimal trailing distance: half the median giveback
var optimalTrail = medianGiveback * 0.5m;
```

**Key Insight**: If we're giving back 6 ticks on average after reaching peak, we should trail closer (e.g., 3 ticks instead of 10 ticks).

#### Application
During trailing stop updates:
```csharp
// Get learned optimal distance
var optimizedTrail = optimizer.GetOptimalTrailingDistance(strategy, regime);

// Use learned distance if available, otherwise use default
var trailTicks = optimizedTrail ?? state.TrailTicks;

// Calculate new stop
var newStop = isLong 
    ? currentPrice - (trailTicks * tickSize)
    : currentPrice + (trailTicks * tickSize);
```

## Per-Strategy/Regime Learning

Each strategy learns independently for each regime:

### S2 Strategy
```
Trending:
  MAE 95th percentile: -14 ticks → Early exit at -16 ticks
  MFE giveback: 8 ticks → Trail at 4 ticks

Ranging:
  MAE 95th percentile: -8 ticks → Early exit at -10 ticks
  MFE giveback: 4 ticks → Trail at 2 ticks
```

### S3 Strategy
```
Trending:
  MAE 95th percentile: -10 ticks → Early exit at -12 ticks
  MFE giveback: 6 ticks → Trail at 3 ticks

Ranging:
  MAE 95th percentile: -12 ticks → Early exit at -14 ticks
  MFE giveback: 5 ticks → Trail at 2.5 ticks
```

## Safety Guardrails

### Critical Safety Rule
**NEVER LOOSEN STOPS - ONLY TIGHTEN**

```csharp
// Example: Strategy default stop is -15 ticks
// Learned threshold is -20 ticks (looser)
if (learnedThreshold > defaultStop)
{
    // REJECT - would increase risk
    return defaultStop;
}

// Learned threshold is -10 ticks (tighter)
if (learnedThreshold < defaultStop)
{
    // ACCEPT - reduces risk
    return learnedThreshold;
}
```

### Minimum Sample Requirements
- **Minimum Samples**: 50 trades (configurable)
- **Analysis Frequency**: Every 5 minutes (configurable)
- **Buffer**: +2 ticks safety margin on MAE threshold

### Regime-Specific Validation
- Only uses data from same regime (Trend/Range/Transition)
- Falls back to "ALL" regimes if insufficient samples
- Requires minimum 10 trades per regime for learning

## Expected Benefits

Based on problem statement analysis:

### MAE Learning
- **Faster Loser Exits**: Exit losing trades 20-30% faster
- **Reduced Drawdown**: Cut losses by 15-25% through early detection
- **Improved Win Rate**: Avoid 5-10% of marginal losing trades

### MFE Learning
- **Better Profit Capture**: Lock in 10-20% more profit before reversals
- **Reduced Giveback**: Cut profit giveback by 30-40%
- **Improved R-Multiple**: Capture 0.3-0.5R more per winning trade

## Monitoring

The bot logs learning progress:

```
📊 [MAE-LEARNING] Optimal early exit for S2 in Trend: 14.2 ticks 
   (95th percentile: 12.2, buffer: 2.0, samples: 73)

📊 [MFE-LEARNING] Optimal trailing distance for S2 in Trend: 4.5 ticks
   (median giveback: 9.0, avg: 10.2, samples: 68)

🚨 [MAE-LEARNING] Early exit triggered for POS-12345: 
   MAE 15.3 ticks exceeds threshold 14.2 ticks
```

## Analysis API

Query learning statistics:

```csharp
// Get MAE distribution
var maeStats = optimizer.AnalyzeMaeDistribution("S2", "Trend");
// Returns: (p50: 5.0, p90: 10.5, p95: 14.2, samples: 73)

// Get MFE distribution
var mfeStats = optimizer.AnalyzeMfeDistribution("S2", "Trend");
// Returns: (avgMfe: 18.5, avgFinalPnL: 12.3, avgGiveback: 6.2, samples: 68)

// Get optimal thresholds
var maeThreshold = optimizer.GetOptimalEarlyExitThreshold("S2", "Trend");
var mfeTrail = optimizer.GetOptimalTrailingDistance("S2", "Trend");
```

## Integration with Dynamic Targeting

MAE/MFE Learning works seamlessly with Dynamic R-Multiple Targeting:

1. **Entry**: Dynamic targeting sets regime-based profit target
2. **Monitoring**: MAE learning checks for early exit opportunities
3. **Profit-Taking**: MFE learning optimizes trailing stop to lock in gains
4. **Exit**: Both features work together to maximize edge

Example flow:
```
Entry: S2 Long @ 5850, Trend regime
  → Dynamic target: 2.5R = 5862.50 (trending R-multiple)
  → MAE threshold: -14 ticks (learned for S2 + Trend)

Monitoring: Price moves to 5860 (+10 ticks)
  → Activate trailing stop with MFE-learned distance: 4 ticks
  
Regime Change: Trend → Range
  → Adjust target: 5862.50 → 5854.00 (1.0R for ranging)
  → Update MAE threshold: -14 → -10 ticks (tighter for ranging)
  → Update trail: 4 → 2 ticks (tighter for ranging)
```

## Disabling the Features

Individual control:
```bash
# Disable MAE early exit learning
BOT_MAE_LEARNING_ENABLED=false

# Disable MFE trailing optimization
BOT_MFE_LEARNING_ENABLED=false
```

When disabled, the bot uses strategy default stops and trailing distances.

## Future Enhancements

Potential improvements:
1. **Volatility Adjustment**: Scale thresholds based on current ATR
2. **Time-of-Day Learning**: Different thresholds for different sessions
3. **Correlation Analysis**: Learn when MAE predicts stop-outs
4. **Confidence Intervals**: Add statistical confidence to recommendations
