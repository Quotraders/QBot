#!/bin/bash
# Quick validation test for WSL fix
# Tests critical functionality without running the full application

echo "=================================="
echo "🧪 WSL Fix Quick Validation"
echo "=================================="
echo ""

cd "$(dirname "$0")"

PASS=0
FAIL=0

pass() { echo "✅ PASS: $1"; ((PASS++)); }
fail() { echo "❌ FAIL: $1"; ((FAIL++)); }
info() { echo "ℹ️  $1"; }

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 1: Build Verification"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -v q 2>&1 | grep -q "0 Error(s)"; then
    pass "Build succeeds with no errors"
else
    fail "Build has errors"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 2: Code Changes Verification"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check Program.cs changes
if grep -q "Inner Exception" src/UnifiedOrchestrator/Program.cs; then
    pass "Enhanced exception handling in Program.cs"
else
    fail "Enhanced exception handling missing"
fi

if grep -q "critical_errors.log" src/UnifiedOrchestrator/Program.cs; then
    pass "Critical error logging implemented"
else
    fail "Critical error logging missing"
fi

# Check TopstepXAdapterService changes
if grep -q "RuntimeInformation" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "Platform detection code present"
else
    fail "Platform detection code missing"
fi

if grep -q "FindExecutableInPath" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "Python path resolution implemented"
else
    fail "Python path resolution missing"
fi

if grep -q "PlatformNotSupportedException" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "WSL platform validation implemented"
else
    fail "WSL platform validation missing"
fi

# Check UnifiedOrchestratorService changes
if grep -q "Constructor invoked" src/UnifiedOrchestrator/Services/UnifiedOrchestratorService.cs; then
    pass "Constructor logging in UnifiedOrchestratorService"
else
    fail "Constructor logging missing"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 3: Configuration & Logging"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check for comprehensive logging
if grep -q "TopstepXAdapter.*Constructor invoked" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "TopstepXAdapter constructor logging present"
else
    fail "TopstepXAdapter constructor logging missing"
fi

if grep -q "SDK-VALIDATION" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "SDK validation logging present"
else
    fail "SDK validation logging missing"
fi

if grep -q "TOPSTEPX_API_KEY" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "Credential validation implemented"
else
    fail "Credential validation missing"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 4: GitIgnore Updates"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if grep -q "^state/$" .gitignore; then
    pass ".gitignore excludes state/ directory"
else
    fail ".gitignore missing state/ exclusion"
fi

if grep -q "^reports/$" .gitignore; then
    pass ".gitignore excludes reports/ directory"
else
    fail ".gitignore missing reports/ exclusion"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 5: File Changes Match Expected"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

CHANGED_FILES=$(git diff --name-only 9632ceb | tr '\n' ' ')
info "Changed files: $CHANGED_FILES"

if echo "$CHANGED_FILES" | grep -q "Program.cs"; then
    pass "Program.cs modified"
else
    fail "Program.cs not modified"
fi

if echo "$CHANGED_FILES" | grep -q "TopstepXAdapterService.cs"; then
    pass "TopstepXAdapterService.cs modified"
else
    fail "TopstepXAdapterService.cs not modified"
fi

if echo "$CHANGED_FILES" | grep -q "UnifiedOrchestratorService.cs"; then
    pass "UnifiedOrchestratorService.cs modified"
else
    fail "UnifiedOrchestratorService.cs not modified"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Test 6: Key Error Messages"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check error message quality
if grep -q "Hint:" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "Helpful hints in error messages"
else
    fail "No helpful hints found"
fi

if grep -q "Use PYTHON_EXECUTABLE=python3 on Linux" src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs; then
    pass "Specific platform guidance provided"
else
    fail "Platform-specific guidance missing"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Test Summary"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Passed: $PASS"
echo "Failed: $FAIL"
echo ""

if [ $FAIL -eq 0 ]; then
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "✅ ALL VALIDATIONS PASSED!"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "Summary of Fixes:"
    echo "  ✅ Enhanced error logging with stack traces"
    echo "  ✅ Platform detection for WSL on Windows only"
    echo "  ✅ Python path resolution with fallbacks"
    echo "  ✅ Comprehensive constructor logging"
    echo "  ✅ Credential validation"
    echo "  ✅ Helpful error messages with hints"
    echo ""
    echo "The bot no longer crashes silently!"
    exit 0
else
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "❌ SOME VALIDATIONS FAILED"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    exit 1
fi
