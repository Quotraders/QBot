using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Strategy;

/// <summary>
/// Strategy intent enumeration for trading direction
/// </summary>
public enum StrategyIntent { Buy, Sell }

/// <summary>
/// Evidence supporting a strategy recommendation
/// Contains metric name, value, and optional description
/// </summary>
public sealed record StrategyEvidence(string Name, double Value, string? Note = null);

/// <summary>
/// Strategy recommendation from the knowledge graph
/// Contains strategy details, confidence, and supporting evidence
/// </summary>
public sealed record StrategyRecommendation(
    string StrategyName,
    StrategyIntent Intent,
    double Confidence,              // 0..1
    IReadOnlyList<StrategyEvidence> Evidence,
    IReadOnlyList<string> TelemetryTags);

/// <summary>
/// Interface for strategy knowledge graph evaluation with async support
/// Evaluates available strategies against current market conditions
/// </summary>
public interface IStrategyKnowledgeGraph
{
    /// <summary>
    /// Asynchronously evaluate all strategies for the given symbol and return ranked recommendations
    /// </summary>
    /// <param name="symbol">Trading symbol to evaluate</param>
    /// <param name="utc">Current UTC time for evaluation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of strategy recommendations ranked by confidence</returns>
    Task<IReadOnlyList<StrategyRecommendation>> EvaluateAsync(string symbol, DateTime utc, CancellationToken cancellationToken = default);
}