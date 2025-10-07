using System;
using System.Collections.Generic;

namespace BotCore.Health;

/// <summary>
/// Represents a component that the bot discovered automatically.
/// This enables complete self-awareness of all services, files, APIs, and models.
/// </summary>
public sealed class DiscoveredComponent
{
    /// <summary>
    /// The name of the component
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of component: Service, File, API, Model, Performance Metric
    /// </summary>
    public ComponentType Type { get; set; }

    /// <summary>
    /// The actual service instance if this is a service component
    /// </summary>
    public object? ServiceInstance { get; set; }

    /// <summary>
    /// The file path if this is a file dependency
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Optional metadata about the component
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Dependencies this component relies on
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = new List<string>();

    /// <summary>
    /// Expected refresh interval for file dependencies (in hours)
    /// </summary>
    public double? ExpectedRefreshIntervalHours { get; set; }

    /// <summary>
    /// Thresholds for performance metrics
    /// </summary>
    public Dictionary<string, double> Thresholds { get; init; } = new();

    /// <summary>
    /// When this component was discovered
    /// </summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time this component was checked
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// Last health status
    /// </summary>
    public string? LastStatus { get; set; }
}

/// <summary>
/// Types of components the bot can discover and monitor
/// </summary>
public enum ComponentType
{
    /// <summary>
    /// Background service (IHostedService)
    /// </summary>
    BackgroundService,

    /// <summary>
    /// Singleton service registered in DI
    /// </summary>
    SingletonService,

    /// <summary>
    /// File dependency (parameters, models, data files)
    /// </summary>
    FileDependency,

    /// <summary>
    /// API connection (TopstepX, Ollama, Python services)
    /// </summary>
    APIConnection,

    /// <summary>
    /// Performance metric (win rate, P&L, latency)
    /// </summary>
    PerformanceMetric
}
