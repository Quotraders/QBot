using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BotCore.Services;

/// <summary>
/// Bot Alert Service - Makes the bot self-aware and communicative
/// Monitors systems, tracks performance, and alerts about important events
/// Uses Ollama AI for natural language warnings when available
/// </summary>
public class BotAlertService
{
    private readonly ILogger<BotAlertService> _logger;
    private readonly OllamaClient _ollamaClient;
    private readonly IConfiguration _configuration;
    private readonly bool _alertsEnabled;
    private readonly bool _ollamaEnabled;

    public BotAlertService(
        ILogger<BotAlertService> logger,
        OllamaClient ollamaClient,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        
        _logger = logger;
        _ollamaClient = ollamaClient;
        _configuration = configuration;

        _alertsEnabled = configuration["BOT_ALERTS_ENABLED"]?.ToUpperInvariant() == "TRUE" || 
                        configuration["BOT_ALERTS_ENABLED"] == "1";
        _ollamaEnabled = configuration["OLLAMA_ENABLED"]?.ToUpperInvariant() == "TRUE" || 
                        configuration["OLLAMA_ENABLED"] == "1";

        if (_alertsEnabled)
        {
            LogAlertSystemEnabled(_logger, null);
        }
    }
    
    private static readonly Action<ILogger, Exception?> LogAlertSystemEnabled =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogAlertSystemEnabled)),
            "🔔 [BOT-ALERT] Bot alert system enabled");

    /// <summary>
    /// Check startup health and report status of all systems
    /// </summary>
    public async Task CheckStartupHealthAsync(
        bool ollamaConnected,
        bool calendarLoaded,
        bool pythonUcbRunning,
        bool cloudModelsDownloaded)
    {
        if (!_alertsEnabled) return;

        var issues = new System.Collections.Generic.List<string>();

        if (!ollamaConnected)
            issues.Add("Ollama AI not running - I'll be silent");
        
        if (!calendarLoaded)
            issues.Add("Economic calendar missing - won't block event trades");
        
        if (!pythonUcbRunning)
            issues.Add("Python UCB service failed to start");
        
        if (!cloudModelsDownloaded)
            issues.Add("Cloud models not downloaded yet");

        if (issues.Count == 0)
        {
            await GenerateAlertAsync(
                "All systems GO! 🚀",
                "All startup systems are healthy and ready for trading",
                "✅"
            ).ConfigureAwait(false);
        }
        else
        {
            var issueList = string.Join(", ", issues);
            await GenerateAlertAsync(
                "Startup Issues Detected",
                $"Some systems need attention: {issueList}",
                "⚠️"
            ).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Alert when VIX spikes suddenly (>30% increase)
    /// </summary>
    public async Task AlertVixSpikeAsync(decimal oldVix, decimal newVix)
    {
        if (!_alertsEnabled) return;

        var percentChange = ((newVix - oldVix) / oldVix) * 100;
        
        await GenerateAlertAsync(
            "VIX Spike Detected",
            $"VIX jumped from {oldVix:F1} to {newVix:F1} ({percentChange:+F1}%)! Getting defensive",
            "🔥"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert about upcoming high-impact economic event
    /// </summary>
    public async Task AlertUpcomingEventAsync(string eventName, int minutesUntil)
    {
        if (!_alertsEnabled) return;

        await GenerateAlertAsync(
            "High-Impact Event Approaching",
            $"{eventName} in {minutesUntil} minutes! Going flat and blocking trades",
            "📢"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert when Gate 5 triggers rollback
    /// </summary>
    public async Task AlertRollbackAsync(string reason, decimal currentWinRate, decimal currentDrawdown)
    {
        if (!_alertsEnabled) return;

        await GenerateAlertAsync(
            "Gate 5 Rollback Triggered",
            $"Reason: {reason}. Win rate: {currentWinRate:F1}%, Drawdown: ${currentDrawdown:F0}",
            "🔄"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert when win rate drops below threshold
    /// </summary>
    public async Task AlertLowWinRateAsync(decimal currentWinRate)
    {
        if (!_alertsEnabled) return;

        var threshold = decimal.Parse(_configuration["BOT_ALERT_WIN_RATE_THRESHOLD"] ?? "60", CultureInfo.InvariantCulture);

        await GenerateAlertAsync(
            "Low Win Rate Warning",
            $"Win rate dropped to {currentWinRate:F1}% (threshold: {threshold}%). Getting cautious",
            "⚠️"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert when daily profit target is reached
    /// </summary>
    public async Task AlertDailyTargetReachedAsync(decimal dailyProfitTarget, decimal currentProfit)
    {
        if (!_alertsEnabled) return;

        await GenerateAlertAsync(
            "Daily Target Reached",
            $"Hit daily target of ${dailyProfitTarget:F0}! Current: ${currentProfit:F0}. Going flat to protect gains",
            "🎯"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert about disabled features that may impact trading
    /// </summary>
    public async Task AlertFeatureDisabledAsync(string featureName, string impact)
    {
        if (!_alertsEnabled) return;

        await GenerateAlertAsync(
            "Feature Disabled",
            $"{featureName} is disabled - {impact}",
            "ℹ️"
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Alert about system health issues
    /// </summary>
    public async Task AlertSystemHealthAsync(string issue, string details)
    {
        if (!_alertsEnabled) return;

        await GenerateAlertAsync(
            "System Health Issue",
            $"{issue}: {details}",
            "🏥"
        ).ConfigureAwait(false);
    }

    // LoggerMessage delegates for performance (CA1848)
    private static readonly Action<ILogger, string, string, Exception?> LogAlertWarning =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogAlertWarning)),
            "{Emoji} [BOT-ALERT] {Message}");
    
    private static readonly Action<ILogger, Exception?> LogAiAlertFallback =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(3, nameof(LogAiAlertFallback)),
            "Failed to generate AI alert, falling back to plain text");

    /// <summary>
    /// Generate alert message using Ollama AI or fallback to plain text
    /// </summary>
    private async Task GenerateAlertAsync(string title, string details, string emoji)
    {
        string message;

        if (_ollamaEnabled)
        {
            try
            {
                var ollamaConnected = await _ollamaClient.IsConnectedAsync().ConfigureAwait(false);
                
                if (ollamaConnected)
                {
                    var prompt = $"You are a trading bot speaking in first person. Create a brief, natural warning message about: {title}. Details: {details}. Keep it under 2 sentences and speak as 'I' not 'the bot'.";
                    
                    var aiResponse = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    
                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        message = aiResponse.Trim();
                        LogAlertWarning(_logger, emoji, message, null);
                        return;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                LogAiAlertFallback(_logger, ex);
            }
            catch (TaskCanceledException ex)
            {
                LogAiAlertFallback(_logger, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogAiAlertFallback(_logger, ex);
            }
        }

        // Fallback to plain text
        message = $"{title}: {details}";
        LogAlertWarning(_logger, emoji, message, null);
    }
}
