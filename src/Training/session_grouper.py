"""
Session Grouper - CME Trading Day Logic
Matches C# MarketTimeService exactly for consistent session detection.
"""

from datetime import datetime, time, timedelta
from typing import Tuple
import pytz


# Timezone for Eastern Time
ET = pytz.timezone('US/Eastern')

# Session time boundaries (Eastern Time)
PREMARKET_START = time(4, 0, 0)
MARKET_OPEN = time(9, 30, 0)
MARKET_CLOSE = time(16, 0, 0)
POSTMARKET_END = time(20, 0, 0)

# CME futures market hours (different from stock market)
CME_OPEN_SUNDAY = time(18, 0, 0)  # Sunday 6:00 PM ET
CME_CLOSE_FRIDAY = time(17, 0, 0)  # Friday 5:00 PM ET
CME_MAINTENANCE_START = time(17, 0, 0)  # Daily 5:00 PM ET
CME_MAINTENANCE_END = time(18, 0, 0)    # Daily 6:00 PM ET


def determine_market_session(dt_et: datetime) -> str:
    """
    Determine market session matching C# MarketTimeService.DetermineMarketSession().
    
    Args:
        dt_et: DateTime in Eastern Time
        
    Returns:
        Session name: "Closed", "PreMarket", "Open", "PostMarket"
    """
    # Handle weekends
    if dt_et.weekday() in (5, 6):  # Saturday=5, Sunday=6
        return "Closed"
    
    t = dt_et.time()
    
    # Market session times (Eastern Time)
    if t < PREMARKET_START:
        return "Closed"
    elif t < MARKET_OPEN:
        return "PreMarket"
    elif t < MARKET_CLOSE:
        return "Open"
    elif t < POSTMARKET_END:
        return "PostMarket"
    else:
        return "Closed"


def cme_trading_day(timestamp_et: datetime) -> Tuple[datetime, str, bool]:
    """
    Determine CME trading day for ES/NQ futures.
    
    CME Trading Day Rules:
    - Market opens Sunday 6:00 PM ET (that's Monday's trading day)
    - Market closes Friday 5:00 PM ET
    - Daily maintenance break 5:00-6:00 PM ET (no trading)
    - Any timestamp during maintenance break gets special "maintenance" flag
    - A bar at Friday 7:00 PM ET belongs to Friday's trading day
    - A bar at Saturday 2:00 AM ET belongs to Friday's trading day (until Sunday 6 PM)
    
    Args:
        timestamp_et: DateTime in Eastern Time
        
    Returns:
        Tuple of (trading_day_date, session_name, is_maintenance)
        - trading_day_date: Date object representing the trading day
        - session_name: "Overnight", "RTH", "PostRTH"
        - is_maintenance: True if during daily maintenance break
    """
    t = timestamp_et.time()
    weekday = timestamp_et.weekday()  # Monday=0, Sunday=6
    
    # Check for maintenance break (5-6 PM daily)
    is_maintenance = CME_MAINTENANCE_START <= t < CME_MAINTENANCE_END
    
    # Determine session type for parameter optimization
    if MARKET_OPEN <= t < MARKET_CLOSE:
        session_name = "RTH"  # Regular Trading Hours
    elif MARKET_CLOSE <= t < CME_MAINTENANCE_START:
        session_name = "PostRTH"  # Post-RTH before maintenance
    else:
        session_name = "Overnight"  # Everything else including pre-market
    
    # Determine trading day
    # If it's before CME open (6 PM), it belongs to current calendar day
    # If it's after CME open (6 PM) but before midnight, it belongs to next day
    # Special handling for Sunday opening
    
    if weekday == 6:  # Sunday
        if t >= CME_OPEN_SUNDAY:
            # Sunday evening after 6 PM = Monday's trading day
            trading_day = (timestamp_et + timedelta(days=1)).date()
        else:
            # Sunday before 6 PM = still Friday's trading day (market closed)
            # Go back to previous Friday
            days_back = 2  # Sunday to Friday
            trading_day = (timestamp_et - timedelta(days=days_back)).date()
    
    elif weekday == 5:  # Saturday
        # Saturday belongs to Friday's trading day (market closed until Sunday 6 PM)
        days_back = 1  # Saturday to Friday
        trading_day = (timestamp_et - timedelta(days=days_back)).date()
    
    else:  # Monday through Friday
        if t >= CME_MAINTENANCE_END:
            # After 6 PM = next calendar day's trading session
            trading_day = (timestamp_et + timedelta(days=1)).date()
        else:
            # Before 6 PM = current calendar day's trading session
            trading_day = timestamp_et.date()
    
    return trading_day, session_name, is_maintenance


def group_bars_by_trading_day(df, timestamp_col='timestamp'):
    """
    Add trading day information to a DataFrame of bars.
    
    Args:
        df: DataFrame with timestamp column in Eastern Time
        timestamp_col: Name of timestamp column
        
    Returns:
        DataFrame with added columns: trading_day, session_name, is_maintenance
    """
    # Ensure timestamps are datetime objects
    if not isinstance(df[timestamp_col].dtype, datetime):
        df[timestamp_col] = pd.to_datetime(df[timestamp_col])
    
    # Apply CME trading day logic
    results = df[timestamp_col].apply(cme_trading_day)
    
    df['trading_day'] = results.apply(lambda x: x[0])
    df['session_name'] = results.apply(lambda x: x[1])
    df['is_maintenance'] = results.apply(lambda x: x[2])
    
    return df


def validate_no_maintenance_crossings(df):
    """
    Validate that no trades or episodes cross the maintenance break.
    
    Args:
        df: DataFrame with is_maintenance flag
        
    Raises:
        ValueError: If any bar during maintenance period is present
    """
    maintenance_bars = df[df['is_maintenance'] == True]
    if len(maintenance_bars) > 0:
        raise ValueError(
            f"Found {len(maintenance_bars)} bars during maintenance break (5-6 PM ET). "
            "Episodes must not cross maintenance window."
        )


if __name__ == "__main__":
    # Test examples
    import pandas as pd
    
    # Create test timestamps
    test_cases = [
        # (timestamp_str, expected_session, description)
        ("2025-01-13 14:30:00", "RTH", "Monday 2:30 PM - during RTH"),
        ("2025-01-13 16:30:00", "PostRTH", "Monday 4:30 PM - post RTH"),
        ("2025-01-13 19:00:00", "Overnight", "Monday 7:00 PM - overnight (next day)"),
        ("2025-01-17 19:00:00", "Overnight", "Friday 7:00 PM - still Friday's trading day"),
        ("2025-01-18 02:00:00", "Overnight", "Saturday 2:00 AM - Friday's trading day"),
        ("2025-01-19 19:00:00", "Overnight", "Sunday 7:00 PM - Monday's trading day"),
    ]
    
    print("CME Trading Day Logic Tests:")
    print("=" * 80)
    
    for ts_str, expected_session, description in test_cases:
        dt = datetime.strptime(ts_str, "%Y-%m-%d %H:%M:%S")
        dt_et = ET.localize(dt)
        
        trading_day, session_name, is_maintenance = cme_trading_day(dt_et)
        
        print(f"\n{description}")
        print(f"  Timestamp: {ts_str} ET")
        print(f"  Trading Day: {trading_day}")
        print(f"  Session: {session_name} (expected: {expected_session})")
        print(f"  Maintenance: {is_maintenance}")
        print(f"  ✓ PASS" if session_name == expected_session else f"  ✗ FAIL")
