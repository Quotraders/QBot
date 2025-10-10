# Position Monitoring Implementation Summary

## Overview
Complete implementation of three advanced position monitoring features for ES/NQ portfolio heat management:
1. **IRealTimePositionMonitor** - Live session exposure tracking
2. **ISessionExposureCalculator** - Risk-adjusted exposure calculation
3. **IPositionTimeTracker** - Position lifecycle history

## Features Implemented

### Feature 1: IRealTimePositionMonitor
**Purpose:** Real-time position monitoring with session-based exposure tracking

**Implementation:**
- Location: `src/BotCore/Services/PositionMonitoring/RealTimePositionMonitor.cs`
- Tracks position entry timestamps and session attribution
- Calculates live exposure per trading session
- Applies time-decay weighting:
  - Fresh (< 1 hour): 1.0x weight
  - Aging (1-4 hours): 0.8x weight
  - Old (4-8 hours): 0.5x weight
  - Stale (> 8 hours): 0.3x weight

**Key Methods:**
- `GetSessionExposureAsync(session, positions)` - Get exposure for specific session
- `GetAllSessionExposuresAsync(positions)` - Get all session exposures
- `SubscribeToExposureUpdates(callback)` - Real-time updates

### Feature 2: ISessionExposureCalculator
**Purpose:** Risk-adjusted exposure calculation with volatility, correlation, and liquidity factors

**Implementation:**
- Location: `src/BotCore/Services/PositionMonitoring/SessionExposureCalculator.cs`
- Session-specific volatility multipliers:
  - Asian: 0.6x (lower volatility)
  - European: 0.85x (moderate)
  - USMorning: 1.2x (highest)
  - USAfternoon: 1.0x (normal)
  - Evening: 0.7x (reduced)
- Liquidity scoring (0.5-1.0 scale)
- ES/NQ correlation adjustments per session

**Key Methods:**
- `CalculateSessionExposureAsync(positions, session)` - Risk-adjusted exposure
- `GetVolatilityMultiplier(session)` - Session volatility factor
- `GetCorrelationAdjustment(positions, session)` - Correlation risk factor
- `GetLiquidityDiscount(session)` - Liquidity adjustment

### Feature 3: IPositionTimeTracker
**Purpose:** Complete position lifecycle tracking across trading sessions

**Implementation:**
- Location: `src/BotCore/Services/PositionMonitoring/PositionTimeTracker.cs`
- Tracks full position lifecycle from entry to exit
- Session attribution (origin session + active sessions)
- Holding period risk multipliers:
  - Intraday: 1.0x
  - Swing (1-3 days): 1.15x
  - Position (3-7 days): 1.3x
  - Extended (> 7 days): 1.4x

**Key Methods:**
- `GetSessionTimeExposureAsync(positions, session)` - Native + inherited exposure
- `GetPositionLifecycleAsync(positionId)` - Full lifecycle history
- `GetSessionAttributionAsync(positionId)` - Session list
- `GetIntraDayPositionsAsync()` - Same-day positions
- `GetOvernightPositionsAsync()` - Held overnight

## Supporting Services

### SessionDetectionService
**Purpose:** UTC time to trading session mapping

**Sessions Defined:**
- Asian: 00:00-08:00 UTC
- European: 08:00-13:00 UTC
- USMorning: 13:00-18:00 UTC
- USAfternoon: 18:00-21:00 UTC
- Evening: 21:00-00:00 UTC

## Integration with ES_NQ_PortfolioHeatManager

**Updated Methods:**
1. `TryGetRealTimeSessionExposureAsync()` - Now uses IRealTimePositionMonitor
2. `TryGetAlgorithmicSessionExposureAsync()` - Now uses ISessionExposureCalculator
3. `TryGetTimeTrackingExposureAsync()` - Now uses IPositionTimeTracker

**Constructor Updated:**
```csharp
public ES_NQ_PortfolioHeatManager(
    ILogger<ES_NQ_PortfolioHeatManager> logger, 
    TopstepX.Bot.Core.Services.PositionTrackingSystem? positionTracker = null,
    IRealTimePositionMonitor? realTimeMonitor = null,
    ISessionExposureCalculator? sessionCalculator = null,
    IPositionTimeTracker? timeTracker = null)
```

## Dependency Injection Registration

**Extension Method:**
```csharp
services.AddPositionMonitoringServices();
```

**Location:** `src/BotCore/Extensions/PositionMonitoringServiceExtensions.cs`

**Registers:**
- IRealTimePositionMonitor (Singleton)
- ISessionExposureCalculator (Singleton)
- IPositionTimeTracker (Singleton)
- SessionDetectionService (Singleton)

## Usage Example

```csharp
// In Startup/Program.cs
services.AddPositionMonitoringServices();

// In a service
public class MyTradingService
{
    private readonly IRealTimePositionMonitor _monitor;
    
    public MyTradingService(IRealTimePositionMonitor monitor)
    {
        _monitor = monitor;
    }
    
    public async Task CheckExposureAsync()
    {
        var positions = await GetCurrentPositionsAsync();
        
        // Get real-time session exposure
        var exposure = await _monitor.GetSessionExposureAsync("USMorning", positions);
        
        // Get all session exposures
        var allExposures = await _monitor.GetAllSessionExposuresAsync(positions);
    }
}
```

## Testing

**Test File:** `tests/Unit/PositionMonitoringTests.cs`

**Tests Included:**
1. Session detection accuracy (5 time zones)
2. Real-time exposure calculation
3. Volatility multiplier verification
4. Risk-adjusted exposure calculation
5. Position lifecycle tracking
6. Empty position handling

## Compliance

✅ **Zero New Warnings:** No analyzer warnings introduced  
✅ **Minimal Changes:** Surgical implementation following existing patterns  
✅ **Production Safe:** All safety guardrails preserved  
✅ **DRY_RUN Compatible:** Works in both simulation and live modes  
✅ **Async/Await:** Proper async patterns with ConfigureAwait(false)  
✅ **Decimal Precision:** Uses decimal for monetary values  
✅ **Existing Patterns:** Follows BotCore service conventions  

## Files Created/Modified

**New Files:**
- `src/BotCore/Services/PositionMonitoring/IRealTimePositionMonitor.cs`
- `src/BotCore/Services/PositionMonitoring/RealTimePositionMonitor.cs`
- `src/BotCore/Services/PositionMonitoring/ISessionExposureCalculator.cs`
- `src/BotCore/Services/PositionMonitoring/SessionExposureCalculator.cs`
- `src/BotCore/Services/PositionMonitoring/IPositionTimeTracker.cs`
- `src/BotCore/Services/PositionMonitoring/PositionTimeTracker.cs`
- `src/BotCore/Services/PositionMonitoring/SessionDetectionService.cs`
- `src/BotCore/Extensions/PositionMonitoringServiceExtensions.cs`
- `tests/Unit/PositionMonitoringTests.cs`

**Modified Files:**
- `src/BotCore/Services/ES_NQ_PortfolioHeatManager.cs` (wiring only)

## Next Steps

To use these features in production:

1. Register services in your startup:
   ```csharp
   services.AddPositionMonitoringServices();
   ```

2. The ES_NQ_PortfolioHeatManager will automatically use them when calculating heat

3. Or inject directly into your services for custom usage

4. Services work with the existing Position model from TradingBot.Abstractions

## Architecture Notes

- **Stateful Tracking:** Services maintain internal state for position history
- **Session Boundaries:** Automatically detects session transitions
- **Thread-Safe:** Uses ConcurrentDictionary for position tracking
- **Fallback Handling:** Returns null/0 when services not available
- **Memory Efficient:** Uses minimal storage for position metadata
