# Python Adapter Architecture - Data Flow Diagrams

## Phase 1: Fill Event Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         TopstepX Broker                                  │
│                      (project-x-py SDK)                                  │
└────────────────────────────┬────────────────────────────────────────────┘
                             │
                             │ WebSocket Connection
                             │ EventType.ORDER_FILLED
                             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Python Adapter (topstep_x_adapter.py)                 │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 1. WebSocket Callback: _on_order_filled(event_data)             │   │
│  │    • Receives GatewayUserTrade event                            │   │
│  │    • Extracts: orderId, contractId, qty, price, commission      │   │
│  │    • Maps contract ID: "CON.F.US.EP.Z25" → "ES"                 │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 2. Transform to C# Format                                       │   │
│  │    {                                                             │   │
│  │      "order_id": "ORD-123456",                                   │   │
│  │      "symbol": "ES",                                             │   │
│  │      "quantity": 1,                                              │   │
│  │      "price": 4500.00,                                           │   │
│  │      "fill_price": 4500.00,                                      │   │
│  │      "commission": 2.50,                                         │   │
│  │      "exchange": "CME",                                          │   │
│  │      "liquidity_type": "TAKER",                                  │   │
│  │      "timestamp": "2025-10-08T21:15:22.207000+00:00"             │   │
│  │    }                                                             │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 3. Store in Queue (Thread-Safe)                                 │   │
│  │    async with self._fill_events_lock:                           │   │
│  │        self._fill_events_queue.append(fill_event)               │   │
│  │                                                                  │   │
│  │    Queue: deque(maxlen=1000) - auto-drops oldest                │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 4. get_fill_events() - Polled by C#                             │   │
│  │    async with self._fill_events_lock:                           │   │
│  │        fills = list(self._fill_events_queue)                    │   │
│  │        self._fill_events_queue.clear()                          │   │
│  │    return {'fills': fills, 'timestamp': ...}                    │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ JSON via stdout
                               │ Every 2 seconds
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                C# Layer (TopstepXAdapterService.cs)                      │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 5. PollForFillEventsAsync() - Background Task                   │   │
│  │    var command = new { action = "get_fill_events" };            │   │
│  │    var result = await ExecutePythonCommandAsync(command);       │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 6. Parse JSON and Enqueue                                       │   │
│  │    foreach (var fillElement in fillsElement.EnumerateArray())   │   │
│  │    {                                                             │   │
│  │        var fillEvent = ParseFillEvent(fillElement);             │   │
│  │        _fillEventQueue.Enqueue(fillEvent);                      │   │
│  │        OnFillEventReceived?.Invoke(fillEvent);                  │   │
│  │    }                                                             │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ Event Notification
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                OrderExecutionService.cs                                  │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 7. OnOrderFillReceived(FillEventData fillData)                  │   │
│  │    • Updates _orders ConcurrentDictionary                        │   │
│  │    • Updates _positions ConcurrentDictionary                     │   │
│  │    • Fires OrderFilled event                                     │   │
│  │    • Records metrics                                             │   │
│  │    • Triggers OCO cancellation logic                             │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

## Phase 2: Position Query Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      C# Layer (OrderExecutionService.cs)                 │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 1. ReconcilePositionsWithBroker() - Scheduled Task               │   │
│  │    var brokerPositions = await _adapter.GetPositionsAsync();     │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ JSON Command
                               │ {"action": "get_positions"}
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│           C# Layer (TopstepXAdapterService.cs) - NEW METHOD              │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 2. GetPositionsAsync() - To Be Added                             │   │
│  │    var command = new { action = "get_positions" };               │   │
│  │    var result = await ExecutePythonCommandAsync(command);        │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ JSON via stdout
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Python Adapter (topstep_x_adapter.py)                 │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 3. get_positions() Method                                        │   │
│  │    all_positions = await self.suite.positions.get_all_positions()│   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│                      ┌────────────────┐                                  │
│                      │ TopstepX SDK    │                                  │
│                      │ Live API Call   │                                  │
│                      └────────┬───────┘                                  │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 4. Transform Each Position                                       │   │
│  │    for position in all_positions:                                │   │
│  │        net_pos = position.netPos  # Signed: +LONG, -SHORT        │   │
│  │        symbol = extract_symbol(position.contractId)              │   │
│  │        side = "LONG" if net_pos > 0 else "SHORT"                 │   │
│  │        avg_price = buyAvgPrice or sellAvgPrice (based on side)   │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
│                               │                                           │
│                               ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 5. Return Transformed Data                                       │   │
│  │    {                                                             │   │
│  │      "success": true,                                            │   │
│  │      "positions": [                                              │   │
│  │        {                                                         │   │
│  │          "symbol": "ES",                                         │   │
│  │          "quantity": 2,                                          │   │
│  │          "side": "LONG",                                         │   │
│  │          "avg_price": 4500.00,                                   │   │
│  │          "unrealized_pnl": 125.50,                               │   │
│  │          "realized_pnl": 0.0,                                    │   │
│  │          "position_id": "POS-ABC123"                             │   │
│  │        }                                                         │   │
│  │      ]                                                           │   │
│  │    }                                                             │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ JSON via stdout
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│           C# Layer (TopstepXAdapterService.cs) - NEW METHOD              │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 6. Parse and Return to Caller                                    │   │
│  │    var positions = new List<Position>();                         │   │
│  │    foreach (var posElement in positionsElement.EnumerateArray()) │   │
│  │    {                                                             │   │
│  │        positions.Add(new Position { ... });                      │   │
│  │    }                                                             │   │
│  │    return positions;                                             │   │
│  └───────────────────────────┬─────────────────────────────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────┘
                               │
                               │ List<Position>
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                OrderExecutionService.cs                                  │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 7. Reconcile Against Bot State                                   │   │
│  │    foreach (var brokerPos in brokerPositions)                    │   │
│  │    {                                                             │   │
│  │        if (!_positions.TryGetValue(brokerPos.Symbol, out botPos))│   │
│  │        {                                                         │   │
│  │            // Discrepancy: Broker has position, bot doesn't      │   │
│  │            LogWarning("Position mismatch detected");             │   │
│  │            // Auto-correct logic                                 │   │
│  │        }                                                         │   │
│  │        else if (brokerPos.Quantity != botPos.Quantity)           │   │
│  │        {                                                         │   │
│  │            // Discrepancy: Quantity mismatch                     │   │
│  │            LogWarning("Quantity mismatch detected");             │   │
│  │            // Auto-correct logic                                 │   │
│  │        }                                                         │   │
│  │    }                                                             │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

## Component Interactions

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                       │
│  ┌─────────────────┐         ┌─────────────────┐                    │
│  │   TopstepX      │◄────────┤  project-x-py   │                    │
│  │   Broker        │ WebSocket│     SDK         │                    │
│  └─────────────────┘         └────────┬────────┘                    │
│                                        │                              │
│                                        │ TradingSuite                 │
│                                        │ EventType                    │
│                                        │ Positions API                │
│                                        ▼                              │
│              ┌───────────────────────────────────────┐               │
│              │  topstep_x_adapter.py                 │               │
│              │  ┌─────────────────────────────────┐  │               │
│              │  │ • _on_order_filled() callback   │  │               │
│              │  │ • _fill_events_queue (deque)    │  │               │
│              │  │ • get_fill_events()             │  │               │
│              │  │ • get_positions()               │  │               │
│              │  │ • get_position(symbol)          │  │               │
│              │  └─────────────────────────────────┘  │               │
│              └───────────────┬───────────────────────┘               │
│                              │                                        │
│                              │ JSON Commands                          │
│                              │ via stdout/stdin                       │
│                              ▼                                        │
│              ┌───────────────────────────────────────┐               │
│              │  TopstepXAdapterService.cs            │               │
│              │  ┌─────────────────────────────────┐  │               │
│              │  │ • ExecutePythonCommandAsync()   │  │               │
│              │  │ • PollForFillEventsAsync()      │  │               │
│              │  │ • GetPositionsAsync() (new)     │  │               │
│              │  │ • _fillEventQueue               │  │               │
│              │  └─────────────────────────────────┘  │               │
│              └───────────────┬───────────────────────┘               │
│                              │                                        │
│                              │ C# Events                              │
│                              │ & Method Calls                         │
│                              ▼                                        │
│              ┌───────────────────────────────────────┐               │
│              │  OrderExecutionService.cs             │               │
│              │  ┌─────────────────────────────────┐  │               │
│              │  │ • OnOrderFillReceived()         │  │               │
│              │  │ • ReconcilePositionsWithBroker()│  │               │
│              │  │ • _orders ConcurrentDictionary  │  │               │
│              │  │ • _positions ConcurrentDict.    │  │               │
│              │  │ • OrderFilled event             │  │               │
│              │  └─────────────────────────────────┘  │               │
│              └───────────────────────────────────────┘               │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

## Symbol Mapping Flow

```
TopstepX Contract ID Format:
┌────────────────────────────────┐
│  CON.F.US.EP.Z25                │
│   │   │  │  │  │                │
│   │   │  │  │  └─ Expiry (Z=Dec)│
│   │   │  │  └──── Symbol Code   │
│   │   │  └─────── Country       │
│   │   └────────── Type          │
│   └────────────── Prefix        │
└────────────────────────────────┘
                │
                ▼
    _extract_symbol_from_contract_id()
                │
                ▼
┌────────────────────────────────┐
│  Symbol Mapping Table:          │
│  EP  → ES  (E-mini S&P 500)     │
│  ENQ → NQ  (E-mini NASDAQ)      │
│  MNQ → MNQ (Micro NASDAQ)       │
│  MES → ES  (Micro S&P 500)      │
└────────────────────────────────┘
                │
                ▼
          Standard Symbol
        (ES, NQ, MNQ, etc.)
```

## Thread Safety Model

```
┌─────────────────────────────────────────────────────────────┐
│                    Fill Events Queue                         │
│                                                              │
│  ┌────────────────────────────────────────────────────┐    │
│  │  Thread 1: WebSocket Callback                       │    │
│  │  async with self._fill_events_lock:                │    │
│  │      self._fill_events_queue.append(event) ───┐    │    │
│  └───────────────────────────────────────────────┼────┘    │
│                                                   │         │
│                    Asyncio Lock                   │         │
│                    (Mutex)                        │         │
│                        ▼                          │         │
│  ┌────────────────────────────────────────────────┼────┐    │
│  │  Thread 2: C# Polling (every 2 seconds)       │    │    │
│  │  async with self._fill_events_lock:           │    │    │
│  │      fills = list(self._fill_events_queue) ◄──┘    │    │
│  │      self._fill_events_queue.clear()               │    │
│  └────────────────────────────────────────────────────┘    │
│                                                              │
│  Result: No race conditions, no duplicates                  │
└─────────────────────────────────────────────────────────────┘
```

## Error Handling Strategy

```
┌──────────────────────────────────────────────────────────────┐
│                  Fail-Closed Architecture                     │
│                                                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ Event Subscription Failure                           │    │
│  │ suite.on(EventType.ORDER_FILLED, callback)          │    │
│  │         │                                            │    │
│  │         ├─ Success → Continue initialization         │    │
│  │         │                                            │    │
│  │         └─ Failure → Cleanup & raise RuntimeError    │    │
│  │                     (Adapter won't start)            │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ Position Query Failure                               │    │
│  │ suite.positions.get_all_positions()                 │    │
│  │         │                                            │    │
│  │         ├─ Success → Return positions                │    │
│  │         │                                            │    │
│  │         └─ Failure → Log error & return empty list   │    │
│  │                     (Graceful degradation)           │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ Fill Event Processing Failure                        │    │
│  │ _on_order_filled(event_data)                        │    │
│  │         │                                            │    │
│  │         ├─ Success → Add to queue                    │    │
│  │         │                                            │    │
│  │         └─ Failure → Log error, skip event           │    │
│  │                     (Individual event failure ok)    │    │
│  └─────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

## Performance Characteristics

```
┌──────────────────────────────────────────────────────────────┐
│                     Latency Profile                           │
│                                                               │
│  Fill Event Timeline:                                         │
│  ┌──────────┬────────────┬─────────┬──────────┬─────────┐  │
│  │  Order   │  Broker    │ WebSocket│ Python   │   C#    │  │
│  │  Filled  │  Confirms  │ Callback │  Queue   │  Poll   │  │
│  │  @ CME   │            │          │          │         │  │
│  └──────────┴────────────┴──────────┴──────────┴─────────┘  │
│       0ms       ~50ms       ~100ms     ~100ms    0-2000ms   │
│                                                               │
│  Total Latency: 100ms (WebSocket) + 0-2000ms (polling)       │
│  Average: ~1100ms (fast for non-HFT trading)                 │
│                                                               │
│  Position Query Timeline:                                     │
│  ┌──────────┬────────────┬─────────┬──────────┐             │
│  │   C#     │  Python    │   SDK   │  Broker  │             │
│  │  Calls   │  Receives  │   API   │  Returns │             │
│  └──────────┴────────────┴─────────┴──────────┘             │
│       0ms       ~10ms      ~50-200ms                          │
│                                                               │
│  Total Latency: 60-210ms (acceptable for reconciliation)     │
└──────────────────────────────────────────────────────────────┘
```

## Memory Usage

```
┌──────────────────────────────────────────────────────────────┐
│                 Memory Footprint Analysis                     │
│                                                               │
│  Fill Events Queue:                                           │
│  ┌────────────────────────────────────────────┐              │
│  │ deque(maxlen=1000)                         │              │
│  │ • Each event: ~200 bytes                   │              │
│  │ • Full queue: 200 KB                       │              │
│  │ • Auto-drops oldest when full              │              │
│  └────────────────────────────────────────────┘              │
│                                                               │
│  Position Data:                                               │
│  ┌────────────────────────────────────────────┐              │
│  │ Typical: 5-10 positions                    │              │
│  │ • Each position: ~150 bytes                │              │
│  │ • Total: ~1.5 KB                           │              │
│  │ • Transient (not cached)                   │              │
│  └────────────────────────────────────────────┘              │
│                                                               │
│  Total Python Adapter Memory: ~500 KB                         │
└──────────────────────────────────────────────────────────────┘
```

---

## Legend

```
┌─────────┐
│ Component│  = System component
└─────────┘

─────────>   = Data flow direction

┌ ─ ─ ─ ┐
│Optional│  = Optional/future component
└ ─ ─ ─ ┘

[LOCK]       = Thread synchronization

(async)      = Asynchronous operation

{retry}      = Retry logic present
```
