# Historical Data Retrieval and Live Streaming Implementation

## Overview
This document explains the implementation of historical data retrieval and live bar streaming for the QBot trading system. The implementation enables the bot to fetch historical OHLCV data from TopstepX and receive real-time bar updates via WebSocket, providing a complete data pipeline for trading decisions.

## Architecture

### Data Flow
```
TopstepX API (REST) → Python SDK Bridge → Historical Bars → C# Services
TopstepX WebSocket → Python Adapter → Live Bars → C# Services → Trading Strategies
```

### Components

#### 1. Historical Data Retrieval (`python/sdk_bridge.py`)
**Purpose:** Fetch historical OHLCV bars from TopstepX REST API

**Key Method:** `get_historical_bars(symbol, timeframe, count, end_time)`
- Maps symbols to TopstepX contract IDs (ES → CON.F.US.EP.U25, MNQ → CON.F.US.MNQ.U25)
- Calls `/api/History/retrieveBars` endpoint with proper authentication
- Returns JSON array of bars with timestamp, open, high, low, close, volume
- Falls back to simulated data if API unavailable

**Usage:**
```bash
python python/sdk_bridge.py get_historical_bars ES 1m 100
```

#### 2. Live Bar Streaming (`src/adapters/topstep_x_adapter.py`)
**Purpose:** Subscribe to real-time bar completion events via WebSocket

**Key Features:**
- Subscribes to EventType.BAR_UPDATE (or CANDLE_UPDATE, BAR_CLOSED, CANDLE_CLOSED)
- `_on_bar_update()` callback processes incoming bar events
- Stores bars in thread-safe queue (`_bar_events_queue`)
- `get_bar_events()` method returns queued bars for C# to poll
- Outputs bar JSON to stdout for streaming

**Bar Event Format:**
```json
{
  "type": "bar",
  "instrument": "ES",
  "timestamp": "2025-10-14T06:00:00Z",
  "open": 6673.0,
  "high": 6673.5,
  "low": 6672.75,
  "close": 6673.25,
  "volume": 1250
}
```

#### 3. C# Bar Event Polling (`src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs`)
**Purpose:** Poll Python adapter for bar events and distribute to subscribers

**Key Features:**
- `StartBarEventListener()` - Background task polling every 5 seconds
- `PollForBarEventsAsync()` - Calls Python `get_bar_events` command
- `ParseBarEvent()` - Converts JSON to `BarEventData` record
- `SubscribeToBarEvents()` - Allows services to subscribe to bar events
- `BarEventReceived` event - Notifies all subscribers when new bars arrive

**Data Types:**
```csharp
internal record BarEventData(
    string Type,
    string Instrument,
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);
```

#### 4. Data Integration (`src/UnifiedOrchestrator/Services/UnifiedDataIntegrationService.cs`)
**Purpose:** Unified data pipeline merging historical and live data

**Key Features:**
- Constructor subscribes to `TopstepXAdapterService.BarEventReceived`
- `OnBarEventReceived()` - Processes incoming live bars
- Tracks data flow metrics (bars received, success rate)
- Updates connection status flags
- Records data flow events for monitoring

**Metrics Tracked:**
- `_liveBarsReceived` - Count of live bars received
- `_totalBarsReceived` - Total bars (historical + live)
- `_isLiveDataConnected` - Connection status flag
- `_lastLiveDataReceived` - Timestamp of last bar

## Configuration

### Environment Variables Required
```bash
# TopstepX Authentication
TOPSTEPX_API_KEY=<your-api-key>
TOPSTEPX_USERNAME=<your-username>
TOPSTEPX_ACCOUNT_ID=<your-account-id>
TOPSTEPX_JWT=<jwt-token>  # For historical API

# Adapter Configuration
ADAPTER_MAX_RETRIES=3
ADAPTER_BASE_DELAY=1.0
ADAPTER_MAX_DELAY=30.0
ADAPTER_TIMEOUT=60.0

# Python Environment
PYTHON_EXECUTABLE=wsl  # Windows with WSL
# or
PYTHON_EXECUTABLE=python3  # Linux/Mac
```

### Contract ID Mapping
```python
contract_id_map = {
    'ES': 'CON.F.US.EP.U25',    # E-mini S&P 500
    'MNQ': 'CON.F.US.MNQ.U25',  # Micro E-mini NASDAQ
    'NQ': 'CON.F.US.ENQ.U25',   # E-mini NASDAQ
    'MES': 'CON.F.US.MES.U25'   # Micro E-mini S&P 500
}
```

## Usage Examples

### 1. Fetch Historical Data
```bash
# From command line
python python/sdk_bridge.py get_historical_bars ES 1m 100

# Expected output: JSON array with 100 bars
```

### 2. Subscribe to Live Bars in C#
```csharp
// In a service constructor
public MyService(TopstepXAdapterService adapter)
{
    adapter.SubscribeToBarEvents(OnBarReceived);
}

private void OnBarReceived(BarEventData bar)
{
    _logger.LogInformation(
        "Bar: {Instrument} @ {Time} - C={Close} V={Volume}",
        bar.Instrument, bar.Timestamp, bar.Close, bar.Volume
    );
}
```

### 3. Check Data Integration Status
```csharp
var status = await _dataIntegrationService.GetDataIntegrationStatusAsync();
// Returns: historical connected, live connected, bars received, etc.
```

## Data Pipeline Flow

### Initialization Sequence
1. **Bot Startup**
   - `TopstepXAdapterService` initializes Python adapter
   - Python adapter connects to TopstepX and subscribes to bar events
   - C# service starts background bar polling task

2. **Historical Data Loading** (if needed)
   - Services call `sdk_bridge.get_historical_bars()`
   - Returns last N bars for warmup/indicators
   - Bars stored in aggregators for strategy use

3. **Live Data Streaming**
   - Python adapter receives bar events via WebSocket
   - Bars queued in `_bar_events_queue`
   - C# polls queue every 5 seconds
   - Bars distributed to all subscribers

### Bar Processing
```
WebSocket Event → Python Callback → Event Queue → C# Polling →
Event Distribution → Service Callbacks → Strategy Updates →
Indicator Calculations → Trading Decisions
```

## Monitoring and Health Checks

### Success Indicators
- ✅ Historical bars return valid OHLCV data
- ✅ Live bars arrive within 1 second of bar close
- ✅ Data flow events show increasing bar count
- ✅ Connection health remains ≥ 80%
- ✅ No WebSocket disconnection errors

### Logs to Monitor
```
[BAR-LISTENER] Received 1m bar for ES: O=6673.0 H=6673.5 L=6672.75 C=6673.25 V=1250
[DATA-INTEGRATION] Live bar: ES @ 2025-10-14T06:00:00 - C=6673.25 V=1250
[DATA-INTEGRATION] Unified pipeline status - Historical: True, Live: True
```

### Performance Metrics
- Historical data loads in < 10 seconds
- Live bars arrive every ~60 seconds (1-minute timeframe)
- Bar polling overhead < 100ms
- Single persistent Python process (no spawning)

## Error Handling

### Historical Data Failures
- Falls back to simulated data if TopstepX API unavailable
- Logs warning and continues operation
- Strategies can still run with live data only

### Live Streaming Failures
- Reconnection logic: 5-second delay before retry
- Non-critical failure mode (doesn't break initialization)
- Continues trying to reconnect in background

### WebSocket Disconnections
- Adapter maintains connection health tracking
- C# polls for reconnection status
- Event subscriptions automatically restored

## Testing

### Unit Tests
```bash
# Test historical data retrieval
python python/sdk_bridge.py get_historical_bars ES 1m 10

# Test adapter connection
dotnet test tests/Integration/TopstepXAdapterTests.cs
```

### Integration Tests
```bash
# Start bot and check logs
./dev-helper.sh run

# Expected logs:
# - "Subscribed to BAR_UPDATE events via WebSocket"
# - "Bar event listener started"
# - "Received 1m bar for ES"
```

### Manual Verification
1. Check Python process is running: `Get-Process -Name python`
2. Monitor bar events in logs
3. Verify data integration status via health endpoint
4. Confirm strategies receive bar updates

## Troubleshooting

### No Historical Data
**Symptoms:** Empty bars array returned
**Solutions:**
- Check TOPSTEPX_JWT environment variable
- Verify contract ID mapping is correct
- Check TopstepX API status
- Review Python logs for API errors

### No Live Bars
**Symptoms:** No bar events in logs
**Solutions:**
- Verify Python adapter initialized successfully
- Check WebSocket connection status
- Ensure EventType subscription succeeded
- Review Python adapter logs

### High Latency
**Symptoms:** Bars arrive > 5 seconds after bar close
**Solutions:**
- Check network connectivity
- Reduce polling interval (not recommended < 2 seconds)
- Verify WebSocket is not reconnecting frequently

## Future Enhancements
- [ ] Support for multiple timeframes (5m, 15m, 1h)
- [ ] Bar aggregation from tick data
- [ ] Historical data caching
- [ ] Configurable polling intervals
- [ ] WebSocket direct streaming (bypass polling)
- [ ] Bar gap detection and recovery
- [ ] Historical backfill on startup

## References
- [TopstepX API Documentation](https://gateway.docs.projectx.com)
- [project-x-py SDK](https://project-x-py.readthedocs.io)
- [Retrieve Bars API](https://gateway.docs.projectx.com/docs/api-reference/market-data/retrieve-bars/)
- [Real-Time Data Guide](https://project-x-py.readthedocs.io/en/latest/user_guide/real_time.html)
