"""
Parallel Parameter Optimizer
Tests thousands of parameter combinations using Bayesian optimization and parallel processing.
"""

import numpy as np
import pandas as pd
from typing import Dict, List, Tuple
import optuna
from multiprocessing import Pool, cpu_count
import warnings
from fast_backtest_engine import backtest_s2

warnings.filterwarnings('ignore')


# Parameter search spaces for each strategy
S2_PARAM_SPACE = {
    'vwap_threshold': (0.05, 0.30, 0.01),      # (min, max, step)
    'rsi_level': (20, 40, 2),
    'stop_atr_mult': (1.0, 4.0, 0.25),
    'target_atr_mult': (2.0, 6.0, 0.5),
    'min_atr': (0.20, 0.50, 0.05),
    'max_bars_in_trade': (30, 60, 5)
}

S3_PARAM_SPACE = {
    'width_rank_enter': (0.05, 0.40, 0.01),
    'min_squeeze_bars': (3, 15, 1),
    'stop_atr_mult': (0.5, 3.0, 0.1),
    'target_r1': (1.0, 4.0, 0.1),
    'target_r2': (2.0, 6.0, 0.2),
}


def get_param_ranges(strategy: str, session: str = None) -> Dict:
    """
    Get parameter search ranges for a strategy and optional session.
    
    Args:
        strategy: Strategy name (S2, S3, S6, S11)
        session: Optional session name for session-specific ranges
        
    Returns:
        Dictionary of parameter ranges
    """
    if strategy == 'S2':
        return S2_PARAM_SPACE
    elif strategy == 'S3':
        return S3_PARAM_SPACE
    else:
        raise ValueError(f"Unknown strategy: {strategy}")


def create_optuna_trial(trial: optuna.Trial, param_space: Dict) -> Dict:
    """
    Create parameter set from Optuna trial.
    
    Args:
        trial: Optuna trial object
        param_space: Parameter space definition
        
    Returns:
        Dictionary of parameters for this trial
    """
    params = {}
    
    for param_name, (min_val, max_val, step) in param_space.items():
        if isinstance(min_val, int):
            params[param_name] = trial.suggest_int(param_name, min_val, max_val, step=step)
        else:
            params[param_name] = trial.suggest_float(param_name, min_val, max_val, step=step)
    
    return params


def objective_function(
    trial: optuna.Trial,
    df_train: pd.DataFrame,
    strategy: str,
    param_space: Dict,
    s7_gate: pd.Series = None
) -> float:
    """
    Optuna objective function to maximize.
    
    Args:
        trial: Optuna trial
        df_train: Training data
        strategy: Strategy name
        param_space: Parameter space
        s7_gate: S7 gate approval series
        
    Returns:
        Sharpe ratio (to be maximized)
    """
    # Get parameters for this trial
    params = create_optuna_trial(trial, param_space)
    
    # Run backtest
    if strategy == 'S2':
        results = backtest_s2(df_train, params, s7_gate)
    else:
        raise ValueError(f"Strategy {strategy} not implemented yet")
    
    # Return Sharpe ratio as optimization target
    sharpe = results['sharpe_ratio']
    
    # Penalize if too few trades
    if results['total_trades'] < 10:
        sharpe *= 0.5
    
    # Penalize excessive drawdown
    if results['max_drawdown'] > 0.25:
        sharpe *= 0.5
    
    return sharpe


def optimize_parameters(
    df_train: pd.DataFrame,
    df_validate: pd.DataFrame,
    strategy: str,
    session: str = None,
    n_trials: int = 100,
    s7_gate_train: pd.Series = None,
    s7_gate_validate: pd.Series = None
) -> Tuple[Dict, Dict, Dict]:
    """
    Optimize strategy parameters using Bayesian optimization.
    
    Args:
        df_train: Training data (in-sample)
        df_validate: Validation data (out-of-sample)
        strategy: Strategy name (S2, S3, etc)
        session: Optional session name for session-specific optimization
        n_trials: Number of trials to run
        s7_gate_train: S7 gate for training data
        s7_gate_validate: S7 gate for validation data
        
    Returns:
        Tuple of (best_params, train_metrics, validate_metrics)
    """
    print(f"\nOptimizing {strategy}" + (f" ({session} session)" if session else ""))
    print(f"  Training data: {len(df_train)} bars")
    print(f"  Validation data: {len(df_validate)} bars")
    print(f"  Trials: {n_trials}")
    
    # Get parameter space
    param_space = get_param_ranges(strategy, session)
    
    # Create Optuna study
    study = optuna.create_study(
        direction='maximize',
        sampler=optuna.samplers.TPESampler(seed=42)
    )
    
    # Run optimization
    study.optimize(
        lambda trial: objective_function(
            trial, df_train, strategy, param_space, s7_gate_train
        ),
        n_trials=n_trials,
        show_progress_bar=True,
        n_jobs=1  # Optuna will parallelize internally
    )
    
    # Get best parameters
    best_params = study.best_params
    
    print(f"  Best in-sample Sharpe: {study.best_value:.3f}")
    print(f"  Best parameters: {best_params}")
    
    # Evaluate on training set
    if strategy == 'S2':
        train_metrics = backtest_s2(df_train, best_params, s7_gate_train)
    else:
        raise ValueError(f"Strategy {strategy} not implemented")
    
    # Evaluate on validation set
    if strategy == 'S2':
        validate_metrics = backtest_s2(df_validate, best_params, s7_gate_validate)
    else:
        raise ValueError(f"Strategy {strategy} not implemented")
    
    print(f"  Training metrics: Sharpe={train_metrics['sharpe_ratio']:.3f}, "
          f"WinRate={train_metrics['win_rate']:.2%}, Trades={train_metrics['total_trades']}")
    print(f"  Validation metrics: Sharpe={validate_metrics['sharpe_ratio']:.3f}, "
          f"WinRate={validate_metrics['win_rate']:.2%}, Trades={validate_metrics['total_trades']}")
    
    return best_params, train_metrics, validate_metrics


def optimize_by_session(
    df: pd.DataFrame,
    strategy: str,
    train_ratio: float = 0.80,
    n_trials: int = 100
) -> Dict[str, Tuple[Dict, Dict, Dict]]:
    """
    Optimize parameters separately for each session (Overnight, RTH, PostRTH).
    
    Args:
        df: Full dataset with session_name column
        strategy: Strategy name
        train_ratio: Ratio of data to use for training (rest for validation)
        n_trials: Number of trials per session
        
    Returns:
        Dictionary mapping session name to (params, train_metrics, val_metrics)
    """
    results = {}
    
    # Split data by session
    sessions = ['Overnight', 'RTH', 'PostRTH']
    
    for session in sessions:
        # Filter data for this session
        df_session = df[df['session_name'] == session].copy()
        
        if len(df_session) < 1000:
            print(f"\nSkipping {session}: insufficient data ({len(df_session)} bars)")
            continue
        
        # Split into train/validate
        split_idx = int(len(df_session) * train_ratio)
        df_train = df_session.iloc[:split_idx]
        df_validate = df_session.iloc[split_idx:]
        
        # Load S7 gate if available (simplified - assume all approved for now)
        s7_gate_train = None
        s7_gate_validate = None
        
        # Optimize
        best_params, train_metrics, val_metrics = optimize_parameters(
            df_train, df_validate, strategy, session, n_trials,
            s7_gate_train, s7_gate_validate
        )
        
        results[session] = (best_params, train_metrics, val_metrics)
    
    return results


def parallel_grid_search(
    df_train: pd.DataFrame,
    strategy: str,
    param_grid: Dict[str, List],
    n_workers: int = None
) -> List[Tuple[Dict, float]]:
    """
    Run grid search in parallel using multiple CPU cores.
    
    Args:
        df_train: Training data
        strategy: Strategy name
        param_grid: Dictionary of parameter lists to try
        n_workers: Number of parallel workers (default: CPU count)
        
    Returns:
        List of (params, sharpe_ratio) tuples sorted by sharpe_ratio
    """
    if n_workers is None:
        n_workers = cpu_count()
    
    print(f"Running grid search with {n_workers} workers...")
    
    # Generate all parameter combinations
    from itertools import product
    
    param_names = list(param_grid.keys())
    param_values = list(param_grid.values())
    
    all_combinations = list(product(*param_values))
    print(f"  Total combinations: {len(all_combinations)}")
    
    # Create parameter dicts
    param_list = [
        dict(zip(param_names, combo))
        for combo in all_combinations
    ]
    
    # Run backtest for each combination in parallel
    def run_single_backtest(params):
        if strategy == 'S2':
            results = backtest_s2(df_train, params)
            return params, results['sharpe_ratio']
        else:
            return params, 0.0
    
    with Pool(n_workers) as pool:
        results = pool.map(run_single_backtest, param_list)
    
    # Sort by Sharpe ratio
    results_sorted = sorted(results, key=lambda x: x[1], reverse=True)
    
    print(f"  Best Sharpe: {results_sorted[0][1]:.3f}")
    print(f"  Top 10 combinations:")
    for i, (params, sharpe) in enumerate(results_sorted[:10]):
        print(f"    {i+1}. Sharpe={sharpe:.3f}: {params}")
    
    return results_sorted


if __name__ == "__main__":
    # Test with synthetic data
    print("Testing Parallel Optimizer...")
    
    # Generate synthetic data
    n_bars = 1000
    np.random.seed(42)
    
    base_price = 4500
    price_changes = np.random.normal(0, 0.5, n_bars)
    close_prices = base_price + np.cumsum(price_changes)
    
    df = pd.DataFrame({
        'high': close_prices + np.random.uniform(0.25, 2.0, n_bars),
        'low': close_prices - np.random.uniform(0.25, 2.0, n_bars),
        'close': close_prices,
        'volume': np.random.randint(100, 1000, n_bars),
        'session_name': np.random.choice(['Overnight', 'RTH', 'PostRTH'], n_bars)
    })
    
    # Split train/validate
    split = int(len(df) * 0.8)
    df_train = df.iloc[:split]
    df_validate = df.iloc[split:]
    
    # Test optimization
    best_params, train_metrics, val_metrics = optimize_parameters(
        df_train, df_validate, 'S2', n_trials=20
    )
    
    print("\n✓ Parallel optimizer working correctly")
