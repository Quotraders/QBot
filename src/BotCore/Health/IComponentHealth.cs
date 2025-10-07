using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Health;

/// <summary>
/// Interface that any component can implement to report its health status.
/// This enables the bot to be self-aware of all its components.
/// </summary>
public interface IComponentHealth
{
    /// <summary>
    /// The name of the component being health checked
    /// </summary>
    string ComponentName { get; }

    /// <summary>
    /// Check if this component is healthy
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result with status and optional metrics</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a health check with status, metrics, and optional description
/// </summary>
public sealed class HealthCheckResult
{
    /// <summary>
    /// Is the component healthy?
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Status string: "Healthy", "Degraded", or "Unhealthy"
    /// </summary>
    public string Status { get; set; } = "Unknown";

    /// <summary>
    /// Optional metrics like memory usage, file age, error count, etc.
    /// </summary>
    public Dictionary<string, object> Metrics { get; set; } = new();

    /// <summary>
    /// Optional description explaining the issue
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Timestamp when the check happened
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional component name being checked
    /// </summary>
    public string? ComponentName { get; set; }

    /// <summary>
    /// Create a healthy result
    /// </summary>
    public static HealthCheckResult Healthy(string? description = null, Dictionary<string, object>? metrics = null)
    {
        return new HealthCheckResult
        {
            IsHealthy = true,
            Status = "Healthy",
            Description = description,
            Metrics = metrics ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Create a degraded result (partially working but with issues)
    /// </summary>
    public static HealthCheckResult Degraded(string description, Dictionary<string, object>? metrics = null)
    {
        return new HealthCheckResult
        {
            IsHealthy = false,
            Status = "Degraded",
            Description = description,
            Metrics = metrics ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Create an unhealthy result (not working)
    /// </summary>
    public static HealthCheckResult Unhealthy(string description, Dictionary<string, object>? metrics = null)
    {
        return new HealthCheckResult
        {
            IsHealthy = false,
            Status = "Unhealthy",
            Description = description,
            Metrics = metrics ?? new Dictionary<string, object>()
        };
    }
}
