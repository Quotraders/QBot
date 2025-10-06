# Canary Monitoring Flow Diagram

## Overview
This document illustrates the complete flow of the canary monitoring system.

## System Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    MODEL UPDATE TRIGGERED                           │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STEP 1: CAPTURE BASELINE METRICS                                   │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ CaptureCurrentMetricsAsync()                                  │  │
│  │ • Query last 60 minutes of trades                            │  │
│  │ • Calculate: Win Rate, Daily PnL, Sharpe, Drawdown          │  │
│  │ • Store in baseline metrics dictionary                       │  │
│  └───────────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STEP 2: BACKUP CURRENT ARTIFACTS                                   │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ BackupCurrentArtifactsAsync()                                │  │
│  │ • Create timestamped folder: backup_YYYYMMDD_HHMMSS          │  │
│  │ • Copy parameter JSON files                                  │  │
│  │ • Copy ONNX model files                                      │  │
│  │ • Generate manifest with file list                           │  │
│  │ • Store backup path                                          │  │
│  └───────────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STEP 3: APPLY MODEL UPDATE                                         │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ _learningManager.CheckAndUpdateModelsAsync()                 │  │
│  │ • Update ONNX models                                         │  │
│  │ • Update parameters                                          │  │
│  │ • Deploy new configuration                                   │  │
│  └───────────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STEP 4: START CANARY MONITORING                                    │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ StartCanaryMonitoring(baselineMetrics)                       │  │
│  │ • Set _isCanaryActive = true                                 │  │
│  │ • Record _canaryStartTime = now                              │  │
│  │ • Store baseline metrics                                     │  │
│  │ • Clear canary trades list                                   │  │
│  │ • Log monitoring started                                     │  │
│  └───────────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│              CANARY MONITORING PERIOD (60-90 minutes)               │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │  TRADING LOOP - For Each Completed Trade:                   │  │
│  │  ┌───────────────────────────────────────────────────────┐  │  │
│  │  │ TrackTradeResult(symbol, strategy, pnl, outcome)      │  │  │
│  │  │ • Check if canary active                              │  │  │
│  │  │ • Create CanaryTradeRecord                            │  │  │
│  │  │ • Add to _canaryTrades list                           │  │  │
│  │  │ • Increment counter                                   │  │  │
│  │  │ • Log trade captured                                  │  │  │
│  │  └───────────────────────────────────────────────────────┘  │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │  ORCHESTRATION LOOP - Every Cycle (~10 seconds):            │  │
│  │  ┌───────────────────────────────────────────────────────┐  │  │
│  │  │ CheckCanaryMetricsAsync()                             │  │  │
│  │  │                                                        │  │  │
│  │  │ IF elapsed < 60 min AND trades < 50:                  │  │  │
│  │  │   → Continue monitoring                               │  │  │
│  │  │                                                        │  │  │
│  │  │ ELSE:                                                  │  │  │
│  │  │   → Calculate current metrics                         │  │  │
│  │  │   → Compare to baseline                               │  │  │
│  │  │   → Check rollback triggers                           │  │  │
│  │  └───────────────────────────────────────────────────────┘  │  │
│  └─────────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                    ┌────────┴────────┐
                    │                 │
                    ▼                 ▼
        ┌────────────────┐  ┌────────────────┐
        │  TRIGGERS OK?  │  │ TRIGGERS FIRE? │
        └────────┬───────┘  └────────┬───────┘
                 │                   │
                 ▼                   ▼
┌─────────────────────────┐ ┌─────────────────────────────────────────┐
│  CANARY SUCCESS ✅      │ │  EXECUTE ROLLBACK 🚨                    │
│  ┌───────────────────┐  │ │  ┌───────────────────────────────────┐ │
│  │ Set canary = off │  │ │  │ ExecuteRollbackAsync()             │ │
│  │ Log success      │  │ │  │ • Log urgent alert                 │ │
│  │ Continue trading │  │ │  │ • Load backup from saved path      │ │
│  └───────────────────┘  │ │  │ • Atomic copy (temp → current)    │ │
└─────────────────────────┘ │  │ • Restore ONNX models             │ │
                            │  │ • Disable auto-promotion           │ │
                            │  │ • Check catastrophic conditions    │ │
                            │  │ • Create kill.txt if catastrophic  │ │
                            │  │ • Log all actions                  │ │
                            │  │ • Set canary = off                 │ │
                            │  └───────────────────────────────────┘ │
                            └─────────────────────────────────────────┘
```

## Rollback Triggers

### Trigger 1: Performance Degradation
```
IF (Win Rate Drop > 15%) AND (Drawdown Increase > $500)
   → ROLLBACK
```

**Example:**
- Baseline: 60% win rate, $200 drawdown
- Current: 40% win rate, $750 drawdown
- Drop: 20% (>15%), Increase: $550 (>$500)
- Result: ✅ TRIGGER FIRES → ROLLBACK

### Trigger 2: Sharpe Ratio Collapse
```
IF (Sharpe Ratio Drop > 30%)
   → ROLLBACK
```

**Example:**
- Baseline: Sharpe ratio 1.5
- Current: Sharpe ratio 0.8
- Drop: 46.7% (>30%)
- Result: ✅ TRIGGER FIRES → ROLLBACK

## Catastrophic Failure Detection

```
IF (Win Rate < 30%) OR (Drawdown > $1000)
   → CREATE kill.txt
   → FORCE DRY_RUN MODE
   → ALERT CRITICAL
```

## Atomic Rollback Process

```
1. Create Temp Directory
   artifacts/rollback_temp_<timestamp>/

2. Copy Backup → Temp
   backup/ → rollback_temp/

3. Delete Current (Atomic)
   DELETE artifacts/current/

4. Move Temp → Current (Atomic)
   MOVE rollback_temp/ → current/

5. Restore Models
   FOR EACH *.onnx IN backup/models/
      COPY → current/models/
```

## File Structure

```
artifacts/
├── current/                    # Active configuration
│   ├── parameters.json
│   ├── config.json
│   └── models/
│       ├── model_v1.onnx
│       └── model_v2.onnx
│
├── backup/                     # Previous version (for quick rollback)
│   ├── parameters.json
│   ├── config.json
│   └── models/
│
└── backups/                    # Timestamped backups
    ├── backup_20241206_120530/
    │   ├── manifest.json       # File inventory
    │   ├── parameters.json
    │   └── models/
    │
    └── backup_20241206_150245/
        ├── manifest.json
        ├── parameters.json
        └── models/
```

## Monitoring Timeline

```
T=0min    Model Update Applied
          ↓
          Baseline Captured: WR=60%, Sharpe=1.5, DD=$200
          ↓
          Backup Created: backup_20241206_150245/
          ↓
          Canary Monitoring Started
          
T=5min    Trade 1: WIN  ($50)   [10 trades, keep monitoring]
T=10min   Trade 2: LOSS (-$25)  [20 trades, keep monitoring]
T=15min   Trade 3: WIN  ($75)   [30 trades, keep monitoring]
...
T=60min   ✅ Threshold Met: 50 trades OR 60 minutes
          ↓
          Calculate Current: WR=58%, Sharpe=1.4, DD=$250
          ↓
          Compare to Baseline:
            - WR drop: 2% (<15%) ✅
            - DD increase: $50 (<$500) ✅
            - Sharpe drop: 6.7% (<30%) ✅
          ↓
          ✅ CANARY SUCCESS - No rollback needed
          
          Monitoring Complete ✅
```

## Error Handling

```
TRY
  Execute Rollback
CATCH IOException
  Log error
  Alert: "Rollback failed: I/O error"
  Continue monitoring
CATCH UnauthorizedAccessException
  Log error
  Alert: "Rollback failed: Access denied"
  Continue monitoring
FINALLY
  Set _isCanaryActive = false
```

## Configuration Matrix

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `GATE5_ENABLED` | `true` | Enable canary monitoring |
| `GATE5_MIN_TRADES` | `50` | Minimum trades before evaluation |
| `GATE5_MIN_MINUTES` | `60` | Minimum time before evaluation |
| `GATE5_MAX_MINUTES` | `90` | Maximum monitoring window |
| `GATE5_WIN_RATE_DROP_THRESHOLD` | `0.15` | 15% win rate drop triggers rollback |
| `GATE5_MAX_DRAWDOWN_DOLLARS` | `500` | $500 drawdown triggers rollback |
| `GATE5_SHARPE_DROP_THRESHOLD` | `0.30` | 30% Sharpe drop triggers rollback |
| `GATE5_CATASTROPHIC_WIN_RATE` | `0.30` | <30% win rate creates kill.txt |
| `GATE5_CATASTROPHIC_DRAWDOWN_DOLLARS` | `1000` | >$1000 drawdown creates kill.txt |

## Log Output Examples

### Canary Start
```
🕊️ [CANARY] Monitoring started with baseline - WinRate: 60.00%, Sharpe: 1.50, Drawdown: $200.00, DailyPnL: $1500.00
```

### Trade Tracking
```
🕊️ [CANARY] Trade captured - ES S11 PnL: $50.00 (15 trades tracked)
```

### Metrics Check
```
📊 [CANARY] Metrics - Trades: 52, WinRate: 58.00%, Sharpe: 1.40, Drawdown: $250.00
```

### Rollback Trigger
```
🚨 [CANARY] Rollback triggers fired - Trigger1: True, Trigger2: False
🚨🚨🚨 [ROLLBACK] URGENT: Triggering automatic rollback at 2024-12-06 15:30:45
📊 [ROLLBACK] Current Metrics - WinRate: 40.00%, Sharpe: 0.80, Drawdown: $750.00
📊 [ROLLBACK] Baseline Metrics - WinRate: 60.00%, Sharpe: 1.50, Drawdown: $200.00
```

### Rollback Success
```
✅ [ROLLBACK] Parameters rolled back successfully
📦 [ROLLBACK] Restored ONNX model: model_v1.onnx
🔒 [ROLLBACK] Auto-promotion disabled - manual review required
📝 [ROLLBACK] Actions completed at 2024-12-06 15:31:02:
  ✓ Backup parameters loaded from: artifacts/backups/backup_20241206_150245
  ✓ Parameters copied atomically to: artifacts/current
  ✓ ONNX models restored (if existed)
  ✓ Auto-promotion disabled
  ✓ High priority alert sent
```

### Catastrophic Failure
```
💥 [ROLLBACK] CATASTROPHIC FAILURE DETECTED - Creating kill.txt
🚨 [GATE-5] kill.txt created at /home/runner/work/trading-bot-c-/trading-bot-c-/kill.txt
💥 [CRITICAL-ALERT] CATASTROPHIC FAILURE
📧 [CRITICAL-ALERT] Win Rate: 25.00%, Drawdown: $1200.00
```

## Summary

The canary monitoring system provides:
- ✅ Automatic pre-upgrade backups
- ✅ Real-time performance tracking
- ✅ Intelligent dual-trigger rollback
- ✅ Catastrophic failure protection
- ✅ Complete audit trail
- ✅ Production-ready safety mechanisms
