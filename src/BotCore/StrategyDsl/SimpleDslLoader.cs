using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BotCore.StrategyDsl;

/// <summary>
/// Simple DSL loader for strategy YAML files
/// Matches the problem statement specification exactly
/// </summary>
public static class SimpleDslLoader
{
    /// <summary>
    /// Load all YAML strategy files from the specified folder
    /// Returns deserialized DslStrategy objects ready for knowledge graph evaluation
    /// </summary>
    /// <param name="folder">Path to folder containing YAML strategy files</param>
    /// <returns>List of loaded DSL strategies</returns>
    public static IReadOnlyList<DslStrategy> LoadAll(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Strategy folder not found: {folder}");
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var strategies = new List<DslStrategy>();
        var yamlFiles = Directory.EnumerateFiles(folder, "*.yaml")
                                .Concat(Directory.EnumerateFiles(folder, "*.yml"));

        foreach (var file in yamlFiles)
        {
            try
            {
                var yamlContent = File.ReadAllText(file);
                var strategy = deserializer.Deserialize<YamlStrategy>(yamlContent);
                
                if (strategy != null)
                {
                    var dslStrategy = ConvertToDslStrategy(strategy);
                    strategies.Add(dslStrategy);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load strategy from {file}: {ex.Message}", ex);
            }
        }

        return strategies;
    }

    /// <summary>
    /// Convert YAML strategy format to internal DSL strategy format
    /// </summary>
    private static DslStrategy ConvertToDslStrategy(YamlStrategy yaml)
    {
        return new DslStrategy
        {
            Name = yaml.Name,
            Label = yaml.Label,
            Family = yaml.Family,
            Bias = yaml.Bias,
            TelemetryTags = yaml.TelemetryTags ?? new List<string>(),
            When = new DslWhen
            {
                Regime = yaml.When?.Regime ?? new List<string>(),
                Micro = yaml.When?.Micro ?? new List<string>()
            },
            Contra = yaml.Contra,
            Confluence = yaml.Confluence ?? new List<string>(),
            Playbook = yaml.Playbook != null ? new DslPlaybook 
            { 
                Name = yaml.Playbook.Entry + "; " + yaml.Playbook.Bracket,
                Description = $"Entry: {yaml.Playbook.Entry}, Bracket: {yaml.Playbook.Bracket}"
            } : null
        };
    }
}

/// <summary>
/// YAML strategy structure matching the problem statement format
/// </summary>
public class YamlStrategy
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Bias { get; set; } = "both";
    public YamlWhen? When { get; set; }
    public List<string>? Contra { get; init; }
    public List<string>? Confluence { get; init; }
    public YamlPlaybook? Playbook { get; set; }
    public List<string>? TelemetryTags { get; init; }
}

/// <summary>
/// YAML when conditions structure
/// </summary>
public class YamlWhen
{
    public List<string>? Regime { get; init; }
    public List<string>? Micro { get; init; }
}

/// <summary>
/// YAML playbook structure
/// </summary>
public class YamlPlaybook
{
    public string Entry { get; set; } = string.Empty;
    public string Bracket { get; set; } = string.Empty;
}