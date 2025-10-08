#!/bin/bash
# Verification script for Cloud Model Sync Integration
# This script verifies that CloudModelSynchronizationService is properly wired to UnifiedTradingBrain

# Don't exit on error - we want to count all pass/fail
set +e

echo "=========================================="
echo "Cloud Model Sync Integration Verification"
echo "=========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Counters
PASSED=0
FAILED=0

# Test function
test_check() {
    local description=$1
    local command=$2
    
    echo -n "Checking: $description... "
    if eval "$command" > /dev/null 2>&1; then
        echo -e "${GREEN}✓ PASS${NC}"
        ((PASSED++))
    else
        echo -e "${RED}✗ FAIL${NC}"
        ((FAILED++))
    fi
}

# 1. Check CloudModelSynchronizationService has UnifiedTradingBrain field
echo "1. CloudModelSynchronizationService Integration"
echo "   =============================================="
test_check "Has UnifiedTradingBrain field" \
    "grep -q 'private readonly UnifiedTradingBrain' src/BotCore/Services/CloudModelSynchronizationService.cs"

test_check "Constructor accepts UnifiedTradingBrain parameter" \
    "grep -q 'UnifiedTradingBrain? tradingBrain' src/BotCore/Services/CloudModelSynchronizationService.cs"

test_check "Assigns tradingBrain in constructor" \
    "grep -q '_tradingBrain = tradingBrain' src/BotCore/Services/CloudModelSynchronizationService.cs"

test_check "Calls ReloadModelsAsync after download" \
    "grep -q 'ReloadModelsAsync' src/BotCore/Services/CloudModelSynchronizationService.cs"

test_check "Has proper error handling for hot-swap" \
    "grep -q 'Model hot-swap failed' src/BotCore/Services/CloudModelSynchronizationService.cs"

echo ""

# 2. Check Program.cs service registration
echo "2. Service Registration in Program.cs"
echo "   ===================================="
test_check "Retrieves UnifiedTradingBrain from DI container" \
    "grep -q 'GetService<BotCore.Brain.UnifiedTradingBrain>' src/UnifiedOrchestrator/Program.cs"

test_check "Passes tradingBrain to CloudModelSynchronizationService" \
    "grep -A 5 'new BotCore.Services.CloudModelSynchronizationService' src/UnifiedOrchestrator/Program.cs | grep -q 'tradingBrain'"

echo ""

# 3. Check UnifiedTradingBrain has ReloadModelsAsync
echo "3. UnifiedTradingBrain Compatibility"
echo "   ==================================="
test_check "Has ReloadModelsAsync method" \
    "grep -q 'public async Task<bool> ReloadModelsAsync' src/BotCore/Brain/UnifiedTradingBrain.cs"

test_check "ReloadModelsAsync accepts model path parameter" \
    "grep -A 2 'public async Task<bool> ReloadModelsAsync' src/BotCore/Brain/UnifiedTradingBrain.cs | grep -q 'string newModelPath'"

test_check "Has model validation logic" \
    "grep -q 'ValidateModelForReloadAsync' src/BotCore/Brain/UnifiedTradingBrain.cs"

test_check "Has backup creation logic" \
    "grep -q 'CreateModelBackup' src/BotCore/Brain/UnifiedTradingBrain.cs"

echo ""

# 4. Check configuration exists
echo "4. Configuration Files"
echo "   ===================="
test_check "CloudSync configuration exists" \
    "grep -q 'CloudSync' src/UnifiedOrchestrator/appsettings.json"

test_check "GitHub configuration exists" \
    "grep -q 'GitHub' src/UnifiedOrchestrator/appsettings.json"

test_check "IntervalMinutes setting exists" \
    "grep -q 'IntervalMinutes' src/UnifiedOrchestrator/appsettings.json"

echo ""

# 5. Check directory structure
echo "5. Directory Structure"
echo "   ===================="
test_check "Models directory exists" \
    "[ -d 'models' ]"

test_check "CloudModelSynchronizationService file exists" \
    "[ -f 'src/BotCore/Services/CloudModelSynchronizationService.cs' ]"

test_check "UnifiedTradingBrain file exists" \
    "[ -f 'src/BotCore/Brain/UnifiedTradingBrain.cs' ]"

test_check "Integration documentation exists" \
    "[ -f 'CLOUD_MODEL_SYNC_INTEGRATION.md' ]"

echo ""

# Summary
echo "=========================================="
echo "Summary"
echo "=========================================="
echo -e "Passed: ${GREEN}${PASSED}${NC}"
echo -e "Failed: ${RED}${FAILED}${NC}"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ All checks passed! Cloud Model Sync is properly integrated.${NC}"
    echo ""
    echo "Next steps:"
    echo "1. Set GITHUB_TOKEN environment variable"
    echo "2. Run: dotnet run --project src/UnifiedOrchestrator"
    echo "3. Monitor logs for: 🌐 [CLOUD-SYNC] and 🔄 [MODEL-RELOAD] messages"
    exit 0
else
    echo -e "${RED}✗ Some checks failed. Please review the integration.${NC}"
    exit 1
fi
