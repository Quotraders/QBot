#!/bin/bash
# verify-dotnet-installation-config.sh
# Verifies that all GitHub Actions workflows use the correct .NET SDK installation pattern
# to avoid permission errors on self-hosted runners

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=================================================="
echo "🔍 .NET SDK Installation Configuration Verification"
echo "=================================================="
echo ""

EXIT_CODE=0

# Check for DOTNET_INSTALL_DIR environment variable - it SHOULD be set to runner.temp
echo "📋 Checking for DOTNET_INSTALL_DIR environment variable..."
if grep -r "DOTNET_INSTALL_DIR" .github/workflows/*.yml 2>/dev/null; then
    echo "✅ FOUND: DOTNET_INSTALL_DIR in workflow files"
    echo "   Verifying it uses runner.temp (user-writable directory)..."
    
    if grep -r "DOTNET_INSTALL_DIR.*runner\.temp" .github/workflows/*.yml 2>/dev/null | grep -v "^#" > /dev/null; then
        echo "✅ PASS: DOTNET_INSTALL_DIR uses runner.temp (correct pattern from PR #559)"
    else
        echo "❌ FAIL: DOTNET_INSTALL_DIR does not use runner.temp"
        echo "   Must use: DOTNET_INSTALL_DIR: \${{ runner.temp }}/.dotnet"
        EXIT_CODE=1
    fi
else
    echo "⚠️ WARNING: No DOTNET_INSTALL_DIR found"
    echo "   PR #559 configuration uses DOTNET_INSTALL_DIR: \${{ runner.temp }}/.dotnet"
    echo "   This forces installation to user-writable temp directory."
fi
echo ""

# Check for install-dir parameter (should NOT be used)
echo "📋 Checking for install-dir parameter in setup-dotnet..."
if grep -A3 "uses: actions/setup-dotnet" .github/workflows/*.yml 2>/dev/null | grep "install-dir:" ; then
    echo "❌ FOUND: install-dir parameter in setup-dotnet (this is incorrect!)"
    echo "   Use DOTNET_INSTALL_DIR env var instead, not install-dir parameter."
    EXIT_CODE=1
else
    echo "✅ PASS: No install-dir parameter found"
fi
echo ""

# Check for manual install-dotnet.ps1 calls
echo "📋 Checking for manual install-dotnet.ps1 calls..."
if grep -r "install-dotnet.ps1\|Install-Dotnet.ps1" .github/workflows/*.yml 2>/dev/null; then
    echo "⚠️ FOUND: Manual install-dotnet.ps1 calls detected"
    echo "   Checking if they use user-writable directories..."
    
    if grep -A5 -B5 "install-dotnet.ps1" .github/workflows/*.yml 2>/dev/null | grep -E "InstallDir.*USERPROFILE|InstallDir.*HOME|InstallDir.*runner\.temp"; then
        echo "✅ Manual calls use user-writable directories (acceptable)"
    else
        echo "❌ Manual calls DO NOT use user-writable directories (this is incorrect!)"
        echo "   Manual install-dotnet.ps1 must use -InstallDir with user-writable path"
        EXIT_CODE=1
    fi
else
    echo "✅ PASS: No manual install-dotnet.ps1 calls found"
fi
echo ""

# Verify correct pattern usage in self-hosted workflows (PR #559 pattern)
echo "📋 Verifying PR #559 setup-dotnet pattern in self-hosted workflows..."
SELFHOSTED_WORKFLOWS=(
    ".github/workflows/bot-launch-diagnostics.yml"
    ".github/workflows/selfhosted-bot-run.yml"
    ".github/workflows/selfhosted-test.yml"
)

for workflow in "${SELFHOSTED_WORKFLOWS[@]}"; do
    if [[ -f "$workflow" ]]; then
        echo "  Checking $workflow..."
        
        # Check for the PR #559 pattern: DOTNET_INSTALL_DIR with runner.temp
        if grep -A6 "uses: actions/setup-dotnet@v4" "$workflow" | grep -q "DOTNET_INSTALL_DIR.*runner\.temp"; then
            echo "    ✅ CORRECT: Uses PR #559 pattern (DOTNET_INSTALL_DIR with runner.temp)"
        elif grep -A3 "uses: actions/setup-dotnet@v4" "$workflow" | grep -q "dotnet-version:"; then
            echo "    ⚠️ WARNING: Missing DOTNET_INSTALL_DIR environment variable"
            echo "       PR #559 working configuration uses: DOTNET_INSTALL_DIR: \${{ runner.temp }}/.dotnet"
            EXIT_CODE=1
        else
            echo "    ❌ INCORRECT: setup-dotnet configuration not found or malformed"
            EXIT_CODE=1
        fi
    else
        echo "  ⚠️ File not found: $workflow"
    fi
done
echo ""

# Summary
echo "=================================================="
if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ ALL CHECKS PASSED"
    echo ""
    echo "All workflows use the PR #559 working .NET SDK installation pattern:"
    echo "  - DOTNET_INSTALL_DIR set to \${{ runner.temp }}/.dotnet"
    echo "  - No install-dir parameters"
    echo "  - No manual install-dotnet.ps1 calls to system directories"
    echo ""
    echo "This configuration forces .NET SDK installation to the runner's temp"
    echo "directory, bypassing permission errors on non-elevated self-hosted runners."
else
    echo "❌ VERIFICATION FAILED"
    echo ""
    echo "Some workflows do not match the PR #559 working configuration."
    echo "Please fix the issues listed above."
    echo ""
    echo "CORRECT PATTERN (from PR #559):"
    echo "  - name: \"🔧 Setup .NET SDK\""
    echo "    uses: actions/setup-dotnet@v4"
    echo "    env:"
    echo "      DOTNET_INSTALL_DIR: \${{ runner.temp }}/.dotnet"
    echo "    with:"
    echo "      dotnet-version: '8.0.x'"
    echo ""
    echo "DO NOT USE:"
    echo "  - install-dir parameter (use DOTNET_INSTALL_DIR env var instead)"
    echo "  - Manual install-dotnet.ps1 calls to C:\\Program Files\\dotnet"
    echo "  - setup-dotnet without DOTNET_INSTALL_DIR on self-hosted runners"
fi
echo "=================================================="

exit $EXIT_CODE
