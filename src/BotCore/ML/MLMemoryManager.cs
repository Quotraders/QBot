using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime;

namespace BotCore.ML;

/// <summary>
/// Memory usage snapshot for ML operations
/// </summary>
public sealed class MemorySnapshot
{
    public long TotalMemory { get; set; }
    public long UsedMemory { get; set; }
    public long MLMemory { get; set; }
    public Dictionary<string, long> ModelMemory { get; } = new();
    public int LoadedModels { get; set; }
    public int CachedPredictions { get; set; }
    private readonly List<string> _memoryLeaks = new();
    public IReadOnlyList<string> MemoryLeaks => _memoryLeaks;
    
    internal void AddMemoryLeak(string leak) => _memoryLeaks.Add(leak);
}

/// <summary>
/// ML Memory Management System for preventing memory leaks in the ML pipeline
/// Manages ML model lifecycle, memory monitoring, and automatic cleanup
/// </summary>
public class MLMemoryManager : IMLMemoryManager
{
    private readonly ILogger<MLMemoryManager> _logger;
    private readonly OnnxModelLoader _onnxLoader;
    private readonly ConcurrentDictionary<string, ModelVersion> _activeModels = new();
    private readonly Queue<ModelVersion> _modelHistory = new();
    private readonly Timer _garbageCollector;
    private readonly Timer _memoryMonitor;
    private readonly object _lockObject = new();
    
    // Memory thresholds
    private const long MAX_MEMORY_BYTES = 8L * 1024 * 1024 * 1024; // 8GB
    private const int MAX_MODEL_VERSIONS = 3;
    
    // Memory pressure thresholds (as percentages of MAX_MEMORY_BYTES)
    private const double WARNING_THRESHOLD = 0.7;      // 70% - Start monitoring for cleanup
    private const double HIGH_THRESHOLD = 0.75;        // 75% - Target level after cleanup
    private const double VERY_HIGH_THRESHOLD = 0.8;    // 80% - Trigger intelligent cleanup
    private const double CRITICAL_THRESHOLD = 0.9;     // 90% - Suggest GC to runtime
    private const double EMERGENCY_THRESHOLD = 0.95;   // 95% - Throw exception, cannot continue
    
    // Byte conversion constants
    private const double BYTES_TO_MB = 1024.0 * 1024.0;
    
    // GC notification thresholds
    private const int GC_NOTIFICATION_THRESHOLD = 10;
    
    // Timing constants
    private const int GC_COLLECTION_INTERVAL_MINUTES = 5;
    private const int MEMORY_MONITOR_INTERVAL_SECONDS = 30;
    private const int CLEANUP_WAIT_SECONDS = 10;
    private const int CLEANUP_DELAY_MS = 500;
    private const int POST_GC_DELAY_MS = 1000;
    private const int UNUSED_MODEL_TIMEOUT_MINUTES = 30;
    private const int LONG_UNUSED_MODEL_TIMEOUT_HOURS = 2;
    private const int MEMORY_LEAK_DETECTION_HOURS = 1;
    private const int MEMORY_SNAPSHOT_LOG_INTERVAL_MINUTES = 5;
    
    // Memory size constants
    private const long NO_GC_REGION_SIZE_BYTES = 1024 * 1024;  // 1MB
    private const long MEANINGFUL_CLEANUP_THRESHOLD_MB = 50;
    private const long LOH_COMPACTION_THRESHOLD_BYTES = 100L * 1024 * 1024;  // 100MB
    private const int GEN2_COLLECTION_GENERATION = 2;
    private const int GC_MONITORING_DELAY_MS = 1000;
    
    // Memory percentage thresholds for monitoring
    private const double HIGH_MEMORY_PERCENTAGE = 75.0;
    private const double CRITICAL_MEMORY_PERCENTAGE = 90.0;
    
    private bool _disposed;

    internal sealed class ModelVersion
    {
        public string ModelId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public object? Model { get; set; }
        public long MemoryFootprint { get; set; }
        public DateTime LoadedAt { get; set; }
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; }
        public WeakReference? WeakRef { get; set; }
    }

    // Structured logging delegates
    private static readonly Action<ILogger, Exception?> LogManagerInitialized =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogManagerInitialized)),
            "[ML-Memory] MLMemoryManager initialized with real ONNX loader");

    private static readonly Action<ILogger, Exception?> LogServicesStarting =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(LogServicesStarting)),
            "[ML-Memory] Starting memory management services");

    private static readonly Action<ILogger, string, Exception?> LogModelReused =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3, nameof(LogModelReused)),
            "[ML-Memory] Reusing cached model: {ModelId}");

    private static readonly Action<ILogger, string, Exception?> LogModelLoadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogModelLoadFailed)),
            "[ML-Memory] Failed to load model from: {ModelPath}");

    private static readonly Action<ILogger, double, Exception?> LogMemoryPressure =
        LoggerMessage.Define<double>(
            LogLevel.Warning,
            new EventId(5, nameof(LogMemoryPressure)),
            "[ML-Memory] Memory pressure detected ({MemoryMB:F1}MB), will monitor for cleanup");

    private static readonly Action<ILogger, string, string, double, Exception?> LogModelLoaded =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Information,
            new EventId(6, nameof(LogModelLoaded)),
            "[ML-Memory] Loaded model: {ModelId} v{Version} ({MemoryMB:F1}MB)");

    private static readonly Action<ILogger, string, Exception?> LogModelLoadError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(7, nameof(LogModelLoadError)),
            "[ML-Memory] Error loading model: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogOnnxModelLoading =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(8, nameof(LogOnnxModelLoading)),
            "[ML-Memory] Loading ONNX model: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogModelFileNotFound =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(9, nameof(LogModelFileNotFound)),
            "[ML-Memory] Model file not found: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogOnnxLoadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(10, nameof(LogOnnxLoadFailed)),
            "[ML-Memory] Failed to load ONNX model: {ModelPath}");

    private static readonly Action<ILogger, string, int, int, Exception?> LogOnnxModelLoadedSuccess =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Information,
            new EventId(11, nameof(LogOnnxModelLoadedSuccess)),
            "[ML-Memory] ONNX model loaded successfully: {ModelPath}, Inputs: {InputCount}, Outputs: {OutputCount}");

    private static readonly Action<ILogger, string, Exception?> LogOnnxLoadError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(12, nameof(LogOnnxLoadError)),
            "[ML-Memory] Error loading ONNX model: {ModelPath}");

    private static readonly Action<ILogger, double, Exception?> LogHighMemoryUsage =
        LoggerMessage.Define<double>(
            LogLevel.Warning,
            new EventId(13, nameof(LogHighMemoryUsage)),
            "[ML-Memory] Memory usage high ({MemoryMB:F1}MB), starting intelligent cleanup");

    private static readonly Action<ILogger, Exception?> LogCriticalMemory =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(14, nameof(LogCriticalMemory)),
            "[ML-Memory] Memory critically high, suggesting collection to runtime");

    private static readonly Action<ILogger, string, Exception?> LogUnusedModelRemoved =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(15, nameof(LogUnusedModelRemoved)),
            "[ML-Memory] Removed unused model: {Key}");

    private static readonly Action<ILogger, int, int, Exception?> LogCleanupCompleted =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(16, nameof(LogCleanupCompleted)),
            "[ML-Memory] Intelligent cleanup completed - removed {RemovedCount} unused models, {DeadRefs} dead references");

    private static readonly Action<ILogger, string, Exception?> LogOldVersionRemoved =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(17, nameof(LogOldVersionRemoved)),
            "[ML-Memory] Removed old model version: {Key}");

    private static readonly Action<ILogger, long, int, Exception?> LogCleanupFreed =
        LoggerMessage.Define<long, int>(
            LogLevel.Information,
            new EventId(18, nameof(LogCleanupFreed)),
            "[ML-Memory] Intelligent cleanup freed {FreedMB}MB, removed {ModelCount} models");

    private static readonly Action<ILogger, Exception?> LogCleanupFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(19, nameof(LogCleanupFailed)),
            "[ML-Memory] Intelligent cleanup failed");

    private static readonly Action<ILogger, double, Exception?> LogCriticalMemoryPercentage =
        LoggerMessage.Define<double>(
            LogLevel.Critical,
            new EventId(20, nameof(LogCriticalMemoryPercentage)),
            "[ML-Memory] CRITICAL: Memory usage at {MemoryPercentage:F1}%");

    private static readonly Action<ILogger, double, Exception?> LogHighMemoryPercentage =
        LoggerMessage.Define<double>(
            LogLevel.Warning,
            new EventId(21, nameof(LogHighMemoryPercentage)),
            "[ML-Memory] High memory usage: {MemoryPercentage:F1}%");

    private static readonly Action<ILogger, Exception?> LogMonitoringFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(22, nameof(LogMonitoringFailed)),
            "[ML-Memory] Memory monitoring failed");

    private static readonly Action<ILogger, Exception?> LogEmergencyCleanup =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(23, nameof(LogEmergencyCleanup)),
            "[ML-Memory] Starting intelligent emergency cleanup");

    private static readonly Action<ILogger, long, int, Exception?> LogEmergencyCleanupCompleted =
        LoggerMessage.Define<long, int>(
            LogLevel.Information,
            new EventId(24, nameof(LogEmergencyCleanupCompleted)),
            "[ML-Memory] Emergency cleanup completed - freed ~{FreedMB}MB from {ModelCount} models");

    private static readonly Action<ILogger, Exception?> LogGCApproaching =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(25, nameof(LogGCApproaching)),
            "[ML-Memory] Full GC approaching - preparing cleanup");

    private static readonly Action<ILogger, Exception?> LogGCCompleted =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(26, nameof(LogGCCompleted)),
            "[ML-Memory] Full GC completed");

    private static readonly Action<ILogger, Exception?> LogGCMonitoringFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(27, nameof(LogGCMonitoringFailed)),
            "[ML-Memory] GC monitoring failed");

    private static readonly Action<ILogger, double, double, int, int, Exception?> LogMemorySnapshot =
        LoggerMessage.Define<double, double, int, int>(
            LogLevel.Information,
            new EventId(28, nameof(LogMemorySnapshot)),
            "[ML-Memory] Memory snapshot - Total: {TotalMB:F1}MB, ML: {MLMB:F1}MB, Models: {ModelCount}, Leaks: {LeakCount}");

    private static readonly Action<ILogger, string, Exception?> LogMemoryLeak =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(29, nameof(LogMemoryLeak)),
            "[ML-Memory] {Leak}");

    private static readonly Action<ILogger, Exception?> LogManagerDisposing =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(30, nameof(LogManagerDisposing)),
            "[ML-Memory] Disposing MLMemoryManager");

    public MLMemoryManager(ILogger<MLMemoryManager> logger, OnnxModelLoader onnxLoader)
    {
        _logger = logger;
        _onnxLoader = onnxLoader;
        
        // Initialize timers
        _garbageCollector = new Timer(CollectGarbage, null, Timeout.Infinite, Timeout.Infinite);
        _memoryMonitor = new Timer(MonitorMemory, null, Timeout.Infinite, Timeout.Infinite);
        
        LogManagerInitialized(_logger, null);
    }

    /// <summary>
    /// Initialize memory management timers and monitoring
    /// </summary>
    public Task InitializeMemoryManagementAsync()
    {
        LogServicesStarting(_logger, null);
        
        // Start garbage collection timer
        _garbageCollector.Change(TimeSpan.Zero, TimeSpan.FromMinutes(GC_COLLECTION_INTERVAL_MINUTES));
        
        // Start memory monitoring
        _memoryMonitor.Change(TimeSpan.Zero, TimeSpan.FromSeconds(MEMORY_MONITOR_INTERVAL_SECONDS));
        
        // Setup memory pressure notifications
        GC.RegisterForFullGCNotification(GC_NOTIFICATION_THRESHOLD, GC_NOTIFICATION_THRESHOLD);
        _ = Task.Run(StartGCMonitoring);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Load and manage ML model with memory tracking
    /// </summary>
    public async Task<T?> LoadModelAsync<T>(string modelPath, string version) where T : class
    {
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));
            
        var modelId = Path.GetFileNameWithoutExtension(modelPath);
        var versionKey = $"{modelId}_{version}";
        
        // Check if model already loaded
        if (_activeModels.TryGetValue(versionKey, out var existing))
        {
            existing.UsageCount++;
            existing.LastUsed = DateTime.UtcNow;
            LogModelReused(_logger, modelId, null);
            return existing.Model as T;
        }
        
        // Check memory before loading
        await EnsureMemoryAvailableAsync().ConfigureAwait(false);
        
        try
        {
            // Load real ONNX model using OnnxModelLoader
            var model = await LoadModelFromDiskAsync<T>(modelPath).ConfigureAwait(false);
            
            if (model == null)
            {
                LogModelLoadFailed(_logger, modelPath, null);
                return null;
            }
            
            // Measure memory footprint using memory pressure monitoring instead of forced GC
            var memoryBefore = GC.GetTotalMemory(false);
            var modelVersion = new ModelVersion
            {
                ModelId = modelId,
                Version = version,
                Model = model,
                LoadedAt = DateTime.UtcNow,
                UsageCount = 1,
                LastUsed = DateTime.UtcNow,
                WeakRef = new WeakReference(model)
            };
            
            // Monitor memory pressure instead of forcing collection
            var memoryAfter = GC.GetTotalMemory(false);
            modelVersion.MemoryFootprint = Math.Max(0, memoryAfter - memoryBefore);
            
            // Register for memory pressure notifications
            if (memoryAfter > MAX_MEMORY_BYTES * WARNING_THRESHOLD)
            {
                LogMemoryPressure(_logger, 
                    memoryAfter / BYTES_TO_MB, null);
            }
            
            _activeModels[versionKey] = modelVersion;
            lock (_lockObject)
            {
                _modelHistory.Enqueue(modelVersion);
            }
            
            // Cleanup old versions
            await CleanupOldVersionsAsync(modelId).ConfigureAwait(false);
            
            LogModelLoaded(_logger, modelId, version, modelVersion.MemoryFootprint / BYTES_TO_MB, null);
            
            return model;
        }
        catch (Exception ex)
        {
            LogModelLoadError(_logger, modelPath, ex);
            throw;
        }
    }

    /// <summary>
    /// Load ONNX models using professional Microsoft.ML.OnnxRuntime integration
    /// </summary>
    private async Task<T?> LoadModelFromDiskAsync<T>(string modelPath) where T : class
    {
        try
        {
            LogOnnxModelLoading(_logger, modelPath, null);
            
            if (!File.Exists(modelPath))
            {
                LogModelFileNotFound(_logger, modelPath, null);
                return null;
            }

            // Load ONNX model using professional OnnxModelLoader with validation
            var session = await _onnxLoader.LoadModelAsync(modelPath, validateInference: true).ConfigureAwait(false);
            
            if (session == null)
            {
                LogOnnxLoadFailed(_logger, modelPath, null);
                return null;
            }

            // Log model metadata for verification
            var inputCount = session.InputMetadata?.Count ?? 0;
            var outputCount = session.OutputMetadata?.Count ?? 0;
            LogOnnxModelLoadedSuccess(_logger, modelPath, inputCount, outputCount, null);
            
            // Return the InferenceSession with proper type casting
            return session as T;
        }
        catch (OnnxRuntimeException ex)
        {
            LogOnnxLoadError(_logger, modelPath, ex);
            return null;
        }
        catch (FileNotFoundException ex)
        {
            LogOnnxLoadError(_logger, modelPath, ex);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            LogOnnxLoadError(_logger, modelPath, ex);
            return null;
        }
        catch (ArgumentException ex)
        {
            LogOnnxLoadError(_logger, modelPath, ex);
            return null;
        }
    }

    private async Task EnsureMemoryAvailableAsync()
    {
        var currentMemory = GC.GetTotalMemory(false);
        
        if (currentMemory > MAX_MEMORY_BYTES * VERY_HIGH_THRESHOLD)
        {
            LogHighMemoryUsage(_logger, currentMemory / BYTES_TO_MB, null);
                
            // Intelligent cleanup based on usage patterns
            await PerformIntelligentCleanupAsync().ConfigureAwait(false);
            
            // Wait for memory pressure to reduce naturally
            var maxWaitTime = TimeSpan.FromSeconds(CLEANUP_WAIT_SECONDS);
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                currentMemory = GC.GetTotalMemory(false);
                if (currentMemory <= MAX_MEMORY_BYTES * HIGH_THRESHOLD)
                    break;
                    
                await Task.Delay(CLEANUP_DELAY_MS).ConfigureAwait(false);
            }
            
            // Only as last resort, suggest collection to runtime
            if (currentMemory > MAX_MEMORY_BYTES * CRITICAL_THRESHOLD)
            {
                LogCriticalMemory(_logger, null);
                GC.Collect(0, GCCollectionMode.Optimized, false); // Gentle suggestion only
                await Task.Delay(POST_GC_DELAY_MS).ConfigureAwait(false); // Give runtime time to respond
                
                currentMemory = GC.GetTotalMemory(false);
                if (currentMemory > MAX_MEMORY_BYTES * EMERGENCY_THRESHOLD)
                {
                    var memoryMB = currentMemory / BYTES_TO_MB;
                    throw new InvalidOperationException($"ML memory limit reached: {memoryMB}MB, cannot continue safely");
                }
            }
        }
    }
    
    /// <summary>
    /// Perform intelligent cleanup based on usage patterns instead of forced GC
    /// </summary>
    private async Task PerformIntelligentCleanupAsync()
    {
        await Task.Yield();
        
        // Remove models that haven't been used recently and have low usage counts
        var candidatesForRemoval = _activeModels.Values
            .Where(m => DateTime.UtcNow - m.LastUsed > TimeSpan.FromMinutes(10) && m.UsageCount < 5)
            .OrderBy(m => m.UsageCount)
            .ThenBy(m => m.LastUsed)
            .Take(_activeModels.Count / 3) // Remove up to 1/3 of unused models
            .ToList();
        
        foreach (var model in candidatesForRemoval)
        {
            var key = $"{model.ModelId}_{model.Version}";
            if (_activeModels.TryRemove(key, out var removed))
            {
                if (removed.Model is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed - safe to ignore
                    }
                }
                removed.Model = null;
                LogUnusedModelRemoved(_logger, key, null);
            }
        }
        
        // Clear any weak references that are no longer alive
        var deadReferences = _activeModels.Values
            .Where(m => m.WeakRef?.IsAlive == false)
            .ToList();
            
        foreach (var deadRef in deadReferences)
        {
            var key = $"{deadRef.ModelId}_{deadRef.Version}";
            _activeModels.TryRemove(key, out _);
        }
        
        LogCleanupCompleted(_logger, candidatesForRemoval.Count, deadReferences.Count, null);
    }

    private Task CleanupOldVersionsAsync(string modelId)
    {
        var versions = _activeModels.Values
            .Where(m => m.ModelId == modelId)
            .OrderByDescending(m => m.Version)
            .ToList();
        
        if (versions.Count > MAX_MODEL_VERSIONS)
        {
            // Keep only recent versions
            var toRemove = versions.Skip(MAX_MODEL_VERSIONS);
            
            foreach (var version in toRemove)
            {
                var key = $"{version.ModelId}_{version.Version}";
                if (_activeModels.TryRemove(key, out var removed))
                {
                    // Dispose if IDisposable
                    if (removed.Model is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Already disposed - safe to ignore
                        }
                    }
                    
                    // Clear strong reference
                    removed.Model = null;
                    
                    LogOldVersionRemoved(_logger, key, null);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void CollectGarbage(object? state)
    {
        try
        {
            var beforeMemory = GC.GetTotalMemory(false);
            
            // Remove unused models based on intelligent criteria
            var unusedModels = _activeModels.Values
                .Where(m => DateTime.UtcNow - m.LastUsed > TimeSpan.FromMinutes(UNUSED_MODEL_TIMEOUT_MINUTES) && m.UsageCount == 0)
                .ToList();
            
            var longUnusedModels = _activeModels.Values
                .Where(m => DateTime.UtcNow - m.LastUsed > TimeSpan.FromHours(LONG_UNUSED_MODEL_TIMEOUT_HOURS))
                .ToList();
                
            var modelsToRemove = unusedModels.Concat(longUnusedModels).Distinct().ToList();
            
            foreach (var model in modelsToRemove)
            {
                var key = $"{model.ModelId}_{model.Version}";
                if (_activeModels.TryRemove(key, out var removed))
                {
                    if (removed.Model is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Already disposed - safe to ignore
                        }
                    }
                    removed.Model = null;
                }
            }
            
            // Only suggest collection if memory pressure is actually high
            var currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > MAX_MEMORY_BYTES * VERY_HIGH_THRESHOLD)
            {
                // Use memory pressure APIs instead of forcing collection
                if (GC.TryStartNoGCRegion(NO_GC_REGION_SIZE_BYTES))
                {
                    // Do critical work if needed
                    GC.EndNoGCRegion();
                }
                
                // Gentle suggestion to runtime - not forced
                GC.Collect(0, GCCollectionMode.Optimized, false);
            }
            
            var afterMemory = GC.GetTotalMemory(false);
            var freedMemory = (beforeMemory - afterMemory) / BYTES_TO_MB;
            
            if (freedMemory > MEANINGFUL_CLEANUP_THRESHOLD_MB || modelsToRemove.Count > 0)
            {
                LogCleanupFreed(_logger, (long)freedMemory, modelsToRemove.Count, null);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogCleanupFailed(_logger, ex);
        }
        catch (OutOfMemoryException ex)
        {
            LogCleanupFailed(_logger, ex);
        }
    }

    private void MonitorMemory(object? state)
    {
        try
        {
            var snapshot = new MemorySnapshot
            {
                TotalMemory = GC.GetTotalMemory(false),
                UsedMemory = Process.GetCurrentProcess().WorkingSet64
            };
            
            // Calculate ML memory usage
            long mlMemory = 0;
            foreach (var model in _activeModels.Values)
            {
                snapshot.ModelMemory[model.ModelId] = model.MemoryFootprint;
                mlMemory += model.MemoryFootprint;
                
                // Check for memory leaks
                if (model.WeakRef?.IsAlive == true && model.UsageCount == 0 && 
                    DateTime.UtcNow - model.LastUsed > TimeSpan.FromHours(MEMORY_LEAK_DETECTION_HOURS))
                {
                    snapshot.AddMemoryLeak($"Potential leak: {model.ModelId} still in memory");
                }
            }
            
            snapshot.MLMemory = mlMemory;
            snapshot.LoadedModels = _activeModels.Count;
            
            // Alert if memory usage is high
            var memoryPercentage = (double)snapshot.UsedMemory / MAX_MEMORY_BYTES * 100;
            
            if (memoryPercentage > CRITICAL_MEMORY_PERCENTAGE)
            {
                LogCriticalMemoryPercentage(_logger, memoryPercentage, null);
                _ = Task.Run(AggressiveCleanupAsync);
            }
            else if (memoryPercentage > HIGH_MEMORY_PERCENTAGE)
            {
                LogHighMemoryPercentage(_logger, memoryPercentage, null);
            }
            
            // Log detailed snapshot periodically
            if (DateTime.UtcNow.Minute % MEMORY_SNAPSHOT_LOG_INTERVAL_MINUTES == 0)
            {
                LogMemorySnapshot(_logger, snapshot.TotalMemory / BYTES_TO_MB, snapshot.MLMemory / BYTES_TO_MB,
                    snapshot.LoadedModels, snapshot.MemoryLeaks.Count, null);
                    
                if (snapshot.MemoryLeaks.Any())
                {
                    foreach (var leak in snapshot.MemoryLeaks)
                    {
                        LogMemoryLeak(_logger, leak, null);
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            LogMonitoringFailed(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogMonitoringFailed(_logger, ex);
        }
    }

    private Task AggressiveCleanupAsync()
    {
        LogEmergencyCleanup(_logger, null);
        
        // Remove least recently used models with priority on memory footprint
        var modelsToUnload = _activeModels.Values
            .OrderBy(m => m.LastUsed)
            .ThenByDescending(m => m.MemoryFootprint) // Prioritize large models
            .Take(Math.Max(1, _activeModels.Count / 2)) // Remove up to half
            .ToList();
        
        var totalFreed = 0L;
        foreach (var model in modelsToUnload)
        {
            var key = $"{model.ModelId}_{model.Version}";
            if (_activeModels.TryRemove(key, out var removed))
            {
                totalFreed += removed.MemoryFootprint;
                if (removed.Model is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed - safe to ignore
                    }
                }
                removed.Model = null;
            }
        }
        
        // Clean up model history queue
        lock (_lockObject)
        {
            while (_modelHistory.Count > 5) // Keep only 5 recent entries
            {
                _modelHistory.Dequeue();
            }
        }
        
        // Suggest LOH compaction only if we freed significant memory
        if (totalFreed > LOH_COMPACTION_THRESHOLD_BYTES)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GEN2_COLLECTION_GENERATION, GCCollectionMode.Optimized, false); // Gentle suggestion
        }
        
        LogEmergencyCleanupCompleted(_logger, (long)(totalFreed / BYTES_TO_MB), modelsToUnload.Count, null);
        return Task.CompletedTask;
    }

    private void StartGCMonitoring()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_disposed)
                {
                    GCNotificationStatus status = GC.WaitForFullGCApproach();
                    if (status == GCNotificationStatus.Succeeded)
                    {
                        LogGCApproaching(_logger, null);
                    }
                    
                    status = GC.WaitForFullGCComplete();
                    if (status == GCNotificationStatus.Succeeded)
                    {
                        LogGCCompleted(_logger, null);
                    }
                    
                    await Task.Delay(GC_MONITORING_DELAY_MS).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex)
            {
                LogGCMonitoringFailed(_logger, ex);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                LogGCMonitoringFailed(_logger, ex);
            }
        });
    }



    /// <summary>
    /// Get current memory usage statistics
    /// </summary>
    public MemorySnapshot GetMemorySnapshot()
    {
        var snapshot = new MemorySnapshot
        {
            TotalMemory = GC.GetTotalMemory(false),
            UsedMemory = Process.GetCurrentProcess().WorkingSet64,
            LoadedModels = _activeModels.Count
        };
        
        long mlMemory = 0;
        foreach (var model in _activeModels.Values)
        {
            snapshot.ModelMemory[model.ModelId] = model.MemoryFootprint;
            mlMemory += model.MemoryFootprint;
        }
        snapshot.MLMemory = mlMemory;
        
        return snapshot;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                LogManagerDisposing(_logger, null);
                
                _garbageCollector?.Dispose();
                _memoryMonitor?.Dispose();
                
                // Cleanup all models
                foreach (var model in _activeModels.Values)
                {
                    if (model.Model is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Already disposed - safe to ignore
                        }
                    }
                }
                
                _activeModels.Clear();
            }
            _disposed = true;
        }
    }
}