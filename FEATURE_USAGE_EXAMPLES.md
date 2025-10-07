# Feature Usage Examples: Dynamic R-Multiple Targeting & MAE/MFE Learning

## Quick Start

### 1. Enable Features in .env

```bash
# Dynamic R-Multiple Targeting
BOT_DYNAMIC_TARGETS_ENABLED=true
BOT_REGIME_CHECK_INTERVAL_SECONDS=60
BOT_TARGET_ADJUSTMENT_THRESHOLD=0.3

# Strategy-specific R-multiples
S2_TARGET_TRENDING=2.5
S2_TARGET_RANGING=1.0
S3_TARGET_TRENDING=3.0
S3_TARGET_RANGING=1.2

# MAE/MFE Learning
BOT_MAE_LEARNING_ENABLED=true
BOT_MAE_MINIMUM_SAMPLES=50
BOT_MAE_ANALYSIS_INTERVAL_MINUTES=5
BOT_MAE_SAFETY_BUFFER_TICKS=2
BOT_MFE_LEARNING_ENABLED=true
```

### 2. Start Trading Bot

```bash
./dev-helper.sh build
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
```

### 3. Monitor Logs

Watch for feature activation:
```
📊 [POSITION-MGMT] Dynamic R-Multiple Targeting enabled: regime check every 60s
🧠 [POSITION-MGMT] MAE/MFE Learning enabled: MAE=True, MFE=True
```

## Example Scenarios

### Scenario 1: S2 Strategy in Trending Market

**Setup**:
- Strategy: S2 (scalping)
- Market: Strong uptrend detected
- Entry: ES @ 5850.00 LONG
- Initial Stop: 5844.00 (-6 ticks = -1.5 points)
- Traditional Target: 5854.80 (+4.8 points = 1.2R)

**With Dynamic Targeting**:
```
📝 [POSITION-MGMT] Registered position POS-12345: S2 ES 1@5850.00, 
   Regime: Trend, Static target: 5854.80, Dynamic target: 5859.00
```

**Calculation**:
```
Risk = 5850.00 - 5844.00 = 6.00 points
R-Multiple (S2 Trending) = 2.5
Reward = 6.00 * 2.5 = 15.00 points
Dynamic Target = 5850.00 + 15.00 = 5865.00
```

**Result**: Target increased from +4.8 to +15.0 points (3x improvement)

---

### Scenario 2: Regime Change During Trade

**Initial State** (T+0 seconds):
- Position: S3 LONG @ 5850.00
- Regime: Trending
- Target: 5868.00 (3.0R)
- Stop: 5844.00

**Regime Change** (T+120 seconds):
```
📊 [POSITION-MGMT] Regime change detected for POS-23456: Trend → Range
🎯 [POSITION-MGMT] Target adjusted for POS-23456: 5868.00 → 5857.20 (Range)
```

**Calculation**:
```
Old R-Multiple: 3.0 (trending)
New R-Multiple: 1.2 (ranging)
Risk: 6.00 points
New Reward: 6.00 * 1.2 = 7.20 points
New Target: 5850.00 + 7.20 = 5857.20
```

**Why**: Market lost momentum, tighten target to take profit before reversal.

---

### Scenario 3: MAE-Based Early Exit

**Learning Phase** (After 50 S2 trades):
```
📊 [MAE-LEARNING] Optimal early exit for S2 in Range: 10.0 ticks 
   (95th percentile: 8.0, buffer: 2.0, samples: 52)
```

**Trade Execution**:
- Entry: ES LONG @ 5850.00
- Stop: 5844.00 (-6 ticks)
- Traditional Stop: Would wait until -24 ticks (full stop loss)
- Learned Threshold: -10 ticks

**Price Action**:
```
T+0:   Entry @ 5850.00
T+30s: Price @ 5848.50 (-6 ticks)
T+60s: Price @ 5847.50 (-10 ticks)
```

**Early Exit Triggered**:
```
🚨 [MAE-LEARNING] Early exit triggered for POS-34567: 
   MAE 10.5 ticks exceeds learned threshold 10.0 ticks
```

**Result**: Exit at -10 ticks instead of waiting for full -24 tick stop loss. Saved 14 ticks ($175 for ES).

---

### Scenario 4: MFE-Based Trailing Optimization

**Learning Phase** (After 50 profitable trades):
```
📊 [MFE-LEARNING] Optimal trailing distance for S2 in Trend: 3.5 ticks
   (median giveback: 7.0, avg: 8.2, samples: 54)
```

**Trade Execution**:
- Entry: ES LONG @ 5850.00
- Traditional Trail: 10 ticks behind peak
- Learned Trail: 3.5 ticks behind peak

**Price Action**:
```
T+0:    Entry @ 5850.00
T+120s: Price @ 5862.00 (+12 ticks) → Trailing activated
T+180s: Price @ 5865.00 (+15 ticks, peak)
        Traditional Stop: 5855.00 (+5 ticks)
        Learned Stop:     5864.12 (+14.12 ticks)
T+210s: Price reverses to 5863.00
        Traditional: Still in trade (no hit)
        Learned: EXITED at 5864.12
```

**Result**: Captured 14.12 ticks vs traditional 5 ticks. Additional 9.12 ticks profit ($114 for ES).

---

### Scenario 5: Combined Features in Action

**Complete Trade Flow**:

**Entry (T+0)**:
```
Strategy: S2
Entry: ES LONG @ 5850.00
Regime: Trend
Stop: 5844.00 (-6 ticks)
Dynamic Target: 5859.00 (2.5R trending)
MAE Threshold: -14 ticks (learned)
MFE Trail: 4 ticks (learned)
```

**Early Trade (T+30s)**:
```
Price: 5848.00 (-8 ticks)
MAE Check: -8 < -14 → Continue (within learned threshold)
Status: Position healthy
```

**Mid Trade (T+90s)**:
```
Price: 5856.00 (+6 ticks)
Breakeven: Activated, stop → 5850.25
Trailing: Not yet (needs +10 ticks)
```

**Profitable Move (T+150s)**:
```
Price: 5860.00 (+10 ticks)
Trailing: Activated with learned 4-tick distance
Stop: 5859.00 (4 ticks behind)
```

**Regime Change (T+180s)**:
```
📊 [POSITION-MGMT] Regime change: Trend → Range
🎯 Target adjusted: 5859.00 → 5854.00 (1.0R for ranging)
Current price: 5861.00 → Already exceeded new target!
Status: Take profit signal (above target)
```

**Exit (T+200s)**:
```
Price: 5861.50 (+11.5 ticks)
Reason: Dynamic target hit
P&L: +11.5 ticks = $143.75 for ES
```

**Comparison**:
- Traditional (static 1.2R): Would target 5854.80 (+4.8 ticks) = $150
- With Dynamic Targeting: Captured 5861.50 (+11.5 ticks) = $143.75
- But avoided holding into reversal by regime adjustment

---

## Monitoring and Analysis

### Check Learning Progress

```bash
# View optimizer statistics
dotnet run --project tools/AnalyzeOptimizer.csproj -- --strategy S2
```

**Output**:
```
Strategy: S2
Total Trades: 127
Win Rate: 68.5%

MAE Analysis (Trend):
  50th percentile: 5.2 ticks
  90th percentile: 11.8 ticks
  95th percentile: 14.5 ticks
  Optimal threshold: 16.5 ticks (95th + 2.0 buffer)

MFE Analysis (Trend):
  Avg MFE: 18.7 ticks
  Avg Final P&L: 12.3 ticks
  Avg Giveback: 6.4 ticks
  Optimal trail: 3.2 ticks (50% of median giveback)
```

### View Live Position Stats

In logs:
```
📊 [POSITION-MGMT] Active positions: 3
  POS-45678: S2 ES +8.5 ticks, Regime: Trend, Target: 5859.00 (2.5R)
  POS-45679: S3 NQ +12.0 ticks, Regime: Range, Target: 15342.00 (1.2R)
  POS-45680: S11 ES +5.0 ticks, Regime: Trend, Target: 5848.50 (2.5R)
```

## Configuration Tuning

### Conservative Setup (Safer)
```bash
# More frequent regime checks
BOT_REGIME_CHECK_INTERVAL_SECONDS=30

# Higher adjustment threshold (less target changes)
BOT_TARGET_ADJUSTMENT_THRESHOLD=0.5

# More samples required before learning
BOT_MAE_MINIMUM_SAMPLES=100

# Larger safety buffer
BOT_MAE_SAFETY_BUFFER_TICKS=3
```

### Aggressive Setup (More Responsive)
```bash
# Less frequent checks (trust initial regime)
BOT_REGIME_CHECK_INTERVAL_SECONDS=120

# Lower threshold (more target adjustments)
BOT_TARGET_ADJUSTMENT_THRESHOLD=0.2

# Fewer samples (learn faster)
BOT_MAE_MINIMUM_SAMPLES=30

# Smaller buffer (tighter stops)
BOT_MAE_SAFETY_BUFFER_TICKS=1
```

## Troubleshooting

### Issue: Not Enough Data for MAE/MFE Learning

**Symptom**:
```
⚠️ [MAE-LEARNING] Not enough data for S2 in Trend (samples: 23 < 50)
```

**Solutions**:
1. Reduce `BOT_MAE_MINIMUM_SAMPLES` to 30
2. Wait for more trades to accumulate
3. Use "ALL" regimes instead of regime-specific (automatic fallback)

### Issue: Target Not Adjusting

**Symptom**:
```
📊 [POSITION-MGMT] Regime change for POS-56789 but target adjustment below threshold (8%)
```

**Solutions**:
1. Lower `BOT_TARGET_ADJUSTMENT_THRESHOLD` from 0.3 to 0.2
2. Increase R-multiple difference between regimes (e.g., S2 trending: 3.0 instead of 2.5)

### Issue: Too Many Early Exits

**Symptom**: Win rate dropped after enabling MAE learning

**Solutions**:
1. Increase `BOT_MAE_SAFETY_BUFFER_TICKS` from 2 to 3 or 4
2. Increase `BOT_MAE_MINIMUM_SAMPLES` to require more data
3. Review strategy: May indicate the strategy itself needs adjustment

## Performance Metrics

Track improvement over time:

```bash
# Before (static targets):
S2 Win Rate: 62%
S2 Avg Profit: $145
S2 Avg Loss: -$180

# After (dynamic targets + MAE/MFE):
S2 Win Rate: 68% (+6%)
S2 Avg Profit: $185 (+27%)
S2 Avg Loss: -$145 (-19%)
```

## Best Practices

1. **Start Conservative**: Use default settings for first 100 trades
2. **Monitor Logs**: Watch for regime changes and learning progress
3. **Review Weekly**: Check if learned thresholds make sense
4. **Backtest First**: Test settings on historical data before live trading
5. **Document Changes**: Keep notes on configuration adjustments and their impact
6. **Disable if Uncertain**: Better to use static targets than misconfigured learning

## Next Steps

1. Run bot with features enabled for 1-2 weeks
2. Collect minimum 50-100 trades per strategy
3. Review learning statistics
4. Fine-tune R-multiples based on actual performance
5. Consider per-regime optimization once sufficient data collected
