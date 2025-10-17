# 🛡️ Trading Safety Guardrails (Runtime Protection)

## ❌ NEVER DO THESE (FINANCIAL SAFETY)

❌ **Stub Code**: Never add placeholder code, fake data, simulated responses, or TODO stubs to production bot  
❌ **Mock Implementations**: Never use mock services, stub methods, or fake API responses in production builds  
❌ **Incomplete Features**: Never merge code with "temporary" workarounds, hardcoded values, or partial implementations  
❌ **Order Bypasses**: Never claim order fills without orderId + GatewayUserTrade fill event proof  
❌ **Risk Bypasses**: Never skip ES/MES tick rounding (0.25) or risk validation (risk ≤ 0)  
❌ **Safety Bypasses**: Never disable DRY_RUN mode, kill.txt monitoring, or production guardrails  
❌ **Live API in CI**: Never connect to live trading APIs from CI/cloud environments  
❌ **VPN Trading**: Never execute trades from VPN, VPS, or remote desktop connections  
❌ **Kill Switch Override**: Never disable or bypass ProductionKillSwitchService monitoring  
❌ **Fake Data in Live**: Never use simulated/fake data when LIVE_TRADING_ENABLED=true  
❌ **Unvalidated Orders**: Never submit orders without price tick validation and risk validation  
❌ **Position Overrides**: Never modify position size without re-validating risk limits  
❌ **Account Bypasses**: Never skip account equity checks before position sizing  

## ✅ ALWAYS DO THESE (FINANCIAL PROTECTION)

✅ **Production Ready Only**: Every feature, service, and component must be fully implemented (no stubs, mocks, or TODOs)  
✅ **Real Implementations**: All code must use real APIs, real data, real connections (no fake/simulated responses)  
✅ **Complete Features**: Every function must be 100% complete before merge (no partial implementations or placeholders)  
✅ **Full Logging**: Every critical operation must have comprehensive structured logging (no silent failures)  
✅ **Order Evidence**: Require orderId + GatewayUserTrade event before claiming fills  
✅ **Tick Rounding**: Round ES/MES prices to 0.25 using `Px.RoundToTick()` before order submission  
✅ **Risk Validation**: Reject trades with risk ≤ 0 using proper R-multiple calculation (risk = entry - stop)  
✅ **DRY_RUN Default**: Default to simulation mode unless LIVE_TRADING_ENABLED=true explicitly set  
✅ **Kill Switch Active**: ProductionKillSwitchService monitors kill.txt every 5 seconds  
✅ **Real Data Validation**: Verify TopstepX connection returns real market data (not simulation)  
✅ **Position Limits**: Enforce max position size limits from configuration (never exceed)  
✅ **Account Protection**: Check account equity before every trade (prevent over-leverage)  
✅ **Stop Loss Required**: Every position must have a stop loss price defined before entry  
✅ **Environment Isolation**: Live trading only from secure local development machine  

## � PRODUCTION-READY REQUIREMENTS (NO EXCEPTIONS)

### Code Quality Standards
Every piece of code added to this bot MUST be production-ready:

❌ **NEVER ALLOWED**:
- Stub methods that return hardcoded values
- `throw new NotImplementedException()` in production code
- `// TODO: Implement this later` comments in merged code
- Mock/fake services in production dependency injection
- Simulated data generators in live trading paths
- Placeholder configuration values
- Commented-out "temporary" code
- Test-only implementations in production classes

✅ **ALWAYS REQUIRED**:
- Full implementation of every method before merge
- Real API connections (TopstepX, not mocks)
- Real data sources (live market data, not simulated)
- Complete error handling (try/catch with logging)
- Comprehensive logging for all critical operations
- Production-grade configuration (no hardcoded test values)
- All dependencies resolved (no missing services)
- Complete test coverage for new features

### Examples of PROHIBITED Code

❌ **WRONG - Stub Method**:
```csharp
public async Task<decimal> GetAccountEquityAsync() {
    // TODO: Implement real account query
    return 100000m; // Hardcoded fake value
}
```

✅ **CORRECT - Real Implementation**:
```csharp
public async Task<decimal> GetAccountEquityAsync() {
    try {
        var account = await _topstepxAdapter.GetAccountInfoAsync();
        
        if (account == null) {
            _logger.LogError("Failed to retrieve account info");
            throw new InvalidOperationException("Account info unavailable");
        }
        
        _logger.LogDebug("Account equity: {Equity}", account.Equity);
        return account.Equity;
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Error retrieving account equity");
        throw;
    }
}
```

❌ **WRONG - Fake Data Generator**:
```csharp
public async Task<Quote> GetQuoteAsync(string symbol) {
    // Temporary: Generate fake quotes until API is ready
    var random = new Random();
    return new Quote {
        Bid = 4500m + (decimal)random.NextDouble() * 100m,
        Ask = 4501m + (decimal)random.NextDouble() * 100m
    };
}
```

✅ **CORRECT - Real API Call**:
```csharp
public async Task<Quote> GetQuoteAsync(string symbol) {
    try {
        var quote = await _topstepxAdapter.GetMarketDataAsync(symbol);
        
        if (quote == null || quote.Bid == 0 || quote.Ask == 0) {
            _logger.LogError("Invalid quote for {Symbol}: Bid={Bid} Ask={Ask}", 
                symbol, quote?.Bid ?? 0, quote?.Ask ?? 0);
            throw new InvalidOperationException($"Invalid market data for {symbol}");
        }
        
        if (quote.Timestamp < DateTimeOffset.UtcNow.AddMinutes(-5)) {
            _logger.LogWarning("Stale quote for {Symbol}: {Timestamp}", symbol, quote.Timestamp);
        }
        
        _logger.LogDebug("Quote for {Symbol}: {Bid}/{Ask} @ {Time}", 
            symbol, quote.Bid, quote.Ask, quote.Timestamp);
        
        return quote;
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Error retrieving quote for {Symbol}", symbol);
        throw;
    }
}
```

❌ **WRONG - Mock Service in Production DI**:
```csharp
// In Program.cs
services.AddSingleton<IOrderExecutor>(sp => new MockOrderExecutor()); // NEVER DO THIS
```

✅ **CORRECT - Real Service in Production DI**:
```csharp
// In Program.cs
services.AddSingleton<IOrderExecutor, TopstepXOrderExecutor>(); // Real implementation
```

### Pre-Merge Checklist

Before merging ANY code to production bot, verify:

- [ ] **No Stubs**: Zero stub methods, all implementations complete
- [ ] **No Mocks**: Zero mock services in production dependency injection
- [ ] **No Fake Data**: Zero fake data generators or simulated responses
- [ ] **No TODOs**: Zero `// TODO` comments in production code paths
- [ ] **No Hardcoded Values**: All configuration from .env or appsettings.json
- [ ] **Full Logging**: Every critical operation logs success/failure
- [ ] **Error Handling**: Every external call wrapped in try/catch with logging
- [ ] **Real APIs**: All services connect to real TopstepX APIs (not mocks)
- [ ] **Real Data**: All market data from live TopstepX connection
- [ ] **Complete Tests**: All new features have passing unit/integration tests
- [ ] **Safety Compliant**: All trading operations enforce guardrails

### Enforcement

**Code Review Process**:
1. Every PR must pass automated checks (build, test, analyzer)
2. Manual review must verify zero stubs/mocks/fake data
3. Any TODO comments must be resolved before merge
4. All new services must have real implementations
5. Production DI container must use real services only

**Rejection Criteria**:
Any PR containing the following will be REJECTED immediately:
- `throw new NotImplementedException()`
- `// TODO: Implement` in production code
- Mock services registered in production DI
- Hardcoded fake values (except for tests)
- Simulated data generators
- Incomplete error handling
- Missing logging for critical operations

## �🔒 Core Trading Safety Systems

### 1. Production Kill Switch (Mandatory)
**File**: `src/Safety/ProductionKillSwitchService.cs`  
**Purpose**: Emergency stop mechanism to force DRY_RUN mode

**How It Works**:
```
1. Monitor kill.txt file every 5 seconds
2. If kill.txt exists → Set LIVE_TRADING_ENABLED=false
3. Force all strategies into DRY_RUN mode
4. Log critical alert: "Kill switch activated - forcing DRY_RUN"
5. Continue monitoring until kill.txt deleted
```

**How to Trigger**:
```bash
# Create kill.txt to stop live trading immediately
echo "Emergency stop - switching to DRY_RUN mode" > kill.txt

# Delete kill.txt to resume live trading (requires bot restart)
rm kill.txt
```

**Validation**:
```bash
# Test kill switch functionality
./test-production-guardrails.sh

# Expected output:
# ✅ Kill switch monitoring active
# ✅ DRY_RUN mode forced when kill.txt exists
# ✅ Live trading blocked when kill switch active
```

### 2. Order Validation System (Pre-Flight Checks)
**File**: `src/BotCore/Services/OrderValidator.cs`  
**Purpose**: Validate every order before submission to broker

**Validation Checks**:
```csharp
// 1. Price Tick Validation (ES/MES)
decimal tickSize = symbol.Contains("ES") ? 0.25m : GetTickSize(symbol);
decimal roundedPrice = Math.Round(price / tickSize) * tickSize;
if (price != roundedPrice) {
    _logger.LogError("Price {Price} not aligned to tick size {TickSize}", price, tickSize);
    return ValidationResult.Fail("Invalid tick size");
}

// 2. Risk Validation (R-Multiple)
decimal risk = Math.Abs(entryPrice - stopPrice);
if (risk <= 0) {
    _logger.LogError("Risk must be > 0, got {Risk}", risk);
    return ValidationResult.Fail("Invalid risk calculation");
}

// 3. Position Size Validation
int maxContracts = GetMaxContractsForAccount(accountEquity, risk);
if (quantity > maxContracts) {
    _logger.LogError("Quantity {Quantity} exceeds max {MaxContracts}", quantity, maxContracts);
    return ValidationResult.Fail("Position size exceeds risk limits");
}

// 4. Account Equity Validation
if (accountEquity < minimumEquity) {
    _logger.LogError("Account equity {Equity} below minimum {MinEquity}", accountEquity, minimumEquity);
    return ValidationResult.Fail("Insufficient account equity");
}
```

**Order Evidence Requirement**:
```csharp
// ✅ CORRECT: Wait for order ID + fill event
var orderId = await _adapter.SubmitOrderAsync(order);
if (string.IsNullOrEmpty(orderId)) {
    _logger.LogError("Order submission failed - no orderId returned");
    return OrderResult.Fail("No orderId");
}

// Wait for fill event from GatewayUserTrade
var fillEvent = await WaitForFillEventAsync(orderId, timeout: TimeSpan.FromSeconds(30));
if (fillEvent == null) {
    _logger.LogWarning("Order {OrderId} not filled within timeout", orderId);
    return OrderResult.Timeout(orderId);
}

_logger.LogInformation(
    "Order filled: {OrderId} at {FillPrice} (Expected: {OrderPrice})",
    orderId, fillEvent.Price, order.Price
);

// ❌ WRONG: Assume fill without proof
// var orderId = await _adapter.SubmitOrderAsync(order);
// _position = new Position(order.Price, order.Quantity); // DON'T DO THIS
```

### 3. Risk Management System
**File**: `src/BotCore/Services/RiskManager.cs`  
**Purpose**: Calculate and enforce position sizing based on account risk

**Risk Calculation**:
```csharp
// R-Multiple = (Entry Price - Stop Loss Price) × Point Value
// Position Size = (Account Equity × Risk %) / R-Multiple

public int CalculatePositionSize(
    decimal accountEquity,
    decimal entryPrice,
    decimal stopPrice,
    decimal riskPercentage = 0.01m) // 1% default
{
    // 1. Calculate risk per contract in dollars
    decimal riskPerContract = Math.Abs(entryPrice - stopPrice) * GetPointValue(symbol);
    
    if (riskPerContract <= 0) {
        _logger.LogError("Risk per contract must be > 0, got {Risk}", riskPerContract);
        return 0; // Reject trade
    }
    
    // 2. Calculate max dollar risk for account
    decimal maxDollarRisk = accountEquity * riskPercentage;
    
    // 3. Calculate contracts
    int contracts = (int)(maxDollarRisk / riskPerContract);
    
    // 4. Apply position limits
    int maxContracts = _config.GetMaxContracts(symbol);
    contracts = Math.Min(contracts, maxContracts);
    
    _logger.LogInformation(
        "Position sizing: Equity={Equity} Risk%={RiskPct} RiskPerContract={RPC} → Contracts={Contracts}",
        accountEquity, riskPercentage, riskPerContract, contracts
    );
    
    return contracts;
}

// ES/MES Point Values
private decimal GetPointValue(string symbol) {
    return symbol switch {
        "ES" => 50m,      // $50 per point
        "MES" => 5m,      // $5 per point
        "NQ" => 20m,      // $20 per point
        "MNQ" => 2m,      // $2 per point
        _ => throw new ArgumentException($"Unknown symbol: {symbol}")
    };
}
```

**Tick Size Enforcement**:
```csharp
public static class Px {
    public static decimal RoundToTick(decimal price, string symbol) {
        decimal tickSize = symbol switch {
            "ES" or "MES" => 0.25m,
            "NQ" or "MNQ" => 0.25m,
            "CL" => 0.01m,
            _ => 0.01m
        };
        
        decimal rounded = Math.Round(price / tickSize) * tickSize;
        
        if (price != rounded) {
            _logger.LogWarning(
                "Price {Price} rounded to {Rounded} (tick size {TickSize})",
                price, rounded, tickSize
            );
        }
        
        return rounded;
    }
}

// Usage in order submission
var entryPrice = Px.RoundToTick(calculatedPrice, "ES"); // ALWAYS round
var stopPrice = Px.RoundToTick(calculatedStop, "ES");
```

### 4. DRY_RUN Mode Enforcement
**File**: `src/UnifiedOrchestrator/Program.cs`  
**Purpose**: Default to simulation mode, require explicit opt-in for live trading

**Environment Configuration**:
```bash
# .env file - DRY_RUN mode (safe default)
LIVE_TRADING_ENABLED=false   # Default: false (simulation)
DRY_RUN_MODE=true            # Default: true (paper trading)

# .env file - LIVE trading (explicit opt-in)
LIVE_TRADING_ENABLED=true    # Must explicitly enable
DRY_RUN_MODE=false           # Must explicitly disable
```

**Code Enforcement**:
```csharp
// Startup validation
var liveEnabled = bool.Parse(Environment.GetEnvironmentVariable("LIVE_TRADING_ENABLED") ?? "false");
var dryRunMode = bool.Parse(Environment.GetEnvironmentVariable("DRY_RUN_MODE") ?? "true");

if (liveEnabled && dryRunMode) {
    _logger.LogCritical("CONFIGURATION ERROR: LIVE_TRADING_ENABLED=true but DRY_RUN_MODE=true");
    throw new InvalidOperationException("Cannot enable live trading while in DRY_RUN mode");
}

if (!liveEnabled && !dryRunMode) {
    _logger.LogWarning("DRY_RUN_MODE=false but LIVE_TRADING_ENABLED=false - forcing DRY_RUN");
    dryRunMode = true; // Force safe default
}

_logger.LogInformation(
    "Trading mode: {Mode} (LIVE_TRADING_ENABLED={Live}, DRY_RUN_MODE={DryRun})",
    liveEnabled ? "LIVE" : "SIMULATION",
    liveEnabled,
    dryRunMode
);
```

### 5. Real Data Connection Validation
**File**: `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs`  
**Purpose**: Verify connection to TopstepX returns real market data, not simulation

**Connection Validation**:
```csharp
public async Task<bool> ValidateRealDataConnectionAsync() {
    try {
        _logger.LogInformation("Validating TopstepX connection for real data...");
        
        // 1. Check adapter mode
        if (_config.AdapterMode?.Equals("http", StringComparison.OrdinalIgnoreCase) ?? false) {
            _logger.LogWarning("HTTP mode detected - prefer PERSISTENT mode for production");
        }
        
        // 2. Request market data for ES futures
        var quote = await GetQuoteAsync("ES");
        
        // 3. Validate quote is real (not simulated)
        if (quote == null) {
            _logger.LogError("No quote returned - connection failed");
            return false;
        }
        
        if (quote.Bid == 0 || quote.Ask == 0) {
            _logger.LogError("Quote has zero bid/ask - likely simulation data");
            return false;
        }
        
        if (quote.Timestamp < DateTimeOffset.UtcNow.AddMinutes(-5)) {
            _logger.LogError("Quote timestamp is stale: {Timestamp}", quote.Timestamp);
            return false;
        }
        
        _logger.LogInformation(
            "Real data validated: ES @ {Bid}/{Ask} (timestamp: {Timestamp})",
            quote.Bid, quote.Ask, quote.Timestamp
        );
        
        return true;
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Failed to validate real data connection");
        return false;
    }
}
```

**Startup Validation**:
```csharp
// In Program.cs startup
if (liveEnabled) {
    var adapter = services.GetRequiredService<ITopstepXAdapterService>();
    var isRealData = await adapter.ValidateRealDataConnectionAsync();
    
    if (!isRealData) {
        _logger.LogCritical("LIVE trading enabled but connection validation FAILED");
        throw new InvalidOperationException("Cannot start live trading without real data connection");
    }
    
    _logger.LogInformation("✅ Real data connection validated - live trading authorized");
}
```

## 🚨 Production Pre-Flight Checklist

Before enabling live trading (`LIVE_TRADING_ENABLED=true`), verify:

### Environment Configuration
- [ ] `.env` copied from `.env.example` with real credentials
- [ ] `LIVE_TRADING_ENABLED=false` (default to simulation first)
- [ ] `DRY_RUN_MODE=true` (default to paper trading first)
- [ ] `TOPSTEPX_ADAPTER_MODE=persistent` (no HTTP ports)
- [ ] `MONITORING_PORT=8080` (health checks enabled)
- [ ] API credentials configured and validated
- [ ] No `kill.txt` file present (or remove before starting)

### Safety Systems Verification
```bash
# Run production guardrail tests
./test-production-guardrails.sh

# Expected output:
# ✅ ProductionKillSwitchService registered and monitoring
# ✅ Kill switch creates kill.txt when triggered
# ✅ DRY_RUN mode forced when kill.txt exists
# ✅ Order validation rejects invalid tick sizes
# ✅ Risk validation rejects risk ≤ 0
# ✅ Position sizing enforces account limits
# ✅ Real data connection validated
```

### Manual Kill Switch Test
```bash
# 1. Start bot in DRY_RUN mode
LIVE_TRADING_ENABLED=false DRY_RUN_MODE=true dotnet run --project src/UnifiedOrchestrator

# 2. Create kill.txt while bot running
echo "Test kill switch" > kill.txt

# 3. Verify logs show: "Kill switch activated - forcing DRY_RUN"
# 4. Verify all strategy signals blocked

# 5. Delete kill.txt
rm kill.txt

# 6. Restart bot (kill switch clears on restart)
```

### Order Validation Test
```bash
# Run order validation tests
dotnet test tests/OrderValidationTests.cs

# Expected results:
# ✅ ES price 4500.00 REJECTED (not on 0.25 tick)
# ✅ ES price 4500.25 ACCEPTED (valid tick)
# ✅ Risk ≤ 0 REJECTED
# ✅ Risk > 0 ACCEPTED
# ✅ Position size exceeding limits REJECTED
# ✅ Account equity below minimum REJECTED
```

### Real Data Connection Test
```bash
# Test TopstepX connection returns real data
dotnet run --project src/UnifiedOrchestrator -- --validate-connection

# Expected output:
# ✅ TopstepX adapter initialized (PERSISTENT mode)
# ✅ Market data connection established
# ✅ ES quote: 4525.25/4525.50 (timestamp: 2025-10-16 14:30:15 UTC)
# ✅ Real data validated - NOT simulation
```

### Risk Management Test
```bash
# Test position sizing calculations
dotnet test tests/RiskManagementTests.cs

# Expected results:
# ✅ $100,000 equity, 1% risk, $100 risk/contract → 10 contracts
# ✅ $50,000 equity, 2% risk, $100 risk/contract → 10 contracts
# ✅ Position capped at max contracts from config
# ✅ Risk per contract ≤ 0 → 0 contracts (reject trade)
```

## 📋 Live Trading Activation Procedure

**ONLY after all tests pass**, enable live trading:

### Step 1: Enable Live Mode
```bash
# Edit .env file
LIVE_TRADING_ENABLED=true
DRY_RUN_MODE=false
```

### Step 2: Final Validation
```bash
# Run complete validation suite
./validate-production-readiness.sh

# Expected output:
# ✅ Environment configuration valid
# ✅ All safety systems operational
# ✅ Real data connection validated
# ✅ Order validation tests passed
# ✅ Risk management tests passed
# ✅ No kill.txt file present
# ✅ READY FOR LIVE TRADING
```

### Step 3: Start Bot with Monitoring
```bash
# Start bot with full logging
dotnet run --project src/UnifiedOrchestrator 2>&1 | tee logs/live-trading-$(date +%Y%m%d-%H%M%S).log

# Expected startup logs:
# [INFO] Trading mode: LIVE (LIVE_TRADING_ENABLED=true, DRY_RUN_MODE=false)
# [INFO] ProductionKillSwitchService monitoring kill.txt every 5 seconds
# [INFO] TopstepX adapter initialized (PERSISTENT mode)
# [INFO] ✅ Real data connection validated - live trading authorized
# [INFO] OrderValidator enforcing tick size and risk validation
# [INFO] RiskManager using 1% account risk
# [INFO] All safety systems operational
# [INFO] 🚀 Live trading ACTIVE
```

### Step 4: Monitor First Trades
```bash
# Watch logs in real-time
tail -f logs/live-trading-*.log

# Key log patterns to watch:
# "Order submitted: ES LONG 1 @ 4525.25 (OrderId: ABC123)"
# "Order filled: ABC123 at 4525.25 (Expected: 4525.25)"
# "Position opened: ES LONG 1 @ 4525.25 (Stop: 4520.00, Risk: $262.50)"
```

## 🚨 Emergency Procedures

### Immediate Stop (Kill Switch)
```bash
# Create kill.txt to stop live trading IMMEDIATELY
echo "Emergency stop - $(date)" > kill.txt

# Verify DRY_RUN forced
tail -f logs/live-trading-*.log | grep "Kill switch activated"

# Expected log:
# [CRITICAL] Kill switch activated - forcing DRY_RUN mode
```

### Manual Position Close
```bash
# If positions need emergency close:
# 1. Create kill.txt (stops new orders)
# 2. Manually close positions via TopstepX web interface
# 3. Verify positions closed in bot logs
# 4. Investigate issue before resuming
```

### Connection Loss Recovery
```bash
# If TopstepX connection lost:
# 1. Bot automatically switches to DRY_RUN mode
# 2. Monitors existing positions but blocks new orders
# 3. Logs critical alert
# 4. Attempts reconnection every 30 seconds
# 5. Resumes live trading when connection restored

# Check connection status
curl http://localhost:8080/health

# Expected response:
# {
#   "status": "healthy",
#   "topstepx_connection": "connected",
#   "live_trading_enabled": true,
#   "dry_run_mode": false,
#   "kill_switch_active": false
# }
```

## 📊 Safety System Monitoring

### Health Check Endpoint
```bash
# Check bot health
curl http://localhost:8080/health | jq

# Response:
{
  "status": "healthy",
  "timestamp": "2025-10-16T14:30:00Z",
  "trading_mode": "LIVE",
  "live_trading_enabled": true,
  "dry_run_mode": false,
  "kill_switch_active": false,
  "topstepx_connection": "connected",
  "account_equity": 100000.00,
  "open_positions": 0,
  "daily_pnl": 0.00
}
```

### Metrics Endpoint (Prometheus)
```bash
# Get Prometheus metrics
curl http://localhost:8080/metrics

# Key metrics:
# trading_bot_orders_total{status="filled"} 42
# trading_bot_orders_total{status="rejected"} 3
# trading_bot_risk_validation_failures_total 5
# trading_bot_kill_switch_activations_total 1
# trading_bot_account_equity_dollars 100000.00
# trading_bot_daily_pnl_dollars 1250.00
```

### Daily Health Report
```bash
# Run automated daily health check
./daily-health-check.ps1

# Generates report:
# - Account equity trend
# - Win/loss ratio
# - Risk management compliance
# - Safety system activations
# - Connection uptime
```

## 🎯 Production Trading Standards

### Order Execution Requirements
1. **Price Validation**: Every order price rounded to valid tick size BEFORE submission
2. **Risk Validation**: Every trade validates risk > 0 using entry - stop calculation
3. **Order Evidence**: Every fill requires orderId + GatewayUserTrade event confirmation
4. **Position Tracking**: Position state updated ONLY after fill event received
5. **Stop Loss Required**: Every position has stop loss defined before entry order submitted

### Position Management Requirements
1. **Size Calculation**: Position size based on account equity and risk percentage (1% default)
2. **Risk Limits**: Never exceed max contracts per symbol from configuration
3. **Account Protection**: Check account equity before every position size calculation
4. **Emergency Exit**: Support immediate position close via kill switch activation
5. **State Persistence**: Position state persisted to disk for recovery after restart

### Connection Requirements
1. **Real Data Only**: Validate TopstepX returns real market data before live trading
2. **Connection Monitoring**: Monitor connection health every 30 seconds
3. **Automatic Recovery**: Attempt reconnection on connection loss, block new orders until restored
4. **DRY_RUN on Loss**: Force DRY_RUN mode if connection lost > 5 minutes
5. **Startup Validation**: Validate real data connection before accepting first order

### Logging Requirements
1. **Order Events**: Log every order submission, fill, rejection with full details
2. **Position Events**: Log every position open, close, stop hit with P&L
3. **Safety Events**: Log every kill switch activation, risk rejection, validation failure
4. **Performance**: Log order submission latency, fill latency, decision latency
5. **No Secrets**: Never log API keys, tokens, passwords, account numbers

Remember: **Trading safety protects capital. Every guardrail exists for a reason. Never bypass safety systems.**
