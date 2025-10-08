# Advanced Order Types Implementation Guide

## Overview

This guide documents the implementation of advanced order types in the Trading Bot, including OCO (One-Cancels-Other), Bracket, and Iceberg orders. These features were implemented as part of Phase 5-7 of the order execution enhancement project.

## Architecture

### Design Principles

1. **Minimal Changes**: Advanced order types are built on top of existing basic order methods (PlaceMarketOrderAsync, PlaceLimitOrderAsync, PlaceStopOrderAsync)
2. **Production Safety**: All features are disabled by default and must be explicitly enabled via configuration flags
3. **Event-Driven**: Uses existing fill event infrastructure to detect order fills and trigger subsequent actions
4. **No Breaking Changes**: Advanced order types are implemented in OrderExecutionService without modifying IOrderService interface (methods are commented out in interface)

### Components

- **OrderExecutionService.cs**: Contains all advanced order type logic and tracking
- **OrderExecutionMetrics.cs**: Existing metrics service tracks execution performance
- **TopstepXAdapterService.cs**: Existing fill event infrastructure supports advanced order types
- **.env configuration**: Feature flags to enable/disable advanced order types

## OCO (One-Cancels-Other) Orders

### What It Does

Places two orders simultaneously where filling one automatically cancels the other. Commonly used for:
- Entry at support OR resistance (whichever hits first)
- Stop-loss OR take-profit (bracket legs)

### Implementation

```csharp
public async Task<(string OcoId, string OrderId1, string OrderId2)> PlaceOcoOrderAsync(
    string symbol,
    string side1, int quantity1, decimal price1, string orderType1,
    string side2, int quantity2, decimal price2, string orderType2,
    CancellationToken cancellationToken = default)
```

### Data Structures

```csharp
internal sealed class OcoOrderPair
{
    public string OcoId { get; set; }
    public string OrderId1 { get; set; }
    public string OrderId2 { get; set; }
    public string Symbol { get; set; }
    public DateTime CreatedAt { get; set; }
    public OcoStatus Status { get; set; }
    public string? FilledOrderId { get; set; }
    public string? CancelledOrderId { get; set; }
}

internal enum OcoStatus { Active, OneFilled, BothCancelled, Expired }
```

### Usage Example

```csharp
// Place limit entry at support and resistance levels
var (ocoId, order1, order2) = await orderService.PlaceOcoOrderAsync(
    symbol: "ES",
    side1: "BUY", quantity1: 1, price1: 5000.00m, orderType1: "LIMIT",
    side2: "BUY", quantity2: 1, price2: 5020.00m, orderType2: "LIMIT"
);

// Whichever price hits first gets filled, the other is automatically cancelled
```

### How It Works

1. Places two orders using basic order methods
2. Tracks both orders in `_ocoOrders` dictionary
3. When fill event received for either order, cancels the other order automatically
4. Updates OCO status to `OneFilled`

## Bracket Orders

### What It Does

Places a single order that includes entry, stop-loss, and take-profit automatically. When entry fills:
1. Stop-loss order is placed
2. Take-profit order is placed
3. Stop and target are linked as OCO pair

### Implementation

```csharp
public async Task<(string BracketId, string EntryOrderId)> PlaceBracketOrderAsync(
    string symbol,
    string side,
    int quantity,
    decimal entryPrice,
    decimal stopPrice,
    decimal targetPrice,
    string entryOrderType = "LIMIT",
    CancellationToken cancellationToken = default)
```

### Data Structures

```csharp
internal sealed class BracketOrderGroup
{
    public string BracketId { get; set; }
    public string Symbol { get; set; }
    public string? EntryOrderId { get; set; }
    public string? StopOrderId { get; set; }
    public string? TargetOrderId { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public int Quantity { get; set; }
    public string Side { get; set; }
    public DateTime CreatedAt { get; set; }
    public BracketStatus Status { get; set; }
}

internal enum BracketStatus { Pending, EntryFilled, StopFilled, TargetFilled, Cancelled, Error }
```

### Usage Example

```csharp
// Place bracket order: entry at 5000, stop at 4990, target at 5020
var (bracketId, entryOrderId) = await orderService.PlaceBracketOrderAsync(
    symbol: "ES",
    side: "BUY",
    quantity: 1,
    entryPrice: 5000.00m,
    stopPrice: 4990.00m,
    targetPrice: 5020.00m,
    entryOrderType: "LIMIT"
);

// When entry fills at 5000:
// - Stop order placed at 4990
// - Target order placed at 5020
// - Stop and target linked as OCO (filling one cancels the other)
```

### Validation

Bracket orders include price validation to ensure:
- **Long positions**: Stop < Entry < Target
- **Short positions**: Target < Entry < Stop

Invalid brackets are rejected with error logging.

### How It Works

1. Places entry order (market or limit)
2. Tracks entry order in `_bracketOrders` dictionary with status `Pending`
3. When entry fill event received:
   - Determines exit side (opposite of entry)
   - Places stop and target as OCO pair
   - Updates bracket status to `EntryFilled`
4. When stop or target fills, other is cancelled automatically via OCO mechanism

## Iceberg Orders

### What It Does

Executes large orders in smaller hidden chunks to avoid showing full size to market. Example:
- Want to trade 10 contracts
- Display only 2 contracts at a time
- As each chunk fills, next chunk is placed
- Market only sees 2 contracts, not full 10

### Implementation

```csharp
public async Task<string> PlaceIcebergOrderAsync(
    string symbol,
    string side,
    int totalQuantity,
    int displayQuantity,
    decimal? limitPrice = null,
    CancellationToken cancellationToken = default)
```

### Data Structures

```csharp
internal sealed class IcebergOrderExecution
{
    public string IcebergId { get; set; }
    public string Symbol { get; set; }
    public string Side { get; set; }
    public int TotalQuantity { get; set; }
    public int DisplayQuantity { get; set; }
    public int FilledQuantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public IcebergStatus Status { get; set; }
    public List<string> ChildOrderIds { get; }
    public decimal? InitialMarketPrice { get; set; }
    public decimal MaxSlippageTicks { get; set; } = 2.0m;
}

internal enum IcebergStatus { Active, Completed, Cancelled, Error }
```

### Usage Example

```csharp
// Execute 10 contracts showing only 2 at a time at limit price 5000
var icebergId = await orderService.PlaceIcebergOrderAsync(
    symbol: "ES",
    side: "BUY",
    totalQuantity: 10,
    displayQuantity: 2,
    limitPrice: 5000.00m
);

// Places 2 contracts immediately
// When those 2 fill, places next 2
// Repeats until all 10 filled or error
```

### How It Works

1. Validates display quantity < total quantity
2. Places first chunk (display quantity)
3. Tracks order in `_icebergOrders` dictionary
4. When chunk fill event received:
   - Updates filled quantity
   - Calculates remaining quantity
   - If remaining > 0, places next chunk
   - If all filled, marks status as `Completed`

### Price Protection

Iceberg orders include `MaxSlippageTicks` parameter (default 2.0) for future enhancement to cancel remaining chunks if market moves adversely.

## Fill Event Processing

All advanced order types leverage the existing fill event infrastructure:

```csharp
private async Task ProcessAdvancedOrderFillAsync(string orderId, FillEventData fillData)
{
    // Check if order is part of OCO pair
    // Check if order is bracket entry
    // Check if order is iceberg chunk
    // Take appropriate action
}
```

This method is called automatically from `OnOrderFillReceived` whenever a fill event is received from TopstepX SDK.

## Configuration

### Environment Variables (.env)

```bash
# Advanced Order Types (Phase 5-7)
ENABLE_OCO_ORDERS=false
ENABLE_BRACKET_ORDERS=false
ENABLE_ICEBERG_ORDERS=false
ENABLE_FILL_TRACKING=true
ENABLE_POSITION_RECONCILIATION=true
RECONCILIATION_INTERVAL_SECONDS=60
EXECUTION_METRICS_ENABLED=true
SLIPPAGE_ALERT_THRESHOLD_TICKS=2
```

### Feature Flags

All advanced order types are **disabled by default** for production safety. Enable them individually after testing:

1. **ENABLE_OCO_ORDERS**: Enable OCO order placement
2. **ENABLE_BRACKET_ORDERS**: Enable bracket order placement
3. **ENABLE_ICEBERG_ORDERS**: Enable iceberg order placement
4. **ENABLE_FILL_TRACKING**: Enable fill event processing (required for all advanced types)
5. **ENABLE_POSITION_RECONCILIATION**: Enable position reconciliation timer

## Integration with UnifiedPositionManagementService

The UnifiedPositionManagementService already uses IOrderService for order placement. To use advanced order types:

```csharp
// Cast to concrete type to access advanced methods
if (orderService is OrderExecutionService advancedOrderService)
{
    // Use bracket orders instead of manual entry + stop modification
    var (bracketId, entryOrderId) = await advancedOrderService.PlaceBracketOrderAsync(
        symbol, side, quantity, entryPrice, stopPrice, targetPrice);
}
else
{
    // Fall back to basic order placement
    var orderId = await orderService.PlaceMarketOrderAsync(symbol, side, quantity);
    // Later modify stop/target manually
}
```

## Production Safety Considerations

### Guardrails Maintained

All production safety guardrails remain functional:
- ✅ DRY_RUN mode enforcement
- ✅ kill.txt monitoring
- ✅ Order evidence requirements (orderId + fill event)
- ✅ Tick rounding (ES/MES 0.25 ticks)
- ✅ Risk validation (risk > 0)
- ✅ No suppressions or config modifications

### Testing Checklist

Before enabling in production:

1. ✅ Build passes with no new warnings: `./dev-helper.sh build`
2. ✅ Analyzer check passes: `./dev-helper.sh analyzer-check`
3. ✅ Tests pass: `./dev-helper.sh test`
4. ✅ Risk validation passes: `./dev-helper.sh riskcheck`
5. ✅ Core guardrails verified: `./verify-core-guardrails.sh`
6. Test OCO orders with small quantities in paper trading
7. Test bracket orders with small quantities in paper trading
8. Test iceberg orders with small quantities in paper trading
9. Monitor fill events and order cancellations
10. Verify metrics tracking (latency, slippage)

## Metrics and Monitoring

All advanced order types integrate with existing OrderExecutionMetrics:

- **Latency tracking**: Time from order placement to fill
- **Slippage tracking**: Difference between expected and actual fill price
- **Fill statistics**: Total orders, fills, partial fills, rejections
- **Execution quality**: Average latency, 95th percentile, average slippage

Query metrics:

```csharp
var summary = metrics.GetMetricsSummary("ES");
Console.WriteLine($"Fill rate: {summary.FillRate:F2}%");
Console.WriteLine($"Avg latency: {summary.AverageLatencyMs:F2}ms");
Console.WriteLine($"Avg slippage: {summary.AverageSlippagePercent:F4}%");
```

## Troubleshooting

### OCO Orders Not Cancelling

**Symptom**: Both orders in OCO pair remain active after one fills

**Causes**:
1. Fill events not being received
2. Order ID mismatch in tracking
3. CancelOrderAsync failing

**Debug**:
```bash
# Check fill event logs
grep "FILL-EVENT" logs/trading-bot.log

# Check OCO tracking
grep "OCO.*filled" logs/trading-bot.log
```

### Bracket Orders Not Placing Stop/Target

**Symptom**: Entry fills but stop and target not placed

**Causes**:
1. Entry fill event not detected
2. Bracket tracking record not found
3. OCO placement failed

**Debug**:
```bash
# Check bracket logs
grep "BRACKET" logs/trading-bot.log

# Check fill processing
grep "ProcessAdvancedOrderFillAsync" logs/trading-bot.log
```

### Iceberg Orders Stopping Early

**Symptom**: Iceberg completes with fewer contracts than requested

**Causes**:
1. Chunk placement failed
2. Price moved adversely (future: slippage protection)
3. Error in fill tracking

**Debug**:
```bash
# Check iceberg logs
grep "ICEBERG" logs/trading-bot.log

# Check filled quantity
grep "ICEBERG.*filled" logs/trading-bot.log
```

## Future Enhancements

Potential improvements for future development:

1. **OCO Expiration**: Auto-cancel OCO pairs after time limit
2. **Bracket Trailing Stops**: Move stop-loss to follow profitable positions
3. **Iceberg Price Protection**: Cancel remaining chunks if market moves X ticks against limit price
4. **Advanced OCO Types**: Support stop-limit orders in OCO pairs
5. **Bracket Partial Fills**: Handle partial entry fills and scale stop/target accordingly
6. **Iceberg Adaptive Sizing**: Adjust chunk size based on market liquidity
7. **Configuration Service**: Centralized configuration for advanced order types
8. **Enhanced Metrics**: Track OCO/Bracket/Iceberg specific success rates

## References

- **Problem Statement**: See GitHub issue for Phase 5-7 requirements
- **OrderExecutionService.cs**: Implementation details
- **IOrderService.cs**: Interface definition (commented advanced methods)
- **.env.example**: Configuration template
- **TopstepXAdapterService.cs**: Fill event infrastructure
- **OrderExecutionMetrics.cs**: Metrics tracking

## Support

For questions or issues with advanced order types:

1. Check logs in `logs/trading-bot.log` for error messages
2. Verify configuration flags are enabled
3. Review this guide for usage patterns
4. Check production guardrails are not blocking execution
5. Test in paper trading mode before live trading

---

**Last Updated**: 2024-10-08  
**Version**: 1.0  
**Status**: Production Ready (with testing required)
