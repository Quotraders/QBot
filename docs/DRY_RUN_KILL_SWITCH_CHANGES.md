# DRY_RUN and Kill Switch Simplification

## Summary of Changes

This document describes the simplification of the bot's trading mode configuration and the automatic DRY_RUN enforcement based on TopStep compliance.

## Previous Behavior (Legacy)

### Multiple Confusing Mode Variables:
- `DRY_RUN` (0/1)
- `PAPER_MODE` (0/1)
- `DEMO_MODE` (0/1)
- `TRADING_MODE` (LIVE/PAPER/DEMO)
- `ENABLE_DRY_RUN` (true/false)
- Multiple conflicting settings that made it unclear what mode the bot was in

### Manual Kill Switch Only:
- Kill.txt had to be created manually
- No automatic protection for TopStep compliance violations

## New Behavior (Simplified)

### Single DRY_RUN Flag:
- `DRY_RUN=1`: Paper trading with **live market data** (simulated trades)
  - Connects to TopstepX API
  - Receives real-time market data
  - Simulates trade execution (no real orders)
  - Perfect for testing strategies with live data
  
- `DRY_RUN=0`: Live trading with **real executions** (⚠️ REAL MONEY AT RISK ⚠️)
  - Connects to TopstepX API
  - Receives real-time market data
  - Executes real trades
  - Requires manual arming token for safety

- Default: `DRY_RUN=1` (safe default - paper trading)

### Automatic DRY_RUN Enforcement for TopStep Compliance:
The bot can **automatically switch to DRY_RUN=1 mode** when TopStep compliance limits are breached (configurable):

- **Daily Loss Limit**: When daily P&L reaches -$1,000 (safe limit) or -$2,400 (hard limit)
- **Drawdown Limit**: When drawdown reaches -$2,000 (safe limit) or -$2,500 (hard limit)
- **Critical Threshold**: When approaching 90% of either limit

**Toggle Control**: Set `ENABLE_AUTO_DRYRUN_ON_COMPLIANCE=true` to enable (default) or `false` to disable

When these limits are hit and auto-enforcement is enabled:
- Bot automatically sets `DRY_RUN=1`
- Continues running with **live market data**
- Simulates trades (no real orders sent)
- Logs CRITICAL messages for visibility
- Protects TopStep account from violations

When auto-enforcement is disabled:
- Bot still logs CRITICAL warnings
- Manual intervention required to switch to DRY_RUN mode
- Allows experienced traders full control

### Manual Kill Switch:
- Kill.txt can still be created manually for critical system failures
- Used for database issues, API failures, or system crashes
- Not for performance degradation or compliance violations

## Configuration

### Environment Variables (Simplified)

```bash
# Trading Mode - SINGLE FLAG
DRY_RUN=1                    # 1=Paper trading, 0=Live trading

# TopStep Compliance Limits (auto-enforces DRY_RUN when breached, if enabled)
ENABLE_AUTO_DRYRUN_ON_COMPLIANCE=true  # Toggle: true=auto DRY_RUN, false=manual only
TOPSTEP_DAILY_LOSS_LIMIT=-2400         # Hard daily loss limit
TOPSTEP_SAFE_DAILY_LOSS_LIMIT=-1000    # Safe daily loss limit (auto DRY_RUN if enabled)
TOPSTEP_DRAWDOWN_LIMIT=-2500           # Hard drawdown limit
TOPSTEP_SAFE_DRAWDOWN_LIMIT=-2000      # Safe drawdown limit (auto DRY_RUN if enabled)

# Connection Settings (unchanged)
ENABLE_TOPSTEPX=1
SKIP_LIVE_CONNECTION=0
AUTH_ALLOW=1

# Auto-execution (only applies when DRY_RUN=0)
ENABLE_AUTO_EXECUTION=true
ENABLE_LIVE_CONNECTION=true
```

### Removed Variables
The following variables are **REMOVED** (no longer needed):
- `PAPER_MODE` - use `DRY_RUN=1` instead
- `DEMO_MODE` - use `DRY_RUN=1` instead
- `TRADING_MODE` - use `DRY_RUN` flag instead
- `ENABLE_DRY_RUN` - use `DRY_RUN` flag instead

## Code Changes

### Files Modified:

1. **`.env`** - Consolidated configuration to single DRY_RUN flag
2. **`ProductionKillSwitchService.cs`** - Simplified to check only DRY_RUN variable
3. **`LiveTradingGate.cs`** - Simplified DRY_RUN checking logic
4. **`TopStepComplianceManager.cs`** - **NEW**: Added automatic DRY_RUN enforcement on compliance violations
5. **`EmergencyStopSystem.cs`** - Updated documentation
6. **`ConfigurationLocks.cs`** - Updated to check DRY_RUN instead of PAPER_MODE
7. **`SystemHealthMonitor.cs`** - Updated to check DRY_RUN instead of PAPER_MODE

### Key Behavior Changes:

1. **IsDryRunMode()** - Now checks only `DRY_RUN` environment variable
   - Returns `true` if `DRY_RUN=1` or unset (safe default)
   - Returns `false` if `DRY_RUN=0` or `DRY_RUN=false`

2. **TopStep Compliance Auto-Protection** - **NEW**
   - Automatically sets `DRY_RUN=1` when daily loss or drawdown limits are breached
   - Bot continues running with live data but simulates trades
   - Protects account from TopStep violations
   - Logs critical messages for visibility

3. **Manual Kill Switch** - Available for true system failures
   - Create `kill.txt` manually for database failures, API issues, etc.
   - Not used for performance degradation or compliance violations

## Migration Guide

### For Developers:

1. Update `.env` file:
   ```bash
   # Old (remove these):
   DRY_RUN=0
   PAPER_MODE=0
   TRADING_MODE=LIVE
   DEMO_MODE=0
   ENABLE_DRY_RUN=false
   
   # New (use this):
   DRY_RUN=0  # or DRY_RUN=1 for paper trading
   ```

2. Code that checks trading mode:
   ```csharp
   // Old (multiple checks):
   var paperMode = Environment.GetEnvironmentVariable("PAPER_MODE");
   var demoMode = Environment.GetEnvironmentVariable("DEMO_MODE");
   var tradingMode = Environment.GetEnvironmentVariable("TRADING_MODE");
   
   // New (single check):
   bool isDryRun = ProductionKillSwitchService.IsDryRunMode();
   ```

3. Kill switch behavior:
   ```bash
   # Old: kill.txt created automatically on catastrophic failure
   # New: Manual creation only
   
   # To trigger kill switch manually:
   echo "Manual shutdown - investigating issue" > kill.txt
   ```

## Testing

### Verify DRY_RUN Behavior:

```bash
# Test 1: Paper trading mode
export DRY_RUN=1
dotnet run --project src/UnifiedOrchestrator
# Expected: Connects to live data, simulates trades

# Test 2: Live trading mode (⚠️ CAUTION)
export DRY_RUN=0
dotnet run --project src/UnifiedOrchestrator
# Expected: Connects to live data, executes real trades
```

### Verify TopStep Compliance Auto-Protection:

```bash
# Simulate daily loss approaching limit
# Bot will automatically switch to DRY_RUN=1 when:
# - Daily P&L reaches -$1,000 (safe limit)
# - Daily P&L reaches -$2,160 (90% of hard limit)
# - Daily P&L reaches -$2,400 (hard limit)

# Simulate drawdown approaching limit
# Bot will automatically switch to DRY_RUN=1 when:
# - Drawdown reaches -$2,000 (safe limit)
# - Drawdown reaches -$2,250 (90% of hard limit)
# - Drawdown reaches -$2,500 (hard limit)

# Monitor logs for:
# 🚨 [TOPSTEP-COMPLIANCE] VIOLATION: Daily loss limit exceeded
# 🛡️ [TOPSTEP-COMPLIANCE] DRY_RUN MODE ENFORCED
# 💡 [TOPSTEP-COMPLIANCE] Bot switched to paper trading - continues with live data but simulates trades
```

### Verify Kill Switch:

```bash
# Manual kill switch activation for system failures:
echo "Database connection lost" > kill.txt
# Expected: Bot detects file and switches to DRY_RUN mode
```

## TopStep Compliance Auto-Protection

### How It Works:

1. **Continuous Monitoring**: Bot tracks daily P&L and drawdown in real-time
2. **Threshold Detection**: Monitors safe limits, critical thresholds (90%), and hard limits
3. **Automatic Enforcement**: When limits are breached:
   - Sets `DRY_RUN=1` environment variable
   - Bot continues running (doesn't shut down)
   - Receives live market data from TopstepX
   - Simulates all trades (no real orders sent)
   - Logs CRITICAL messages for operator awareness

4. **Recovery**: To resume live trading after a DRY_RUN enforcement:
   - Review account status and determine cause
   - Wait for daily reset (5 PM ET) if needed
   - Manually set `DRY_RUN=0` to resume live trading
   - Ensure compliance limits have reset

### Protection Levels:

| Threshold | Daily Loss | Drawdown | Action |
|-----------|-----------|----------|--------|
| Warning (80%) | -$800 | -$1,600 | Log warning, reduce position size |
| Critical (90%) | -$900 | -$1,800 | **Auto DRY_RUN=1**, log critical |
| Safe Limit | -$1,000 | -$2,000 | **Auto DRY_RUN=1**, stop live trades |
| Hard Limit | -$2,400 | -$2,500 | **Auto DRY_RUN=1**, compliance violation |

### Benefits:

1. **Account Protection**: Prevents TopStep evaluation failures
2. **Continuous Operation**: Bot keeps running with live data
3. **No Manual Intervention**: Automatic protection activates instantly
4. **Visibility**: Critical logs alert operators immediately
5. **Easy Recovery**: Simple flag change to resume live trading

## Benefits

1. **Simpler Configuration**: Single flag instead of 5 conflicting variables
2. **Clearer Intent**: `DRY_RUN=1` clearly means "paper trading with live data"
3. **Automatic Account Protection**: TopStep compliance limits enforced automatically
4. **No Manual Monitoring**: Bot protects itself from violations
5. **Continuous Operation**: Switches to paper trading instead of shutting down
6. **Live Data Testing**: Paper mode uses real market data for accurate testing
7. **Safe Defaults**: Defaults to paper trading when unset
8. **Easy Recovery**: Simple flag change to resume live trading after compliance issue

## Safety Considerations

### Default Settings:
- Default: `DRY_RUN=1` (paper trading) - SAFE
- Unset: Defaults to `DRY_RUN=1` - SAFE
- Explicit `DRY_RUN=0` required for live trading - INTENTIONAL

### Kill Switch:
- Manual intervention required for shutdown
- Critical failures are logged with high visibility
- Allows investigation before forcing shutdown
- Prevents automatic shutdowns during normal operation

## Questions & Answers

**Q: How do I enable paper trading with live data?**  
A: Set `DRY_RUN=1` (default)

**Q: How do I enable live trading?**  
A: Set `DRY_RUN=0` and ensure proper authentication

**Q: Will the bot shut down automatically on bad performance?**  
A: No - you'll get CRITICAL log messages but must create kill.txt manually

**Q: What if I don't set DRY_RUN at all?**  
A: Defaults to `DRY_RUN=1` (paper trading) for safety

**Q: Can I still test with historical data?**  
A: Yes - use backtest mode separately from DRY_RUN setting
