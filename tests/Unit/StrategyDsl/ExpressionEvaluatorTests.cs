using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Unit.StrategyDsl;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _evaluator;

    public ExpressionEvaluatorTests()
    {
        _evaluator = new ExpressionEvaluator(NullLogger<ExpressionEvaluator>.Instance);
    }

    [Theory]
    [InlineData("zone.distance_atr <= 0.8", "zone.distance_atr", 0.5, true)]
    [InlineData("zone.distance_atr <= 0.8", "zone.distance_atr", 1.0, false)]
    [InlineData("zone.breakout_score >= 0.7", "zone.breakout_score", 0.8, true)]
    [InlineData("zone.breakout_score >= 0.7", "zone.breakout_score", 0.6, false)]
    [InlineData("pattern.bull_score > pattern.bear_score", null, 0, true)] // Will set both values
    public void EvaluateExpression_NumericComparisons_ReturnsExpectedResult(string expression, string featureName, double value, bool expected)
    {
        // Arrange
        var features = new Dictionary<string, object>();
        
        if (featureName != null)
        {
            features[featureName] = value;
        }
        else
        {
            // Special case for pattern comparison
            features["pattern.bull_score"] = 0.7;
            features["pattern.bear_score"] = 0.4;
        }
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("volatility_contraction == true", true, true)]
    [InlineData("volatility_contraction == true", false, false)]
    [InlineData("volatility_contraction == false", false, true)]
    [InlineData("volatility_contraction == false", true, false)]
    public void EvaluateExpression_BooleanComparisons_ReturnsExpectedResult(string expression, bool featureValue, bool expected)
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["volatility_contraction"] = featureValue
        };
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("market_regime == \"Range\"", "Range", true)]
    [InlineData("market_regime == \"Range\"", "Trend", false)]
    [InlineData("zone.type == \"Support\"", "Support", true)]
    [InlineData("zone.type == \"Support\"", "Resistance", false)]
    public void EvaluateExpression_StringComparisons_ReturnsExpectedResult(string expression, string featureValue, bool expected)
    {
        // Arrange
        var featureName = expression.Split(' ')[0]; // Extract feature name
        var features = new Dictionary<string, object>
        {
            [featureName] = featureValue
        };
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EvaluateExpression_LogicalAnd_ReturnsCorrectResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["zone.distance_atr"] = 0.5,
            ["zone.breakout_score"] = 0.8
        };
        
        _evaluator.UpdateFeatures(features);

        // Act & Assert
        Assert.True(_evaluator.EvaluateExpression("zone.distance_atr <= 0.8 AND zone.breakout_score >= 0.7"));
        Assert.False(_evaluator.EvaluateExpression("zone.distance_atr <= 0.3 AND zone.breakout_score >= 0.7"));
    }

    [Fact]
    public void EvaluateExpression_LogicalOr_ReturnsCorrectResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["pattern.bull_score"] = 0.3,
            ["pattern.bear_score"] = 0.8
        };
        
        _evaluator.UpdateFeatures(features);

        // Act & Assert
        Assert.True(_evaluator.EvaluateExpression("pattern.bull_score >= 0.7 OR pattern.bear_score >= 0.7"));
        Assert.False(_evaluator.EvaluateExpression("pattern.bull_score >= 0.9 OR pattern.bear_score >= 0.9"));
    }

    [Fact]
    public void EvaluateExpression_WithParentheses_ReturnsCorrectResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["zone.distance_atr"] = 0.5,
            ["zone.pressure"] = 0.6,
            ["zone.breakout_score"] = 0.8
        };
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.EvaluateExpression("zone.breakout_score >= 0.7 OR (zone.distance_atr <= 0.4 AND zone.pressure >= 0.6)");

        // Assert
        Assert.True(result); // Should be true because breakout_score >= 0.7
    }

    [Theory]
    [InlineData("fibonacci.level IN [0.382, 0.5, 0.618]", 0.5, true)]
    [InlineData("fibonacci.level IN [0.382, 0.5, 0.618]", 0.236, false)]
    [InlineData("market_regime IN [\"Range\", \"LowVol\"]", "Range", true)]
    [InlineData("market_regime IN [\"Range\", \"LowVol\"]", "Trend", false)]
    public void EvaluateExpression_InOperator_ReturnsExpectedResult(string expression, object featureValue, bool expected)
    {
        // Arrange
        var featureName = expression.Split(' ')[0];
        var features = new Dictionary<string, object>
        {
            [featureName] = featureValue
        };
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EvaluateExpression_TimeComparisons_ReturnsCorrectResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["time_of_day"] = TimeSpan.FromHours(10.5), // 10:30 AM
        };
        
        _evaluator.UpdateFeatures(features);

        // Act & Assert
        Assert.True(_evaluator.EvaluateExpression("time_of_day >= \"09:45\""));
        Assert.True(_evaluator.EvaluateExpression("time_of_day <= \"15:45\""));
        Assert.False(_evaluator.EvaluateExpression("time_of_day >= \"16:00\""));
    }

    [Fact]
    public void EvaluateAllExpressions_WithMultipleExpressions_ReturnsAndResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["zone.distance_atr"] = 0.5,
            ["zone.breakout_score"] = 0.8,
            ["volatility_contraction"] = true
        };
        
        _evaluator.UpdateFeatures(features);

        var expressions = new[]
        {
            "zone.distance_atr <= 0.8",
            "zone.breakout_score >= 0.7",
            "volatility_contraction == true"
        };

        // Act
        var result = _evaluator.EvaluateAllExpressions(expressions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAnyExpression_WithMultipleExpressions_ReturnsOrResult()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["zone.distance_atr"] = 1.5, // Fails first condition
            ["zone.breakout_score"] = 0.8, // Passes second condition
            ["volatility_contraction"] = false // Fails third condition
        };
        
        _evaluator.UpdateFeatures(features);

        var expressions = new[]
        {
            "zone.distance_atr <= 0.8",
            "zone.breakout_score >= 0.7", 
            "volatility_contraction == true"
        };

        // Act
        var result = _evaluator.EvaluateAnyExpression(expressions);

        // Assert
        Assert.True(result); // Should be true because second expression passes
    }

    [Fact]
    public void EvaluateExpression_WithMissingFeature_ReturnsFalse()
    {
        // Arrange - No features set
        var expression = "nonexistent.feature >= 0.5";

        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetFeatureValue_ExistingFeature_ReturnsValue()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["test.feature"] = 42.5
        };
        
        _evaluator.UpdateFeatures(features);

        // Act
        var result = _evaluator.GetFeatureValue("test.feature");

        // Assert
        Assert.Equal(42.5, result);
    }

    [Fact]
    public void HasFeature_ExistingFeature_ReturnsTrue()
    {
        // Arrange
        var features = new Dictionary<string, object>
        {
            ["test.feature"] = "value"
        };
        
        _evaluator.UpdateFeatures(features);

        // Act & Assert
        Assert.True(_evaluator.HasFeature("test.feature"));
        Assert.False(_evaluator.HasFeature("nonexistent.feature"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void EvaluateExpression_InvalidInput_ReturnsFalse(string expression, bool expected)
    {
        // Act
        var result = _evaluator.EvaluateExpression(expression);

        // Assert
        Assert.Equal(expected, result);
    }
}