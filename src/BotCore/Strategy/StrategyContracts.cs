using System;
using System.Collections.Generic;

namespace BotCore.Strategy;

/// <summary>
/// Strategy intent enumeration for trading direction
/// </summary>
public enum StrategyIntent { Long, Short }

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
    string[] TelemetryTags);

/// <summary>
/// Interface for strategy knowledge graph evaluation
/// Evaluates available strategies against current market conditions
/// </summary>
public interface IStrategyKnowledgeGraph
{
    /// <summary>
    /// Evaluate all strategies for the given symbol and return ranked recommendations
    /// </summary>
    /// <param name="symbol">Trading symbol to evaluate</param>
    /// <param name="utc">Current UTC time for evaluation</param>
    /// <returns>List of strategy recommendations ranked by confidence</returns>
    IReadOnlyList<StrategyRecommendation> Evaluate(string symbol, DateTime utc);
}