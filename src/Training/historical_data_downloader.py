"""
Historical Data Downloader for TopstepX API
Downloads ES and NQ 1-minute bars for last 90 days and saves as parquet files.
"""

import os
import sys
from datetime import datetime, timedelta
from typing import List, Dict, Any
import pandas as pd
import pytz
import requests
from dotenv import load_dotenv
from session_grouper import cme_trading_day, ET


# Load environment variables
load_dotenv()

# API Configuration
API_BASE_URL = os.getenv('TOPSTEPX_API_BASE_URL', 'https://api.topstepx.com')
API_KEY = os.getenv('TOPSTEPX_API_KEY')
API_SECRET = os.getenv('TOPSTEPX_API_SECRET')

# Data configuration
SYMBOLS = ['ES', 'NQ']
LOOKBACK_DAYS = 90
DATA_DIR = os.path.join(os.path.dirname(__file__), 'data')


def download_historical_bars(
    symbol: str,
    start_date: datetime,
    end_date: datetime,
    interval: str = '1m'
) -> pd.DataFrame:
    """
    Download historical 1-minute bars from TopstepX API.
    
    Args:
        symbol: Trading symbol (ES or NQ)
        start_date: Start date (UTC)
        end_date: End date (UTC)
        interval: Bar interval (default: '1m')
        
    Returns:
        DataFrame with columns: timestamp, open, high, low, close, volume
    """
    print(f"Downloading {symbol} bars from {start_date.date()} to {end_date.date()}...")
    
    # TopstepX API endpoint (adjust based on actual API)
    endpoint = f"{API_BASE_URL}/v1/marketdata/history"
    
    headers = {
        'Authorization': f'Bearer {API_KEY}',
        'Content-Type': 'application/json'
    }
    
    params = {
        'symbol': symbol,
        'interval': interval,
        'start': start_date.isoformat(),
        'end': end_date.isoformat()
    }
    
    try:
        response = requests.get(endpoint, headers=headers, params=params, timeout=60)
        response.raise_for_status()
        
        data = response.json()
        
        # Parse response (adjust based on actual API response format)
        if 'bars' in data:
            bars = data['bars']
        elif 'data' in data:
            bars = data['data']
        else:
            bars = data
        
        # Convert to DataFrame
        df = pd.DataFrame(bars)
        
        # Standardize column names
        df = df.rename(columns={
            'time': 'timestamp',
            't': 'timestamp',
            'o': 'open',
            'h': 'high',
            'l': 'low',
            'c': 'close',
            'v': 'volume'
        })
        
        # Ensure timestamp is datetime
        df['timestamp'] = pd.to_datetime(df['timestamp'])
        
        # Convert from UTC to Eastern Time
        df['timestamp'] = df['timestamp'].dt.tz_localize('UTC').dt.tz_convert(ET)
        
        print(f"  Downloaded {len(df)} bars")
        
        return df
        
    except requests.exceptions.RequestException as e:
        print(f"  ERROR: Failed to download data: {e}")
        raise
    except Exception as e:
        print(f"  ERROR: Failed to parse data: {e}")
        raise


def validate_data(df: pd.DataFrame, symbol: str) -> None:
    """
    Validate downloaded data for quality issues.
    
    Args:
        df: DataFrame with bar data
        symbol: Symbol name for error messages
        
    Raises:
        ValueError: If validation fails
    """
    print(f"Validating {symbol} data...")
    
    # Check for duplicate timestamps
    duplicates = df[df.duplicated('timestamp', keep=False)]
    if len(duplicates) > 0:
        raise ValueError(
            f"Found {len(duplicates)} duplicate timestamps in {symbol} data"
        )
    
    # Check for missing data gaps during market hours
    df_sorted = df.sort_values('timestamp')
    time_diffs = df_sorted['timestamp'].diff()
    
    # Expected interval is 1 minute
    expected_interval = timedelta(minutes=1)
    
    # Allow gaps up to 5 minutes (maintenance break causes gaps)
    max_gap = timedelta(minutes=5)
    large_gaps = time_diffs[time_diffs > max_gap]
    
    if len(large_gaps) > 0:
        print(f"  WARNING: Found {len(large_gaps)} gaps > 5 minutes")
        # Don't fail on gaps - they're expected during maintenance and weekends
    
    # Check for null values
    null_counts = df.isnull().sum()
    if null_counts.any():
        raise ValueError(
            f"Found null values in {symbol} data: {null_counts[null_counts > 0]}"
        )
    
    # Check price reasonableness (ES typically 4000-5000, NQ 15000-20000)
    if symbol == 'ES':
        if df['close'].min() < 2000 or df['close'].max() > 10000:
            raise ValueError(
                f"ES prices out of reasonable range: "
                f"min={df['close'].min()}, max={df['close'].max()}"
            )
    elif symbol == 'NQ':
        if df['close'].min() < 8000 or df['close'].max() > 30000:
            raise ValueError(
                f"NQ prices out of reasonable range: "
                f"min={df['close'].min()}, max={df['close'].max()}"
            )
    
    print(f"  ✓ Validation passed: {len(df)} bars, "
          f"date range {df['timestamp'].min().date()} to {df['timestamp'].max().date()}")


def add_trading_day_info(df: pd.DataFrame) -> pd.DataFrame:
    """
    Add CME trading day information to DataFrame.
    
    Args:
        df: DataFrame with timestamp column in Eastern Time
        
    Returns:
        DataFrame with added trading_day, session_name, is_maintenance columns
    """
    results = df['timestamp'].apply(lambda dt: cme_trading_day(dt))
    
    df['trading_day'] = results.apply(lambda x: x[0])
    df['session_name'] = results.apply(lambda x: x[1])
    df['is_maintenance'] = results.apply(lambda x: x[2])
    
    return df


def save_to_parquet(df: pd.DataFrame, symbol: str) -> str:
    """
    Save DataFrame to parquet file.
    
    Args:
        df: DataFrame to save
        symbol: Symbol name for filename
        
    Returns:
        Path to saved file
    """
    # Ensure data directory exists
    os.makedirs(DATA_DIR, exist_ok=True)
    
    filename = f"{symbol}_90d.parquet"
    filepath = os.path.join(DATA_DIR, filename)
    
    # Save with compression
    df.to_parquet(filepath, compression='snappy', index=False)
    
    file_size_mb = os.path.getsize(filepath) / (1024 * 1024)
    print(f"  Saved to {filepath} ({file_size_mb:.2f} MB)")
    
    return filepath


def download_all_symbols() -> Dict[str, str]:
    """
    Download historical data for all configured symbols.
    
    Returns:
        Dict mapping symbol to saved file path
    """
    if not API_KEY:
        raise ValueError(
            "TOPSTEPX_API_KEY not found in environment. "
            "Please set it in .env file or environment variables."
        )
    
    # Calculate date range
    end_date = datetime.now(pytz.UTC)
    start_date = end_date - timedelta(days=LOOKBACK_DAYS)
    
    print("=" * 80)
    print("Historical Data Download")
    print("=" * 80)
    print(f"Date range: {start_date.date()} to {end_date.date()}")
    print(f"Symbols: {', '.join(SYMBOLS)}")
    print()
    
    results = {}
    
    for symbol in SYMBOLS:
        try:
            # Download data
            df = download_historical_bars(symbol, start_date, end_date)
            
            # Validate data
            validate_data(df, symbol)
            
            # Add trading day info
            df = add_trading_day_info(df)
            
            # Save to parquet
            filepath = save_to_parquet(df, symbol)
            
            results[symbol] = filepath
            print()
            
        except Exception as e:
            print(f"ERROR: Failed to process {symbol}: {e}")
            sys.exit(1)
    
    print("=" * 80)
    print("Download complete!")
    print("=" * 80)
    
    return results


if __name__ == "__main__":
    # Run download
    results = download_all_symbols()
    
    # Print summary
    print("\nSummary:")
    for symbol, filepath in results.items():
        df = pd.read_parquet(filepath)
        print(f"  {symbol}: {len(df)} bars, "
              f"{df['trading_day'].nunique()} trading days")
