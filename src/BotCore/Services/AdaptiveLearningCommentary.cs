using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BotCore.Services;

/// <summary>
/// Provides natural language commentary on adaptive learning and parameter changes
/// </summary>
public sealed class AdaptiveLearningCommentary
{
    private const int MaxStrategiesToReport = 5;
    private const int MaxChangesPerStrategy = 3;
    
    private readonly ILogger<AdaptiveLearningCommentary> _logger;
    private readonly ParameterChangeTracker _changeTracker;
    private readonly OllamaClient? _ollamaClient;
    private readonly bool _isEnabled;

    public AdaptiveLearningCommentary(
        ILogger<AdaptiveLearningCommentary> logger,
        ParameterChangeTracker changeTracker,
        IConfiguration configuration,
        OllamaClient? ollamaClient = null)
    {
        _logger = logger;
        _changeTracker = changeTracker;
        _ollamaClient = ollamaClient;
        _isEnabled = configuration["OLLAMA_LEARNING_COMMENTARY_ENABLED"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;
    }

    /// <summary>
    /// Generate commentary on recent adaptations
    /// </summary>
    public async Task<string> ExplainRecentAdaptationsAsync(int lookbackMinutes = 60)
    {
        // Skip learning commentary if disabled or Ollama not available
        if (!_isEnabled || _ollamaClient == null)
        {
            return string.Empty;
        }

        try
        {
            var window = TimeSpan.FromMinutes(lookbackMinutes);
            var recentChanges = _changeTracker.GetChangesInWindow(window);

            if (recentChanges.Count == 0)
            {
                return string.Empty;
            }

            // Group changes by strategy
            var changesByStrategy = recentChanges
                .GroupBy(c => c.StrategyName)
                .Take(MaxStrategiesToReport)
                .ToList();

            var changesDescription = string.Join("\n", changesByStrategy.Select(group =>
            {
                var strategyChanges = group.Take(MaxChangesPerStrategy).Select(c =>
                    $"  - {c.ParameterName}: {c.OldValue} → {c.NewValue} (reason: {c.Reason})"
                );
                return $"{group.Key}:\n{string.Join("\n", strategyChanges)}";
            }));

            var prompt = $@"I am a trading bot. I recently adapted my parameters:

{changesDescription}

Explain these adaptations in 2-3 sentences. What am I learning? Speak as ME (the bot).";

            // Pass isTradeCommentary: false so this works in DRY_RUN mode
            var response = await _ollamaClient.AskAsync(prompt, isTradeCommentary: false).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEARNING-COMMENTARY] Error explaining adaptations");
            return string.Empty;
        }
    }

    /// <summary>
    /// Fire-and-forget: Start learning explanation in background without waiting for result
    /// Trading continues immediately while Ollama processes in background
    /// </summary>
    public void ExplainRecentAdaptationsFireAndForget(int lookbackMinutes = 60)
    {
        if (_ollamaClient == null)
        {
            return;
        }

        // Start background task but don't wait for it
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await ExplainRecentAdaptationsAsync(lookbackMinutes).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogInformation("📚 [LEARNING-COMMENTARY] {Commentary}", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [LEARNING-COMMENTARY] Background explanation failed");
            }
        });
    }

    /// <summary>
    /// Generate commentary on a specific parameter change
    /// </summary>
    public async Task<string> ExplainParameterChangeAsync(
        string strategyName,
        string parameterName,
        string oldValue,
        string newValue,
        string reason)
    {
        if (_ollamaClient == null)
        {
            return string.Empty;
        }

        try
        {
            var prompt = $@"I am a trading bot. I just changed a parameter:

Strategy: {strategyName}
Parameter: {parameterName}
Old value: {oldValue}
New value: {newValue}
Reason: {reason}

Explain this change in 1-2 sentences. Why did I make this adjustment? Speak as ME (the bot).";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEARNING-COMMENTARY] Error explaining parameter change for {Strategy}.{Parameter}", 
                strategyName, parameterName);
            return string.Empty;
        }
    }

    /// <summary>
    /// Generate learning summary for alerting significant changes
    /// </summary>
    public string GetLearningSummary(int lookbackMinutes = 60)
    {
        var window = TimeSpan.FromMinutes(lookbackMinutes);
        var recentChanges = _changeTracker.GetChangesInWindow(window);

        if (recentChanges.Count == 0)
        {
            return "No recent parameter changes";
        }

        var changesByStrategy = recentChanges
            .GroupBy(c => c.StrategyName)
            .Select(g => $"{g.Key}: {g.Count()} changes")
            .ToList();

        return $"Recent adaptations: {string.Join(", ", changesByStrategy)}";
    }
}
