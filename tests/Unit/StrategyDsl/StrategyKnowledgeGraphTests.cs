using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using BotCore.StrategyDsl;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

namespace BotCore.Tests.StrategyDsl;

/// <summary>
/// Unit tests for StrategyKnowledgeGraph - comprehensive testing of strategy evaluation and recommendation generation
/// </summary>
public sealed class StrategyKnowledgeGraphTests
{
    private readonly Mock<ILogger<StrategyKnowledgeGraph>> _loggerMock;
    private readonly Mock<FeatureProbe> _featureProbeMock;
    private readonly Mock<DslLoader> _dslLoaderMock;
    private readonly Mock<ExpressionEvaluator> _expressionEvaluatorMock;
    private readonly Mock<IOptionsMonitor<StrategyKnowledgeGraphOptions>> _optionsMock;
    private readonly StrategyKnowledgeGraph _knowledgeGraph;
    private readonly StrategyKnowledgeGraphOptions _defaultOptions;

    public StrategyKnowledgeGraphTests()
    {
        _loggerMock = new Mock<ILogger<StrategyKnowledgeGraph>>();
        _featureProbeMock = new Mock<FeatureProbe>(
            Mock.Of<ILogger<FeatureProbe>>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<PatternEngine>(),
            Mock.Of<FeatureBusMapper>());
        _dslLoaderMock = new Mock<DslLoader>(
            Mock.Of<ILogger<DslLoader>>(),
            Mock.Of<IOptionsMonitor<DslLoaderOptions>>());
        _expressionEvaluatorMock = new Mock<ExpressionEvaluator>(Mock.Of<ILogger<ExpressionEvaluator>>());
        _optionsMock = new Mock<IOptionsMonitor<StrategyKnowledgeGraphOptions>>();

        _defaultOptions = new StrategyKnowledgeGraphOptions
        {
            MinConfidenceThreshold = 0.6,
            MaxRecommendations = 5,
            EnabledStrategies = new List<string> { "S2_Zone_Mean_Reversion", "S3_Compression_Breakout", "S6_Trend_Following", "S11_Failed_Breakout_Reversal" },
            EmitTelemetry = true
        };

        _optionsMock.Setup(x => x.CurrentValue).Returns(_defaultOptions);

        _knowledgeGraph = new StrategyKnowledgeGraph(
            _loggerMock.Object,
            _featureProbeMock.Object,
            _dslLoaderMock.Object,
            _expressionEvaluatorMock.Object,
            _optionsMock.Object);
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_ValidScenario_ReturnsRankedRecommendations()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        // Setup expression evaluator to return successful results
        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(new ExpressionResult { IsSuccess = true, Value = true });

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.NotEmpty(recommendations);
        Assert.All(recommendations, r => Assert.True(r.Confidence >= _defaultOptions.MinConfidenceThreshold));
        Assert.True(recommendations.Count <= _defaultOptions.MaxRecommendations);
        
        // Verify recommendations are ordered by confidence descending
        for (int i = 0; i < recommendations.Count - 1; i++)
        {
            Assert.True(recommendations[i].Confidence >= recommendations[i + 1].Confidence);
        }
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_NoStrategiesAvailable_ReturnsEmpty()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DslStrategy>());

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_AllStrategiesFiltered_ReturnsEmpty()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        // Setup expression evaluator to fail all regime conditions
        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(new ExpressionResult { IsSuccess = true, Value = false });

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_LowConfidenceStrategy_IsFiltered()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        // Setup expression evaluator to return low confluence (only 1 out of 3 conditions pass)
        var callCount = 0;
        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First call (regime) passes, but only 1 out of 3 micro conditions pass
                return new ExpressionResult { IsSuccess = true, Value = callCount == 1 || callCount == 2 };
            });

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        // Should be empty because low confluence leads to low confidence which gets filtered
        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_ContraIndicationMet_BlocksStrategy()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategiesWithContraIndications();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        // Setup expression evaluator: regime and micro conditions pass, but contraindication is met
        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(new ExpressionResult { IsSuccess = true, Value = true });

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations); // Blocked by contraindications
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_DisabledStrategy_IsSkipped()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        // Disable all strategies
        var options = new StrategyKnowledgeGraphOptions
        {
            MinConfidenceThreshold = 0.6,
            MaxRecommendations = 5,
            EnabledStrategies = new List<string>(), // Empty enabled list
            EmitTelemetry = true
        };
        _optionsMock.Setup(x => x.CurrentValue).Returns(options);

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
        
        // Expression evaluator should not be called for disabled strategies
        _expressionEvaluatorMock.Verify(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EvaluateStrategiesAsync_InvalidSymbol_ThrowsArgumentException(string invalidSymbol)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _knowledgeGraph.EvaluateStrategiesAsync(invalidSymbol));
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        const string symbol = "ES";
        var cancelledToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _knowledgeGraph.EvaluateStrategiesAsync(symbol, cancelledToken));
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_ExpressionEvaluatorThrows_HandlesGracefully()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ThrowsAsync(new InvalidOperationException("Expression evaluation failed"));

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations); // All strategies should fail gracefully
    }

    [Fact]
    public async Task EvaluateStrategiesAsync_ValidRecommendation_HasCompleteMetadata()
    {
        // Arrange
        const string symbol = "ES";
        var featureSnapshot = CreateMockFeatureSnapshot(symbol);
        var strategies = CreateMockStrategies();

        _featureProbeMock
            .Setup(x => x.ProbeCurrentStateAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(featureSnapshot);

        _dslLoaderMock
            .Setup(x => x.GetStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategies);

        _expressionEvaluatorMock
            .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(new ExpressionResult { IsSuccess = true, Value = true });

        // Act
        var recommendations = await _knowledgeGraph.EvaluateStrategiesAsync(symbol);

        // Assert
        Assert.NotEmpty(recommendations);
        var recommendation = recommendations.First();
        
        Assert.NotEmpty(recommendation.StrategyName);
        Assert.NotEmpty(recommendation.Intent);
        Assert.True(recommendation.Confidence > 0);
        Assert.NotEmpty(recommendation.Evidence);
        Assert.NotNull(recommendation.TelemetryTags);
        Assert.True(recommendation.Timestamp != default);
        Assert.Equal(symbol, recommendation.Symbol);
        Assert.True(recommendation.ConfluenceCount >= 0);
        Assert.NotEmpty(recommendation.Playbook);
    }

    private static FeatureSnapshot CreateMockFeatureSnapshot(string symbol)
    {
        return new FeatureSnapshot
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            Features = new Dictionary<string, object>
            {
                ["zone.distance_atr"] = 0.5,
                ["zone.breakout_score"] = 0.7,
                ["pattern.bull_score"] = 0.6,
                ["pattern.bear_score"] = 0.4,
                ["market_regime"] = "trending",
                ["volatility_z_score"] = 0.2,
                ["trend.strength"] = 0.8,
                ["momentum.z_score"] = 1.5,
                ["vwap_distance"] = 0.02,
                ["time_of_day"] = 14.5,
                ["session_volume"] = 1500000
            }
        };
    }

    private static List<DslStrategy> CreateMockStrategies()
    {
        return new List<DslStrategy>
        {
            new()
            {
                Name = "S2_Zone_Mean_Reversion",
                Intent = "mean_reversion",
                TelemetryTags = new[] { "S2", "MeanReversion" },
                When = new DslWhen
                {
                    Regime = new[] { "market_regime == 'ranging'" },
                    MicroConditions = new[] 
                    { 
                        "zone.distance_atr <= 0.5",
                        "pattern.bull_score >= 0.6",
                        "momentum.z_score <= 1.0"
                    }
                },
                Playbook = new DslPlaybook { Name = "Zone Mean Reversion" }
            },
            new()
            {
                Name = "S3_Compression_Breakout",
                Intent = "breakout",
                TelemetryTags = new[] { "S3", "Compression" },
                When = new DslWhen
                {
                    Regime = new[] { "volatility_z_score <= 0.5" },
                    MicroConditions = new[]
                    {
                        "pattern.bull_score >= 0.7",
                        "zone.breakout_score >= 0.6",
                        "trend.strength >= 0.5"
                    }
                },
                Playbook = new DslPlaybook { Name = "Compression Breakout" }
            }
        };
    }

    private static List<DslStrategy> CreateMockStrategiesWithContraIndications()
    {
        return new List<DslStrategy>
        {
            new()
            {
                Name = "S2_Zone_Mean_Reversion",  
                Intent = "mean_reversion",
                TelemetryTags = new[] { "S2", "MeanReversion" },
                When = new DslWhen
                {
                    Regime = new[] { "market_regime == 'ranging'" },
                    MicroConditions = new[]
                    {
                        "zone.distance_atr <= 0.5",
                        "pattern.bull_score >= 0.6"
                    },
                    ContraIndications = new[]
                    {
                        "volatility_z_score >= 2.0" // This will be met, blocking the strategy
                    }
                },
                Playbook = new DslPlaybook { Name = "Zone Mean Reversion" }
            }
        };
    }
}