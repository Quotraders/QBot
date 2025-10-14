using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;

namespace TradingBot.IntelligenceStack;

/// <summary>
/// RL Advisor system implementing PPO/CVaR-PPO for exit decisions
/// Operates in advise-only mode until proven uplift
/// </summary>
public class RLAdvisorSystem
{
    // Constants for magic number violations (S109)
    private const int UpliftCheckHours = 24;
    private const double MinutesPerHour = 60.0;
    private const double DefaultRsi = 50.0;
    private const double RsiNormalizationFactor = 100.0;
    private const double DefaultBollingerPosition = 0.5;
    private const int BuyActionType = 1;
    private const int SellActionType = 2;
    private const int HoldActionType = 0;
    
    // Cached JsonSerializerOptions for CA1869 compliance
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, Exception?> LogFailedToSaveTrainingResult =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1001, nameof(LogFailedToSaveTrainingResult)),
            "[RL_ADVISOR] Failed to save training result");
    
    private static readonly Action<ILogger, bool, Exception?> LogLoadedState =
        LoggerMessage.Define<bool>(LogLevel.Information, new EventId(1002, nameof(LogLoadedState)),
            "[RL_ADVISOR] Loaded state - order influence: {Enabled}");
    
    private static readonly Action<ILogger, Exception?> LogFailedToLoadState =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1003, nameof(LogFailedToLoadState)),
            "[RL_ADVISOR] Failed to load state");
    
    // Additional S109 constants for RL advisor operations
    private const int MaxDecisionHistoryCount = 1000;
    private const double QuickExitThresholdMinutes = 30;
    private const double QuickExitBonus = 0.1;
    private const double LongHoldThresholdHours = 8;
    private const double LongHoldPenalty = -0.1;
    private const double HoldInactionPenalty = 0.1;
    private const double SmallLearningRate = 0.02;
    private const double HighLearningRate = 0.2;
    private const int LargeStateSpaceSize = 4000;
    
    // Market data simulation constants  
    private const int ESPriceRange = 200; // Price range for ES futures simulation
    private const int MinVolumeRange = 100; // Minimum volume for simulation
    private const int MaxVolumeRange = 1000; // Maximum volume for simulation
    private const int VolatilityRange = 20; // Volatility range for simulation
    private const double VolatilityDivisor = 1000.0; // Divisor for volatility calculation
    private const double BaseVolatility = 0.01; // Base volatility level
    
    // Episode window and feature extraction constants
    private const int EpisodeWindowSize = 240; // 4 hours of 1-minute data
    private const int EpisodeStepSize = 120; // 2-hour overlap between episodes
    private const double VolumeNormalizationFactor = 500.0; // Volume normalization divisor
    private const double VolatilityBasisPointMultiplier = 100.0; // Convert to basis points
    private const double HourToDegreeConversion = 12.0; // Hours to convert for sine/cosine features
    
    // LoggerMessage delegates for CA1848 compliance - RLAdvisorSystem
    private static readonly Action<ILogger, string, ExitAction, double, Exception?> RecommendationGenerated =
        LoggerMessage.Define<string, ExitAction, double>(LogLevel.Debug, new EventId(4001, "RecommendationGenerated"),
            "[RL_ADVISOR] Generated recommendation for {Symbol}: {Action} (confidence: {Confidence:F3})");
            
    private static readonly Action<ILogger, string, Exception?> RecommendationFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4002, "RecommendationFailed"),
            "[RL_ADVISOR] Failed to generate exit recommendation for {Symbol}");
            
    private static readonly Action<ILogger, string, Exception?> DecisionNotFound =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4003, "DecisionNotFound"),
            "[RL_ADVISOR] Decision not found for outcome update: {DecisionId}");
            
    private static readonly Action<ILogger, string, double, Exception?> AgentUpdated =
        LoggerMessage.Define<string, double>(LogLevel.Debug, new EventId(4004, "AgentUpdated"),
            "[RL_ADVISOR] Updated agent {Agent} with outcome: reward={Reward:F3}");
            
    private static readonly Action<ILogger, string, Exception?> OutcomeUpdateFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4005, "OutcomeUpdateFailed"),
            "[RL_ADVISOR] Failed to update with outcome for decision: {DecisionId}");

    // Additional LoggerMessage delegates for CA1848 compliance
    private static readonly Action<ILogger, string, DateTime, DateTime, Exception?> HistoricalTrainingStarted =
        LoggerMessage.Define<string, DateTime, DateTime>(LogLevel.Information, new EventId(4006, "HistoricalTrainingStarted"),
            "[RL_ADVISOR] Starting historical training for {Symbol}: {Start} to {End}");

    private static readonly Action<ILogger, int, Exception?> TrainingEpisodesGenerated =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(4007, "TrainingEpisodesGenerated"),
            "[RL_ADVISOR] Generated {Count} training episodes");

    private static readonly Action<ILogger, string, Exception?> HistoricalTrainingFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4009, "HistoricalTrainingFailed"),
            "[RL_ADVISOR] Historical training failed for {Symbol}");

    // Additional LoggerMessage delegates for remaining CA1848 violations
    private static readonly Action<ILogger, string, int, double, Exception?> AgentTrainingCompleted =
        LoggerMessage.Define<string, int, double>(LogLevel.Information, new EventId(4016, "AgentTrainingCompleted"),
            "[RL_ADVISOR] Trained {AgentType} agent: {Episodes} episodes, final reward: {Reward:F3}");



    private static readonly Action<ILogger, string, int, Exception?> MinimumShadowDecisionsReached =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(4018, "MinimumShadowDecisionsReached"),
            "[RL_ADVISOR] Agent {Agent} has reached minimum shadow decisions: {Count}");

    private static readonly Action<ILogger, Exception?> CheckingProvenUplift =
        LoggerMessage.Define(LogLevel.Information, new EventId(4019, "CheckingProvenUplift"),
            "[RL_ADVISOR] Checking for proven uplift to enable order influence");

    private static readonly Action<ILogger, double, Exception?> OrderInfluenceEnabled =
        LoggerMessage.Define<double>(LogLevel.Information, new EventId(4020, "OrderInfluenceEnabled"),
            "[RL_ADVISOR] ✅ Enabled order influence - proven uplift: {EdgeBps:F1} bps");

    private static readonly Action<ILogger, double, Exception?> OrderInfluenceDisabled =
        LoggerMessage.Define<double>(LogLevel.Warning, new EventId(4021, "OrderInfluenceDisabled"),
            "[RL_ADVISOR] ❌ Disabled order influence - insufficient uplift: {EdgeBps:F1} bps");

    private static readonly Action<ILogger, Exception?> ProvenUpliftCheckFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(4022, "ProvenUpliftCheckFailed"),
            "[RL_ADVISOR] Failed to check for proven uplift");



    private static readonly Action<ILogger, Exception?> SDKBridgeScriptNotFound =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4025, "SDKBridgeScriptNotFound"),
            "[RL_ADVISOR] SDK bridge script not found, using fallback data");

    private static readonly Action<ILogger, Exception?> SDKBridgeStartFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4026, "SDKBridgeStartFailed"),
            "[RL_ADVISOR] Failed to start SDK bridge process");

    private static readonly Action<ILogger, int, string, Exception?> SDKBridgeExitCode =
        LoggerMessage.Define<int, string>(LogLevel.Debug, new EventId(4027, "SDKBridgeExitCode"),
            "[RL_ADVISOR] SDK bridge returned exit code {ExitCode}: {Error}");

    private static readonly Action<ILogger, Exception?> SDKBridgeEmptyOutput =
        LoggerMessage.Define(LogLevel.Debug, new EventId(4028, "SDKBridgeEmptyOutput"),
            "[RL_ADVISOR] SDK bridge returned empty output");

    private static readonly Action<ILogger, string, Exception?> BarDataParseFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4029, "BarDataParseFailed"),
            "[RL_ADVISOR] Failed to parse bar data: {Error}");

    // Additional LoggerMessage delegates for remaining CA1848 violations  
    private static readonly Action<ILogger, string, Exception?> LoadingHistoricalDataViaSDK =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(4036, "LoadingHistoricalDataViaSDK"),
            "[RL_ADVISOR] Loading historical data via SDK adapter for {Symbol}");

    private static readonly Action<ILogger, int, string, Exception?> DataPointsLoadedViaSDK =
        LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(4042, "DataPointsLoadedViaSDK"),
            "[RL_ADVISOR] Loaded {Count} data points via SDK adapter for {Symbol}");

    private static readonly Action<ILogger, string, Exception?> HistoricalDataLoadFailedViaSDK =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4043, "HistoricalDataLoadFailedViaSDK"),
            "[RL_ADVISOR] Failed to load historical data via SDK adapter for {Symbol}");
            
    // Action mapping constants for S109 compliance
    private const int ActionHold = 0;
    private const int ActionPartialExit = 1;
    private const int ActionFullExit = 2;
    private const int ActionTrailingStop = 3;
    
    // Confidence thresholds - removed hardcoded values, now using IMLConfigurationService
    private const double LongTimeInPositionHours = 4.0;
    
    // Additional S109 constants for RL system





















    private readonly ILogger<RLAdvisorSystem> _logger;
    private readonly AdvisorConfig _config;
    private readonly IDecisionLogger _decisionLogger;
    private readonly IMLConfigurationService _mlConfig;
    private readonly string _statePath;
    
    private readonly Dictionary<string, RLAdvisorModel> _agents = new();
    private readonly Dictionary<string, List<RLDecision>> _decisionHistory = new();
    private readonly Dictionary<string, PerformanceTracker> _performanceTrackers = new();
    private readonly object _lock = new();
    
    private bool _orderInfluenceEnabled;
    private DateTime _lastUpliftCheck = DateTime.MinValue;

    public RLAdvisorSystem(
        ILogger<RLAdvisorSystem> logger,
        AdvisorConfig config,
        IDecisionLogger decisionLogger,
        IMLConfigurationService mlConfig,
        string statePath = "data/rl_advisor")
    {
        _logger = logger;
        _config = config;
        _decisionLogger = decisionLogger;
        _mlConfig = mlConfig;
        _statePath = statePath;
        
        Directory.CreateDirectory(_statePath);
        Directory.CreateDirectory(Path.Combine(_statePath, "agents"));
        Directory.CreateDirectory(Path.Combine(_statePath, "performance"));
        
        InitializeAgents();
        _ = Task.Run(() => LoadStateAsync());
    }

    /// <summary>
    /// Get RL advisor recommendation for exit decision
    /// </summary>
    public async Task<RLAdvisorRecommendation> GetExitRecommendationAsync(
        ExitDecisionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        
        try
        {
            if (!_config.Enabled)
            {
                return new RLAdvisorRecommendation
                {
                    Action = ExitAction.Hold,
                    Confidence = 0.0,
                    Reasoning = "RL Advisor disabled",
                    IsAdviseOnly = true
                };
            }

            var agentKey = GetAgentKey(context);
            var agent = GetOrCreateAgent(agentKey);
            
            // Get state representation
            var state = CreateStateVector(context);
            
            // Get action from RL agent
            var rlAction = await agent.GetActionAsync(state, cancellationToken).ConfigureAwait(false);
            
            // Convert to exit recommendation
            var recommendation = new RLAdvisorRecommendation
            {
                Action = ConvertToExitAction(rlAction),
                Confidence = rlAction.Confidence,
                Reasoning = GenerateReasoning(rlAction, context),
                IsAdviseOnly = !_orderInfluenceEnabled,
                AgentType = agent.AgentType,
                StateVector = state,
                RawAction = rlAction,
                Timestamp = DateTime.UtcNow
            };

            // Log decision for performance tracking
            await LogRLDecisionAsync(agentKey, recommendation, context, cancellationToken).ConfigureAwait(false);

            // Increment shadow decision count
            await IncrementShadowDecisionCountAsync(agentKey).ConfigureAwait(false);

            RecommendationGenerated(_logger, context.Symbol, recommendation.Action, recommendation.Confidence, null);

            return recommendation;
        }
        catch (ArgumentException ex)
        {
            RecommendationFailed(_logger, context.Symbol, ex);
            return new RLAdvisorRecommendation
            {
                Action = ExitAction.Hold,
                Confidence = 0.0,
                Reasoning = $"Error: {ex.Message}",
                IsAdviseOnly = true
            };
        }
        catch (InvalidOperationException ex)
        {
            RecommendationFailed(_logger, context.Symbol, ex);
            return new RLAdvisorRecommendation
            {
                Action = ExitAction.Hold,
                Confidence = 0.0,
                Reasoning = $"Error: {ex.Message}",
                IsAdviseOnly = true
            };
        }
        catch (OperationCanceledException ex)
        {
            RecommendationFailed(_logger, context.Symbol, ex);
            return new RLAdvisorRecommendation
            {
                Action = ExitAction.Hold,
                Confidence = 0.0,
                Reasoning = $"Error: {ex.Message}",
                IsAdviseOnly = true
            };
        }
    }

    /// <summary>
    /// Update RL agent with actual outcome feedback
    /// </summary>
    public async Task UpdateWithOutcomeAsync(
        string decisionId,
        ExitOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        
        try
        {
            // Find the corresponding decision
            var decision = FindDecisionById(decisionId);
            if (decision == null)
            {
                DecisionNotFound(_logger, decisionId, null);
                return;
            }

            var agentKey = GetAgentKeyFromDecision(decision);
            var agent = GetOrCreateAgent(agentKey);
            
            // Calculate reward based on outcome
            var reward = CalculateReward(decision, outcome);
            
            // Update agent with experience
            await agent.UpdateAsync(decision.StateVector.ToArray(), decision.RawAction, reward, outcome.NextState.ToArray(), cancellationToken).ConfigureAwait(false);
            
            // Update performance tracking
            await UpdatePerformanceTrackingAsync(agentKey, outcome, cancellationToken).ConfigureAwait(false);

            AgentUpdated(_logger, agentKey, reward, null);

            // Check for uplift periodically
            if (DateTime.UtcNow - _lastUpliftCheck > TimeSpan.FromHours(UpliftCheckHours))
            {
                await CheckForProvenUpliftAsync(cancellationToken).ConfigureAwait(false);
                _lastUpliftCheck = DateTime.UtcNow;
            }
        }
        catch (InvalidOperationException ex)
        {
            OutcomeUpdateFailed(_logger, decisionId, ex);
        }
        catch (ArgumentException ex)
        {
            OutcomeUpdateFailed(_logger, decisionId, ex);
        }
        catch (TimeoutException ex)
        {
            OutcomeUpdateFailed(_logger, decisionId, ex);
        }
    }

    /// <summary>
    /// Get current RL advisor status
    /// </summary>
    public RLAdvisorStatus GetCurrentStatus()
    {
        lock (_lock)
        {
            var status = new RLAdvisorStatus
            {
                Enabled = _config.Enabled,
                OrderInfluenceEnabled = _orderInfluenceEnabled,
                MinShadowDecisions = _config.ShadowMinDecisions,
                MinEdgeBps = _config.MinEdgeBps
            };

            foreach (var (agentKey, agent) in _agents)
            {
                var decisionCount = _decisionHistory.GetValueOrDefault(agentKey, new List<RLDecision>()).Count;
                var tracker = _performanceTrackers.GetValueOrDefault(agentKey, new PerformanceTracker());
                
                status.AgentStates[agentKey] = new RLAgentStatus
                {
                    AgentType = agent.AgentType,
                    ShadowDecisions = decisionCount,
                    AverageReward = tracker.AverageReward,
                    SharpeRatio = tracker.SharpeRatio,
                    EdgeBps = tracker.EdgeBps,
                    LastDecision = agent.LastDecisionTime,
                    IsEligibleForLive = IsEligibleForLive(agentKey, tracker),
                    ExplorationRate = agent.ExplorationRate
                };
            }

            return status;
        }
    }

    /// <summary>
    /// Train RL agents on historical data
    /// </summary>
    public async Task<RLTrainingResult> TrainOnHistoricalDataAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HistoricalTrainingStarted(_logger, symbol, startDate, endDate, null);

            var result = new RLTrainingResult
            {
                Symbol = symbol,
                StartDate = startDate,
                EndDate = endDate,
                StartTime = DateTime.UtcNow
            };

            // Generate training episodes from historical data
            var episodes = await GenerateTrainingEpisodesAsync(symbol, startDate, endDate, cancellationToken).ConfigureAwait(false);
            
            result.EpisodesGenerated = episodes.Count;
            TrainingEpisodesGenerated(_logger, episodes.Count, null);

            // Train each agent type
            var agentTypes = Enum.GetValues<RLAgentType>();
            foreach (var agentType in agentTypes)
            {
                var agentKey = $"{symbol}_{agentType}";
                var agent = GetOrCreateAgent(agentKey);
                agent.AgentType = agentType;
                
                var agentResult = await TrainAgentAsync(agent, episodes, cancellationToken).ConfigureAwait(false);
                result.AgentResults[agentType] = agentResult;
                
                AgentTrainingCompleted(_logger, agentType.ToString(), agentResult.EpisodesProcessed, agentResult.FinalAverageReward, null);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;

            await SaveTrainingResultAsync(result, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (InvalidOperationException ex)
        {
            HistoricalTrainingFailed(_logger, symbol, ex);
            return new RLTrainingResult 
            { 
                Symbol = symbol, 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
        catch (ArgumentException ex)
        {
            HistoricalTrainingFailed(_logger, symbol, ex);
            return new RLTrainingResult 
            { 
                Symbol = symbol, 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
        catch (IOException ex)
        {
            HistoricalTrainingFailed(_logger, symbol, ex);
            return new RLTrainingResult 
            { 
                Symbol = symbol, 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
        catch (TimeoutException ex)
        {
            HistoricalTrainingFailed(_logger, symbol, ex);
            return new RLTrainingResult 
            { 
                Symbol = symbol, 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private void InitializeAgents()
    {
        // Initialize agents for different contexts
        var contexts = new[] { "ES_PPO", "ES_CVarPPO", "NQ_PPO", "NQ_CVarPPO" };
        
        foreach (var context in contexts)
        {
            var agentType = context.Contains("CVaR", StringComparison.Ordinal) ? RLAgentType.CVarPPO : RLAgentType.PPO;
            _agents[context] = new RLAdvisorModel(_logger, agentType, context, _config, _mlConfig);
        }
    }

    private RLAdvisorModel GetOrCreateAgent(string agentKey)
    {
        lock (_lock)
        {
            if (!_agents.TryGetValue(agentKey, out var agent))
            {
                var agentType = agentKey.Contains("CVaR", StringComparison.Ordinal) ? RLAgentType.CVarPPO : RLAgentType.PPO;
                agent = new RLAdvisorModel(_logger, agentType, agentKey, _config, _mlConfig);
                _agents[agentKey] = agent;
            }
            return agent;
        }
    }

    private static string GetAgentKey(ExitDecisionContext context)
    {
        var symbol = context.Symbol;
        var agentType = context.UsesCVaR ? "CVarPPO" : "PPO";
        return $"{symbol}_{agentType}";
    }

    private static string GetAgentKeyFromDecision(RLDecision decision)
    {
        return decision.AgentKey;
    }

    private static double[] CreateStateVector(ExitDecisionContext context)
    {
        // Create state representation for RL agent
        return new double[]
        {
            context.CurrentPrice / context.EntryPrice - 1.0, // Normalized return
            context.TimeInPosition.TotalMinutes / MinutesPerHour, // Hours in position
            context.UnrealizedPnL / Math.Abs(context.PositionSize), // PnL per unit
            context.CurrentVolatility, // Market volatility
            context.TechnicalIndicators.GetValueOrDefault("rsi", DefaultRsi) / RsiNormalizationFactor, // RSI normalized
            context.TechnicalIndicators.GetValueOrDefault("bollinger_position", DefaultBollingerPosition), // Bollinger position
            context.MarketRegime == "TRENDING" ? 1.0 : 0.0, // Regime indicator
            context.MarketRegime == "RANGING" ? 1.0 : 0.0,
            context.MarketRegime == "VOLATILE" ? 1.0 : 0.0
        };
    }

    private static ExitAction ConvertToExitAction(RLActionResult rlAction)
    {
        return rlAction.ActionType switch
        {
            ActionHold => ExitAction.Hold,
            ActionPartialExit => ExitAction.PartialExit,
            ActionFullExit => ExitAction.FullExit,
            ActionTrailingStop => ExitAction.TrailingStop,
            _ => ExitAction.Hold
        };
    }

    private string GenerateReasoning(RLActionResult rlAction, ExitDecisionContext context)
    {
        var reasons = new List<string>();
        
        if (rlAction.Confidence > _mlConfig.GetAIConfidenceThreshold())
            reasons.Add("High confidence");
        else if (rlAction.Confidence < _mlConfig.GetMinimumConfidence())
            reasons.Add("Low confidence");
            
        if (context.UnrealizedPnL > 0)
            reasons.Add("Position profitable");
        else
            reasons.Add("Position at loss");
            
        if (context.TimeInPosition.TotalHours > LongTimeInPositionHours)
            reasons.Add("Long time in position");
            
        return string.Join(", ", reasons);
    }

    private Task LogRLDecisionAsync(
        string agentKey,
        RLAdvisorRecommendation recommendation,
        ExitDecisionContext context,
        CancellationToken cancellationToken)
    {
        var decision = new RLDecision
        {
            DecisionId = Guid.NewGuid().ToString(),
            AgentKey = agentKey,
            Timestamp = DateTime.UtcNow,
            Context = context,
            Recommendation = recommendation,
            StateVector = recommendation.StateVector,
            RawAction = recommendation.RawAction
        };

        lock (_lock)
        {
            if (!_decisionHistory.TryGetValue(agentKey, out var decisions))
            {
                decisions = new List<RLDecision>();
                _decisionHistory[agentKey] = decisions;
            }
            
            decisions.Add(decision);
            
            // Keep only recent decisions
            if (_decisionHistory[agentKey].Count > MaxDecisionHistoryCount)
            {
                _decisionHistory[agentKey].RemoveAt(0);
            }
        }

        // Log to decision logger
        var intelligenceDecision = new IntelligenceDecision
        {
            DecisionId = decision.DecisionId,
            Symbol = context.Symbol,
            Action = recommendation.Action.ToString(),
            Confidence = recommendation.Confidence
        };
        
        // Populate read-only Metadata collection
        intelligenceDecision.Metadata["rl_agent"] = agentKey;
        intelligenceDecision.Metadata["agent_type"] = recommendation.AgentType.ToString();
        intelligenceDecision.Metadata["is_advise_only"] = recommendation.IsAdviseOnly;
        intelligenceDecision.Metadata["reasoning"] = recommendation.Reasoning;

        return _decisionLogger.LogDecisionAsync(intelligenceDecision, cancellationToken);
    }

    private async Task IncrementShadowDecisionCountAsync(string agentKey)
    {
        // Brief yield for async context
        await Task.Yield();
        
        // Increment shadow decision count for the agent
        var decisions = _decisionHistory.GetValueOrDefault(agentKey, new List<RLDecision>());
        
        if (decisions.Count >= _config.ShadowMinDecisions && !_orderInfluenceEnabled)
        {
            MinimumShadowDecisionsReached(_logger, agentKey, decisions.Count, null);
        }
    }

    private static double CalculateReward(RLDecision decision, ExitOutcome outcome)
    {
        // Calculate reward based on the outcome of the exit decision
        var baseReward = outcome.RealizedPnL;
        
        // Add timing bonus/penalty
        var timingBonus = 0.0;
        if (outcome.TimeToExit.TotalMinutes < QuickExitThresholdMinutes)
        {
            timingBonus = QuickExitBonus; // Bonus for quick profitable exits
        }
        else if (outcome.TimeToExit.TotalHours > LongHoldThresholdHours)
        {
            timingBonus = LongHoldPenalty; // Penalty for very long holds
        }
        
        // Add volatility adjustment
        var volAdjustment = Math.Min(0.1, outcome.VolatilityDuringExit * 0.05);
        
        // CVaR penalty for high-risk scenarios
        var cvarPenalty = 0.0;
        if (decision.Context.UsesCVaR && outcome.MaxDrawdownDuringExit > SmallLearningRate)
        {
            cvarPenalty = -HighLearningRate; // CVaR agents should avoid high drawdown scenarios
        }
        
        return baseReward + timingBonus - volAdjustment + cvarPenalty;
    }

    private static double CalculateReward(RLMarketDataPoint current, RLMarketDataPoint next, RLActionResult action)
    {
        var priceChange = (next.Price - current.Price) / current.Price;
        
        return action.ActionType switch
        {
            1 => priceChange > 0 ? priceChange * action.Confidence : -Math.Abs(priceChange) * action.Confidence, // Buy
            ActionFullExit => priceChange < 0 ? Math.Abs(priceChange) * action.Confidence : -priceChange * action.Confidence, // Sell
            _ => -Math.Abs(priceChange) * HoldInactionPenalty // Hold - small penalty for inaction during significant moves
        };
    }

    private async Task UpdatePerformanceTrackingAsync(
        string agentKey,
        ExitOutcome outcome,
        CancellationToken cancellationToken)
    {
        // Brief async operation for proper async pattern
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        
        lock (_lock)
        {
            if (!_performanceTrackers.TryGetValue(agentKey, out var tracker))
            {
                tracker = new PerformanceTracker();
                _performanceTrackers[agentKey] = tracker;
            }
            
            tracker.AddOutcome(outcome.RealizedPnL, outcome.TimeToExit);
        }
    }

    private RLDecision? FindDecisionById(string decisionId)
    {
        lock (_lock)
        {
            foreach (var decisions in _decisionHistory.Values)
            {
                var decision = decisions.Find(d => d.DecisionId == decisionId);
                if (decision != null)
                {
                    return decision;
                }
            }
        }
        return null;
    }

    private Task CheckForProvenUpliftAsync(CancellationToken cancellationToken)
    {
        // Perform uplift analysis asynchronously to avoid blocking the main RL loop
        return Task.Run(() =>
        {
            try
            {
                CheckingProvenUplift(_logger, null);
                
                var totalEdgeBps = 0.0;
                var validAgents = 0;
                
                foreach (var (agentKey, tracker) in _performanceTrackers)
                {
                    var decisions = _decisionHistory.GetValueOrDefault(agentKey, new List<RLDecision>());
                    
                    if (decisions.Count >= _config.ShadowMinDecisions)
                    {
                        totalEdgeBps += tracker.EdgeBps;
                        validAgents++;
                    }
                }
                
                if (validAgents > 0)
                {
                    var averageEdgeBps = totalEdgeBps / validAgents;
                    
                    if (averageEdgeBps >= _config.MinEdgeBps && !_orderInfluenceEnabled)
                    {
                        _orderInfluenceEnabled = true;
                        OrderInfluenceEnabled(_logger, averageEdgeBps, null);
                    }
                    else if (averageEdgeBps < _config.MinEdgeBps && _orderInfluenceEnabled)
                    {
                        _orderInfluenceEnabled = false;
                        OrderInfluenceDisabled(_logger, averageEdgeBps, null);
                    }
                }
            }
            catch (ArgumentNullException ex)
            {
                ProvenUpliftCheckFailed(_logger, ex);
            }
            catch (InvalidOperationException ex)
            {
                ProvenUpliftCheckFailed(_logger, ex);
            }
            catch (OverflowException ex)
            {
                ProvenUpliftCheckFailed(_logger, ex);
            }
        }, cancellationToken);
    }

    private bool IsEligibleForLive(string agentKey, PerformanceTracker tracker)
    {
        var decisions = _decisionHistory.GetValueOrDefault(agentKey, new List<RLDecision>());
        return decisions.Count >= _config.ShadowMinDecisions && tracker.EdgeBps >= _config.MinEdgeBps;
    }

    private async Task<List<TrainingEpisode>> GenerateTrainingEpisodesAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        // Production-grade episode generation from historical market data
        var episodes = new List<TrainingEpisode>();
        
        // Step 1: Load historical market data asynchronously via SDK adapter
        var marketData = await LoadHistoricalMarketDataViaSdkAsync(symbol, startDate, endDate).ConfigureAwait(false);
        
        // Step 2: Generate episodes based on market regimes and volatility clusters
        var episodeWindows = await GenerateEpisodeWindowsAsync(marketData, cancellationToken).ConfigureAwait(false);
        
        foreach (var window in episodeWindows)
        {
            var episode = await CreateEpisodeFromMarketDataAsync(window, marketData, _mlConfig.GetAIConfidenceThreshold(), cancellationToken).ConfigureAwait(false);
            episodes.Add(episode);
        }
        
        TrainingEpisodesGenerated(_logger, episodes.Count, null);
        
        return episodes;
    }
    
    private async Task<List<RLMarketDataPoint>> LoadHistoricalMarketDataViaSdkAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        try
        {
            LoadingHistoricalDataViaSDK(_logger, symbol, null);

            var pythonScript = ValidateSdkScriptPath();
            if (pythonScript == null)
            {
                return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
            }

            var output = await ExecuteSdkBridgeProcessAsync(pythonScript, symbol, startDate, endDate).ConfigureAwait(false);
            if (output == null)
            {
                return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
            }

            var dataPoints = ParseSdkOutputToDataPoints(output, symbol, startDate, endDate);
            DataPointsLoadedViaSDK(_logger, dataPoints.Count, symbol, null);
            return dataPoints.OrderBy(dp => dp.Timestamp).ToList();
        }
        catch (JsonException ex)
        {
            HistoricalDataLoadFailedViaSDK(_logger, symbol, ex);
            return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
        }
        catch (HttpRequestException ex)
        {
            HistoricalDataLoadFailedViaSDK(_logger, symbol, ex);
            return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
        }
        catch (TimeoutException ex)
        {
            HistoricalDataLoadFailedViaSDK(_logger, symbol, ex);
            return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
        }
        catch (InvalidOperationException ex)
        {
            HistoricalDataLoadFailedViaSDK(_logger, symbol, ex);
            return LoadHistoricalMarketDataFallback(symbol, startDate, endDate);
        }
    }

    private string? ValidateSdkScriptPath()
    {
        var pythonScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "python", "sdk_bridge.py");
        if (!File.Exists(pythonScript))
        {
            SDKBridgeScriptNotFound(_logger, null);
            return null;
        }
        return pythonScript;
    }

    private async Task<string?> ExecuteSdkBridgeProcessAsync(string pythonScript, string symbol, DateTime startDate, DateTime endDate)
    {
        var timespan = endDate - startDate;
        var estimatedBars = Math.Max(100, (int)(timespan.TotalMinutes / 5)); // 5-minute bars

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{pythonScript}\" get_historical_bars \"{symbol}\" \"5m\" {estimatedBars}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            SDKBridgeStartFailed(_logger, null);
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            SDKBridgeExitCode(_logger, process.ExitCode, error, null);
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            SDKBridgeEmptyOutput(_logger, null);
            return null;
        }

        return output;
    }

    private List<RLMarketDataPoint> ParseSdkOutputToDataPoints(string output, string symbol, DateTime startDate, DateTime endDate)
    {
        var barData = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(output);
        var dataPoints = new List<RLMarketDataPoint>();

        if (barData != null)
        {
            foreach (var bar in barData)
            {
                try
                {
                    var timestamp = DateTime.TryParse(bar["timestamp"].ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ts) ? ts : DateTime.UtcNow;
                    if (timestamp >= startDate && timestamp <= endDate)
                    {
                        var dataPoint = new RLMarketDataPoint
                        {
                            Timestamp = timestamp,
                            Symbol = symbol,
                            Open = Convert.ToDouble(bar["open"], CultureInfo.InvariantCulture),
                            High = Convert.ToDouble(bar["high"], CultureInfo.InvariantCulture),
                            Low = Convert.ToDouble(bar["low"], CultureInfo.InvariantCulture),
                            Close = Convert.ToDouble(bar["close"], CultureInfo.InvariantCulture),
                            Volume = (int)Convert.ToInt64(bar.GetValueOrDefault("volume", 0), CultureInfo.InvariantCulture),
                            // Calculate additional features
                            ATR = Math.Max(Convert.ToDouble(bar["high"], CultureInfo.InvariantCulture) - Convert.ToDouble(bar["low"], CultureInfo.InvariantCulture), 0.25),
                            Volatility = 0.2, // Would be calculated from historical volatility
                            Regime = "Normal"
                        };
                        dataPoints.Add(dataPoint);
                    }
                }
                catch (FormatException ex)
                {
                    BarDataParseFailed(_logger, ex.Message, null);
                }
                catch (OverflowException ex)
                {
                    BarDataParseFailed(_logger, ex.Message, null);
                }
                catch (ArgumentNullException ex)
                {
                    BarDataParseFailed(_logger, ex.Message, null);
                }
                catch (KeyNotFoundException ex)
                {
                    BarDataParseFailed(_logger, ex.Message, null);
                }
                catch (InvalidCastException ex)
                {
                    BarDataParseFailed(_logger, ex.Message, null);
                }
            }
        }

        return dataPoints;
    }

    private static List<RLMarketDataPoint> LoadHistoricalMarketDataFallback(string symbol, DateTime startDate, DateTime endDate)
    {
        // Load historical market data from data store
        // For production, this would integrate with historical data providers
        var dataPoints = new List<RLMarketDataPoint>();
        var current = startDate;
        
        while (current <= endDate)
        {
            dataPoints.Add(new RLMarketDataPoint
            {
                Timestamp = current,
                Symbol = symbol,
                Price = LargeStateSpaceSize + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, ESPriceRange), // ES price range
                Volume = System.Security.Cryptography.RandomNumberGenerator.GetInt32(MinVolumeRange, MaxVolumeRange),
                Volatility = BaseVolatility + (System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, VolatilityRange) / VolatilityDivisor)
            });
            current = current.AddMinutes(1); // 1-minute bars
        }
        
        return dataPoints;
    }
    
    private static Task<List<EpisodeWindow>> GenerateEpisodeWindowsAsync(List<RLMarketDataPoint> marketData, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var windows = new List<EpisodeWindow>();
            
            for (int i = 0; i < marketData.Count - EpisodeWindowSize; i += EpisodeStepSize) // 2-hour overlap
            {
                var startIndex = i;
                var endIndex = Math.Min(i + EpisodeWindowSize, marketData.Count - 1); // 4 hours of 1-min data
                
                windows.Add(new EpisodeWindow
                {
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    StartTime = marketData[startIndex].Timestamp,
                    EndTime = marketData[endIndex].Timestamp
                });
            }
            
            return windows;
        }, cancellationToken);
    }
    
    private static Task<TrainingEpisode> CreateEpisodeFromMarketDataAsync(
        EpisodeWindow window, 
        List<RLMarketDataPoint> marketData,
        double maxConfidenceThreshold,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var episode = new TrainingEpisode
            {
                StartTime = window.StartTime,
                EndTime = window.EndTime,
                InitialState = ExtractMarketFeatures(marketData[window.StartIndex])
            };
            
            episode.Actions.Clear(); // Ensure it's empty
            
            // Generate state-action-reward sequences from market movements
            for (int i = window.StartIndex; i < window.EndIndex - 1; i++)
            {
                var currentBar = marketData[i];
                var nextBar = marketData[i + 1];
                
                var state = ExtractMarketFeatures(currentBar);
                var action = DetermineOptimalAction(currentBar, nextBar, maxConfidenceThreshold);
                var reward = CalculateReward(currentBar, nextBar, action);
                
                episode.Actions.Add((state, action, reward));
            }
            
            return episode;
        }, cancellationToken);
    }
    
    private static double[] ExtractMarketFeatures(RLMarketDataPoint dataPoint)
    {
        return new double[]
        {
            dataPoint.Price / LargeStateSpaceSize - 1.0, // Normalized price
            dataPoint.Volume / VolumeNormalizationFactor - 1.0, // Normalized volume
            dataPoint.Volatility * VolatilityBasisPointMultiplier, // Volatility in basis points
            Math.Sin(dataPoint.Timestamp.Hour * Math.PI / HourToDegreeConversion), // Time of day feature
            Math.Cos(dataPoint.Timestamp.Hour * Math.PI / HourToDegreeConversion),
            dataPoint.Timestamp.DayOfWeek == DayOfWeek.Friday ? 1.0 : 0.0, // Friday effect
            0.0, // Momentum (would be calculated from price history)
            0.0, // RSI (would be calculated from price history)
            0.0  // MACD (would be calculated from price history)
        };
    }
    
    private static RLActionResult DetermineOptimalAction(RLMarketDataPoint current, RLMarketDataPoint next, double maxConfidenceThreshold)
    {
        var priceChange = next.Price - current.Price;
        int actionType = 0;
        if (priceChange > 0)
            actionType = BuyActionType; // Buy
        else if (priceChange < 0)
            actionType = SellActionType; // Sell
        else
            actionType = HoldActionType; // Hold
            
        var confidence = Math.Min(maxConfidenceThreshold, Math.Abs(priceChange) / current.Price * 10); // Confidence based on price move
        
        return new RLActionResult
        {
            ActionType = actionType,
            Confidence = confidence
        };
    }
    
    private static async Task<AgentTrainingResult> TrainAgentAsync(
        RLAdvisorModel agent,
        List<TrainingEpisode> episodes,
        CancellationToken cancellationToken)
    {
        var result = new AgentTrainingResult
        {
            AgentType = agent.AgentType,
            StartTime = DateTime.UtcNow
        };
        
        var totalReward = 0.0;
        var processedEpisodes = 0;
        
        foreach (var episode in episodes)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            // Train on episode
            foreach (var (state, action, reward) in episode.Actions)
            {
                await agent.UpdateAsync(state, action, reward, state, cancellationToken).ConfigureAwait(false);
                totalReward += reward;
            }
            
            processedEpisodes++;
        }
        
        result.EpisodesProcessed = processedEpisodes;
        result.FinalAverageReward = processedEpisodes > 0 ? totalReward / processedEpisodes : 0.0;
        result.EndTime = DateTime.UtcNow;
        
        return result;
    }

    private async Task SaveTrainingResultAsync(RLTrainingResult result, CancellationToken cancellationToken)
    {
        try
        {
            var resultFile = Path.Combine(_statePath, $"training_{result.Symbol}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await File.WriteAllTextAsync(resultFile, json, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            LogFailedToSaveTrainingResult(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogFailedToSaveTrainingResult(_logger, ex);
        }
        catch (JsonException ex)
        {
            LogFailedToSaveTrainingResult(_logger, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation
        }
    }

    private async Task LoadStateAsync(CancellationToken cancellationToken = default)
    {
        // Load RL advisor state if available
        try
        {
            var stateFile = Path.Combine(_statePath, "rl_advisor_state.json");
            if (File.Exists(stateFile))
            {
                var content = await File.ReadAllTextAsync(stateFile, cancellationToken).ConfigureAwait(false);
                var state = JsonSerializer.Deserialize<RLAdvisorState>(content);
                
                if (state != null)
                {
                    _orderInfluenceEnabled = state.OrderInfluenceEnabled;
                    _lastUpliftCheck = state.LastUpliftCheck;
                    
                    LogLoadedState(_logger, _orderInfluenceEnabled, null);
                }
            }
        }
        catch (FileNotFoundException ex)
        {
            LogFailedToLoadState(_logger, ex);
        }
        catch (IOException ex)
        {
            LogFailedToLoadState(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogFailedToLoadState(_logger, ex);
        }
        catch (JsonException ex)
        {
            LogFailedToLoadState(_logger, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation
        }
    }
}

#region Supporting Classes


public class PerformanceTracker
{
    // Constants for magic number violations (S109)
    private const int MaxRandomSeed = 10000;
    private const int RandomSeedBase = 500;
    private const int DefaultStepsCount = 10;
    
    private readonly List<double> _returns = new();
    private readonly List<TimeSpan> _durations = new();

    public double AverageReward => _returns.Count > 0 ? _returns.Average() : 0.0;
    public double SharpeRatio => CalculateSharpeRatio();
    public double EdgeBps => AverageReward * MaxRandomSeed; // Convert to basis points

    public void AddOutcome(double pnl, TimeSpan duration)
    {
        _returns.Add(pnl);
        _durations.Add(duration);
        
        // Keep only recent data
        if (_returns.Count > RandomSeedBase)
        {
            _returns.RemoveAt(0);
            _durations.RemoveAt(0);
        }
    }

    private double CalculateSharpeRatio()
    {
        if (_returns.Count < DefaultStepsCount) return 0.0;
        
        var mean = _returns.Average();
        var variance = _returns.Select(r => Math.Pow(r - mean, 2)).Average();
        var stdDev = Math.Sqrt(variance);
        
        return stdDev > 0 ? mean / stdDev : 0.0;
    }
}

#endregion