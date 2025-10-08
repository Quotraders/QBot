#!/bin/bash
# Test CLI commands for C# integration

echo "Testing Python adapter CLI commands..."
echo ""

# Set environment variables
export ADAPTER_MAX_RETRIES=3
export ADAPTER_BASE_DELAY=1.0
export ADAPTER_MAX_DELAY=10.0
export ADAPTER_TIMEOUT=30.0

# Test 1: get_fill_events command
echo "Test 1: get_fill_events"
python3 src/adapters/topstep_x_adapter.py '{"action":"get_fill_events"}' 2>/dev/null
echo ""

# Test 2: get_positions command
echo "Test 2: get_positions"
python3 src/adapters/topstep_x_adapter.py '{"action":"get_positions"}' 2>/dev/null
echo ""

# Test 3: get_health_score command
echo "Test 3: get_health_score"
python3 src/adapters/topstep_x_adapter.py '{"action":"get_health_score"}' 2>/dev/null
echo ""

echo "All CLI tests completed"
