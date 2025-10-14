# run-bot-wsl.ps1 - Run trading bot with WSL Python and TopstepX credentials
$ErrorActionPreference = "Stop"

Write-Host "=== Trading Bot with WSL ===" -ForegroundColor Cyan

# Set WSL mode
$env:PYTHON_EXECUTABLE = "wsl"
Write-Host "[OK] PYTHON_EXECUTABLE = wsl" -ForegroundColor Green

# Load .env 
$envPath = ".\.env"
if (Test-Path $envPath) {
    Write-Host "[OK] Loading .env..." -ForegroundColor Green
    Get-Content $envPath | Where-Object { $_ -match '^[A-Z]' -and $_ -notmatch '^#' } | ForEach-Object {
        if ($_ -match '^([A-Z_]+)=(.*)$') {
            $key = $matches[1]
            $value = $matches[2]
            Set-Item -Path "env:$key" -Value $value
        }
    }
}

# Show cred status
Write-Host "`nCredentials:" -ForegroundColor Cyan
Write-Host "  API_KEY: $( if ($env:TOPSTEPX_API_KEY) { '[SET]' } else { '[MISSING]' } )"
Write-Host "  USERNAME: $( if ($env:TOPSTEPX_USERNAME) { $env:TOPSTEPX_USERNAME } else { '[MISSING]' } )"
Write-Host "  ACCOUNT: $( if ($env:TOPSTEPX_ACCOUNT_ID) { $env:TOPSTEPX_ACCOUNT_ID } else { '[MISSING]' } )`n"

# Run bot
cd src\UnifiedOrchestrator
Write-Host "Starting bot...`n" -ForegroundColor Cyan
dotnet run --no-build
