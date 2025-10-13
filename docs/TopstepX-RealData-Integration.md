# TopstepX Real Market Data Integration Guide

## Overview

This document describes the integration of real TopstepX market data into the trading bot's RedundantDataFeedManager and TopstepXDataFeed classes.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     UnifiedOrchestrator                         │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │          RedundantDataFeedManager                          │ │
│  │                                                            │ │
│  │  ┌──────────────────────────┐  ┌──────────────────────┐  │ │
│  │  │  TopstepXDataFeed        │  │  BackupDataFeed      │  │ │
│  │  │  (Primary)               │  │  (Backup)            │  │ │
│  │  │                          │  │                      │  │ │
│  │  │  - Gets real prices      │  │  - Simulation data   │  │ │
│  │  │  - Calculates bid/ask    │  │  - Slower response   │  │ │
│  │  │  - Falls back on error   │  │                      │  │ │
│  │  └──────────┬───────────────┘  └──────────────────────┘  │ │
│  │             │                                             │ │
│  └─────────────┼─────────────────────────────────────────────┘ │
│                │                                               │
│  ┌─────────────▼──────────────────────────────────────────┐   │
│  │      ITopstepXAdapterService                           │   │
│  │      (TopstepXAdapterService)                          │   │
│  │                                                         │   │
│  │  - GetPriceAsync(symbol)                              │   │
│  │  - PlaceOrderAsync(...)                               │   │
│  │  - Manages Python SDK bridge                          │   │
│  └────────────────────┬────────────────────────────────────┘   │
│                       │                                        │
└───────────────────────┼────────────────────────────────────────┘
                        │
            ┌───────────▼────────────────┐
            │  Python SDK Bridge         │
            │  (topstep_x_adapter.py)    │
            │                            │
            │  - JSON command handler    │
            │  - TradingSuite management │
            │  - Real SDK integration    │
            └────────────┬───────────────┘
                         │
            ┌────────────▼──────────────┐
            │   TopstepX SDK            │
            │   (project-x-py)          │
            │                           │
            │  - Live market data       │
            │  - Order execution        │
            │  - WebSocket streams      │
            └───────────────────────────┘
```

## Changes Made

### 1. TopstepXDataFeed Class

**File:** `src/BotCore/Market/RedundantDataFeedManager.cs`

#### Constructor Enhancement
```csharp
public TopstepXDataFeed(
    TradingBot.Abstractions.ITopstepXAdapterService? adapterService = null,
    ILogger<TopstepXDataFeed>? logger = null)
{
    _adapterService = adapterService;
    _logger = logger;
    
    if (_adapterService != null)
    {
        _logger?.LogInformation("[TopstepXDataFeed] Initialized with real TopstepX adapter");
    }
    else
    {
        _logger?.LogWarning("[TopstepXDataFeed] No adapter service provided - using simulation mode");
    }
}
```

#### GetMarketDataAsync Enhancement
```csharp
public async Task<MarketData?> GetMarketDataAsync(string symbol)
{
    // Try to get real market data if adapter is available
    if (_adapterService != null)
    {
        try
        {
            if (_adapterService.IsConnected)
            {
                var price = await _adapterService.GetPriceAsync(symbol, CancellationToken.None);
                
                // Calculate bid/ask as price ± one tick (0.25 for ES/MNQ)
                var bid = price - ES_TICK_SIZE;
                var ask = price + ES_TICK_SIZE;
                
                return new MarketData
                {
                    Symbol = symbol,
                    Price = price,
                    Volume = DEFAULT_VOLUME,
                    Bid = bid,
                    Ask = ask,
                    Timestamp = DateTime.UtcNow,
                    Source = FeedName
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get real market data, falling back to simulation");
        }
    }
    
    // Fallback to simulated data
    // ... simulation code ...
}
```

### 2. RedundantDataFeedManager Class

**File:** `src/BotCore/Market/RedundantDataFeedManager.cs`

#### Constructor Enhancement
```csharp
public RedundantDataFeedManager(
    ILogger<RedundantDataFeedManager> logger,
    TradingBot.Abstractions.ITopstepXAdapterService? topstepXAdapter = null,
    ILoggerFactory? loggerFactory = null)
{
    _logger = logger;
    _topstepXAdapter = topstepXAdapter;
    _loggerFactory = loggerFactory;
    // ... initialization ...
}
```

#### Data Feed Initialization
```csharp
public async Task InitializeDataFeedsAsync()
{
    _logger.LogInformation("[DataFeed] Initializing data feeds");
    
    // Add data feeds with TopstepX adapter for real market data
    var topstepXLogger = _loggerFactory?.CreateLogger<TopstepXDataFeed>();
    AddDataFeed(new TopstepXDataFeed(_topstepXAdapter, topstepXLogger) { Priority = PRIMARY_FEED_PRIORITY });
    AddDataFeed(new BackupDataFeed { Priority = BACKUP_FEED_PRIORITY });
    
    // ... rest of initialization ...
}
```

## Usage Example

### Basic Usage (With Adapter)
```csharp
// In DI container setup (already configured in UnifiedOrchestrator)
services.AddSingleton<ITopstepXAdapterService, TopstepXAdapterService>();
services.AddSingleton<ILoggerFactory, LoggerFactory>();

// RedundantDataFeedManager will automatically use the adapter
var manager = serviceProvider.GetRequiredService<RedundantDataFeedManager>();
await manager.InitializeDataFeedsAsync();

// Get real market data
var marketData = await manager.GetMarketDataAsync("ES");
Console.WriteLine($"ES Price: ${marketData.Price:F2} (Bid: ${marketData.Bid:F2}, Ask: ${marketData.Ask:F2})");
Console.WriteLine($"Source: {marketData.Source}"); // "TopstepX" for real data, "TopstepX_Simulation" for fallback
```

### Standalone Usage (Without Adapter)
```csharp
// For testing or when adapter is not available
var logger = loggerFactory.CreateLogger<RedundantDataFeedManager>();
var manager = new RedundantDataFeedManager(logger, null, null);
await manager.InitializeDataFeedsAsync();

// Will use simulation mode
var marketData = await manager.GetMarketDataAsync("ES");
Console.WriteLine($"Source: {marketData.Source}"); // "TopstepX_Simulation"
```

## Data Flow

### Real Data Mode
1. **Request**: Application calls `GetMarketDataAsync("ES")`
2. **Check**: TopstepXDataFeed checks if `_adapterService.IsConnected`
3. **Fetch**: Calls `_adapterService.GetPriceAsync("ES")`
4. **Bridge**: TopstepXAdapterService executes Python command: `{"action":"get_price","symbol":"ES"}`
5. **SDK**: Python adapter calls TopstepX SDK to fetch real-time price
6. **Calculate**: C# code calculates bid = price - 0.25, ask = price + 0.25
7. **Return**: Returns MarketData with real price and calculated bid/ask

### Simulation Mode (Fallback)
1. **Trigger**: Adapter unavailable, not connected, or error occurs
2. **Log**: Warning logged about fallback to simulation
3. **Generate**: Random price variation around base price (4500 for ES)
4. **Return**: Returns MarketData with `Source = "TopstepX_Simulation"`

## Configuration

### Required Environment Variables
```bash
# TopstepX API credentials (for real data)
PROJECT_X_API_KEY=your_api_key
PROJECT_X_USERNAME=your_username
PROJECT_X_ACCOUNT_ID=your_account_id

# Adapter retry configuration (for production)
ADAPTER_MAX_RETRIES=3
ADAPTER_BASE_DELAY=1.0
ADAPTER_MAX_DELAY=10.0
ADAPTER_TIMEOUT=30.0
```

### Python SDK Installation
```bash
pip install 'project-x-py[all]'
```

## Error Handling

### Connection Failures
- **Symptom**: Adapter not connected
- **Behavior**: Falls back to simulation mode
- **Log**: `[TopstepXDataFeed] TopstepX adapter not connected, falling back to simulation`

### API Errors
- **Symptom**: Exception during GetPriceAsync
- **Behavior**: Falls back to simulation mode
- **Log**: `[TopstepXDataFeed] Failed to get real market data for {Symbol}, falling back to simulation`

### SDK Not Installed
- **Symptom**: Python SDK not available
- **Behavior**: Adapter initialization fails, uses simulation
- **Log**: `[TopstepXDataFeed] No adapter service provided - using simulation mode`

## Monitoring and Logging

### Connection Status Logs
```
[TopstepXDataFeed] Initialized with real TopstepX adapter
[TopstepXDataFeed] Real market data for ES: Price=$4500.25, Bid=$4500.00, Ask=$4500.50
```

### Fallback Logs
```
[TopstepXDataFeed] TopstepX adapter not connected, falling back to simulation
[TopstepXDataFeed] Failed to get real market data for ES, falling back to simulation
```

### Data Source Identification
Check the `Source` field in MarketData:
- `"TopstepX"` = Real market data from TopstepX SDK
- `"TopstepX_Simulation"` = Simulated data (fallback mode)

## DRY_RUN Mode Behavior

**Important**: DRY_RUN mode only affects order execution, NOT market data.

- ✅ Real market data flows regardless of DRY_RUN setting
- ✅ Market data used for decision-making and analysis
- ❌ Order execution blocked when DRY_RUN is enabled
- ❌ Orders simulated locally instead of sent to TopstepX

This separation ensures:
1. Strategies can be tested with real market conditions
2. Paper trading uses accurate live data
3. Risk-free testing of decision logic
4. Seamless transition to live trading (just disable DRY_RUN)

## Testing

### Unit Test Example
```csharp
[Fact]
public async Task TopstepXDataFeed_WithAdapter_ReturnsRealData()
{
    // Arrange
    var mockAdapter = new Mock<ITopstepXAdapterService>();
    mockAdapter.Setup(a => a.IsConnected).Returns(true);
    mockAdapter.Setup(a => a.GetPriceAsync("ES", It.IsAny<CancellationToken>()))
               .ReturnsAsync(4500.25m);
    
    var feed = new TopstepXDataFeed(mockAdapter.Object);
    
    // Act
    var data = await feed.GetMarketDataAsync("ES");
    
    // Assert
    Assert.Equal(4500.25m, data.Price);
    Assert.Equal(4500.00m, data.Bid);  // Price - 0.25
    Assert.Equal(4500.50m, data.Ask);  // Price + 0.25
    Assert.Equal("TopstepX", data.Source);
}
```

### Integration Test
```bash
# Test with real SDK (requires credentials)
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj

# Watch logs for:
# [TopstepXDataFeed] Initialized with real TopstepX adapter
# [TopstepXDataFeed] Real market data for ES: Price=$4500.25...
```

## Performance Characteristics

### Real Data Mode
- **Latency**: ~50-200ms (network + SDK processing)
- **Update Rate**: On-demand (every GetMarketDataAsync call)
- **Data Quality**: Real-time, accurate bid/ask calculated from last price

### Simulation Mode
- **Latency**: ~50ms (simulated network delay)
- **Update Rate**: Instant
- **Data Quality**: Random variations around base price

## Best Practices

1. **Always check Source field** to distinguish real vs simulated data
2. **Monitor adapter connection status** via logs
3. **Use graceful degradation** - simulation mode keeps bot functional
4. **Configure retry policies** appropriately for production
5. **Test with simulation first** before enabling real data
6. **Verify tick size** matches your instruments (0.25 for ES/MNQ)

## Future Enhancements

### Planned Improvements
1. **Level 1 Quotes**: Get actual bid/ask from market data (not calculated)
2. **Level 2 Order Book**: Full order book depth
3. **Streaming Updates**: WebSocket-based continuous data feed
4. **Volume Data**: Real volume from market data
5. **Multiple Tick Sizes**: Support for different instruments (NQ, RTY, etc.)

### Implementation Notes
- Bid/ask currently estimated as price ± one tick
- Volume uses constant DEFAULT_VOLUME (1000)
- Future: Add GetQuote method for real bid/ask/volume
- Future: Subscribe to market data streams instead of polling

## Troubleshooting

### Issue: Always getting simulation data
**Check:**
1. Is TopstepXAdapterService registered in DI?
2. Is adapter passed to RedundantDataFeedManager constructor?
3. Is Python SDK installed (`pip install 'project-x-py[all]'`)?
4. Are environment variables set correctly?
5. Check logs for adapter initialization errors

### Issue: Adapter not connecting
**Check:**
1. API credentials are valid
2. Network connectivity to TopstepX
3. Python environment has SDK installed
4. Retry policy configuration is correct
5. Check logs for specific error messages

### Issue: Prices seem incorrect
**Check:**
1. Source field - is it "TopstepX" (real) or "TopstepX_Simulation"?
2. Compare with live market prices
3. Verify symbol is supported ("ES", "MNQ", etc.)
4. Check for SDK errors in logs

## References

- **TopstepX SDK Docs**: https://docs.topstepx.com/
- **Python Adapter**: `src/adapters/topstep_x_adapter.py`
- **Adapter Service**: `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs`
- **Data Feed Manager**: `src/BotCore/Market/RedundantDataFeedManager.cs`
