# TopstepX HTTP Adapter Integration Guide

This document describes the HTTP-based communication layer between the .NET bot and the Python TopstepX adapter.

## Overview

The HTTP adapter mode replaces the previous stdin/stdout communication with a REST API, providing:
- Better reliability and error handling
- Real-time event streaming
- Easier debugging and monitoring
- Independent process lifecycle management

## Architecture

```
┌──────────────────────────────────────────┐
│  .NET Trading Bot (UnifiedOrchestrator)  │
│                                          │
│  ┌────────────────────────────────────┐ │
│  │  TopstepXAdapterService.cs         │ │
│  │  (HTTP Client)                     │ │
│  └────────────────┬───────────────────┘ │
└───────────────────┼─────────────────────┘
                    │
         HTTP REST API (localhost:8765)
                    │
┌───────────────────┼─────────────────────┐
│  Python Adapter   │                     │
│                   ▼                     │
│  ┌────────────────────────────────────┐ │
│  │  Flask HTTP Server                 │ │
│  │  - GET /health                     │ │
│  │  - GET /price/{symbol}             │ │
│  │  - POST /order                     │ │
│  │  - GET /positions                  │ │
│  │  - GET /events                     │ │
│  │  - POST /close_position            │ │
│  │  - POST /modify_stop               │ │
│  │  - POST /cancel_order              │ │
│  └────────────────┬───────────────────┘ │
│                   │                     │
│  ┌────────────────▼───────────────────┐ │
│  │  TopstepXAdapter                   │ │
│  │  (WebSocket connection kept alive) │ │
│  └────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

## Configuration

### appsettings.json

Add the following to your `TopstepX` configuration section:

```json
{
  "TopstepX": {
    "ApiBaseUrl": "https://api.topstepx.com",
    "UserHubUrl": "https://rtc.topstepx.com/hubs/user",
    "MarketHubUrl": "https://rtc.topstepx.com/hubs/market",
    "AdapterMode": "http",
    "AdapterHttpHost": "localhost",
    "AdapterHttpPort": 8765,
    "AdapterStartupTimeoutSeconds": 30
  }
}
```

### Environment Variables

The Python adapter requires these environment variables:
- `TOPSTEPX_API_KEY` - Your TopstepX API key
- `TOPSTEPX_USERNAME` - Your TopstepX username
- `TOPSTEPX_ACCOUNT_ID` - Your TopstepX account ID
- `ADAPTER_HTTP_HOST` - Server host (default: localhost)
- `ADAPTER_HTTP_PORT` - Server port (default: 8765)

## Running the System

### Option 1: Manual Start (for development)

1. **Start Python HTTP Server:**
   ```bash
   # WSL (if using Windows with WSL)
   wsl -d Ubuntu-24.04 -e bash -c "python3 src/adapters/topstep_x_adapter.py serve"
   
   # Native Linux/macOS
   python3 src/adapters/topstep_x_adapter.py serve
   ```

2. **Verify Server is Running:**
   ```bash
   curl http://localhost:8765/health
   ```
   
   Should return:
   ```json
   {
     "status": "healthy",
     "initialized": true,
     "health_score": 100,
     "details": { ... }
   }
   ```

3. **Start .NET Bot:**
   ```bash
   dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
   ```

### Option 2: Automated Start (recommended for production)

The .NET service automatically starts the Python HTTP server when `AdapterMode` is set to `"http"`.

## API Endpoints

### GET /health
Returns adapter health status and initialization state.

**Response:**
```json
{
  "status": "healthy",
  "initialized": true,
  "health_score": 100,
  "details": {
    "health_score": 100,
    "status": "healthy",
    "instruments": {
      "ES": 100.0,
      "NQ": 100.0
    }
  }
}
```

### GET /price/{symbol}
Get current price for a symbol (ES or NQ).

**Response:**
```json
{
  "success": true,
  "symbol": "ES",
  "price": 4520.25,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### POST /order
Place a bracket order.

**Request:**
```json
{
  "symbol": "ES",
  "size": 1,
  "stop_loss": 4515.00,
  "take_profit": 4525.00,
  "max_risk_percent": 0.01
}
```

**Response:**
```json
{
  "success": true,
  "order_id": "ORD123456",
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### GET /positions
Get all open positions.

**Response:**
```json
{
  "success": true,
  "positions": [
    {
      "symbol": "ES",
      "size": 1,
      "entry_price": 4520.00,
      "unrealized_pnl": 125.00
    }
  ],
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### GET /events
Poll for new WebSocket events (fills and bars).

**Response:**
```json
{
  "success": true,
  "fills": [
    {
      "orderId": "ORD123456",
      "symbol": "ES",
      "quantity": 1,
      "fillPrice": 4520.25,
      "timestamp": "2025-01-15T10:30:00Z"
    }
  ],
  "bars": [
    {
      "instrument": "ES",
      "timestamp": "2025-01-15T10:30:00Z",
      "open": 4520.00,
      "high": 4522.00,
      "low": 4519.00,
      "close": 4521.00,
      "volume": 1000
    }
  ],
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### POST /close_position
Close a position.

**Request:**
```json
{
  "symbol": "ES",
  "quantity": 1
}
```

**Response:**
```json
{
  "success": true,
  "message": "Position closed"
}
```

### POST /modify_stop
Modify stop loss for a position.

**Request:**
```json
{
  "symbol": "ES",
  "stop_price": 4518.00
}
```

**Response:**
```json
{
  "success": true,
  "message": "Stop loss modified"
}
```

### POST /cancel_order
Cancel an order.

**Request:**
```json
{
  "order_id": "ORD123456"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Order cancelled"
}
```

## Event Streaming

The .NET service polls the `/events` endpoint every 100ms to receive real-time:
- **Fill Events**: Order fills from TopstepX
- **Bar Events**: Price bar completions

These events are automatically processed and dispatched to the appropriate handlers in the trading system.

## Debugging

### Check Python Server Logs
The Python server logs all requests and responses to stdout. Monitor these logs to debug issues.

### Test Endpoints with curl
```bash
# Health check
curl http://localhost:8765/health

# Get ES price
curl http://localhost:8765/price/ES

# Get positions
curl http://localhost:8765/positions

# Get events
curl http://localhost:8765/events
```

### .NET Service Logs
The .NET service logs all HTTP requests and responses with `[HTTP]` prefix:
```
[HTTP] HTTP client created: http://localhost:8765
[HTTP] Starting Python HTTP server: python3 src/adapters/topstep_x_adapter.py serve
[HTTP] Python server is ready: {"status":"healthy","initialized":true}
[HTTP] Starting event polling task
```

## Switching Between Modes

### HTTP Mode (recommended)
```json
{
  "TopstepX": {
    "AdapterMode": "http"
  }
}
```

### stdin/stdout Mode (legacy)
```json
{
  "TopstepX": {
    "AdapterMode": "stdin"
  }
}
```

## Troubleshooting

### Python server won't start
- **Check Flask is installed**: `pip install flask flask-cors`
- **Check project-x-py SDK is installed**: `pip install 'project-x-py[all]'`
- **Verify credentials are set**: `echo $TOPSTEPX_API_KEY`

### Connection refused errors
- **Verify server is running**: `curl http://localhost:8765/health`
- **Check port is not in use**: `lsof -i :8765` (Linux/macOS)
- **Check firewall settings**: Ensure localhost:8765 is accessible

### Server starts but returns errors
- **Check TopstepX credentials are valid**
- **Monitor Python server logs for error messages**
- **Verify WebSocket connection to TopstepX is established**

### .NET service can't connect
- **Verify `AdapterHttpHost` and `AdapterHttpPort` match server configuration**
- **Increase `AdapterStartupTimeoutSeconds` if server takes longer to initialize**
- **Check Python process is still running**: `ps aux | grep topstep`

## Performance Considerations

- **Event Polling**: Polls every 100ms by default - adjust if needed
- **HTTP Overhead**: Minimal compared to stdin/stdout for most operations
- **WebSocket**: Kept alive in background thread for real-time events
- **Thread Safety**: Event loop runs in dedicated background thread

## Migration from stdin/stdout Mode

1. Update `appsettings.json` to set `AdapterMode: "http"`
2. Add HTTP configuration (host, port, timeout)
3. Restart the bot - it will automatically use HTTP mode
4. Monitor logs to verify HTTP communication is working
5. Test all trading operations (price, orders, positions)

The service maintains backward compatibility - you can switch back to `stdin` mode at any time.
