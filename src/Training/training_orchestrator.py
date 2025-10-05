"""
Training Orchestrator - Main Entry Point
Coordinates the full training pipeline for session-aware parameter optimization.
"""

import os
import sys
import json
from datetime import datetime, time
from typing import Dict, List
import pandas as pd
import pytz
from dotenv import load_dotenv

# Import training modules
from historical_data_downloader import download_all_symbols
from parallel_optimizer import optimize_by_session
from parameter_validator import validate_optimized_parameters, ValidationResult
from session_grouper import ET


# Load environment
load_dotenv()

# Configuration
STRATEGIES = ['S2', 'S3', 'S6', 'S11']  # All active strategies for unified system
ARTIFACTS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(__file__))), 'artifacts')
STAGE_DIR = os.path.join(ARTIFACTS_DIR, 'stage', 'parameters')
CURRENT_DIR = os.path.join(ARTIFACTS_DIR, 'current', 'parameters')
DATA_DIR = os.path.join(os.path.dirname(__file__), 'data')

# CME market closed window (Friday 5 PM - Sunday 6 PM ET)
CME_CLOSE_FRIDAY = time(17, 0, 0)
CME_OPEN_SUNDAY = time(18, 0, 0)


def is_market_closed() -> bool:
    """
    Check if current time is within CME closed window.
    Training should only run when market is closed to avoid live trading conflicts.
    
    Returns:
        True if market is closed (safe to train)
    """
    now_et = datetime.now(ET)
    current_time = now_et.time()
    weekday = now_et.weekday()
    
    # Friday after 5 PM
    if weekday == 4 and current_time >= CME_CLOSE_FRIDAY:
        return True
    
    # Saturday (all day)
    if weekday == 5:
        return True
    
    # Sunday before 6 PM
    if weekday == 6 and current_time < CME_OPEN_SUNDAY:
        return True
    
    return False


def check_market_timing():
    """
    Verify training is running during market closed window.
    Exit with error if market is open.
    """
    if not is_market_closed():
        now_et = datetime.now(ET)
        print("=" * 80)
        print("ERROR: Market is currently OPEN")
        print("=" * 80)
        print(f"Current time: {now_et.strftime('%A %Y-%m-%d %H:%M:%S %Z')}")
        print()
        print("Training can only run during CME closed window:")
        print("  Friday 5:00 PM ET - Sunday 6:00 PM ET")
        print()
        print("This prevents conflicts with live trading and ensures")
        print("fresh parameters are deployed before market open.")
        print("=" * 80)
        sys.exit(1)
    
    print("✓ Market timing check passed - safe to train")


def load_baseline_parameters(strategy: str) -> Dict:
    """
    Load current baseline parameters for comparison.
    
    Args:
        strategy: Strategy name
        
    Returns:
        Dictionary of baseline parameters
    """
    filepath = os.path.join(CURRENT_DIR, f"{strategy}_parameters.json")
    
    if os.path.exists(filepath):
        with open(filepath, 'r') as f:
            data = json.load(f)
            return data.get('default_parameters', {})
    
    # Return hardcoded defaults if file doesn't exist
    if strategy == 'S2':
        return {
            'vwap_threshold': 0.15,
            'rsi_level': 30,
            'stop_atr_mult': 2.0,
            'target_atr_mult': 3.5,
            'min_atr': 0.25,
            'max_bars_in_trade': 45
        }
    
    return {}


def save_optimized_parameters(
    strategy: str,
    session_results: Dict,
    baseline_sharpe: float,
    overall_metrics: Dict
):
    """
    Save optimized parameters to staging directory.
    
    Args:
        strategy: Strategy name
        session_results: Dictionary mapping session to (params, train_metrics, val_metrics)
        baseline_sharpe: Baseline Sharpe ratio
        overall_metrics: Overall performance metrics
    """
    # Ensure staging directory exists
    os.makedirs(STAGE_DIR, exist_ok=True)
    
    # Extract default parameters (use RTH as default, or first available)
    default_params = None
    if 'RTH' in session_results:
        default_params = session_results['RTH'][0]
    else:
        default_params = list(session_results.values())[0][0]
    
    # Build session overrides
    session_overrides = {}
    for session, (params, _, val_metrics) in session_results.items():
        session_overrides[session] = params
    
    # Calculate improvement
    optimized_sharpe = overall_metrics.get('sharpe_ratio', 0.0)
    improvement_pct = ((optimized_sharpe - baseline_sharpe) / baseline_sharpe * 100) if baseline_sharpe > 0 else 0.0
    
    # Build parameter file
    param_data = {
        'strategy': strategy,
        'version': '1.0',
        'last_updated': datetime.utcnow().isoformat() + 'Z',
        'baseline_sharpe': round(baseline_sharpe, 3),
        'optimized_sharpe': round(optimized_sharpe, 3),
        'improvement_pct': round(improvement_pct, 1),
        'default_parameters': default_params,
        'session_overrides': session_overrides,
        'validation_metrics': {
            'in_sample_sharpe': round(overall_metrics.get('in_sample_sharpe', 0.0), 3),
            'out_of_sample_sharpe': round(optimized_sharpe, 3),
            'win_rate': round(overall_metrics.get('win_rate', 0.0), 3),
            'avg_r_multiple': round(overall_metrics.get('avg_r_multiple', 0.0), 3),
            'max_drawdown': round(overall_metrics.get('max_drawdown', 0.0), 3)
        }
    }
    
    # Save to file
    filepath = os.path.join(STAGE_DIR, f"{strategy}_parameters.json")
    with open(filepath, 'w') as f:
        json.dump(param_data, f, indent=2)
    
    print(f"  Saved optimized parameters to {filepath}")


def generate_performance_report(
    strategy: str,
    session_results: Dict,
    validation_result: ValidationResult
) -> str:
    """
    Generate markdown performance report.
    
    Args:
        strategy: Strategy name
        session_results: Optimization results by session
        validation_result: Validation result object
        
    Returns:
        Markdown formatted report string
    """
    lines = []
    lines.append(f"# {strategy} Parameter Optimization Report")
    lines.append(f"\n**Generated:** {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S UTC')}")
    lines.append(f"\n## Validation Status: {'✓ PASSED' if validation_result.passed else '✗ FAILED'}")
    
    if validation_result.errors:
        lines.append("\n### Errors")
        for error in validation_result.errors:
            lines.append(f"- {error}")
    
    if validation_result.warnings:
        lines.append("\n### Warnings")
        for warning in validation_result.warnings:
            lines.append(f"- {warning}")
    
    lines.append("\n## Session-by-Session Results")
    lines.append("\n| Session | Sharpe (Train) | Sharpe (Val) | Win Rate | Trades |")
    lines.append("|---------|----------------|--------------|----------|--------|")
    
    for session, (params, train_metrics, val_metrics) in session_results.items():
        lines.append(
            f"| {session:10s} | {train_metrics['sharpe_ratio']:14.3f} | "
            f"{val_metrics['sharpe_ratio']:12.3f} | {val_metrics['win_rate']:8.1%} | "
            f"{val_metrics['total_trades']:6d} |"
        )
    
    lines.append("\n## Overall Metrics")
    for key, value in validation_result.metrics.items():
        if isinstance(value, float):
            lines.append(f"- **{key}**: {value:.3f}")
        else:
            lines.append(f"- **{key}**: {value}")
    
    lines.append("\n## Parameter Changes")
    lines.append("\n### RTH Session Parameters")
    if 'RTH' in session_results:
        params = session_results['RTH'][0]
        for key, value in params.items():
            lines.append(f"- **{key}**: {value}")
    
    return "\n".join(lines)


def train_single_strategy(strategy: str, df: pd.DataFrame) -> bool:
    """
    Train a single strategy across all sessions.
    
    Args:
        strategy: Strategy name
        df: Historical data DataFrame
        
    Returns:
        True if training succeeded and validation passed
    """
    print("\n" + "=" * 80)
    print(f"TRAINING {strategy} STRATEGY")
    print("=" * 80)
    
    # Load baseline parameters
    baseline_params = load_baseline_parameters(strategy)
    print(f"\nBaseline parameters: {baseline_params}")
    
    # Optimize by session
    print("\nRunning parameter optimization...")
    session_results = optimize_by_session(
        df,
        strategy,
        train_ratio=0.80,
        n_trials=100  # Increase for production
    )
    
    if len(session_results) == 0:
        print("ERROR: No optimization results - insufficient data")
        return False
    
    # Validate parameters
    print("\nValidating optimized parameters...")
    
    # Use RTH session for overall validation
    if 'RTH' in session_results:
        optimized_params = session_results['RTH'][0]
        val_metrics = session_results['RTH'][2]
    else:
        optimized_params = list(session_results.values())[0][0]
        val_metrics = list(session_results.values())[0][2]
    
    # Create validation dataset (last 20% of data)
    split_idx = int(len(df) * 0.80)
    df_validate = df.iloc[split_idx:]
    
    validation_result = validate_optimized_parameters(
        baseline_params,
        optimized_params,
        df_validate,
        strategy,
        baseline_metrics=None  # Will compute from scratch
    )
    
    print(validation_result)
    
    # If validation passed, save parameters
    if validation_result.passed:
        print("\n✓ Validation PASSED - Saving optimized parameters")
        
        baseline_sharpe = validation_result.metrics.get('baseline_sharpe', 0.0)
        save_optimized_parameters(
            strategy,
            session_results,
            baseline_sharpe,
            validation_result.metrics
        )
        
        # Generate report
        report = generate_performance_report(strategy, session_results, validation_result)
        report_path = os.path.join(STAGE_DIR, f"{strategy}_optimization_report.md")
        with open(report_path, 'w') as f:
            f.write(report)
        print(f"  Generated report: {report_path}")
        
        return True
    else:
        print("\n✗ Validation FAILED - Parameters NOT saved")
        return False


def main():
    """
    Main training orchestration function.
    """
    print("=" * 80)
    print("SESSION-AWARE PARAMETER OPTIMIZATION")
    print("=" * 80)
    print(f"Started: {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S UTC')}")
    
    # 1. Check market timing
    print("\n[1/5] Checking market timing...")
    check_market_timing()
    
    # 2. Download/update historical data
    print("\n[2/5] Downloading historical data...")
    try:
        # Check if data already exists
        es_path = os.path.join(DATA_DIR, 'ES_90d.parquet')
        nq_path = os.path.join(DATA_DIR, 'NQ_90d.parquet')
        
        if os.path.exists(es_path) and os.path.exists(nq_path):
            # Check if data is recent (within 7 days)
            data_age_days = (datetime.now() - datetime.fromtimestamp(os.path.getmtime(es_path))).days
            if data_age_days < 7:
                print(f"  Using existing data (age: {data_age_days} days)")
            else:
                print(f"  Data is {data_age_days} days old - re-downloading...")
                download_all_symbols()
        else:
            print("  No existing data found - downloading...")
            download_all_symbols()
    except Exception as e:
        print(f"ERROR: Failed to download data: {e}")
        sys.exit(1)
    
    # 3. Load data
    print("\n[3/5] Loading historical data...")
    try:
        df_es = pd.read_parquet(os.path.join(DATA_DIR, 'ES_90d.parquet'))
        print(f"  Loaded ES: {len(df_es)} bars, {df_es['trading_day'].nunique()} trading days")
    except Exception as e:
        print(f"ERROR: Failed to load data: {e}")
        sys.exit(1)
    
    # 4. Train each strategy
    print("\n[4/5] Training strategies...")
    results = {}
    
    for strategy in STRATEGIES:
        try:
            success = train_single_strategy(strategy, df_es)
            results[strategy] = success
        except Exception as e:
            print(f"ERROR training {strategy}: {e}")
            import traceback
            traceback.print_exc()
            results[strategy] = False
    
    # 5. Summary
    print("\n[5/5] Training Summary")
    print("=" * 80)
    
    total = len(results)
    passed = sum(1 for success in results.values() if success)
    failed = total - passed
    
    print(f"\nTotal strategies: {total}")
    print(f"Passed validation: {passed}")
    print(f"Failed validation: {failed}")
    
    print("\nResults by strategy:")
    for strategy, success in results.items():
        status = "✓ PASSED" if success else "✗ FAILED"
        print(f"  {strategy}: {status}")
    
    print("\n" + "=" * 80)
    print(f"Completed: {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S UTC')}")
    print("=" * 80)
    
    # Exit with appropriate code
    if failed > 0:
        print("\nSome strategies failed validation - manual review required")
        sys.exit(1)
    else:
        print("\nAll strategies passed validation - ready for deployment")
        sys.exit(0)


if __name__ == "__main__":
    main()
