using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.UnifiedOrchestrator.Interfaces;
using TradingBot.Abstractions;

namespace TradingBot.UnifiedOrchestrator.Services;

/// <summary>
/// Automatically bootstraps the model registry with initial champions on first startup
/// Only runs once - skips if champions already registered
/// </summary>
internal sealed class ModelRegistryBootstrapService : IHostedService
{
    private readonly IModelRegistry _modelRegistry;
    private readonly ILogger<ModelRegistryBootstrapService> _logger;

    public ModelRegistryBootstrapService(
        IModelRegistry modelRegistry,
        ILogger<ModelRegistryBootstrapService> logger)
    {
        _modelRegistry = modelRegistry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔍 [MODEL-BOOTSTRAP] Checking model registry status...");

            // Check if any champions already registered
            var cvarChampion = await _modelRegistry.GetChampionAsync("CVaR-PPO", cancellationToken).ConfigureAwait(false);
            
            if (cvarChampion != null)
            {
                _logger.LogInformation("✅ [MODEL-BOOTSTRAP] Model registry already populated - skipping bootstrap");
                return;
            }

            _logger.LogInformation("🌱 [MODEL-BOOTSTRAP] Empty registry detected - registering initial champions...");

            // 1. CVaR-PPO (Risk-Adjusted RL Agent)
            await RegisterCVaRPPOAsync(cancellationToken).ConfigureAwait(false);

            // 2. Neural-UCB (Strategy Selector)
            await RegisterNeuralUCBAsync(cancellationToken).ConfigureAwait(false);

            // 3. Regime-Detector (Market Classifier)
            await RegisterRegimeDetectorAsync(cancellationToken).ConfigureAwait(false);

            // 4. Model-Ensemble (Meta-Learner)
            await RegisterModelEnsembleAsync(cancellationToken).ConfigureAwait(false);

            // 5. Online-Learning-System (Continuous Adaptation)
            await RegisterOnlineLearningAsync(cancellationToken).ConfigureAwait(false);

            // 6. Slippage-Latency-Model (Execution Predictor)
            await RegisterSlippageModelAsync(cancellationToken).ConfigureAwait(false);

            // 7. S15_RL Policy (RL-Based Strategy)
            await RegisterS15RLPolicyAsync(cancellationToken).ConfigureAwait(false);

            // 8. Historical Pattern Recognition
            await RegisterPatternRecognitionAsync(cancellationToken).ConfigureAwait(false);

            // 9. Position Management Optimizer
            await RegisterPMOptimizerAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("✅ [MODEL-BOOTSTRAP] Successfully registered 9 ML/RL components as champions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [MODEL-BOOTSTRAP] Failed to bootstrap model registry");
            // Don't throw - allow bot to continue even if bootstrap fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RegisterCVaRPPOAsync(CancellationToken cancellationToken)
    {
        var modelPath = Path.Combine("models", "rl", "cvar_ppo_agent.onnx");
        var model = new ModelVersion
        {
            Algorithm = "CVaR-PPO",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = File.Exists(modelPath) ? modelPath : string.Empty,
            Metrics = new PerformanceMetrics
            {
                Sharpe = 0.0,
                WinRate = 0.0,
                MaxDrawdown = 0.0
            },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("CVaR-PPO", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered CVaR-PPO champion");
    }

    private async Task RegisterNeuralUCBAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Neural-UCB",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Python service
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Neural-UCB", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Neural-UCB champion");
    }

    private async Task RegisterRegimeDetectorAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Regime-Detector",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Statistical service
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Regime-Detector", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Regime-Detector champion");
    }

    private async Task RegisterModelEnsembleAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Model-Ensemble",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Meta-learner
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Model-Ensemble", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Model-Ensemble champion");
    }

    private async Task RegisterOnlineLearningAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Online-Learning-System",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Adaptive service
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Online-Learning-System", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Online-Learning-System champion");
    }

    private async Task RegisterSlippageModelAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Slippage-Latency-Model",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Execution metrics
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Slippage-Latency-Model", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Slippage-Latency-Model champion");
    }

    private async Task RegisterS15RLPolicyAsync(CancellationToken cancellationToken)
    {
        var modelPath = Path.Combine("artifacts", "current", "rl_policy.onnx");
        var model = new ModelVersion
        {
            Algorithm = "S15-RL-Policy",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = File.Exists(modelPath) ? modelPath : string.Empty,
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("S15-RL-Policy", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered S15-RL-Policy champion");
    }

    private async Task RegisterPatternRecognitionAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "Pattern-Recognition-System",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Pattern library in memory
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("Pattern-Recognition-System", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered Pattern-Recognition-System champion");
    }

    private async Task RegisterPMOptimizerAsync(CancellationToken cancellationToken)
    {
        var model = new ModelVersion
        {
            Algorithm = "PM-Optimizer",
            VersionId = "v1.0.0-bootstrap",
            CreatedAt = DateTime.UtcNow,
            ArtifactPath = string.Empty, // Learned parameters
            Metrics = new PerformanceMetrics { Sharpe = 0.0, WinRate = 0.0, MaxDrawdown = 0.0 },
            IsPromoted = true,
            PromotedAt = DateTime.UtcNow
        };

        await _modelRegistry.RegisterModelAsync(model, cancellationToken).ConfigureAwait(false);
        await _modelRegistry.PromoteToChampionAsync("PM-Optimizer", model.VersionId, new PromotionRecord
        {
            Id = Guid.NewGuid().ToString(),
            PromotedAt = DateTime.UtcNow,
            Reason = "Bootstrap: Initial champion registration"
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("  ✅ Registered PM-Optimizer champion");
    }
}
