#!/usr/bin/env python3
"""Test script to find TopstepX SDK historical data methods."""
import asyncio
from project_x_py import TradingSuite

async def main():
    suite = await TradingSuite.from_env(instrument='ES')
    
    print("=== TradingSuite Instance Methods ===")
    methods = [m for m in dir(suite) if not m.startswith('_')]
    for method in sorted(methods):
        if any(keyword in method.lower() for keyword in ['bar', 'history', 'historical', 'candle', 'data']):
            print(f"  ✓ {method}")
    
    print("\n=== Checking suite['ES'] access ===")
    es_data = suite['ES']
    print(f"Type: {type(es_data)}")
    print(f"Methods: {[m for m in dir(es_data) if not m.startswith('_')]}")
    
    print("\n=== Checking data manager ===")
    if hasattr(es_data, 'data'):
        print(f"Data manager type: {type(es_data.data)}")
        data_methods = [m for m in dir(es_data.data) if not m.startswith('_')]
        print(f"Data methods: {data_methods}")
        
        # Check for historical/bar methods
        for method in data_methods:
            if any(keyword in method.lower() for keyword in ['bar', 'history', 'historical', 'candle', 'ohlc', 'fetch']):
                print(f"  ⭐ {method}")
    
    await suite.disconnect()

if __name__ == "__main__":
    asyncio.run(main())
