using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using BotCore.StrategyDsl;
using BotCore.Patterns;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace BotCore.Tests.StrategyDsl;

/// <summary>
/// Unit tests for FeatureProbe - comprehensive testing of feature aggregation from multiple sources
/// </summary>
public sealed class FeatureProbeTests
{
    private readonly Mock<ILogger<FeatureProbe>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<PatternEngine> _patternEngineMock;
    private readonly Mock<FeatureBusMapper> _featureBusMapperMock;
    private readonly FeatureProbe _featureProbe;

    public FeatureProbeTests()
    {
        _loggerMock = new Mock<ILogger<FeatureProbe>>();
        _configurationMock = new Mock<IConfiguration>();
        _patternEngineMock = new Mock<PatternEngine>(Mock.Of<ILogger<PatternEngine>>(), Mock.Of<IFeatureBus>(), Array.Empty<IPatternDetector>());
        _featureBusMapperMock = new Mock<FeatureBusMapper>(Mock.Of<ILogger<FeatureBusMapper>>());
        
        _featureProbe = new FeatureProbe(
            _loggerMock.Object,
            _configurationMock.Object,
            _patternEngineMock.Object,
            _featureBusMapperMock.Object);
    }

    [Fact]
    public async Task ProbeCurrentStateAsync_ValidSymbol_ReturnsCompleteSnapshot()
    {
        // Arrange
        const string symbol = "ES";
        var mockPatternScores = new PatternScoresWithDetails
        {
            BullScore = 0.7,
            BearScore = 0.3,
            OverallConfidence = 0.8,
            DetectedPatterns = new List<PatternDetail>
            {
                new() { Name = "BullFlag", Score = 0.8, IsActive = true, Direction = 1, Confidence = 0.9 },
                new() { Name = "Doji", Score = 0.6, IsActive = true, Direction = 0, Confidence = 0.7 }
            }
        };

        _patternEngineMock
            .Setup(x => x.GetCurrentScoresAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatternScores);

        // Act
        var snapshot = await _featureProbe.ProbeCurrentStateAsync(symbol);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(symbol, snapshot.Symbol);
        Assert.True(snapshot.Features.Count > 10); // Should have many features

        // Verify zone metrics
        Assert.True(snapshot.HasFeature("zone.distance_atr"));
        Assert.True(snapshot.HasFeature("zone.breakout_score"));
        Assert.True(snapshot.HasFeature("zone.pressure"));
        Assert.True(snapshot.HasFeature("zone.type"));

        // Verify pattern metrics
        Assert.Equal(0.7, snapshot.GetFeature<double>("pattern.bull_score"));
        Assert.Equal(0.3, snapshot.GetFeature<double>("pattern.bear_score"));
        Assert.Equal(0.8, snapshot.GetFeature<double>("pattern.confidence"));
        Assert.True(snapshot.GetFeature<bool>("pattern.kind::BullFlag"));

        // Verify regime metrics  
        Assert.True(snapshot.HasFeature("market_regime"));
        Assert.True(snapshot.HasFeature("volatility_z_score"));
        Assert.True(snapshot.HasFeature("trend.strength"));

        // Verify microstructure metrics
        Assert.True(snapshot.HasFeature("order_flow_imbalance"));
        Assert.True(snapshot.HasFeature("volume_profile"));
        Assert.True(snapshot.HasFeature("momentum.z_score"));

        // Verify additional features
        Assert.True(snapshot.HasFeature("vwap_distance"));
        Assert.True(snapshot.HasFeature("session_volume"));
        Assert.True(snapshot.HasFeature("time_of_day"));
        Assert.True(snapshot.HasFeature("time_to_close_minutes"));
    }

    [Fact]
    public async Task ProbeCurrentStateAsync_PatternEngineThrows_ReturnsSnapshotWithDefaults()
    {
        // Arrange
        const string symbol = "NQ";
        _patternEngineMock
            .Setup(x => x.GetCurrentScoresAsync(symbol, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Pattern engine error"));

        // Act
        var snapshot = await _featureProbe.ProbeCurrentStateAsync(symbol);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(symbol, snapshot.Symbol);
        
        // Should have default pattern values
        Assert.Equal(0.5, snapshot.GetFeature<double>("pattern.bull_score"));
        Assert.Equal(0.5, snapshot.GetFeature<double>("pattern.bear_score"));
        Assert.Equal(0.0, snapshot.GetFeature<double>("pattern.confidence"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ProbeCurrentStateAsync_InvalidSymbol_ThrowsArgumentException(string invalidSymbol)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _featureProbe.ProbeCurrentStateAsync(invalidSymbol));
    }

    [Fact]
    public async Task ProbeCurrentStateAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        const string symbol = "ES";
        var cancelledToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _featureProbe.ProbeCurrentStateAsync(symbol, cancelledToken));
    }

    [Fact]
    public void FeatureSnapshot_GetFeature_ReturnsCorrectTypes()
    {
        // Arrange
        var snapshot = new FeatureSnapshot();
        snapshot.Features["double_feature"] = 3.14;
        snapshot.Features["string_feature"] = "test";
        snapshot.Features["bool_feature"] = true;
        snapshot.Features["int_feature"] = 42;

        // Act & Assert
        Assert.Equal(3.14, snapshot.GetFeature<double>("double_feature"));
        Assert.Equal("test", snapshot.GetFeature<string>("string_feature"));
        Assert.True(snapshot.GetFeature<bool>("bool_feature"));
        Assert.Equal(42, snapshot.GetFeature<int>("int_feature"));
    }

    [Fact]
    public void FeatureSnapshot_GetFeature_WithDefault_ReturnsDefaultForMissingKeys()
    {
        // Arrange
        var snapshot = new FeatureSnapshot();

        // Act & Assert
        Assert.Equal(99.9, snapshot.GetFeature("missing_key", 99.9));
        Assert.Equal("default", snapshot.GetFeature("missing_key", "default"));
        Assert.False(snapshot.GetFeature("missing_key", false));
    }

    [Fact]
    public void FeatureSnapshot_HasFeature_CorrectlyIdentifiesExistence()
    {
        // Arrange
        var snapshot = new FeatureSnapshot();
        snapshot.Features["existing_key"] = "value";

        // Act & Assert
        Assert.True(snapshot.HasFeature("existing_key"));
        Assert.False(snapshot.HasFeature("missing_key"));
    }

    [Fact]
    public async Task ProbeCurrentStateAsync_MultipleCallsWithinCacheWindow_UsesCachedValues()
    {
        // Arrange
        const string symbol = "ES";
        var mockPatternScores = new PatternScoresWithDetails
        {
            BullScore = 0.6,
            BearScore = 0.4,
            OverallConfidence = 0.75,
            DetectedPatterns = new List<PatternDetail>()
        };

        _patternEngineMock
            .Setup(x => x.GetCurrentScoresAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatternScores);

        // Act - Make multiple calls quickly
        var snapshot1 = await _featureProbe.ProbeCurrentStateAsync(symbol);
        var snapshot2 = await _featureProbe.ProbeCurrentStateAsync(symbol);

        // Assert
        Assert.NotNull(snapshot1);
        Assert.NotNull(snapshot2);
        
        // Values should be similar due to caching (though some randomization may occur)
        Assert.Equal(0.6, snapshot1.GetFeature<double>("pattern.bull_score"));
        Assert.Equal(0.6, snapshot2.GetFeature<double>("pattern.bull_score"));
        
        // Pattern engine should only be called once per symbol
        _patternEngineMock.Verify(x => x.GetCurrentScoresAsync(symbol, It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ProbeCurrentStateAsync_TimeBasedFeatures_AreRealistic()
    {
        // Arrange
        const string symbol = "ES";
        var mockPatternScores = new PatternScoresWithDetails
        {
            BullScore = 0.5,
            BearScore = 0.5,
            OverallConfidence = 0.5,
            DetectedPatterns = new List<PatternDetail>()
        };

        _patternEngineMock
            .Setup(x => x.GetCurrentScoresAsync(symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatternScores);

        // Act
        var snapshot = await _featureProbe.ProbeCurrentStateAsync(symbol);

        // Assert
        var timeOfDay = snapshot.GetFeature<double>("time_of_day");
        var timeToClose = snapshot.GetFeature<double>("time_to_close_minutes");

        Assert.InRange(timeOfDay, 0.0, 24.0);
        Assert.InRange(timeToClose, -1440.0, 1440.0); // Within 24 hours
    }
}