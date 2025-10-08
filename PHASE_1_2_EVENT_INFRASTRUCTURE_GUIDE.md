# PHASE 1 & 2: Event Infrastructure and Order Fill Tracking - Integration Guide

## Overview

This guide explains how to use the newly implemented event infrastructure and metrics tracking for order execution.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     TopstepX Python SDK                          │
│                  (Real-time Trade Events)                        │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              TopstepXAdapterService (C#)                         │
│  - PollForFillEventsAsync (every 2 seconds)                     │
│  - ParseFillEvent (JSON → FillEventData)                        │
│  - FillEventReceived event                                      │
│  - Reconnection logic (5s retry on errors)                      │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼ FillEventReceived Event
┌─────────────────────────────────────────────────────────────────┐
│              OrderExecutionService                               │
│  - OnOrderFillReceived(FillEventData)                           │
│  - Updates: _orders, _positions ConcurrentDictionaries          │
│  - Records: Latency, Slippage, Fill Statistics                  │
│  - Fires: OrderFilled event for external subscribers            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              OrderExecutionMetrics                               │
│  - Latency tracking (order placed → filled)                     │
│  - Slippage tracking (expected vs actual price)                 │
│  - Fill statistics (orders, fills, rejections, partials)        │
│  - Rolling averages & percentile calculations                   │
└─────────────────────────────────────────────────────────────────┘
```

## Component Details

### 1. OrderExecutionService Events

**Events Available:**
```csharp
public event EventHandler<OrderFillEventArgs>? OrderFilled;
public event EventHandler<OrderPlacedEventArgs>? OrderPlaced;
public event EventHandler<OrderRejectedEventArgs>? OrderRejected;
```

**Event Data Classes:**
```csharp
public class OrderFillEventArgs : EventArgs
{
    public string OrderId { get; init; }
    public string Symbol { get; init; }
    public int Quantity { get; init; }
    public decimal FillPrice { get; init; }
    public decimal Commission { get; init; }
    public DateTime Timestamp { get; init; }
    public string Exchange { get; init; }
    public string LiquidityType { get; init; }
}

public class OrderPlacedEventArgs : EventArgs
{
    public string OrderId { get; init; }
    public string Symbol { get; init; }
    public int Quantity { get; init; }
    public decimal Price { get; init; }
    public string OrderType { get; init; }
    public DateTime Timestamp { get; init; }
}

public class OrderRejectedEventArgs : EventArgs
{
    public string OrderId { get; init; }
    public string Symbol { get; init; }
    public string Reason { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### 2. OrderExecutionMetrics API

**Recording Metrics:**
```csharp
// Latency tracking
metrics.RecordExecutionLatency(orderId, symbol, orderPlacedTime, fillTime);

// Slippage tracking
metrics.RecordSlippage(orderId, symbol, expectedPrice, actualFillPrice, quantity);

// Order placement
metrics.RecordOrderPlaced(symbol);

// Order fill
metrics.RecordOrderFilled(symbol, isPartialFill: false);

// Order rejection
metrics.RecordOrderRejected(symbol, reason);
```

**Querying Metrics:**
```csharp
// Get average latency
double avgLatency = metrics.GetAverageLatencyMs("ES");

// Get 95th percentile latency
double p95Latency = metrics.GetLatencyPercentile("ES", 95.0);

// Get average slippage
double avgSlippage = metrics.GetAverageSlippagePercent("ES");

// Get fill statistics
FillStatistics stats = metrics.GetFillStatistics("ES");
// stats.TotalOrders, stats.TotalFills, stats.PartialFills, stats.TotalRejections

// Get comprehensive summary
OrderExecutionMetricsSummary summary = metrics.GetMetricsSummary("ES");
```

### 3. TopstepXAdapterService Fill Events

**Subscribe to Fill Events:**
```csharp
topstepXAdapter.SubscribeToFillEvents(fillData => 
{
    logger.LogInformation("Fill received: {OrderId} {Symbol} {Qty} @ {Price}",
        fillData.OrderId, fillData.Symbol, fillData.Quantity, fillData.FillPrice);
});
```

**Or use the event directly:**
```csharp
topstepXAdapter.FillEventReceived += (sender, fillData) =>
{
    // Handle fill event
    ProcessFillEvent(fillData);
};
```

## Integration Example

### Step 1: Register Services in DI Container (Program.cs)

```csharp
// Register OrderExecutionMetrics
services.AddSingleton<OrderExecutionMetrics>();

// Register OrderExecutionService with metrics
services.AddSingleton<IOrderService>((sp) =>
{
    var logger = sp.GetRequiredService<ILogger<OrderExecutionService>>();
    var adapter = sp.GetRequiredService<ITopstepXAdapterService>();
    var metrics = sp.GetRequiredService<OrderExecutionMetrics>();
    
    return new OrderExecutionService(logger, adapter, metrics);
});

// TopstepXAdapterService is already registered
```

### Step 2: Wire Up Fill Event Subscription

```csharp
// In your application startup or orchestrator initialization
public class TradingSystemInitializer
{
    private readonly ITopstepXAdapterService _adapter;
    private readonly OrderExecutionService _orderService;
    private readonly ILogger<TradingSystemInitializer> _logger;
    
    public async Task InitializeAsync()
    {
        // Initialize adapter
        await _adapter.InitializeAsync();
        
        // Subscribe OrderExecutionService to fill events from adapter
        _adapter.SubscribeToFillEvents(fillData => 
        {
            _orderService.OnOrderFillReceived(fillData);
        });
        
        _logger.LogInformation("✅ Fill event subscription established");
        
        // Subscribe external components to OrderExecutionService events
        _orderService.OrderFilled += OnOrderFilled;
        _orderService.OrderPlaced += OnOrderPlaced;
        
        _logger.LogInformation("✅ Trading system initialized with event infrastructure");
    }
    
    private void OnOrderFilled(object? sender, OrderFillEventArgs e)
    {
        _logger.LogInformation("📊 Order filled: {OrderId} {Symbol} {Qty} @ {Price}",
            e.OrderId, e.Symbol, e.Quantity, e.FillPrice);
        
        // Additional processing: alerts, notifications, reporting, etc.
    }
    
    private void OnOrderPlaced(object? sender, OrderPlacedEventArgs e)
    {
        _logger.LogInformation("📈 Order placed: {OrderId} {Symbol} {Type}",
            e.OrderId, e.Symbol, e.OrderType);
    }
}
```

### Step 3: Query Metrics for Monitoring

```csharp
public class TradingDashboardService
{
    private readonly OrderExecutionService _orderService;
    
    public async Task<object> GetExecutionMetricsAsync(string symbol)
    {
        var summary = _orderService.GetMetricsSummary(symbol);
        
        if (summary == null)
        {
            return new { error = "No metrics available" };
        }
        
        return new
        {
            symbol = summary.Symbol,
            totalOrders = summary.TotalOrders,
            totalFills = summary.TotalFills,
            partialFills = summary.PartialFills,
            totalRejections = summary.TotalRejections,
            fillRate = $"{summary.FillRate:F2}%",
            averageLatencyMs = $"{summary.AverageLatencyMs:F2}ms",
            p95LatencyMs = $"{summary.Latency95thPercentileMs:F2}ms",
            averageSlippage = $"{summary.AverageSlippagePercent:F4}%",
            lastFillTime = summary.LastFillTime,
            lastRejectionReason = summary.LastRejectionReason
        };
    }
}
```

## Event Flow Example

### Scenario: ES Contract Order Filled

1. **Order Placement** (t=0ms)
   ```csharp
   var orderId = await orderService.PlaceMarketOrderAsync("ES", "BUY", 1);
   // → Fires OrderPlaced event
   // → Records metrics.RecordOrderPlaced("ES")
   ```

2. **Fill Notification from TopstepX** (t=150ms)
   - Python SDK receives fill from exchange
   - `get_fill_events()` returns fill data
   - TopstepXAdapterService polls and gets the fill
   
3. **Fill Event Processing** (t=152ms)
   ```csharp
   // TopstepXAdapterService.PollForFillEventsAsync
   var fillData = new FillEventData {
       OrderId = "ORD-1234",
       Symbol = "ES",
       Quantity = 1,
       FillPrice = 5000.25m,
       // ... other fields
   };
   
   // Fires FillEventReceived event
   FillEventReceived?.Invoke(this, fillData);
   ```

4. **OrderExecutionService Processing** (t=153ms)
   ```csharp
   // OrderExecutionService.OnOrderFillReceived(fillData)
   // → Updates _orders dictionary (status = Filled)
   // → Updates _positions dictionary
   // → Records latency: 153ms
   // → Records slippage: 0.25 ticks
   // → Records fill statistics
   // → Fires OrderFilled event
   ```

5. **External Subscribers Notified** (t=154ms)
   ```csharp
   // All subscribers to OrderFilled event receive notification
   // → Alerts, dashboards, logging, analytics, etc.
   ```

## Configuration

### Environment Variables for Python Adapter

The Python adapter requires retry policy configuration:

```bash
ADAPTER_MAX_RETRIES=3
ADAPTER_BASE_DELAY=1.0
ADAPTER_MAX_DELAY=10.0
ADAPTER_TIMEOUT=30.0
```

### Metrics Configuration

Metrics are configured with constants in `OrderExecutionMetrics.cs`:

```csharp
private const int MaxRecordsToKeep = 1000;        // Rolling window size
private const int StatisticsWindowMinutes = 60;   // Metrics window duration
```

## Monitoring and Observability

### Key Metrics to Monitor

1. **Execution Latency**
   - Average: < 200ms (target)
   - 95th Percentile: < 500ms (target)

2. **Slippage**
   - Average: < 0.1% (target)
   - Monitor spikes during volatile periods

3. **Fill Rate**
   - Target: > 95%
   - Low fill rate indicates market or connectivity issues

4. **Rejections**
   - Monitor rejection reasons
   - Set alerts for unusual rejection spikes

### Sample Logging Output

```
[INFO] 📈 [ORDER-EXEC] Placing market order: ES BUY 1 contracts (tag: none)
[INFO] ✅ [ORDER-EXEC] Order ORD-1234 created
[DEBUG] [METRICS] Order placed count updated: ES total=1
[INFO] 🎧 [FILL-LISTENER] Starting fill event listener...
[INFO] 📥 [FILL-LISTENER] Received fill: ORD-1234 ES 1 @ 5000.25
[INFO] 📥 [FILL-EVENT] Received fill notification: ORD-1234 ES 1 @ 5000.25
[INFO] ✅ [ORDER-UPDATE] Order ORD-1234 updated: 1/1 filled, status: Filled
[DEBUG] [METRICS] Execution latency recorded: ORD-1234 ES 153.45ms
[DEBUG] [METRICS] Slippage recorded: ORD-1234 ES expected=$5000.00 actual=$5000.25 slippage=0.0050%
[DEBUG] [METRICS] Fill count updated: ES fills=1 partial=0
```

## Troubleshooting

### Fill Events Not Received

1. Check adapter initialization:
   ```csharp
   var isConnected = await adapter.IsConnectedAsync();
   var health = await adapter.GetHealthScoreAsync();
   ```

2. Check fill listener status:
   ```csharp
   // Look for log: "🎧 [FILL-LISTENER] Starting fill event listener..."
   ```

3. Verify subscription:
   ```csharp
   // Ensure SubscribeToFillEvents was called after InitializeAsync
   ```

### High Latency

1. Check network connectivity
2. Review Python SDK performance
3. Consider reducing poll interval (currently 2 seconds)

### Metrics Not Recording

1. Verify OrderExecutionMetrics is injected:
   ```csharp
   var metrics = serviceProvider.GetService<OrderExecutionMetrics>();
   ```

2. Check if metrics is null in OrderExecutionService constructor

## Future Enhancements

### Phase 3: Real-time WebSocket Events (Planned)

Replace polling with WebSocket streaming:

```python
# topstep_x_adapter.py
async def subscribe_to_fill_stream(self):
    """Subscribe to real-time fill events via WebSocket"""
    async for fill in self.suite.subscribe_to_trades():
        yield {
            'order_id': fill.order_id,
            'symbol': fill.symbol,
            # ... fill data
        }
```

### Phase 4: Advanced Metrics (Planned)

- Order book impact analysis
- Time-weighted average price (TWAP) tracking
- Multi-leg order correlation
- Commission optimization tracking

### Phase 5: Alerting (Planned)

- High latency alerts (> 500ms)
- High slippage alerts (> 0.2%)
- Fill rate degradation alerts (< 90%)
- Reconnection failure alerts

## Testing

### Unit Testing Example

```csharp
[Test]
public void OrderExecutionMetrics_RecordsLatency_Successfully()
{
    // Arrange
    var logger = new Mock<ILogger<OrderExecutionMetrics>>();
    var metrics = new OrderExecutionMetrics(logger.Object);
    var orderPlacedTime = DateTime.UtcNow;
    var fillTime = orderPlacedTime.AddMilliseconds(150);
    
    // Act
    metrics.RecordExecutionLatency("ORD-123", "ES", orderPlacedTime, fillTime);
    
    // Assert
    var avgLatency = metrics.GetAverageLatencyMs("ES");
    Assert.That(avgLatency, Is.EqualTo(150.0).Within(0.1));
}
```

### Integration Testing Example

```csharp
[Test]
public async Task FillEventFlow_EndToEnd_WorksCorrectly()
{
    // Arrange
    var adapter = CreateMockAdapter();
    var metrics = CreateMetrics();
    var orderService = new OrderExecutionService(logger, adapter, metrics);
    
    bool eventFired = false;
    orderService.OrderFilled += (s, e) => eventFired = true;
    
    // Act
    var fillData = new FillEventData { /* ... */ };
    adapter.SimulateFillEvent(fillData);
    
    // Assert
    await Task.Delay(100); // Allow event processing
    Assert.IsTrue(eventFired);
    var summary = metrics.GetMetricsSummary("ES");
    Assert.That(summary.TotalFills, Is.EqualTo(1));
}
```

## Conclusion

The PHASE 1 & 2 implementation provides a robust foundation for:

✅ Real-time fill event notifications  
✅ Comprehensive execution metrics tracking  
✅ Event-driven architecture for extensibility  
✅ Production-ready error handling and reconnection  
✅ Minimal performance overhead with concurrent collections  

This infrastructure enables advanced features like automated trading strategies, performance analytics, and real-time monitoring dashboards.
