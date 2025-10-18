#!/bin/bash
# Diagnostic script to check TopstepX adapter environment configuration
# Run this to troubleshoot credential issues

set -e

echo "=================================================="
echo "TopstepX Adapter Environment Diagnostics"
echo "=================================================="
echo ""

# Check if .env file exists
echo "1. Checking for .env file..."
if [ -f ".env" ]; then
    echo "   ✅ .env file found"
    echo "   Location: $(pwd)/.env"
    echo ""
    
    # Check for required variables in .env file
    echo "2. Checking .env file contents..."
    if grep -q "TOPSTEPX_API_KEY" .env 2>/dev/null; then
        echo "   ✅ TOPSTEPX_API_KEY found in .env"
    else
        echo "   ❌ TOPSTEPX_API_KEY NOT found in .env"
    fi
    
    if grep -q "TOPSTEPX_USERNAME" .env 2>/dev/null; then
        echo "   ✅ TOPSTEPX_USERNAME found in .env"
    else
        echo "   ❌ TOPSTEPX_USERNAME NOT found in .env"
    fi
    
    if grep -q "TOPSTEPX_ACCOUNT_ID" .env 2>/dev/null; then
        echo "   ✅ TOPSTEPX_ACCOUNT_ID found in .env"
    else
        echo "   ❌ TOPSTEPX_ACCOUNT_ID NOT found in .env"
    fi
else
    echo "   ❌ .env file NOT found in current directory"
    echo "   Expected location: $(pwd)/.env"
fi

echo ""
echo "3. Checking current environment variables..."

if [ -n "$TOPSTEPX_API_KEY" ]; then
    if [ "$TOPSTEPX_API_KEY" = "" ]; then
        echo "   ⚠️  TOPSTEPX_API_KEY is set but EMPTY"
    else
        echo "   ✅ TOPSTEPX_API_KEY is set (value: ${TOPSTEPX_API_KEY:0:10}...)"
    fi
else
    echo "   ℹ️  TOPSTEPX_API_KEY is not set in environment"
fi

if [ -n "$TOPSTEPX_USERNAME" ]; then
    if [ "$TOPSTEPX_USERNAME" = "" ]; then
        echo "   ⚠️  TOPSTEPX_USERNAME is set but EMPTY"
    else
        echo "   ✅ TOPSTEPX_USERNAME is set (value: $TOPSTEPX_USERNAME)"
    fi
else
    echo "   ℹ️  TOPSTEPX_USERNAME is not set in environment"
fi

if [ -n "$TOPSTEPX_ACCOUNT_ID" ]; then
    if [ "$TOPSTEPX_ACCOUNT_ID" = "" ]; then
        echo "   ⚠️  TOPSTEPX_ACCOUNT_ID is set but EMPTY"
    else
        echo "   ✅ TOPSTEPX_ACCOUNT_ID is set (value: $TOPSTEPX_ACCOUNT_ID)"
    fi
else
    echo "   ℹ️  TOPSTEPX_ACCOUNT_ID is not set in environment"
fi

echo ""
echo "4. Checking Python SDK availability..."
if command -v python3 &> /dev/null; then
    echo "   ✅ python3 is available: $(which python3)"
    
    # Check if project-x-py is installed
    if python3 -c "import project_x_py" 2>/dev/null; then
        echo "   ✅ project-x-py SDK is installed"
        python3 -c "import project_x_py; print(f'   Version: {project_x_py.__version__ if hasattr(project_x_py, \"__version__\") else \"unknown\"}')" 2>/dev/null || echo "   Version: unknown"
    else
        echo "   ❌ project-x-py SDK is NOT installed"
        echo "   Install with: pip install 'project-x-py[all]>=3.5.0'"
    fi
else
    echo "   ❌ python3 is not available in PATH"
fi

echo ""
echo "5. Recommendations..."
echo ""

# Provide recommendations based on findings
has_dotenv_file=$([ -f ".env" ] && echo "yes" || echo "no")
has_api_key_in_file=$(grep -q "TOPSTEPX_API_KEY" .env 2>/dev/null && echo "yes" || echo "no")
has_username_in_file=$(grep -q "TOPSTEPX_USERNAME" .env 2>/dev/null && echo "yes" || echo "no")
has_empty_env_vars=$([ -n "$TOPSTEPX_API_KEY" ] && [ "$TOPSTEPX_API_KEY" = "" ] && echo "yes" || echo "no")

if [ "$has_dotenv_file" = "no" ]; then
    echo "   ⚠️  Create .env file with credentials:"
    echo "   "
    echo "      TOPSTEPX_API_KEY=your_api_key"
    echo "      TOPSTEPX_USERNAME=your_username"
    echo "      TOPSTEPX_ACCOUNT_ID=your_account_id"
    echo ""
elif [ "$has_api_key_in_file" = "no" ] || [ "$has_username_in_file" = "no" ]; then
    echo "   ⚠️  Add missing credentials to .env file"
    echo ""
fi

if [ "$has_empty_env_vars" = "yes" ]; then
    echo "   ⚠️  WARNING: Empty environment variables detected!"
    echo "      These will override .env file values."
    echo "      Solution: Unset them before running bot:"
    echo "      "
    echo "      unset TOPSTEPX_API_KEY TOPSTEPX_USERNAME TOPSTEPX_ACCOUNT_ID"
    echo "      "
    echo "      Or fix the workflow/script that sets them."
    echo ""
fi

echo "=================================================="
echo "Diagnostic complete!"
echo "=================================================="
