#!/bin/bash
# Test WebSocket connection in detail

set -a
source /workspaces/QBot/.env
set +a

echo "=== Testing TopstepX WebSocket Connection ==="
echo "API Key: ${TOPSTEPX_API_KEY:0:10}..."
echo "Username: $TOPSTEPX_USERNAME"
echo "Account: $TOPSTEPX_ACCOUNT_ID"
echo ""

# Create a simple test script
cat > /tmp/test_ws.py << 'EOF'
import asyncio
import os
import sys

try:
    from project_x_py import TradingSuite
    print("✅ SDK imported successfully")
except ImportError as e:
    print(f"❌ Failed to import SDK: {e}")
    sys.exit(1)

async def test_connection():
    print(f"🔑 API Key present: {bool(os.getenv('TOPSTEPX_API_KEY'))}")
    print(f"👤 Username: {os.getenv('TOPSTEPX_USERNAME')}")
    print(f"🔢 Account ID: {os.getenv('TOPSTEPX_ACCOUNT_ID')}")
    print("")
    
    try:
        print("🚀 Initializing TradingSuite...")
        suite = TradingSuite(instruments=["ES", "NQ"])
        print("✅ TradingSuite created")
        
        print("🔌 Connecting...")
        await suite.connect()
        print("✅ Connected successfully!")
        
        # Wait a moment to see if connection stays alive
        await asyncio.sleep(3)
        
        print("📊 Checking connection status...")
        # Try to get some data
        print("✅ Connection test complete!")
        
        await suite.disconnect()
        print("👋 Disconnected cleanly")
        
    except Exception as e:
        print(f"❌ Connection failed: {type(e).__name__}: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    asyncio.run(test_connection())
EOF

echo "Running WebSocket connection test..."
/home/codespace/.python/current/bin/python3 /tmp/test_ws.py
echo ""
echo "=== Test Complete ==="
