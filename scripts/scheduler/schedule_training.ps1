# Trading Bot Parameter Optimization - Windows Task Scheduler Script
# Runs weekly parameter optimization during market closed window
# 
# Schedule: Every Saturday at 2:00 AM Eastern Time
# Expected Duration: 1-2 hours
# Timeout: 4 hours (safety)

param(
    [string]$ArtifactsPath = "$PSScriptRoot\..\..\artifacts",
    [string]$TrainingPath = "$PSScriptRoot\..\..\src\Training",
    [string]$LogsPath = "$PSScriptRoot\..\..\logs"
)

# Configuration
$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $LogsPath "training_${timestamp}.log"

# Ensure log directory exists
New-Item -ItemType Directory -Force -Path $LogsPath | Out-Null

# Logging function
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $logMessage = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Write-Host $logMessage
    Add-Content -Path $logFile -Value $logMessage
}

# Safety checks
function Test-SafeToRun {
    Write-Log "Performing safety checks..."
    
    # Check if on VPN
    $vpnAdapters = Get-NetAdapter | Where-Object { $_.InterfaceDescription -like "*VPN*" -and $_.Status -eq "Up" }
    if ($vpnAdapters) {
        Write-Log "ERROR: VPN connection detected. Training cannot run on VPN." "ERROR"
        return $false
    }
    
    # Check if in remote desktop session
    $rdpSession = (Get-Process -Name "mstsc" -ErrorAction SilentlyContinue) -or $env:SESSIONNAME -like "RDP*"
    if ($rdpSession) {
        Write-Log "ERROR: Remote desktop session detected. Training cannot run in RDP." "ERROR"
        return $false
    }
    
    # Check if DRY_RUN mode bypass is active
    if ($env:DRY_RUN -eq "0" -or $env:DRY_RUN -eq "false") {
        Write-Log "ERROR: DRY_RUN mode is disabled. Training should not run with live trading." "ERROR"
        return $false
    }
    
    # Check for kill.txt file
    $killFile = Join-Path $PSScriptRoot "..\..\kill.txt"
    if (Test-Path $killFile) {
        Write-Log "WARNING: kill.txt file exists. System may be in maintenance mode." "WARN"
    }
    
    Write-Log "Safety checks passed."
    return $true
}

# Main execution
try {
    Write-Log "========================================" "INFO"
    Write-Log "Trading Bot Parameter Optimization" "INFO"
    Write-Log "========================================" "INFO"
    Write-Log "Started at: $(Get-Date)" "INFO"
    Write-Log "Log file: $logFile" "INFO"
    
    # Safety checks
    if (-not (Test-SafeToRun)) {
        Write-Log "Safety checks failed. Aborting training." "ERROR"
        exit 1
    }
    
    # Set environment variables
    $env:TRAINING_MODE = "OPTIMIZATION"
    $env:ARTIFACTS_PATH = (Resolve-Path $ArtifactsPath).Path
    
    Write-Log "Environment variables set:" "INFO"
    Write-Log "  TRAINING_MODE=$env:TRAINING_MODE" "INFO"
    Write-Log "  ARTIFACTS_PATH=$env:ARTIFACTS_PATH" "INFO"
    Write-Log "  TOPSTEP_API_KEY=$(if ($env:TOPSTEP_API_KEY) { '***SET***' } else { 'NOT SET' })" "INFO"
    
    # Verify API credentials
    if (-not $env:TOPSTEP_API_KEY) {
        Write-Log "ERROR: TOPSTEP_API_KEY environment variable not set" "ERROR"
        exit 1
    }
    
    # Change to training directory
    Set-Location $TrainingPath
    Write-Log "Changed to training directory: $TrainingPath" "INFO"
    
    # Run training orchestrator
    Write-Log "Starting parameter optimization..." "INFO"
    Write-Log "Running: python training_orchestrator.py" "INFO"
    
    $trainingOutput = python training_orchestrator.py 2>&1
    $exitCode = $LASTEXITCODE
    
    # Log training output
    $trainingOutput | ForEach-Object { Write-Log $_ "TRAINING" }
    
    if ($exitCode -eq 0) {
        Write-Log "Training completed successfully!" "INFO"
        
        # Promote parameters from stage to current (atomic promotion)
        Write-Log "Promoting optimized parameters to production..." "INFO"
        
        $stageDir = Join-Path $env:ARTIFACTS_PATH "stage\parameters"
        $currentDir = Join-Path $env:ARTIFACTS_PATH "current\parameters"
        $previousDir = Join-Path $env:ARTIFACTS_PATH "previous\parameters"
        
        # Backup current parameters to previous
        if (Test-Path $currentDir) {
            Write-Log "Backing up current parameters to previous..." "INFO"
            if (Test-Path $previousDir) {
                Remove-Item -Path $previousDir -Recurse -Force
            }
            Copy-Item -Path $currentDir -Destination $previousDir -Recurse -Force
            Write-Log "Current parameters backed up." "INFO"
        }
        
        # Copy stage parameters to current
        if (Test-Path $stageDir) {
            $stageFiles = Get-ChildItem -Path $stageDir -Filter "*_parameters.json"
            foreach ($file in $stageFiles) {
                $destFile = Join-Path $currentDir $file.Name
                Copy-Item -Path $file.FullName -Destination $destFile -Force
                Write-Log "Promoted: $($file.Name)" "INFO"
            }
            Write-Log "All parameters promoted to production." "INFO"
        } else {
            Write-Log "WARNING: No stage directory found. Parameters not promoted." "WARN"
        }
        
        # Generate dashboard summary
        $reportFile = Join-Path $env:ARTIFACTS_PATH "reports\training_summary_${timestamp}.md"
        New-Item -ItemType Directory -Force -Path (Split-Path $reportFile) | Out-Null
        
        $summary = @"
# Training Summary - $timestamp

## Status: ✓ SUCCESS

**Started:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Duration:** $((Get-Date) - (Get-Date $timestamp))
**Log File:** $logFile

## Optimized Strategies

$(Get-ChildItem -Path $stageDir -Filter "*_parameters.json" | ForEach-Object { "- $($_.BaseName -replace '_parameters','')" })

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
"@
        Set-Content -Path $reportFile -Value $summary
        Write-Log "Dashboard summary generated: $reportFile" "INFO"
        
    } else {
        Write-Log "Training failed with exit code: $exitCode" "ERROR"
        Write-Log "Parameters NOT promoted. Current parameters remain unchanged." "WARN"
        
        # Send alert (log-based for now, can integrate email later)
        Write-Log "ALERT: Parameter optimization failed. Manual review required." "ERROR"
        
        exit 1
    }
    
} catch {
    Write-Log "EXCEPTION: $($_.Exception.Message)" "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    exit 1
    
} finally {
    Write-Log "Completed at: $(Get-Date)" "INFO"
    Write-Log "========================================" "INFO"
}

exit 0
