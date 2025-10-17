#!/usr/bin/env pwsh
# PowerShell script to launch the bot in diagnostic mode
# This script captures full output and creates a diagnostic report

param(
    [int]$RuntimeMinutes = 5,
    [switch]$NoLogFile,
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"

# Colors for output
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Info($message) {
    Write-ColorOutput Cyan "[INFO] $message"
}

function Write-Success($message) {
    Write-ColorOutput Green "[SUCCESS] $message"
}

function Write-Warning($message) {
    Write-ColorOutput Yellow "[WARNING] $message"
}

function Write-ErrorMsg($message) {
    Write-ColorOutput Red "[ERROR] $message"
}

# Header
Write-Host ""
Write-ColorOutput Cyan "═══════════════════════════════════════════════════════"
Write-ColorOutput Cyan "   QBot Diagnostic Launcher"
Write-ColorOutput Cyan "═══════════════════════════════════════════════════════"
Write-Host ""

# Check prerequisites
Write-Info "Checking prerequisites..."

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK detected: $dotnetVersion"
} catch {
    Write-ErrorMsg ".NET SDK not found. Please install .NET 8.0+ from https://dotnet.microsoft.com/download"
    exit 1
}

# Check Python
try {
    $pythonVersion = python --version 2>&1
    Write-Success "Python detected: $pythonVersion"
} catch {
    Write-Warning "Python not found in PATH. Make sure PYTHON_EXECUTABLE is set correctly in .env"
}

# Check .env file
if (-not (Test-Path ".env")) {
    Write-ErrorMsg ".env file not found!"
    Write-Info "Creating .env from template..."
    if (Test-Path ".env.example") {
        Copy-Item ".env.example" ".env"
        Write-Warning "Please edit .env with your TopstepX credentials before continuing"
        exit 1
    } else {
        Write-ErrorMsg ".env.example not found. Cannot create .env"
        exit 1
    }
} else {
    Write-Success ".env file found"
}

# Check TopstepX credentials in .env
$envContent = Get-Content ".env" -Raw
if ($envContent -match "TOPSTEPX_API_KEY=.+") {
    Write-Success "TopstepX API key found in .env"
} else {
    Write-Warning "TOPSTEPX_API_KEY not found or empty in .env"
}

# Check UnifiedOrchestrator project
if (-not (Test-Path "src/UnifiedOrchestrator/UnifiedOrchestrator.csproj")) {
    Write-ErrorMsg "UnifiedOrchestrator project not found!"
    exit 1
}

# Build the project
Write-Info "Building UnifiedOrchestrator..."
$buildOutput = dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --no-restore 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "Build successful"
} else {
    Write-ErrorMsg "Build failed!"
    Write-Host $buildOutput
    exit 1
}

# Setup logging
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logsDir = "logs"

if (-not $NoLogFile) {
    if (-not (Test-Path $logsDir)) {
        New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
    }
    $logFile = "$logsDir/bot-diagnostic-$timestamp.log"
    $errorLogFile = "$logsDir/bot-errors-$timestamp.log"
    Write-Info "Logs will be saved to:"
    Write-Host "  - Console: $logFile"
    Write-Host "  - Errors:  $errorLogFile"
}

# Set environment variables for enhanced diagnostics
$env:ASPNETCORE_ENVIRONMENT = "Development"
if ($Verbose) {
    $env:Logging__LogLevel__Default = "Debug"
    $env:Logging__LogLevel__TradingBot = "Trace"
}

# Display runtime configuration
Write-Host ""
Write-Info "Runtime Configuration:"
Write-Host "  - Runtime Duration: $RuntimeMinutes minutes"
Write-Host "  - Verbose Logging: $Verbose"
Write-Host "  - Log to File: $(-not $NoLogFile)"
Write-Host ""

# Start countdown
Write-ColorOutput Yellow "═══════════════════════════════════════════════════════"
Write-ColorOutput Yellow "   Bot will run for $RuntimeMinutes minutes"
Write-ColorOutput Yellow "   Press Ctrl+C to stop early"
Write-ColorOutput Yellow "═══════════════════════════════════════════════════════"
Write-Host ""

# Calculate end time
$endTime = (Get-Date).AddMinutes($RuntimeMinutes)

# Launch the bot
Write-Info "Starting UnifiedOrchestrator..."
Write-Host ""

try {
    if ($NoLogFile) {
        # Run without logging to file
        $job = Start-Job -ScriptBlock {
            param($projectPath)
            Set-Location $projectPath
            dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj 2>&1
        } -ArgumentList $PWD
    } else {
        # Run with logging to file
        $job = Start-Job -ScriptBlock {
            param($projectPath, $logPath, $errorLogPath)
            Set-Location $projectPath
            $output = dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj 2>&1
            $output | ForEach-Object {
                $_ | Out-File -Append -FilePath $logPath
                if ($_ -match "error|exception|failed" -and $_ -notmatch "0 Error\(s\)") {
                    $_ | Out-File -Append -FilePath $errorLogPath
                }
                $_
            }
        } -ArgumentList $PWD, (Resolve-Path $logFile).Path, (Resolve-Path $errorLogFile).Path
    }

    # Monitor job output
    $lastOutputTime = Get-Date
    while ((Get-Date) -lt $endTime -and $job.State -eq "Running") {
        $output = Receive-Job -Job $job
        if ($output) {
            $output | ForEach-Object {
                Write-Host $_
            }
            $lastOutputTime = Get-Date
        }
        
        # Display countdown
        $remaining = ($endTime - (Get-Date)).TotalSeconds
        if ([int]$remaining % 30 -eq 0) {
            Write-ColorOutput Cyan "`n[INFO] Bot running... $([math]::Round($remaining/60, 1)) minutes remaining`n"
        }
        
        Start-Sleep -Seconds 1
    }

    # Stop the job
    if ($job.State -eq "Running") {
        Write-Info "Runtime limit reached. Stopping bot gracefully..."
        Stop-Job -Job $job
        Start-Sleep -Seconds 2
    }

    # Get any remaining output
    $output = Receive-Job -Job $job
    if ($output) {
        $output | ForEach-Object {
            Write-Host $_
        }
    }

    Remove-Job -Job $job -Force

    Write-Host ""
    Write-Success "Bot stopped successfully"

} catch {
    Write-ErrorMsg "An error occurred while running the bot:"
    Write-Host $_.Exception.Message
    if ($job) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    exit 1
}

# Generate diagnostic report
Write-Host ""
Write-ColorOutput Cyan "═══════════════════════════════════════════════════════"
Write-ColorOutput Cyan "   Diagnostic Summary"
Write-ColorOutput Cyan "═══════════════════════════════════════════════════════"
Write-Host ""

if (-not $NoLogFile) {
    Write-Info "Log files created:"
    Write-Host "  - $logFile"
    Write-Host "  - $errorLogFile"
    Write-Host ""

    # Count errors
    if (Test-Path $errorLogFile) {
        $errorCount = (Get-Content $errorLogFile | Measure-Object -Line).Lines
        if ($errorCount -gt 0) {
            Write-Warning "Found $errorCount error messages. Check $errorLogFile"
        } else {
            Write-Success "No errors detected!"
        }
    }

    # Check for common issues
    $logContent = Get-Content $logFile -Raw
    
    Write-Info "Checking for common issues..."
    
    if ($logContent -match "TopstepX.*[Ss]uccessful|Authentication.*success") {
        Write-Success "TopstepX authentication successful"
    } elseif ($logContent -match "TopstepX.*failed|Authentication.*failed|401|403") {
        Write-ErrorMsg "TopstepX authentication failed - check your credentials in .env"
    }
    
    if ($logContent -match "Python.*not found|Win32Exception") {
        Write-ErrorMsg "Python executable not found - verify PYTHON_EXECUTABLE in .env"
    }
    
    if ($logContent -match "UnifiedOrchestrator.*started|Application started") {
        Write-Success "UnifiedOrchestrator started successfully"
    }
    
    if ($logContent -match "WebSocket.*connected") {
        Write-Success "WebSocket connection established"
    } elseif ($logContent -match "WebSocket.*failed|SSL.*failed") {
        Write-Warning "WebSocket connection failed - check network/firewall settings"
    }

    Write-Host ""
    Write-Info "Next steps:"
    Write-Host "  1. Review the logs: $logFile"
    Write-Host "  2. Check errors: $errorLogFile"
    Write-Host "  3. Share logs with Copilot for analysis"
    Write-Host ""
    Write-Info "To share with Copilot, create a GitHub issue/PR and paste:"
    Write-Host "  Get-Content '$logFile' | Select-Object -First 100"
    Write-Host ""
}

Write-ColorOutput Green "════════════════════════════════════════════════════════"
Write-ColorOutput Green "   Diagnostic run complete!"
Write-ColorOutput Green "════════════════════════════════════════════════════════"
Write-Host ""
