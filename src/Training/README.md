# Trading Bot Parameter Optimization System

This directory contains the Python training infrastructure for session-aware parameter optimization.

## Overview

The training system optimizes trading strategy parameters separately for different market sessions (Overnight, RTH, PostRTH) using historical data and Bayesian optimization. It produces session-specific parameter files that the C# trading strategies load and use.

## Architecture

```
Training/
├── historical_data_downloader.py  # Downloads ES/NQ 1-min bars from TopstepX API
├── session_grouper.py             # CME trading day logic (matches C# MarketTimeService)
├── fast_backtest_engine.py        # Numba-accelerated backtesting (5-10 sec for 90 days)
├── parallel_optimizer.py          # Bayesian optimization with Optuna
├── parameter_validator.py         # Safety checks and validation
├── training_orchestrator.py       # Main entry point - coordinates full pipeline
├── requirements.txt               # Python dependencies
├── data/                          # Downloaded historical data (parquet files)
└── README.md                      # This file
```

## Workflow

1. **Download Data** - Fetch 90 days of 1-minute bars for ES and NQ
2. **Group by Session** - Apply CME trading day logic and categorize bars by session
3. **Optimize Parameters** - Run Bayesian optimization separately for each session
4. **Validate Results** - Ensure improvements are real and parameters are safe
5. **Save Parameters** - Write optimized parameters to JSON files
6. **Generate Report** - Create markdown performance report

## Installation

```bash
# Install Python dependencies
cd src/Training
pip install -r requirements.txt

# Set up environment variables (in .env file)
TOPSTEPX_API_KEY=your_api_key_here
TOPSTEPX_API_SECRET=your_api_secret_here
```

## Usage

### Full Training Pipeline

Run the complete optimization for all strategies:

```bash
python training_orchestrator.py
```

**Important:** Training can only run during CME closed window (Friday 5 PM - Sunday 6 PM ET) to avoid conflicts with live trading.

### Individual Components

Download historical data:
```bash
python historical_data_downloader.py
```

Test session grouping logic:
```bash
python session_grouper.py
```

Test fast backtest engine:
```bash
python fast_backtest_engine.py
```

Run parameter optimization:
```bash
python parallel_optimizer.py
```

Test parameter validation:
```bash
python parameter_validator.py
```

## Output Files

Optimized parameters are saved to `artifacts/stage/parameters/`:

```
artifacts/
└── stage/
    └── parameters/
        ├── S2_parameters.json           # Optimized S2 parameters
        ├── S2_optimization_report.md    # Performance report
        ├── S3_parameters.json           # Optimized S3 parameters
        └── ...
```

### Parameter File Format

```json
{
  "strategy": "S2",
  "version": "1.0",
  "last_updated": "2025-01-15T02:30:00Z",
  "baseline_sharpe": 1.45,
  "optimized_sharpe": 1.63,
  "improvement_pct": 12.4,
  "default_parameters": {
    "vwap_threshold": 0.15,
    "rsi_level": 30,
    "stop_atr_mult": 2.0,
    "target_atr_mult": 3.5,
    "min_atr": 0.25
  },
  "session_overrides": {
    "Overnight": {
      "vwap_threshold": 0.18,
      "stop_atr_mult": 2.5,
      "target_atr_mult": 4.0
    },
    "RTH": {
      "vwap_threshold": 0.12,
      "stop_atr_mult": 1.8,
      "target_atr_mult": 3.2
    },
    "PostRTH": {
      "vwap_threshold": 0.16,
      "stop_atr_mult": 2.2,
      "target_atr_mult": 3.8
    }
  },
  "validation_metrics": {
    "out_of_sample_sharpe": 1.63,
    "win_rate": 0.58,
    "avg_r_multiple": 1.45,
    "max_drawdown": 0.12
  }
}
```

## Integration with C# Code

The C# parameter classes (`S2Parameters`, `S3Parameters`, etc.) automatically load these JSON files:

```csharp
// C# code loads parameters with hourly reload
var parameters = S2Parameters.LoadOptimal();

// Get session-specific overrides
var rthParams = parameters.LoadOptimalForSession("RTH");
```

## Performance

- **Fast Backtest Engine**: 5-10 seconds for 90 days of 1-minute data (using numba JIT)
- **Parameter Optimization**: ~5-10 minutes for 100 trials per session (Bayesian optimization)
- **Full Training Pipeline**: ~30-60 minutes for all strategies

## Safety Mechanisms

1. **Market Timing Check** - Only runs during closed window
2. **Parameter Bounds** - Validates all parameters are within safety limits
3. **Minimum Improvement** - Requires 10% Sharpe improvement
4. **Session Validation** - No single session can degrade >5%
5. **Out-of-Sample Testing** - Uses last 20% of data for validation
6. **Convergence Check** - Ensures optimization didn't just get lucky

## Extending the System

### Adding a New Strategy

1. Implement backtest function in `fast_backtest_engine.py`:
   ```python
   def backtest_s3(df, params, s7_gate=None):
       # Strategy-specific logic
       pass
   ```

2. Define parameter space in `parallel_optimizer.py`:
   ```python
   S3_PARAM_SPACE = {
       'width_rank_enter': (0.05, 0.40, 0.01),
       # ... other parameters
   }
   ```

3. Add strategy name to `STRATEGIES` list in `training_orchestrator.py`

4. Create corresponding C# parameter class

## Troubleshooting

### "Market is currently OPEN" Error
Training can only run during CME closed window (Friday 5 PM - Sunday 6 PM ET). Wait until market closes or override (not recommended).

### "Insufficient data" Error
Ensure historical data is downloaded and covers at least 90 days. Check `data/` directory.

### Validation Failures
- Check parameter bounds are reasonable
- Verify sufficient improvement (>10% Sharpe)
- Review session-by-session performance
- Check optimization report for details

### Slow Performance
- Reduce `n_trials` in `training_orchestrator.py` (default: 100)
- Use fewer CPU cores in parallel processing
- Reduce data lookback period (default: 90 days)

## Developer Notes

- **CME Trading Day Logic** must match C# `MarketTimeService` exactly
- **Numba JIT** requires arrays (not lists) for best performance
- **Optuna** handles Bayesian optimization - don't need grid search
- **Parquet files** provide fast I/O (~10x faster than CSV)
- **Session grouping** is critical - never cross maintenance break (5-6 PM daily)

## Future Enhancements

- [ ] Add S3, S6, S11 strategy implementations
- [ ] Implement S15_RL model training with PPO
- [ ] Add walk-forward validation
- [ ] Support for multiple symbols beyond ES/NQ
- [ ] Real-time monitoring dashboard
- [ ] Automated deployment to production
- [ ] A/B testing framework
- [ ] Parameter drift detection

## Support

For questions or issues, contact the development team or file an issue in the repository.
