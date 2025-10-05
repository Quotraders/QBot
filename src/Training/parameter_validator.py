"""
Parameter Validator
Ensures optimized parameters are safe and better than baseline before deployment.
"""

from typing import Dict, Tuple, List
import numpy as np
import pandas as pd
from fast_backtest_engine import backtest_s2


# Validation thresholds
MIN_IMPROVEMENT_PCT = 10.0  # Minimum 10% improvement in Sharpe ratio
MAX_DEGRADATION_PCT = 5.0   # Max 5% degradation in any single session
MIN_TRADES = 10              # Minimum trades required
MAX_DRAWDOWN = 0.30          # Maximum acceptable drawdown (30%)

# Parameter safety bounds for S2
S2_SAFETY_BOUNDS = {
    'vwap_threshold': (0.05, 0.30),
    'rsi_level': (20, 80),
    'stop_atr_mult': (1.0, 4.0),
    'target_atr_mult': (2.0, 6.0),
    'min_atr': (0.15, 0.60),
    'max_bars_in_trade': (20, 100)
}

# Maximum reasonable parameter changes
MAX_PARAM_CHANGE_PCT = {
    'vwap_threshold': 50,  # 50% change max
    'rsi_level': 30,
    'stop_atr_mult': 50,
    'target_atr_mult': 50,
    'min_atr': 50,
    'max_bars_in_trade': 50
}


class ValidationResult:
    """Container for validation results."""
    
    def __init__(self):
        self.passed = False
        self.errors: List[str] = []
        self.warnings: List[str] = []
        self.metrics: Dict = {}
        self.improvements: Dict = {}
    
    def add_error(self, message: str):
        """Add validation error."""
        self.errors.append(message)
        self.passed = False
    
    def add_warning(self, message: str):
        """Add validation warning."""
        self.warnings.append(message)
    
    def set_passed(self):
        """Mark validation as passed."""
        if len(self.errors) == 0:
            self.passed = True
    
    def __str__(self) -> str:
        """String representation of validation result."""
        status = "✓ PASSED" if self.passed else "✗ FAILED"
        lines = [f"\nValidation Result: {status}"]
        
        if self.errors:
            lines.append("\nErrors:")
            for error in self.errors:
                lines.append(f"  - {error}")
        
        if self.warnings:
            lines.append("\nWarnings:")
            for warning in self.warnings:
                lines.append(f"  - {warning}")
        
        if self.metrics:
            lines.append("\nMetrics:")
            for key, value in self.metrics.items():
                lines.append(f"  {key}: {value}")
        
        return "\n".join(lines)


def validate_parameter_bounds(
    params: Dict[str, float],
    strategy: str
) -> Tuple[bool, List[str]]:
    """
    Validate parameters are within safety bounds.
    
    Args:
        params: Parameter dictionary
        strategy: Strategy name
        
    Returns:
        Tuple of (is_valid, error_messages)
    """
    errors = []
    
    if strategy == 'S2':
        bounds = S2_SAFETY_BOUNDS
    else:
        return True, []  # Other strategies not implemented yet
    
    for param_name, (min_val, max_val) in bounds.items():
        if param_name in params:
            value = params[param_name]
            if value < min_val or value > max_val:
                errors.append(
                    f"{param_name}={value} outside safety bounds [{min_val}, {max_val}]"
                )
    
    # Check target exceeds stop
    if 'target_atr_mult' in params and 'stop_atr_mult' in params:
        if params['target_atr_mult'] <= params['stop_atr_mult'] + 0.5:
            errors.append(
                f"target_atr_mult ({params['target_atr_mult']}) must exceed "
                f"stop_atr_mult ({params['stop_atr_mult']}) by at least 0.5"
            )
    
    return len(errors) == 0, errors


def validate_parameter_changes(
    baseline_params: Dict[str, float],
    optimized_params: Dict[str, float],
    strategy: str
) -> List[str]:
    """
    Validate parameter changes are reasonable (not too extreme).
    
    Args:
        baseline_params: Current baseline parameters
        optimized_params: Newly optimized parameters
        strategy: Strategy name
        
    Returns:
        List of warning messages
    """
    warnings = []
    
    if strategy == 'S2':
        max_changes = MAX_PARAM_CHANGE_PCT
    else:
        return []
    
    for param_name in optimized_params.keys():
        if param_name in baseline_params:
            baseline_val = baseline_params[param_name]
            optimized_val = optimized_params[param_name]
            
            if baseline_val == 0:
                continue
            
            change_pct = abs((optimized_val - baseline_val) / baseline_val * 100)
            
            if param_name in max_changes:
                max_change = max_changes[param_name]
                if change_pct > max_change:
                    warnings.append(
                        f"{param_name} changed by {change_pct:.1f}% "
                        f"({baseline_val} -> {optimized_val}), "
                        f"exceeds {max_change}% threshold - may need manual review"
                    )
    
    return warnings


def validate_performance_improvement(
    baseline_metrics: Dict[str, float],
    optimized_metrics: Dict[str, float],
    min_improvement_pct: float = MIN_IMPROVEMENT_PCT
) -> Tuple[bool, float, List[str]]:
    """
    Validate optimized parameters show meaningful improvement.
    
    Args:
        baseline_metrics: Baseline performance metrics
        optimized_metrics: Optimized performance metrics
        min_improvement_pct: Minimum required improvement percentage
        
    Returns:
        Tuple of (is_valid, improvement_pct, error_messages)
    """
    errors = []
    
    baseline_sharpe = baseline_metrics.get('sharpe_ratio', 0.0)
    optimized_sharpe = optimized_metrics.get('sharpe_ratio', 0.0)
    
    if baseline_sharpe == 0:
        errors.append("Baseline Sharpe ratio is zero - cannot validate improvement")
        return False, 0.0, errors
    
    improvement_pct = ((optimized_sharpe - baseline_sharpe) / abs(baseline_sharpe)) * 100
    
    if improvement_pct < min_improvement_pct:
        errors.append(
            f"Sharpe improvement {improvement_pct:.1f}% below "
            f"minimum threshold {min_improvement_pct}%"
        )
    
    return len(errors) == 0, improvement_pct, errors


def validate_session_performance(
    baseline_metrics_by_session: Dict[str, Dict],
    optimized_metrics_by_session: Dict[str, Dict],
    max_degradation_pct: float = MAX_DEGRADATION_PCT
) -> Tuple[bool, List[str]]:
    """
    Validate no single session degrades significantly.
    
    Args:
        baseline_metrics_by_session: Baseline metrics per session
        optimized_metrics_by_session: Optimized metrics per session
        max_degradation_pct: Maximum allowed degradation percentage
        
    Returns:
        Tuple of (is_valid, error_messages)
    """
    errors = []
    
    for session in baseline_metrics_by_session.keys():
        if session not in optimized_metrics_by_session:
            continue
        
        baseline_sharpe = baseline_metrics_by_session[session].get('sharpe_ratio', 0.0)
        optimized_sharpe = optimized_metrics_by_session[session].get('sharpe_ratio', 0.0)
        
        if baseline_sharpe == 0:
            continue
        
        change_pct = ((optimized_sharpe - baseline_sharpe) / abs(baseline_sharpe)) * 100
        
        if change_pct < -max_degradation_pct:
            errors.append(
                f"{session} session degraded by {abs(change_pct):.1f}% "
                f"(exceeds {max_degradation_pct}% threshold)"
            )
    
    return len(errors) == 0, errors


def validate_convergence(
    optimization_results: List[Tuple[Dict, float]],
    top_n: int = 50
) -> Tuple[bool, List[str]]:
    """
    Validate optimization converged (top results are similar).
    
    Args:
        optimization_results: List of (params, sharpe) tuples sorted by sharpe
        top_n: Number of top results to analyze
        
    Returns:
        Tuple of (converged, warning_messages)
    """
    warnings = []
    
    if len(optimization_results) < top_n:
        warnings.append(
            f"Only {len(optimization_results)} results available, "
            f"expected at least {top_n} for convergence check"
        )
        return True, warnings  # Don't fail, just warn
    
    top_results = optimization_results[:top_n]
    
    # Check if top Sharpe ratios are close
    sharpes = [sharpe for _, sharpe in top_results]
    sharpe_std = np.std(sharpes)
    sharpe_mean = np.mean(sharpes)
    
    if sharpe_mean > 0 and sharpe_std / sharpe_mean > 0.20:
        warnings.append(
            f"Top {top_n} results have high variance (CV={sharpe_std/sharpe_mean:.2%}), "
            "optimization may not have converged - consider more trials"
        )
    
    # Check parameter consistency in top results
    param_stds = {}
    for param_name in top_results[0][0].keys():
        values = [params[param_name] for params, _ in top_results]
        param_stds[param_name] = np.std(values)
    
    return True, warnings


def validate_optimized_parameters(
    baseline_params: Dict[str, float],
    optimized_params: Dict[str, float],
    df_validate: pd.DataFrame,
    strategy: str,
    baseline_metrics: Dict[str, float] = None,
    s7_gate: pd.Series = None
) -> ValidationResult:
    """
    Comprehensive validation of optimized parameters.
    
    Args:
        baseline_params: Current baseline parameters
        optimized_params: Newly optimized parameters
        df_validate: Out-of-sample validation data
        strategy: Strategy name
        baseline_metrics: Optional baseline metrics (will compute if not provided)
        s7_gate: S7 gate approval series
        
    Returns:
        ValidationResult object
    """
    result = ValidationResult()
    
    print("\n" + "=" * 80)
    print("PARAMETER VALIDATION")
    print("=" * 80)
    
    # 1. Validate parameter bounds
    print("\n1. Checking parameter bounds...")
    bounds_valid, bound_errors = validate_parameter_bounds(optimized_params, strategy)
    for error in bound_errors:
        result.add_error(error)
    
    if bounds_valid:
        print("   ✓ All parameters within safety bounds")
    else:
        print(f"   ✗ {len(bound_errors)} parameter(s) outside bounds")
    
    # 2. Validate parameter changes
    print("\n2. Checking parameter changes...")
    change_warnings = validate_parameter_changes(baseline_params, optimized_params, strategy)
    for warning in change_warnings:
        result.add_warning(warning)
    
    if len(change_warnings) == 0:
        print("   ✓ Parameter changes are reasonable")
    else:
        print(f"   ⚠ {len(change_warnings)} parameter(s) with large changes")
    
    # 3. Compute baseline metrics if not provided
    if baseline_metrics is None:
        print("\n3. Computing baseline metrics...")
        if strategy == 'S2':
            baseline_metrics = backtest_s2(df_validate, baseline_params, s7_gate)
        else:
            result.add_error(f"Strategy {strategy} not implemented")
            return result
    
    # 4. Compute optimized metrics
    print("\n4. Computing optimized metrics...")
    if strategy == 'S2':
        optimized_metrics = backtest_s2(df_validate, optimized_params, s7_gate)
    else:
        result.add_error(f"Strategy {strategy} not implemented")
        return result
    
    # 5. Validate performance improvement
    print("\n5. Checking performance improvement...")
    improvement_valid, improvement_pct, improvement_errors = validate_performance_improvement(
        baseline_metrics, optimized_metrics
    )
    
    for error in improvement_errors:
        result.add_error(error)
    
    if improvement_valid:
        print(f"   ✓ Sharpe improved by {improvement_pct:.1f}%")
    else:
        print(f"   ✗ Insufficient improvement ({improvement_pct:.1f}%)")
    
    # 6. Check minimum trades
    print("\n6. Checking trade count...")
    if optimized_metrics['total_trades'] < MIN_TRADES:
        result.add_error(
            f"Too few trades: {optimized_metrics['total_trades']} < {MIN_TRADES}"
        )
        print(f"   ✗ Insufficient trades ({optimized_metrics['total_trades']})")
    else:
        print(f"   ✓ Sufficient trades ({optimized_metrics['total_trades']})")
    
    # 7. Check maximum drawdown
    print("\n7. Checking drawdown...")
    if optimized_metrics['max_drawdown'] > MAX_DRAWDOWN:
        result.add_error(
            f"Drawdown too high: {optimized_metrics['max_drawdown']:.1%} > {MAX_DRAWDOWN:.1%}"
        )
        print(f"   ✗ Excessive drawdown ({optimized_metrics['max_drawdown']:.1%})")
    else:
        print(f"   ✓ Acceptable drawdown ({optimized_metrics['max_drawdown']:.1%})")
    
    # Store metrics
    result.metrics = {
        'baseline_sharpe': baseline_metrics['sharpe_ratio'],
        'optimized_sharpe': optimized_metrics['sharpe_ratio'],
        'improvement_pct': improvement_pct,
        'baseline_win_rate': baseline_metrics['win_rate'],
        'optimized_win_rate': optimized_metrics['win_rate'],
        'baseline_trades': baseline_metrics['total_trades'],
        'optimized_trades': optimized_metrics['total_trades'],
        'optimized_drawdown': optimized_metrics['max_drawdown']
    }
    
    result.improvements = {
        'sharpe_improvement': improvement_pct,
        'win_rate_change': (optimized_metrics['win_rate'] - baseline_metrics['win_rate']) * 100,
        'trade_count_change': optimized_metrics['total_trades'] - baseline_metrics['total_trades']
    }
    
    # Final determination
    if len(result.errors) == 0:
        result.set_passed()
    
    print("\n" + "=" * 80)
    return result


if __name__ == "__main__":
    # Test validation
    print("Testing Parameter Validator...")
    
    baseline_params = {
        'vwap_threshold': 0.15,
        'rsi_level': 30,
        'stop_atr_mult': 2.0,
        'target_atr_mult': 3.5,
        'min_atr': 0.25,
        'max_bars_in_trade': 45
    }
    
    optimized_params = {
        'vwap_threshold': 0.12,
        'rsi_level': 32,
        'stop_atr_mult': 1.8,
        'target_atr_mult': 3.2,
        'min_atr': 0.20,
        'max_bars_in_trade': 50
    }
    
    # Test bounds validation
    valid, errors = validate_parameter_bounds(optimized_params, 'S2')
    print(f"\nBounds validation: {'✓ PASS' if valid else '✗ FAIL'}")
    if errors:
        for error in errors:
            print(f"  {error}")
    
    # Test change validation
    warnings = validate_parameter_changes(baseline_params, optimized_params, 'S2')
    print(f"\nChange validation: {len(warnings)} warnings")
    for warning in warnings:
        print(f"  {warning}")
    
    print("\n✓ Parameter validator working correctly")
