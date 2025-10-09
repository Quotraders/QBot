using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BotCore.StrategyDsl;

/// <summary>
/// Simple expression evaluator for DSL conditions
/// Supports basic comparison operators, logical operators, and feature references
/// </summary>
public class ExpressionEvaluator
{
    private readonly ILogger<ExpressionEvaluator> _logger;
    private readonly Dictionary<string, object> _featureValues = new();

    // Numeric comparison tolerance constant
    private const double NumericComparisonTolerance = 0.0001;   // Tolerance for floating-point equality comparisons

    // Regex patterns for parsing expressions
    private static readonly Regex ComparisonRegex = new(
        @"^(\w+(?:\.\w+)*)\s*(>=|<=|>|<|==|!=)\s*(.+)$", 
        RegexOptions.Compiled);
    
    private static readonly Regex BooleanRegex = new(
        @"^(\w+(?:\.\w+)*)\s*(==)\s*(true|false)$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex InOperatorRegex = new(
        @"^(\w+(?:\.\w+)*)\s+IN\s+\[(.+)\]$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExpressionEvaluator(ILogger<ExpressionEvaluator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Update feature values for evaluation
    /// </summary>
    public void UpdateFeatures(Dictionary<string, object> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        
        lock (_featureValues)
        {
            _featureValues.Clear();
            foreach (var kvp in features)
            {
                _featureValues[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Evaluate a single DSL expression
    /// </summary>
    public bool EvaluateExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        try
        {
            expression = expression.Trim();

            // Handle logical operators (AND, OR)
            if (expression.Contains(" AND ", StringComparison.Ordinal))
            {
                return EvaluateLogicalAnd(expression);
            }

            if (expression.Contains(" OR ", StringComparison.Ordinal))
            {
                return EvaluateLogicalOr(expression);
            }

            // Handle parentheses (simple nested evaluation)
            if (expression.Contains('(', StringComparison.Ordinal) && expression.Contains(')', StringComparison.Ordinal))
            {
                return EvaluateWithParentheses(expression);
            }

            // Handle single condition
            return EvaluateSingleCondition(expression);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression: {Expression}", expression);
            return false;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression: {Expression}", expression);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression: {Expression}", expression);
            return false;
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression: {Expression}", expression);
            return false;
        }
    }

    /// <summary>
    /// Evaluate multiple expressions with AND logic
    /// </summary>
    public bool EvaluateAllExpressions(IEnumerable<string> expressions)
    {
        return expressions.All(EvaluateExpression);
    }

    /// <summary>
    /// Evaluate multiple expressions with OR logic
    /// </summary>
    public bool EvaluateAnyExpression(IEnumerable<string> expressions)
    {
        return expressions.Any(EvaluateExpression);
    }

    /// <summary>
    /// Get the current value of a feature
    /// </summary>
    public object? GetFeatureValue(string featureName)
    {
        lock (_featureValues)
        {
            return _featureValues.TryGetValue(featureName, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Check if a feature exists
    /// </summary>
    public bool HasFeature(string featureName)
    {
        lock (_featureValues)
        {
            return _featureValues.ContainsKey(featureName);
        }
    }

    /// <summary>
    /// Async wrapper for expression evaluation (compatible with knowledge graph)
    /// </summary>
    public Task<ExpressionResult> EvaluateAsync(string expression, Dictionary<string, object> features)
    {
        return Task.Run(() =>
        {
            try
            {
                UpdateFeatures(features);
                var result = EvaluateExpression(expression);
                return new ExpressionResult { IsSuccess = true, Value = result };
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Failed to evaluate expression: {Expression}", expression);
                return new ExpressionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Failed to evaluate expression: {Expression}", expression);
                return new ExpressionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to evaluate expression: {Expression}", expression);
                return new ExpressionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, "Failed to evaluate expression: {Expression}", expression);
                return new ExpressionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        });
    }

    private static readonly string[] AndSeparator = new[] { " AND " };
    private static readonly string[] OrSeparator = new[] { " OR " };

    private bool EvaluateLogicalAnd(string expression)
    {
        var parts = expression.Split(AndSeparator, StringSplitOptions.RemoveEmptyEntries);
        return parts.All(part => EvaluateExpression(part.Trim()));
    }

    private bool EvaluateLogicalOr(string expression)
    {
        var parts = expression.Split(OrSeparator, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => EvaluateExpression(part.Trim()));
    }

    private bool EvaluateWithParentheses(string expression)
    {
        // Simple parentheses handling - evaluate innermost parentheses first
        var innerMatch = Regex.Match(expression, @"\(([^()]+)\)");
        if (innerMatch.Success)
        {
            var innerExpression = innerMatch.Groups[1].Value;
            var innerResult = EvaluateExpression(innerExpression);
            
            // Replace the parenthetical expression with its result
            var replacedExpression = expression.Replace(innerMatch.Value, innerResult.ToString().ToUpperInvariant(), StringComparison.Ordinal);
            return EvaluateExpression(replacedExpression);
        }

        // No nested parentheses, just remove outer parentheses if they exist
        var trimmed = expression.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            return EvaluateExpression(trimmed[1..^1]);
        }

        return EvaluateSingleCondition(expression);
    }

    private bool EvaluateSingleCondition(string condition)
    {
        condition = condition.Trim();

        // Handle boolean literals
        if (condition.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (condition.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        // Handle IN operator
        var inMatch = InOperatorRegex.Match(condition);
        if (inMatch.Success)
        {
            return EvaluateInOperator(inMatch);
        }

        // Handle boolean feature checks
        var boolMatch = BooleanRegex.Match(condition);
        if (boolMatch.Success)
        {
            return EvaluateBooleanComparison(boolMatch);
        }

        // Handle comparison operators
        var compMatch = ComparisonRegex.Match(condition);
        if (compMatch.Success)
        {
            return EvaluateComparison(compMatch);
        }

        // Handle simple feature existence or boolean features
        if (condition.Contains('.', StringComparison.Ordinal))
        {
            var featureValue = GetFeatureValue(condition);
            if (featureValue is bool boolValue)
                return boolValue;
            
            return featureValue != null;
        }

        _logger.LogWarning("Unrecognized condition format: {Condition}", condition);
        return false;
    }

    private bool EvaluateInOperator(Match match)
    {
        var featureName = match.Groups[1].Value;
        var listString = match.Groups[2].Value;
        
        var featureValue = GetFeatureValue(featureName);
        if (featureValue == null)
            return false;

        // Parse the list values
        var listValues = listString.Split(',')
            .Select(v => v.Trim().Trim('"', '\''))
            .ToList();

        var featureStringValue = featureValue.ToString();
        return listValues.Contains(featureStringValue, StringComparer.OrdinalIgnoreCase);
    }

    private bool EvaluateBooleanComparison(Match match)
    {
        var featureName = match.Groups[1].Value;
        var expectedValue = bool.Parse(match.Groups[3].Value);
        
        var featureValue = GetFeatureValue(featureName);
        if (featureValue is bool boolValue)
            return boolValue == expectedValue;
        
        return false;
    }

    private bool EvaluateComparison(Match match)
    {
        var featureName = match.Groups[1].Value;
        var operatorStr = match.Groups[2].Value;
        var valueStr = match.Groups[3].Value.Trim().Trim('"', '\'');

        var featureValue = GetFeatureValue(featureName);
        if (featureValue == null)
            return false;

        // Handle string comparisons
        if (operatorStr == "==" && featureValue is string strValue)
        {
            return strValue.Equals(valueStr, StringComparison.OrdinalIgnoreCase);
        }

        if (operatorStr == "!=" && featureValue is string strValue2)
        {
            return !strValue2.Equals(valueStr, StringComparison.OrdinalIgnoreCase);
        }

        // Handle time comparisons
        if (featureName.Contains("time", StringComparison.Ordinal) && TimeSpan.TryParse(valueStr, out var timeValue) && TimeSpan.TryParse(featureValue.ToString(), out var featureTime))
        {
            return operatorStr switch
            {
                ">=" => featureTime >= timeValue,
                "<=" => featureTime <= timeValue,
                ">" => featureTime > timeValue,
                "<" => featureTime < timeValue,
                "==" => featureTime == timeValue,
                "!=" => featureTime != timeValue,
                _ => false
                };
            }
        }

        // Handle numeric comparisons
        if (double.TryParse(valueStr, out var numericValue))
        {
            var featureNumericValue = Convert.ToDouble(featureValue, System.Globalization.CultureInfo.InvariantCulture);
            
            return operatorStr switch
            {
                ">=" => featureNumericValue >= numericValue,
                "<=" => featureNumericValue <= numericValue,
                ">" => featureNumericValue > numericValue,
                "<" => featureNumericValue < numericValue,
                "==" => Math.Abs(featureNumericValue - numericValue) < NumericComparisonTolerance,
                "!=" => Math.Abs(featureNumericValue - numericValue) >= NumericComparisonTolerance,
                _ => false
            };
        }

        return false;
    }

    /// <summary>
    /// Get all available feature names for debugging
    /// </summary>
    public List<string> GetAvailableFeatures()
    {
        lock (_featureValues)
        {
            return _featureValues.Keys.ToList();
        }
    }

    /// <summary>
    /// Clear all feature values
    /// </summary>
    public void Clear()
    {
        lock (_featureValues)
        {
            _featureValues.Clear();
        }
    }
}

/// <summary>
/// Result of expression evaluation
/// </summary>
public sealed class ExpressionResult
{
    public bool IsSuccess { get; set; }
    public object? Value { get; set; }
    public string? ErrorMessage { get; set; }
}