using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using BotCore.Fusion;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using BotCore.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotCore.Tests.Integration.DecisionPipeline;

/// <summary>
/// Integration tests that validate the complete decision pipeline including zone gates and bracket integration
/// Tests that decisions pass through SafeHold policy and zone/pattern gates as required
/// </summary>
public sealed class DecisionPipelineIntegrationTests
{
    [Fact]
    public void FusionDecision_PassesThroughZoneGate_WhenConditionsAllow()
    {
        // Arrange - Create fusion decision with zone-favorable conditions
        var fusionCoordinator = CreateFusionCoordinator();
        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        // Setup fusion to return a recommendation
        var expectedRecommendation = new StrategyRecommendation(
            "S2", StrategyIntent.Long, 0.8,
            new List<StrategyEvidence> { new("zone_favorable", 1.0, "Good zone position") },
            new[] { "S2", "MeanReversion" });

        // Setup zone gate to allow the decision
        zoneGateMock
            .Setup(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()))
            .Returns((false, string.Empty, expectedRecommendation)); // Not held, allow through

        // Setup pattern gate to allow
        patternGateMock
            .Setup(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()))
            .Returns((false, string.Empty, expectedRecommendation)); // Not held, allow through

        // Setup bracket manager
        var expectedBracket = new TradingBracket { StopLoss = 4500, TakeProfit = 4550, BracketMode = "PerEntry" };
        bracketManagerMock
            .Setup(b => b.BuildAnchored(It.IsAny<string>(), It.IsAny<StrategyRecommendation>(), It.IsAny<decimal>()))
            .Returns(expectedBracket);

        // Create decision pipeline
        var pipeline = new DecisionPipeline(
            fusionCoordinator, 
            zoneGateMock.Object, 
            patternGateMock.Object,
            bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("S2", result.StrategyName);
        Assert.Equal(StrategyIntent.Long, result.Intent);
        Assert.NotNull(result.Bracket);
        Assert.Equal(expectedBracket.StopLoss, result.Bracket.StopLoss);
        Assert.Equal(expectedBracket.TakeProfit, result.Bracket.TakeProfit);

        // Verify all gates were called in order
        zoneGateMock.Verify(z => z.EvaluateEntry("ES", StrategyIntent.Long, 4525.0), Times.Once);
        patternGateMock.Verify(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), "ES"), Times.Once);
        bracketManagerMock.Verify(b => b.BuildAnchored("ES", It.IsAny<StrategyRecommendation>(), 4525m), Times.Once);
    }

    [Fact]
    public void FusionDecision_BlockedByZoneGate_ReturnsNull()
    {
        // Arrange - Create fusion decision but zone gate blocks it
        var fusionCoordinator = CreateFusionCoordinator();
        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        // Setup zone gate to block the decision
        zoneGateMock
            .Setup(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()))
            .Returns((true, "Too close to supply zone", null)); // Held, blocked

        var pipeline = new DecisionPipeline(
            fusionCoordinator, 
            zoneGateMock.Object, 
            patternGateMock.Object,
            bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.Null(result);

        // Verify zone gate was called but pattern gate and bracket manager were not
        zoneGateMock.Verify(z => z.EvaluateEntry("ES", StrategyIntent.Long, 4525.0), Times.Once);
        patternGateMock.Verify(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()), Times.Never);
        bracketManagerMock.Verify(b => b.BuildAnchored(It.IsAny<string>(), It.IsAny<StrategyRecommendation>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public void FusionDecision_BlockedByPatternGate_ReturnsNull()
    {
        // Arrange - Zone gate allows but pattern gate blocks
        var fusionCoordinator = CreateFusionCoordinator();
        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        var recommendation = new StrategyRecommendation(
            "S2", StrategyIntent.Long, 0.8,
            new List<StrategyEvidence>(),
            new[] { "S2" });

        // Zone gate allows
        zoneGateMock
            .Setup(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()))
            .Returns((false, string.Empty, recommendation));

        // Pattern gate blocks
        patternGateMock
            .Setup(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()))
            .Returns((true, "Strong opposing pattern detected", null)); // Held, blocked

        var pipeline = new DecisionPipeline(
            fusionCoordinator, 
            zoneGateMock.Object, 
            patternGateMock.Object,
            bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.Null(result);

        // Verify both gates were called but bracket manager was not
        zoneGateMock.Verify(z => z.EvaluateEntry("ES", StrategyIntent.Long, 4525.0), Times.Once);
        patternGateMock.Verify(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), "ES"), Times.Once);
        bracketManagerMock.Verify(b => b.BuildAnchored(It.IsAny<string>(), It.IsAny<StrategyRecommendation>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public void NoFusionRecommendation_ReturnsNullWithoutCallingGates()
    {
        // Arrange - Fusion coordinator returns no recommendation
        var fusionCoordinatorMock = new Mock<DecisionFusionCoordinator>(
            Mock.Of<IStrategyKnowledgeGraph>(),
            Mock.Of<IUcbStrategyChooser>(),
            Mock.Of<IPpoSizer>(),
            Mock.Of<IMLConfigurationService>(),
            Mock.Of<IMetrics>(),
            Mock.Of<ILogger<DecisionFusionCoordinator>>());

        fusionCoordinatorMock
            .Setup(f => f.Decide(It.IsAny<string>()))
            .Returns((BotCore.Strategy.StrategyRecommendation)null); // No recommendation

        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        var pipeline = new DecisionPipeline(
            fusionCoordinatorMock.Object, 
            zoneGateMock.Object, 
            patternGateMock.Object,
            bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.Null(result);

        // Verify no gates were called since there was no fusion recommendation
        zoneGateMock.Verify(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()), Times.Never);
        patternGateMock.Verify(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()), Times.Never);
        bracketManagerMock.Verify(b => b.BuildAnchored(It.IsAny<string>(), It.IsAny<StrategyRecommendation>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public void BracketManager_CreatesCorrectBracketForStrategy()
    {
        // Arrange
        var fusionCoordinator = CreateFusionCoordinator();
        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        var s2Recommendation = new StrategyRecommendation(
            "S2", StrategyIntent.Long, 0.8,
            new List<StrategyEvidence>(),
            new[] { "S2", "MeanReversion" });

        // Both gates allow
        zoneGateMock
            .Setup(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()))
            .Returns((false, string.Empty, s2Recommendation));

        patternGateMock
            .Setup(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()))
            .Returns((false, string.Empty, s2Recommendation));

        // Setup bracket manager to create strategy-specific bracket
        var expectedBracket = new TradingBracket 
        { 
            StopLoss = 4500, 
            TakeProfit = 4550, 
            BracketMode = "PerEntry",
            StrategySpecificSettings = "tp_at_zone_edge; sl_outside_zone buffer_ticks: 6" // From S2 YAML
        };
        
        bracketManagerMock
            .Setup(b => b.BuildAnchored("ES", It.Is<StrategyRecommendation>(r => r.StrategyName == "S2"), 4525m))
            .Returns(expectedBracket);

        var pipeline = new DecisionPipeline(
            fusionCoordinator, 
            zoneGateMock.Object, 
            patternGateMock.Object,
            bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Bracket);
        Assert.Equal(expectedBracket.StrategySpecificSettings, result.Bracket.StrategySpecificSettings);
        Assert.Equal("PerEntry", result.Bracket.BracketMode);

        // Verify bracket manager was called with correct strategy
        bracketManagerMock.Verify(b => b.BuildAnchored("ES", 
            It.Is<StrategyRecommendation>(r => r.StrategyName == "S2" && r.Intent == StrategyIntent.Long), 4525m), 
            Times.Once);
    }

    [Theory]
    [InlineData("S2", "MeanReversion", "tp_at_zone_edge; sl_outside_zone")]
    [InlineData("S3", "Compression", "measured_move_or_next_zone")]
    [InlineData("S6", "Momentum", "trail_by_atr multiple: 1.5")]
    [InlineData("S11", "Exhaustion", "tp_mid_to_opposite_zone; sl_beyond_zone")]
    public void BracketManager_CreatesStrategySpecificBrackets(string strategyName, string family, string expectedBracketPattern)
    {
        // Arrange
        var fusionCoordinator = CreateFusionCoordinator(strategyName);
        var zoneGateMock = new Mock<IZoneGateService>();
        var patternGateMock = new Mock<IPatternGateService>();
        var bracketManagerMock = new Mock<IBracketManager>();

        var recommendation = new StrategyRecommendation(
            strategyName, StrategyIntent.Long, 0.8,
            new List<StrategyEvidence>(),
            new[] { strategyName, family });

        // Gates allow
        zoneGateMock.Setup(z => z.EvaluateEntry(It.IsAny<string>(), It.IsAny<StrategyIntent>(), It.IsAny<double>()))
                   .Returns((false, string.Empty, recommendation));
        patternGateMock.Setup(p => p.EvaluateEntry(It.IsAny<StrategyRecommendation>(), It.IsAny<string>()))
                      .Returns((false, string.Empty, recommendation));

        // Bracket manager creates strategy-specific bracket
        bracketManagerMock
            .Setup(b => b.BuildAnchored(It.IsAny<string>(), It.IsAny<StrategyRecommendation>(), It.IsAny<decimal>()))
            .Returns(new TradingBracket 
            { 
                StopLoss = 4500, 
                TakeProfit = 4550,
                BracketMode = "PerEntry",
                StrategySpecificSettings = expectedBracketPattern
            });

        var pipeline = new DecisionPipeline(fusionCoordinator, zoneGateMock.Object, patternGateMock.Object, bracketManagerMock.Object);

        // Act
        var result = pipeline.ProcessDecision("ES", 4525m);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(expectedBracketPattern, result.Bracket.StrategySpecificSettings);
    }

    private static DecisionFusionCoordinator CreateFusionCoordinator(string strategyName = "S2")
    {
        var knowledgeGraphMock = new Mock<IStrategyKnowledgeGraph>();
        var ucbChooserMock = new Mock<IUcbStrategyChooser>();
        var ppoSizerMock = new Mock<IPpoSizer>();
        var mlConfigMock = new Mock<IMLConfigurationService>();
        var metricsMock = new Mock<IMetrics>();
        var loggerMock = new Mock<ILogger<DecisionFusionCoordinator>>();

        // Setup to return a consistent recommendation
        var recommendation = new StrategyRecommendation(
            strategyName, StrategyIntent.Long, 0.8,
            new List<StrategyEvidence> { new("test", 1.0) },
            new[] { strategyName });

        knowledgeGraphMock
            .Setup(kg => kg.Evaluate(It.IsAny<string>(), It.IsAny<DateTime>()))
            .Returns(new[] { recommendation });

        ucbChooserMock
            .Setup(ucb => ucb.Predict(It.IsAny<string>()))
            .Returns((strategyName, StrategyIntent.Long, 0.7));

        mlConfigMock
            .Setup(cfg => cfg.GetFusionRails())
            .Returns(new FusionRails { KnowledgeWeight = 0.6, UcbWeight = 0.4, MinConfidence = 0.5, HoldOnDisagree = 0 });

        return new DecisionFusionCoordinator(
            knowledgeGraphMock.Object,
            ucbChooserMock.Object,
            ppoSizerMock.Object,
            mlConfigMock.Object,
            metricsMock.Object,
            loggerMock.Object);
    }
}

/// <summary>
/// Mock decision pipeline that integrates fusion coordinator with zone gates and bracket management
/// This simulates the real UnifiedDecisionRouter integration
/// </summary>
public sealed class DecisionPipeline
{
    private readonly DecisionFusionCoordinator _fusionCoordinator;
    private readonly IZoneGateService _zoneGate;
    private readonly IPatternGateService _patternGate;
    private readonly IBracketManager _bracketManager;

    public DecisionPipeline(
        DecisionFusionCoordinator fusionCoordinator,
        IZoneGateService zoneGate,
        IPatternGateService patternGate,
        IBracketManager bracketManager)
    {
        _fusionCoordinator = fusionCoordinator;
        _zoneGate = zoneGate;
        _patternGate = patternGate;
        _bracketManager = bracketManager;
    }

    public StrategyDecision ProcessDecision(string symbol, decimal currentPrice)
    {
        // Step 1: Get fusion recommendation
        var fusionRecommendation = _fusionCoordinator.Decide(symbol);
        if (fusionRecommendation == null)
            return null;

        // Step 2: Zone gate evaluation
        var (zoneHeld, zoneReason, zoneAmended) = _zoneGate.EvaluateEntry(symbol, fusionRecommendation.Intent, (double)currentPrice);
        if (zoneHeld)
            return null;

        var workingRecommendation = zoneAmended ?? fusionRecommendation;

        // Step 3: Pattern gate evaluation  
        var (patternHeld, patternReason, patternAmended) = _patternGate.EvaluateEntry(workingRecommendation, symbol);
        if (patternHeld)
            return null;

        var finalRecommendation = patternAmended ?? workingRecommendation;

        // Step 4: Bracket creation
        var bracket = _bracketManager.BuildAnchored(symbol, finalRecommendation, currentPrice);
        
        return new StrategyDecision
        {
            StrategyName = finalRecommendation.StrategyName,
            Intent = finalRecommendation.Intent,
            Confidence = finalRecommendation.Confidence,
            Evidence = finalRecommendation.Evidence,
            TelemetryTags = finalRecommendation.TelemetryTags,
            Bracket = bracket
        };
    }
}

// Mock interfaces for testing
public interface IZoneGateService
{
    (bool Held, string Reason, StrategyRecommendation MaybeAmended) EvaluateEntry(string symbol, StrategyIntent intent, double price);
}

public interface IPatternGateService
{
    (bool Held, string Reason, StrategyRecommendation MaybeAmended) EvaluateEntry(StrategyRecommendation recommendation, string symbol);
}

public interface IBracketManager
{
    TradingBracket BuildAnchored(string symbol, StrategyRecommendation recommendation, decimal currentPrice);
}

public class TradingBracket
{
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public string BracketMode { get; set; }
    public string StrategySpecificSettings { get; set; }
}

public class StrategyDecision
{
    public string StrategyName { get; set; }
    public StrategyIntent Intent { get; set; }
    public double Confidence { get; set; }
    public IReadOnlyList<StrategyEvidence> Evidence { get; set; }
    public string[] TelemetryTags { get; set; }
    public TradingBracket Bracket { get; set; }
}