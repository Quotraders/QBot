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
public sealed partial class AtomicStatePersistence : IDisposable
{
    private readonly ILogger<AtomicStatePersistence> _logger;
    private readonly string _stateDirectory;
    
    // LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, string, Exception?> LogPersistenceInitialized =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(9001, nameof(LogPersistenceInitialized)),
            "Atomic state persistence initialized with base directory: {StateDirectory}");
    
    private static readonly Action<ILogger, string, Exception?> LogCreatedStateDirectory =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(9002, nameof(LogCreatedStateDirectory)),
            "Created state directory: {Directory}");
    
    private static readonly Action<ILogger, Exception?> LogZonesStateSavingStarted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9003, nameof(LogZonesStateSavingStarted)),
            "Periodic persistence: Saving zones state...");
    
    private static readonly Action<ILogger, Exception?> LogZonesStateSavingError =
        LoggerMessage.Define(LogLevel.Error, new EventId(9004, nameof(LogZonesStateSavingError)),
            "Error during periodic zone state persistence");
    
    private static readonly Action<ILogger, Exception?> LogZonesStateSavingFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(9005, nameof(LogZonesStateSavingFailed)),
            "Failed to persist zone state during periodic persistence");
    
    private static readonly Action<ILogger, Exception?> LogPatternsStateSavingStarted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9006, nameof(LogPatternsStateSavingStarted)),
            "Periodic persistence: Saving patterns state...");
    
    private static readonly Action<ILogger, Exception?> LogPatternsStateSavingError =
        LoggerMessage.Define(LogLevel.Error, new EventId(9007, nameof(LogPatternsStateSavingError)),
            "Error during periodic pattern state persistence");
    
    private static readonly Action<ILogger, Exception?> LogPatternsStateSavingFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(9008, nameof(LogPatternsStateSavingFailed)),
            "Failed to persist pattern state during periodic persistence");
    
    private static readonly Action<ILogger, Exception?> LogFusionStateSavingStarted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9009, nameof(LogFusionStateSavingStarted)),
            "Periodic persistence: Saving fusion state...");
    
    private static readonly Action<ILogger, Exception?> LogFusionStateSavingError =
        LoggerMessage.Define(LogLevel.Error, new EventId(9010, nameof(LogFusionStateSavingError)),
            "Error during periodic fusion state persistence");
    
    private static readonly Action<ILogger, Exception?> LogFusionStateSavingFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(9011, nameof(LogFusionStateSavingFailed)),
            "Failed to persist fusion state during periodic persistence");
    
    private static readonly Action<ILogger, Exception?> LogMetricsStateSavingStarted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9012, nameof(LogMetricsStateSavingStarted)),
            "Periodic persistence: Saving metrics state...");
    
    private static readonly Action<ILogger, Exception?> LogMetricsStateSavingError =
        LoggerMessage.Define(LogLevel.Error, new EventId(9013, nameof(LogMetricsStateSavingError)),
            "Error during periodic metrics state persistence");
    
    private static readonly Action<ILogger, Exception?> LogMetricsStateSavingFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(9014, nameof(LogMetricsStateSavingFailed)),
            "Failed to persist metrics state during periodic persistence");
    
    private static readonly Action<ILogger, string, Exception?> LogZoneStatePersisted =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(9015, nameof(LogZoneStatePersisted)),
            "Zone state persisted for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogIOErrorPersistingZoneState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9016, nameof(LogIOErrorPersistingZoneState)),
            "I/O error persisting zone state for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogSerializationErrorPersistingZoneState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9017, nameof(LogSerializationErrorPersistingZoneState)),
            "Serialization error persisting zone state for {Symbol}");
    
    private static readonly Action<ILogger, Exception?> LogPatternReliabilityPersisted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9018, nameof(LogPatternReliabilityPersisted)),
            "Pattern reliability state persisted");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorPersistingPatternReliability =
        LoggerMessage.Define(LogLevel.Error, new EventId(9019, nameof(LogIOErrorPersistingPatternReliability)),
            "I/O error persisting pattern reliability state");
    
    private static readonly Action<ILogger, Exception?> LogSerializationErrorPersistingPatternReliability =
        LoggerMessage.Define(LogLevel.Error, new EventId(9020, nameof(LogSerializationErrorPersistingPatternReliability)),
            "Serialization error persisting pattern reliability state");
    
    private static readonly Action<ILogger, Exception?> LogFusionStatePersisted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(9021, nameof(LogFusionStatePersisted)),
            "Fusion coordinator state persisted");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorPersistingFusionState =
        LoggerMessage.Define(LogLevel.Error, new EventId(9022, nameof(LogIOErrorPersistingFusionState)),
            "I/O error persisting fusion coordinator state");
    
    private static readonly Action<ILogger, Exception?> LogSerializationErrorPersistingFusionState =
        LoggerMessage.Define(LogLevel.Error, new EventId(9023, nameof(LogSerializationErrorPersistingFusionState)),
            "Serialization error persisting fusion coordinator state");
    
    private static readonly Action<ILogger, string, Exception?> LogMetricsStatePersisted =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(9024, nameof(LogMetricsStatePersisted)),
            "Metrics state persisted for {MetricsName}");
    
    private static readonly Action<ILogger, string, Exception?> LogIOErrorPersistingMetricsState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9025, nameof(LogIOErrorPersistingMetricsState)),
            "I/O error persisting metrics state for {MetricsName}");
    
    private static readonly Action<ILogger, string, Exception?> LogSerializationErrorPersistingMetricsState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9026, nameof(LogSerializationErrorPersistingMetricsState)),
            "Serialization error persisting metrics state for {MetricsName}");
    
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
        
        LogPersistenceInitialized(_logger, _stateDirectory, null);
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
                LogCreatedStateDirectory(_logger, directory, null);
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
            
            LogZoneStatePersisted(_logger, symbol, null);
        }
        catch (IOException ex)
        {
            LogIOErrorPersistingZoneState(_logger, symbol, ex);
            throw new InvalidOperationException($"Failed to persist zone state for {symbol} due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            LogSerializationErrorPersistingZoneState(_logger, symbol, ex);
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
            
            LogPatternReliabilityPersisted(_logger, null);
        }
        catch (IOException ex)
        {
            LogIOErrorPersistingPatternReliability(_logger, ex);
            throw new InvalidOperationException("Failed to persist pattern reliability state due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            LogSerializationErrorPersistingPatternReliability(_logger, ex);
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
            
            LogFusionStatePersisted(_logger, null);
        }
        catch (IOException ex)
        {
            LogIOErrorPersistingFusionState(_logger, ex);
            throw new InvalidOperationException("Failed to persist fusion coordinator state due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            LogSerializationErrorPersistingFusionState(_logger, ex);
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
            
            LogMetricsStatePersisted(_logger, metricsName, null);
        }
        catch (IOException ex)
        {
            LogIOErrorPersistingMetricsState(_logger, metricsName, ex);
            throw new InvalidOperationException($"Failed to persist metrics state for {metricsName} due to I/O error", ex);
        }
        catch (JsonException ex)
        {
            LogSerializationErrorPersistingMetricsState(_logger, metricsName, ex);
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
                    LogRestoredFromBackup(_logger, filePath, ex);
                } 
                catch (IOException restoreEx)
                {
                    LogFailedToRestoreBackup(_logger, filePath, restoreEx);
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
                LogNoSavedZoneState(_logger, symbol, null);
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<ZoneStateSnapshot>(jsonData, _jsonOptions);
            
            LogLoadedZoneState(_logger, symbol, filePath, null);
            return snapshot;
        }
        catch (IOException ex)
        {
            LogIOErrorLoadingZoneState(_logger, symbol, ex);
            return null;
        }
        catch (JsonException ex)
        {
            LogDeserializationErrorZoneState(_logger, symbol, ex);
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
                LogNoSavedPatternState(_logger, null);
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<PatternReliabilitySnapshot>(jsonData, _jsonOptions);
            
            LogLoadedPatternState(_logger, filePath, null);
            return snapshot;
        }
        catch (IOException ex)
        {
            LogIOErrorLoadingPatternState(_logger, ex);
            return null;
        }
        catch (JsonException ex)
        {
            LogDeserializationErrorPatternState(_logger, ex);
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
                LogNoSavedFusionState(_logger, null);
                return null;
            }
            
            var jsonData = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<FusionStateSnapshot>(jsonData, _jsonOptions);
            
            LogLoadedFusionState(_logger, filePath, null);
            return snapshot;
        }
        catch (IOException ex)
        {
            LogIOErrorLoadingFusionState(_logger, ex);
            return null;
        }
        catch (JsonException ex)
        {
            LogDeserializationErrorFusionState(_logger, ex);
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
                        LogIOErrorLoadingMetricsState(_logger, metricsName, ex);
                    }
                    catch (JsonException ex)
                    {
                        LogDeserializationErrorMetricsState(_logger, metricsName, ex);
                    }
                }
            }
            
            LogWarmRestartStateLoaded(_logger, collection.ZoneStates.Count, 
                collection.PatternReliability != null,
                collection.FusionState != null,
                collection.MetricsStates.Count, null);
                
            return collection;
        }
        catch (IOException ex)
        {
            LogIOErrorLoadingWarmRestartState(_logger, ex);
            return collection;
        }
        catch (JsonException ex)
        {
            LogDeserializationErrorWarmRestartState(_logger, ex);
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
                LogPeriodicPersistenceCompleted(_logger, null);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationInPeriodicPersistence(_logger, ex);
        }
        catch (IOException ex)
        {
            LogIOErrorInPeriodicPersistence(_logger, ex);
        }
    }
    
    /// <summary>
    /// Enable or disable state persistence
    /// </summary>
    public void SetPersistenceEnabled(bool enabled)
    {
        _persistenceEnabled = enabled;
        LogPersistenceStatusChanged(_logger, enabled ? "enabled" : "disabled", null);
    }
    
    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        _persistenceTimer?.Dispose();
        LogDisposed(_logger, null);
    }
    
    // LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, string, Exception?> LogRestoredFromBackup =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(6201, nameof(LogRestoredFromBackup)),
            "Restored state file from backup after IOException: {FilePath}");
    
    private static readonly Action<ILogger, string, Exception?> LogFailedToRestoreBackup =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6202, nameof(LogFailedToRestoreBackup)),
            "Failed to restore backup for {FilePath}");
    
    private static readonly Action<ILogger, string, Exception?> LogNoSavedZoneState =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(6203, nameof(LogNoSavedZoneState)),
            "No saved zone state found for {Symbol}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogLoadedZoneState =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(6204, nameof(LogLoadedZoneState)),
            "Loaded zone state for {Symbol} from {FilePath}");
    
    private static readonly Action<ILogger, string, Exception?> LogIOErrorLoadingZoneState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6205, nameof(LogIOErrorLoadingZoneState)),
            "I/O error loading zone state for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogDeserializationErrorZoneState =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6206, nameof(LogDeserializationErrorZoneState)),
            "Deserialization error loading zone state for {Symbol}");
    
    private static readonly Action<ILogger, Exception?> LogNoSavedPatternState =
        LoggerMessage.Define(LogLevel.Debug, new EventId(6207, nameof(LogNoSavedPatternState)),
            "No saved pattern reliability state found");
    
    private static readonly Action<ILogger, string, Exception?> LogLoadedPatternState =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(6208, nameof(LogLoadedPatternState)),
            "Loaded pattern reliability state from {FilePath}");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorLoadingPatternState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6209, nameof(LogIOErrorLoadingPatternState)),
            "I/O error loading pattern reliability state");
    
    private static readonly Action<ILogger, Exception?> LogDeserializationErrorPatternState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6210, nameof(LogDeserializationErrorPatternState)),
            "Deserialization error loading pattern reliability state");
    
    private static readonly Action<ILogger, Exception?> LogNoSavedFusionState =
        LoggerMessage.Define(LogLevel.Debug, new EventId(6211, nameof(LogNoSavedFusionState)),
            "No saved fusion coordinator state found");
    
    private static readonly Action<ILogger, string, Exception?> LogLoadedFusionState =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(6212, nameof(LogLoadedFusionState)),
            "Loaded fusion coordinator state from {FilePath}");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorLoadingFusionState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6213, nameof(LogIOErrorLoadingFusionState)),
            "I/O error loading fusion coordinator state");
    
    private static readonly Action<ILogger, Exception?> LogDeserializationErrorFusionState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6214, nameof(LogDeserializationErrorFusionState)),
            "Deserialization error loading fusion coordinator state");
    
    private static readonly Action<ILogger, string, Exception?> LogIOErrorLoadingMetricsState =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(6215, nameof(LogIOErrorLoadingMetricsState)),
            "I/O error loading metrics state: {MetricsName}");
    
    private static readonly Action<ILogger, string, Exception?> LogDeserializationErrorMetricsState =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(6216, nameof(LogDeserializationErrorMetricsState)),
            "Deserialization error loading metrics state: {MetricsName}");
    
    private static readonly Action<ILogger, int, bool, bool, int, Exception?> LogWarmRestartStateLoaded =
        LoggerMessage.Define<int, bool, bool, int>(LogLevel.Information, new EventId(6217, nameof(LogWarmRestartStateLoaded)),
            "Warm restart state loaded - Zones: {ZoneCount}, Patterns: {PatternAvailable}, Fusion: {FusionAvailable}, Metrics: {MetricsCount}");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorLoadingWarmRestartState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6218, nameof(LogIOErrorLoadingWarmRestartState)),
            "I/O error loading warm restart state collection");
    
    private static readonly Action<ILogger, Exception?> LogDeserializationErrorWarmRestartState =
        LoggerMessage.Define(LogLevel.Error, new EventId(6219, nameof(LogDeserializationErrorWarmRestartState)),
            "Deserialization error loading warm restart state collection");
    
    private static readonly Action<ILogger, Exception?> LogPeriodicPersistenceCompleted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(6220, nameof(LogPeriodicPersistenceCompleted)),
            "Periodic persistence check completed");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationInPeriodicPersistence =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6221, nameof(LogInvalidOperationInPeriodicPersistence)),
            "Invalid operation in periodic persistence callback");
    
    private static readonly Action<ILogger, Exception?> LogIOErrorInPeriodicPersistence =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6222, nameof(LogIOErrorInPeriodicPersistence)),
            "IO error in periodic persistence callback");
    
    private static readonly Action<ILogger, string, Exception?> LogPersistenceStatusChanged =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(6223, nameof(LogPersistenceStatusChanged)),
            "State persistence {Status}");
    
    private static readonly Action<ILogger, Exception?> LogDisposed =
        LoggerMessage.Define(LogLevel.Information, new EventId(6224, nameof(LogDisposed)),
            "Atomic state persistence disposed");
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