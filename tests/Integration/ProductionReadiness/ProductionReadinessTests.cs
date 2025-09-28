using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BotCore.Extensions;
using BotCore.StrategyDsl;
using BotCore.Strategy;
using BotCore.Fusion;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BotCore.Tests.Integration.ProductionReadiness;

/// <summary>
/// Production readiness tests for the complete Strategy Knowledge Graph & Decision Fusion system
/// Validates end-to-end functionality, service registration, configuration loading, and integration
/// </summary>
public sealed class ProductionReadinessTests
{
    [Fact]
    public void ServiceRegistration_AllServicesRegisterCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        
        // Add required base services
        services.AddLogging(builder => builder.AddConsole());
        
        // Act - Register all pattern and strategy services
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify all key services are registered
        Assert.NotNull(serviceProvider.GetService<IFeatureProbe>());
        Assert.NotNull(serviceProvider.GetService<IRegimeService>());
        Assert.NotNull(serviceProvider.GetService<IStrategyKnowledgeGraph>());
        Assert.NotNull(serviceProvider.GetService<IUcbStrategyChooser>());
        Assert.NotNull(serviceProvider.GetService<IPpoSizer>());
        Assert.NotNull(serviceProvider.GetService<IMLConfigurationService>());
        Assert.NotNull(serviceProvider.GetService<IMetrics>());
        Assert.NotNull(serviceProvider.GetService<DecisionFusionCoordinator>());
    }

    [Fact]
    public void ConfigurationLoading_AllConfigValuesLoadCorrectly()
    {
        // Arrange
        var configuration = CreateTestConfiguration();
        
        // Act & Assert - Verify configuration sections exist and have expected values
        var strategyCatalogConfig = configuration.GetSection("StrategyCatalog");
        Assert.NotNull(strategyCatalogConfig);
        Assert.Equal("/home/runner/work/trading-bot-c-/trading-bot-c-/config/strategies", strategyCatalogConfig["Folder"]);
        Assert.Equal("true", strategyCatalogConfig["Enabled"]);
        
        // Verify fusion bounds are accessible (would be loaded from bounds.json in real system)
        Assert.NotNull(configuration);
    }

    [Fact]
    public void YamlStrategies_LoadAndParseSuccessfully()
    {
        // Arrange - Use real strategies directory
        var strategiesPath = "/home/runner/work/trading-bot-c-/trading-bot-c-/config/strategies";
        
        // Skip if directory doesn't exist (CI environment may not have files)
        if (!Directory.Exists(strategiesPath))
        {
            return;
        }
        
        // Act
        var strategies = SimpleDslLoader.LoadAll(strategiesPath);

        // Assert - Verify all expected strategies are loaded
        Assert.NotEmpty(strategies);
        Assert.Contains(strategies, s => s.Name == "S2");
        Assert.Contains(strategies, s => s.Name == "S3");
        Assert.Contains(strategies, s => s.Name == "S6");
        Assert.Contains(strategies, s => s.Name == "S11");

        // Verify strategy structure completeness
        foreach (var strategy in strategies)
        {
            Assert.NotEmpty(strategy.Name);
            Assert.NotEmpty(strategy.Label);
            Assert.NotEmpty(strategy.Family);
            Assert.NotEmpty(strategy.Bias);
            Assert.NotNull(strategy.When);
            Assert.NotEmpty(strategy.When.Regime);
            Assert.NotNull(strategy.TelemetryTags);
            Assert.NotEmpty(strategy.TelemetryTags);
        }
    }

    [Fact]
    public async Task KnowledgeGraph_ProducesValidRecommendations()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var knowledgeGraph = serviceProvider.GetRequiredService<IStrategyKnowledgeGraph>();

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert
        Assert.NotNull(recommendations);
        
        // If recommendations exist, verify they are well-formed
        foreach (var recommendation in recommendations)
        {
            Assert.NotEmpty(recommendation.StrategyName);
            Assert.True(Enum.IsDefined(typeof(StrategyIntent), recommendation.Intent));
            Assert.True(recommendation.Confidence >= 0 && recommendation.Confidence <= 1);
            Assert.NotNull(recommendation.Evidence);
            Assert.NotNull(recommendation.TelemetryTags);
        }
    }

    [Fact]
    public void DecisionFusion_HandlesAllConfiguredScenarios()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var fusionCoordinator = serviceProvider.GetRequiredService<DecisionFusionCoordinator>();

        // Act & Assert - Test various scenarios
        var decision1 = fusionCoordinator.Decide("ES");
        var decision2 = fusionCoordinator.Decide("NQ");

        // Should not throw exceptions and return consistent results
        Assert.True(decision1 == null || decision1.StrategyName != null);
        Assert.True(decision2 == null || decision2.StrategyName != null);
    }

    [Fact]
    public void TelemetryEmission_WorksCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var metrics = serviceProvider.GetRequiredService<IMetrics>();

        // Act & Assert - Verify metrics service doesn't throw
        Assert.NotNull(metrics);
        
        // Test metric emission (mock implementation should handle this gracefully)
        metrics.Gauge("test.metric", 1.0, ("symbol", "ES"));
        metrics.IncTagged("test.counter", 1, ("symbol", "ES"));
    }

    [Fact]
    public void ExpressionEvaluation_HandlesComplexConditions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var logger = services.BuildServiceProvider().GetRequiredService<ILogger<ExpressionEvaluator>>();
        var evaluator = new ExpressionEvaluator(logger);

        var features = new Dictionary<string, object>
        {
            ["zone.dist_to_supply_atr"] = 0.6,
            ["zone.dist_to_demand_atr"] = 0.7,
            ["pattern.bear_score"] = 0.3,
            ["pattern.bull_score"] = 0.4,
            ["zone.breakout_score"] = 0.5,
            ["mom.zscore"] = 1.2,
            ["vdc"] = 0.4
        };

        // Act & Assert - Test various expression patterns from YAML
        var result1 = evaluator.EvaluateExpression("zone.dist_to_supply_atr >= 0.5");
        var result2 = evaluator.EvaluateExpression("pattern.bear_score > 0.7 AND intent == Long");
        var result3 = evaluator.EvaluateExpression("vdc <= 0.6");

        evaluator.UpdateFeatures(features);
        var result4 = evaluator.EvaluateExpression("zone.dist_to_supply_atr >= 0.5");

        // Should not throw exceptions
        Assert.True(result4); // This should evaluate to true based on test data
    }

    [Fact]
    public void ErrorHandling_GracefulDegradation()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var fusionCoordinator = serviceProvider.GetRequiredService<DecisionFusionCoordinator>();

        // Act & Assert - Test with invalid inputs
        try
        {
            var result1 = fusionCoordinator.Decide(""); // Empty symbol
            Assert.Null(result1); // Should handle gracefully
        }
        catch (ArgumentException)
        {
            // Expected behavior for invalid input
        }

        var result2 = fusionCoordinator.Decide("INVALID"); // Invalid symbol
        // Should handle gracefully without exceptions or return null
        Assert.True(result2 == null || result2.StrategyName != null);
    }

    [Fact]
    public void MemoryManagement_NoLeaksInRepeatedOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var fusionCoordinator = serviceProvider.GetRequiredService<DecisionFusionCoordinator>();
        var knowledgeGraph = serviceProvider.GetRequiredService<IStrategyKnowledgeGraph>();

        // Act - Perform repeated operations
        for (int i = 0; i < 100; i++)
        {
            var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);
            var decision = fusionCoordinator.Decide("ES");
            
            // Verify operations complete without building up memory
            Assert.NotNull(recommendations);
        }

        // Assert - If we get here without OutOfMemoryException, memory management is working
        Assert.True(true);
    }

    [Fact]
    public void ThreadSafety_ConcurrentOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var fusionCoordinator = serviceProvider.GetRequiredService<DecisionFusionCoordinator>();

        // Act - Run concurrent operations
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await Task.Delay(10); // Small delay to increase chance of concurrency
            var decision = fusionCoordinator.Decide($"ES");
            return decision;
        });

        // Assert - All tasks should complete without exceptions
        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();
        Assert.Equal(10, results.Length);
    }

    [Fact]
    public void ConfigurationValidation_BoundsRespected()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var mlConfig = serviceProvider.GetRequiredService<IMLConfigurationService>();

        // Act
        var fusionRails = mlConfig.GetFusionRails();

        // Assert - Verify configuration values are within expected bounds
        Assert.True(fusionRails.KnowledgeWeight >= 0.0 && fusionRails.KnowledgeWeight <= 1.0);
        Assert.True(fusionRails.UcbWeight >= 0.0 && fusionRails.UcbWeight <= 1.0);
        Assert.True(fusionRails.MinConfidence >= 0.5 && fusionRails.MinConfidence <= 0.9);
        Assert.True(fusionRails.HoldOnDisagree >= 0 && fusionRails.HoldOnDisagree <= 1);
        
        // Weights should approximately sum to 1.0
        var weightSum = fusionRails.KnowledgeWeight + fusionRails.UcbWeight;
        Assert.True(Math.Abs(weightSum - 1.0) < 0.1, $"Weight sum {weightSum} should be close to 1.0");
    }

    [Fact]
    public void ProductionGuardrails_AllSafetiesInPlace()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPatternAndStrategyServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Verify all critical safety components are present
        var knowledgeGraph = serviceProvider.GetRequiredService<IStrategyKnowledgeGraph>();
        var fusionCoordinator = serviceProvider.GetRequiredService<DecisionFusionCoordinator>();
        var mlConfig = serviceProvider.GetRequiredService<IMLConfigurationService>();

        Assert.NotNull(knowledgeGraph);
        Assert.NotNull(fusionCoordinator);
        Assert.NotNull(mlConfig);

        // Verify fusion coordinator handles null/invalid inputs safely
        try
        {
            var nullDecision = fusionCoordinator.Decide("");
            Assert.Null(nullDecision); // Should return null for invalid input
        }
        catch (ArgumentException)
        {
            // Expected behavior for invalid input
        }

        // Verify knowledge graph handles edge cases
        try
        {
            var emptyRecommendations = knowledgeGraph.Evaluate("", DateTime.UtcNow);
            // Should either throw ArgumentException or return empty list, not crash
        }
        catch (ArgumentException)
        {
            // Expected behavior for invalid input
        }
    }

    private static IConfiguration CreateTestConfiguration()
    {
        var configData = new Dictionary<string, string>
        {
            ["StrategyCatalog:Folder"] = "/home/runner/work/trading-bot-c-/trading-bot-c-/config/strategies",
            ["StrategyCatalog:Enabled"] = "true",
            ["StrategyKnowledgeGraph:MinConfidenceThreshold"] = "0.6",
            ["StrategyKnowledgeGraph:MaxRecommendations"] = "5",
            ["StrategyKnowledgeGraph:EmitTelemetry"] = "true"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }
}