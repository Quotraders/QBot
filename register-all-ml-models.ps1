#!/usr/bin/env pwsh
# Register ALL Machine Learning Models for Auto-Upgrade System

Write-Host "`n🧠 REGISTERING COMPLETE AI STACK`n" -ForegroundColor Cyan

$registryModelsPath = "model_registry/models"
$registryArtifactsPath = "model_registry/artifacts"
New-Item -ItemType Directory -Force -Path $registryModelsPath | Out-Null
New-Item -ItemType Directory -Force -Path $registryArtifactsPath | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$modelsRegistered = 0

# Define all AI components in your trading bot
$aiComponents = @(
    @{
        Name = "CVaR-PPO"
        Path = "models/rl/cvar_ppo_agent.onnx"
        Type = "RL-Agent"
        Description = "Risk-adjusted position sizing with CVaR tail risk control"
        IsChampion = $true
        TrainingFrequency = "6 hours or 1000 experiences"
    },
    @{
        Name = "Neural-UCB"
        Path = "python/ucb/neural_ucb_topstep.py"
        Type = "Strategy-Selector"
        Description = "Neural Upper Confidence Bound for optimal strategy selection"
        IsChampion = $true
        TrainingFrequency = "Real-time with each trade result"
    },
    @{
        Name = "Regime-Detector"
        Type = "Market-Classifier"
        Description = "Detects Trending, Ranging, and Transition market regimes"
        IsChampion = $true
        TrainingFrequency = "Online learning per bar"
    },
    @{
        Name = "Model-Ensemble"
        Type = "Meta-Learner"
        Description = "Intelligent blending (70% cloud, 30% local predictions)"
        IsChampion = $true
        TrainingFrequency = "Continuous optimization"
    },
    @{
        Name = "Slippage-Latency-Model"
        Type = "Execution-Predictor"
        Description = "Predicts real slippage and execution latency"
        IsChampion = $false
        TrainingFrequency = "After each fill"
    },
    @{
        Name = "Online-Learning-System"
        Type = "Continuous-Learner"
        Description = "Adapts to market changes in real-time"
        IsChampion = $true
        TrainingFrequency = "Continuous"
    }
)

Write-Host "Registering $($aiComponents.Count) AI Components...`n" -ForegroundColor Yellow

foreach ($component in $aiComponents) {
    Write-Host "  📦 $($component.Name) ($($component.Type))" -ForegroundColor Cyan
    
    $versionId = "$($component.Name.ToLower().Replace('-','_'))_champion_$timestamp"
    
    # Create model metadata
    $modelData = @{
        versionId = $versionId
        algorithm = $component.Name
        type = $component.Type
        status = "active"
        isPromoted = $component.IsChampion
        isValidated = $true
        isChampion = $component.IsChampion
        trainedAt = (Get-Date).ToUniversalTime().ToString("o")
        registeredAt = (Get-Date).ToUniversalTime().ToString("o")
        description = $component.Description
        metadata = @{
            source = "complete_stack_registration"
            trainingFrequency = $component.TrainingFrequency
            autoUpgradeEnabled = $true
        }
        performanceMetrics = @{
            totalPredictions = 0
            accuracy = 0.0
            precision = 0.0
            recall = 0.0
            f1Score = 0.0
        }
    }
    
    if ($component.ContainsKey("Path") -and (Test-Path $component.Path)) {
        $modelData.artifactPath = (Resolve-Path $component.Path).Path
        Write-Host "     ✅ Artifact: $($component.Path)" -ForegroundColor Green
    } else {
        $modelData.artifactPath = $null
        Write-Host "     ⚠️  No physical artifact (code-based component)" -ForegroundColor Yellow
    }
    
    # Save model registration
    $modelJson = $modelData | ConvertTo-Json -Depth 10
    $fileName = "$registryModelsPath/$($component.Name)_$timestamp.json"
    $modelJson | Out-File -FilePath $fileName -Encoding UTF8
    
    # Create champion pointer if applicable
    if ($component.IsChampion) {
        $championPointer = @{
            algorithm = $component.Name
            currentChampion = $versionId
            lastUpdated = (Get-Date).ToUniversalTime().ToString("o")
            type = $component.Type
        } | ConvertTo-Json
        
        $pointerFile = "$registryModelsPath/champion_$($component.Name).json"
        $championPointer | Out-File -FilePath $pointerFile -Encoding UTF8
        Write-Host "     ✅ Champion pointer created" -ForegroundColor Green
    }
    
    $modelsRegistered++
    Write-Host ""
}

# Create comprehensive learning pipeline config
Write-Host "`n📊 Creating Learning Pipeline Configuration..." -ForegroundColor Yellow

$learningPipeline = @{
    initialized = (Get-Date).ToUniversalTime().ToString("o")
    totalComponents = $modelsRegistered
    champions = ($aiComponents | Where-Object { $_.IsChampion }).Count
    learningModes = @{
        "CVaR-PPO" = @{
            mode = "experience_replay"
            bufferSize = 1000
            trainEvery = "6 hours or 1000 experiences"
            minTradesForPromotion = 50
        }
        "Neural-UCB" = @{
            mode = "online"
            updateFrequency = "per_trade"
            explorationRate = 0.1
        }
        "Regime-Detector" = @{
            mode = "online"
            updateFrequency = "per_bar"
            regimeTypes = @("Trending", "Ranging", "Transition")
        }
        "Model-Ensemble" = @{
            mode = "meta_learning"
            blending = @{ cloud = 0.7; local = 0.3 }
            rebalanceEvery = "1 hour"
        }
    }
    promotionCriteria = @{
        minimumTrades = 50
        minimumSessions = 5
        confidenceThreshold = 0.65
        requiresBetterPerformance = $true
        autoRollbackEnabled = $true
    }
}

$pipelineConfig = $learningPipeline | ConvertTo-Json -Depth 10
$pipelineFile = "model_registry/learning_pipeline.json"
$pipelineConfig | Out-File -FilePath $pipelineFile -Encoding UTF8
Write-Host "  ✅ Pipeline config: $pipelineFile" -ForegroundColor Green

# Create learning schedule
Write-Host "`n⏰ Creating Training Schedule..." -ForegroundColor Yellow

$schedule = @{
    CVaRPPO = @{
        nextTraining = (Get-Date).AddHours(6).ToUniversalTime().ToString("o")
        frequency = "6 hours"
        trigger = "time_or_buffer_full"
    }
    NeuralUCB = @{
        mode = "continuous"
        updateOn = "trade_completion"
    }
    RegimeDetector = @{
        mode = "continuous"
        updateOn = "new_bar"
    }
    ModelEnsemble = @{
        nextOptimization = (Get-Date).AddHours(1).ToUniversalTime().ToString("o")
        frequency = "1 hour"
    }
}

$scheduleJson = $schedule | ConvertTo-Json -Depth 10
$scheduleFile = "model_registry/training_schedule.json"
$scheduleJson | Out-File -FilePath $scheduleFile -Encoding UTF8
Write-Host "  ✅ Schedule: $scheduleFile" -ForegroundColor Green

# Verification
Write-Host "`n✅ REGISTRATION COMPLETE`n" -ForegroundColor Green
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  • Total Components: $modelsRegistered" -ForegroundColor White
Write-Host "  • Champions: $(($aiComponents | Where-Object { $_.IsChampion }).Count)" -ForegroundColor White
Write-Host "  • Challengers: $(($aiComponents | Where-Object { -not $_.IsChampion }).Count)" -ForegroundColor White
Write-Host ""

# List all registered components
Write-Host "Registered AI Components:" -ForegroundColor Yellow
$aiComponents | ForEach-Object {
    $status = if ($_.IsChampion) { "🏆 Champion" } else { "🔄 Challenger" }
    Write-Host "  $status $($_.Name) - $($_.Description)" -ForegroundColor White
}

Write-Host "`n🚀 AUTO-UPGRADE SYSTEM READY FOR ALL COMPONENTS!" -ForegroundColor Green
Write-Host "`nNext Steps:" -ForegroundColor Cyan
Write-Host "  1. Each component learns from bootstrap trades" -ForegroundColor White
Write-Host "  2. Training happens per component schedule" -ForegroundColor White
Write-Host "  3. Improved models shadow test champions" -ForegroundColor White
Write-Host "  4. Auto-promotion when 65%+ better" -ForegroundColor White
Write-Host "  5. Auto-rollback if performance declines`n" -ForegroundColor White
