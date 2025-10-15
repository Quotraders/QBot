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

# Check for DOTNET_INSTALL_DIR environment variable
echo "📋 Checking for DOTNET_INSTALL_DIR environment variable..."
if grep -r "DOTNET_INSTALL_DIR" .github/workflows/*.yml 2>/dev/null; then
    echo "❌ FOUND: DOTNET_INSTALL_DIR in workflow files (this is incorrect!)"
    echo "   The setup-dotnet action should NOT use custom install directories."
    EXIT_CODE=1
else
    echo "✅ PASS: No DOTNET_INSTALL_DIR found in workflow files"
fi
echo ""

# Check for install-dir parameter
echo "📋 Checking for install-dir parameter in setup-dotnet..."
if grep -A3 "uses: actions/setup-dotnet" .github/workflows/*.yml 2>/dev/null | grep "install-dir:" ; then
    echo "❌ FOUND: install-dir parameter in setup-dotnet (this is incorrect!)"
    echo "   The setup-dotnet action should only specify dotnet-version."
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
    
    if grep -A5 -B5 "install-dotnet.ps1" .github/workflows/*.yml 2>/dev/null | grep -E "InstallDir.*USERPROFILE|InstallDir.*HOME"; then
        echo "✅ Manual calls use user-writable directories (acceptable)"
    else
        echo "❌ Manual calls DO NOT use user-writable directories (this is incorrect!)"
        echo "   Manual install-dotnet.ps1 must use -InstallDir \"\$env:USERPROFILE\\.dotnet\""
        EXIT_CODE=1
    fi
else
    echo "✅ PASS: No manual install-dotnet.ps1 calls found"
fi
echo ""

# Verify correct pattern usage in self-hosted workflows
echo "📋 Verifying correct setup-dotnet pattern in self-hosted workflows..."
SELFHOSTED_WORKFLOWS=(
    ".github/workflows/bot-launch-diagnostics.yml"
    ".github/workflows/selfhosted-bot-run.yml"
    ".github/workflows/selfhosted-test.yml"
)

for workflow in "${SELFHOSTED_WORKFLOWS[@]}"; do
    if [[ -f "$workflow" ]]; then
        echo "  Checking $workflow..."
        
        # Extract setup-dotnet configuration
        if grep -A5 "uses: actions/setup-dotnet@v4" "$workflow" | grep -q "dotnet-version:"; then
            # Check that ONLY dotnet-version is specified (no env block with DOTNET_INSTALL_DIR)
            if grep -B2 "uses: actions/setup-dotnet@v4" "$workflow" | grep -q "DOTNET_INSTALL_DIR"; then
                echo "    ❌ INCORRECT: Has DOTNET_INSTALL_DIR"
                EXIT_CODE=1
            elif grep -A5 "uses: actions/setup-dotnet@v4" "$workflow" | grep -q "install-dir:"; then
                echo "    ❌ INCORRECT: Has install-dir parameter"
                EXIT_CODE=1
            else
                echo "    ✅ CORRECT: Uses simple setup-dotnet with dotnet-version only"
            fi
        else
            echo "    ⚠️ WARNING: setup-dotnet found but no dotnet-version specified"
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
    echo "All workflows use the correct .NET SDK installation pattern:"
    echo "  - No DOTNET_INSTALL_DIR environment variables"
    echo "  - No install-dir parameters"
    echo "  - No manual install-dotnet.ps1 calls (or they use user directories)"
    echo "  - Self-hosted workflows use simple setup-dotnet@v4 with dotnet-version only"
    echo ""
    echo "This configuration allows the setup-dotnet action to install .NET SDK"
    echo "to its default user-writable cache directory, avoiding permission errors"
    echo "on non-elevated self-hosted runners."
else
    echo "❌ VERIFICATION FAILED"
    echo ""
    echo "Some workflows have incorrect .NET SDK installation configuration."
    echo "Please fix the issues listed above."
    echo ""
    echo "CORRECT PATTERN:"
    echo "  - name: \"🔧 Setup .NET SDK\""
    echo "    uses: actions/setup-dotnet@v4"
    echo "    with:"
    echo "      dotnet-version: '8.0.x'"
    echo ""
    echo "DO NOT USE:"
    echo "  - DOTNET_INSTALL_DIR environment variable"
    echo "  - install-dir parameter"
    echo "  - Manual install-dotnet.ps1 calls to C:\\Program Files\\dotnet"
fi
echo "=================================================="

exit $EXIT_CODE
