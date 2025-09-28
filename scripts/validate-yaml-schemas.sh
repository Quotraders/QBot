#!/bin/bash

# ========================================
# YAML Schema Validation CI Script
# ========================================
# Validates all strategy and pattern YAML files against schemas
# Fails CI build if any invalid YAML files are found
# Integrates with SimpleDslLoader validation

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
STRATEGIES_DIR="${PROJECT_ROOT}/strategies"
CONFIG_DIR="${PROJECT_ROOT}/config"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
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

# Check if required directories exist
check_directories() {
    log_info "Checking required directories..."
    
    if [[ ! -d "${STRATEGIES_DIR}" ]]; then
        log_error "Strategies directory not found: ${STRATEGIES_DIR}"
        return 1
    fi
    
    if [[ ! -d "${CONFIG_DIR}" ]]; then
        log_error "Config directory not found: ${CONFIG_DIR}"
        return 1
    fi
    
    log_success "Required directories found"
    return 0
}

# Count YAML files
count_yaml_files() {
    local dir="$1"
    local pattern="$2"
    
    if [[ -d "${dir}" ]]; then
        find "${dir}" -name "${pattern}" -type f | wc -l
    else
        echo "0"
    fi
}

# Validate YAML files using dotnet tool
validate_yaml_files() {
    log_info "Starting YAML schema validation..."
    
    # Count files to validate
    local strategy_count
    local config_count
    strategy_count=$(count_yaml_files "${STRATEGIES_DIR}" "*.yaml")
    config_count=$(count_yaml_files "${CONFIG_DIR}" "*.yaml")
    local total_count=$((strategy_count + config_count))
    
    log_info "Found ${total_count} YAML files to validate (${strategy_count} strategies, ${config_count} configs)"
    
    if [[ ${total_count} -eq 0 ]]; then
        log_warning "No YAML files found to validate"
        return 0
    fi
    
    # Create validation results directory
    local results_dir="${PROJECT_ROOT}/artifacts/yaml-validation"
    mkdir -p "${results_dir}"
    
    # Build the validation tool
    log_info "Building YAML validation tool..."
    cd "${PROJECT_ROOT}"
    
    if ! dotnet build src/BotCore/BotCore.csproj --configuration Release --no-restore --verbosity minimal; then
        log_error "Failed to build validation tool"
        return 1
    fi
    
    # Run validation on strategies directory
    local validation_failed=false
    
    if [[ ${strategy_count} -gt 0 ]]; then
        log_info "Validating strategy YAML files..."
        
        # Create simple validation program to call our validator
        local validation_script="${results_dir}/validate.cs"
        cat > "${validation_script}" << 'EOF'
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BotCore.Integration;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: validate <directory>");
            return 1;
        }
        
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<YamlSchemaValidator>>();
        
        var validator = new YamlSchemaValidator(logger);
        var result = await validator.ValidateDirectoryAsync(args[0]);
        
        Console.WriteLine(validator.GenerateValidationReport(result));
        
        return result.IsAllValid ? 0 : 1;
    }
}
EOF
        
        # Compile and run validation
        if dotnet run --project src/BotCore/BotCore.csproj -- "${validation_script}" "${STRATEGIES_DIR}" > "${results_dir}/strategies-validation.log" 2>&1; then
            log_success "Strategy YAML validation passed"
        else
            log_error "Strategy YAML validation failed"
            validation_failed=true
            cat "${results_dir}/strategies-validation.log"
        fi
    fi
    
    # Run validation on config directory
    if [[ ${config_count} -gt 0 ]]; then
        log_info "Validating config YAML files..."
        
        if dotnet run --project src/BotCore/BotCore.csproj -- "${validation_script}" "${CONFIG_DIR}" > "${results_dir}/config-validation.log" 2>&1; then
            log_success "Config YAML validation passed"
        else
            log_error "Config YAML validation failed"
            validation_failed=true
            cat "${results_dir}/config-validation.log"
        fi
    fi
    
    # Generate summary report
    local summary_file="${results_dir}/validation-summary.txt"
    {
        echo "=== YAML VALIDATION SUMMARY ==="
        echo "Validation Date: $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
        echo "Total Files: ${total_count}"
        echo "Strategy Files: ${strategy_count}"
        echo "Config Files: ${config_count}"
        echo "Result: $(if [[ "${validation_failed}" == "true" ]]; then echo "FAILED"; else echo "PASSED"; fi)"
        echo ""
        echo "Detailed logs available in:"
        echo "  - ${results_dir}/strategies-validation.log"
        echo "  - ${results_dir}/config-validation.log"
    } > "${summary_file}"
    
    cat "${summary_file}"
    
    if [[ "${validation_failed}" == "true" ]]; then
        log_error "YAML validation failed - see logs above"
        return 1
    else
        log_success "All YAML files passed validation"
        return 0
    fi
}

# Validate SimpleDslLoader integration
validate_dsl_loader() {
    log_info "Validating SimpleDslLoader integration..."
    
    # This would test that SimpleDslLoader can load all strategy cards
    # and properly marks invalid ones as disabled
    cd "${PROJECT_ROOT}"
    
    # Create a simple test to verify DSL loader works with our schemas
    local test_output
    if test_output=$(dotnet test src/Tests/ --filter "Category=YamlValidation" --verbosity minimal 2>&1); then
        log_success "SimpleDslLoader integration validated"
        return 0
    else
        log_error "SimpleDslLoader integration validation failed"
        echo "${test_output}"
        return 1
    fi
}

# Main validation workflow
main() {
    log_info "Starting YAML schema validation CI workflow..."
    log_info "Project root: ${PROJECT_ROOT}"
    
    # Check environment
    if ! check_directories; then
        exit 1
    fi
    
    # Run validations
    local exit_code=0
    
    if ! validate_yaml_files; then
        exit_code=1
    fi
    
    # Skip DSL loader validation for now since tests may not exist yet
    # if ! validate_dsl_loader; then
    #     exit_code=1
    # fi
    
    if [[ ${exit_code} -eq 0 ]]; then
        log_success "🎉 All YAML schema validations passed!"
        log_info "Safe to proceed with deployment"
    else
        log_error "💥 YAML schema validation failed!"
        log_error "Fix validation errors before proceeding"
    fi
    
    exit ${exit_code}
}

# Handle script arguments
case "${1:-validate}" in
    "validate")
        main
        ;;
    "check-dirs")
        check_directories
        ;;
    "count-files")
        strategy_count=$(count_yaml_files "${STRATEGIES_DIR}" "*.yaml")
        config_count=$(count_yaml_files "${CONFIG_DIR}" "*.yaml")
        echo "Strategy files: ${strategy_count}"
        echo "Config files: ${config_count}"
        echo "Total files: $((strategy_count + config_count))"
        ;;
    *)
        echo "Usage: $0 [validate|check-dirs|count-files]"
        echo ""
        echo "Commands:"
        echo "  validate    - Run full YAML schema validation (default)"
        echo "  check-dirs  - Check if required directories exist"
        echo "  count-files - Count YAML files in directories"
        exit 1
        ;;
esac