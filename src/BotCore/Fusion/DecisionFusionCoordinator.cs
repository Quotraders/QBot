using System;
using BotCore.Strategy;
using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging;

namespace BotCore.Fusion;

/// <summary>
/// UCB strategy chooser interface for Neural-UCB #1 integration
/// </summary>
public interface IUcbStrategyChooser 
{ 
    (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol); 
}

/// <summary>
/// PPO position sizer interface for CVaR-PPO integration
/// </summary>
public interface IPpoSizer 
{ 
    double Size(double baseSize, string strategy, double risk, string symbol); 
}

/// <summary>
/// ML configuration service for fusion bounds and thresholds
/// </summary>
public interface IMLConfigurationService
{
    FusionRails GetFusionRails();
}

/// <summary>
/// Fusion configuration bounds from bounds.json
/// </summary>
public sealed class FusionRails
{
    public double KnowledgeWeight { get; set; } = 0.6;
    public double UcbWeight { get; set; } = 0.4;
    public double MinConfidence { get; set; } = 0.65;
    public int HoldOnDisagree { get; set; } = 1;
    public int ReplayExplore { get; set; } = 0;
}

/// <summary>
/// Metrics interface for telemetry emission
/// </summary>
public interface IMetrics
{
    void Gauge(string name, double value, params (string key, string value)[] tags);
    void IncTagged(string name, int value, params (string key, string value)[] tags);
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
    public BotCore.Strategy.StrategyRecommendation? Decide(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        try
        {
            var rails = _cfg.GetFusionRails();
            
            // Get Knowledge Graph recommendation
            var knowledgeRecommendations = _graph.Evaluate(symbol, DateTime.UtcNow);
            var knowledgeRec = knowledgeRecommendations.FirstOrDefault();
            
            // Get UCB prediction
            var (ucbStrategy, ucbIntent, ucbScore) = _ucb.Predict(symbol);

            // If neither system has a recommendation, hold
            if (knowledgeRec is null && string.IsNullOrEmpty(ucbStrategy))
            {
                _logger.LogTrace("No recommendations from knowledge graph or UCB for {Symbol} - holding", symbol);
                return null;
            }

            // Calculate blended confidence
            var knowledgeScore = knowledgeRec?.Confidence ?? 0;
            double blendedScore = rails.KnowledgeWeight * knowledgeScore + rails.UcbWeight * ucbScore;

            // Check for disagreement
            bool disagree = knowledgeRec != null && !string.IsNullOrEmpty(ucbStrategy) &&
                           !string.Equals(knowledgeRec.StrategyName, ucbStrategy, StringComparison.Ordinal);

            // Emit telemetry
            _metrics.Gauge("fusion.blended", blendedScore, ("sym", symbol));
            _metrics.Gauge("fusion.ucb", ucbScore, ("sym", symbol));
            _metrics.Gauge("fusion.knowledge", knowledgeScore, ("sym", symbol));
            _metrics.IncTagged("fusion.disagree", disagree ? 1 : 0, ("sym", symbol));

            _logger.LogDebug("Fusion evaluation for {Symbol}: Knowledge={KnowledgeScore:F2}, UCB={UcbScore:F2}, Blended={BlendedScore:F2}, Disagree={Disagree}",
                symbol, knowledgeScore, ucbScore, blendedScore, disagree);

            // Apply confidence threshold
            if (blendedScore < rails.MinConfidence)
            {
                _logger.LogTrace("Blended confidence {BlendedScore:F2} below threshold {MinConfidence:F2} for {Symbol} - holding",
                    blendedScore, rails.MinConfidence, symbol);
                return null;
            }

            // Apply disagreement handling (fail-closed unless both align)
            if (disagree && rails.HoldOnDisagree == 1)
            {
                _logger.LogTrace("Knowledge graph and UCB disagree for {Symbol} (Knowledge: {KnowledgeStrategy}, UCB: {UcbStrategy}) - holding",
                    symbol, knowledgeRec?.StrategyName ?? "none", ucbStrategy ?? "none");
                return null;
            }

            // Choose the best recommendation (prefer knowledge graph if available)
            var finalRecommendation = knowledgeRec ?? new BotCore.Strategy.StrategyRecommendation(
                ucbStrategy, 
                ucbIntent, 
                ucbScore, 
                Array.Empty<BotCore.Strategy.StrategyEvidence>(), 
                Array.Empty<string>());

            _logger.LogInformation("Fusion decision for {Symbol}: Strategy={Strategy}, Intent={Intent}, Confidence={Confidence:F2}",
                symbol, finalRecommendation.StrategyName, finalRecommendation.Intent, finalRecommendation.Confidence);

            return finalRecommendation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fusion decision for {Symbol}", symbol);
            return null; // Fail-safe to hold on errors
        }
    }
}

/// <summary>
/// Mock UCB strategy chooser for testing
/// </summary>
public sealed class MockUcbStrategyChooser : IUcbStrategyChooser
{
    public (string Strategy, BotCore.Strategy.StrategyIntent Intent, double Score) Predict(string symbol)
    {
        // Mock UCB predictions for testing
        return ("S6", BotCore.Strategy.StrategyIntent.Long, 0.7);
    }
}

/// <summary>
/// Mock PPO sizer for testing
/// </summary>
public sealed class MockPpoSizer : IPpoSizer
{
    public double Size(double baseSize, string strategy, double risk, string symbol)
    {
        // Simple size calculation based on strategy and risk
        return baseSize * (strategy switch
        {
            "S2" => 1.0,   // Conservative sizing for mean reversion
            "S3" => 0.8,   // Smaller size for breakout plays
            "S6" => 1.2,   // Larger size for momentum
            "S11" => 0.6,  // Very conservative for reversal
            _ => 1.0
        });
    }
}

/// <summary>
/// Mock ML configuration service
/// </summary>
public sealed class MockMLConfigurationService : IMLConfigurationService
{
    public FusionRails GetFusionRails()
    {
        return new FusionRails
        {
            KnowledgeWeight = 0.6,
            UcbWeight = 0.4,
            MinConfidence = 0.65,
            HoldOnDisagree = 1,
            ReplayExplore = 0
        };
    }
}

/// <summary>
/// Mock metrics service
/// </summary>
public sealed class MockMetrics : IMetrics
{
    private readonly ILogger<MockMetrics> _logger;

    public MockMetrics(ILogger<MockMetrics> logger)
    {
        _logger = logger;
    }

    public void Gauge(string name, double value, params (string key, string value)[] tags)
    {
        var tagsStr = string.Join(",", tags.Select(t => $"{t.key}={t.value}"));
        _logger.LogTrace("[METRIC] {Name}={Value:F2} {Tags}", name, value, tagsStr);
    }

    public void IncTagged(string name, int value, params (string key, string value)[] tags)
    {
        var tagsStr = string.Join(",", tags.Select(t => $"{t.key}={t.value}"));
        _logger.LogTrace("[METRIC] {Name}+={Value} {Tags}", name, value, tagsStr);
    }
}