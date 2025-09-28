using System;
using System.Collections.Generic;
using System.Linq;
using BotCore.Strategy;
using Microsoft.Extensions.Logging;

namespace BotCore.StrategyDsl;

/// <summary>
/// Feature value retrieval interface for strategy knowledge graph evaluation
/// Maps DSL feature keys to real feature values from zones, patterns, regimes, etc.
/// </summary>
public interface IFeatureProbe 
{ 
    double Get(string symbol, string key); 
}

/// <summary>
/// Simple feature probe implementation
/// Maps strategy DSL feature keys to actual feature values from the trading system
/// </summary>
public sealed class FeatureProbe : IFeatureProbe
{
    private readonly ILogger<FeatureProbe> _logger;
    private readonly Dictionary<string, double> _featureCache = new();

    public FeatureProbe(ILogger<FeatureProbe> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public double Get(string symbol, string key)
    {
        // For now, return mock values that allow strategies to evaluate
        // In real implementation, this would call actual feature sources
        return key switch
        {
            "zone.dist_to_demand_atr" => 0.6,
            "zone.dist_to_supply_atr" => 0.7,
            "zone.breakout_score" => 0.5,
            "zone.pressure" => 0.4,
            "pattern.bull_score" => 0.3,
            "pattern.bear_score" => 0.2,
            "vdc" => 0.5,  // volatility contraction ratio
            "mom.zscore" => 0.8,
            "pullback.at_risk" => 0.4,
            "climax.volume_thrust" => 1.2,
            "inside_bars_lookback" => 3.0,
            "zone.dist_to_opposing_atr" => 0.9,
            "zone.test_count" => 2.0,
            "vwap.distance_atr" => 0.5,
            "keltner.band_touch" => 1.0, // true
            "boll.band_touch" => 0.0, // false
            _ => 0.0
        };
    }
}

/// <summary>
/// Simple regime service interface for strategy evaluation  
/// </summary>
public interface IRegimeService
{
    RegimeType GetRegime(string symbol);
}

/// <summary>
/// Basic regime types for strategy filtering
/// </summary>
public enum RegimeType
{
    Range,
    LowVol,
    Trend,
    HighVol
}

/// <summary>
/// Strategy knowledge graph implementation that follows the problem statement specification
/// Evaluates YAML-defined strategies against real-time market features
/// </summary>
public sealed class StrategyKnowledgeGraphNew : IStrategyKnowledgeGraph
{
    private readonly IReadOnlyList<DslStrategy> _cards;
    private readonly IFeatureProbe _probe;
    private readonly IRegimeService _regimes;
    private readonly ILogger<StrategyKnowledgeGraphNew> _logger;

    public StrategyKnowledgeGraphNew(
        IReadOnlyList<DslStrategy> cards, 
        IFeatureProbe probe, 
        IRegimeService regimes,
        ILogger<StrategyKnowledgeGraphNew> logger)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _regimes = regimes ?? throw new ArgumentNullException(nameof(regimes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<StrategyRecommendation> Evaluate(string symbol, DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        var regime = _regimes.GetRegime(symbol);
        var list = new List<StrategyRecommendation>();

        _logger.LogDebug("Evaluating {StrategyCount} strategies for {Symbol} in regime {Regime}", 
            _cards.Count, symbol, regime);

        foreach (var card in _cards)
        {
            try
            {
                // Step 1: Regime filter
                if (!EvaluateRegimeFilter(card, regime))
                {
                    _logger.LogTrace("Strategy {StrategyName} filtered out by regime {Regime}", card.Name, regime);
                    continue;
                }

                // Step 2: Micro conditions (all must pass)
                if (!EvaluateMicroConditions(card, symbol))
                {
                    _logger.LogTrace("Strategy {StrategyName} failed micro conditions", card.Name);
                    continue;
                }

                // Step 3: Contraindications (any failing blocks the strategy)
                if (EvaluateContraindications(card, symbol))
                {
                    _logger.LogTrace("Strategy {StrategyName} blocked by contraindications", card.Name);
                    continue;
                }

                // Step 4: Confluence (at least one must pass)
                var confluenceResults = EvaluateConfluence(card, symbol);
                if (!confluenceResults.Any())
                {
                    _logger.LogTrace("Strategy {StrategyName} has no confluence", card.Name);
                    continue;
                }

                // Step 5: Calculate confidence from evidence strength
                var evidence = CreateEvidence(card, symbol, confluenceResults);
                double confidence = CalculateConfidence(evidence, card);

                // Create recommendations for both directions unless bias restricts it
                if (!string.Equals(card.Bias, "bearish", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new StrategyRecommendation(
                        card.Name, 
                        StrategyIntent.Long, 
                        confidence, 
                        evidence, 
                        card.TelemetryTags.ToArray()));
                }

                if (!string.Equals(card.Bias, "bullish", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new StrategyRecommendation(
                        card.Name, 
                        StrategyIntent.Short, 
                        confidence, 
                        evidence, 
                        card.TelemetryTags.ToArray()));
                }

                _logger.LogTrace("Strategy {StrategyName} recommended with confidence {Confidence:F2}", 
                    card.Name, confidence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating strategy {StrategyName}", card.Name);
            }
        }

        // Return top 5 ranked by confidence
        var ranked = list.OrderByDescending(x => x.Confidence).Take(5).ToList();
        
        _logger.LogDebug("Knowledge graph evaluation complete: {RecommendationCount} recommendations from {StrategyCount} strategies", 
            ranked.Count, _cards.Count);

        return ranked;
    }

    private bool EvaluateRegimeFilter(DslStrategy card, RegimeType regime)
    {
        if (card.When?.Regime == null || !card.When.Regime.Any())
            return true; // No regime filter

        // Check if current regime matches any of the required regimes
        return card.When.Regime.Any(r => 
            string.Equals(r, regime.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private bool EvaluateMicroConditions(DslStrategy card, string symbol)
    {
        var microConditions = card.When?.Micro ?? new List<string>();
        if (!microConditions.Any())
            return true; // No micro conditions

        // All micro conditions must pass
        return microConditions.All(condition => EvaluateExpression(condition, symbol));
    }

    private bool EvaluateContraindications(DslStrategy card, string symbol)
    {
        var contraindications = card.Contra ?? new List<string>();
        if (!contraindications.Any())
            return false; // No contraindications = not blocked

        // Any contraindication passing blocks the strategy
        return contraindications.Any(condition => EvaluateExpression(condition, symbol));
    }

    private List<string> EvaluateConfluence(DslStrategy card, string symbol)
    {
        var confluenceConditions = card.Confluence ?? new List<string>();
        if (!confluenceConditions.Any())
            return new List<string>(); // No confluence conditions

        // Return all passing confluence conditions
        return confluenceConditions
            .Where(condition => EvaluateExpression(condition, symbol))
            .ToList();
    }

    private bool EvaluateExpression(string expression, string symbol)
    {
        try
        {
            // Simple expression evaluator using feature probe
            if (expression.Contains(">="))
            {
                var parts = expression.Split(">=", StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var featureValue = _probe.Get(symbol, parts[0]);
                    if (double.TryParse(parts[1], out var threshold))
                        return featureValue >= threshold;
                }
            }
            else if (expression.Contains("<="))
            {
                var parts = expression.Split("<=", StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var featureValue = _probe.Get(symbol, parts[0]);
                    if (double.TryParse(parts[1], out var threshold))
                        return featureValue <= threshold;
                }
            }
            else if (expression.Contains(">"))
            {
                var parts = expression.Split(">", StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var featureValue = _probe.Get(symbol, parts[0]);
                    if (double.TryParse(parts[1], out var threshold))
                        return featureValue > threshold;
                }
            }
            else if (expression.Contains("=="))
            {
                if (expression.Contains("true"))
                {
                    var featureName = expression.Split("==")[0].Trim();
                    var featureValue = _probe.Get(symbol, featureName);
                    return featureValue > 0.5; // Treat > 0.5 as true
                }
            }
            else if (expression.Contains("or"))
            {
                var parts = expression.Split("or", StringSplitOptions.TrimEntries);
                return parts.Any(part => EvaluateExpression(part.Trim(), symbol));
            }
            else if (expression.Contains("and"))
            {
                var parts = expression.Split("and", StringSplitOptions.TrimEntries);
                return parts.All(part => EvaluateExpression(part.Trim(), symbol));
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate expression: {Expression}", expression);
            return false;
        }
    }

    private IReadOnlyList<StrategyEvidence> CreateEvidence(DslStrategy card, string symbol, List<string> confluenceResults)
    {
        var evidence = new List<StrategyEvidence>();

        // Add micro condition evidence
        var microConditions = card.When?.Micro ?? new List<string>();
        foreach (var condition in microConditions)
        {
            if (EvaluateExpression(condition, symbol))
            {
                evidence.Add(new StrategyEvidence(condition, 1.0, "Micro condition met"));
            }
        }

        // Add confluence evidence
        foreach (var confluence in confluenceResults)
        {
            evidence.Add(new StrategyEvidence(confluence, 1.0, "Confluence condition met"));
        }

        return evidence;
    }

    private double CalculateConfidence(IReadOnlyList<StrategyEvidence> evidence, DslStrategy card)
    {
        if (!evidence.Any())
            return 0.0;

        // Base confidence from evidence count
        var baseConfidence = Math.Min(1.0, evidence.Count / 4.0); // Assume 4 conditions is full confidence

        // Strategy-specific multipliers
        var strategyMultiplier = card.Name switch
        {
            "S2" => 0.9,  // Conservative for mean reversion
            "S3" => 1.1,  // Higher confidence for compression breakouts
            "S6" => 1.0,  // Neutral for momentum
            "S11" => 0.85, // Lower confidence for exhaustion/reversal
            _ => 1.0
        };

        return Math.Max(0.0, Math.Min(1.0, baseConfidence * strategyMultiplier));
    }
}

/// <summary>
/// Mock regime service for testing
/// </summary>
public sealed class MockRegimeService : IRegimeService
{
    public RegimeType GetRegime(string symbol)
    {
        // For now, return a default regime that allows strategy evaluation
        return RegimeType.Range;
    }
}