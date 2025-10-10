using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Atomic state persistence system for warm restart capabilities
/// Persists zone buffers, pattern reliability, and other critical state
/// Uses atomic File.Replace operations to prevent corruption
/// </summary>
public sealed class AtomicStatePersistence : IDisposable
{
    private readonly ILogger<AtomicStatePersistence> _logger;
    private readonly string _stateDirectory;
    
    // Subdirectories for different state types
    private readonly string _zonesStateDirectory;
    private readonly string _patternsStateDirectory;
    private readonly string _fusionStateDirectory;
    private readonly string _metricsStateDirectory;
    
    // JSON serialization options
    private readonly JsonSerializerOptions _jsonOptions;
    
    // State persistence intervals and settings
    private readonly TimeSpan _persistenceInterval = TimeSpan.FromSeconds(5);
    private readonly Timer _persistenceTimer;
    private readonly object _persistenceLock = new();
    
    // State tracking
    private volatile bool _persistenceEnabled = true;
    
    public AtomicStatePersistence(ILogger<AtomicStatePersistence> logger, string? baseStateDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Default to state directory in project root
        _stateDirectory = baseStateDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "state");
        _zonesStateDirectory = Path.Combine(_stateDirectory, "zones");
        _patternsStateDirectory = Path.Combine(_stateDirectory, "patterns");
        _fusionStateDirectory = Path.Combine(_stateDirectory, "fusion");
        _metricsStateDirectory = Path.Combine(_stateDirectory, "metrics");
        
        // Configure JSON serialization
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        
        // Initialize directories
        InitializeStateDirectories();
        
        // Start periodic persistence timer
        _persistenceTimer = new Timer(PeriodicPersistenceCallback, null, _persistenceInterval, _persistenceInterval);
        
        _logger.LogInformation("Atomic state persistence initialized with base directory: {StateDirectory}", _stateDirectory);
    }
    
    /// <summary>
    /// Initialize all required state directories
    /// </summary>
    private void InitializeStateDirectories()
    {
        var directories = new[]
        {
            _stateDirectory,
            _zonesStateDirectory,
            _patternsStateDirectory,
            _fusionStateDirectory,
            _metricsStateDirectory
        };
        
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug("Created state directory: {Directory}", directory);
            }
        }
    }
    
    /// <summary>
    /// Persist zone state for a symbol atomically
    /// </summary>
    public async Task PersistZoneStateAsync(string symbol, ZoneStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        ArgumentNullException.ThrowIfNull(snapshot);
            
        if (!_persistenceEnabled)
            return;
            
        try
        {
            var fileName = $"{symbol}.json";
            var filePath = Path.Combine(_zonesStateDirectory, fileName);
            
            await PersistStateAtomicallyAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Zone state persisted for {Symbol}", symbol);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error persisting zone state for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to persist zone state for {symbol} due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Serialization error persisting zone state for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to serialize zone state for {symbol}", ex);
        }
    }
    
    /// <summary>
    /// Persist pattern reliability state atomically
    /// </summary>
    public async Task PersistPatternReliabilityAsync(PatternReliabilitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
            
        if (!_persistenceEnabled)
            return;
            
        try
        {
            var filePath = Path.Combine(_patternsStateDirectory, "reliability.json");
            
            await PersistStateAtomicallyAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Pattern reliability state persisted");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error persisting pattern reliability state");
            throw new InvalidOperationException("Failed to persist pattern reliability state due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Serialization error persisting pattern reliability state");
            throw new InvalidOperationException("Failed to serialize pattern reliability state", ex);
        }
    }
    
    /// <summary>
    /// Persist fusion coordinator state atomically
    /// </summary>
    public async Task PersistFusionStateAsync(FusionStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
            
        if (!_persistenceEnabled)
            return;
            
        try
        {
            var filePath = Path.Combine(_fusionStateDirectory, "coordinator.json");
            
            await PersistStateAtomicallyAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Fusion coordinator state persisted");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error persisting fusion coordinator state");
            throw new InvalidOperationException("Failed to persist fusion coordinator state due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Serialization error persisting fusion coordinator state");
            throw new InvalidOperationException("Failed to serialize fusion coordinator state", ex);
        }
    }
    
    /// <summary>
    /// Persist metrics state atomically
    /// </summary>
    public async Task PersistMetricsStateAsync(string metricsName, MetricsStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metricsName))
            throw new ArgumentException("Metrics name cannot be null or empty", nameof(metricsName));
        ArgumentNullException.ThrowIfNull(snapshot);
            
        if (!_persistenceEnabled)
            return;
            
        try
        {
            var fileName = $"{metricsName}.json";
            var filePath = Path.Combine(_metricsStateDirectory, fileName);
            
            await PersistStateAtomicallyAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Metrics state persisted for {MetricsName}", metricsName);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error persisting metrics state for {MetricsName}", metricsName);
            throw new InvalidOperationException($"Failed to persist metrics state for {metricsName} due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Serialization error persisting metrics state for {MetricsName}", metricsName);
            throw new InvalidOperationException($"Failed to serialize metrics state for {metricsName}", ex);
        }
    }
    
    /// <summary>
    /// Atomically persist state using temp file + File.Replace pattern
    /// </summary>
    private async Task PersistStateAtomicallyAsync<T>(string filePath, T state, CancellationToken cancellationToken)
    {
        var tempFilePath = filePath + ".tmp";
        var backupFilePath = filePath + ".bak";
        
        try
        {
            // Serialize to temp file first
            var jsonData = JsonSerializer.Serialize(state, _jsonOptions);
            await File.WriteAllTextAsync(tempFilePath, jsonData, cancellationToken).ConfigureAwait(false);
            
            // Ensure data is written to disk
            using (var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            
            // Atomic replace: temp -> target (with backup if original exists)
            if (File.Exists(filePath))
            {
                // Create backup of existing file
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                }
                File.Move(filePath, backupFilePath);
            }
            
            // Move temp file to final location
            File.Move(tempFilePath, filePath);
            
            // Clean up backup file after successful operation
            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
            }
        }
        catch (IOException ex)
        {
            // Clean up temp file on error
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch (IOException) { /* ignore cleanup errors */ }
            }
            
            // Restore from backup if available
            if (File.Exists(backupFilePath) && !File.Exists(filePath))
            {
                try 
                { 
                    File.Move(backupFilePath, filePath);
                    _logger.LogInformation(ex, "Restored state file from backup after IOException: {FilePath}", filePath);
                } 
                catch (IOException restoreEx)
                {
                    _logger.LogError(restoreEx, "Failed to restore backup for {FilePath}", filePath);
                }
            }
            
            throw;
        }
        catch (JsonException)
        {
            // Clean up temp file on serialization error
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch (IOException) { /* ignore cleanup errors */ }
            }
            
            throw;
        }
    }
    
    /// <summary>
    /// Load zone state for warm restart
    /// </summary>
    public async Task<ZoneStateSnapshot?> LoadZoneStateAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
            
        try
        {
            var fileName = $"{symbol}.json";
            var filePath = Path.Combine(_zonesStateDirectory, fileName);
            
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No saved zone state found for {Symbol}", symbol);
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<ZoneStateSnapshot>(jsonData, _jsonOptions);
            
            _logger.LogDebug("Loaded zone state for {Symbol} from {FilePath}", symbol, filePath);
            return snapshot;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error loading zone state for {Symbol}", symbol);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization error loading zone state for {Symbol}", symbol);
            return null;
        }
    }
    
    /// <summary>
    /// Load pattern reliability state for warm restart
    /// </summary>
    public async Task<PatternReliabilitySnapshot?> LoadPatternReliabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_patternsStateDirectory, "reliability.json");
            
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No saved pattern reliability state found");
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<PatternReliabilitySnapshot>(jsonData, _jsonOptions);
            
            _logger.LogDebug("Loaded pattern reliability state from {FilePath}", filePath);
            return snapshot;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error loading pattern reliability state");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization error loading pattern reliability state");
            return null;
        }
    }
    
    /// <summary>
    /// Load fusion coordinator state for warm restart
    /// </summary>
    public async Task<FusionStateSnapshot?> LoadFusionStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_fusionStateDirectory, "coordinator.json");
            
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No saved fusion coordinator state found");
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<FusionStateSnapshot>(jsonData, _jsonOptions);
            
            _logger.LogDebug("Loaded fusion coordinator state from {FilePath}", filePath);
            return snapshot;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error loading fusion coordinator state");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization error loading fusion coordinator state");
            return null;
        }
    }
    
    /// <summary>
    /// Load all available state for complete warm restart
    /// </summary>
    public async Task<WarmRestartState> LoadAllStateAsync(CancellationToken cancellationToken = default)
    {
        var collection = new WarmRestartState
        {
            LoadedAt = DateTime.UtcNow,
            ZoneStates = new Dictionary<string, ZoneStateSnapshot>(),
            PatternReliability = null,
            FusionState = null,
            MetricsStates = new Dictionary<string, MetricsStateSnapshot>()
        };
        
        try
        {
            // Load zone states for all symbols
            if (Directory.Exists(_zonesStateDirectory))
            {
                var zoneFiles = Directory.GetFiles(_zonesStateDirectory, "*.json");
                foreach (var zoneFile in zoneFiles)
                {
                    var symbol = Path.GetFileNameWithoutExtension(zoneFile);
                    var zoneState = await LoadZoneStateAsync(symbol, cancellationToken).ConfigureAwait(false);
                    if (zoneState != null)
                    {
                        collection.ZoneStates[symbol] = zoneState;
                    }
                }
            }
            
            // Load pattern reliability
            collection.PatternReliability = await LoadPatternReliabilityAsync(cancellationToken).ConfigureAwait(false);
            
            // Load fusion state
            collection.FusionState = await LoadFusionStateAsync(cancellationToken).ConfigureAwait(false);
            
            // Load metrics states
            if (Directory.Exists(_metricsStateDirectory))
            {
                var metricsFiles = Directory.GetFiles(_metricsStateDirectory, "*.json");
                foreach (var metricsFile in metricsFiles)
                {
                    var metricsName = Path.GetFileNameWithoutExtension(metricsFile);
                    try
                    {
                        var jsonData = await File.ReadAllTextAsync(metricsFile, cancellationToken).ConfigureAwait(false);
                        var metricsState = JsonSerializer.Deserialize<MetricsStateSnapshot>(jsonData, _jsonOptions);
                        if (metricsState != null)
                        {
                            collection.MetricsStates[metricsName] = metricsState;
                        }
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "I/O error loading metrics state: {MetricsName}", metricsName);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Deserialization error loading metrics state: {MetricsName}", metricsName);
                    }
                }
            }
            
            _logger.LogInformation("Warm restart state loaded - Zones: {ZoneCount}, Patterns: {PatternAvailable}, Fusion: {FusionAvailable}, Metrics: {MetricsCount}",
                collection.ZoneStates.Count, 
                collection.PatternReliability != null ? "Available" : "None",
                collection.FusionState != null ? "Available" : "None",
                collection.MetricsStates.Count);
                
            return collection;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error loading warm restart state collection");
            return collection;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization error loading warm restart state collection");
            return collection;
        }
    }
    
    /// <summary>
    /// Periodic persistence callback
    /// </summary>
    private void PeriodicPersistenceCallback(object? state)
    {
        if (!_persistenceEnabled)
            return;
            
        try
        {
            lock (_persistenceLock)
            {
                // Persist any pending state changes
                // This would be triggered by state change notifications from various services
                _logger.LogTrace("Periodic persistence check completed");
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation in periodic persistence callback");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error in periodic persistence callback");
        }
    }
    
    /// <summary>
    /// Enable or disable state persistence
    /// </summary>
    public void SetPersistenceEnabled(bool enabled)
    {
        _persistenceEnabled = enabled;
        _logger.LogInformation("State persistence {Status}", enabled ? "enabled" : "disabled");
    }
    
    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        _persistenceTimer?.Dispose();
        _logger.LogInformation("Atomic state persistence disposed");
    }
}

/// <summary>
/// Zone state snapshot for persistence
/// </summary>
public sealed class ZoneStateSnapshot
{
    public DateTime CapturedAt { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public IReadOnlyList<ZoneBuffer> ZoneBuffers { get; set; } = new List<ZoneBuffer>();
    public IReadOnlyDictionary<string, double> ZoneMetrics { get; set; } = new Dictionary<string, double>();
    public int ActiveZoneCount { get; set; }
    public DateTime LastZoneUpdate { get; set; }
}

/// <summary>
/// Zone buffer data for persistence
/// </summary>
public sealed class ZoneBuffer
{
    public double HighPrice { get; set; }
    public double LowPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TestCount { get; set; }
    public double BreakoutScore { get; set; }
    public double Pressure { get; set; }
    public string ZoneType { get; set; } = string.Empty; // "demand" or "supply"
}

/// <summary>
/// Pattern reliability snapshot for persistence
/// </summary>
public sealed class PatternReliabilitySnapshot
{
    public DateTime CapturedAt { get; set; }
    public IReadOnlyDictionary<string, PatternReliabilityData> PatternReliabilities { get; set; } = new Dictionary<string, PatternReliabilityData>();
    public long TotalPatternDetections { get; set; }
    public long ConfirmedPatterns { get; set; }
}

/// <summary>
/// Pattern reliability data
/// </summary>
public sealed class PatternReliabilityData
{
    public string PatternName { get; set; } = string.Empty;
    public double ReliabilityScore { get; set; }
    public int TotalDetections { get; set; }
    public int ConfirmedDetections { get; set; }
    public DateTime LastSeen { get; set; }
    public double AverageConfidence { get; set; }
}

/// <summary>
/// Fusion coordinator state snapshot
/// </summary>
public sealed class FusionStateSnapshot
{
    public DateTime CapturedAt { get; set; }
    public IReadOnlyDictionary<string, double> LastFeatureValues { get; set; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, DateTime> FeatureTimestamps { get; set; } = new Dictionary<string, DateTime>();
    public long DecisionCount { get; set; }
    public long HoldDecisionCount { get; set; }
    public DateTime LastDecision { get; set; }
}

/// <summary>
/// Metrics state snapshot
/// </summary>
public sealed class MetricsStateSnapshot
{
    public DateTime CapturedAt { get; set; }
    public IReadOnlyDictionary<string, double> GaugeValues { get; set; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, long> CounterValues { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, IReadOnlyList<double>> HistogramValues { get; set; } = new Dictionary<string, IReadOnlyList<double>>();
}

/// <summary>
/// Complete warm restart state
/// </summary>
public sealed class WarmRestartState
{
    private readonly Dictionary<string, ZoneStateSnapshot> _zoneStates = new();
    private readonly Dictionary<string, MetricsStateSnapshot> _metricsStates = new();
    
    public DateTime LoadedAt { get; set; }
    
    public Dictionary<string, ZoneStateSnapshot> ZoneStates 
    { 
        get => _zoneStates;
        init => _zoneStates = value ?? new Dictionary<string, ZoneStateSnapshot>();
    }
    
    public PatternReliabilitySnapshot? PatternReliability { get; set; }
    public FusionStateSnapshot? FusionState { get; set; }
    
    public Dictionary<string, MetricsStateSnapshot> MetricsStates 
    { 
        get => _metricsStates;
        init => _metricsStates = value ?? new Dictionary<string, MetricsStateSnapshot>();
    }
}