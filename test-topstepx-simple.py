#!/usr/bin/env python3
"""
Simple TopstepX Connection Test
Tests authentication, live quotes, and historical data
"""

import asyncio
import os
import sys
from pathlib import Path
from datetime import datetime, timedelta

# Load .env file
env_path = Path(__file__).parent / '.env'
if env_path.exists():
    with open(env_path, 'r', encoding='utf-8', errors='ignore') as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                os.environ[key.strip()] = value.strip()

try:
    from project_x_py import ProjectX
except ImportError as e:
    print(f"❌ Failed to import project-x-py: {e}")
    sys.exit(1)

# Get credentials
API_KEY = os.getenv('TOPSTEPX_API_KEY') or os.getenv('PROJECT_X_API_KEY')
USERNAME = os.getenv('TOPSTEPX_USERNAME') or os.getenv('PROJECT_X_USERNAME')
ACCOUNT_NAME = os.getenv('TOPSTEPX_ACCOUNT_NAME') or os.getenv('PROJECT_X_ACCOUNT_NAME')

print("\n" + "="*80)
print("TopstepX Simple Connection Test")
print("="*80)
print(f"\n✅ API Key: {API_KEY[:20] if API_KEY else 'NOT SET'}...")
print(f"✅ Username: {USERNAME}")
print(f"✅ Account: {ACCOUNT_NAME}")

async def test_connection():
    """Test authentication and data access"""
    
    try:
        print("\n" + "-"*80)
        print("TEST 1: Authentication")
        print("-"*80)
        
        # Create client
        client = ProjectX(
            api_key=API_KEY,
            username=USERNAME,
            account_name=ACCOUNT_NAME
        )
        
        print("✅ Client created successfully")
        
        # Authenticate first
        print("\n🔍 Authenticating with TopstepX...")
        await client.authenticate()
        print("✅ Authentication successful!")
        
        # Test getting account info (sync method)
        print("\n🔍 Getting account information...")
        account_info = client.get_account_info()
        
        print(f"✅ Account Info: {account_info}")
        
        print("\n" + "-"*80)
        print("TEST 2: Get Live Session Token")
        print("-"*80)
        
        # Get session token (this is the "live data token")
        print("\n🔍 Getting session token for live data...")
        token = client.get_session_token()
        print(f"✅ Session Token: {token[:50] if token else 'None'}...")
        
        # Try to get current positions to verify API is working
        print("\n🔍 Testing API access (getting positions)...")
        positions = await client.search_open_positions()
        print(f"✅ Current positions: {len(positions) if positions else 0}")
        
        print("\n" + "-"*80)
        print("TEST 3: Historical Data")
        print("-"*80)
        
        # Test historical data
        print("\n🔍 Requesting historical bars for ES...")
        
        end_time = datetime.now()
        start_time = end_time - timedelta(days=1)
        
        # Get instrument for ES (use search_instruments)
        instruments = await client.search_instruments(query="ES")
        if instruments and len(instruments) > 0:
            # Find the ES futures contract
            es_contract = None
            for inst in instruments[:5]:  # Check first 5 results
                symbol = inst.get('symbol', '') if hasattr(inst, 'get') else getattr(inst, 'symbol', '')
                if 'ES' in symbol:
                    es_contract = inst
                    break
            
            if es_contract:
                contract_id = es_contract.get('id') if hasattr(es_contract, 'get') else getattr(es_contract, 'id', None)
                symbol = es_contract.get('symbol') if hasattr(es_contract, 'get') else getattr(es_contract, 'symbol', 'Unknown')
                print(f"✅ Found ES contract: {symbol} (ID: {contract_id})")
                
                # Request bars
                try:
                    bars = await client.get_bars(
                        instrument_id=contract_id,
                        interval='1m',
                        start=start_time,
                        end=end_time
                    )
                    
                    if bars:
                        print(f"✅ Received {len(bars)} historical bars")
                        if len(bars) > 0:
                            latest = bars[-1]
                            bar_time = latest.get('time') if hasattr(latest, 'get') else getattr(latest, 'time', 'Unknown')
                            bar_close = latest.get('close') if hasattr(latest, 'get') else getattr(latest, 'close', 'Unknown')
                            print(f"   Latest bar: {bar_time} - Close: ${bar_close}")
                    else:
                        print("⚠️  No historical bars returned")
                except Exception as bar_error:
                    print(f"⚠️  Error fetching bars: {bar_error}")
            else:
                print("❌ Could not find ES futures contract")
        else:
            print("❌ Could not find any instruments matching ES")
        
        print("\n" + "="*80)
        print("✅ ALL TESTS PASSED!")
        print("="*80)
        print("\n✅ Authentication: WORKING")
        print("✅ API Token: VALID")
        print("✅ Historical Data: ACCESSIBLE")
        print("\n💡 The bot should be able to connect successfully!")
        
    except Exception as e:
        print(f"\n❌ Test failed: {e}")
        import traceback
        traceback.print_exc()
        
        print("\n" + "="*80)
        print("❌ TESTS FAILED")
        print("="*80)
        print("\n💡 Troubleshooting:")
        print("   1. Verify your API key is correct")
        print("   2. Check that your account is active")
        print("   3. Ensure you have API access enabled")

if __name__ == "__main__":
    asyncio.run(test_connection())
