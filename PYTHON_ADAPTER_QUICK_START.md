# Python Adapter Quick Start Guide

## Overview

The TopstepX Python adapter now supports real-time fill events and position querying for C# integration.

## Prerequisites

```bash
# Install SDK
pip install 'project-x-py[all]>=3.5.0'

# Set credentials
export PROJECT_X_API_KEY='your_api_key'
export PROJECT_X_USERNAME='your_username'

# Set retry policy (required)
export ADAPTER_MAX_RETRIES=3
export ADAPTER_BASE_DELAY=1.0
export ADAPTER_MAX_DELAY=10.0
export ADAPTER_TIMEOUT=30.0
```

## Usage Examples

### 1. Get Fill Events (C# Polling)

**Command:**
```bash
python3 src/adapters/topstep_x_adapter.py '{"action":"get_fill_events"}'
```

**Response:**
```json
{
  "fills": [
    {
      "order_id": "ORD-123456",
      "symbol": "ES",
      "quantity": 1,
      "price": 4500.00,
      "fill_price": 4500.00,
      "commission": 2.50,
      "exchange": "CME",
      "liquidity_type": "TAKER",
      "timestamp": "2025-10-08T21:15:22.207000+00:00"
    }
  ],
  "timestamp": "2025-10-08T21:15:22.308000+00:00"
}
```

**Notes:**
- Returns all fills since last call (queue is cleared)
- WebSocket events populate queue automatically
- Empty array if no fills: `{"fills": [], "timestamp": "..."}`

### 2. Get Positions (For Reconciliation)

**Command:**
```bash
python3 src/adapters/topstep_x_adapter.py '{"action":"get_positions"}'
```

**Response:**
```json
{
  "success": true,
  "positions": [
    {
      "symbol": "ES",
      "quantity": 2,
      "side": "LONG",
      "avg_price": 4500.00,
      "unrealized_pnl": 125.50,
      "realized_pnl": 0.0,
      "position_id": "POS-ABC123"
    },
    {
      "symbol": "MNQ",
      "quantity": 1,
      "side": "SHORT",
      "avg_price": 18500.00,
      "unrealized_pnl": -50.25,
      "realized_pnl": 75.00,
      "position_id": "POS-XYZ789"
    }
  ]
}
```

**Notes:**
- Queries live broker positions via SDK
- `quantity` is always positive (use `side` for direction)
- `avg_price` uses `buyAvgPrice` for LONG, `sellAvgPrice` for SHORT
- Empty array if no positions: `{"success": true, "positions": []}`

### 3. Other Commands

**Get Health Score:**
```bash
python3 src/adapters/topstep_x_adapter.py '{"action":"get_health_score"}'
```

**Get Price:**
```bash
python3 src/adapters/topstep_x_adapter.py '{"action":"get_price", "symbol":"ES"}'
```

**Place Order:**
```bash
python3 src/adapters/topstep_x_adapter.py '{
  "action":"place_order",
  "symbol":"MNQ",
  "size":1,
  "stop_loss":18490.00,
  "take_profit":18515.00
}'
```

## Testing

### Run Integration Tests
```bash
# With mock SDK (no live API required)
ADAPTER_MAX_RETRIES=3 ADAPTER_BASE_DELAY=1.0 ADAPTER_MAX_DELAY=10.0 ADAPTER_TIMEOUT=30.0 \
python3 test_adapter_integration.py
```

**Expected Output:**
```
✅ All validation tests passed!
🔧 Ready for C# integration testing
```

### Test CLI Commands
```bash
chmod +x test_cli_commands.sh
./test_cli_commands.sh
```

## C# Integration

### Polling Fill Events (Already Implemented)

**C# Code:** `TopstepXAdapterService.cs`
```csharp
// Polls every 2 seconds automatically
private async Task PollForFillEventsAsync(CancellationToken cancellationToken)
{
    var command = new { action = "get_fill_events" };
    var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken);
    
    if (result.Success && result.Data.TryGetProperty("fills", out var fillsElement))
    {
        foreach (var fillElement in fillsElement.EnumerateArray())
        {
            var fillEvent = ParseFillEvent(fillElement);
            _fillEventQueue.Enqueue(fillEvent);
            // Notify OrderExecutionService
        }
    }
}
```

### Querying Positions (To Be Added)

**Suggested C# Method:**
```csharp
public async Task<List<Position>> GetPositionsAsync()
{
    var command = new { action = "get_positions" };
    var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command));
    
    if (!result.Success || result.Data == null)
        return new List<Position>();
    
    if (result.Data.TryGetProperty("positions", out var positionsElement))
    {
        var positions = new List<Position>();
        foreach (var posElement in positionsElement.EnumerateArray())
        {
            positions.Add(new Position
            {
                Symbol = posElement.GetProperty("symbol").GetString(),
                Quantity = posElement.GetProperty("quantity").GetInt32(),
                Side = posElement.GetProperty("side").GetString(),
                AveragePrice = posElement.GetProperty("avg_price").GetDecimal(),
                UnrealizedPnL = posElement.GetProperty("unrealized_pnl").GetDecimal(),
                RealizedPnL = posElement.GetProperty("realized_pnl").GetDecimal(),
                PositionId = posElement.GetProperty("position_id").GetString()
            });
        }
        return positions;
    }
    
    return new List<Position>();
}
```

**Usage in Reconciliation:**
```csharp
public async Task ReconcilePositionsWithBroker()
{
    var brokerPositions = await _adapterService.GetPositionsAsync();
    
    foreach (var brokerPos in brokerPositions)
    {
        if (!_positions.TryGetValue(brokerPos.Symbol, out var botPos))
        {
            _logger.LogWarning("Position discrepancy: Broker has {Symbol} but bot doesn't", 
                brokerPos.Symbol);
            // Auto-correct logic here
        }
        else if (brokerPos.Quantity != Math.Abs(botPos.Quantity))
        {
            _logger.LogWarning("Quantity mismatch for {Symbol}: Broker={Broker}, Bot={Bot}",
                brokerPos.Symbol, brokerPos.Quantity, Math.Abs(botPos.Quantity));
            // Auto-correct logic here
        }
    }
}
```

## Troubleshooting

### Issue: Empty Fill Events

**Symptoms:** `get_fill_events` always returns empty array

**Checks:**
1. Verify WebSocket subscription succeeded:
   ```
   ✅ Subscribed to ORDER_FILLED events via WebSocket
   ```
2. Check for fill event logs:
   ```
   [FILL-EVENT] Order filled: ORD-123 ES 1 @ $4500.00
   ```
3. Ensure orders are actually filling in the SDK

### Issue: No Positions Returned

**Symptoms:** `get_positions` returns empty array when positions exist

**Checks:**
1. Verify SDK connection:
   ```bash
   python3 src/adapters/topstep_x_adapter.py '{"action":"get_health_score"}'
   ```
2. Check SDK version: `pip show project-x-py` (should be >=3.5.0)
3. Verify positions exist in SDK:
   ```python
   positions = await suite.positions.get_all_positions()
   print(positions)  # Should show position objects
   ```

### Issue: Adapter Initialization Fails

**Symptoms:** RuntimeError during initialization

**Checks:**
1. Verify environment variables are set:
   ```bash
   echo $PROJECT_X_API_KEY
   echo $ADAPTER_MAX_RETRIES
   ```
2. Check credentials validity
3. Review logs for specific error message

## Performance Notes

- **Fill Events:** ~100ms latency (WebSocket), polled every 2 seconds by C#
- **Position Queries:** ~50-200ms per call, should be called sparingly (reconciliation only)
- **Queue Capacity:** 1000 events (auto-drops oldest)
- **Memory Usage:** ~200KB for full fill event queue

## Best Practices

1. **Fill Events:** Let C# poll automatically, don't call directly except for testing
2. **Positions:** Call only during reconciliation (e.g., every 5 minutes)
3. **Error Handling:** Always check `success` field and handle errors gracefully
4. **Logging:** Monitor `[FILL-EVENT]` and `[POSITION]` log entries
5. **Testing:** Use mock SDK for unit tests, live SDK for integration tests

## Further Reading

- Full implementation details: `PHASE_1_2_PYTHON_ADAPTER_IMPLEMENTATION.md`
- C# integration: `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs`
- Mock SDK: `tests/mocks/topstep_x_mock.py`
