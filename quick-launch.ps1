#!/usr/bin/env pwsh
# Quick launcher for the QBot - Simplified version
# Just runs the bot with minimal setup

param(
    [switch]$Help
)

if ($Help) {
    Write-Host @"
QBot Quick Launcher

Usage: .\quick-launch.ps1 [options]

Options:
  -Help       Show this help message

This script will:
1. Verify prerequisites (.NET, Python, .env)
2. Build the UnifiedOrchestrator project
3. Launch the bot
4. Display output in real-time

To stop: Press Ctrl+C

For diagnostics with log capture, use:
  .\launch-bot-diagnostic.ps1

For more information, see:
  COPILOT_REAL_TIME_DEBUGGING_GUIDE.md
"@
    exit 0
}

# Simple colored output
function Write-Step($message) {
    Write-Host "[*] $message" -ForegroundColor Cyan
}

function Write-OK($message) {
    Write-Host "[✓] $message" -ForegroundColor Green
}

function Write-Fail($message) {
    Write-Host "[✗] $message" -ForegroundColor Red
}

Clear-Host
Write-Host ""
Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║         QBot Quick Launcher                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Quick prerequisite check
Write-Step "Checking prerequisites..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail ".NET SDK not found"
    exit 1
}
Write-OK ".NET SDK found"

if (-not (Test-Path ".env")) {
    Write-Fail ".env file not found - copy .env.example to .env and configure"
    exit 1
}
Write-OK ".env file found"

if (-not (Test-Path "src/UnifiedOrchestrator/UnifiedOrchestrator.csproj")) {
    Write-Fail "UnifiedOrchestrator project not found"
    exit 1
}
Write-OK "UnifiedOrchestrator project found"

# Build
Write-Step "Building UnifiedOrchestrator..."
$buildResult = dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --verbosity quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-OK "Build successful"
} else {
    Write-Fail "Build failed"
    Write-Host $buildResult
    exit 1
}

# Launch
Write-Host ""
Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Yellow
Write-Host "║     Starting UnifiedOrchestrator...        ║" -ForegroundColor Yellow
Write-Host "║     Press Ctrl+C to stop                   ║" -ForegroundColor Yellow
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Yellow
Write-Host ""

# Set development environment
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Run the bot
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --no-build
