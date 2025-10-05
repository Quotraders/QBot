using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotCore.Features;

/// <summary>
/// Represents a single feature column specification.
/// </summary>
public record Column
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    
    [JsonPropertyName("fillValue")]
    public required decimal FillValue { get; init; }
    
    [JsonPropertyName("index")]
    public required int Index { get; init; }
}

/// <summary>
/// Scaler parameters for feature normalization.
/// </summary>
public record Scaler
{
    [JsonPropertyName("mean")]
    public required decimal[] Mean { get; init; }
    
    [JsonPropertyName("std")]
    public required decimal[] Std { get; init; }
}

/// <summary>
/// Inference configuration mapping logits to actions.
/// </summary>
public record InferenceConfig
{
    [JsonPropertyName("logitToAction")]
    public required Dictionary<int, int> LogitToAction { get; init; }
}

/// <summary>
/// Complete feature specification including columns, scaling, and inference config.
/// </summary>
public record FeatureSpec
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }
    
    [JsonPropertyName("columns")]
    public required IReadOnlyList<Column> Columns { get; init; }
    
    [JsonPropertyName("scaler")]
    public required Scaler Scaler { get; init; }
    
    [JsonPropertyName("inference")]
    public required InferenceConfig Inference { get; init; }
}

/// <summary>
/// Static class for loading, saving, and validating feature specifications.
/// </summary>
public static class FeatureSpecLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Load feature specification from JSON file.
    /// </summary>
    public static FeatureSpec Load(string path)
    {
        var json = File.ReadAllText(path);
        var spec = JsonSerializer.Deserialize<FeatureSpec>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse feature spec from {path}");
        
        if (!ValidateSpec(spec))
        {
            throw new InvalidOperationException($"Feature spec validation failed for {path}");
        }
        
        return spec;
    }

    /// <summary>
    /// Save feature specification to JSON file.
    /// </summary>
    public static void Save(FeatureSpec spec, string path)
    {
        ArgumentNullException.ThrowIfNull(spec);
        
        if (!ValidateSpec(spec))
        {
            throw new InvalidOperationException("Cannot save invalid feature spec");
        }
        
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var json = JsonSerializer.Serialize(spec, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Validate that the feature specification is consistent.
    /// </summary>
    public static bool ValidateSpec(FeatureSpec spec)
    {
        if (spec == null) return false;
        if (spec.Columns == null || spec.Scaler == null || spec.Inference == null) return false;
        
        // Check that Mean length equals Std length equals Columns count
        if (spec.Scaler.Mean.Length != spec.Scaler.Std.Length) return false;
        if (spec.Scaler.Mean.Length != spec.Columns.Count) return false;
        
        // Validate that column indices are sequential and start at 0
        for (int i = 0; i < spec.Columns.Count; i++)
        {
            if (spec.Columns[i].Index != i) return false;
        }
        
        return true;
    }
}
