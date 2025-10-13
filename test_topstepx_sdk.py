"""Test TopstepX SDK with real credentials"""
import asyncio
import os
from project_x_py import TradingSuite, ProjectX

async def test_sdk():
    # Your credentials
    api_key = "J3pePdNU/mvmoRGTygBcNtKbRvL/wSNZ3pFOKCdIy34="
    username = "kevinsuero072897@gmail.com"
    account_name = "PRAC-V2-297693-73603697"  # Full account name from TopstepX

    print("🧪 Testing TopstepX SDK...")
    print(f"Username: {username}")
    print(f"Account: {account_name}\n")

    # Set environment variables (SDK reads credentials from these)
    os.environ['PROJECT_X_API_KEY'] = api_key
    os.environ['PROJECT_X_USERNAME'] = username
    os.environ['PROJECT_X_ACCOUNT_NAME'] = account_name

    try:
        # Create TradingSuite instance - it reads credentials from environment
        suite = await TradingSuite.create(
            instruments=["MNQ", "ES"],
            timeframes=["1min", "5min"]  # Use '1min' not '1m'
        )

        print("✅ TradingSuite created and initialized successfully!")
        print(f"   Connected: {suite.is_connected}")
        print(f"   Instruments: {list(suite.keys())}")

        # Wait a bit for data to arrive
        print("\n⏳ Waiting 5 seconds for market data...")
        await asyncio.sleep(5)

        # Check if we have any bars for ES
        print("\n📊 Checking ES market data...")
        es_instrument = suite["ES"]
        print(f"   ES Instrument: {es_instrument.instrument_info}")

        # Check MNQ
        print("\n📊 Checking MNQ market data...")
        mnq_instrument = suite["MNQ"]
        print(f"   MNQ Instrument: {mnq_instrument.instrument_info}")

        # Get account info
        print("\n💼 Getting account state...")
        try:
            account_info = await suite.client.get_account()
            print(f"   Account Balance: ${account_info.get('balance', 'N/A')}")
            print(f"   Account Status: {account_info.get('status', 'N/A')}")
        except Exception as e:
            print(f"   ⚠️  Account retrieval: {e}")

        # Disconnect
        print("\n🔌 Disconnecting...")
        await suite.disconnect()
        print("✅ Test completed successfully!")

    except Exception as e:
        print(f"\n❌ Error: {type(e).__name__}: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    asyncio.run(test_sdk())
