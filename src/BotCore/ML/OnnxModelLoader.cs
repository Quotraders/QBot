using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using BotCore.Utilities;
using System.Globalization;

namespace BotCore.ML;

/// <summary>
/// Professional ONNX model loader with hot-reload, versioning, health probe, and integrated registry
/// Implements requirement 1.1: Model version check, hot-reload watcher, fallback order, health probe
/// Includes timestamped, hash-versioned model registry with metadata and health checks
/// </summary>
public sealed class OnnxModelLoader : IDisposable
{
    private readonly ILogger<OnnxModelLoader> _logger;
    private readonly ConcurrentDictionary<string, InferenceSession> _loadedSessions = new();
    private readonly ConcurrentDictionary<string, ModelMetadata> _modelMetadata = new();
    private readonly SessionOptions _sessionOptions;
    private readonly Timer _hotReloadTimer;
    private readonly string _modelsDirectory;
    private readonly Regex _modelNamePattern;
    private readonly ModelRegistryOptions _registryOptions;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _registryPath;
    private bool _disposed;

    // Model versioning pattern: {family}.{symbol}.{strategy}.{regime}.v{semver}+{sha}.onnx
    private static readonly Regex ModelVersionPattern = new(
        @"^(?<family>\w+)\.(?<symbol>\w+)\.(?<strategy>\w+)\.(?<regime>\w+)\.v(?<semver>\d+\.\d+\.\d+)\+(?<sha>[a-f0-9]{8})\.onnx$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Health probe synthetic data constants
    private const float HealthProbePriceReturn = 0.001f;        // 0.1% price return
    private const float HealthProbeTimeInTrade = 2.5f;          // 2.5 trading hours
    private const float HealthProbePnlPerUnit = 50.0f;          // PnL per unit
    private const float HealthProbeVolatility = 0.15f;          // 15% volatility
    private const float HealthProbeRsiValue = 0.6f;             // 60% RSI
    private const float HealthProbeBollingerPosition = 0.3f;    // Bollinger position
    private const float HealthProbeTrendingRegime = 1.0f;       // Trending regime indicator
    private const float HealthProbeRangingRegime = 0.0f;        // Not ranging
    private const float HealthProbeVolatileRegime = 0.0f;       // Not volatile
    private const float HealthProbeDefaultValue = 0.1f;         // Default small value
    private const int HealthProbeCategoricalModulus = 3;        // Modulus for categorical features (0, 1, 2)
    
    // Health probe feature indices
    private const int FeatureIndexPriceReturn = 0;
    private const int FeatureIndexTimeInTrade = 1;
    private const int FeatureIndexPnlPerUnit = 2;
    private const int FeatureIndexVolatility = 3;
    private const int FeatureIndexRsiValue = 4;
    private const int FeatureIndexBollingerPosition = 5;
    private const int FeatureIndexTrendingRegime = 6;
    private const int FeatureIndexRangingRegime = 7;
    private const int FeatureIndexVolatileRegime = 8;

    public event EventHandler<ModelHotReloadEventArgs>? ModelReloaded;
    public event EventHandler<ModelHealthEventArgs>? ModelHealthChanged;

    // Structured logging delegates
    private static readonly Action<ILogger, string, string, Exception?> LogLoaderInitialized =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1, nameof(LogLoaderInitialized)),
            "[ONNX-Loader] Initialized with models directory: {ModelsDir}, registry: {RegistryPath}, hot-reload enabled");

    private static readonly Action<ILogger, Exception?> LogModelPathEmpty =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, nameof(LogModelPathEmpty)),
            "[ONNX-Loader] Model path is null or empty");

    private static readonly Action<ILogger, string, Exception?> LogModelReused =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(3, nameof(LogModelReused)),
            "[ONNX-Loader] Reusing cached model: {ModelPath}");

    private static readonly Action<ILogger, string, string, Exception?> LogModelLoaded =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(4, nameof(LogModelLoaded)),
            "[ONNX-Loader] Model successfully loaded: {ModelPath} (version: {Version})");

    private static readonly Action<ILogger, string, Exception?> LogLoadingAttempt =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(5, nameof(LogLoadingAttempt)),
            "[ONNX-Loader] Attempting to load model: {ModelPath}");

    private static readonly Action<ILogger, string, string, Exception?> LogFallbackLoaded =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(6, nameof(LogFallbackLoaded)),
            "[ONNX-Loader] Loaded fallback model: {FallbackPath} (original: {OriginalPath})");

    private static readonly Action<ILogger, string, Exception?> LogLoadAttemptFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(7, nameof(LogLoadAttemptFailed)),
            "[ONNX-Loader] Failed to load model candidate: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogAllAttemptsFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8, nameof(LogAllAttemptsFailed)),
            "[ONNX-Loader] All model loading attempts failed for: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogFallbackError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(9, nameof(LogFallbackError)),
            "[ONNX-Loader] Error finding fallback candidates for: {ModelPath}");

    private static readonly Action<ILogger, double, int, int, Exception?> LogModelLoadSuccess =
        LoggerMessage.Define<double, int, int>(LogLevel.Information, new EventId(10, nameof(LogModelLoadSuccess)),
            "[ONNX-Loader] Model loaded in {Duration}ms: {InputCount} inputs, {OutputCount} outputs");

    private static readonly Action<ILogger, string, string, Exception?> LogHealthProbeFailed =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(11, nameof(LogHealthProbeFailed)),
            "[ONNX-Loader] Model failed health probe: {ModelPath} - {Error}");

    private static readonly Action<ILogger, string, Exception?> LogHealthProbePassed =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(12, nameof(LogHealthProbePassed)),
            "[ONNX-Loader] Model passed health probe: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogModelLoadError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(13, nameof(LogModelLoadError)),
            "[ONNX-Loader] Error loading model: {ModelPath}");

    private static readonly Action<ILogger, Exception?> LogRunningHealthProbe =
        LoggerMessage.Define(LogLevel.Debug, new EventId(14, nameof(LogRunningHealthProbe)),
            "[ONNX-Loader] Running health probe...");

    private static readonly Action<ILogger, double, int, Exception?> LogHealthProbeSuccess =
        LoggerMessage.Define<double, int>(LogLevel.Debug, new EventId(15, nameof(LogHealthProbeSuccess)),
            "[ONNX-Loader] Health probe passed in {Duration}ms with {OutputCount} outputs");

    private static readonly Action<ILogger, Exception?> LogCheckingUpdates =
        LoggerMessage.Define(LogLevel.Debug, new EventId(16, nameof(LogCheckingUpdates)),
            "[ONNX-Loader] Checking for model updates...");

    private static readonly Action<ILogger, string, string, string, Exception?> LogModelUpdateDetected =
        LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(17, nameof(LogModelUpdateDetected)),
            "[ONNX-Loader] Detected model update: {ModelFile} (v{OldVersion} → v{NewVersion})");

    private static readonly Action<ILogger, Exception?> LogHotReloadError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(18, nameof(LogHotReloadError)),
            "[ONNX-Loader] Error during hot-reload check");

    private static readonly Action<ILogger, string, string, Exception?> LogRegistryUpdate =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(19, nameof(LogRegistryUpdate)),
            "[HOT_RELOAD] Registry update detected: {MetadataFile} (modified: {LastWrite})");

    private static readonly Action<ILogger, Exception?> LogRegistryCheckError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(20, nameof(LogRegistryCheckError)),
            "[HOT_RELOAD] Error checking data/registry updates");

    private static readonly Action<ILogger, string, string, Exception?> LogSacUpdate =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(21, nameof(LogSacUpdate)),
            "[HOT_RELOAD] SAC model update detected: {SACFile} (modified: {LastWrite})");

    private static readonly Action<ILogger, Exception?> LogSacCheckError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(22, nameof(LogSacCheckError)),
            "[HOT_RELOAD] Error checking data/rl/sac updates");

    private static readonly Action<ILogger, string, Exception?> LogHotReloading =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(23, nameof(LogHotReloading)),
            "[ONNX-Loader] Hot-reloading model: {ModelFile}");

    private static readonly Action<ILogger, string, string, Exception?> LogHotReloadSuccess =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(24, nameof(LogHotReloadSuccess)),
            "[ONNX-Loader] ✅ Hot-reload successful: {ModelFile} (version: {Version})");

    private static readonly Action<ILogger, string, Exception?> LogHotReloadHealthFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(25, nameof(LogHotReloadHealthFailed)),
            "[ONNX-Loader] ❌ Hot-reload failed - model failed health probe: {ModelFile}");

    private static readonly Action<ILogger, string, Exception?> LogHotReloadException =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(26, nameof(LogHotReloadException)),
            "[ONNX-Loader] ❌ Hot-reload error: {ModelFile}");

    private static readonly Action<ILogger, string, Exception?> LogMetadataError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(27, nameof(LogMetadataError)),
            "[ONNX-Loader] Error getting model metadata: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogUnsupportedInputType =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(28, nameof(LogUnsupportedInputType)),
            "[ONNX-Loader] Unsupported input type for health probe: {Type}");

    private static readonly Action<ILogger, string, Exception?> LogCannedInputError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(29, nameof(LogCannedInputError)),
            "[ONNX-Loader] Error creating canned input for {InputName}");

    private static readonly Action<ILogger, string, Exception?> LogModelUnloaded =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(30, nameof(LogModelUnloaded)),
            "[ONNX-Loader] Model unloaded: {ModelKey}");

    public OnnxModelLoader(
        ILogger<OnnxModelLoader> logger, 
        string modelsDirectory = "models",
        IOptions<ModelRegistryOptions>? registryOptions = null)
    {
        _logger = logger;
        _modelsDirectory = modelsDirectory;
        _modelNamePattern = ModelVersionPattern;
        
        // Initialize registry options
        _registryOptions = registryOptions?.Value ?? new ModelRegistryOptions();
        _registryPath = _registryOptions.RegistryPath;
        
        // Configure JSON serialization
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
        // Configure ONNX Runtime session options for optimal performance
        _sessionOptions = new SessionOptions
        {
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            EnableProfiling = false, // Disable in production
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING
        };

        // Ensure models and registry directories exist
        Directory.CreateDirectory(_modelsDirectory);
        Directory.CreateDirectory(_registryPath);

        // Start hot-reload timer (60s polling as per requirement) using TimerHelper
        var hotReloadInterval = TimeSpan.FromSeconds(60);
        _hotReloadTimer = TimerHelper.CreateAsyncTimerWithImmediateStart(CheckForModelUpdates, hotReloadInterval);

        LogLoaderInitialized(_logger, _modelsDirectory, _registryPath, null);
    }

    /// <summary>
    /// Load ONNX model with versioning, fallback order, and health probe
    /// Fallback order: new → previous_good → last_known_good
    /// </summary>
    public async Task<InferenceSession?> LoadModelAsync(string modelPath, bool validateInference = true)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            LogModelPathEmpty(_logger, null);
            return null;
        }

        var modelKey = GetModelKey(modelPath);
        
        // Check if already loaded and up-to-date
        if (_loadedSessions.TryGetValue(modelKey, out var existingSession) && 
            _modelMetadata.TryGetValue(modelKey, out var existingMetadata))
        {
            var currentMetadata = await GetModelMetadataAsync(modelPath).ConfigureAwait(false);
            if (currentMetadata != null && currentMetadata.Checksum == existingMetadata.Checksum)
            {
                LogModelReused(_logger, modelPath, null);
                return existingSession;
            }
        }

        // Try loading with fallback order
        var loadResult = await LoadModelWithFallbackAsync(modelPath, validateInference).ConfigureAwait(false);
        
        if (loadResult.Session != null)
        {
            // Cache the loaded session and metadata
            _loadedSessions[modelKey] = loadResult.Session;
            _modelMetadata[modelKey] = loadResult.Metadata!;
            
            _logger.LogInformation("[ONNX-Loader] Model successfully loaded: {ModelPath} (version: {Version})", 
                modelPath, loadResult.Metadata?.Version ?? "unknown");
                
            // Emit model reload event
            ModelReloaded?.Invoke(this, new ModelHotReloadEventArgs
            {
                ModelKey = modelKey,
                ModelPath = modelPath,
                Version = loadResult.Metadata?.Version ?? "unknown",
                LoadedAt = DateTime.UtcNow,
                IsHealthy = loadResult.IsHealthy
            });
        }

        return loadResult.Session;
    }

    /// <summary>
    /// Load model with fallback order: new → previous_good → last_known_good
    /// </summary>
    private async Task<ModelLoadResult> LoadModelWithFallbackAsync(string modelPath, bool validateInference)
    {
        var fallbackCandidates = GetFallbackCandidates(modelPath);
        
        foreach (var candidate in fallbackCandidates)
        {
            try
            {
                LogLoadingAttempt(_logger, candidate, null);
                
                var result = await LoadSingleModelAsync(candidate, validateInference).ConfigureAwait(false);
                if (result.Session != null && result.IsHealthy)
                {
                    if (candidate != modelPath)
                    {
                        LogFallbackLoaded(_logger, candidate, modelPath, null);
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                LogLoadAttemptFailed(_logger, candidate, ex);
            }
        }
        
        LogAllAttemptsFailed(_logger, modelPath, null);
        return new ModelLoadResult { Session = null, IsHealthy = false };
    }

    /// <summary>
    /// Get fallback candidates in order: new → previous_good → last_known_good
    /// </summary>
    private List<string> GetFallbackCandidates(string modelPath)
    {
        var candidates = new List<string> { modelPath };
        
        try
        {
            var parsedModel = ParseModelPath(modelPath);
            if (parsedModel != null)
            {
                // Find previous versions in the same directory
                var directory = Path.GetDirectoryName(modelPath) ?? _modelsDirectory;
                var pattern = $"{parsedModel.Family}.{parsedModel.Symbol}.{parsedModel.Strategy}.{parsedModel.Regime}.v*.onnx";
                
                var versionedFiles = Directory.GetFiles(directory, pattern)
                    .Select(ParseModelPath)
                    .Where(p => p != null)
                    .OrderByDescending(p => p!.SemVer)
                    .ToList();
                
                // Add previous_good (previous version)
                var previousVersion = versionedFiles.Skip(1).FirstOrDefault();
                if (previousVersion != null)
                {
                    var previousPath = Path.Combine(directory, 
                        $"{previousVersion.Family}.{previousVersion.Symbol}.{previousVersion.Strategy}.{previousVersion.Regime}.v{previousVersion.SemVer}+{previousVersion.Sha}.onnx");
                    if (File.Exists(previousPath))
                    {
                        candidates.Add(previousPath);
                    }
                }
                
                // Add last_known_good (oldest stable version)
                var lastKnownGood = versionedFiles.LastOrDefault();
                if (lastKnownGood != null && lastKnownGood != previousVersion)
                {
                    var lastKnownPath = Path.Combine(directory, 
                        $"{lastKnownGood.Family}.{lastKnownGood.Symbol}.{lastKnownGood.Strategy}.{lastKnownGood.Regime}.v{lastKnownGood.SemVer}+{lastKnownGood.Sha}.onnx");
                    if (File.Exists(lastKnownPath))
                    {
                        candidates.Add(lastKnownPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogFallbackError(_logger, modelPath, ex);
        }
        
        return candidates.Distinct().ToList();
    }

    /// <summary>
    /// Load single model with validation and health probe
    /// </summary>
    private async Task<ModelLoadResult> LoadSingleModelAsync(string modelPath, bool validateInference)
    {
        if (!File.Exists(modelPath))
        {
            return new ModelLoadResult { Session = null, IsHealthy = false };
        }

        try
        {
            var startTime = DateTime.UtcNow;
            
            // Get model metadata
            var metadata = await GetModelMetadataAsync(modelPath).ConfigureAwait(false);
            
            // Load the ONNX model
            var session = new InferenceSession(modelPath, _sessionOptions);
            
            var loadDuration = DateTime.UtcNow - startTime;
            
            // Validate model structure
            var inputInfo = session.InputMetadata;
            var outputInfo = session.OutputMetadata;
            
            _logger.LogInformation("[ONNX-Loader] Model loaded in {Duration}ms: {InputCount} inputs, {OutputCount} outputs", 
                loadDuration.TotalMilliseconds, inputInfo.Count, outputInfo.Count);
            
            // Health probe: smoke-predict on canned feature row
            var isHealthy = true;
            if (validateInference)
            {
                var healthProbeResult = await HealthProbeAsync(session).ConfigureAwait(false);
                isHealthy = healthProbeResult.IsHealthy;
                
                if (!isHealthy)
                {
                    _logger.LogError("[ONNX-Loader] Model failed health probe: {ModelPath} - {Error}", 
                        modelPath, healthProbeResult.ErrorMessage);
                    session.Dispose();
                    return new ModelLoadResult { Session = null, IsHealthy = false };
                }
            }

            LogHealthProbePassed(_logger, modelPath, null);
            
            return new ModelLoadResult 
            { 
                Session = session, 
                Metadata = metadata, 
                IsHealthy = isHealthy 
            };
        }
        catch (Exception ex)
        {
            LogModelLoadError(_logger, modelPath, ex);
            return new ModelLoadResult { Session = null, IsHealthy = false };
        }
    }

    /// <summary>
    /// Health probe: smoke-predict on canned feature row
    /// </summary>
    private Task<HealthProbeResult> HealthProbeAsync(InferenceSession session)
    {
        try
        {
            LogRunningHealthProbe(_logger, null);
            
            var inputMetadata = session.InputMetadata;
            var inputs = new List<NamedOnnxValue>();

            // Create canned inputs based on model metadata
            foreach (var input in inputMetadata)
            {
                var inputName = input.Key;
                var inputType = input.Value.ElementType;
                var dimensions = input.Value.Dimensions;

                var cannedInput = CreateCannedInput(inputName, inputType, dimensions);
                if (cannedInput != null)
                {
                    inputs.Add(cannedInput);
                }
            }

            if (inputs.Count == 0)
            {
                return Task.FromResult(new HealthProbeResult 
                { 
                    IsHealthy = false, 
                    ErrorMessage = "No valid inputs created for health probe" 
                });
            }

            // Run inference with canned data
            var startTime = DateTime.UtcNow;
            using var results = session.Run(inputs);
            var inferenceDuration = DateTime.UtcNow - startTime;

            // Validate outputs
            var outputCount = results.Count;
            if (outputCount == 0)
            {
                return Task.FromResult(new HealthProbeResult 
                { 
                    IsHealthy = false, 
                    ErrorMessage = "Model produced no outputs during health probe" 
                });
            }

            // Check for NaN or invalid outputs
            foreach (var result in results)
            {
                if (result.Value is Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> tensor)
                {
                    var tensorArray = tensor.ToArray();
                    var hasNaN = Array.Exists(tensorArray, f => float.IsNaN(f) || float.IsInfinity(f));
                    if (hasNaN)
                    {
                        return Task.FromResult(new HealthProbeResult 
                        { 
                            IsHealthy = false, 
                            ErrorMessage = "Model output contains NaN or Infinity values" 
                        });
                    }
                }
            }

            _logger.LogDebug("[ONNX-Loader] Health probe passed in {Duration}ms with {OutputCount} outputs", 
                inferenceDuration.TotalMilliseconds, outputCount);

            return Task.FromResult(new HealthProbeResult 
            { 
                IsHealthy = true, 
                InferenceDurationMs = inferenceDuration.TotalMilliseconds 
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthProbeResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            });
        }
    }

    /// <summary>
    /// Hot-reload watcher - checks for model updates every 60 seconds
    /// </summary>
    private async Task CheckForModelUpdates()
    {
        try
        {
            LogCheckingUpdates(_logger, null);
            
            // 4️⃣ Enable Model Hot-Reload: Poll data/registry and data/rl/sac every 60s
            await CheckDataRegistryUpdatesAsync().ConfigureAwait(false);
            await CheckSACModelsUpdatesAsync().ConfigureAwait(false);
            
            var modelFiles = Directory.GetFiles(_modelsDirectory, "*.onnx", SearchOption.AllDirectories)
                .Where(f => _modelNamePattern.IsMatch(Path.GetFileName(f)))
                .ToList();

            foreach (var modelFile in modelFiles)
            {
                var modelKey = GetModelKey(modelFile);
                
                // Check if we have this model loaded
                if (_loadedSessions.ContainsKey(modelKey) && _modelMetadata.TryGetValue(modelKey, out var currentMetadata))
                {
                    var newMetadata = await GetModelMetadataAsync(modelFile).ConfigureAwait(false);
                    
                    // Check if newer version or different checksum
                    if (newMetadata != null && 
                        (newMetadata.SemVer > currentMetadata.SemVer || newMetadata.Checksum != currentMetadata.Checksum))
                    {
                        _logger.LogInformation("[ONNX-Loader] Detected model update: {ModelFile} (v{OldVersion} → v{NewVersion})", 
                            modelFile, currentMetadata.Version, newMetadata.Version);
                        
                        // Hot-reload the model
                        await HotReloadModelAsync(modelFile, modelKey).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHotReloadError(_logger, ex);
        }
    }

    /// <summary>
    /// 4️⃣ Check data/registry for updated model metadata
    /// </summary>
    private async Task CheckDataRegistryUpdatesAsync()
    {
        try
        {
            var registryDir = "data/registry";
            if (!Directory.Exists(registryDir))
            {
                Directory.CreateDirectory(registryDir);
                return;
            }

            var metadataFiles = Directory.GetFiles(registryDir, "*_latest.yaml", SearchOption.AllDirectories);
            foreach (var metadataFile in metadataFiles)
            {
                var lastWriteTime = File.GetLastWriteTimeUtc(metadataFile);
                var modelName = Path.GetFileNameWithoutExtension(metadataFile).Replace("_latest", "", StringComparison.Ordinal);
                
                // Check if this metadata file has been updated since last check
                var cacheKey = $"registry_{modelName}_lastcheck";
                if (!_modelMetadata.TryGetValue(cacheKey, out var metadata) || 
                    metadata.LoadedAt < lastWriteTime)
                {
                    _logger.LogInformation("[HOT_RELOAD] Registry update detected: {MetadataFile} (modified: {LastWrite})", 
                        Path.GetFileName(metadataFile), lastWriteTime);
                    
                    // Read and parse metadata to trigger model reload if needed
                    var content = await File.ReadAllTextAsync(metadataFile).ConfigureAwait(false);
                    await ParseMetadataAndTriggerReloadAsync(content, metadataFile).ConfigureAwait(false);
                    
                    // Update cache
                    _modelMetadata[cacheKey] = new ModelMetadata
                    {
                        Version = "registry_check",
                        LoadedAt = lastWriteTime,
                        IsHealthy = true
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogRegistryCheckError(_logger, ex);
        }
    }

    /// <summary>
    /// 4️⃣ Check data/rl/sac for updated SAC models
    /// </summary>
    private async Task CheckSACModelsUpdatesAsync()
    {
        try
        {
            var sacDir = "data/rl/sac";
            if (!Directory.Exists(sacDir))
            {
                Directory.CreateDirectory(sacDir);
                return;
            }

            var sacFiles = Directory.GetFiles(sacDir, "*.zip", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(sacDir, "*.pkl", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(sacDir, "*.pt", SearchOption.AllDirectories));

            foreach (var sacFile in sacFiles)
            {
                var lastWriteTime = File.GetLastWriteTimeUtc(sacFile);
                var modelName = Path.GetFileNameWithoutExtension(sacFile);
                
                // Check if this SAC model has been updated since last check
                var cacheKey = $"sac_{modelName}_lastcheck";
                if (!_modelMetadata.TryGetValue(cacheKey, out var sacMetadata) || 
                    sacMetadata.LoadedAt < lastWriteTime)
                {
                    _logger.LogInformation("[HOT_RELOAD] SAC model update detected: {SACFile} (modified: {LastWrite})", 
                        Path.GetFileName(sacFile), lastWriteTime);
                    
                    // Trigger SAC model reload in Python side
                    await TriggerSacModelReloadAsync(sacFile).ConfigureAwait(false);
                    
                    // Update cache
                    _modelMetadata[cacheKey] = new ModelMetadata
                    {
                        Version = "sac_check",
                        LoadedAt = lastWriteTime,
                        IsHealthy = true
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogSacCheckError(_logger, ex);
        }
    }

    /// <summary>
    /// Hot-reload a specific model
    /// </summary>
    private async Task HotReloadModelAsync(string modelFile, string modelKey)
    {
        try
        {
            LogHotReloading(_logger, modelFile, null);
            
            // Load new model with health probe
            var loadResult = await LoadSingleModelAsync(modelFile, true).ConfigureAwait(false);
            
            if (loadResult.Session != null && loadResult.IsHealthy)
            {
                // Dispose old session
                if (_loadedSessions.TryRemove(modelKey, out var oldSession))
                {
                    oldSession.Dispose();
                }
                
                // Update with new session and metadata
                _loadedSessions[modelKey] = loadResult.Session;
                _modelMetadata[modelKey] = loadResult.Metadata!;
                
                _logger.LogInformation("[ONNX-Loader] ✅ Hot-reload successful: {ModelFile} (version: {Version})", 
                    modelFile, loadResult.Metadata?.Version ?? "unknown");
                
                // Emit reload event
                ModelReloaded?.Invoke(this, new ModelHotReloadEventArgs
                {
                    ModelKey = modelKey,
                    ModelPath = modelFile,
                    Version = loadResult.Metadata?.Version ?? "unknown",
                    LoadedAt = DateTime.UtcNow,
                    IsHealthy = true
                });
            }
            else
            {
                LogHotReloadHealthFailed(_logger, modelFile, null);
                
                // Emit health event
                ModelHealthChanged?.Invoke(this, new ModelHealthEventArgs
                {
                    ModelKey = modelKey,
                    ModelPath = modelFile,
                    IsHealthy = false,
                    ErrorMessage = "Hot-reload failed - model failed health probe",
                    CheckedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            LogHotReloadException(_logger, modelFile, ex);
            
            ModelHealthChanged?.Invoke(this, new ModelHealthEventArgs
            {
                ModelKey = modelKey,
                ModelPath = modelFile,
                IsHealthy = false,
                ErrorMessage = $"Hot-reload error: {ex.Message}",
                CheckedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Parse model file name to extract version information
    /// </summary>
    private ParsedModelInfo? ParseModelPath(string modelPath)
    {
        var fileName = Path.GetFileName(modelPath);
        var match = _modelNamePattern.Match(fileName);
        
        if (!match.Success)
        {
            return null;
        }
        
        if (!Version.TryParse(match.Groups["semver"].Value, out var semVer))
        {
            return null;
        }
        
        return new ParsedModelInfo
        {
            Family = match.Groups["family"].Value,
            Symbol = match.Groups["symbol"].Value,
            Strategy = match.Groups["strategy"].Value,
            Regime = match.Groups["regime"].Value,
            SemVer = semVer,
            Sha = match.Groups["sha"].Value
        };
    }

    /// <summary>
    /// Get model metadata including version and checksum
    /// </summary>
    private async Task<ModelMetadata?> GetModelMetadataAsync(string modelPath)
    {
        try
        {
            var parsedInfo = ParseModelPath(modelPath);
            if (parsedInfo == null)
            {
                return null;
            }
            
            // Calculate file checksum
            var checksum = await CalculateFileChecksumAsync(modelPath).ConfigureAwait(false);
            
            return new ModelMetadata
            {
                ModelPath = modelPath,
                Family = parsedInfo.Family,
                Symbol = parsedInfo.Symbol,
                Strategy = parsedInfo.Strategy,
                Regime = parsedInfo.Regime,
                SemVer = parsedInfo.SemVer,
                Sha = parsedInfo.Sha,
                Version = $"{parsedInfo.SemVer}+{parsedInfo.Sha}",
                Checksum = checksum,
                LastModified = File.GetLastWriteTimeUtc(modelPath)
            };
        }
        catch (Exception ex)
        {
            LogMetadataError(_logger, modelPath, ex);
            return null;
        }
    }

    /// <summary>
    /// Calculate SHA256 checksum of model file
    /// </summary>
    private static async Task<string> CalculateFileChecksumAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await Task.Run(() => sha256.ComputeHash(stream)).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    /// <summary>
    /// Get model key for caching
    /// </summary>
    private string GetModelKey(string modelPath)
    {
        var parsedInfo = ParseModelPath(modelPath);
        if (parsedInfo != null)
        {
            return $"{parsedInfo.Family}.{parsedInfo.Symbol}.{parsedInfo.Strategy}.{parsedInfo.Regime}";
        }
        return Path.GetFullPath(modelPath);
    }
    /// <summary>
    /// Create canned input data for health probe
    /// </summary>
    private NamedOnnxValue? CreateCannedInput(string inputName, System.Type elementType, int[] dimensions)
    {
        try
        {
            // Handle dynamic dimensions (replace -1 with 1)
            var safeDimensions = dimensions.Select(d => d == -1 ? 1 : d).ToArray();
            
            if (elementType == typeof(float))
            {
                var shape = safeDimensions;
                var totalElements = shape.Aggregate(1, (a, b) => a * b);
                var data = new float[totalElements];
                
                // Fill with realistic canned trading features
                for (int i = 0; i < totalElements; i++)
                {
                    data[i] = i switch
                    {
                        FeatureIndexPriceReturn => HealthProbePriceReturn,           // Price return: 0.1%
                        FeatureIndexTimeInTrade => HealthProbeTimeInTrade,           // Time in trade: 2.5 trading hours
                        FeatureIndexPnlPerUnit => HealthProbePnlPerUnit,             // PnL per unit
                        FeatureIndexVolatility => HealthProbeVolatility,             // Volatility: 15%
                        FeatureIndexRsiValue => HealthProbeRsiValue,                 // RSI: 60%
                        FeatureIndexBollingerPosition => HealthProbeBollingerPosition, // Bollinger position
                        FeatureIndexTrendingRegime => HealthProbeTrendingRegime,     // Trending regime
                        FeatureIndexRangingRegime => HealthProbeRangingRegime,       // Not ranging
                        FeatureIndexVolatileRegime => HealthProbeVolatileRegime,     // Not volatile
                        _ => HealthProbeDefaultValue                                 // Default small value
                    };
                }
                
                var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data, shape);
                return NamedOnnxValue.CreateFromTensor(inputName, tensor);
            }
            else if (elementType == typeof(long))
            {
                var shape = safeDimensions;
                var totalElements = shape.Aggregate(1, (a, b) => a * b);
                var data = new long[totalElements];
                
                // Fill with appropriate integer values
                for (int i = 0; i < totalElements; i++)
                {
                    data[i] = i % HealthProbeCategoricalModulus; // Values 0, 1, 2 for typical categorical features
                }
                
                var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(data, shape);
                return NamedOnnxValue.CreateFromTensor(inputName, tensor);
            }
            else if (elementType == typeof(int))
            {
                var shape = safeDimensions;
                var totalElements = shape.Aggregate(1, (a, b) => a * b);
                var data = new int[totalElements];
                
                for (int i = 0; i < totalElements; i++)
                {
                    data[i] = i % HealthProbeCategoricalModulus;
                }
                
                var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<int>(data, shape);
                return NamedOnnxValue.CreateFromTensor(inputName, tensor);
            }
            else
            {
                LogUnsupportedInputType(_logger, elementType.ToString(), null);
                return null;
            }
        }
        catch (Exception ex)
        {
            LogCannedInputError(_logger, inputName, ex);
            return null;
        }
    }

    /// <summary>
    /// Get a loaded model session by path or model key
    /// </summary>
    public InferenceSession? GetLoadedModel(string modelPathOrKey)
    {
        var modelKey = GetModelKey(modelPathOrKey);
        return _loadedSessions.TryGetValue(modelKey, out var session) ? session : null;
    }

    /// <summary>
    /// Get model metadata for a loaded model
    /// </summary>
    public ModelMetadata? GetModelMetadata(string modelPathOrKey)
    {
        var modelKey = GetModelKey(modelPathOrKey);
        return _modelMetadata.TryGetValue(modelKey, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// Unload a specific model to free memory
    /// </summary>
    public bool UnloadModel(string modelPathOrKey)
    {
        var modelKey = GetModelKey(modelPathOrKey);
        
        var unloaded = false;
        if (_loadedSessions.TryRemove(modelKey, out var session))
        {
            session.Dispose();
            unloaded = true;
        }
        
        _modelMetadata.TryRemove(modelKey, out _);
        
        if (unloaded)
        {
            LogModelUnloaded(_logger, modelKey, null);
        }
        
        return unloaded;
    }

    /// <summary>
    /// Get count of currently loaded models
    /// </summary>
    public int LoadedModelCount => _loadedSessions.Count;

    /// <summary>
    /// Get list of loaded model keys
    /// </summary>
    public IEnumerable<string> LoadedModelKeys => _loadedSessions.Keys;

    /// <summary>
    /// Get loaded model information
    /// </summary>
    public IEnumerable<LoadedModelInfo> GetLoadedModels()
    {
        return _loadedSessions.Keys.Select(key => new LoadedModelInfo
        {
            ModelKey = key,
            Metadata = _modelMetadata.GetValueOrDefault(key),
            LoadedAt = _modelMetadata.GetValueOrDefault(key)?.LastModified ?? DateTime.MinValue
        }).ToList();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("[ONNX-Loader] Disposing {ModelCount} loaded models", _loadedSessions.Count);
            
            // Stop hot-reload timer
            _hotReloadTimer?.Dispose();
            
            // Dispose all loaded sessions
            foreach (var session in _loadedSessions.Values)
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ONNX-Loader] Error disposing model session");
                }
            }
            
            _loadedSessions.Clear();
            _modelMetadata.Clear();
            _sessionOptions.Dispose();
            
            _disposed = true;
            _logger.LogInformation("[ONNX-Loader] Disposed successfully");
        }
    }

    #region Model Registry Methods

    /// <summary>
    /// Register a new model with timestamped, hash-versioned metadata
    /// </summary>
    public async Task<ModelRegistryEntry> RegisterModelAsync(
        string modelName,
        string modelPath,
        ModelRegistryMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Calculate model hash
            var modelHash = await CalculateFileHashAsync(modelPath, cancellationToken).ConfigureAwait(false);
            var timestamp = DateTime.UtcNow;
            var version = $"{timestamp:yyyyMMdd-HHmmss}-{modelHash[..8]}";

            // Create registry entry
            var entry = new ModelRegistryEntry
            {
                ModelName = modelName,
                Version = version,
                Timestamp = timestamp,
                Hash = modelHash,
                Metadata = metadata,
                OriginalPath = modelPath,
                Status = ModelRegistryStatus.Registered
            };

            // Create versioned directory
            var versionedDir = Path.Combine(_registryPath, modelName, version);
            Directory.CreateDirectory(versionedDir);

            // Atomic copy model file to registry using temp file + File.Move pattern
            var registryModelPath = Path.Combine(versionedDir, $"{modelName}.onnx");
            var tempModelPath = registryModelPath + ".tmp";
            
            try
            {
                // Step 1: Copy to transient file first
                File.Copy(modelPath, tempModelPath, overwrite: false);
                
                // Step 2: Atomic move to final destination
                if (File.Exists(registryModelPath))
                {
                    var backupPath = registryModelPath + ".backup";
                    File.Replace(tempModelPath, registryModelPath, backupPath);
                    _logger.LogDebug("[ONNX-Registry] Model deployed atomically with backup: {Model}, backup: {Backup}", 
                        Path.GetFileName(registryModelPath), Path.GetFileName(backupPath));
                }
                else
                {
                    File.Move(tempModelPath, registryModelPath);
                    _logger.LogDebug("[ONNX-Registry] Model deployed atomically: {Model}", Path.GetFileName(registryModelPath));
                }
            }
            catch
            {
                // Cleanup temp file on error
                if (File.Exists(tempModelPath))
                {
                    try 
                    { 
                        File.Delete(tempModelPath); 
                    } 
                    catch (Exception cleanupEx)
                    {
                        // Ignore cleanup errors - temp file deletion is best-effort
                        _logger.LogDebug(cleanupEx, "[ONNX-Loader] Failed to cleanup temp file: {TempPath}", tempModelPath);
                    }
                }
                throw;
            }
            
            entry.RegistryPath = registryModelPath;

            // Compress model if enabled
            if (_registryOptions.AutoCompress)
            {
                await CompressModelAsync(registryModelPath, cancellationToken).ConfigureAwait(false);
                entry.IsCompressed = true;
            }

            // Save metadata
            var metadataPath = Path.Combine(versionedDir, "metadata.json");
            var metadataJson = JsonSerializer.Serialize(entry, _jsonOptions);
            await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken).ConfigureAwait(false);

            // Update registry index
            await UpdateRegistryIndexAsync(entry, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("[ONNX-Registry] Model registered: {ModelName} v{Version}", modelName, version);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ONNX-Registry] Failed to register model: {ModelName}", modelName);
            throw;
        }
    }

    /// <summary>
    /// Get latest model version from registry
    /// </summary>
    public async Task<ModelRegistryEntry?> GetLatestRegisteredModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var modelDir = Path.Combine(_registryPath, modelName);
            if (!Directory.Exists(modelDir))
            {
                return null;
            }

            var versions = Directory.GetDirectories(modelDir)
                .Select(d => Path.GetFileName(d))
                .OrderByDescending(v => v)
                .ToList();

            if (versions.Count == 0)
            {
                return null;
            }

            var latestVersion = versions.First();
            var metadataPath = Path.Combine(modelDir, latestVersion, "metadata.json");
            
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var metadataJson = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<ModelRegistryEntry>(metadataJson, _jsonOptions);
            
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ONNX-Registry] Failed to get latest model: {ModelName}", modelName);
            return null;
        }
    }

    /// <summary>
    /// Perform comprehensive health check on registered models
    /// </summary>
    public async Task<ModelHealthReport> PerformRegistryHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var report = new ModelHealthReport
        {
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var modelDirs = Directory.GetDirectories(_registryPath);
            
            foreach (var modelDir in modelDirs)
            {
                var modelName = Path.GetFileName(modelDir);
                var latestModel = await GetLatestRegisteredModelAsync(modelName, cancellationToken).ConfigureAwait(false);
                
                var status = new ModelHealthStatus
                {
                    ModelName = modelName,
                    IsHealthy = false
                };

                if (latestModel == null)
                {
                    status.AddIssue("No valid model found");
                }
                else
                {
                    // Check if model file exists
                    if (!File.Exists(latestModel.RegistryPath))
                    {
                        status.AddIssue("Model file missing");
                    }
                    
                    // Check file integrity
                    var currentHash = await CalculateFileHashAsync(latestModel.RegistryPath, cancellationToken).ConfigureAwait(false);
                    if (currentHash != latestModel.Hash)
                    {
                        status.AddIssue("Model file hash mismatch - file may be corrupted");
                    }
                    
                    // Check age
                    var age = DateTime.UtcNow - latestModel.Timestamp;
                    if (age > TimeSpan.FromDays(_registryOptions.ModelExpiryDays))
                    {
                        status.AddIssue($"Model is {age.TotalDays:F1} days old (expires after {_registryOptions.ModelExpiryDays} days)");
                    }

                    status.IsHealthy = !status.Issues.Any();
                    status.LastUpdated = latestModel.Timestamp;
                    status.Version = latestModel.Version;
                }

                report.AddModelStatus(status);
            }

            report.IsHealthy = report.ModelStatuses.All(s => s.IsHealthy);
            _logger.LogInformation("[ONNX-Registry] Health check completed: {HealthyCount}/{TotalCount} healthy", 
                report.ModelStatuses.Count(s => s.IsHealthy), report.ModelStatuses.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ONNX-Registry] Failed to perform health check");
            report.IsHealthy = false;
        }

        return report;
    }

    /// <summary>
    /// Clean up old model versions
    /// </summary>
    public Task CleanupOldVersionsAsync(int keepVersions = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            var modelDirs = Directory.GetDirectories(_registryPath);
            
            foreach (var modelDir in modelDirs)
            {
                var modelName = Path.GetFileName(modelDir);
                var versions = Directory.GetDirectories(modelDir)
                    .Select(d => new { Path = d, Version = Path.GetFileName(d) })
                    .OrderByDescending(v => v.Version)
                    .ToList();

                if (versions.Count > keepVersions)
                {
                    var toDelete = versions.Skip(keepVersions);
                    foreach (var version in toDelete)
                    {
                        Directory.Delete(version.Path, recursive: true);
                        _logger.LogInformation("[ONNX-Registry] Cleaned up old model version: {ModelName} v{Version}", 
                            modelName, version.Version);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ONNX-Registry] Failed to cleanup old model versions");
        }
        
        return Task.CompletedTask;
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        var hashBytes = await Task.Run(() => sha256.ComputeHash(fileStream), cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToUpperInvariant();
    }

    private async Task CompressModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        // Compress model file using GZip compression
        await Task.Run(() =>
        {
            var compressedPath = modelPath + ".gz";
            
            using (var originalFileStream = File.OpenRead(modelPath))
            using (var compressedFileStream = File.Create(compressedPath))
            using (var compressionStream = new System.IO.Compression.GZipStream(compressedFileStream, System.IO.Compression.CompressionMode.Compress))
            {
                originalFileStream.CopyTo(compressionStream);
            }
            
            var originalSize = new FileInfo(modelPath).Length;
            var compressedSize = new FileInfo(compressedPath).Length;
            var compressionRatio = (double)compressedSize / originalSize;
            
            _logger.LogInformation("[ONNX-Registry] Model compressed: {ModelPath} (ratio: {Ratio:P1})", 
                modelPath, compressionRatio);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateRegistryIndexAsync(ModelRegistryEntry entry, CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(_registryPath, "registry_index.json");
        
        List<ModelRegistryEntry> index;
        if (File.Exists(indexPath))
        {
            var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            index = JsonSerializer.Deserialize<List<ModelRegistryEntry>>(indexJson, _jsonOptions) ?? new();
        }
        else
        {
            index = new List<ModelRegistryEntry>();
        }

        // Add or update entry
        var existingIndex = index.FindIndex(e => e.ModelName == entry.ModelName && e.Version == entry.Version);
        if (existingIndex >= 0)
        {
            index[existingIndex] = entry;
        }
        else
        {
            index.Add(entry);
        }

        // Save updated index
        var updatedIndexJson = JsonSerializer.Serialize(index, _jsonOptions);
        await File.WriteAllTextAsync(indexPath, updatedIndexJson, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse metadata and trigger model reload if needed
    /// </summary>
    private async Task ParseMetadataAndTriggerReloadAsync(string content, string metadataFile)
    {
        try
        {
            // Parse metadata content (could be YAML or JSON)
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(content);
            if (metadata != null && metadata.TryGetValue("version", out var version))
            {
                _logger.LogInformation("[MODEL_RELOAD] Model metadata parsed from {File}, version: {Version}", 
                    Path.GetFileName(metadataFile), version);
                
                // Trigger model reload notification
                await NotifyModelUpdateAsync(metadataFile, metadata).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[MODEL_RELOAD] Failed to parse metadata from {File}", metadataFile);
        }
    }

    /// <summary>
    /// Trigger SAC model reload in Python side
    /// </summary>
    private async Task TriggerSacModelReloadAsync(string sacFile)
    {
        try
        {
            _logger.LogInformation("[SAC_RELOAD] Triggering SAC model reload for {File}", Path.GetFileName(sacFile));
            
            // Send reload signal to Python SAC agent via file system signal
            var reloadSignalFile = Path.Combine(Path.GetDirectoryName(sacFile) ?? "", ".sac_reload_signal");
            await File.WriteAllTextAsync(reloadSignalFile, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            
            _logger.LogInformation("[SAC_RELOAD] Reload signal sent for SAC model");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[SAC_RELOAD] Failed to trigger SAC model reload for {File}", sacFile);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "[SAC_RELOAD] Failed to trigger SAC model reload for {File}", sacFile);
        }
    }

    /// <summary>
    /// Notify model update to interested systems
    /// </summary>
    private async Task NotifyModelUpdateAsync(string filePath, Dictionary<string, object> metadata)
    {
        try
        {
            // Create notification payload
            var notification = new
            {
                Type = "ModelUpdate",
                File = Path.GetFileName(filePath),
                Timestamp = DateTime.UtcNow,
                Metadata = metadata
            };

            // Write notification to model update queue (file-based for simplicity)
            var notificationDir = Path.Combine(Directory.GetCurrentDirectory(), "notifications");
            Directory.CreateDirectory(notificationDir);
            
            var notificationFile = Path.Combine(notificationDir, $"model_update_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var notificationJson = System.Text.Json.JsonSerializer.Serialize(notification, _jsonOptions);
            
            await File.WriteAllTextAsync(notificationFile, notificationJson).ConfigureAwait(false);
            _logger.LogInformation("[MODEL_NOTIFICATION] Model update notification created: {File}", notificationFile);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[MODEL_NOTIFICATION] Failed to create model update notification");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "[MODEL_NOTIFICATION] Failed to create model update notification");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[MODEL_NOTIFICATION] Failed to create model update notification");
        }
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Model metadata with version information and checksum
/// </summary>
public class ModelMetadata
{
    public string ModelPath { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Regime { get; set; } = string.Empty;
    public Version SemVer { get; set; } = new();
    public string Sha { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public DateTime LoadedAt { get; set; }
    public bool IsHealthy { get; set; } = true;
}

/// <summary>
/// Parsed model information from filename
/// </summary>
public class ParsedModelInfo
{
    public string Family { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Regime { get; set; } = string.Empty;
    public Version SemVer { get; set; } = new();
    public string Sha { get; set; } = string.Empty;
}

/// <summary>
/// Result of model loading operation
/// </summary>
public class ModelLoadResult
{
    public InferenceSession? Session { get; set; }
    public ModelMetadata? Metadata { get; set; }
    public bool IsHealthy { get; set; }
}

/// <summary>
/// Result of health probe operation
/// </summary>
public class HealthProbeResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public double InferenceDurationMs { get; set; }
}

/// <summary>
/// Event for model hot-reload notifications
/// </summary>
public class ModelHotReloadEventArgs : EventArgs
{
    public string ModelKey { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; }
    public bool IsHealthy { get; set; }
}

/// <summary>
/// Event for model health status changes
/// </summary>
public class ModelHealthEventArgs : EventArgs
{
    public string ModelKey { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// Information about a loaded model
/// </summary>
public class LoadedModelInfo
{
    public string ModelKey { get; set; } = string.Empty;
    public ModelMetadata? Metadata { get; set; }
    public DateTime LoadedAt { get; set; }
}

/// <summary>
/// Configuration options for model registry (merged from ModelRegistryService)
/// </summary>
public class ModelRegistryOptions
{
    public string RegistryPath { get; set; } = "models/registry";
    public bool AutoCompress { get; set; } = true;
    public int ModelExpiryDays { get; set; } = 30;
}

/// <summary>
/// Model registry entry with metadata (merged from ModelRegistryService)
/// </summary>
public class ModelRegistryEntry
{
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Hash { get; set; } = string.Empty;
    public ModelRegistryMetadata Metadata { get; set; } = new();
    public string OriginalPath { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public ModelRegistryStatus Status { get; set; }
    public bool IsCompressed { get; set; }
}

/// <summary>
/// Model registry metadata (merged from ModelRegistryService)
/// </summary>
public class ModelRegistryMetadata
{
    public DateTime TrainingDate { get; set; }
    public Dictionary<string, object> Hyperparams { get; } = new();
    public string TrainingDataHash { get; set; } = string.Empty;
    public double ValidationAccuracy { get; set; }
    public Dictionary<string, double> TrainingMetrics { get; } = new();
    public string Description { get; set; } = string.Empty;
    private readonly List<string> _tags = new();
    public IReadOnlyList<string> Tags => _tags;
    
    internal void AddTag(string tag) => _tags.Add(tag);
}

/// <summary>
/// Model registry status enumeration (merged from ModelRegistryService)
/// </summary>
public enum ModelRegistryStatus
{
    Registered,
    Active,
    Deprecated,
    Failed
}

/// <summary>
/// Model health report (merged from ModelRegistryService)
/// </summary>
public class ModelHealthReport
{
    public DateTime Timestamp { get; set; }
    public bool IsHealthy { get; set; }
    private readonly List<ModelHealthStatus> _modelStatuses = new();
    public IReadOnlyList<ModelHealthStatus> ModelStatuses => _modelStatuses;
    
    internal void AddModelStatus(ModelHealthStatus status) => _modelStatuses.Add(status);
}

/// <summary>
/// Individual model health status (merged from ModelRegistryService)
/// </summary>
public class ModelHealthStatus
{
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public DateTime LastUpdated { get; set; }
    private readonly List<string> _issues = new();
    public IReadOnlyList<string> Issues => _issues;
    
    internal void AddIssue(string issue) => _issues.Add(issue);
}

#endregion
