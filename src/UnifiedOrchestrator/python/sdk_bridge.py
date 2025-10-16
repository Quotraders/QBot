#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
TopstepX SDK Bridge - Historical Data Retrieval
Provides historical bar data from TopstepX using the project-x-py SDK
Called by C# HistoricalDataBridgeService to seed initial bars
"""

import asyncio
import json
import sys
import os
import logging
import io
from datetime import datetime, timedelta, timezone
from pathlib import Path

# CRITICAL FIX: Redirect stdout during SDK initialization to suppress JSON logs
# The project-x-py SDK outputs JSON-formatted logs to stdout which breaks C# parsing
_original_stdout = sys.stdout
_log_buffer = io.StringIO()

# Fix Windows console encoding to handle Unicode characters from project-x-py SDK
if sys.platform == 'win32':
    import codecs
    if _original_stdout.encoding != 'utf-8':
        _original_stdout.reconfigure(encoding='utf-8', errors='replace')
    if sys.stderr.encoding != 'utf-8':
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')

# Redirect stdout to buffer to capture SDK logs (we'll discard them)
sys.stdout = _log_buffer

# Suppress all logging to prevent any console output during SDK operations
logging.basicConfig(level=logging.CRITICAL, stream=sys.stderr, format='%(message)s')
logging.getLogger('project_x_py').setLevel(logging.CRITICAL)
logging.getLogger().setLevel(logging.CRITICAL)

# Load environment from .env file
env_path = Path(__file__).parent.parent.parent.parent / '.env'
if env_path.exists():
    with open(env_path, 'r', encoding='utf-8', errors='ignore') as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                os.environ[key.strip()] = value.strip()

try:
    import aiohttp
except ImportError as e:
    # Restore stdout to print error
    sys.stdout = _original_stdout
    print(json.dumps({
        'success': False,
        'error': f'aiohttp not installed: {str(e)}',
        'bars': []
    }))
    sys.exit(1)


async def fetch_historical_bars(symbol: str, days: int = 1, timeframe_minutes: int = 5) -> dict:
    """
    Fetch historical bars from TopstepX SDK
    
    Args:
        symbol: Instrument symbol (ES, NQ, MES, MNQ)
        days: Number of days of historical data
        timeframe_minutes: Bar timeframe in minutes (1, 5, 15, 60)
    
    Returns:
        Dictionary with success status and bars array
    """
    try:
        # Map standard symbols to TopstepX instrument codes
        symbol_map = {
            'ES': 'ES',
            'NQ': 'NQ',
            'MES': 'MES',
            'MNQ': 'MNQ'
        }
        
        topstepx_symbol = symbol_map.get(symbol, symbol)
        
        # Use the existing HTTP adapter at localhost:8765 instead of creating new SDK connection
        # This prevents "Close message received from server" errors from concurrent connections
        adapter_url = 'http://localhost:8765'
        
        # Wait for adapter to be ready (it takes ~20 seconds to initialize)
        max_wait = 30  # seconds
        wait_interval = 1  # second
        adapter_ready = False
        
        async with aiohttp.ClientSession() as session:
            for attempt in range(max_wait):
                try:
                    async with session.get(f"{adapter_url}/health", timeout=aiohttp.ClientTimeout(total=2)) as health_response:
                        if health_response.status == 200:
                            adapter_ready = True
                            break
                except Exception:
                    pass  # Adapter not ready yet
                await asyncio.sleep(wait_interval)
            
            if not adapter_ready:
                return {
                    'success': False,
                    'error': f'HTTP adapter at {adapter_url} not ready after {max_wait} seconds',
                    'symbol': symbol,
                    'bars': []
                }
        
        # Calculate time range
        end_time = datetime.now(timezone.utc)
        start_time = end_time - timedelta(days=days)
        
        # Call the HTTP adapter's historical endpoint
        async with aiohttp.ClientSession() as session:
            # Request historical bars from the adapter
            url = f"{adapter_url}/historical/{topstepx_symbol}"
            params = {
                'days': days,
                'timeframe_minutes': timeframe_minutes,
                'start_time': start_time.isoformat(),
                'end_time': end_time.isoformat()
            }
            
            async with session.get(url, params=params, timeout=aiohttp.ClientTimeout(total=30)) as response:
                if response.status != 200:
                    error_text = await response.text()
                    return {
                        'success': False,
                        'error': f'HTTP adapter error: {response.status} - {error_text}',
                        'symbol': symbol,
                        'bars': []
                    }
                
                data = await response.json()
                
                # Adapter should return: {"success": true, "bars": [...], "count": N}
                if not data.get('success', False):
                    return {
                        'success': False,
                        'error': data.get('error', 'Unknown error from adapter'),
                        'symbol': symbol,
                        'bars': []
                    }
                
                bars = data.get('bars', [])
                
                return {
                    'success': True,
                    'symbol': symbol,
                    'bars': bars,
                    'count': len(bars),
                    'timeframe_minutes': timeframe_minutes,
                    'start': start_time.isoformat(),
                    'end': end_time.isoformat()
                }
        
    except Exception as e:
        return {
            'success': False,
            'error': str(e),
            'symbol': symbol,
            'bars': []
        }


def main():
    """
    Main entry point for command-line usage
    Expects JSON input with: {"symbol": "ES", "days": 1, "timeframe_minutes": 5}
    """
    try:
        # Read input from command line argument
        if len(sys.argv) < 2:
            # Restore stdout to print error
            sys.stdout = _original_stdout
            print(json.dumps({
                'success': False,
                'error': 'Missing input JSON. Usage: sdk_bridge.py \'{"symbol":"ES","days":1,"timeframe_minutes":5}\'',
                'bars': []
            }))
            sys.exit(1)
        
        input_json = sys.argv[1]
        params = json.loads(input_json)
        
        symbol = params.get('symbol', 'ES')
        days = params.get('days', 1)
        timeframe_minutes = params.get('timeframe_minutes', 5)
        
        # Run async fetch (stdout still redirected to buffer during SDK operations)
        result = asyncio.run(fetch_historical_bars(symbol, days, timeframe_minutes))
        
        # Restore original stdout and output ONLY the final JSON result
        sys.stdout = _original_stdout
        print(json.dumps(result))
        
        if result['success']:
            sys.exit(0)
        else:
            sys.exit(1)
            
    except Exception as e:
        # Restore stdout to print error
        sys.stdout = _original_stdout
        print(json.dumps({
            'success': False,
            'error': f'Unexpected error: {str(e)}',
            'bars': []
        }))
        sys.exit(1)


if __name__ == '__main__':
    main()