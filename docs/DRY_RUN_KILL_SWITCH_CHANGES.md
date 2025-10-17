# DRY_RUN and Kill Switch Simplification

## Summary of Changes

This document describes the simplification of the bot's trading mode configuration and kill switch behavior.

## Previous Behavior (Legacy)

### Multiple Confusing Mode Variables:
- `DRY_RUN` (0/1)
- `PAPER_MODE` (0/1)
- `DEMO_MODE` (0/1)
- `TRADING_MODE` (LIVE/PAPER/DEMO)
- `ENABLE_DRY_RUN` (true/false)
- Multiple conflicting settings that made it unclear what mode the bot was in

### Automatic Kill Switch Activation:
- Kill.txt was created automatically by canary monitoring on catastrophic failures
- Kill switch would trigger on performance degradation
- Made debugging difficult as the bot would shut down automatically

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

### Manual Kill Switch Only:
- Kill.txt is **NOT** created automatically
- Kill switch is for **critical system failures only**
  - Example: Database connection loss
  - Example: API authentication failure
  - Example: Memory/resource exhaustion
  
- Kill switch is **NOT** for performance degradation
  - Catastrophic failures are logged with CRITICAL severity
  - Manual intervention is required to create kill.txt
  - Allows operators to investigate before forcing shutdown

## Configuration

### Environment Variables (Simplified)

```bash
# Trading Mode - SINGLE FLAG
DRY_RUN=1                    # 1=Paper trading, 0=Live trading

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
4. **`MasterDecisionOrchestrator.cs`** - Removed automatic kill.txt creation
5. **`EmergencyStopSystem.cs`** - Updated documentation
6. **`ConfigurationLocks.cs`** - Updated to check DRY_RUN instead of PAPER_MODE
7. **`SystemHealthMonitor.cs`** - Updated to check DRY_RUN instead of PAPER_MODE

### Key Behavior Changes:

1. **IsDryRunMode()** - Now checks only `DRY_RUN` environment variable
   - Returns `true` if `DRY_RUN=1` or unset (safe default)
   - Returns `false` if `DRY_RUN=0` or `DRY_RUN=false`

2. **Catastrophic Failure Handling** - No longer creates kill.txt automatically
   - Logs critical failure with severity CRITICAL
   - Requires manual intervention to create kill.txt
   - Allows investigation before forcing shutdown

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

### Verify Kill Switch:

```bash
# Kill switch should NOT be created automatically
# Monitor logs for CRITICAL failures instead

# Manual kill switch activation:
echo "Testing kill switch" > kill.txt
# Expected: Bot detects file and forces shutdown
```

## Benefits

1. **Simpler Configuration**: Single flag instead of 5 conflicting variables
2. **Clearer Intent**: `DRY_RUN=1` clearly means "paper trading with live data"
3. **Better Debugging**: Kill switch doesn't trigger automatically on performance issues
4. **Manual Control**: Operators decide when to force shutdown
5. **Live Data Testing**: Paper mode uses real market data for accurate testing

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
