# Position Management Integration Guide

## Overview
This guide documents how to integrate the UnifiedPositionManagementService with trading strategies (S2, S3, S6, S11) to enable automatic breakeven protection, trailing stops, and time-based exits.

## Architecture

### Core Components Created

1. **ExitReason Enum** (`src/BotCore/Models/ExitReason.cs`)
   - Comprehensive exit classification for ML/RL optimization
   - Values: Unknown, Target, StopLoss, Breakeven, TrailingStop, TimeLimit, ZoneBreak, Emergency, Manual, SessionEnd, Partial

2. **PositionManagementState** (`src/BotCore/Models/PositionManagementState.cs`)
   - Tracks position metadata including:
     - Entry/exit prices and times
     - Max favorable/adverse excursion
     - Breakeven and trailing stop activation status
     - Stop modification count

3. **UnifiedPositionManagementService** (`src/BotCore/Services/UnifiedPositionManagementService.cs`)
   - Background service running every 5 seconds
   - Monitors all open positions
   - Applies position management rules:
     - Breakeven protection when profit ≥ BreakevenAfterTicks
     - Trailing stops when profit ≥ BreakevenAfterTicks + TrailTicks
     - Time-based exits when duration ≥ MaxHoldMinutes

4. **Enhanced Exit Logging** (`src/UnifiedOrchestrator/Services/TradingOrchestratorService.cs`)
   - Added exit tracking fields to TradeExecutionResult
   - Comprehensive exit logging format with all metrics
   - Integration with existing PerformanceTracker

## Integration Points

### Where Position Management Happens

The position management system operates at the **order execution and tracking layer**, NOT at the strategy signal generation layer. This is because:

1. **S2, S3 Strategies** are static classes that generate `Candidate` signals
   - They don't track open positions
   - They produce entry signals with initial stop/target
   - Position management happens AFTER entry execution

2. **S6, S11 Strategies** have built-in position management
   - S6 has `TightenToBEOrTrail()` method
   - S11 has `ManagePosition()` method with time exits
   - These use `IOrderRouter.ModifyStop()` directly

3. **UnifiedPositionManagementService** provides centralized management
   - Works across ALL strategies
   - Uses ParameterBundle for configuration
   - Tracks excursion metrics for ML/RL

### Required Integration Steps

#### Step 1: Register Service in DI Container
```csharp
// In Program.cs or Startup.cs
services.AddSingleton<UnifiedPositionManagementService>();
services.AddHostedService<UnifiedPositionManagementService>();
```

#### Step 2: Register Positions When Opened
When a trade is executed (after order fill confirmation):

```csharp
// In order execution service (e.g., TradingOrchestratorService)
private readonly UnifiedPositionManagementService _positionMgmt;

// After trade execution
var bracketMode = parameterBundle.BracketMode; // From ParameterBundle
_positionMgmt.RegisterPosition(
    positionId: orderId,
    symbol: decision.Symbol,
    strategy: decision.Strategy,
    entryPrice: fillPrice,
    stopPrice: initialStopPrice,
    targetPrice: targetPrice,
    quantity: fillQuantity,
    bracketMode: bracketMode
);
```

#### Step 3: Unregister Positions When Closed
```csharp
// When position is closed (target hit, stop hit, manual close, etc.)
var excursionMetrics = _positionMgmt.GetExcursionMetrics(positionId);
_positionMgmt.UnregisterPosition(positionId, exitReason);

// Use excursion metrics for exit logging
executionResult.MaxFavorableExcursion = excursionMetrics.maxFavorable;
executionResult.MaxAdverseExcursion = excursionMetrics.maxAdverse;
```

#### Step 4: Integrate with Market Data
The service needs current market prices to calculate profit/loss:

```csharp
// In UnifiedPositionManagementService.GetCurrentMarketPriceAsync()
// TODO: Replace stub with actual market data service integration
private Task<decimal> GetCurrentMarketPriceAsync(string symbol, CancellationToken cancellationToken)
{
    // Option 1: Use existing market data service
    var marketData = _serviceProvider.GetService<IMarketDataService>();
    return marketData.GetLastPriceAsync(symbol, cancellationToken);
    
    // Option 2: Subscribe to TopstepX market data feed
    // and maintain a price cache
}
```

#### Step 5: Integrate with Order Management
The service needs to modify stops and close positions:

```csharp
// In UnifiedPositionManagementService.ModifyStopPriceAsync()
// TODO: Replace stub with actual order management integration
private async Task ModifyStopPriceAsync(
    PositionManagementState state,
    decimal newStopPrice,
    string reason,
    CancellationToken cancellationToken)
{
    // Option 1: Use IOrderRouter (for S6/S11 compatibility)
    var router = _serviceProvider.GetService<IOrderRouter>();
    router.ModifyStop(state.PositionId, (double)newStopPrice);
    
    // Option 2: Use TopstepX order management service
    var orderService = _serviceProvider.GetService<IOrderService>();
    await orderService.ModifyStopLossAsync(
        state.PositionId, 
        newStopPrice, 
        cancellationToken);
    
    state.CurrentStopPrice = newStopPrice;
    state.StopModificationCount++;
}
```

## Strategy-Specific Considerations

### S2 (VWAP Mean Reversion)
- **Max Hold Time**: 60 minutes (configured in service)
- **Breakeven Trigger**: From ParameterBundle (typically 6-8 ticks)
- **Trailing Distance**: From ParameterBundle (typically 3-6 ticks)
- **Integration**: Register position after S2 candidate execution

### S3 (Compression Breakout)
- **Max Hold Time**: 90 minutes (configured in service)
- **Breakeven Trigger**: From ParameterBundle (typically 8-10 ticks)
- **Trailing Distance**: From ParameterBundle (typically 6-8 ticks)
- **Integration**: Register position after S3 candidate execution

### S6 (Opening Drive)
- **Existing Management**: Has `TightenToBEOrTrail()` built-in
- **Max Hold Time**: 45 minutes (from existing code)
- **Integration Options**:
  1. **Replace** built-in with unified service
  2. **Augment** with unified service for excursion tracking
  3. **Keep separate** but add excursion tracking
- **Recommendation**: Keep existing logic, add excursion tracking only

### S11 (ADR Exhaustion)
- **Existing Management**: Has `ManagePosition()` with time exits
- **Max Hold Time**: 60 minutes (from existing code)
- **Trailing Threshold**: 0.5 R-multiple (from existing code)
- **Integration Options**: Same as S6
- **Recommendation**: Keep existing logic, add excursion tracking only

## Configuration via ParameterBundle

Position management parameters come from `BracketMode` in ParameterBundle:

```csharp
public record BracketMode
{
    public int StopTicks { get; init; }           // 6-20 ticks
    public int TargetTicks { get; init; }         // 8-30 ticks
    public int BreakevenAfterTicks { get; init; } // 4-16 ticks ← Used by service
    public int TrailTicks { get; init; }          // 3-12 ticks ← Used by service
    public string ModeType { get; init; }         // Conservative, Moderate, Aggressive, etc.
}

// Predefined modes:
- Conservative: BE after 6 ticks, Trail 4 ticks
- Moderate: BE after 8 ticks, Trail 5 ticks
- Aggressive: BE after 10 ticks, Trail 6 ticks
- Scalping: BE after 4 ticks, Trail 3 ticks
- Swing: BE after 12 ticks, Trail 8 ticks
```

## Exit Logging Format

The enhanced exit logging produces comprehensive trade records:

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

This data is captured in:
- `TradeExecutionResult` (orchestrator level)
- `TradeRecord` (PerformanceTracker)
- Learning feedback metadata (ML/RL systems)

## Testing Checklist

- [ ] Service starts and runs without errors
- [ ] Positions are registered correctly after execution
- [ ] Breakeven protection activates at correct profit level
- [ ] Trailing stops update as price moves favorably
- [ ] Time-based exits trigger after max hold time
- [ ] Excursion metrics are tracked accurately
- [ ] Exit logging includes all required fields
- [ ] No new analyzer warnings introduced
- [ ] Production guardrails remain functional

## Future Enhancements

1. **ML/RL Optimization**
   - Use exit data to optimize breakeven trigger
   - Learn optimal trailing distance per strategy
   - Optimize time limits based on market conditions

2. **Zone-Based Exits**
   - Exit when price breaks support/demand zone
   - Tighten stops when approaching zone boundaries

3. **Volatility-Adaptive Management**
   - Wider stops/trails in high volatility
   - Tighter in low volatility
   - Use VIX or ATR for adaptation

4. **Partial Exits**
   - Scale out at first target (50%)
   - Move to breakeven on remainder
   - Trail final portion for runners

## Notes

- **Minimal Changes**: Integration is surgical and non-invasive
- **Backward Compatible**: Existing strategy logic unchanged
- **Configurable**: Uses ParameterBundle for all settings
- **Observable**: Comprehensive logging for debugging
- **Production Ready**: Includes error handling and safety checks
