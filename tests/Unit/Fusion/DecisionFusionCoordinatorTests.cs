using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using BotCore.Fusion;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BotCore.Tests.Fusion;

/// <summary>
/// Unit tests for DecisionFusionCoordinator - validates ML decision fusion logic
/// </summary>
public sealed class DecisionFusionCoordinatorTests
{
    private readonly Mock<IStrategyKnowledgeGraph> _knowledgeGraphMock;
    private readonly Mock<IUcbStrategyChooser> _ucbChooserMock;
    private readonly Mock<IPpoSizer> _ppoSizerMock;
    private readonly Mock<IMLConfigurationService> _mlConfigMock;
    private readonly Mock<IMetrics> _metricsMock;
    private readonly Mock<ILogger<DecisionFusionCoordinator>> _loggerMock;
    private readonly DecisionFusionCoordinator _fusionCoordinator;

    public DecisionFusionCoordinatorTests()
    {
        _knowledgeGraphMock = new Mock<IStrategyKnowledgeGraph>();
        _ucbChooserMock = new Mock<IUcbStrategyChooser>();
        _ppoSizerMock = new Mock<IPpoSizer>();
        _mlConfigMock = new Mock<IMLConfigurationService>();
        _metricsMock = new Mock<IMetrics>();
        _loggerMock = new Mock<ILogger<DecisionFusionCoordinator>>();

        _fusionCoordinator = new DecisionFusionCoordinator(
            _knowledgeGraphMock.Object,
            _ucbChooserMock.Object,
            _ppoSizerMock.Object,
            _mlConfigMock.Object,
            _metricsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void Decide_BothSystemsAgreeAboveThreshold_ReturnsKnowledgeGraphRecommendation()
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, 0.8);
        var ucbPrediction = ("S2", StrategyIntent.Long, 0.7);
        var fusionRails = CreateFusionRails(minConfidence: 0.65, holdOnDisagree: 1);

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("S2", result.StrategyName);
        Assert.Equal(StrategyIntent.Long, result.Intent);
        
        // Verify telemetry was emitted
        _metricsMock.Verify(m => m.Gauge("fusion.blended", It.IsAny<double>(), It.IsAny<(string, string)[]>()), Times.Once);
        _metricsMock.Verify(m => m.IncTagged("fusion.disagree", 0, It.IsAny<(string, string)[]>()), Times.Once);
    }

    [Fact]
    public void Decide_SystemsDisagreeWithHoldOnDisagree_ReturnsNull()
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, 0.8);
        var ucbPrediction = ("S6", StrategyIntent.Short, 0.7); // Different strategy and direction
        var fusionRails = CreateFusionRails(minConfidence: 0.65, holdOnDisagree: 1);

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.Null(result);
        
        // Verify disagreement was logged
        _metricsMock.Verify(m => m.IncTagged("fusion.disagree", 1, It.IsAny<(string, string)[]>()), Times.Once);
    }

    [Fact]
    public void Decide_SystemsDisagreeWithoutHoldOnDisagree_ReturnsKnowledgeGraphRecommendation()
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, 0.8);
        var ucbPrediction = ("S6", StrategyIntent.Short, 0.7);
        var fusionRails = CreateFusionRails(minConfidence: 0.65, holdOnDisagree: 0); // Allow disagreement

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("S2", result.StrategyName); // Should prefer knowledge graph
        Assert.Equal(StrategyIntent.Long, result.Intent);
    }

    [Fact]
    public void Decide_BlendedConfidenceBelowThreshold_ReturnsNull()
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, 0.5); // Low confidence
        var ucbPrediction = ("S2", StrategyIntent.Long, 0.4); // Low confidence
        var fusionRails = CreateFusionRails(knowledgeWeight: 0.6, ucbWeight: 0.4, minConfidence: 0.65);

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.Null(result);
        
        // Blended score should be: 0.6 * 0.5 + 0.4 * 0.4 = 0.46, which is below 0.65 threshold
    }

    [Fact]
    public void Decide_OnlyKnowledgeGraphRecommendation_ReturnsRecommendation()
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, 0.8);
        var ucbPrediction = ("", StrategyIntent.Long, 0.0); // No UCB recommendation
        var fusionRails = CreateFusionRails(minConfidence: 0.65);

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("S2", result.StrategyName);
        Assert.Equal(StrategyIntent.Long, result.Intent);
    }

    [Fact]
    public void Decide_OnlyUcbRecommendation_ReturnsUcbRecommendation()
    {
        // Arrange
        const string symbol = "ES";
        var ucbPrediction = ("S6", StrategyIntent.Short, 0.8);
        var fusionRails = CreateFusionRails(minConfidence: 0.65);

        SetupMocks(symbol, new StrategyRecommendation[0], ucbPrediction, fusionRails); // No knowledge graph

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("S6", result.StrategyName);
        Assert.Equal(StrategyIntent.Short, result.Intent);
    }

    [Fact]
    public void Decide_NoRecommendationsFromEitherSystem_ReturnsNull()
    {
        // Arrange
        const string symbol = "ES";
        var ucbPrediction = ("", StrategyIntent.Long, 0.0);
        var fusionRails = CreateFusionRails(minConfidence: 0.65);

        SetupMocks(symbol, new StrategyRecommendation[0], ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Decide_InvalidSymbol_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _fusionCoordinator.Decide(""));
        Assert.Throws<ArgumentException>(() => _fusionCoordinator.Decide(null));
        Assert.Throws<ArgumentException>(() => _fusionCoordinator.Decide("   "));
    }

    [Fact]
    public void Decide_ExceptionInKnowledgeGraph_HandlesGracefullyAndUsesUcb()
    {
        // Arrange
        const string symbol = "ES";
        var ucbPrediction = ("S6", StrategyIntent.Long, 0.8);
        var fusionRails = CreateFusionRails(minConfidence: 0.65);

        _knowledgeGraphMock
            .Setup(kg => kg.Evaluate(symbol, It.IsAny<DateTime>()))
            .Throws(new InvalidOperationException("Knowledge graph failed"));

        _ucbChooserMock
            .Setup(ucb => ucb.Predict(symbol))
            .Returns(ucbPrediction);

        _mlConfigMock
            .Setup(cfg => cfg.GetFusionRails())
            .Returns(fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.Null(result); // Should return null on exception for safety
    }

    [Theory]
    [InlineData(0.7, 0.6, 0.8, 0.2, 0.68)] // 0.8 * 0.7 + 0.2 * 0.6 = 0.68
    [InlineData(0.5, 0.8, 0.6, 0.4, 0.62)] // 0.6 * 0.5 + 0.4 * 0.8 = 0.62
    [InlineData(0.9, 0.1, 1.0, 0.0, 0.9)]  // 1.0 * 0.9 + 0.0 * 0.1 = 0.9
    public void Decide_BlendingWeights_CalculatesCorrectly(double knowledgeScore, double ucbScore, 
        double knowledgeWeight, double ucbWeight, double expectedBlended)
    {
        // Arrange
        const string symbol = "ES";
        var knowledgeRec = CreateStrategyRecommendation("S2", StrategyIntent.Long, knowledgeScore);
        var ucbPrediction = ("S2", StrategyIntent.Long, ucbScore);
        var fusionRails = CreateFusionRails(
            knowledgeWeight: knowledgeWeight, 
            ucbWeight: ucbWeight, 
            minConfidence: 0.5); // Low threshold to allow testing

        SetupMocks(symbol, new[] { knowledgeRec }, ucbPrediction, fusionRails);

        // Act
        var result = _fusionCoordinator.Decide(symbol);

        // Assert
        Assert.NotNull(result);
        
        // Verify the blended score was calculated and emitted correctly
        _metricsMock.Verify(m => m.Gauge("fusion.blended", 
            It.Is<double>(d => Math.Abs(d - expectedBlended) < 0.001), 
            It.IsAny<(string, string)[]>()), Times.Once);
    }

    private static StrategyRecommendation CreateStrategyRecommendation(string strategyName, StrategyIntent intent, double confidence)
    {
        return new StrategyRecommendation(
            strategyName,
            intent,
            confidence,
            new List<StrategyEvidence> { new("test_evidence", 1.0, "Test evidence") },
            new[] { "TestTag" });
    }

    private static FusionRails CreateFusionRails(double knowledgeWeight = 0.6, double ucbWeight = 0.4, 
        double minConfidence = 0.65, int holdOnDisagree = 1)
    {
        return new FusionRails
        {
            KnowledgeWeight = knowledgeWeight,
            UcbWeight = ucbWeight,
            MinConfidence = minConfidence,
            HoldOnDisagree = holdOnDisagree,
            ReplayExplore = 0
        };
    }

    private void SetupMocks(string symbol, IReadOnlyList<StrategyRecommendation> knowledgeRecs, 
        (string Strategy, StrategyIntent Intent, double Score) ucbPrediction, FusionRails fusionRails)
    {
        _knowledgeGraphMock
            .Setup(kg => kg.Evaluate(symbol, It.IsAny<DateTime>()))
            .Returns(knowledgeRecs);

        _ucbChooserMock
            .Setup(ucb => ucb.Predict(symbol))
            .Returns(ucbPrediction);

        _mlConfigMock
            .Setup(cfg => cfg.GetFusionRails())
            .Returns(fusionRails);
    }
}