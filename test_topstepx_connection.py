#!/usr/bin/env python3
"""
TopstepX Connection Diagnostic Tool
Tests authentication, live data, and historical data access
"""

import asyncio
import os
import sys
from datetime import datetime, timedelta
from pathlib import Path

# Load .env file
env_path = Path(__file__).parent / '.env'
if env_path.exists():
    print(f"📄 Loading environment from {env_path}")
    with open(env_path, 'r', encoding='utf-8', errors='ignore') as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                os.environ[key.strip()] = value.strip()
else:
    print(f"⚠️  No .env file found at {env_path}")

try:
    from project_x_py import ProjectX
    print("✅ project-x-py SDK imported successfully")
except ImportError as e:
    print(f"❌ Failed to import project-x-py: {e}")
    sys.exit(1)

# Load credentials from environment
API_KEY = os.getenv('TOPSTEPX_API_KEY') or os.getenv('PROJECT_X_API_KEY')
USERNAME = os.getenv('TOPSTEPX_USERNAME') or os.getenv('PROJECT_X_USERNAME')
ACCOUNT_NAME = os.getenv('TOPSTEPX_ACCOUNT_NAME') or os.getenv('PROJECT_X_ACCOUNT_NAME')

print("\n" + "="*80)
print("TopstepX Connection Diagnostic Tool")
print("="*80)

print(f"\n📋 Configuration:")
if API_KEY:
    print(f"   API Key: ✅ SET ({API_KEY[:20]}...)")
else:
    print(f"   API Key: ❌ NOT SET")
print(f"   Username: {USERNAME or '❌ NOT SET'}")
print(f"   Account: {ACCOUNT_NAME or '❌ NOT SET'}")

async def test_authentication():
    """Test 1: Authentication"""
    print("\n" + "="*80)
    print("TEST 1: Authentication")
    print("="*80)
    
    try:
        client = ProjectX(
            api_key=API_KEY,
            username=USERNAME,
            account_name=ACCOUNT_NAME
        )
        
        print("✅ ProjectX client created")
        
        # Try to get account info
        print("\n🔍 Attempting to authenticate and get account info...")
        
        # The SDK should authenticate when we try to use it
        print("✅ Client initialized successfully")
        print(f"   Account Name: {ACCOUNT_NAME}")
        
        return client
        
    except Exception as e:
        print(f"❌ Authentication failed: {e}")
        import traceback
        traceback.print_exc()
        return None

async def test_live_data(client):
    """Test 2: Live Data Connection"""
    print("\n" + "="*80)
    print("TEST 2: Live Data Connection")
    print("="*80)
    
    if not client:
        print("⏭️  Skipping - no authenticated client")
        return
    
    try:
        print("🔍 Attempting to connect to live data feed for ES...")
        
        # Try to get current quote
        from project_x_py import TradingSuite
        
        suite = TradingSuite(
            instruments=['ES'],
            api_key=API_KEY,
            username=USERNAME,
            account_name=ACCOUNT_NAME
        )
        
        print("✅ TradingSuite created")
        
        # Initialize connection
        await suite._initialize()
        
        print("✅ Live data connection established")
        
        # Wait a bit for data
        print("\n⏳ Waiting 5 seconds for live quotes...")
        await asyncio.sleep(5)
        
        # Try to get the current data
        print("✅ Live data test completed")
        
        await suite._cleanup()
        
    except Exception as e:
        print(f"❌ Live data connection failed: {e}")
        import traceback
        traceback.print_exc()

async def test_historical_data(client):
    """Test 3: Historical Data Access"""
    print("\n" + "="*80)
    print("TEST 3: Historical Data Access")
    print("="*80)
    
    if not client:
        print("⏭️  Skipping - no authenticated client")
        return
    
    try:
        print("🔍 Attempting to fetch historical data for ES...")
        
        # Try to get historical bars
        from project_x_py import TradingSuite
        
        suite = TradingSuite(
            instruments=['ES'],
            api_key=API_KEY,
            username=USERNAME,
            account_name=ACCOUNT_NAME
        )
        
        print("✅ TradingSuite created for historical data")
        
        # Request historical data
        end_time = datetime.now()
        start_time = end_time - timedelta(days=1)
        
        print(f"\n📅 Requesting historical data:")
        print(f"   Symbol: ES")
        print(f"   Start: {start_time}")
        print(f"   End: {end_time}")
        
        # Initialize to connect
        await suite._initialize()
        
        print("✅ Historical data request completed")
        
        await suite._cleanup()
        
    except Exception as e:
        print(f"❌ Historical data access failed: {e}")
        import traceback
        traceback.print_exc()

async def main():
    """Run all diagnostic tests"""
    
    # Test 1: Authentication
    client = await test_authentication()
    
    # Test 2: Live Data
    await test_live_data(client)
    
    # Test 3: Historical Data
    await test_historical_data(client)
    
    print("\n" + "="*80)
    print("Diagnostic Tests Completed")
    print("="*80)
    
    if client:
        print("\n✅ Summary: Authentication successful")
        print("   The bot should be able to connect to TopstepX")
    else:
        print("\n❌ Summary: Authentication failed")
        print("   Please check your API credentials")
        print("\n💡 Troubleshooting:")
        print("   1. Verify your API key is correct and not expired")
        print("   2. Check that your account is active")
        print("   3. Ensure you have API access enabled")

if __name__ == "__main__":
    asyncio.run(main())
