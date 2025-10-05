"""
Gate 1: Parameter Optimization Validation
Implements comprehensive validation before deploying optimized parameters to production.
"""

import os
import json
import hashlib
from datetime import datetime
from typing import Dict, Tuple, Optional
import numpy as np
import pandas as pd
from scipy import stats


# Validation thresholds
MIN_TRADES_THRESHOLD = 200
WIN_RATE_IMPROVEMENT_THRESHOLD = 0.02  # 2 percentage points
SHARPE_IMPROVEMENT_THRESHOLD = 0.10
BOOTSTRAP_P_VALUE_THRESHOLD = 0.05
BOOTSTRAP_ITERATIONS = 10000


def bootstrap_test(baseline_pnl: np.ndarray, candidate_pnl: np.ndarray, n_iterations: int = BOOTSTRAP_ITERATIONS) -> float:
    """
    Run bootstrap statistical test to determine if candidate performance is significantly better.
    
    Args:
        baseline_pnl: Array of baseline P&L values
        candidate_pnl: Array of candidate P&L values
        n_iterations: Number of bootstrap iterations
        
    Returns:
        p-value indicating statistical significance
    """
    if len(baseline_pnl) == 0 or len(candidate_pnl) == 0:
        return 1.0  # Fail by default
    
    # Calculate observed difference
    observed_diff = np.mean(candidate_pnl) - np.mean(baseline_pnl)
    
    # Bootstrap resampling
    combined = np.concatenate([baseline_pnl, candidate_pnl])
    n_baseline = len(baseline_pnl)
    n_candidate = len(candidate_pnl)
    
    bootstrap_diffs = []
    for _ in range(n_iterations):
        # Resample with replacement
        indices = np.random.choice(len(combined), size=len(combined), replace=True)
        resampled = combined[indices]
        
        # Split into two groups
        group1 = resampled[:n_baseline]
        group2 = resampled[n_baseline:]
        
        # Calculate difference
        diff = np.mean(group2) - np.mean(group1)
        bootstrap_diffs.append(diff)
    
    # Calculate p-value (one-tailed test for improvement)
    p_value = np.sum(np.array(bootstrap_diffs) <= 0) / n_iterations
    
    return p_value


def calculate_metrics(trades_df: pd.DataFrame) -> Dict[str, float]:
    """
    Calculate performance metrics from trades DataFrame.
    
    Args:
        trades_df: DataFrame with columns: pnl, win (boolean)
        
    Returns:
        Dictionary of metrics
    """
    if len(trades_df) == 0:
        return {
            'num_trades': 0,
            'win_rate': 0.0,
            'sharpe_ratio': 0.0,
            'mean_pnl': 0.0,
            'std_pnl': 0.0
        }
    
    num_trades = len(trades_df)
    win_rate = trades_df['win'].mean() if 'win' in trades_df.columns else 0.0
    
    pnl_values = trades_df['pnl'].values
    mean_pnl = np.mean(pnl_values)
    std_pnl = np.std(pnl_values)
    
    # Calculate Sharpe ratio (annualized, assuming daily trades)
    if std_pnl > 0:
        sharpe_ratio = (mean_pnl / std_pnl) * np.sqrt(252)
    else:
        sharpe_ratio = 0.0
    
    return {
        'num_trades': num_trades,
        'win_rate': win_rate,
        'sharpe_ratio': sharpe_ratio,
        'mean_pnl': mean_pnl,
        'std_pnl': std_pnl
    }


def validate_parameter_bounds(params: Dict, strategy: str) -> Tuple[bool, str]:
    """
    Verify that all parameter values fall within safe bounds.
    
    Args:
        params: Parameter dictionary
        strategy: Strategy name
        
    Returns:
        Tuple of (is_valid, error_message)
    """
    # Define safe bounds per strategy
    bounds = {
        'S2': {
            'vwap_threshold': (0.05, 0.30),
            'rsi_level': (20, 40),
            'stop_atr_mult': (1.0, 4.0),
            'target_atr_mult': (2.0, 6.0),
            'min_atr': (0.20, 0.50)
        },
        'S3': {
            'width_rank_enter': (0.05, 0.40),
            'min_squeeze_bars': (3, 15),
            'stop_atr_mult': (0.5, 3.0),
            'target_r1': (1.0, 4.0),
            'target_r2': (2.0, 6.0)
        },
        'S6': {
            'min_atr': (0.20, 0.50),
            'stop_atr_mult': (1.0, 4.0),
            'target_atr_mult': (2.0, 6.0)
        },
        'S11': {
            'min_atr': (0.20, 0.50),
            'stop_atr_mult': (1.0, 4.0),
            'target_atr_mult': (2.0, 6.0)
        }
    }
    
    if strategy not in bounds:
        return True, ""  # No bounds defined for this strategy
    
    strategy_bounds = bounds[strategy]
    
    for param_name, (min_val, max_val) in strategy_bounds.items():
        if param_name in params:
            value = params[param_name]
            if not (min_val <= value <= max_val):
                return False, f"Parameter {param_name}={value} outside safe bounds [{min_val}, {max_val}]"
    
    return True, ""


def validate_optimized_parameters_gate(
    baseline_params: Dict,
    candidate_params: Dict,
    holdout_df: pd.DataFrame,
    strategy: str,
    backtest_func
) -> Tuple[bool, Dict[str, any], str]:
    """
    Gate 1: Comprehensive validation of optimized parameters before production deployment.
    
    Args:
        baseline_params: Current baseline parameters
        candidate_params: New candidate parameters from Optuna
        holdout_df: Out-of-sample holdout data (last 20-60 days)
        strategy: Strategy name (S2, S3, S6, S11)
        backtest_func: Backtest function that returns trades DataFrame
        
    Returns:
        Tuple of (passed, metrics_dict, reason)
    """
    print("\n" + "=" * 80)
    print("GATE 1: PARAMETER OPTIMIZATION VALIDATION")
    print("=" * 80)
    
    # Check 1: Parameter bounds validation
    print("\n[1/4] Validating parameter bounds...")
    bounds_valid, bounds_error = validate_parameter_bounds(candidate_params, strategy)
    if not bounds_valid:
        return False, {}, f"Parameter bounds check FAILED: {bounds_error}"
    print("  ✓ All parameters within safe bounds")
    
    # Check 2: Run holdout backtests
    print("\n[2/4] Running holdout backtests...")
    print(f"  Holdout period: {len(holdout_df)} bars")
    
    try:
        baseline_trades = backtest_func(holdout_df, baseline_params, strategy)
        candidate_trades = backtest_func(holdout_df, candidate_params, strategy)
    except Exception as e:
        return False, {}, f"Backtest execution FAILED: {str(e)}"
    
    baseline_metrics = calculate_metrics(baseline_trades)
    candidate_metrics = calculate_metrics(candidate_trades)
    
    print(f"  Baseline:  {baseline_metrics['num_trades']} trades, "
          f"WR={baseline_metrics['win_rate']:.1%}, Sharpe={baseline_metrics['sharpe_ratio']:.2f}")
    print(f"  Candidate: {candidate_metrics['num_trades']} trades, "
          f"WR={candidate_metrics['win_rate']:.1%}, Sharpe={candidate_metrics['sharpe_ratio']:.2f}")
    
    # Check 3: Minimum trade count
    print("\n[3/4] Checking sample size...")
    if candidate_metrics['num_trades'] < MIN_TRADES_THRESHOLD:
        reason = (f"Insufficient trades: {candidate_metrics['num_trades']} < {MIN_TRADES_THRESHOLD} "
                  "(sample size too small for statistical reliability)")
        return False, {'baseline': baseline_metrics, 'candidate': candidate_metrics}, reason
    print(f"  ✓ Sample size sufficient: {candidate_metrics['num_trades']} trades")
    
    # Check 4: Performance improvements
    print("\n[4/4] Validating performance improvements...")
    
    # Win rate improvement
    win_rate_diff = candidate_metrics['win_rate'] - baseline_metrics['win_rate']
    if win_rate_diff < WIN_RATE_IMPROVEMENT_THRESHOLD:
        reason = (f"Win rate improvement insufficient: {win_rate_diff:+.1%} < "
                  f"{WIN_RATE_IMPROVEMENT_THRESHOLD:+.1%}")
        return False, {'baseline': baseline_metrics, 'candidate': candidate_metrics}, reason
    print(f"  ✓ Win rate improved: {win_rate_diff:+.1%}")
    
    # Sharpe ratio improvement
    sharpe_diff = candidate_metrics['sharpe_ratio'] - baseline_metrics['sharpe_ratio']
    if sharpe_diff < SHARPE_IMPROVEMENT_THRESHOLD:
        reason = (f"Sharpe ratio improvement insufficient: {sharpe_diff:+.2f} < "
                  f"{SHARPE_IMPROVEMENT_THRESHOLD:+.2f}")
        return False, {'baseline': baseline_metrics, 'candidate': candidate_metrics}, reason
    print(f"  ✓ Sharpe ratio improved: {sharpe_diff:+.2f}")
    
    # Statistical significance (bootstrap test)
    print("  Running bootstrap test...")
    baseline_pnl = baseline_trades['pnl'].values if len(baseline_trades) > 0 else np.array([])
    candidate_pnl = candidate_trades['pnl'].values if len(candidate_trades) > 0 else np.array([])
    
    p_value = bootstrap_test(baseline_pnl, candidate_pnl)
    print(f"  Bootstrap p-value: {p_value:.4f}")
    
    if p_value >= BOOTSTRAP_P_VALUE_THRESHOLD:
        reason = (f"Statistical significance test FAILED: p-value={p_value:.4f} >= "
                  f"{BOOTSTRAP_P_VALUE_THRESHOLD} (improvement not statistically significant)")
        return False, {'baseline': baseline_metrics, 'candidate': candidate_metrics, 'p_value': p_value}, reason
    print(f"  ✓ Statistically significant: p={p_value:.4f}")
    
    # All checks passed
    metrics = {
        'baseline': baseline_metrics,
        'candidate': candidate_metrics,
        'win_rate_improvement': win_rate_diff,
        'sharpe_improvement': sharpe_diff,
        'p_value': p_value
    }
    
    print("\n" + "=" * 80)
    print("✓ GATE 1 PASSED - Parameters validated for production deployment")
    print("=" * 80)
    
    return True, metrics, "All validation checks passed"


def create_parameter_manifest(
    strategy: str,
    params: Dict,
    validation_metrics: Dict,
    holdout_period: Tuple[datetime, datetime]
) -> Dict:
    """
    Create manifest file with parameter validation details.
    
    Args:
        strategy: Strategy name
        params: Parameter dictionary
        validation_metrics: Validation metrics
        holdout_period: Tuple of (start_date, end_date) for holdout period
        
    Returns:
        Manifest dictionary
    """
    # Calculate SHA256 hash of parameters
    params_json = json.dumps(params, sort_keys=True)
    params_hash = hashlib.sha256(params_json.encode()).hexdigest()
    
    manifest = {
        'strategy': strategy,
        'version': datetime.utcnow().isoformat() + 'Z',
        'parameters': params,
        'parameters_hash': params_hash,
        'validation': {
            'gate': 'parameter_optimization',
            'status': 'passed',
            'holdout_period': {
                'start': holdout_period[0].isoformat(),
                'end': holdout_period[1].isoformat()
            },
            'baseline_metrics': validation_metrics.get('baseline', {}),
            'candidate_metrics': validation_metrics.get('candidate', {}),
            'improvements': {
                'win_rate': validation_metrics.get('win_rate_improvement', 0.0),
                'sharpe_ratio': validation_metrics.get('sharpe_improvement', 0.0),
                'p_value': validation_metrics.get('p_value', 1.0)
            }
        },
        'thresholds': {
            'min_trades': MIN_TRADES_THRESHOLD,
            'min_win_rate_improvement': WIN_RATE_IMPROVEMENT_THRESHOLD,
            'min_sharpe_improvement': SHARPE_IMPROVEMENT_THRESHOLD,
            'max_p_value': BOOTSTRAP_P_VALUE_THRESHOLD
        },
        'created_at': datetime.utcnow().isoformat() + 'Z'
    }
    
    return manifest


def save_manifest(manifest: Dict, filepath: str):
    """Save manifest to JSON file."""
    with open(filepath, 'w') as f:
        json.dump(manifest, f, indent=2)
    print(f"  Manifest saved: {filepath}")
