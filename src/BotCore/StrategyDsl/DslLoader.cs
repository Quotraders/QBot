using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotCore.StrategyDsl;

/// <summary>
/// Configuration options for DSL loader
/// </summary>
public class DslLoaderOptions
{
    public string StrategyFolder { get; set; } = "./strategies/yaml";
    public bool AutoReload { get; set; } = true;
    public int ReloadIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// YAML DSL loader for strategy definitions
/// </summary>
public class DslLoader
{
    private readonly ILogger<DslLoader> _logger;
    private readonly DslLoaderOptions _options;
    private readonly IDeserializer _deserializer;
    private readonly Dictionary<string, DslStrategy> _strategies = new();
    private readonly object _lock = new();
    private DateTime _lastLoad = DateTime.MinValue;

    public DslLoader(ILogger<DslLoader> logger, IOptions<DslLoaderOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Load all strategy definitions from YAML files
    /// </summary>
    public async Task<IReadOnlyList<DslStrategy>> LoadStrategiesAsync()
    {
        if (!Directory.Exists(_options.StrategyFolder))
        {
            _logger.LogWarning("Strategy folder does not exist: {Folder}", _options.StrategyFolder);
            return new List<DslStrategy>();
        }

        var shouldReload = _lastLoad == DateTime.MinValue || 
                          (_options.AutoReload && DateTime.UtcNow - _lastLoad > TimeSpan.FromMinutes(_options.ReloadIntervalMinutes));

        if (!shouldReload)
        {
            lock (_lock)
            {
                return _strategies.Values.ToList();
            }
        }

        var yamlFiles = Directory.GetFiles(_options.StrategyFolder, "*.yml")
                                .Concat(Directory.GetFiles(_options.StrategyFolder, "*.yaml"))
                                .ToList();

        var loadedStrategies = new Dictionary<string, DslStrategy>();
        var loadCount = 0;
        var errorCount = 0;

        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(file);
                var strategy = _deserializer.Deserialize<DslStrategy>(yaml);
                
                if (strategy != null && !string.IsNullOrEmpty(strategy.Name))
                {
                    // Apply post-processing and validation
                    strategy = PostProcessStrategy(strategy, file);
                    loadedStrategies[strategy.Name] = strategy;
                    loadCount++;
                    
                    _logger.LogDebug("Loaded strategy: {Name} from {File}", strategy.Name, Path.GetFileName(file));
                }
                else
                {
                    _logger.LogWarning("Invalid strategy definition in file: {File}", file);
                    errorCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading strategy from file: {File}", file);
                errorCount++;
            }
        }

        lock (_lock)
        {
            _strategies.Clear();
            foreach (var kvp in loadedStrategies)
            {
                _strategies[kvp.Key] = kvp.Value;
            }
            _lastLoad = DateTime.UtcNow;
        }

        _logger.LogInformation("Loaded {LoadCount} strategies successfully, {ErrorCount} errors from {TotalFiles} files", 
            loadCount, errorCount, yamlFiles.Count);

        return loadedStrategies.Values.ToList();
    }

    /// <summary>
    /// Get a specific strategy by name
    /// </summary>
    public async Task<DslStrategy?> GetStrategyAsync(string name)
    {
        await LoadStrategiesAsync(); // Ensure strategies are loaded
        
        lock (_lock)
        {
            return _strategies.TryGetValue(name, out var strategy) ? strategy : null;
        }
    }

    /// <summary>
    /// Get all strategy names
    /// </summary>
    public async Task<IReadOnlyList<string>> GetStrategyNamesAsync()
    {
        await LoadStrategiesAsync(); // Ensure strategies are loaded
        
        lock (_lock)
        {
            return _strategies.Keys.ToList();
        }
    }

    /// <summary>
    /// Check if strategies need reloading
    /// </summary>
    public bool NeedsReload()
    {
        return _options.AutoReload && 
               DateTime.UtcNow - _lastLoad > TimeSpan.FromMinutes(_options.ReloadIntervalMinutes);
    }

    /// <summary>
    /// Force reload of all strategies
    /// </summary>
    public async Task ForceReloadAsync()
    {
        _lastLoad = DateTime.MinValue; // Force reload
        await LoadStrategiesAsync();
    }

    /// <summary>
    /// Post-process and validate strategy definition
    /// </summary>
    private DslStrategy PostProcessStrategy(DslStrategy strategy, string filePath)
    {
        // Ensure required fields have defaults
        if (string.IsNullOrEmpty(strategy.Priority))
            strategy.Priority = "Medium";

        if (string.IsNullOrEmpty(strategy.Family))
            strategy.Family = "unknown";

        // Convert snake_case YAML properties to proper casing (YamlDotNet handles most of this)
        PostProcessConditions(strategy.RegimeFilters);
        PostProcessConditions(strategy.ZoneConditions);
        PostProcessConditions(strategy.PatternConditions);
        PostProcessConditions(strategy.MicroConditions);

        // Add source metadata
        if (strategy.Metadata == null)
            strategy.Metadata = new DslMetadata();
        
        strategy.Metadata.Author = strategy.Metadata.Author ?? "DslLoader";
        if (string.IsNullOrEmpty(strategy.Metadata.LastUpdated))
            strategy.Metadata.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Validate critical settings
        ValidateStrategy(strategy, filePath);

        return strategy;
    }

    /// <summary>
    /// Post-process conditions to handle YAML structure variations
    /// </summary>
    private static void PostProcessConditions(DslConditions conditions)
    {
        // Ensure all condition lists are initialized
        conditions.Required ??= new List<string>();
        conditions.Preferred ??= new List<string>();
        conditions.Blocked ??= new List<string>();
        conditions.Confluence ??= new List<string>();
        conditions.Contraindications ??= new List<string>();
        conditions.EntryTriggers ??= new List<string>();
        conditions.Timing ??= new List<string>();
    }

    /// <summary>
    /// Validate strategy definition
    /// </summary>
    private void ValidateStrategy(DslStrategy strategy, string filePath)
    {
        var warnings = new List<string>();

        // Check required fields
        if (string.IsNullOrEmpty(strategy.Name))
            warnings.Add("Strategy name is required");

        if (string.IsNullOrEmpty(strategy.Description))
            warnings.Add("Strategy description is recommended");

        // Check confidence calculation
        if (strategy.ConfidenceCalculation.BaseConfidence < 0.3 || strategy.ConfidenceCalculation.BaseConfidence > 0.95)
            warnings.Add($"Base confidence {strategy.ConfidenceCalculation.BaseConfidence:F2} is outside recommended range [0.3, 0.95]");

        // Check position sizing
        if (strategy.RiskManagement.PositionSizing.BaseSize <= 0)
            warnings.Add("Position base size must be positive");

        if (strategy.RiskManagement.PositionSizing.MaxSize > 10)
            warnings.Add($"Maximum position size {strategy.RiskManagement.PositionSizing.MaxSize} is very large");

        // Log warnings
        foreach (var warning in warnings)
        {
            _logger.LogWarning("Strategy validation warning in {File}: {Warning}", Path.GetFileName(filePath), warning);
        }
    }

    /// <summary>
    /// Get strategy statistics
    /// </summary>
    public async Task<StrategyLoaderStats> GetStatsAsync()
    {
        await LoadStrategiesAsync();
        
        lock (_lock)
        {
            var enabledCount = _strategies.Values.Count(s => s.Enabled);
            var familyCounts = _strategies.Values
                .GroupBy(s => s.Family)
                .ToDictionary(g => g.Key, g => g.Count());

            return new StrategyLoaderStats
            {
                TotalStrategies = _strategies.Count,
                EnabledStrategies = enabledCount,
                DisabledStrategies = _strategies.Count - enabledCount,
                FamilyCounts = familyCounts,
                LastLoadTime = _lastLoad
            };
        }
    }
}

/// <summary>
/// Strategy loader statistics
/// </summary>
public class StrategyLoaderStats
{
    public int TotalStrategies { get; set; }
    public int EnabledStrategies { get; set; }
    public int DisabledStrategies { get; set; }
    public Dictionary<string, int> FamilyCounts { get; set; } = new();
    public DateTime LastLoadTime { get; set; }
}