# Register CVaR-PPO Model at Startup
Write-Host "`n📝 Registering CVaR-PPO model..." -ForegroundColor Cyan

$modelPath = "models/rl/cvar_ppo_agent.onnx"
$registryPath = "model_registry/models"

if (Test-Path $modelPath) {
    $modelInfo = @{
        versionId = "cvar_ppo_v1_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        algorithm = "CVaR-PPO"
        artifactPath = (Resolve-Path $modelPath).Path
        status = "active"
        isPromoted = $false
        isValidated = $true
        trainedAt = (Get-Date).ToUniversalTime().ToString("o")
        metadata = @{
            source = "bootstrap_registration"
            bootstrap_mode = $true
            initial_registration = $true
        }
    } | ConvertTo-Json -Depth 10

    $fileName = "$registryPath/CVaR-PPO_initial.json"
    New-Item -ItemType Directory -Force -Path $registryPath | Out-Null
    $modelInfo | Out-File -FilePath $fileName -Encoding UTF8
    
    Write-Host "✅ Model registered: $fileName" -ForegroundColor Green
} else {
    Write-Host "⚠️  Model not found: $modelPath" -ForegroundColor Red
}
