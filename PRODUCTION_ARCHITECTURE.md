# Production Trading Bot Architecture

## Overview

This document describes the complete production architecture of the autonomous trading bot, including all trading flows, background services, dependencies, and configuration requirements. This is the definitive reference for understanding how the system operates in production.

---

## Section 1: Production Trading Flow

### Complete Path from Market Data to Trade Execution

The trading bot follows a sophisticated multi-stage pipeline from market data ingestion to order execution:

#### 1.1 Market Data Ingestion
- **Source**: TopstepX Python SDK provides real-time market data via WebSocket connections
- **Symbols Monitored**: ES (E-mini S&P 500), MNQ (Micro E-mini Nasdaq), NQ (E-mini Nasdaq)
- **Data Types**: Real-time quotes (bid/ask), last trade, volume, order book depth
- **Frequency**: Streaming real-time data with millisecond precision

#### 1.2 Opportunity Detection (Every 30 Seconds)
**Service**: `AutonomousDecisionEngine` (HostedService running continuously)

The autonomous engine monitors the market every 30 seconds looking for trading opportunities:

```
Market Data → AutonomousDecisionEngine.ExecuteAsync()
  → RunAutonomousMainLoopAsync()
  → AnalyzeTradingOpportunities()
```

**Opportunity Detection Process**:
1. Fetch current market prices from TopstepX adapter
2. Check market hours via `IMarketHours` service
3. Evaluate TopStep compliance limits (daily loss, max drawdown)
4. Analyze market conditions (trending, ranging, volatile)
5. Calculate available capital and position sizing limits
6. Identify high-confidence trading opportunities

#### 1.3 Decision Routing
**Service**: `MasterDecisionOrchestrator` (routes decisions through ML/RL pipeline)

When opportunity detected, AutonomousDecisionEngine calls MasterDecisionOrchestrator:

```
Opportunity → MasterDecisionOrchestrator.MakeDecisionAsync()
  → EnhancedTradingBrain.ProcessDecision()
  → UnifiedTradingBrain.MakeDecision()
```

#### 1.4 Multi-Model Decision Making
**Service**: `UnifiedTradingBrain` (ensemble ML/RL decision system)

The Unified Brain coordinates multiple advanced models:

1. **Neural UCB Bandit** (`NeuralUcbBandit`)
   - Multi-armed bandit for strategy selection
   - Selects from strategies: S2, S3, S6, S7, S11
   - Balances exploration vs exploitation
   - Uses neural network for context-aware decisions

2. **CVaR-PPO Position Sizer** (`CvarPpoPositionSizer`)
   - Proximal Policy Optimization for position sizing
   - Conditional Value at Risk (CVaR) for tail risk management
   - Optimizes position size based on market regime
   - Ensures TopStep compliance limits

3. **LSTM Direction Predictor** (`LstmDirectionPredictor`)
   - Long Short-Term Memory network for price direction
   - Analyzes temporal patterns in market data
   - Provides confidence scores for buy/sell decisions
   - Considers multiple timeframes

4. **HMM Regime Detector** (`HmmRegimeDetector`)
   - Hidden Markov Model for market regime classification
   - Identifies: trending, ranging, volatile, low-volatility regimes
   - Adjusts strategy parameters based on regime
   - Provides regime confidence scores

**Decision Output**:
- Strategy: Which strategy to use (S2, S3, S6, S7, S11)
- Direction: Buy or Sell
- Confidence: 0.0 to 1.0 probability score
- Position Size: Dollar amount to risk
- Entry Price: Suggested entry level

#### 1.5 Risk Validation
**Service**: `AutonomousDecisionEngine.ValidateTradeRisk()`

Before placing any order, comprehensive risk validation occurs:

```csharp
// Six critical validation checks:
1. Risk > 0 (stop loss must limit losses)
2. Reward > 0 (take profit must secure profits)
3. R-multiple ≥ 1.0 (reward must equal or exceed risk)
4. Stop on correct side (below entry for buy, above for sell)
5. Target on correct side (above entry for buy, below for sell)
6. Price compliance (all prices rounded to 0.25 tick increments)
```

**Validation Failures**: Trade rejected with detailed error logging

#### 1.6 Bracket Price Calculation
**Service**: `AutonomousDecisionEngine.ExecuteTradeAsync()`

Calculate precise entry, stop loss, and take profit levels:

```csharp
const decimal tickSize = 0.25m;
const int stopTicks = 10;   // 10 ticks = 2.5 points
const int targetTicks = 15; // 15 ticks = 3.75 points

// For Buy orders:
stopLoss = currentPrice - (stopTicks * tickSize);    // 2.5 points below entry
takeProfit = currentPrice + (targetTicks * tickSize); // 3.75 points above entry

// For Sell orders:
stopLoss = currentPrice + (stopTicks * tickSize);    // 2.5 points above entry
takeProfit = currentPrice - (targetTicks * tickSize); // 3.75 points below entry

// Price rounding (ensures TopstepX compliance):
entry = PriceHelper.RoundToTick(currentPrice, symbol);
stopLoss = PriceHelper.RoundToTick(stopLoss, symbol);
takeProfit = PriceHelper.RoundToTick(takeProfit, symbol);
```

#### 1.7 Order Placement
**Service**: `TopstepXAdapterService` (interfaces with TopstepX Python SDK)

Final order placement to broker:

```
AutonomousDecisionEngine → TopstepXAdapterService.PlaceOrderAsync()
  → Python SDK → TopstepX API
  → Order submitted to CME exchange
```

**Order Details**:
- Symbol: ES, MNQ, or NQ
- Size: Positive for buy, negative for sell
- Entry: Market or limit order
- Stop Loss: Protective stop order
- Take Profit: Target limit order
- Order Type: Bracket order (parent + 2 child orders)

**Connection Checks**:
- Verifies TopstepX adapter is connected
- Checks connection health score > 80%
- Falls back gracefully if disconnected

#### 1.8 Fill Confirmation
**Service**: `TopstepXAdapterService` receives fill events

Order fill confirmation flow:

```
TopstepX API → Python SDK → TopstepXAdapterService
  → OrderExecutionResult with real order ID
  → AutonomousDecisionEngine receives confirmation
  → Position registration
```

**Fill Confirmation Contains**:
- Success: true/false
- OrderId: Real broker order ID
- ExecutedPrice: Actual fill price
- ExecutedSize: Number of contracts filled
- Timestamp: Execution time (UTC)
- Stop Loss: Confirmed stop price
- Take Profit: Confirmed target price

#### 1.9 Position Registration
**Service**: `UnifiedPositionManagementService`

Once order is filled, position enters active management:

```
OrderExecutionResult → UnifiedPositionManagementService.RegisterPosition()
  → Position tracking begins
  → Breakeven monitoring activated
  → Trailing stop monitoring activated
  → Time-based exit monitoring activated
```

#### 1.10 Continuous Position Monitoring
**Service**: `UnifiedPositionManagementService` (runs every 5 seconds)

Active position management loop:

```
Every 5 seconds:
  → Check each open position
  → Evaluate breakeven conditions
  → Evaluate trailing stop conditions
  → Evaluate time-based exit conditions
  → Evaluate regime change exit conditions
  → Update stop loss orders if needed
  → Close positions that meet exit criteria
```

**Position Management Features** (all enabled):
1. **Dynamic Targets**: Adjust profit targets based on market regime
2. **MAE Learning**: Optimize stop placement from maximum adverse excursion
3. **Regime Monitoring**: Exit when regime confidence drops below threshold
4. **Progressive Tightening**: Move stops closer as position becomes profitable
5. **Confidence Adjustment**: Adjust management based on ML confidence scores

#### 1.11 Position Close and Learning
**Service**: `MasterDecisionOrchestrator.RecordTradeOutcome()`

When position closes (hit stop, hit target, or manual exit):

```
Position Close → UnifiedPositionManagementService
  → TradeOutcome created
  → MasterDecisionOrchestrator.RecordTradeOutcome()
  → Update model performance metrics
  → Feed into reinforcement learning
  → Adjust strategy weights
  → Update exploration parameters
```

**Learning Metrics Tracked**:
- Win/Loss outcome
- Profit/Loss in dollars
- R-multiple achieved
- Maximum Adverse Excursion (MAE)
- Maximum Favorable Excursion (MFE)
- Time in trade
- Strategy used
- Market regime at entry/exit
- ML model confidence scores

---

## Section 2: HostedServices Overview

### Background Services Running 24/7

The bot runs multiple background services as `HostedService` instances:

#### 2.1 Primary Trading Services

**AutonomousDecisionEngine** (Critical)
- **Run Frequency**: Every 30 seconds
- **Purpose**: Main trading decision loop
- **Actions**: 
  - Monitors market for opportunities
  - Routes decisions through ML/RL pipeline
  - Validates risk and places orders
  - Tracks performance and learns from outcomes
- **Dependencies**: TopstepXAdapterService, MasterDecisionOrchestrator, UnifiedPositionManagementService

**MasterDecisionOrchestrator** (Critical)
- **Run Frequency**: Continuously (processes requests as they arrive)
- **Purpose**: Central decision routing and learning coordinator
- **Actions**:
  - Routes decisions through Enhanced Brain → Unified Brain
  - Manages model updates and learning cycles
  - Coordinates between multiple ML/RL models
  - Records trade outcomes for reinforcement learning
- **Dependencies**: UnifiedTradingBrain, NeuralUcbBandit, CvarPpoPositionSizer

**UnifiedPositionManagementService** (Critical)
- **Run Frequency**: Every 5 seconds
- **Purpose**: Active position management
- **Actions**:
  - Monitors all open positions
  - Implements breakeven, trailing, and time exits
  - Adjusts stops based on market conditions
  - Closes positions when exit criteria met
- **Dependencies**: TopstepXAdapterService, IMarketHours

**SessionEndPositionFlattener** (Critical)
- **Run Frequency**: Once per day at 4:55 PM ET
- **Purpose**: Close all positions before market close
- **Actions**:
  - Checks for any open positions at session end
  - Forces all positions closed to avoid overnight risk
  - Ensures compliance with intraday-only trading rules
- **Dependencies**: UnifiedPositionManagementService, TopstepXAdapterService

#### 2.2 Learning and Model Services

**CloudModelSynchronizationService**
- **Run Frequency**: Every 15 minutes
- **Purpose**: Download updated ML models from GitHub
- **Actions**:
  - Checks GitHub releases for new model versions
  - Downloads model files to local storage
  - Validates model checksums
  - Notifies ModelRotationService of new models
- **Dependencies**: GitHub API access

**ModelRotationService**
- **Run Frequency**: On-demand (triggered by new models)
- **Purpose**: Hot-swap ML models without downtime
- **Actions**:
  - Loads new model versions
  - Validates model compatibility
  - Switches active models atomically
  - Maintains rollback capability
- **Dependencies**: CloudModelSynchronizationService

**BacktestLearningService** (if enabled)
- **Run Frequency**: Every hour
- **Purpose**: Learn from historical data
- **Actions**:
  - Loads historical market data
  - Runs backtests with current strategies
  - Extracts patterns and insights
  - Updates model parameters
- **Dependencies**: Historical data storage

#### 2.3 Feature and Data Services

**ZoneFeaturePublisher**
- **Run Frequency**: Continuously (publishes on data arrival)
- **Purpose**: Calculate and publish zone-based features
- **Actions**:
  - Identifies support/resistance zones
  - Calculates zone strength and proximity
  - Publishes features to central message bus
- **Dependencies**: Market data streams

**S7FeaturePublisher**
- **Run Frequency**: Continuously
- **Purpose**: Calculate S7 strategy-specific features
- **Actions**:
  - Computes S7 indicator values
  - Publishes to feature store
- **Dependencies**: Market data, S7MarketDataBridge

**FeaturePublisher** (General)
- **Run Frequency**: Continuously
- **Purpose**: Calculate general trading features
- **Actions**:
  - Moving averages, RSI, MACD, etc.
  - Volume profiles
  - Volatility metrics
  - Price action patterns
- **Dependencies**: Market data streams

**ZoneMarketDataBridge**
- **Run Frequency**: Continuously
- **Purpose**: Bridge between market data and zone calculations
- **Dependencies**: TopstepXAdapterService

**S7MarketDataBridge**
- **Run Frequency**: Continuously
- **Purpose**: Bridge between market data and S7 features
- **Dependencies**: TopstepXAdapterService

#### 2.4 Monitoring and Health Services

**UnifiedOrchestratorService**
- **Run Frequency**: Every 1 second
- **Purpose**: System health monitoring and emergency control
- **Actions**:
  - Monitors memory usage
  - Checks TopstepX connection health
  - Triggers emergency shutdown if limits breached
  - **NOT the production trading path** (clarified in Phase 3)
- **Dependencies**: TopstepXAdapterService

**SystemHealthMonitoringService**
- **Run Frequency**: Every 30 seconds
- **Purpose**: Overall system health checks
- **Actions**:
  - Monitors CPU, memory, disk usage
  - Checks service availability
  - Logs health metrics
  - Sends alerts on degradation
- **Dependencies**: All major services

**ComponentHealthMonitoringService**
- **Run Frequency**: Every 60 seconds
- **Purpose**: Component-level health tracking
- **Actions**:
  - Checks each component's health status
  - Validates dependency chains
  - Reports component failures
- **Dependencies**: All registered components

**BotSelfAwarenessService**
- **Run Frequency**: Every 5 minutes (health check), every 60 minutes (status report)
- **Purpose**: Self-awareness and introspection
- **Actions**:
  - Monitors own performance metrics
  - Detects anomalies in behavior
  - Generates comprehensive status reports
  - Recommends optimizations
- **Dependencies**: All services

**MonitoringIntegrationService**
- **Run Frequency**: Continuously
- **Purpose**: Integration with external monitoring tools
- **Actions**:
  - Sends metrics to monitoring platforms
  - Forwards logs to centralized logging
  - Reports alerts and incidents
- **Dependencies**: External monitoring APIs

#### 2.5 Support Services

**JwtLifecycleManager**
- **Run Frequency**: Continuously (manages token refresh)
- **Purpose**: JWT token lifecycle management
- **Actions**:
  - Refreshes JWT tokens before expiration
  - Handles authentication failures
  - Maintains valid credentials
- **Dependencies**: TopstepX API

**LogRetentionService**
- **Run Frequency**: Daily at midnight
- **Purpose**: Log file management
- **Actions**:
  - Rotates log files
  - Compresses old logs
  - Deletes logs older than retention period
- **Dependencies**: File system

**ErrorHandlingService**
- **Run Frequency**: Continuously (handles errors as they occur)
- **Purpose**: Centralized error handling
- **Actions**:
  - Catches unhandled exceptions
  - Logs errors with context
  - Implements retry logic
  - Sends error notifications
- **Dependencies**: All services

**TopstepXIntegrationTestService**
- **Run Frequency**: On startup (if RUN_TOPSTEPX_TESTS=true)
- **Purpose**: Validate TopstepX integration
- **Actions**:
  - Tests connection to TopstepX
  - Validates API credentials
  - Checks order placement capability
  - Runs in paper trading mode only
- **Dependencies**: TopstepXAdapterService

#### 2.6 Additional Monitoring Services

**ZoneBreakMonitoringService**
- **Run Frequency**: Continuously
- **Purpose**: Monitor support/resistance zone breaks
- **Actions**:
  - Detects when price breaks key zones
  - Calculates break strength
  - Publishes break events
- **Dependencies**: ZoneFeaturePublisher

**PositionManagementOptimizer**
- **Run Frequency**: Every 15 minutes
- **Purpose**: Optimize position management parameters
- **Actions**:
  - Analyzes recent trade outcomes
  - Adjusts breakeven distances
  - Optimizes trailing stop settings
  - Tunes time-based exit parameters
- **Dependencies**: UnifiedPositionManagementService

---

## Section 3: Service Dependencies

### Dependency Hierarchy Tree

```
Legend: → depends on

Root Services (No dependencies):
├─ TopstepXAdapterService (Python SDK wrapper)
├─ IMarketHours (market hours calculator)
└─ ICentralMessageBus (event bus)

Level 1 (Depend on Root):
├─ JwtLifecycleManager → TopstepXAdapterService
├─ ZoneMarketDataBridge → TopstepXAdapterService
├─ S7MarketDataBridge → TopstepXAdapterService
├─ UnifiedOrchestratorService → TopstepXAdapterService
└─ TopstepXIntegrationTestService → TopstepXAdapterService

Level 2 (Depend on Level 1):
├─ ZoneFeaturePublisher → ZoneMarketDataBridge, ICentralMessageBus
├─ S7FeaturePublisher → S7MarketDataBridge, ICentralMessageBus
├─ FeaturePublisher → TopstepXAdapterService, ICentralMessageBus
└─ ZoneBreakMonitoringService → ZoneFeaturePublisher

Level 3 (ML/RL Models):
├─ NeuralUcbBandit → FeaturePublisher (consumes features)
├─ CvarPpoPositionSizer → FeaturePublisher
├─ LstmDirectionPredictor → FeaturePublisher
└─ HmmRegimeDetector → FeaturePublisher

Level 4 (Decision Making):
├─ UnifiedTradingBrain → NeuralUcbBandit, CvarPpoPositionSizer, LstmDirectionPredictor, HmmRegimeDetector
└─ EnhancedTradingBrain → UnifiedTradingBrain

Level 5 (Orchestration):
└─ MasterDecisionOrchestrator → EnhancedTradingBrain, UnifiedTradingBrain

Level 6 (Position Management):
├─ UnifiedPositionManagementService → TopstepXAdapterService, IMarketHours
├─ SessionEndPositionFlattener → UnifiedPositionManagementService, TopstepXAdapterService
└─ PositionManagementOptimizer → UnifiedPositionManagementService

Level 7 (Primary Trading Engine):
└─ AutonomousDecisionEngine → MasterDecisionOrchestrator, UnifiedPositionManagementService, TopstepXAdapterService, IMarketHours

Level 8 (Learning and Optimization):
├─ CloudModelSynchronizationService → GitHub API
├─ ModelRotationService → CloudModelSynchronizationService, All ML Models
└─ BacktestLearningService → MasterDecisionOrchestrator, Historical Data

Level 9 (Monitoring):
├─ SystemHealthMonitoringService → All Services
├─ ComponentHealthMonitoringService → All Services
├─ BotSelfAwarenessService → All Services
├─ MonitoringIntegrationService → External APIs
├─ ErrorHandlingService → All Services
└─ LogRetentionService → File System
```

### Critical Dependency Chains

**Trading Flow Chain** (highest priority):
```
Market Data → TopstepXAdapterService → AutonomousDecisionEngine 
  → MasterDecisionOrchestrator → UnifiedTradingBrain 
  → [NeuralUcbBandit, CvarPpoPositionSizer, LstmDirectionPredictor, HmmRegimeDetector]
  → AutonomousDecisionEngine → TopstepXAdapterService → Order Execution
```

**Position Management Chain**:
```
Order Fill → UnifiedPositionManagementService 
  → TopstepXAdapterService (for stop adjustments)
  → Position Close → MasterDecisionOrchestrator (learning)
```

**Learning Chain**:
```
Trade Outcome → MasterDecisionOrchestrator 
  → Model Updates → CloudModelSynchronizationService (upload to GitHub)
  → ModelRotationService → Production Models Updated
```

### Dependency Injection Registration Order

Services are registered in dependency order in `Program.cs`:

1. **Configuration and Options** (lines 100-200)
2. **Core Services** (TopstepXAdapterService, MessageBus) (lines 200-400)
3. **Market Data Bridges** (lines 400-500)
4. **Feature Publishers** (lines 500-600)
5. **ML/RL Models** (lines 600-800)
6. **Decision Orchestrators** (lines 800-900)
7. **Trading Engine** (AutonomousDecisionEngine) (lines 900-1000)
8. **Position Management** (lines 1000-1100)
9. **Monitoring Services** (lines 1100-1200)

---

## Section 4: Configuration Reference

### Critical Environment Variables

#### Required for Trading (Must be set)

```bash
# TopstepX API Credentials (REQUIRED)
PROJECT_X_API_KEY=<your_api_key>          # TopstepX API key (required for order placement)
PROJECT_X_USERNAME=<your_username>        # TopstepX account username

# Trading Mode (REQUIRED)
TRADING_MODE=DRY_RUN                      # DRY_RUN (paper), LIVE (real money)
PAPER_MODE=1                              # 1 = paper trading, 0 = live trading

# TopstepX Connection (REQUIRED)
ENABLE_TOPSTEPX=1                         # Must be 1 to place orders
TOPSTEPX_API_BASE=https://api.topstepx.com
TOPSTEPX_RTC_BASE=https://rtc.topstepx.com
RTC_USER_HUB=https://rtc.topstepx.com/hubs/user
RTC_MARKET_HUB=https://rtc.topstepx.com/hubs/market

# Contract Symbols (REQUIRED)
TOPSTEPX_EVAL_ES_ID=CON.F.US.EP.Z25      # ES contract ID (adjust for contract month)
TOPSTEPX_EVAL_NQ_ID=CON.F.US.ENQ.Z25     # NQ contract ID (adjust for contract month)
```

#### Optional - Position Management (Recommended)

```bash
# Position Management Features (All enabled by default)
BOT_DYNAMIC_TARGETS_ENABLED=true          # Adjust targets based on regime
BOT_MAE_LEARNING_ENABLED=true             # Learn optimal stop placement
BOT_REGIME_MONITORING_ENABLED=true        # Exit on regime changes
BOT_PROGRESSIVE_TIGHTENING_ENABLED=true   # Move stops as profit grows
BOT_CONFIDENCE_ADJUSTMENT_ENABLED=true    # Adjust based on ML confidence

# Position Management Parameters
BOT_REGIME_CHECK_INTERVAL_SECONDS=60      # How often to check regime (default: 60)
BOT_TARGET_ADJUSTMENT_THRESHOLD=0.3       # Regime confidence threshold (default: 0.3)
BOT_MAE_MINIMUM_SAMPLES=50                # Minimum trades for MAE learning (default: 50)
BOT_MAE_ANALYSIS_INTERVAL_MINUTES=5       # MAE analysis frequency (default: 5)
```

#### Optional - Learning Systems (Advanced)

```bash
# Cloud Learning (Recommended for multi-instance deployments)
CLOUD_PROVIDER=github                     # github, azure, aws, none
RL_ENABLED=1                              # Enable reinforcement learning
GITHUB_CLOUD_LEARNING=1                   # Sync models via GitHub

# GitHub Configuration (if CLOUD_PROVIDER=github)
GITHUB_OWNER=c-trading-bo                 # GitHub org/user
GITHUB_REPO=trading-bot-c-                # Repository name
GITHUB_TOKEN=<your_token>                 # GitHub PAT with repo access

# Auto-Learning Settings
AUTO_PROMOTION_ENABLED=1                  # Auto-promote better models
AUTO_LEARNING_ENABLED=1                   # Enable continuous learning
MIN_SHADOW_TEST_TRADES=50                 # Trades before promotion (default: 50)
MIN_SHADOW_TEST_SESSIONS=5                # Sessions before promotion (default: 5)
PROMOTION_CONFIDENCE_THRESHOLD=0.65       # Confidence for promotion (default: 0.65)
ROLLBACK_ON_PERFORMANCE_DECLINE=1         # Auto-rollback bad models

# Concurrent Learning
CONCURRENT_LEARNING_INTERVAL_MINUTES=60   # Learning cycle frequency (default: 60)
```

#### Optional - Monitoring and Safety (Highly Recommended)

```bash
# Emergency Controls
ENABLE_EMERGENCY_STOP=true                # Enable memory-based emergency stop
ENABLE_RESOURCE_MONITORING=true           # Monitor CPU/memory
MAX_MEMORY_USAGE_MB=2048                  # Memory limit for emergency stop (default: 2048)

# Health Monitoring
BOT_HEALTH_CHECK_INTERVAL_MINUTES=5       # Health check frequency (default: 5)
BOT_STATUS_REPORT_INTERVAL_MINUTES=60     # Status report frequency (default: 60)
BOT_SELF_AWARENESS_ENABLED=true           # Enable self-awareness system

# Alert Configuration
ALERT_EMAIL_ENABLED=false                 # Send email alerts (requires SMTP setup)
ALERT_SLACK_ENABLED=false                 # Send Slack alerts (requires webhook)
```

#### Optional - Development (For Testing Only)

```bash
# Development Flags (DO NOT USE IN PRODUCTION)
DRY_RUN=true                              # Alternative to TRADING_MODE=DRY_RUN
SKIP_LIVE_CONNECTION=0                    # Skip TopstepX connection (testing only)
DEMO_MODE=0                               # Demo mode (deprecated, use PAPER_MODE)
RUN_TOPSTEPX_TESTS=false                  # Run integration tests on startup

# Debugging
LOG_LEVEL=Information                     # Minimum: Information, Debug, Trace
ASPNETCORE_ENVIRONMENT=Production         # Production, Development, Staging
```

#### Optional - TopStep Compliance Limits

```bash
# TopStep Evaluation Account Limits (adjust per your account)
DAILY_LOSS_LIMIT=500                      # Max daily loss in dollars (default: 500)
MAX_POSITION_SIZE=5                       # Max contracts per trade (default: 5)
MAX_DAILY_TRADES=20                       # Max trades per day (default: 20)
TRAILING_DRAWDOWN_ENABLED=true            # Track trailing drawdown
```

#### Optional - Trading Time Windows

```bash
# Trading Hours (all default to true for 24/7 trading)
TRADE_DURING_LUNCH=true                   # Trade during lunch hour (11:30-1:00 ET)
TRADE_OVERNIGHT=true                      # Trade overnight session (6:00pm-9:30am ET)
TRADE_PREMARKET=true                      # Trade pre-market (8:30-9:30am ET)

# Auto-execution
ENABLE_AUTO_EXECUTION=true                # Allow auto-trade execution
ENABLE_LIVE_CONNECTION=true               # Maintain live TopstepX connection
```

### Configuration Validation

The bot performs startup validation to ensure all required configuration is present:

```
Startup Sequence:
1. Load environment variables from .env file
2. Validate required credentials (PROJECT_X_API_KEY, PROJECT_X_USERNAME)
3. Validate trading mode settings
4. Initialize TopstepX connection
5. Verify connection health
6. Load ML models
7. Start all HostedServices
8. Begin trading loop (if auto-execution enabled)
```

**Startup Failures** (bot will not start):
- Missing PROJECT_X_API_KEY or PROJECT_X_USERNAME
- Invalid TopstepX credentials
- Cannot connect to TopstepX API
- ML models missing or corrupted
- Memory limits too low for operation

**Startup Warnings** (bot will start with limitations):
- GitHub token not set (cloud sync disabled)
- Monitoring services unavailable
- Optional features disabled

---

## Section 5: Production Safety Mechanisms

### Built-in Safety Features

#### 5.1 Price Validation
- **PriceHelper.RoundToTick()**: All prices rounded to 0.25 tick increments
- **Prevents**: Order rejections from invalid tick sizes
- **Location**: Integrated in ExecuteTradeAsync and PlaceOrderAsync

#### 5.2 Risk Validation
- **ValidateTradeRisk()**: Six-point validation before every trade
- **Checks**: Risk > 0, Reward > 0, R-multiple ≥ 1.0, Stop/target correct sides
- **Prevents**: Trades with negative risk or unfavorable risk/reward
- **Location**: AutonomousDecisionEngine.ExecuteTradeAsync

#### 5.3 Connection Health
- **Health Score Monitoring**: Requires >80% health before orders
- **Connection Checks**: Validates adapter connected before placing orders
- **Graceful Degradation**: Falls back to simulation if disconnected
- **Location**: TopstepXAdapterService, AutonomousDecisionEngine

#### 5.4 TopStep Compliance
- **TopStepComplianceManager**: Enforces daily loss limits, max drawdown
- **Daily P&L Tracking**: Monitors cumulative profit/loss
- **Position Size Limits**: Enforces max contracts per trade
- **Trading Restrictions**: Prevents trading when limits breached
- **Location**: AutonomousDecisionEngine

#### 5.5 Emergency Stop
- **Memory-based**: Triggers shutdown if memory exceeds limit
- **Manual Kill Switch**: Create kill.txt file to force DRY_RUN mode
- **Session End Flattening**: Closes all positions at 4:55 PM ET
- **Location**: UnifiedOrchestratorService, SessionEndPositionFlattener

#### 5.6 Order Evidence Requirements
- **Order ID Verification**: Requires real order ID from broker
- **Fill Event Confirmation**: Waits for fill event before position registration
- **No Fake Orders**: Cannot claim fills without broker confirmation
- **Location**: TopstepXAdapterService, AutonomousDecisionEngine

---

## Section 6: Production Deployment Checklist

### Pre-Deployment Verification

- [ ] TopstepX account funded and active
- [ ] API credentials set in environment (PROJECT_X_API_KEY, PROJECT_X_USERNAME)
- [ ] Trading mode set to PAPER_MODE=1 for initial testing
- [ ] All position management features enabled
- [ ] Emergency stop and resource monitoring enabled
- [ ] Health check intervals configured
- [ ] ML models downloaded and validated
- [ ] TopstepX Python SDK installed and tested
- [ ] .NET 8.0 SDK installed
- [ ] Build succeeds with `dotnet build`
- [ ] All analyzer warnings reviewed (should be ~5,763 baseline)

### Deployment Steps

1. **Clone Repository**
   ```bash
   git clone https://github.com/c-trading-bo/trading-bot-c-.git
   cd trading-bot-c-
   ```

2. **Configure Environment**
   ```bash
   cp .env.example .env
   # Edit .env with your credentials and settings
   ```

3. **Install Dependencies**
   ```bash
   dotnet restore
   pip install -r requirements.txt  # For Python SDK
   ```

4. **Build Project**
   ```bash
   dotnet build
   ```

5. **Test Paper Trading**
   ```bash
   export PAPER_MODE=1
   export TRADING_MODE=DRY_RUN
   dotnet run --project src/UnifiedOrchestrator
   ```

6. **Monitor Logs**
   - Watch for "Real order executed" messages
   - Verify orders appear in TopstepX paper account
   - Confirm position management working correctly
   - Check health monitoring reports

7. **Enable Live Trading** (only after extensive paper trading)
   ```bash
   export PAPER_MODE=0
   export TRADING_MODE=LIVE
   # Restart bot
   ```

### Post-Deployment Monitoring

- Monitor daily P&L vs TopStep limits
- Watch for connection health degradation
- Review ML model performance metrics
- Check for memory leaks or resource issues
- Validate position management adjustments
- Review trade outcomes and learning progress
- Monitor emergency stop triggers
- Verify session end position flattening

---

## Section 7: Troubleshooting Common Issues

### Issue: Orders Not Placing

**Symptoms**: Bot detects opportunities but no orders appear in TopstepX

**Causes**:
1. TopstepX adapter not connected
2. Credentials invalid or expired
3. Risk validation failing
4. Trading mode set to DRY_RUN without TopstepX connection

**Solutions**:
1. Check logs for "TopstepX adapter not available" warnings
2. Verify PROJECT_X_API_KEY and PROJECT_X_USERNAME are set
3. Review risk validation error messages
4. Ensure ENABLE_TOPSTEPX=1 and connection health >80%

### Issue: Positions Not Closing at Session End

**Symptoms**: Open positions remain after 4:55 PM ET

**Causes**:
1. SessionEndPositionFlattener not running
2. Market hours service incorrectly configured
3. TopstepX connection lost before session end

**Solutions**:
1. Check SessionEndPositionFlattener is registered as HostedService
2. Verify IMarketHours service detecting correct session end time
3. Ensure TopstepX connection maintained until 5:00 PM ET

### Issue: Memory Usage Growing

**Symptoms**: Memory usage increases over time, hits limits

**Causes**:
1. ML models not releasing resources
2. Position tracking accumulating data
3. Log files growing without rotation

**Solutions**:
1. Enable ENABLE_RESOURCE_MONITORING=true
2. Set appropriate MAX_MEMORY_USAGE_MB limit
3. Enable LogRetentionService for log rotation
4. Monitor for memory leaks with profiler

### Issue: ML Models Not Updating

**Symptoms**: Models never change, no learning observed

**Causes**:
1. CloudModelSynchronizationService not running
2. GitHub credentials missing or invalid
3. Network connectivity to GitHub blocked
4. Model validation failing

**Solutions**:
1. Verify CLOUD_PROVIDER=github and GITHUB_CLOUD_LEARNING=1
2. Set GITHUB_TOKEN with repo access
3. Check firewall allows GitHub API access
4. Review model validation error logs

---

## Section 8: Performance Metrics

### Key Performance Indicators (KPIs)

**Trading Performance**:
- Daily P&L ($): Profit/loss for the day
- Win Rate (%): Percentage of profitable trades
- Average R-Multiple: Average reward-to-risk ratio achieved
- Sharpe Ratio: Risk-adjusted returns
- Max Drawdown ($): Largest peak-to-trough decline

**System Performance**:
- Order Placement Latency (ms): Time from decision to order submission
- Fill Confirmation Latency (ms): Time from order submission to fill confirmation
- Decision Loop Time (ms): Time to complete one decision cycle
- Memory Usage (MB): Current RAM consumption
- CPU Usage (%): Processor utilization

**ML Model Performance**:
- Strategy Win Rates: Win rate per strategy (S2, S3, S6, S7, S11)
- Model Confidence Accuracy: How often high-confidence trades win
- Regime Detection Accuracy: Correct regime identification rate
- Position Size Optimization: Optimal vs actual position sizes

### Monitoring Dashboards

Metrics are available via:
1. **Console Logs**: Real-time logging to stdout
2. **BotSelfAwarenessService**: Hourly status reports
3. **SystemHealthMonitoringService**: Health metrics every 30s
4. **MonitoringIntegrationService**: External monitoring platforms

---

## Conclusion

This production architecture represents a sophisticated, multi-layered trading system with robust safety mechanisms, continuous learning, and comprehensive monitoring. The system is designed for 24/7 autonomous operation with minimal human intervention while maintaining strict risk controls and compliance requirements.

**Key Architectural Principles**:
- **Safety First**: Multiple validation layers prevent unsafe trades
- **Continuous Learning**: RL models improve from every trade
- **Graceful Degradation**: System remains operational when components fail
- **Comprehensive Monitoring**: All aspects tracked and reported
- **Production Ready**: Battle-tested with real trading requirements

For questions or issues, refer to the specific section above or consult the inline code documentation in the relevant service files.
