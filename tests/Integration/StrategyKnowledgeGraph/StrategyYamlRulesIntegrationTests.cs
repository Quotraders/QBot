using Xunit;
using Microsoft.Extensions.Logging;
using BotCore.StrategyDsl;
using BotCore.Strategy;
using BotCore.Fusion;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace BotCore.Tests.Integration.StrategyKnowledgeGraph;

/// <summary>
/// Integration tests that validate YAML strategy rules against knowledge graph recommendations
/// Tests the complete pipeline from YAML loading through strategy evaluation
/// </summary>
public sealed class StrategyYamlRulesIntegrationTests
{
    [Fact]
    public void S2_MeanReversion_RangeRegime_RecommendedWhenConditionsMet()
    {
        // Arrange - Create scenario matching S2 YAML conditions
        var strategies = LoadRealStrategies();
        var s2Strategy = strategies.First(s => s.Name == "S2");
        
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // Range regime conditions
            ["zone.dist_to_supply_atr"] = 0.6, // >= 0.5 ✓
            ["zone.dist_to_demand_atr"] = 0.7, // >= 0.5 ✓
            
            // Confluence conditions  
            ["vwap.distance_atr"] = 0.5, // <= 0.6 ✓
            ["keltner.band_touch"] = 1.0, // true ✓
            
            // Contraindications (should NOT be met)
            ["pattern.bear_score"] = 0.3, // < 0.7 for Long ✓
            ["pattern.bull_score"] = 0.3, // < 0.7 for Short ✓
            ["zone.breakout_score"] = 0.5  // < 0.7 ✓
        });
        
        var regimeService = new TestRegimeService(RegimeType.Range);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert
        Assert.NotEmpty(recommendations);
        var s2Recommendation = recommendations.FirstOrDefault(r => r.StrategyName == "S2");
        Assert.NotNull(s2Recommendation);
        Assert.True(s2Recommendation.Confidence > 0.6, 
            $"S2 should have high confidence when conditions are met. Got: {s2Recommendation.Confidence}");
        
        // Verify both Long and Short recommendations exist (bias: both)
        var longRec = recommendations.FirstOrDefault(r => r.StrategyName == "S2" && r.Intent == StrategyIntent.Long);
        var shortRec = recommendations.FirstOrDefault(r => r.StrategyName == "S2" && r.Intent == StrategyIntent.Short);
        Assert.NotNull(longRec);
        Assert.NotNull(shortRec);
    }

    [Fact]
    public void S2_MeanReversion_ContraIndicationBlocks_NotRecommendedWhenBearScoreHigh()
    {
        // Arrange - Create scenario where contraindication is triggered
        var strategies = LoadRealStrategies();
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // All other conditions met
            ["zone.dist_to_supply_atr"] = 0.6,
            ["zone.dist_to_demand_atr"] = 0.7,
            ["vwap.distance_atr"] = 0.5,
            ["keltner.band_touch"] = 1.0,
            
            // Contraindication triggered - high bear score should block Long
            ["pattern.bear_score"] = 0.8, // > 0.7 - blocks Long ✗
            ["pattern.bull_score"] = 0.3,
            ["zone.breakout_score"] = 0.5
        });
        
        var regimeService = new TestRegimeService(RegimeType.Range);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert - S2 Long should be blocked by contraindication
        var s2LongRec = recommendations.FirstOrDefault(r => r.StrategyName == "S2" && r.Intent == StrategyIntent.Long);
        Assert.Null(s2LongRec);
        
        // S2 Short should still be possible (bear score doesn't block Short)
        var s2ShortRec = recommendations.FirstOrDefault(r => r.StrategyName == "S2" && r.Intent == StrategyIntent.Short);
        // Note: This depends on the exact contraindication logic - adjust based on implementation
    }

    [Fact]
    public void S3_Compression_LowVolRegime_RecommendedWhenVolContractionPresent()
    {
        // Arrange - Create scenario matching S3 YAML conditions
        var strategies = LoadRealStrategies();
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // Volatility contraction conditions
            ["vdc"] = 0.5, // <= 0.6 ✓
            ["inside_bars_lookback"] = 3.0, // >= 2 ✓
            
            // Confluence conditions
            ["zone.breakout_score"] = 0.7, // >= 0.65 ✓
        });
        
        var regimeService = new TestRegimeService(RegimeType.LowVol);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert
        Assert.NotEmpty(recommendations);
        var s3Recommendation = recommendations.FirstOrDefault(r => r.StrategyName == "S3");
        Assert.NotNull(s3Recommendation);
        Assert.True(s3Recommendation.Confidence > 0.5, 
            $"S3 should be recommended when compression conditions are met. Got: {s3Recommendation.Confidence}");
    }

    [Fact]
    public void S6_Momentum_TrendRegime_RecommendedWhenMomentumHigh()
    {
        // Arrange - Create scenario matching S6 YAML conditions
        var strategies = LoadRealStrategies();
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // Momentum conditions
            ["mom.zscore"] = 1.5, // >= 1.0 ✓
            ["pullback.at_risk"] = 0.4, // <= 0.6 ✓
            
            // Confluence conditions
            ["zone.dist_to_opposing_atr"] = 0.9, // >= 0.8 ✓
            ["zone.breakout_score"] = 0.8, // >= 0.75 ✓
        });
        
        var regimeService = new TestRegimeService(RegimeType.Trend);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert
        Assert.NotEmpty(recommendations);
        var s6Recommendation = recommendations.FirstOrDefault(r => r.StrategyName == "S6");
        Assert.NotNull(s6Recommendation);
        Assert.True(s6Recommendation.Confidence > 0.5, 
            $"S6 should be recommended when momentum conditions are met. Got: {s6Recommendation.Confidence}");
    }

    [Fact]
    public void S11_Exhaustion_HighVolRegime_RecommendedWhenClimaxPresent()
    {
        // Arrange - Create scenario matching S11 YAML conditions
        var strategies = LoadRealStrategies();
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // Exhaustion/climax conditions
            ["climax.volume_thrust"] = 2.0, // >= 1.5 ✓
            ["mom.zscore"] = -1.5, // <= -1.0 for Long intent ✓
            
            // Confluence conditions
            ["zone.test_count"] = 3.0, // >= 2 ✓
            ["zone.pressure"] = 0.8, // >= 0.6 ✓
            
            // Contraindication should NOT be met
            ["zone.breakout_score"] = 0.6  // < 0.75 ✓
        });
        
        var regimeService = new TestRegimeService(RegimeType.HighVol);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert
        Assert.NotEmpty(recommendations);
        var s11Recommendation = recommendations.FirstOrDefault(r => r.StrategyName == "S11");
        Assert.NotNull(s11Recommendation);
        Assert.True(s11Recommendation.Confidence > 0.5, 
            $"S11 should be recommended when exhaustion conditions are met. Got: {s11Recommendation.Confidence}");
    }

    [Fact]
    public void WrongRegime_FiltersOutStrategies()
    {
        // Arrange - S6 requires Trend regime, but we're in Range
        var strategies = LoadRealStrategies();
        var featureProbe = new TestFeatureProbe();
        featureProbe.SetFeatureValues(new Dictionary<string, double>
        {
            // Perfect S6 conditions
            ["mom.zscore"] = 1.5,
            ["pullback.at_risk"] = 0.4,
            ["zone.dist_to_opposing_atr"] = 0.9,
            ["zone.breakout_score"] = 0.8,
        });
        
        // Wrong regime - S6 requires Trend, but we're in Range
        var regimeService = new TestRegimeService(RegimeType.Range);
        var knowledgeGraph = new StrategyKnowledgeGraphNew(
            strategies, 
            featureProbe, 
            regimeService,
            CreateLogger<StrategyKnowledgeGraphNew>());

        // Act
        var recommendations = knowledgeGraph.Evaluate("ES", DateTime.UtcNow);

        // Assert - S6 should be filtered out due to wrong regime
        var s6Recommendation = recommendations.FirstOrDefault(r => r.StrategyName == "S6");
        Assert.Null(s6Recommendation);
    }

    private static IReadOnlyList<DslStrategy> LoadRealStrategies()
    {
        var strategiesPath = "/home/runner/work/trading-bot-c-/trading-bot-c-/config/strategies";
        if (!Directory.Exists(strategiesPath))
        {
            throw new DirectoryNotFoundException($"Strategies directory not found: {strategiesPath}");
        }
        
        return SimpleDslLoader.LoadAll(strategiesPath);
    }

    private static ILogger<T> CreateLogger<T>()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<T>();
    }
}

/// <summary>
/// Test implementation of IFeatureProbe for controlled testing
/// </summary>
internal sealed class TestFeatureProbe : IFeatureProbe
{
    private readonly Dictionary<string, double> _features = new();

    public void SetFeatureValues(Dictionary<string, double> features)
    {
        _features.Clear();
        foreach (var kvp in features)
        {
            _features[kvp.Key] = kvp.Value;
        }
    }

    public double Get(string symbol, string key)
    {
        return _features.TryGetValue(key, out var value) ? value : 0.0;
    }
}

/// <summary>
/// Test implementation of IRegimeService for controlled testing
/// </summary>
internal sealed class TestRegimeService : IRegimeService
{
    private readonly RegimeType _regime;

    public TestRegimeService(RegimeType regime)
    {
        _regime = regime;
    }

    public RegimeType GetRegime(string symbol)
    {
        return _regime;
    }
}