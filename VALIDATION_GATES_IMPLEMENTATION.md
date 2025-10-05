# Validation Gates Implementation

This document describes the three production-grade validation gates implemented to ensure safe deployment of optimized parameters, cloud models, and S15 RL strategy.

## Gate 1: Parameter Optimization Validation

**File**: `src/Training/parameter_optimization_validator.py`
**Purpose**: Validates optimized parameters before production deployment

### Validation Checks

1. **Parameter Bounds Validation**
   - Ensures all parameters fall within safe predefined bounds
   - Strategy-specific bounds for S2, S3, S6, S11

2. **Minimum Trade Count**
   - Requires ≥200 trades in holdout period
   - Ensures statistical reliability

3. **Performance Improvements**
   - Win rate: +2 percentage points minimum
   - Sharpe ratio: +0.10 minimum improvement

4. **Statistical Significance**
   - Bootstrap test with 10,000 iterations
   - p-value < 0.05 required
   - Proves improvement is not random luck

### Thresholds

```python
MIN_TRADES_THRESHOLD = 200
WIN_RATE_IMPROVEMENT_THRESHOLD = 0.02  # 2%
SHARPE_IMPROVEMENT_THRESHOLD = 0.10
BOOTSTRAP_P_VALUE_THRESHOLD = 0.05
```

### Integration

Integrated into `training_orchestrator.py` in the `train_single_strategy()` function. Runs after basic validation and before saving parameters.

### Manifest Creation

Creates `{strategy}_parameters_manifest.json` with:
- Parameter values and SHA256 hash
- Validation metrics (baseline vs candidate)
- Holdout period details
- Timestamp and version info

## Gate 2: Cloud ONNX Model Download Validation

**File**: `src/BotCore/Services/CloudModelDownloader.cs`
**Purpose**: Validates ONNX models before deploying to production

### Validation Checks

1. **Download to Staging**
   - Downloads model to temporary location
   - Never downloads directly to live directory

2. **SHA256 Hash Verification**
   - Verifies file integrity against manifest
   - Detects corruption or tampering

3. **ONNX Compatibility Check**
   - Validates opset version
   - Checks input/output tensor shapes
   - Ensures feature specification compatibility

4. **Prediction Distribution Comparison**
   - Loads 200+ validation vectors
   - Compares predictions from live vs candidate model
   - Calculates KL divergence
   - Rejects if divergence > 0.25

5. **Historical Data Validation**
   - Runs 500+ historical decisions through both models
   - Calculates validation loss and Sharpe ratio
   - Requires 5% loss improvement OR 3% Sharpe improvement

6. **Simulation Safety Check**
   - Runs 5000-bar deterministic simulation
   - Tracks drawdown for both models
   - Rejects if candidate drawdown > 2x baseline

7. **Safe Deployment**
   - Backs up current live model
   - Moves candidate from staging to live
   - Cleans up staging files

### Thresholds

```csharp
MIN_VALIDATION_SAMPLES = 500
MIN_SANITY_TEST_VECTORS = 200
MAX_KL_DIVERGENCE = 0.25
MIN_LOSS_IMPROVEMENT = 0.05  // 5%
MIN_SHARPE_IMPROVEMENT = 0.03  // 3%
MAX_DRAWDOWN_RATIO = 2.0  // 2x baseline
```

### Registration

Registered in `src/UnifiedOrchestrator/Program.cs` as:
```csharp
services.AddSingleton<ICloudModelDownloader, CloudModelDownloader>();
```

## Gate 3: S15 Shadow Learning Promotion Validation

**File**: `src/BotCore/Services/S15ShadowLearningService.cs`
**Purpose**: Validates S15 RL strategy before promoting from shadow to live

### Validation Checks

### How It Works

1. **Shadow Observation**
   - Hooks into decision pipeline after S7 approval
   - Records what S15 recommends vs what S2/S3/S6/S11 do
   - Tracks hypothetical outcomes for S15
   - Tracks actual outcomes for baseline strategies

2. **Accumulation Phase**
   - Collects 1000 shadow decisions (or 500 for thin markets)
   - Only counts decisions where S7 allowed trading
   - Ensures apples-to-apples comparison

3. **Performance Validation**
   - Calculates S15 metrics: win rate, Sharpe, P&L
   - Calculates baseline metrics for comparison
   - Validates against absolute and relative thresholds

4. **Promotion Decision**
   - If all checks pass: promote to canary with 5% traffic
   - If any check fails: reset counter and continue learning
   - Writes promotion config to `config/s15_promotion.json`

### Validation Checks

1. **Absolute Sharpe Requirement**
   - S15 Sharpe ≥ 2.0 (absolute minimum)

2. **Win Rate Requirement**
   - S15 win rate ≥ 50%

3. **Relative Performance**
   - S15 Sharpe ≥ 1.20x baseline (20% better)
   - For thin markets with <1000 decisions: ≥ 1.30x (30% better)

4. **Statistical Significance**
   - Bootstrap test comparing P&L distributions
   - p-value < 0.05 required

### Thresholds

```csharp
MIN_SHADOW_DECISIONS = 1000  // 500 for thin markets
MIN_SHARPE_RATIO = 2.0
MIN_WIN_RATE = 0.50  // 50%
MIN_SHARPE_MULTIPLIER = 1.20  // 20% better
MIN_SHARPE_MULTIPLIER_THIN = 1.30  // 30% for thin markets
BOOTSTRAP_P_VALUE_THRESHOLD = 0.05
CANARY_TRAFFIC_PERCENTAGE = 0.05  // 5%
```

### Registration

Registered in `src/UnifiedOrchestrator/Program.cs` as:
```csharp
services.AddSingleton<S15ShadowLearningService>();
services.AddHostedService<S15ShadowLearningService>(provider => 
    provider.GetRequiredService<S15ShadowLearningService>());
```

## Gate 4: UnifiedTradingBrain Model Reload Safety

**File**: `src/BotCore/Brain/UnifiedTradingBrain.cs` (ValidateModelForReloadAsync method)
**Purpose**: Validates ONNX models before hot-reloading into production brain

### Validation Checks

1. **Feature Specification Compatibility**
   - Loads feature spec from `config/feature_specification.json`
   - Verifies model expects same number of features
   - Checks feature names, types, and ranges match
   - Aborts reload if any mismatch detected

2. **Sanity Test with Deterministic Dataset**
   - Loads or generates 200 deterministic feature vectors
   - Cached in `data/validation/sanity_test_vectors.json`
   - Uses fixed random seed (42) for reproducibility
   - Vectors cover normal trading feature ranges

3. **Prediction Distribution Comparison**
   - Runs inference through both current and new model
   - Calculates total variation distance between predictions
   - Calculates KL divergence as secondary check
   - Rejects if divergence indicates unstable learning

4. **NaN/Infinity Validation**
   - Runs model on all sanity test vectors
   - Checks every output for NaN or Infinity values
   - Rejects model if any invalid outputs detected
   - Ensures model stability and numerical robustness

### Thresholds

```csharp
SANITY_TEST_VECTORS = 200
MAX_TOTAL_VARIATION = 0.20  // 20% threshold
MAX_KL_DIVERGENCE = 0.25
FAIL_ON_NAN_INFINITY = true
```

### Integration

Called by `ModelHotReloadManager` before atomic model swap:

```csharp
// In ProcessHotReloadAsync()
var (isValid, reason) = await _unifiedBrain.ValidateModelForReloadAsync(
    newModelPath, currentModelPath, cancellationToken);
    
if (!isValid)
{
    _logger.LogError("Gate 4 validation failed: {Reason}", reason);
    // Abort reload, keep current model
    return;
}
```

Also integrated into existing smoke test flow in `ModelHotReloadManager.cs`.

### Fail-Safe Behavior

- Model file validated for existence and non-zero size
- Feature specification created if missing (default 11 features for CVaR-PPO)
- Current model path checked - skips comparison if first deployment
- Any validation failure aborts reload immediately
- Current in-memory model remains unchanged
- Detailed failure reason logged for debugging

### Feature Specification Format

```json
{
  "version": "1.0",
  "feature_count": 11,
  "features": [
    { "name": "volatility_normalized", "type": "float", "range": [-1.0, 1.0] },
    { "name": "price_change_momentum", "type": "float", "range": [-1.0, 1.0] },
    { "name": "volume_surge", "type": "float", "range": [-1.0, 1.0] },
    { "name": "atr_normalized", "type": "float", "range": [-1.0, 1.0] },
    { "name": "ucb_value", "type": "float", "range": [-1.0, 1.0] },
    { "name": "strategy_encoding", "type": "float", "range": [0.0, 1.0] },
    { "name": "direction_encoding", "type": "float", "range": [-1.0, 1.0] },
    { "name": "time_of_day_sin", "type": "float", "range": [-1.0, 1.0] },
    { "name": "time_of_day_cos", "type": "float", "range": [-1.0, 1.0] },
    { "name": "decisions_per_day_normalized", "type": "float", "range": [-1.0, 1.0] },
    { "name": "risk_metric", "type": "float", "range": [-1.0, 1.0] }
  ]
}
```

## Environment Variables

All validation thresholds are configurable via `.env`:

### Gate 1 Variables
```bash
PARAM_OPT_MIN_TRADES=200
PARAM_OPT_WIN_RATE_IMPROVEMENT=0.02
PARAM_OPT_SHARPE_IMPROVEMENT=0.10
PARAM_OPT_BOOTSTRAP_P_VALUE=0.05
```

### Gate 2 Variables
```bash
CLOUD_MODEL_ENDPOINT=https://api.github.com/repos/...
CLOUD_MODEL_MIN_VALIDATION_SAMPLES=500
CLOUD_MODEL_MAX_KL_DIVERGENCE=0.25
CLOUD_MODEL_MIN_LOSS_IMPROVEMENT=0.05
CLOUD_MODEL_MIN_SHARPE_IMPROVEMENT=0.03
```

### Gate 3 Variables
```bash
S15_MIN_SHADOW_DECISIONS=1000
S15_MIN_SHARPE_RATIO=2.0
S15_MIN_WIN_RATE=0.50
S15_MIN_SHARPE_MULTIPLIER=1.20
S15_BOOTSTRAP_P_VALUE_THRESHOLD=0.05
```

### Gate 4 Variables
```bash
GATE4_SANITY_TEST_VECTORS=200
GATE4_MAX_TOTAL_VARIATION=0.20
GATE4_MAX_KL_DIVERGENCE=0.25
GATE4_FAIL_ON_NAN_INFINITY=true
```

## Testing & Verification

### Gate 4: Model Reload Safety
Runs automatically when ModelHotReloadManager detects new model file.
Check logs for validation results:
```
=== GATE 4: UNIFIED TRADING BRAIN MODEL RELOAD VALIDATION ===
[1/4] Validating feature specification compatibility...
  ✓ Feature specification matches
[2/4] Running sanity tests with deterministic dataset...
  Loaded 200 sanity test vectors
[3/4] Comparing prediction distributions...
  ✓ Distribution divergence acceptable: 0.0523
[4/4] Validating model outputs for NaN/Infinity...
  ✓ All outputs valid (no NaN/Infinity)
=== GATE 4 PASSED - Model validated for hot-reload ===
```

Check generated artifacts:
```bash
cat config/feature_specification.json
cat data/validation/sanity_test_vectors.json
```

### Gate 1: Parameter Optimization
Run weekly via Saturday 2 AM scheduler:
```bash
cd src/Training
python training_orchestrator.py
```

Check manifest files in `artifacts/stage/parameters/`:
- `{strategy}_parameters_manifest.json` - validation details
- `{strategy}_parameters.json` - parameter values
- `{strategy}_optimization_report.md` - human-readable report

### Gate 2: Cloud Model Download
Runs automatically every 15 minutes via CloudModelSynchronizationService.
Check logs for validation results:
```
=== GATE 2: CLOUD ONNX MODEL DOWNLOAD VALIDATION ===
[1/7] Downloading model to staging location...
[2/7] Verifying SHA256 hash...
  ✓ Hash verification passed
...
✓ GATE 2 PASSED - Model validated and deployed
```

### Gate 3: S15 Shadow Learning
Runs continuously as background service.
Check promotion status:
```bash
cat config/s15_promotion.json
```

Monitor logs for evaluation:
```
=== GATE 3: S15 SHADOW LEARNING PROMOTION VALIDATION ===
[1/4] Calculating S15 shadow metrics...
[2/4] Calculating baseline metrics...
[3/4] Validating performance thresholds...
[4/4] Running bootstrap statistical test...
✓ GATE 3 PASSED - Promoting S15 to canary mode
```

## Fail-Safe Behavior

### Gate 1 Failure
- Parameters NOT saved
- Current baseline remains unchanged
- Detailed failure reason logged
- Optuna continues on next weekly run

### Gate 2 Failure
- Downloaded model deleted from staging
- Live model remains unchanged
- Alert sent to monitoring system
- Detailed validation failure logged

### Gate 3 Failure
- S15 remains in shadow-only mode
- Counter reset to zero
- Continues accumulating shadow data
- No impact on live trading

### Gate 4 Failure
- New model file NOT loaded
- Current in-memory model unchanged
- Hot-reload aborted immediately
- Detailed failure reason logged

## Safety Philosophy

All four gates follow the same principle:
1. **Pessimistic by default**: Failures result in status quo
2. **Multiple checks**: No single point of failure
3. **Statistical rigor**: Bootstrap tests ensure significance
4. **Comprehensive logging**: Every decision is auditable
5. **Gradual rollout**: Canary traffic before full deployment

This ensures that production trading is never disrupted by untested changes.
