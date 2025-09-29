using System;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotCore.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using TradingBot.IntelligenceStack;
using System.Threading.Tasks;
using System.Linq;

namespace BotCore.Fusion;

/// <summary>
/// Fusion configuration bounds from bounds.json
/// </summary>
public sealed class FusionRails
{
    public double KnowledgeWeight { get; set; } = 0.6;
    public double UcbWeight { get; set; } = 0.4;
    public double MinConfidence { get; set; } = 0.65;
    public int HoldOnDisagree { get; set; } = 1;
    public int ReplayExplore { get; set; }
}

/// <summary>
/// Decision Fusion Coordinator - blends Knowledge Graph recommendations with Neural-UCB and PPO
/// Implements the core fusion logic with disagreement handling and confidence thresholds
/// </summary>
public sealed class DecisionFusionCoordinator
{
    private readonly IStrategyKnowledgeGraph _graph;
    private readonly IUcbStrategyChooser _ucb;
    private readonly IPpoSizer _ppo;
    private readonly IMLConfigurationService _cfg;
    private readonly IMetrics _metrics;
    private readonly ILogger<DecisionFusionCoordinator> _logger;

    public DecisionFusionCoordinator(
        IStrategyKnowledgeGraph graph, 
        IUcbStrategyChooser ucb, 
        IPpoSizer ppo, 
        IMLConfigurationService cfg, 
        IMetrics metrics,
        ILogger<DecisionFusionCoordinator> logger)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _ucb = ucb ?? throw new ArgumentNullException(nameof(ucb));
        _ppo = ppo ?? throw new ArgumentNullException(nameof(ppo));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Core decision fusion logic - blends Knowledge Graph with UCB and applies confidence thresholds
    /// Returns null for hold decisions when confidence is too low or systems disagree
    /// </summary>
    public async Task<BotCore.Strategy.StrategyRecommendation?> DecideAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        try
        {
            var rails = await _cfg.GetFusionRailsAsync(cancellationToken).ConfigureAwait(false);
            
            // Get Knowledge Graph recommendation
            var knowledgeRecommendations = await _graph.EvaluateAsync(symbol, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            var knowledgeRec = knowledgeRecommendations.FirstOrDefault();
            
            // Get UCB prediction
            var (ucbStrategy, ucbIntent, ucbScore) = await _ucb.PredictAsync(symbol, cancellationToken).ConfigureAwait(false);

            // If neither system has a recommendation, hold
            if (knowledgeRec is null && string.IsNullOrEmpty(ucbStrategy))
            {
                _logger.LogTrace("No recommendations from knowledge graph or UCB for {Symbol} - holding", symbol);
                return null;
            }

            // Calculate fusion scores
            double knowledgeScore = knowledgeRec?.Confidence ?? 0.0;
            double combinedScore = (knowledgeScore * rails.KnowledgeWeight) + (ucbScore * rails.UcbWeight);

            // Check minimum confidence threshold
            if (combinedScore < rails.MinConfidence)
            {
                _logger.LogTrace("Combined confidence {Score:F3} below minimum {MinConfidence:F3} for {Symbol} - holding",
                    combinedScore, rails.MinConfidence, symbol);
                
                await _metrics.RecordGaugeAsync("fusion.confidence_too_low", combinedScore, 
                    new Dictionary<string, string> { ["symbol"] = symbol }, cancellationToken).ConfigureAwait(false);
                
                return null;
            }

            // Determine final recommendation - prefer Knowledge Graph if available and high confidence
            StrategyRecommendation finalRec;
            
            if (knowledgeRec != null && knowledgeScore >= rails.MinConfidence)
            {
                // Use Knowledge Graph recommendation
                finalRec = knowledgeRec;
                _logger.LogDebug("Using knowledge graph recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, knowledgeRec.StrategyName, knowledgeScore);
            }
            else if (!string.IsNullOrEmpty(ucbStrategy) && ucbScore >= rails.MinConfidence)
            {
                // Use UCB recommendation
                finalRec = new StrategyRecommendation
                {
                    StrategyName = ucbStrategy,
                    Intent = ucbIntent,
                    Confidence = ucbScore,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow,
                    Evidence = new List<StrategyEvidence>
                    {
                        new StrategyEvidence
                        {
                            Name = "UCB_prediction",
                            Score = ucbScore,
                            Explanation = $"Neural-UCB selected {ucbStrategy} with confidence {ucbScore:F3}"
                        }
                    },
                    TelemetryTags = new List<string> { "ucb_source", "fusion_coordinator" }
                };
                _logger.LogDebug("Using UCB recommendation for {Symbol}: {Strategy} (score: {Score:F3})",
                    symbol, ucbStrategy, ucbScore);
            }
            else
            {
                // Systems disagree or low confidence - hold based on configuration
                if (rails.HoldOnDisagree > 0)
                {
                    _logger.LogTrace("Systems disagree or low individual confidence for {Symbol} - holding per configuration", symbol);
                    return null;
                }
                
                // Fall back to highest scoring system
                finalRec = knowledgeScore > ucbScore ? knowledgeRec : 
                    new StrategyRecommendation
                    {
                        StrategyName = ucbStrategy,
                        Intent = ucbIntent,
                        Confidence = ucbScore,
                        Symbol = symbol,
                        Timestamp = DateTime.UtcNow
                    };
            }

            // Apply PPO position sizing if available
            if (finalRec != null)
            {
                try
                {
                    var baseSize = finalRec.Intent == StrategyIntent.Long ? 1.0 : finalRec.Intent == StrategyIntent.Short ? -1.0 : 0.0;
                    var risk = 1.0 - finalRec.Confidence; // Higher confidence = lower risk
                    
                    var adjustedSize = await _ppo.SizeAsync(baseSize, finalRec.StrategyName, risk, symbol, cancellationToken).ConfigureAwait(false);
                    
                    // Update recommendation with PPO sizing
                    finalRec.AdditionalData ??= new Dictionary<string, object>();
                    finalRec.AdditionalData["ppo_adjusted_size"] = adjustedSize;
                    finalRec.AdditionalData["original_size"] = baseSize;
                    finalRec.AdditionalData["fusion_score"] = combinedScore;
                    
                    _logger.LogDebug("Applied PPO sizing for {Symbol}: {OriginalSize} -> {AdjustedSize}",
                        symbol, baseSize, adjustedSize);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply PPO sizing for {Symbol}, using original recommendation", symbol);
                }
            }

            // Record fusion metrics
            await _metrics.RecordGaugeAsync("fusion.combined_score", combinedScore, 
                new Dictionary<string, string> { ["symbol"] = symbol, ["strategy"] = finalRec?.StrategyName ?? "hold" }, 
                cancellationToken).ConfigureAwait(false);
                
            await _metrics.RecordCounterAsync("fusion.recommendations", 1, 
                new Dictionary<string, string> { ["symbol"] = symbol, ["decision"] = finalRec?.Intent.ToString() ?? "hold" }, 
                cancellationToken).ConfigureAwait(false);

            return finalRec;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fusion decision for {Symbol}", symbol);
            
            await _metrics.RecordCounterAsync("fusion.errors", 1, 
                new Dictionary<string, string> { ["symbol"] = symbol }, 
                cancellationToken).ConfigureAwait(false);
                
            return null;
        }
    }

    /// <summary>
    /// Synchronous wrapper for backward compatibility
    /// </summary>
    public BotCore.Strategy.StrategyRecommendation? Decide(string symbol)
    {
        return DecideAsync(symbol, CancellationToken.None).GetAwaiter().GetResult();
    }
}