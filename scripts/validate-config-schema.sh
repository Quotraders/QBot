#!/bin/bash
# Configuration Schema Validation Test
# Validates config files against expected schemas to catch misconfigurations

set -e
echo "🔍 Configuration Schema Validation"

CONFIG_DIR="config"
VALIDATION_ERRORS=0

# Function to validate JSON syntax
validate_json() {
    local file="$1"
    
    # Skip files that are not actually JSON (like holiday dates)
    if [[ "$file" == *"holiday"* ]]; then
        echo "✅ Holiday file (text format): $file"
        return 0
    fi
    
    if ! python3 -m json.tool "$file" > /dev/null 2>&1; then
        echo "❌ Invalid JSON syntax: $file"
        ((VALIDATION_ERRORS++))
    else
        echo "✅ Valid JSON: $file"
    fi
}

# Function to validate required fields in enhanced-trading-bot.json
validate_enhanced_config() {
    local file="$1"
    
    # Check for required top-level sections
    if ! grep -q "CounterfactualReplay" "$file"; then
        echo "❌ Missing CounterfactualReplay section in $file"
        ((VALIDATION_ERRORS++))
    fi
    
    if ! grep -q "Explainability" "$file"; then
        echo "❌ Missing Explainability section in $file"
        ((VALIDATION_ERRORS++))
    fi
    
    if ! grep -q "EnhancedAlerting" "$file"; then
        echo "❌ Missing EnhancedAlerting section in $file"
        ((VALIDATION_ERRORS++))
    fi
    
    # Check that legacy BookAwareSimulator is removed
    if grep -q "BookAwareSimulator" "$file"; then
        echo "❌ Legacy BookAwareSimulator section found in $file - should be removed per audit requirements"
        ((VALIDATION_ERRORS++))
    else
        echo "✅ BookAwareSimulator legacy config properly removed from $file"
    fi
}

# Function to validate strategy config files
validate_strategy_config() {
    local file="$1"
    
    # Basic structure checks for strategy files
    if [[ "$file" == *.json ]]; then
        validate_json "$file"
        
        # Strategy-specific validations could go here
        if ! grep -q -E "(parameters|config|strategy)" "$file"; then
            echo "⚠️  Warning: Strategy file may be missing expected structure: $file"
        fi
    fi
}

echo "Validating JSON configuration files..."

# Validate all JSON files in config directory
find "$CONFIG_DIR" -name "*.json" -type f | while read -r config_file; do
    echo "Checking: $config_file"
    validate_json "$config_file"
    
    # Special validation for enhanced-trading-bot.json
    if [[ "$config_file" == *"enhanced-trading-bot.json" ]]; then
        validate_enhanced_config "$config_file"
    fi
    
    # Special validation for strategy files
    if [[ "$config_file" == *"/strategies/"* || "$config_file" == *"strategy"* ]]; then
        validate_strategy_config "$config_file"
    fi
done

# Validate required runtime directories exist
echo ""
echo "Validating required runtime directories..."

REQUIRED_DIRS=("state/gates" "state/explain" "data/training/execution")
for dir in "${REQUIRED_DIRS[@]}"; do
    if [[ -d "$dir" ]]; then
        echo "✅ Required directory exists: $dir"
        if [[ -f "$dir/.gitkeep" ]]; then
            echo "✅ .gitkeep placeholder found in $dir"
        else
            echo "⚠️  Warning: Missing .gitkeep in $dir (may cause issues on first run)"
        fi
    else
        echo "❌ Missing required directory: $dir"
        ((VALIDATION_ERRORS++))
    fi
done

# Final result
echo ""
if [[ $VALIDATION_ERRORS -eq 0 ]]; then
    echo "✅ All configuration validations passed!"
    exit 0
else
    echo "❌ Found $VALIDATION_ERRORS configuration validation errors"
    exit 1
fi