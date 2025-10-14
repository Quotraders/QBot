#!/bin/bash
# Bot Runner Script - Launch trading bot with comprehensive logging
# Usage: ./run-bot.sh [--with-logs] [--check-sdk]

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Helper functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_header() {
    echo -e "${CYAN}================================================================${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}================================================================${NC}"
}

# Check SDK installation
check_sdk() {
    log_header "Checking TopstepX SDK Installation"
    
    # Check Python
    if command -v python3 &> /dev/null; then
        log_success "Python 3 found: $(python3 --version)"
    else
        log_error "Python 3 not found - required for SDK bridge"
        return 1
    fi
    
    # Check project-x-py SDK
    if python3 -c "import project_x_py" 2>/dev/null; then
        log_success "TopstepX SDK (project-x-py) is installed"
        python3 -c "import project_x_py; print('  Version:', project_x_py.__version__ if hasattr(project_x_py, '__version__') else 'Unknown')" 2>/dev/null || true
    else
        log_warning "TopstepX SDK (project-x-py) is NOT installed"
        log_warning "  Bot will run in DEGRADED MODE (no live data)"
        log_info ""
        log_info "To install TopstepX SDK:"
        log_info "  pip install 'project-x-py[all]'"
        log_info ""
        log_info "After installation, restart the bot for full functionality"
        return 1
    fi
    
    # Check SDK bridge script
    if [ -f "python/sdk_bridge.py" ]; then
        log_success "SDK bridge script found: python/sdk_bridge.py"
    else
        log_error "SDK bridge script missing: python/sdk_bridge.py"
        return 1
    fi
    
    # Check environment variables
    log_info ""
    log_info "Checking environment variables..."
    if [ -n "$TOPSTEPX_API_KEY" ]; then
        log_success "  TOPSTEPX_API_KEY: [SET]"
    else
        log_warning "  TOPSTEPX_API_KEY: [NOT SET]"
    fi
    
    if [ -n "$TOPSTEPX_USERNAME" ]; then
        log_success "  TOPSTEPX_USERNAME: $TOPSTEPX_USERNAME"
    else
        log_warning "  TOPSTEPX_USERNAME: [NOT SET]"
    fi
    
    echo ""
    return 0
}

# Show startup info
show_startup_info() {
    log_header "Trading Bot Startup Information"
    log_info "This script launches the UnifiedOrchestrator trading bot"
    log_info ""
    log_info "What to expect in the logs:"
    log_info "  ✅ TopstepX SDK Validation - Checks if project-x-py is installed"
    log_info "  ✅ WebSocket Connections - Establishes live data connections"
    log_info "  ✅ Historical Data Bridge - Seeds system with recent market data"
    log_info "  ✅ Component Initialization - Loads all trading components"
    log_info ""
    log_info "Common issues and solutions:"
    log_info "  ⚠️  'ModuleNotFoundError: project_x_py'"
    log_info "     → Install SDK: pip install 'project-x-py[all]'"
    log_info ""
    log_info "  ⚠️  'SDK bridge script not found'"
    log_info "     → Ensure python/sdk_bridge.py exists in project root"
    log_info ""
    log_info "  ⚠️  'NO real historical data available'"
    log_info "     → Install SDK and set TOPSTEPX_API_KEY, TOPSTEPX_USERNAME"
    log_info ""
}

# Parse arguments
WITH_LOGS=false
CHECK_SDK=false

for arg in "$@"; do
    case $arg in
        --with-logs)
            WITH_LOGS=true
            shift
            ;;
        --check-sdk)
            CHECK_SDK=true
            shift
            ;;
        --help)
            echo "Usage: ./run-bot.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --with-logs    Save logs to file (logs/bot-run-TIMESTAMP.log)"
            echo "  --check-sdk    Check SDK installation before running"
            echo "  --help         Show this help message"
            echo ""
            exit 0
            ;;
    esac
done

# Show startup info
show_startup_info

# Check SDK if requested
if [ "$CHECK_SDK" = true ]; then
    check_sdk || log_warning "SDK check found issues - bot will run in degraded mode"
    echo ""
    read -p "Press Enter to continue or Ctrl+C to abort..."
fi

# Build the bot
log_header "Building Trading Bot"
log_info "Building UnifiedOrchestrator..."
if dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --configuration Release --verbosity quiet; then
    log_success "Build completed successfully"
else
    log_error "Build failed - check compilation errors above"
    exit 1
fi

# Run the bot
log_header "Starting Trading Bot"
log_info "Launching UnifiedOrchestrator..."
log_info "Press Ctrl+C to stop the bot"
echo ""

if [ "$WITH_LOGS" = true ]; then
    # Create logs directory
    mkdir -p logs
    LOG_FILE="logs/bot-run-$(date +%Y%m%d-%H%M%S).log"
    log_info "Logs will be saved to: $LOG_FILE"
    log_info ""
    
    # Run with logging
    dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --no-build --configuration Release 2>&1 | tee "$LOG_FILE"
else
    # Run without file logging
    dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --no-build --configuration Release
fi
