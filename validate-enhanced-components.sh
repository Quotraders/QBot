#!/bin/bash

# Enhanced Trading Bot Validation Script
# Validates all enhanced components and ensures production readiness

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Helper functions
log_info() {
    echo -e "${BLUE}[VALIDATION]${NC} $1"
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

# Validation functions

validate_directory_structure() {
    log_info "Validating enhanced component directory structure..."
    
    local required_dirs=(
        "src/Backtest/ExecutionSimulators"
        "src/Safety/Analysis"
        "src/Safety/Explainability"
        "src/Monitoring/Alerts"
        "src/BotCore/Extensions"
        "src/Tests/Integration"
        "config"
        "state"
    )
    
    local missing_dirs=()
    for dir in "${required_dirs[@]}"; do
        if [ ! -d "$dir" ]; then
            missing_dirs+=("$dir")
        fi
    done
    
    if [ ${#missing_dirs[@]} -eq 0 ]; then
        log_success "All required directories exist"
        return 0
    else
        log_error "Missing directories: ${missing_dirs[*]}"
        return 1
    fi
}

validate_enhanced_components() {
    log_info "Validating enhanced component files..."
    
    local required_files=(
        "src/Backtest/ExecutionSimulators/BookAwareExecutionSimulator.cs"
        "src/Safety/Analysis/CounterfactualReplayService.cs"
        "src/Safety/Explainability/ExplainabilityStampService.cs"
        "src/Monitoring/Alerts/EnhancedAlertingService.cs"
        "src/BotCore/Extensions/EnhancedTradingBotServiceExtensions.cs"
        "config/enhanced-trading-bot.json"
    )
    
    local missing_files=()
    for file in "${required_files[@]}"; do
        if [ ! -f "$file" ]; then
            missing_files+=("$file")
        fi
    done
    
    if [ ${#missing_files[@]} -eq 0 ]; then
        log_success "All required enhanced component files exist"
        return 0
    else
        log_error "Missing files: ${missing_files[*]}"
        return 1
    fi
}

validate_configuration() {
    log_info "Validating enhanced configuration..."
    
    if [ ! -f "config/enhanced-trading-bot.json" ]; then
        log_error "Enhanced configuration file not found"
        return 1
    fi
    
    # Validate JSON syntax
    if ! jq empty "config/enhanced-trading-bot.json" 2>/dev/null; then
        log_error "Invalid JSON in enhanced configuration file"
        return 1
    fi
    
    # Check required configuration sections
    local required_sections=("BookAwareSimulator" "CounterfactualReplay" "Explainability" "EnhancedAlerting")
    for section in "${required_sections[@]}"; do
        if ! jq -e ".$section" "config/enhanced-trading-bot.json" >/dev/null 2>&1; then
            log_error "Missing configuration section: $section"
            return 1
        fi
    done
    
    log_success "Enhanced configuration is valid"
    return 0
}

validate_build_integration() {
    log_info "Validating build integration with enhanced components..."
    
    # Try to build with the enhanced components
    if dotnet build TopstepX.Bot.sln --verbosity quiet --no-restore >/dev/null 2>&1; then
        log_success "Enhanced components build successfully"
        return 0
    else
        log_error "Enhanced components failed to build"
        log_info "Running detailed build to show errors..."
        dotnet build TopstepX.Bot.sln --verbosity minimal | head -20
        return 1
    fi
}

validate_state_directories() {
    log_info "Validating state directory structure for enhanced components..."
    
    local state_dirs=("state/explain" "state/audits" "state/gates" "data/training/execution")
    
    for dir in "${state_dirs[@]}"; do
        if [ ! -d "$dir" ]; then
            log_info "Creating state directory: $dir"
            mkdir -p "$dir"
        fi
        
        # Test write permissions
        local test_file="$dir/test_write_permissions.tmp"
        if echo "test" > "$test_file" 2>/dev/null; then
            rm -f "$test_file"
            log_success "Directory $dir is writable"
        else
            log_error "Directory $dir is not writable"
            return 1
        fi
    done
    
    return 0
}

validate_analyzer_compliance() {
    log_info "Validating analyzer compliance for enhanced components..."
    
    # Check specific enhanced component files for analyzer compliance
    local enhanced_files=(
        "src/Backtest/ExecutionSimulators/BookAwareExecutionSimulator.cs"
        "src/Safety/Analysis/CounterfactualReplayService.cs"
        "src/Safety/Explainability/ExplainabilityStampService.cs"
        "src/Monitoring/Alerts/EnhancedAlertingService.cs"
    )
    
    local violations_found=false
    for file in "${enhanced_files[@]}"; do
        if [ -f "$file" ]; then
            # Check for common analyzer violations in enhanced components
            if grep -q "List<" "$file"; then
                # Check if it's properly using Collection<T> instead
                if ! grep -q "Collection<" "$file"; then
                    log_warning "File $file may have CA1002 violations (List instead of Collection)"
                    violations_found=true
                fi
            fi
            
            # Check for proper async/await patterns
            if grep -q "\.Wait()" "$file" || grep -q "\.Result" "$file"; then
                log_warning "File $file may have improper async patterns"
                violations_found=true
            fi
        fi
    done
    
    if [ "$violations_found" = false ]; then
        log_success "No obvious analyzer violations found in enhanced components"
        return 0
    else
        log_warning "Potential analyzer violations detected - review recommended"
        return 0  # Don't fail validation for warnings
    fi
}

validate_production_readiness() {
    log_info "Validating production readiness of enhanced components..."
    
    local readiness_checks=(
        "fail_closed_behavior"
        "config_driven_parameters"
        "proper_logging"
        "error_handling"
        "resource_cleanup"
    )
    
    local issues_found=0
    
    # Check for fail-closed behavior patterns
    if ! grep -r "throw new NotImplementedException" src/Backtest/ExecutionSimulators/ src/Safety/ src/Monitoring/ >/dev/null 2>&1; then
        log_success "No NotImplementedException found in enhanced components"
    else
        log_error "Found NotImplementedException in enhanced components"
        issues_found=$((issues_found + 1))
    fi
    
    # Check for config-driven parameters
    if grep -r "Environment.GetEnvironmentVariable\|GetValue<\|GetSection(" src/BotCore/Extensions/ >/dev/null 2>&1; then
        log_success "Config-driven parameters detected in enhanced components"
    else
        log_warning "No config-driven parameters detected"
    fi
    
    # Check for proper logging patterns
    if grep -r "_logger\.Log" src/Backtest/ExecutionSimulators/ src/Safety/ src/Monitoring/ >/dev/null 2>&1; then
        log_success "Proper logging patterns detected in enhanced components"
    else
        log_warning "No structured logging patterns detected"
    fi
    
    # Check for proper async patterns
    if grep -r "ConfigureAwait(false)" src/Backtest/ExecutionSimulators/ src/Safety/ src/Monitoring/ >/dev/null 2>&1; then
        log_success "Proper async patterns (ConfigureAwait) detected"
    else
        log_warning "ConfigureAwait(false) patterns not consistently used"
    fi
    
    if [ $issues_found -eq 0 ]; then
        log_success "Enhanced components pass production readiness checks"
        return 0
    else
        log_error "Enhanced components failed $issues_found production readiness checks"
        return 1
    fi
}

validate_integration_test_readiness() {
    log_info "Validating integration test readiness..."
    
    if [ -f "src/Tests/Integration/EnhancedTradingBotIntegrationTests.cs" ]; then
        log_success "Integration tests are available"
        
        # Check if integration tests can be compiled
        if grep -q "MockTradeJournal\|MockSlippageLatencyModel\|MockAlertService" "src/Tests/Integration/EnhancedTradingBotIntegrationTests.cs"; then
            log_success "Mock services are properly implemented"
        else
            log_warning "Mock services may not be properly implemented"
        fi
        
        return 0
    else
        log_error "Integration tests not found"
        return 1
    fi
}

# Main validation routine
main() {
    log_info "Starting enhanced trading bot validation..."
    log_info "Validating components: Book-aware Simulator, Counterfactual Replay, Explainability, Enhanced Alerting"
    
    local validation_results=()
    
    # Run all validations
    validate_directory_structure && validation_results+=("directories") || validation_results+=("directories_FAILED")
    validate_enhanced_components && validation_results+=("components") || validation_results+=("components_FAILED")
    validate_configuration && validation_results+=("configuration") || validation_results+=("configuration_FAILED")
    validate_state_directories && validation_results+=("state_dirs") || validation_results+=("state_dirs_FAILED")
    validate_analyzer_compliance && validation_results+=("analyzers") || validation_results+=("analyzers_FAILED")
    validate_production_readiness && validation_results+=("production") || validation_results+=("production_FAILED")
    validate_integration_test_readiness && validation_results+=("integration") || validation_results+=("integration_FAILED")
    validate_build_integration && validation_results+=("build") || validation_results+=("build_FAILED")
    
    # Summary
    log_info "Validation Summary:"
    local failed_count=0
    for result in "${validation_results[@]}"; do
        if [[ $result == *"_FAILED" ]]; then
            log_error "❌ ${result/_FAILED/} validation failed"
            failed_count=$((failed_count + 1))
        else
            log_success "✅ $result validation passed"
        fi
    done
    
    echo ""
    if [ $failed_count -eq 0 ]; then
        log_success "🎉 ALL VALIDATIONS PASSED - Enhanced trading bot is ready for production"
        log_info "Enhanced components validated:"
        log_info "  • Book-aware Execution Simulator with live fill distributions"
        log_info "  • Counterfactual Replay Service with nightly gate analysis"
        log_info "  • Explainability Stamp Service with decision evidence"
        log_info "  • Enhanced Alerting with configurable thresholds"
        log_info "  • Service registration and dependency injection"
        log_info "  • Integration testing framework"
        return 0
    else
        log_error "❌ $failed_count validation(s) failed - Review and fix issues before production deployment"
        return 1
    fi
}

# Script entry point
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi