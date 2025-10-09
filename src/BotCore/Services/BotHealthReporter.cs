using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Health;

namespace BotCore.Services;

/// <summary>
/// Converts health check results into natural language reports for humans
/// Uses AI (Ollama) to generate plain English explanations of system health
/// Part of Phase 4: Bot Self-Awareness System
/// </summary>
public class BotHealthReporter
{
    private readonly ILogger<BotHealthReporter> _logger;
    private readonly OllamaClient? _ollamaClient;

    public BotHealthReporter(
        ILogger<BotHealthReporter> logger,
        IConfiguration configuration,
        OllamaClient? ollamaClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);
        _ollamaClient = ollamaClient;
    }

    /// <summary>
    /// Generate a natural language health report for a component
    /// </summary>
    public async Task<string> GenerateHealthReportAsync(
        string componentName,
        HealthCheckResult healthResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(healthResult);

        try
        {
            // If Ollama is not available, return basic description
            if (_ollamaClient == null || !await _ollamaClient.IsConnectedAsync().ConfigureAwait(false))
            {
                return GenerateBasicHealthReport(componentName, healthResult);
            }

            // Generate AI-powered explanation
            var prompt = BuildHealthReportPrompt(componentName, healthResult);
            var aiReport = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);

            return aiReport ?? GenerateBasicHealthReport(componentName, healthResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI health report for {ComponentName}, using basic report", componentName);
            return GenerateBasicHealthReport(componentName, healthResult);
        }
    }

    /// <summary>
    /// Generate a summary report for multiple components
    /// </summary>
    public static Task<string> GenerateSummaryReportAsync(
        IEnumerable<(string ComponentName, HealthCheckResult Result)> healthResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(healthResults);

        var results = healthResults.ToList();
        var totalComponents = results.Count;
        var healthyCount = results.Count(r => r.Result.Status == "Healthy");
        var degradedCount = results.Count(r => r.Result.Status == "Degraded");
        var unhealthyCount = results.Count(r => r.Result.Status == "Unhealthy");

        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine("Bot Self-Awareness Status Report");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Total Components: {totalComponents}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"✅ Healthy: {healthyCount}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"⚠️ Degraded: {degradedCount}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"❌ Unhealthy: {unhealthyCount}");

        // List unhealthy components
        var unhealthyComponents = results.Where(r => r.Result.Status == "Unhealthy").ToList();
        if (unhealthyComponents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("❌ UNHEALTHY COMPONENTS:");
            foreach (var (componentName, result) in unhealthyComponents)
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {componentName}: {result.Description}");
            }
        }

        // List degraded components
        var degradedComponents = results.Where(r => r.Result.Status == "Degraded").ToList();
        if (degradedComponents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ DEGRADED COMPONENTS:");
            foreach (var (componentName, result) in degradedComponents)
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {componentName}: {result.Description}");
            }
        }

        sb.AppendLine("═══════════════════════════════════════");

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Generate a plain English explanation for a health issue
    /// </summary>
    public async Task<string> ExplainHealthIssueAsync(
        string componentName,
        HealthCheckResult healthResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(healthResult);

        try
        {
            // If component is healthy, no explanation needed
            if (healthResult.Status == "Healthy")
            {
                return $"{componentName} is healthy and operating normally.";
            }

            // If Ollama is not available, return basic explanation
            if (_ollamaClient == null || !await _ollamaClient.IsConnectedAsync().ConfigureAwait(false))
            {
                return GenerateBasicExplanation(componentName, healthResult);
            }

            // Generate AI-powered explanation
            var prompt = BuildIssueExplanationPrompt(componentName, healthResult);
            var aiExplanation = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);

            return aiExplanation ?? GenerateBasicExplanation(componentName, healthResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI explanation for {ComponentName}, using basic explanation", componentName);
            return GenerateBasicExplanation(componentName, healthResult);
        }
    }

    #region Private Helper Methods

    private static string GenerateBasicHealthReport(string componentName, HealthCheckResult healthResult)
    {
        var emoji = healthResult.Status switch
        {
            "Healthy" => "✅",
            "Degraded" => "⚠️",
            "Unhealthy" => "❌",
            _ => "❓"
        };

        return $"{emoji} {componentName}: {healthResult.Status} - {healthResult.Description}";
    }

    private static string GenerateBasicExplanation(string componentName, HealthCheckResult healthResult)
    {
        var explanation = new StringBuilder();
        explanation.Append(System.Globalization.CultureInfo.InvariantCulture, $"My {componentName} is {healthResult.Status.ToLowerInvariant()}. ");

        if (healthResult.Status == "Unhealthy")
        {
            explanation.Append("This is a critical issue that needs immediate attention. ");
        }
        else if (healthResult.Status == "Degraded")
        {
            explanation.Append("This is not critical but should be addressed soon. ");
        }

        explanation.Append(healthResult.Description);

        return explanation.ToString();
    }

    private static string BuildHealthReportPrompt(string componentName, HealthCheckResult healthResult)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("I am a trading bot reporting on my system health.");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Component: {componentName}");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Status: {healthResult.Status}");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Issue: {healthResult.Description}");

        if (healthResult.Metrics.Count > 0)
        {
            prompt.AppendLine("Metrics:");
            foreach (var metric in healthResult.Metrics)
            {
                prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {metric.Key}: {metric.Value}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Generate a brief (1-2 sentences) status report in first person.");
        prompt.AppendLine("Speak as ME (the bot), not as an observer.");
        prompt.AppendLine("Be concise and clear about the issue and its impact.");

        return prompt.ToString();
    }

    private static string BuildIssueExplanationPrompt(string componentName, HealthCheckResult healthResult)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("I am a trading bot explaining a system health issue.");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Component: {componentName}");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Status: {healthResult.Status}");
        prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Issue: {healthResult.Description}");

        if (healthResult.Metrics.Count > 0)
        {
            prompt.AppendLine("Metrics:");
            foreach (var metric in healthResult.Metrics)
            {
                prompt.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {metric.Key}: {metric.Value}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Explain in plain English what this means and why it matters for trading.");
        prompt.AppendLine("Speak as ME (the bot) in 2-3 sentences.");
        prompt.AppendLine("Focus on the practical impact and what should be done.");

        return prompt.ToString();
    }

    #endregion
}
