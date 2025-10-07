# Position Management Quick Start Guide

## 🚀 30-Second Overview

New unified position management system provides:
- ✅ Automatic breakeven protection
- ✅ Trailing stops
- ✅ Time-based exits
- ✅ Max excursion tracking for ML/RL

**Status**: Core infrastructure complete, needs 3 integration points (see below).

## 📦 What's Included

```
src/BotCore/Models/
├── ExitReason.cs              → 11 exit types (Target, StopLoss, Breakeven, etc.)
├── PositionManagementState.cs → Position tracking with excursion data

src/BotCore/Services/
└── UnifiedPositionManagementService.cs → Core service (436 lines)

src/UnifiedOrchestrator/Services/
└── TradingOrchestratorService.cs → Enhanced exit logging
```

## 🔌 3-Step Integration

### Step 1: Register Service (DI Container)
```csharp
// In Program.cs or Startup.cs
services.AddSingleton<UnifiedPositionManagementService>();
services.AddHostedService<UnifiedPositionManagementService>();
```

### Step 2: Connect Market Data
```csharp
// In UnifiedPositionManagementService.cs, line ~332
private Task<decimal> GetCurrentMarketPriceAsync(string symbol, CancellationToken ct)
{
    // REPLACE THIS STUB:
    return Task.FromResult(0m);
    
    // WITH YOUR MARKET DATA:
    var marketData = _serviceProvider.GetService<IMarketDataService>();
    return marketData.GetLastPriceAsync(symbol, ct);
}
```

### Step 3: Wire Position Lifecycle
```csharp
// After trade execution (in order fill handler)
_positionMgmt.RegisterPosition(
    positionId: orderId,
    symbol: fillEvent.Symbol,
    strategy: "S6",
    entryPrice: fillEvent.Price,
    stopPrice: initialStop,
    targetPrice: target,
    quantity: fillEvent.Quantity,
    bracketMode: parameterBundle.BracketMode
);

// On trade exit (target hit, stop hit, manual close)
var (maxFav, maxAdv) = _positionMgmt.GetExcursionMetrics(positionId);
_positionMgmt.UnregisterPosition(positionId, ExitReason.Target);

// Use metrics in exit logging
result.MaxFavorableExcursion = maxFav;
result.MaxAdverseExcursion = maxAdv;
```

## ⚙️ How It Works

### Monitoring Loop (Every 5 Seconds)
```
1. Get all registered positions
2. Get current market price for each
3. Calculate profit in ticks
4. Check if time limit exceeded → Close position
5. Check if profit ≥ BreakevenAfterTicks → Move stop to entry + 1 tick
6. Check if trailing active → Update stop to follow price
7. Track max favorable/adverse excursion
```

### Configuration (From ParameterBundle)
```csharp
// Conservative mode
BreakevenAfterTicks = 6  // Move to BE after 6 ticks profit
TrailTicks = 4           // Trail 4 ticks behind price
MaxHoldMinutes = 60      // Close after 60 minutes

// Aggressive mode
BreakevenAfterTicks = 10
TrailTicks = 6
MaxHoldMinutes = 45
```

### Exit Logging Format
```
📊 [TRADE-EXIT] S6 ES LONG CLOSED | 
    Entry: 5000.00@09:30:15 | 
    Exit: 5012.00@09:45:22 | 
    Reason: Target | 
    MaxFav: +15 | 
    MaxAdv: -3 | 
    Duration: 15.1m | 
    PnL: $450.00 | 
    Success: True
```

## 🎯 Strategy-Specific Settings

| Strategy | Max Hold | Breakeven | Trail | Notes |
|----------|----------|-----------|-------|-------|
| S2 | 60m | 6-8 ticks | 3-6 ticks | From ParameterBundle |
| S3 | 90m | 8-10 ticks | 6-8 ticks | From ParameterBundle |
| S6 | 45m | 6-8 ticks | 4-6 ticks | Has built-in logic |
| S11 | 60m | 6-8 ticks | 4-6 ticks | Has built-in logic |

## 📊 Exit Reason Types

```csharp
public enum ExitReason
{
    Unknown,      // Default
    Target,       // Hit profit target
    StopLoss,     // Hit initial stop
    Breakeven,    // Stopped at entry after BE activation
    TrailingStop, // Trailing stop hit
    TimeLimit,    // Max hold time exceeded
    ZoneBreak,    // Support/resistance zone broken
    Emergency,    // Risk violation or system issue
    Manual,       // User closed position
    SessionEnd,   // End of session forced close
    Partial       // Partial exit/scale-out
}
```

## 🔍 Debugging

### Check if service is running
```bash
# Look for startup message
grep "POSITION-MGMT.*starting" logs/app.log

# Check for monitoring activity
grep "POSITION-MGMT" logs/app.log | tail -20
```

### Check if positions are registered
```bash
# Look for registration logs
grep "Registered position" logs/app.log

# Look for management actions
grep "Breakeven activated\|Trailing stop\|Time limit" logs/app.log
```

### Common Issues

**Service not monitoring positions**
→ Check market data connection returns > 0

**Stops not being modified**
→ Check order management integration in `ModifyStopPriceAsync()`

**Positions not registered**
→ Add registration call after order fill confirmation

## 📚 Full Documentation

- **Integration Guide**: `POSITION_MANAGEMENT_INTEGRATION_GUIDE.md`
- **Implementation Summary**: `POSITION_MANAGEMENT_IMPLEMENTATION_SUMMARY.md`

## 🎨 Example: Complete Integration

```csharp
public class OrderFillHandler
{
    private readonly UnifiedPositionManagementService _positionMgmt;
    private readonly ParameterBundleFactory _paramFactory;
    
    public async Task HandleFillAsync(OrderFillEvent fill)
    {
        // 1. Confirm fill with order service
        var orderId = fill.OrderId;
        
        // 2. Get bracket configuration
        var bundle = _paramFactory.GetBundleForStrategy(fill.Strategy);
        var bracket = bundle.BracketMode;
        
        // 3. Calculate stop and target
        var tickSize = 0.25m;
        var stopPrice = fill.IsLong 
            ? fill.Price - (bracket.StopTicks * tickSize)
            : fill.Price + (bracket.StopTicks * tickSize);
        var targetPrice = fill.IsLong
            ? fill.Price + (bracket.TargetTicks * tickSize)
            : fill.Price - (bracket.TargetTicks * tickSize);
        
        // 4. Register with position management
        _positionMgmt.RegisterPosition(
            positionId: orderId,
            symbol: fill.Symbol,
            strategy: fill.Strategy,
            entryPrice: fill.Price,
            stopPrice: stopPrice,
            targetPrice: targetPrice,
            quantity: fill.Quantity,
            bracketMode: bracket
        );
        
        // 5. Position is now monitored automatically
        // Service will handle breakeven, trailing, time exits
    }
    
    public async Task HandleExitAsync(string orderId, ExitReason reason)
    {
        // 1. Get excursion metrics before unregistering
        var (maxFav, maxAdv) = _positionMgmt.GetExcursionMetrics(orderId);
        
        // 2. Unregister position
        _positionMgmt.UnregisterPosition(orderId, reason);
        
        // 3. Log with metrics
        _logger.LogInformation(
            "Trade closed: {OrderId} MaxFav={MaxFav:F1} MaxAdv={MaxAdv:F1}",
            orderId, maxFav, maxAdv);
    }
}
```

## ✅ Checklist

- [ ] Service registered in DI container
- [ ] Market data connected (returns current price)
- [ ] Order management connected (can modify stops)
- [ ] Position registration wired (after fill)
- [ ] Position unregistration wired (on exit)
- [ ] Excursion metrics captured (for logging)
- [ ] Test with one position in simulation
- [ ] Verify breakeven activation
- [ ] Verify trailing stop updates
- [ ] Verify time-based exit
- [ ] Check logs for comprehensive exit data

## 🆘 Need Help?

1. Read `POSITION_MANAGEMENT_INTEGRATION_GUIDE.md` for detailed instructions
2. Check logs for `[POSITION-MGMT]` messages
3. Verify service is registered and started
4. Confirm market data returns valid prices
5. Test with stub data first before live integration

---

**Ready to integrate?** Start with Step 1 (DI registration) and work through the 3-step checklist above.
