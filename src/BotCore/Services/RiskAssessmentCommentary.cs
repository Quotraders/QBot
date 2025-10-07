using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BotCore.Patterns;
using Zones;

namespace BotCore.Services;

/// <summary>
/// Provides natural language risk commentary by aggregating zone data, pattern data, 
/// and market context, then sending to Ollama for analysis
/// </summary>
public sealed class RiskAssessmentCommentary
{
    private readonly ILogger<RiskAssessmentCommentary> _logger;
    private readonly IZoneService _zoneService;
    private readonly PatternEngine _patternEngine;
    private readonly OllamaClient? _ollamaClient;

    public RiskAssessmentCommentary(
        ILogger<RiskAssessmentCommentary> logger,
        IZoneService zoneService,
        PatternEngine patternEngine,
        OllamaClient? ollamaClient = null)
    {
        _logger = logger;
        _zoneService = zoneService;
        _patternEngine = patternEngine;
        _ollamaClient = ollamaClient;
    }

    /// <summary>
    /// Analyze risk for a given symbol and provide natural language commentary
    /// </summary>
    public async Task<string> AnalyzeRiskAsync(
        string symbol, 
        decimal currentPrice, 
        decimal atr,
        CancellationToken cancellationToken = default)
    {
        if (_ollamaClient == null)
        {
            return string.Empty;
        }

        try
        {
            // Get zone data
            var zoneSnapshot = _zoneService.GetSnapshot(symbol);
            
            // Get pattern data
            var patternScores = await _patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Build risk context
            var zoneContext = BuildZoneContext(zoneSnapshot, currentPrice, atr);
            var patternContext = BuildPatternContext(patternScores);
            
            // Create prompt for Ollama
            var prompt = $@"Analyze trading risk for {symbol}:

Price: {currentPrice:F2}
{zoneContext}
{patternContext}

Provide a brief risk assessment (2-3 sentences) categorized as LOW, MODERATE, or HIGH RISK. 
Focus on actionable insights about support/resistance and pattern signals.";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [RISK-COMMENTARY] Error analyzing risk for {Symbol}", symbol);
            return string.Empty;
        }
    }

    private static string BuildZoneContext(ZoneSnapshot snapshot, decimal currentPrice, decimal atr)
    {
        if (snapshot.NearestDemand == null && snapshot.NearestSupply == null)
        {
            return "Zones: No significant support/resistance zones detected";
        }

        var parts = new System.Collections.Generic.List<string>();
        
        if (snapshot.NearestDemand != null && snapshot.DistToDemandAtr < 2.0m)
        {
            parts.Add($"Demand zone {snapshot.DistToDemandAtr:F1} ATR away (pressure: {snapshot.ZonePressure:F2}, {snapshot.NearestDemand.TouchCount} touches)");
        }
        
        if (snapshot.NearestSupply != null && snapshot.DistToSupplyAtr < 2.0m)
        {
            parts.Add($"Supply zone {snapshot.DistToSupplyAtr:F1} ATR away (pressure: {snapshot.ZonePressure:F2}, {snapshot.NearestSupply.TouchCount} touches)");
        }
        
        if (snapshot.BreakoutScore > 0.5m)
        {
            parts.Add($"Breakout score: {snapshot.BreakoutScore:F2}");
        }

        return parts.Count > 0 
            ? "Zones: " + string.Join(", ", parts)
            : "Zones: Zones present but not nearby";
    }

    private static string BuildPatternContext(PatternScoresWithDetails patternScores)
    {
        if (patternScores.DetectedPatterns.Count == 0)
        {
            return "Patterns: No significant patterns detected";
        }

        var parts = new System.Collections.Generic.List<string>();
        
        foreach (var pattern in patternScores.DetectedPatterns)
        {
            if (pattern.Confidence > 0.6)
            {
                parts.Add($"{pattern.Name} ({pattern.Confidence:P0} confidence)");
            }
        }

        if (parts.Count == 0)
        {
            return "Patterns: Low confidence patterns only";
        }

        return "Patterns: " + string.Join(", ", parts);
    }
}
