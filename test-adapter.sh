#!/bin/bash
# Test TopstepX Python adapter

# Load .env file
set -a
source /workspaces/QBot/.env
set +a

# Test 1: Validate SDK
echo "=== Test 1: Validate SDK ==="
/home/codespace/.python/current/bin/python3 /workspaces/QBot/src/adapters/topstep_x_adapter.py validate_sdk
echo ""

# Test 2: Initialize adapter (one-shot mode)
echo "=== Test 2: Initialize Adapter ==="
/home/codespace/.python/current/bin/python3 /workspaces/QBot/src/adapters/topstep_x_adapter.py initialize 2>&1 | /usr/bin/head -20
echo ""

echo "✅ Tests complete!"
