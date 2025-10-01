using System;
using System.Collections.Generic;
using System.Linq;
using BotCore.Strategy;
using Microsoft.Extensions.Logging;
using BotCore.Patterns;
using BotCore.Services;
using BotCore.Fusion;
using TradingBot.IntelligenceStack;
using TradingBot.Abstractions;

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
/// Production feature probe implementation that integrates with real trading systems
/// Maps strategy DSL feature keys to actual feature values from zones, patterns, regimes, etc.
/// </summary>
public sealed class ProductionFeatureProbe : IFeatureProbe
{
    // Feature fallback constants for missing data scenarios
    private const double DefaultVolatilityContractionFallback = 0.6;
    private const double DefaultPullbackRiskFallback = 0.5;
    private const double DefaultVwapDistanceFallback = 0.3;
    private const double DefaultEvaluationThreshold = 0.5;
    private const double DefaultPatternScoreThreshold = 0.3;
    
    // Breadth features neutral score constants (disabled functionality)
    private const double BreadthNeutralScore = 0.5; // Neutral score for disabled breadth features
    
    private readonly ILogger<ProductionFeatureProbe> _logger;
    private readonly IFeatureBusWithProbe _featureBus;
    private readonly PatternEngine _patternEngine;
    private readonly Zones.IZoneFeatureSource _zoneFeatures;

    public ProductionFeatureProbe(
        ILogger<ProductionFeatureProbe> logger,
        IFeatureBusWithProbe featureBus,
        PatternEngine patternEngine,
        Zones.IZoneFeatureSource zoneFeatures)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureBus = featureBus ?? throw new ArgumentNullException(nameof(featureBus));
        _patternEngine = patternEngine ?? throw new ArgumentNullException(nameof(patternEngine));
        _zoneFeatures = zoneFeatures ?? throw new ArgumentNullException(nameof(zoneFeatures));
    }

    public double Get(string symbol, string key)
    {
        try
        {
            // Route feature requests to appropriate real sources
            return key switch
            {
                // Zone-based features
                string k when k.StartsWith("zone.", StringComparison.OrdinalIgnoreCase) => GetZoneBasedFeature(symbol, key),
                
                // Pattern-based features
                string k when k.StartsWith("pattern.", StringComparison.OrdinalIgnoreCase) => GetPatternBasedFeature(symbol, key),
                
                // Market microstructure features
                string k when IsMarketMicrostructureFeature(k) => GetMarketMicrostructureFeature(symbol, key),
                
                // BREADTH FEATURES INTENTIONALLY DISABLED: Short-circuit to neutral scores
                // Breadth feed subscription not active - return neutral values to avoid risk check bypass
                string k when k.StartsWith("breadth.", StringComparison.OrdinalIgnoreCase) => BreadthNeutralScore,
                
                // Default fallback
                _ => 0.0
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Feature retrieval operation failed for {Key} for {Symbol}", key, symbol);
            return 0.0;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument when retrieving feature {Key} for {Symbol}", key, symbol);
            return 0.0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving feature {Key} for {Symbol}", key, symbol);
            throw new InvalidOperationException($"Feature retrieval failed for {key} on {symbol}", ex);
        }
    }

    private double GetZoneBasedFeature(string symbol, string key)
    {
        return key switch
        {
            "zone.dist_to_demand_atr" => GetZoneFeature(symbol, "dist_to_demand_atr"),
            "zone.dist_to_supply_atr" => GetZoneFeature(symbol, "dist_to_supply_atr"),
            "zone.breakout_score" => GetZoneFeature(symbol, "breakout_score"),
            "zone.pressure" => GetZoneFeature(symbol, "pressure"),
            "zone.test_count" => GetZoneFeature(symbol, "test_count"),
            "zone.dist_to_opposing_atr" => GetZoneFeature(symbol, "dist_to_opposing_atr"),
            _ => 0.0
        };
    }

    private double GetPatternBasedFeature(string symbol, string key)
    {
        return key switch
        {
            "pattern.bull_score" => GetPatternScore(symbol, true),
            "pattern.bear_score" => GetPatternScore(symbol, false),
            _ => 0.0
        };
    }

    private static bool IsMarketMicrostructureFeature(string key)
    {
        return key is "vdc" or "mom.zscore" or "pullback.at_risk" or "climax.volume_thrust" or 
               "inside_bars_lookback" or "vwap.distance_atr" or "keltner.band_touch" or "boll.band_touch";
    }

    private double GetMarketMicrostructureFeature(string symbol, string key)
    {
        return key switch
        {
            "vdc" => _featureBus.Probe(symbol, "volatility.contraction") ?? DefaultVolatilityContractionFallback,
            "mom.zscore" => _featureBus.Probe(symbol, "momentum.zscore") ?? 0.0,
            "pullback.at_risk" => _featureBus.Probe(symbol, "pullback.risk") ?? DefaultPullbackRiskFallback,
            "climax.volume_thrust" => _featureBus.Probe(symbol, "volume.thrust") ?? 1.0,
            "inside_bars_lookback" => _featureBus.Probe(symbol, "inside_bars") ?? 0.0,
            "vwap.distance_atr" => _featureBus.Probe(symbol, "vwap.distance_atr") ?? DefaultVwapDistanceFallback,
            "keltner.band_touch" => _featureBus.Probe(symbol, "keltner.touch") ?? 0.0,
            "boll.band_touch" => _featureBus.Probe(symbol, "bollinger.touch") ?? 0.0,
            _ => 0.0
        };
    }

    private double GetZoneFeature(string symbol, string featureName)
    {
        try
        {
            var zoneFeatures = _zoneFeatures.GetFeatures(symbol);
            return featureName switch
            {
                "dist_to_demand_atr" => zoneFeatures.distToDemandAtr,
                "dist_to_supply_atr" => zoneFeatures.distToSupplyAtr,
                "breakout_score" => zoneFeatures.breakoutScore,
                "pressure" => zoneFeatures.zonePressure,
                "test_count" => GetZoneTestCount(symbol, zoneFeatures), // Calculate or use default
                "dist_to_opposing_atr" => Math.Max(zoneFeatures.distToDemandAtr, zoneFeatures.distToSupplyAtr),
                _ => 0.0
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation getting zone feature {Feature} for {Symbol}", featureName, symbol);
            return 0.0;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument getting zone feature {Feature} for {Symbol}", featureName, symbol);
            return 0.0;
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Zone feature {Feature} not found for {Symbol}", featureName, symbol);
            return 0.0;
        }
    }

    private double GetPatternScore(string symbol, bool bullish)
    {
        try
        {
            // Get pattern scores directly from PatternEngine instead of cascading through feature bus
            var patternScoresTask = _patternEngine.GetCurrentScoresAsync(symbol);
            patternScoresTask.Wait(TimeSpan.FromSeconds(5)); // Wait with timeout
            
            var patternScores = patternScoresTask.Result;
            var score = bullish ? patternScores.BullScore : patternScores.BearScore;
            
            _logger.LogTrace("Pattern score from PatternEngine for {Symbol}, bullish={Bullish}: {Score}", 
                symbol, bullish, score);
            
            return score;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout getting pattern score from PatternEngine for {Symbol}, bullish={Bullish}, falling back to feature bus", 
                symbol, bullish);
                
            // Fallback to feature bus if PatternEngine fails
            try
            {
                return bullish 
                    ? _featureBus.Probe(symbol, "pattern.bull_score") ?? DefaultPatternScoreThreshold
                    : _featureBus.Probe(symbol, "pattern.bear_score") ?? DefaultPatternScoreThreshold;
            }
            catch (InvalidOperationException)
            {
                return DefaultPatternScoreThreshold; // Neutral score as final fallback
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation getting pattern score from PatternEngine for {Symbol}, bullish={Bullish}, falling back to feature bus", 
                symbol, bullish);
                
            // Fallback to feature bus if PatternEngine fails
            try
            {
                return bullish 
                    ? _featureBus.Probe(symbol, "pattern.bull_score") ?? DefaultPatternScoreThreshold
                    : _featureBus.Probe(symbol, "pattern.bear_score") ?? DefaultPatternScoreThreshold;
            }
            catch (InvalidOperationException)
            {
                return DefaultPatternScoreThreshold; // Neutral score as final fallback
            }
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument getting pattern score from PatternEngine for {Symbol}, bullish={Bullish}, falling back to feature bus", 
                symbol, bullish);
                
            // Fallback to feature bus if PatternEngine fails
            return DefaultPatternScoreThreshold; // Use default directly for argument errors
        }
    }
    
    private double GetZoneTestCount(string symbol, (double distToDemandAtr, double distToSupplyAtr, double breakoutScore, double zonePressure) zoneFeatures)
    {
        // Calculate zone test count based on zone pressure and breakout score
        // Higher pressure and breakout scores indicate more tests of the zone
        var pressureComponent = Math.Max(1.0, zoneFeatures.zonePressure * 2.0);
        var breakoutComponent = Math.Max(1.0, zoneFeatures.breakoutScore * 1.5);
        var estimatedTestCount = Math.Min(5.0, pressureComponent + breakoutComponent);
        
        _logger.LogTrace("Calculated zone test count for {Symbol}: {TestCount} (pressure: {Pressure}, breakout: {Breakout})", 
            symbol, estimatedTestCount, zoneFeatures.zonePressure, zoneFeatures.breakoutScore);
        
        return estimatedTestCount;
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
    // Evaluation threshold constants for expression evaluation
    private const double DefaultEvaluationThreshold = 0.5; // Threshold for boolean-like evaluations
    
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

    public async Task<IReadOnlyList<BotCore.Strategy.StrategyRecommendation>> EvaluateAsync(string symbol, DateTime utc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        // Strategy evaluation is synchronous for performance, with async capability available
        await Task.CompletedTask.ConfigureAwait(false);

        var regime = _regimes.GetRegime(symbol);
        var list = new List<BotCore.Strategy.StrategyRecommendation>();

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
                var confluenceEvidence = CreateEvidence(card, symbol, confluenceResults);
                double confidence = CalculateConfidence(confluenceEvidence, card);

                // Create recommendations for both directions unless bias restricts it
                if (!string.Equals(card.Bias, "bearish", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new BotCore.Strategy.StrategyRecommendation(
                        card.Name, 
                        BotCore.Strategy.StrategyIntent.Buy, 
                        confidence, 
                        confluenceEvidence, 
                        card.TelemetryTags.ToArray()));
                }

                if (!string.Equals(card.Bias, "bullish", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new BotCore.Strategy.StrategyRecommendation(
                        card.Name, 
                        BotCore.Strategy.StrategyIntent.Sell, 
                        confidence, 
                        confluenceEvidence, 
                        card.TelemetryTags.ToArray()));
                }

                _logger.LogTrace("Strategy {StrategyName} recommended with confidence {Confidence:F2}", 
                    card.Name, confidence);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation evaluating strategy {StrategyName}", card.Name);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument evaluating strategy {StrategyName}", card.Name);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout evaluating strategy {StrategyName}", card.Name);
            }
        }

        // Return top 5 ranked by confidence
        var ranked = list.OrderByDescending(x => x.Confidence).Take(5).ToList();
        
        _logger.LogDebug("Knowledge graph evaluation complete: {RecommendationCount} recommendations from {StrategyCount} strategies", 
            ranked.Count, _cards.Count);

        return ranked;
    }

    public IReadOnlyList<BotCore.Strategy.StrategyRecommendation> Evaluate(string symbol, DateTime utc)
    {
        return EvaluateAsync(symbol, utc, CancellationToken.None).GetAwaiter().GetResult();
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
                    return featureValue > DefaultEvaluationThreshold; // Treat > 0.5 as true
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation evaluating expression: {Expression}", expression);
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument evaluating expression: {Expression}", expression);
            return false;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Format error evaluating expression: {Expression}", expression);
            return false;
        }
    }

    private IReadOnlyList<BotCore.Strategy.StrategyEvidence> CreateEvidence(DslStrategy card, string symbol, List<string> confluenceResults)
    {
        var evidence = new List<BotCore.Strategy.StrategyEvidence>();

        // Add micro condition evidence
        var microConditions = card.When?.Micro ?? new List<string>();
        foreach (var condition in microConditions)
        {
            if (EvaluateExpression(condition, symbol))
            {
                evidence.Add(new BotCore.Strategy.StrategyEvidence(condition, 1.0, "Micro condition met"));
            }
        }

        // Add confluence evidence
        foreach (var confluence in confluenceResults)
        {
            evidence.Add(new BotCore.Strategy.StrategyEvidence(confluence, 1.0, "Confluence condition met"));
        }

        return evidence;
    }

    private double CalculateConfidence(IReadOnlyList<BotCore.Strategy.StrategyEvidence> evidence, DslStrategy card)
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
/// Production regime service that integrates with real regime detection system
/// Uses RegimeDetectorWithHysteresis for accurate market regime classification
/// </summary>
public sealed class ProductionRegimeService : IRegimeService
{
    private readonly ILogger<ProductionRegimeService> _logger;
    private readonly RegimeDetectorWithHysteresis _regimeDetector;
    private readonly object _lockObject = new();
    private RegimeType _lastRegime = RegimeType.Range;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheTime = TimeSpan.FromSeconds(30);

    public ProductionRegimeService(
        ILogger<ProductionRegimeService> logger,
        RegimeDetectorWithHysteresis regimeDetector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _regimeDetector = regimeDetector ?? throw new ArgumentNullException(nameof(regimeDetector));
    }

    public RegimeType GetRegime(string symbol)
    {
        lock (_lockObject)
        {
            // Use cached regime if recent
            if (DateTime.UtcNow - _lastUpdate < _cacheTime)
            {
                return _lastRegime;
            }

            try
            {
                // Get current regime from detector
                var regimeStateTask = _regimeDetector.DetectCurrentRegimeAsync();
                var regimeState = regimeStateTask.GetAwaiter().GetResult();

                if (regimeState != null)
                {
                    _lastRegime = MapToStrategyRegimeType(regimeState.Type);
                    _lastUpdate = DateTime.UtcNow;

                    _logger.LogTrace("Regime detected for {Symbol}: {Regime} (confidence: {Confidence:F2})", 
                        symbol, _lastRegime, regimeState.Confidence);
                }

                return _lastRegime;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation detecting regime for {Symbol}, using cached value {Regime}", symbol, _lastRegime);
                return _lastRegime;
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout detecting regime for {Symbol}, using cached value {Regime}", symbol, _lastRegime);
                return _lastRegime;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument detecting regime for {Symbol}, using cached value {Regime}", symbol, _lastRegime);
                return _lastRegime;
            }
        }
    }

    private RegimeType MapToStrategyRegimeType(TradingBot.Abstractions.RegimeType detectedRegime)
    {
        return detectedRegime switch
        {
            TradingBot.Abstractions.RegimeType.Range => RegimeType.Range,
            TradingBot.Abstractions.RegimeType.LowVol => RegimeType.LowVol,
            TradingBot.Abstractions.RegimeType.Trend => RegimeType.Trend,
            TradingBot.Abstractions.RegimeType.HighVol => RegimeType.HighVol,
            TradingBot.Abstractions.RegimeType.Volatility => RegimeType.HighVol,
            _ => RegimeType.Range // Default to range if unknown
        };
    }
}