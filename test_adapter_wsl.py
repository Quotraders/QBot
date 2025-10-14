#!/usr/bin/env python3
import asyncio
import sys
import os
from pathlib import Path

# Add src/adapters to path
sys.path.insert(0, str(Path(__file__).parent / "src" / "adapters"))

from dotenv import load_dotenv
load_dotenv()

# Set required environment variables for retry policy
os.environ['ADAPTER_MAX_RETRIES'] = '3'
os.environ['ADAPTER_BASE_DELAY'] = '1.0'
os.environ['ADAPTER_MAX_DELAY'] = '8.0'
os.environ['ADAPTER_TIMEOUT'] = '30.0'

async def test_adapter():
    print("=== TopstepX Adapter Test ===")
    print(f"Username: {os.getenv('TOPSTEPX_USERNAME')}")
    print(f"Account ID: {os.getenv('TOPSTEPX_ACCOUNT_ID')}")
    print(f"API Key present: {bool(os.getenv('TOPSTEPX_API_KEY'))}")
    
    print("\nImporting adapter...")
    from topstep_x_adapter import TopstepXAdapter
    
    print("Creating adapter instance...")
    adapter = TopstepXAdapter(instruments=['MNQ'])
    
    print("Initializing adapter...")
    await adapter.initialize()
    
    print("✅ Adapter initialized successfully!")
    print(f"Adapter object: {adapter}")
    
    # Keep alive for a bit to see if subscriptions work
    print("\nWaiting 10 seconds to see if data flows...")
    await asyncio.sleep(10)
    
    print("\n✅ Test complete!")

if __name__ == "__main__":
    asyncio.run(test_adapter())
