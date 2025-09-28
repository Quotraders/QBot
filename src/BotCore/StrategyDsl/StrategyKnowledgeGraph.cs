using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotCore.StrategyDsl;

/// <summary>
/// Strategy knowledge graph that evaluates DSL-defined strategies against real-time market features
/// Filters strategies by regime, micro conditions, and contraindications to produce ranked recommendations
/// </summary>
public sealed class StrategyKnowledgeGraph
{
    private readonly ILogger<StrategyKnowledgeGraph> _logger;
    private readonly FeatureProbe _featureProbe;
    private readonly DslLoader _dslLoader;
    private readonly ExpressionEvaluator _expressionEvaluator;
    private readonly IOptionsMonitor<StrategyKnowledgeGraphOptions> _options;

    public StrategyKnowledgeGraph(
        ILogger<StrategyKnowledgeGraph> logger,
        FeatureProbe featureProbe,
        DslLoader dslLoader, 
        ExpressionEvaluator expressionEvaluator,
        IOptionsMonitor<StrategyKnowledgeGraphOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureProbe = featureProbe ?? throw new ArgumentNullException(nameof(featureProbe));
        _dslLoader = dslLoader ?? throw new ArgumentNullException(nameof(dslLoader));
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Evaluate all DSL strategies against current market state and produce ranked recommendations
    /// Returns strategies that pass regime filters, micro conditions, and contraindication checks
    /// </summary>
    public async Task<IReadOnlyList<StrategyRecommendation>> EvaluateStrategiesAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        try
        {
            var options = _options.CurrentValue;
            
            // Probe current market state
            var featureSnapshot = await _featureProbe.ProbeCurrentStateAsync(symbol, cancellationToken);
            
            // Get available strategies from DSL loader
            var strategies = await _dslLoader.GetStrategiesAsync(cancellationToken);
            
            if (!strategies.Any())
            {
                _logger.LogWarning("No DSL strategies available for evaluation");
                return Array.Empty<StrategyRecommendation>();
            }

            var recommendations = new List<StrategyRecommendation>();

            foreach (var strategy in strategies)
            {
                if (!options.EnabledStrategies.Contains(strategy.Name))
                {
                    _logger.LogTrace("Strategy {StrategyName} is disabled, skipping evaluation", strategy.Name);
                    continue;
                }

                try
                {
                    var recommendation = await EvaluateStrategyAsync(strategy, featureSnapshot, cancellationToken);
                    if (recommendation != null)
                    {
                        recommendations.Add(recommendation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to evaluate strategy {StrategyName}", strategy.Name);
                }
            }

            // Rank and filter recommendations
            var rankedRecommendations = RankRecommendations(recommendations, options);

            _logger.LogDebug("Knowledge graph evaluation completed for {Symbol}: {StrategyCount} strategies evaluated, {RecommendationCount} recommendations produced",
                symbol, strategies.Count, rankedRecommendations.Count);

            return rankedRecommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate strategies for symbol {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// Evaluate a single DSL strategy against the feature snapshot
    /// Returns recommendation if strategy passes all conditions, null otherwise
    /// </summary>
    private async Task<StrategyRecommendation?> EvaluateStrategyAsync(
        DslStrategy strategy,
        FeatureSnapshot featureSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Check regime filter
            if (!await EvaluateRegimeFilterAsync(strategy, featureSnapshot))
            {
                _logger.LogTrace("Strategy {StrategyName} filtered out by regime conditions", strategy.Name);
                return null;
            }

            // Step 2: Evaluate micro conditions (must have at least one confluence)
            var microConditionResults = await EvaluateMicroConditionsAsync(strategy, featureSnapshot);
            var confluenceCount = microConditionResults.Count(r => r.Passed);
            
            if (confluenceCount == 0)
            {
                _logger.LogTrace("Strategy {StrategyName} has no confluence conditions met", strategy.Name);
                return null;
            }

            // Step 3: Check contraindications (any failing contraindication blocks the strategy)
            var contraIndicationResults = await EvaluateContraIndicationsAsync(strategy, featureSnapshot);
            if (contraIndicationResults.Any(r => !r.Passed))
            {
                _logger.LogTrace("Strategy {StrategyName} blocked by contraindications", strategy.Name);
                return null;
            }

            // Step 4: Calculate overall confidence
            var confidence = CalculateStrategyConfidence(microConditionResults, confluenceCount, strategy);
            
            if (confidence < _options.CurrentValue.MinConfidenceThreshold)
            {
                _logger.LogTrace("Strategy {StrategyName} confidence {Confidence:F2} below threshold {Threshold:F2}",
                    strategy.Name, confidence, _options.CurrentValue.MinConfidenceThreshold);
                return null;
            }

            // Step 5: Create recommendation
            var recommendation = new StrategyRecommendation
            {
                StrategyName = strategy.Name,
                Intent = strategy.Intent,
                Confidence = confidence,
                Evidence = CreateEvidenceList(microConditionResults, contraIndicationResults),
                TelemetryTags = strategy.TelemetryTags?.ToArray() ?? Array.Empty<string>(),
                Timestamp = DateTime.UtcNow,
                Symbol = featureSnapshot.Symbol,
                ConfluenceCount = confluenceCount,
                Playbook = strategy.Playbook?.Name ?? "Unknown"
            };

            _logger.LogTrace("Strategy {StrategyName} recommended with confidence {Confidence:F2} and {ConfluenceCount} confluences",
                strategy.Name, confidence, confluenceCount);

            return recommendation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate strategy {StrategyName}", strategy.Name);
            return null;
        }
    }

    /// <summary>
    /// Evaluate regime filter conditions for a strategy
    /// </summary>
    private async Task<bool> EvaluateRegimeFilterAsync(DslStrategy strategy, FeatureSnapshot featureSnapshot)
    {
        if (strategy.When?.Regime == null || !strategy.When.Regime.Any())
            return true; // No regime filter means always allowed

        foreach (var regimeCondition in strategy.When.Regime)
        {
            try
            {
                var evaluationResult = await _expressionEvaluator.EvaluateAsync(regimeCondition, featureSnapshot.Features);
                if (evaluationResult.IsSuccess && evaluationResult.Value is bool boolValue && boolValue)
                {
                    return true; // At least one regime condition passed
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate regime condition '{Condition}' for strategy {StrategyName}",
                    regimeCondition, strategy.Name);
            }
        }

        return false; // No regime conditions passed
    }

    /// <summary>
    /// Evaluate micro conditions (confluence requirements) for a strategy
    /// </summary>
    private async Task<List<ConditionResult>> EvaluateMicroConditionsAsync(DslStrategy strategy, FeatureSnapshot featureSnapshot)
    {
        var results = new List<ConditionResult>();

        if (strategy.When?.MicroConditions == null)
            return results;

        foreach (var microCondition in strategy.When.MicroConditions)
        {
            try
            {
                var evaluationResult = await _expressionEvaluator.EvaluateAsync(microCondition, featureSnapshot.Features);
                var passed = evaluationResult.IsSuccess && evaluationResult.Value is bool boolValue && boolValue;
                
                results.Add(new ConditionResult
                {
                    Condition = microCondition,
                    Passed = passed,
                    ErrorMessage = evaluationResult.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate micro condition '{Condition}' for strategy {StrategyName}",
                    microCondition, strategy.Name);
                
                results.Add(new ConditionResult
                {
                    Condition = microCondition,
                    Passed = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Evaluate contraindication conditions for a strategy
    /// </summary>
    private async Task<List<ConditionResult>> EvaluateContraIndicationsAsync(DslStrategy strategy, FeatureSnapshot featureSnapshot)
    {
        var results = new List<ConditionResult>();

        if (strategy.When?.ContraIndications == null)
            return results;

        foreach (var contraIndication in strategy.When.ContraIndications)
        {
            try
            {
                var evaluationResult = await _expressionEvaluator.EvaluateAsync(contraIndication, featureSnapshot.Features);
                var passed = !evaluationResult.IsSuccess || !(evaluationResult.Value is bool boolValue && boolValue);
                
                results.Add(new ConditionResult
                {
                    Condition = contraIndication,
                    Passed = passed,
                    ErrorMessage = evaluationResult.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate contraindication '{Condition}' for strategy {StrategyName}",
                    contraIndication, strategy.Name);
                
                results.Add(new ConditionResult
                {
                    Condition = contraIndication,
                    Passed = true, // Assume contraindication is not met if evaluation fails
                    ErrorMessage = ex.Message
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Calculate overall confidence for a strategy based on confluence conditions
    /// </summary>
    private double CalculateStrategyConfidence(List<ConditionResult> microConditionResults, int confluenceCount, DslStrategy strategy)
    {
        if (!microConditionResults.Any())
            return 0.0;

        // Base confidence from confluence ratio
        var confluenceRatio = (double)confluenceCount / microConditionResults.Count;
        var baseConfidence = confluenceRatio;

        // Apply strategy-specific confidence boosts
        var strategyMultiplier = strategy.Name switch
        {
            "S2_Zone_Mean_Reversion" => 0.9, // Conservative strategy
            "S3_Compression_Breakout" => 1.1, // Higher confidence in compression plays
            "S6_Trend_Following" => 1.0, // Neutral
            "S11_Failed_Breakout_Reversal" => 0.85, // Lower confidence for reversal plays
            _ => 1.0
        };

        // Boost confidence for high confluence
        var confluenceBoost = confluenceCount >= 3 ? 0.1 : 0.0;

        var finalConfidence = Math.Min(1.0, baseConfidence * strategyMultiplier + confluenceBoost);
        
        return Math.Max(0.0, finalConfidence);
    }

    /// <summary>
    /// Create evidence list from condition evaluation results
    /// </summary>
    private List<string> CreateEvidenceList(List<ConditionResult> microConditions, List<ConditionResult> contraIndications)
    {
        var evidence = new List<string>();

        // Add passed micro conditions as supporting evidence
        evidence.AddRange(microConditions
            .Where(c => c.Passed)
            .Select(c => $"✓ {c.Condition}"));

        // Add failed contraindications as supporting evidence  
        evidence.AddRange(contraIndications
            .Where(c => c.Passed)
            .Select(c => $"✓ No contraindication: {c.Condition}"));

        return evidence;
    }

    /// <summary>
    /// Rank and filter recommendations based on confidence and confluence
    /// </summary>
    private List<StrategyRecommendation> RankRecommendations(List<StrategyRecommendation> recommendations, StrategyKnowledgeGraphOptions options)
    {
        return recommendations
            .Where(r => r.Confidence >= options.MinConfidenceThreshold)
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.ConfluenceCount)
            .Take(options.MaxRecommendations)
            .ToList();
    }
}

/// <summary>
/// Configuration options for the strategy knowledge graph
/// </summary>
public sealed class StrategyKnowledgeGraphOptions
{
    public double MinConfidenceThreshold { get; set; } = 0.6;
    public int MaxRecommendations { get; set; } = 5;
    public List<string> EnabledStrategies { get; set; } = new() { "S2_Zone_Mean_Reversion", "S3_Compression_Breakout", "S6_Trend_Following", "S11_Failed_Breakout_Reversal" };
    public bool EmitTelemetry { get; set; } = true;
}

/// <summary>
/// Strategy recommendation produced by the knowledge graph
/// Contains strategy details, confidence score, and supporting evidence
/// </summary>
public sealed class StrategyRecommendation
{
    public string StrategyName { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Evidence { get; set; } = new();
    public string[] TelemetryTags { get; set; } = Array.Empty<string>();
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public int ConfluenceCount { get; set; }
    public string Playbook { get; set; } = string.Empty;
}

/// <summary>
/// Result of evaluating a single condition
/// </summary>
internal sealed class ConditionResult
{
    public string Condition { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? ErrorMessage { get; set; }
}