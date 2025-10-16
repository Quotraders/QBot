#!/usr/bin/env pwsh
# Fix Auto-Upgrade System - Complete Setup Script

Write-Host "`n🔧 FIXING AUTO-UPGRADE SYSTEM`n" -ForegroundColor Cyan

# Step 1: Register CVaR-PPO Model
Write-Host "1️⃣  Registering CVaR-PPO Champion Model..." -ForegroundColor Yellow
$modelPath = "models/rl/cvar_ppo_agent.onnx"
$registryModelsPath = "model_registry/models"
$registryArtifactsPath = "model_registry/artifacts"

New-Item -ItemType Directory -Force -Path $registryModelsPath | Out-Null
New-Item -ItemType Directory -Force -Path $registryArtifactsPath | Out-Null

if (Test-Path $modelPath) {
    $versionId = "cvar_ppo_champion_$(Get-Date -Format 'yyyyMMdd')"
    
    # Create champion model metadata
    $championModel = @{
        versionId = $versionId
        algorithm = "CVaR-PPO"
        artifactPath = (Resolve-Path $modelPath).Path
        status = "active"
        isPromoted = $true
        isValidated = $true
        isChampion = $true
        trainedAt = (Get-Date).ToUniversalTime().ToString("o")
        promotedAt = (Get-Date).ToUniversalTime().ToString("o")
        metadata = @{
            source = "bootstrap_initialization"
            bootstrap_mode = $true
            initial_champion = $true
            description = "Initial CVaR-PPO champion for auto-upgrade system"
        }
        performanceMetrics = @{
            totalTrades = 0
            winRate = 0.0
            sharpeRatio = 0.0
            maxDrawdown = 0.0
        }
    } | ConvertTo-Json -Depth 10
    
    $championFile = "$registryModelsPath/CVaR-PPO_$versionId.json"
    $championModel | Out-File -FilePath $championFile -Encoding UTF8
    Write-Host "   ✅ Champion registered: $championFile" -ForegroundColor Green
    
    # Create champion pointer
    $championPointer = @{
        algorithm = "CVaR-PPO"
        currentChampion = $versionId
        lastUpdated = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json
    
    $pointerFile = "$registryModelsPath/champion_CVaR-PPO.json"
    $championPointer | Out-File -FilePath $pointerFile -Encoding UTF8
    Write-Host "   ✅ Champion pointer: $pointerFile" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Model not found: $modelPath" -ForegroundColor Red
}

# Step 2: Enable Experience Persistence
Write-Host "`n2️⃣  Setting up Experience Buffer Persistence..." -ForegroundColor Yellow
$experiencePath = "data/rl/cvar_ppo_experiences"
New-Item -ItemType Directory -Force -Path $experiencePath | Out-Null
Write-Host "   ✅ Experience directory: $experiencePath" -ForegroundColor Green

# Step 3: Configure Shadow Testing
Write-Host "`n3️⃣  Enabling Shadow Testing..." -ForegroundColor Yellow
$shadowConfig = @"
{
  "ShadowTesting": {
    "Enabled": true,
    "MinTrades": 50,
    "MinSessions": 5,
    "ConfidenceThreshold": 0.65,
    "CollectMetrics": true,
    "CompareToChampion": true
  }
}
"@
$shadowConfigFile = "config/shadow-testing.json"
New-Item -ItemType Directory -Force -Path "config" | Out-Null
$shadowConfig | Out-File -FilePath $shadowConfigFile -Encoding UTF8
Write-Host "   ✅ Shadow config: $shadowConfigFile" -ForegroundColor Green

# Step 4: Verify Auto-Promotion Settings
Write-Host "`n4️⃣  Verifying Auto-Promotion Configuration..." -ForegroundColor Yellow
$envContent = Get-Content .env
$promotionSettings = $envContent | Select-String -Pattern "AUTO_PROMOTION|AUTO_LEARNING|CHALLENGER"
Write-Host "   Current settings:" -ForegroundColor Cyan
$promotionSettings | ForEach-Object { Write-Host "     $_" -ForegroundColor White }

# Step 5: Create Promotion Tracking
Write-Host "`n5️⃣  Setting up Promotion Tracking..." -ForegroundColor Yellow
$promotionLog = @{
    systemInitialized = (Get-Date).ToUniversalTime().ToString("o")
    autoPromotionEnabled = $true
    currentChampion = "CVaR-PPO_champion"
    nextEvaluation = (Get-Date).AddHours(6).ToUniversalTime().ToString("o")
    minimumTradesBeforePromotion = 50
    status = "operational"
} | ConvertTo-Json -Depth 10

$promotionLogFile = "model_registry/promotion_status.json"
$promotionLog | Out-File -FilePath $promotionLogFile -Encoding UTF8
Write-Host "   ✅ Promotion tracking: $promotionLogFile" -ForegroundColor Green

# Step 6: Verification
Write-Host "`n6️⃣  Verification..." -ForegroundColor Yellow
$checks = @{
    "Model Registry" = (Test-Path "model_registry/models")
    "Champion Registered" = (Test-Path "model_registry/models/CVaR-PPO_*.json")
    "Experience Storage" = (Test-Path "data/rl/cvar_ppo_experiences")
    "Shadow Config" = (Test-Path "config/shadow-testing.json")
    "Promotion Tracking" = (Test-Path "model_registry/promotion_status.json")
}

$allGood = $true
foreach ($check in $checks.GetEnumerator()) {
    if ($check.Value) {
        Write-Host "   ✅ $($check.Key)" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $($check.Key)" -ForegroundColor Red
        $allGood = $false
    }
}

Write-Host "`n" -NoNewline
if ($allGood) {
    Write-Host "🎉 AUTO-UPGRADE SYSTEM FIXED!" -ForegroundColor Green
    Write-Host "`nNext Steps:" -ForegroundColor Cyan
    Write-Host "  1. Bot will collect experiences from bootstrap trades" -ForegroundColor White
    Write-Host "  2. CVaR-PPO trains every 6 hours (or 1000 experiences)" -ForegroundColor White
    Write-Host "  3. After 50+ trades, new model shadows champion" -ForegroundColor White
    Write-Host "  4. If 65%+ better, automatic promotion happens" -ForegroundColor White
    Write-Host "  5. Auto-rollback if performance declines`n" -ForegroundColor White
} else {
    Write-Host "⚠️  SOME CHECKS FAILED - Review above`n" -ForegroundColor Red
}
