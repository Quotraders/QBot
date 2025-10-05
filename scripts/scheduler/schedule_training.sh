#!/bin/bash
# Trading Bot Parameter Optimization - Linux/Mac Cron Script
# Runs weekly parameter optimization during market closed window
#
# Crontab entry (Saturday 2:00 AM Eastern Time):
# 0 2 * * 6 /path/to/schedule_training.sh >> /path/to/logs/training_cron.log 2>&1

set -e

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARTIFACTS_PATH="${ARTIFACTS_PATH:-$REPO_ROOT/artifacts}"
TRAINING_PATH="$REPO_ROOT/src/Training"
LOGS_PATH="${LOGS_PATH:-$REPO_ROOT/logs}"

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
LOG_FILE="$LOGS_PATH/training_${TIMESTAMP}.log"

# Ensure log directory exists
mkdir -p "$LOGS_PATH"

# Logging function
log() {
    local level="${2:-INFO}"
    local message="[$(date '+%Y-%m-%d %H:%M:%S')] [$level] $1"
    echo "$message" | tee -a "$LOG_FILE"
}

# Safety checks
safety_checks() {
    log "Performing safety checks..."
    
    # Check if on VPN (basic check for common VPN interfaces)
    if ip link show | grep -q "tun0\|ppp0\|vpn"; then
        log "ERROR: VPN connection detected. Training cannot run on VPN." "ERROR"
        return 1
    fi
    
    # Check if in remote desktop/SSH session with X forwarding
    if [ -n "$SSH_CLIENT" ] && [ -n "$DISPLAY" ]; then
        log "WARNING: SSH session with X forwarding detected." "WARN"
    fi
    
    # Check if DRY_RUN mode bypass is active
    if [ "$DRY_RUN" = "0" ] || [ "$DRY_RUN" = "false" ]; then
        log "ERROR: DRY_RUN mode is disabled. Training should not run with live trading." "ERROR"
        return 1
    fi
    
    # Check for kill.txt file
    if [ -f "$REPO_ROOT/kill.txt" ]; then
        log "WARNING: kill.txt file exists. System may be in maintenance mode." "WARN"
    fi
    
    # Check Python availability
    if ! command -v python3 &> /dev/null; then
        log "ERROR: Python 3 not found in PATH" "ERROR"
        return 1
    fi
    
    log "Safety checks passed."
    return 0
}

# Main execution
main() {
    log "========================================" "INFO"
    log "Trading Bot Parameter Optimization" "INFO"
    log "========================================" "INFO"
    log "Started at: $(date)" "INFO"
    log "Log file: $LOG_FILE" "INFO"
    
    # Safety checks
    if ! safety_checks; then
        log "Safety checks failed. Aborting training." "ERROR"
        exit 1
    fi
    
    # Set environment variables
    export TRAINING_MODE="OPTIMIZATION"
    export ARTIFACTS_PATH="$ARTIFACTS_PATH"
    
    log "Environment variables set:" "INFO"
    log "  TRAINING_MODE=$TRAINING_MODE" "INFO"
    log "  ARTIFACTS_PATH=$ARTIFACTS_PATH" "INFO"
    log "  TOPSTEP_API_KEY=$([ -n "$TOPSTEP_API_KEY" ] && echo '***SET***' || echo 'NOT SET')" "INFO"
    
    # Verify API credentials
    if [ -z "$TOPSTEP_API_KEY" ]; then
        log "ERROR: TOPSTEP_API_KEY environment variable not set" "ERROR"
        exit 1
    fi
    
    # Change to training directory
    cd "$TRAINING_PATH"
    log "Changed to training directory: $TRAINING_PATH" "INFO"
    
    # Run training orchestrator
    log "Starting parameter optimization..." "INFO"
    log "Running: python3 training_orchestrator.py" "INFO"
    
    if python3 training_orchestrator.py 2>&1 | tee -a "$LOG_FILE"; then
        EXIT_CODE=0
    else
        EXIT_CODE=$?
    fi
    
    if [ $EXIT_CODE -eq 0 ]; then
        log "Training completed successfully!" "INFO"
        
        # Promote parameters from stage to current (atomic promotion)
        log "Promoting optimized parameters to production..." "INFO"
        
        STAGE_DIR="$ARTIFACTS_PATH/stage/parameters"
        CURRENT_DIR="$ARTIFACTS_PATH/current/parameters"
        PREVIOUS_DIR="$ARTIFACTS_PATH/previous/parameters"
        
        # Backup current parameters to previous
        if [ -d "$CURRENT_DIR" ]; then
            log "Backing up current parameters to previous..." "INFO"
            rm -rf "$PREVIOUS_DIR"
            cp -r "$CURRENT_DIR" "$PREVIOUS_DIR"
            log "Current parameters backed up." "INFO"
        fi
        
        # Copy stage parameters to current
        if [ -d "$STAGE_DIR" ]; then
            mkdir -p "$CURRENT_DIR"
            for file in "$STAGE_DIR"/*_parameters.json; do
                if [ -f "$file" ]; then
                    cp "$file" "$CURRENT_DIR/"
                    log "Promoted: $(basename "$file")" "INFO"
                fi
            done
            log "All parameters promoted to production." "INFO"
        else
            log "WARNING: No stage directory found. Parameters not promoted." "WARN"
        fi
        
        # Generate dashboard summary
        REPORT_DIR="$ARTIFACTS_PATH/reports"
        mkdir -p "$REPORT_DIR"
        REPORT_FILE="$REPORT_DIR/training_summary_${TIMESTAMP}.md"
        
        cat > "$REPORT_FILE" << EOF
# Training Summary - $TIMESTAMP

## Status: ✓ SUCCESS

**Started:** $(date '+%Y-%m-%d %H:%M:%S')
**Log File:** $LOG_FILE

## Optimized Strategies

$(find "$STAGE_DIR" -name "*_parameters.json" 2>/dev/null | xargs -n1 basename | sed 's/_parameters.json//' | sed 's/^/- /')

## Actions Taken

1. Downloaded historical data (90 days)
2. Optimized parameters by session (Overnight/RTH/PostRTH)
3. Validated improvements (>10% Sharpe required)
4. Backed up current parameters to previous
5. Promoted optimized parameters to production

## Next Steps

1. Monitor live performance for 3 trading days
2. Automatic rollback if Sharpe drops >20%
3. Review performance reports in artifacts/reports/

---
*Automated training run - Next run: Next Saturday 2:00 AM ET*
EOF
        log "Dashboard summary generated: $REPORT_FILE" "INFO"
        
    else
        log "Training failed with exit code: $EXIT_CODE" "ERROR"
        log "Parameters NOT promoted. Current parameters remain unchanged." "WARN"
        log "ALERT: Parameter optimization failed. Manual review required." "ERROR"
        exit 1
    fi
    
    log "Completed at: $(date)" "INFO"
    log "========================================" "INFO"
}

# Run main function
main

exit 0
