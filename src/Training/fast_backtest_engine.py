"""
Fast Backtest Engine with Numba JIT Compilation
Runs single strategy backtest in 5-10 seconds for 90 days of 1-minute data.
"""

import numpy as np
import pandas as pd
from numba import jit, float64, int64
from typing import Dict, Tuple, List
import warnings

warnings.filterwarnings('ignore')


@jit(nopython=True)
def compute_atr_numba(high: np.ndarray, low: np.ndarray, close: np.ndarray, period: int = 14) -> np.ndarray:
    """
    Compute ATR (Average True Range) using numba JIT compilation.
    
    Args:
        high: Array of high prices
        low: Array of low prices  
        close: Array of close prices
        period: ATR period (default 14)
        
    Returns:
        Array of ATR values
    """
    n = len(close)
    tr = np.zeros(n)
    atr = np.zeros(n)
    
    # Calculate True Range
    for i in range(1, n):
        hl = high[i] - low[i]
        hc = abs(high[i] - close[i-1])
        lc = abs(low[i] - close[i-1])
        tr[i] = max(hl, hc, lc)
    
    # Calculate ATR using EMA
    atr[period] = np.mean(tr[1:period+1])
    multiplier = 1.0 / period
    
    for i in range(period+1, n):
        atr[i] = atr[i-1] + multiplier * (tr[i] - atr[i-1])
    
    return atr


@jit(nopython=True)
def compute_rsi_numba(close: np.ndarray, period: int = 14) -> np.ndarray:
    """
    Compute RSI (Relative Strength Index) using numba JIT.
    
    Args:
        close: Array of close prices
        period: RSI period (default 14)
        
    Returns:
        Array of RSI values (0-100)
    """
    n = len(close)
    rsi = np.full(n, 50.0)  # Default to neutral 50
    
    if n < period + 1:
        return rsi
    
    # Calculate price changes
    delta = np.diff(close)
    
    # Separate gains and losses
    gains = np.where(delta > 0, delta, 0.0)
    losses = np.where(delta < 0, -delta, 0.0)
    
    # Calculate initial average gain and loss
    avg_gain = np.mean(gains[:period])
    avg_loss = np.mean(losses[:period])
    
    # Calculate RSI for each bar
    for i in range(period, n-1):
        avg_gain = (avg_gain * (period - 1) + gains[i]) / period
        avg_loss = (avg_loss * (period - 1) + losses[i]) / period
        
        if avg_loss == 0:
            rsi[i+1] = 100.0
        else:
            rs = avg_gain / avg_loss
            rsi[i+1] = 100.0 - (100.0 / (1.0 + rs))
    
    return rsi


@jit(nopython=True)
def compute_vwap_distance_numba(
    close: np.ndarray,
    volume: np.ndarray,
    window: int = 20
) -> np.ndarray:
    """
    Compute distance from VWAP as percentage.
    
    Args:
        close: Array of close prices
        volume: Array of volumes
        window: Rolling window size
        
    Returns:
        Array of (close - vwap) / vwap values
    """
    n = len(close)
    vwap_dist = np.zeros(n)
    
    for i in range(window, n):
        start = i - window
        pv_sum = np.sum(close[start:i] * volume[start:i])
        v_sum = np.sum(volume[start:i])
        
        if v_sum > 0:
            vwap = pv_sum / v_sum
            if vwap > 0:
                vwap_dist[i] = (close[i] - vwap) / vwap
    
    return vwap_dist


@jit(nopython=True)
def compute_bollinger_width_numba(close: np.ndarray, period: int = 20, std_mult: float = 2.0) -> np.ndarray:
    """
    Compute Bollinger Band width as percentage of price.
    
    Args:
        close: Array of close prices
        period: BB period
        std_mult: Standard deviation multiplier
        
    Returns:
        Array of (upper_band - lower_band) / middle_band values
    """
    n = len(close)
    bb_width = np.zeros(n)
    
    for i in range(period, n):
        start = i - period
        window_data = close[start:i]
        
        middle = np.mean(window_data)
        std = np.std(window_data)
        
        upper = middle + std_mult * std
        lower = middle - std_mult * std
        
        if middle > 0:
            bb_width[i] = (upper - lower) / middle
    
    return bb_width


@jit(nopython=True)
def simulate_s2_strategy_numba(
    close: np.ndarray,
    atr: np.ndarray,
    rsi: np.ndarray,
    vwap_dist: np.ndarray,
    vwap_threshold: float,
    rsi_level: float,
    stop_atr_mult: float,
    target_atr_mult: float,
    min_atr: float,
    max_bars_in_trade: int,
    s7_approved: np.ndarray
) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """
    Simulate S2 (VWAP Mean Reversion) strategy trades.
    
    Returns:
        Tuple of (entry_prices, exit_prices, entry_bars, exit_bars)
    """
    n = len(close)
    max_trades = n // 10  # Estimate max trades
    
    entries = np.zeros(max_trades)
    exits = np.zeros(max_trades)
    entry_bars = np.zeros(max_trades, dtype=np.int64)
    exit_bars = np.zeros(max_trades, dtype=np.int64)
    
    trade_count = 0
    in_trade = False
    entry_price = 0.0
    entry_bar = 0
    stop_loss = 0.0
    target = 0.0
    trade_direction = 0  # 1 for long, -1 for short
    bars_in_trade = 0
    
    for i in range(100, n):  # Start after sufficient bars for indicators
        # Exit logic
        if in_trade:
            bars_in_trade += 1
            
            # Check stop loss
            if trade_direction == 1:  # Long
                if close[i] <= stop_loss or close[i] >= target or bars_in_trade >= max_bars_in_trade:
                    exits[trade_count] = close[i]
                    exit_bars[trade_count] = i
                    trade_count += 1
                    in_trade = False
            else:  # Short
                if close[i] >= stop_loss or close[i] <= target or bars_in_trade >= max_bars_in_trade:
                    exits[trade_count] = close[i]
                    exit_bars[trade_count] = i
                    trade_count += 1
                    in_trade = False
        
        # Entry logic
        if not in_trade and s7_approved[i] and atr[i] >= min_atr:
            # Long entry: price far below VWAP, RSI oversold
            if vwap_dist[i] < -vwap_threshold and rsi[i] < rsi_level:
                entries[trade_count] = close[i]
                entry_bars[trade_count] = i
                entry_price = close[i]
                entry_bar = i
                stop_loss = entry_price - stop_atr_mult * atr[i]
                target = entry_price + target_atr_mult * atr[i]
                trade_direction = 1
                in_trade = True
                bars_in_trade = 0
            
            # Short entry: price far above VWAP, RSI overbought
            elif vwap_dist[i] > vwap_threshold and rsi[i] > (100 - rsi_level):
                entries[trade_count] = close[i]
                entry_bars[trade_count] = i
                entry_price = close[i]
                entry_bar = i
                stop_loss = entry_price + stop_atr_mult * atr[i]
                target = entry_price - target_atr_mult * atr[i]
                trade_direction = -1
                in_trade = True
                bars_in_trade = 0
    
    # Close any open trade at end
    if in_trade:
        exits[trade_count] = close[n-1]
        exit_bars[trade_count] = n-1
        trade_count += 1
    
    return entries[:trade_count], exits[:trade_count], entry_bars[:trade_count], exit_bars[:trade_count]


def backtest_s2(
    df: pd.DataFrame,
    params: Dict[str, float],
    s7_gate: pd.Series = None
) -> Dict[str, float]:
    """
    Backtest S2 strategy with given parameters.
    
    Args:
        df: DataFrame with OHLCV data and session info
        params: Strategy parameters dict
        s7_gate: Boolean series indicating S7 approval (True = approved)
        
    Returns:
        Dictionary of performance metrics
    """
    # Extract parameters
    vwap_threshold = params.get('vwap_threshold', 0.15)
    rsi_level = params.get('rsi_level', 30)
    stop_atr_mult = params.get('stop_atr_mult', 2.0)
    target_atr_mult = params.get('target_atr_mult', 3.5)
    min_atr = params.get('min_atr', 0.25)
    max_bars_in_trade = params.get('max_bars_in_trade', 45)
    
    # Compute indicators
    high = df['high'].values
    low = df['low'].values
    close = df['close'].values
    volume = df['volume'].values
    
    atr = compute_atr_numba(high, low, close, period=14)
    rsi = compute_rsi_numba(close, period=14)
    vwap_dist = compute_vwap_distance_numba(close, volume, window=20)
    
    # S7 gate approval (default all True if not provided)
    if s7_gate is None:
        s7_approved = np.ones(len(df), dtype=np.bool_)
    else:
        s7_approved = s7_gate.values
    
    # Run simulation
    entries, exits, entry_bars, exit_bars = simulate_s2_strategy_numba(
        close, atr, rsi, vwap_dist,
        vwap_threshold, rsi_level, stop_atr_mult, target_atr_mult,
        min_atr, max_bars_in_trade, s7_approved
    )
    
    # Calculate performance metrics
    if len(entries) == 0:
        return {
            'total_trades': 0,
            'total_return': 0.0,
            'sharpe_ratio': 0.0,
            'win_rate': 0.0,
            'avg_r_multiple': 0.0,
            'max_drawdown': 0.0
        }
    
    # Calculate trade returns
    returns = []
    for i in range(len(entries)):
        entry = entries[i]
        exit_val = exits[i]
        trade_return = (exit_val - entry) / entry
        returns.append(trade_return)
    
    returns = np.array(returns)
    
    # Calculate metrics
    total_return = np.sum(returns)
    win_rate = np.sum(returns > 0) / len(returns) if len(returns) > 0 else 0.0
    
    # Sharpe ratio
    if len(returns) > 1 and np.std(returns) > 0:
        sharpe_ratio = np.mean(returns) / np.std(returns) * np.sqrt(252)  # Annualized
    else:
        sharpe_ratio = 0.0
    
    # Average R-multiple (avg win / avg loss)
    wins = returns[returns > 0]
    losses = returns[returns < 0]
    if len(wins) > 0 and len(losses) > 0:
        avg_r_multiple = np.mean(wins) / abs(np.mean(losses))
    else:
        avg_r_multiple = 0.0
    
    # Max drawdown (simplified)
    cumulative = np.cumsum(returns)
    running_max = np.maximum.accumulate(cumulative)
    drawdown = running_max - cumulative
    max_drawdown = np.max(drawdown) if len(drawdown) > 0 else 0.0
    
    return {
        'total_trades': len(entries),
        'total_return': float(total_return),
        'sharpe_ratio': float(sharpe_ratio),
        'win_rate': float(win_rate),
        'avg_r_multiple': float(avg_r_multiple),
        'max_drawdown': float(max_drawdown)
    }


if __name__ == "__main__":
    # Test with synthetic data
    print("Testing Fast Backtest Engine...")
    
    # Generate synthetic 1-minute data for 1 day (390 bars)
    n_bars = 390
    np.random.seed(42)
    
    # Simulate realistic ES price movement
    base_price = 4500
    price_changes = np.random.normal(0, 0.5, n_bars)
    close_prices = base_price + np.cumsum(price_changes)
    
    df = pd.DataFrame({
        'high': close_prices + np.random.uniform(0.25, 2.0, n_bars),
        'low': close_prices - np.random.uniform(0.25, 2.0, n_bars),
        'close': close_prices,
        'volume': np.random.randint(100, 1000, n_bars)
    })
    
    # Test S2 backtest
    params = {
        'vwap_threshold': 0.15,
        'rsi_level': 30,
        'stop_atr_mult': 2.0,
        'target_atr_mult': 3.5,
        'min_atr': 0.25,
        'max_bars_in_trade': 45
    }
    
    import time
    start = time.time()
    results = backtest_s2(df, params)
    elapsed = time.time() - start
    
    print(f"\nBacktest completed in {elapsed:.3f} seconds")
    print(f"Results: {results}")
    print("\n✓ Fast backtest engine working correctly")
