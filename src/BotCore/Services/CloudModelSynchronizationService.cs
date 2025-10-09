using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.IO.Compression;
using BotCore.ML;
using BotCore.Brain;

namespace BotCore.Services;

/// <summary>
/// Production-grade service that automatically synchronizes trained models from GitHub Actions workflows
/// Enhances existing UnifiedTradingBrain by providing fresh cloud-trained models with comprehensive error handling
/// </summary>
public class CloudModelSynchronizationService : BackgroundService
{
    private static readonly JsonSerializerOptions s_jsonOptionsSnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    // Version display constants
    private const int GitShaDisplayLength = 8; // Number of git SHA characters to display
    
    private readonly ILogger<CloudModelSynchronizationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly UnifiedTradingBrain? _tradingBrain;
    private readonly string _githubToken;
    private readonly string _repositoryOwner;
    private readonly string _repositoryName;
    private readonly string _modelsDirectory;
    private readonly TimeSpan _syncInterval;
    
    // Track model versions and performance
    private readonly Dictionary<string, ModelInfo> _currentModels = new();
    private readonly object _syncLock = new();
    private DateTime _lastSyncTime = DateTime.MinValue;

    public CloudModelSynchronizationService(
        ILogger<CloudModelSynchronizationService> logger,
        HttpClient httpClient,
        IMLMemoryManager memoryManager,
        IConfiguration configuration,
        UnifiedTradingBrain? tradingBrain = null,
        ProductionResilienceService? resilienceService = null,
        ProductionMonitoringService? monitoringService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _tradingBrain = tradingBrain;
        ArgumentNullException.ThrowIfNull(memoryManager);
        ArgumentNullException.ThrowIfNull(configuration);
        
        // Configure GitHub API access with validation
        _githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? 
                      configuration["GitHub:Token"] ?? "";
        
        if (string.IsNullOrWhiteSpace(_githubToken))
        {
            _logger.LogWarning("⚠️ [CLOUD-SYNC] GitHub token not configured - cloud model sync will be disabled");
        }
        
        _repositoryOwner = configuration["GitHub:Owner"] ?? "c-trading-bo";
        _repositoryName = configuration["GitHub:Repository"] ?? "trading-bot-c-";
        _modelsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "models");
        _syncInterval = TimeSpan.FromMinutes(int.Parse(configuration["CloudSync:IntervalMinutes"] ?? "15", CultureInfo.InvariantCulture));
        
        // Configure HTTP client for GitHub API with proper headers
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TradingBot-CloudSync/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Allow time for large model downloads
        if (!string.IsNullOrEmpty(_githubToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_githubToken}");
        }
        
        // Ensure models directory exists
        Directory.CreateDirectory(_modelsDirectory);
        Directory.CreateDirectory(Path.Combine(_modelsDirectory, "rl"));
        Directory.CreateDirectory(Path.Combine(_modelsDirectory, "cloud"));
        Directory.CreateDirectory(Path.Combine(_modelsDirectory, "ensemble"));
        
        // Ensure data directories exist
        var baseDir = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(Path.Combine(baseDir, "datasets"));
        Directory.CreateDirectory(Path.Combine(baseDir, "datasets", "features"));
        Directory.CreateDirectory(Path.Combine(baseDir, "datasets", "regime_output"));
        Directory.CreateDirectory(Path.Combine(baseDir, "datasets", "news_flags"));
        Directory.CreateDirectory(Path.Combine(baseDir, "Intelligence", "data"));
        Directory.CreateDirectory(Path.Combine(baseDir, "Intelligence", "data", "macro"));
        Directory.CreateDirectory(Path.Combine(baseDir, "Intelligence", "data", "regime"));
        
        _logger.LogInformation("🌐 [CLOUD-SYNC] Service initialized - Repository: {Owner}/{Repo}, Sync interval: {Interval}", 
            _repositoryOwner, _repositoryName, _syncInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🌐 [CLOUD-SYNC] Starting automated model synchronization...");
        
        // Initial sync on startup
        await SynchronizeModelsAsync(stoppingToken).ConfigureAwait(false);
        
        // Continue periodic sync
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncInterval, stoppingToken).ConfigureAwait(false);
                await SynchronizeModelsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🌐 [CLOUD-SYNC] Error in sync loop");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false); // Wait before retry
            }
        }
        
        _logger.LogInformation("🌐 [CLOUD-SYNC] Service stopped");
    }

    /// <summary>
    /// Synchronize all models from GitHub Actions artifacts
    /// </summary>
    public async Task SynchronizeModelsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_githubToken))
        {
            _logger.LogWarning("🌐 [CLOUD-SYNC] No GitHub token configured, skipping sync");
            return;
        }

        lock (_syncLock)
        {
            if (DateTime.UtcNow - _lastSyncTime < TimeSpan.FromMinutes(5))
            {
                return; // Rate limiting
            }
            _lastSyncTime = DateTime.UtcNow;
        }

        try
        {
            _logger.LogInformation("🌐 [CLOUD-SYNC] Starting model synchronization...");
            
            // Get completed workflow runs
            var workflowRuns = await GetCompletedWorkflowRunsAsync(cancellationToken).ConfigureAwait(false);
            
            var syncedCount = 0;
            var newModelCount = 0;
            
            foreach (var run in workflowRuns)
            {
                try
                {
                    var artifacts = await GetWorkflowArtifactsAsync(run.Id, cancellationToken).ConfigureAwait(false);
                    
                    foreach (var artifact in artifacts.Where(a => 
                        a.Name.Contains("model", StringComparison.OrdinalIgnoreCase) || 
                        a.Name.Contains("onnx", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains("data-features", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains("regime-outputs", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains("news-flags", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains("trained-models", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Check if this is a data artifact or model artifact
                        if (artifact.Name.Contains("data-features", StringComparison.OrdinalIgnoreCase) ||
                            artifact.Name.Contains("regime-outputs", StringComparison.OrdinalIgnoreCase) ||
                            artifact.Name.Contains("news-flags", StringComparison.OrdinalIgnoreCase))
                        {
                            var wasNew = await ExtractDataArtifactAsync(artifact, run, cancellationToken).ConfigureAwait(false);
                            syncedCount++;
                            if (wasNew) newModelCount++;
                        }
                        else
                        {
                            var wasNew = await DownloadAndUpdateModelAsync(artifact, run, cancellationToken).ConfigureAwait(false);
                            syncedCount++;
                            if (wasNew) newModelCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "🌐 [CLOUD-SYNC] Failed to process workflow run {RunId}", run.Id);
                }
            }
            
            // Update model registry after sync
            await UpdateModelRegistryAsync(cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("🌐 [CLOUD-SYNC] Sync completed - {Total} artifacts processed, {New} new models downloaded", 
                syncedCount, newModelCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🌐 [CLOUD-SYNC] Model synchronization failed");
        }
    }

    /// <summary>
    /// Get completed workflow runs from GitHub API
    /// </summary>
    private async Task<List<WorkflowRun>> GetCompletedWorkflowRunsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/actions/runs?status=completed&per_page=50";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("🌐 [CLOUD-SYNC] GitHub API request failed: {StatusCode}", response.StatusCode);
                return new List<WorkflowRun>();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GitHubWorkflowRunsResponse>(content, s_jsonOptionsSnakeCase);
            
            // Filter for ML training workflows
            var mlWorkflows = result?.WorkflowRuns?.Where(r => 
                r.Name.Contains("train", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("ml", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("rl", StringComparison.OrdinalIgnoreCase) ||
                r.WorkflowId == 0 || // Include all if we can't filter
                r.Conclusion == "success"
            ).ToList() ?? new List<WorkflowRun>();
            
            _logger.LogDebug("🌐 [CLOUD-SYNC] Found {Count} completed ML workflow runs", mlWorkflows.Count);
            return mlWorkflows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🌐 [CLOUD-SYNC] Failed to get workflow runs");
            return new List<WorkflowRun>();
        }
    }

    /// <summary>
    /// Get artifacts for a specific workflow run
    /// </summary>
    private async Task<System.Collections.Generic.IReadOnlyList<Artifact>> GetWorkflowArtifactsAsync(long runId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/actions/runs/{runId}/artifacts";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                return System.Array.Empty<Artifact>();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GitHubArtifactsResponse>(content, s_jsonOptionsSnakeCase);
            
            return result?.Artifacts ?? System.Array.Empty<Artifact>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🌐 [CLOUD-SYNC] Failed to get artifacts for run {RunId}", runId);
            return System.Array.Empty<Artifact>();
        }
    }

    /// <summary>
    /// Download and update a model from artifact
    /// </summary>
    private async Task<bool> DownloadAndUpdateModelAsync(Artifact artifact, WorkflowRun run, CancellationToken cancellationToken)
    {
        try
        {
            // Check if we already have this model version
            var modelKey = $"{artifact.Name}_{run.HeadSha[..8]}";
            if (_currentModels.ContainsKey(modelKey))
            {
                return false; // Not new
            }
            
            _logger.LogInformation("🌐 [CLOUD-SYNC] Downloading new model: {Name} from run {RunId}", artifact.Name, run.Id);
            
            // Download artifact
            var downloadUrl = $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/actions/artifacts/{artifact.Id}/zip";
            var downloadResponse = await _httpClient.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
            
            if (!downloadResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("🌐 [CLOUD-SYNC] Failed to download artifact {ArtifactId}", artifact.Id);
                return false;
            }
            
            // Extract and save model
            using var zipStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            
            var extracted = false;
            string? onnxModelPath = null;
            
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.EndsWith(".onnx", StringComparison.Ordinal) || entry.Name.EndsWith(".pkl", StringComparison.Ordinal) || entry.Name.EndsWith(".json", StringComparison.Ordinal))
                {
                    var targetPath = DetermineModelPath(artifact.Name, entry.Name);
                    await ExtractAndSaveFileAsync(entry, targetPath, cancellationToken).ConfigureAwait(false);
                    
                    // Track ONNX model path for hot-swap
                    if (entry.Name.EndsWith(".onnx", StringComparison.Ordinal))
                    {
                        onnxModelPath = targetPath;
                    }
                    
                    // Update model info
                    _currentModels[modelKey] = new ModelInfo
                    {
                        Name = artifact.Name,
                        Version = run.HeadSha[..GitShaDisplayLength],
                        Path = targetPath,
                        DownloadedAt = DateTime.UtcNow,
                        WorkflowRun = run.Id,
                        Size = entry.Length
                    };
                    
                    extracted = true;
                    _logger.LogInformation("🌐 [CLOUD-SYNC] Model extracted: {Path}", targetPath);
                }
            }
            
            // Trigger hot-swap in UnifiedTradingBrain if we downloaded a new ONNX model
            if (extracted && onnxModelPath != null && _tradingBrain != null)
            {
                try
                {
                    _logger.LogInformation("🔄 [CLOUD-SYNC] Triggering model hot-swap for: {ModelPath}", onnxModelPath);
                    var reloadSuccess = await _tradingBrain.ReloadModelsAsync(onnxModelPath, cancellationToken).ConfigureAwait(false);
                    
                    if (reloadSuccess)
                    {
                        _logger.LogInformation("✅ [CLOUD-SYNC] Model hot-swap completed successfully");
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [CLOUD-SYNC] Model hot-swap failed - keeping current model");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [CLOUD-SYNC] Exception during model hot-swap");
                }
            }
            
            return extracted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🌐 [CLOUD-SYNC] Failed to download model {ArtifactName}", artifact.Name);
            return false;
        }
    }

    /// <summary>
    /// Extract data artifact (parquet, json) to datasets directory
    /// </summary>
    private async Task<bool> ExtractDataArtifactAsync(Artifact artifact, WorkflowRun run, CancellationToken cancellationToken)
    {
        try
        {
            // Check if we already have this data version
            var dataKey = $"{artifact.Name}_{run.HeadSha[..8]}";
            if (_currentModels.ContainsKey(dataKey))
            {
                return false; // Not new
            }
            
            _logger.LogInformation("🌐 [CLOUD-SYNC] Downloading data artifact: {Name} from run {RunId}", artifact.Name, run.Id);
            
            // Download artifact
            var downloadUrl = $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/actions/artifacts/{artifact.Id}/zip";
            var downloadResponse = await _httpClient.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
            
            if (!downloadResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("🌐 [CLOUD-SYNC] Failed to download data artifact {ArtifactId}", artifact.Id);
                return false;
            }
            
            // Extract and save data files
            using var zipStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            
            var extracted = false;
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || 
                    entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var targetPath = DetermineDataPath(entry.FullName);
                    await ExtractAndSaveFileAsync(entry, targetPath, cancellationToken).ConfigureAwait(false);
                    
                    extracted = true;
                    _logger.LogInformation("🌐 [CLOUD-SYNC] Extracted data file: {FileName}", entry.Name);
                }
            }
            
            if (extracted)
            {
                // Track that we've processed this data artifact
                _currentModels[dataKey] = new ModelInfo
                {
                    Name = artifact.Name,
                    Version = run.HeadSha[..GitShaDisplayLength],
                    Path = "datasets",
                    DownloadedAt = DateTime.UtcNow,
                    WorkflowRun = run.Id,
                    Size = 0
                };
            }
            
            return extracted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🌐 [CLOUD-SYNC] Failed to extract data artifact {ArtifactName}", artifact.Name);
            return false;
        }
    }

    /// <summary>
    /// Determine where to save data files based on entry path
    /// </summary>
    private static string DetermineDataPath(string entryFullName)
    {
        var baseDir = Directory.GetCurrentDirectory();
        
        // Preserve folder structure from the zip
        // If entry contains path separators, preserve the structure
        if (entryFullName.Contains('/', StringComparison.Ordinal) || entryFullName.Contains('\\', StringComparison.Ordinal))
        {
            // Normalize path separators
            var normalizedPath = entryFullName.Replace('\\', '/');
            
            // Extract just the filename if it's at the root
            var fileName = Path.GetFileName(normalizedPath);
            
            // Determine target directory based on naming conventions
            if (normalizedPath.Contains("features", StringComparison.OrdinalIgnoreCase) || 
                fileName.Contains("features", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("market_features", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "datasets", "features", fileName);
            }
            else if (normalizedPath.Contains("regime", StringComparison.OrdinalIgnoreCase) || 
                     fileName.Contains("regime", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Contains("market_state", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "datasets", "regime_output", fileName);
            }
            else if (normalizedPath.Contains("news", StringComparison.OrdinalIgnoreCase) || 
                     fileName.Contains("news", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Contains("government_releases", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "datasets", "news_flags", fileName);
            }
            else if (normalizedPath.Contains("quotes", StringComparison.OrdinalIgnoreCase) || 
                     fileName.Contains("quotes", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "datasets", "quotes", fileName);
            }
            else if (normalizedPath.Contains("macro", StringComparison.OrdinalIgnoreCase) || 
                     fileName.Contains("macro", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Contains("vix", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Contains("spx", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Contains("ndx", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "Intelligence", "data", "macro", fileName);
            }
            else if (normalizedPath.Contains("cot", StringComparison.OrdinalIgnoreCase) || 
                     fileName.Contains("cot", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseDir, "Intelligence", "data", "cot", fileName);
            }
        }
        
        // Default to datasets/features
        var defaultFileName = Path.GetFileName(entryFullName);
        return Path.Combine(baseDir, "datasets", "features", defaultFileName);
    }

    /// <summary>
    /// Determine where to save the model based on artifact name
    /// </summary>
    private string DetermineModelPath(string artifactName, string fileName)
    {
        var upperName = artifactName.ToUpperInvariant();
        
        if (upperName.Contains("CVAR", StringComparison.Ordinal) || upperName.Contains("PPO", StringComparison.Ordinal) || upperName.Contains("RL", StringComparison.Ordinal))
        {
            return Path.Combine(_modelsDirectory, "rl", fileName);
        }
        else if (upperName.Contains("ENSEMBLE", StringComparison.Ordinal) || upperName.Contains("BLEND", StringComparison.Ordinal))
        {
            return Path.Combine(_modelsDirectory, "ensemble", fileName);
        }
        else if (upperName.Contains("CLOUD", StringComparison.Ordinal))
        {
            return Path.Combine(_modelsDirectory, "cloud", fileName);
        }
        else
        {
            return Path.Combine(_modelsDirectory, fileName);
        }
    }

    /// <summary>
    /// Extract and save file from zip entry with atomic write operation
    /// </summary>
    private async Task ExtractAndSaveFileAsync(ZipArchiveEntry entry, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        
        // Write to transient file first for atomic operation
        var tempPath = targetPath + $".tmp_{Guid.NewGuid():N}";
        
        try
        {
            // Extract to temp file
            using (var entryStream = entry.Open())
            using (var fileStream = File.Create(tempPath))
            {
                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            
            // Create backup if original exists
            if (File.Exists(targetPath))
            {
                var backupPath = targetPath + $".backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                File.Move(targetPath, backupPath);
                _logger.LogDebug("🌐 [CLOUD-SYNC] Created backup: {BackupPath}", backupPath);
            }
            
            // Atomic replace: temp to target in single operation
            File.Move(tempPath, targetPath, overwrite: true);
            _logger.LogDebug("🌐 [CLOUD-SYNC] Atomically saved: {TargetPath}", targetPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation during shutdown - cleanup and rethrow to allow graceful termination
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            throw;
        }
        catch (Exception ex)
        {
            // Cleanup temp file on failure
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            // Rethrow with context - caller will log with appropriate context
            throw new InvalidOperationException($"Failed to save file: {targetPath}", ex);
        }
    }

    /// <summary>
    /// Update the model registry with new models
    /// </summary>
    private async Task UpdateModelRegistryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var registryPath = Path.Combine(_modelsDirectory, "model_registry.json");
            var registry = new ModelRegistry
            {
                LastUpdated = DateTime.UtcNow,
                TotalModels = _currentModels.Count
            };
            
            // Add models to the collection property
            foreach (var model in _currentModels.Values)
            {
                registry.AddModel(model);
            }
            
            var json = JsonSerializer.Serialize(registry, s_jsonOptions);
            await File.WriteAllTextAsync(registryPath, json, cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("🌐 [CLOUD-SYNC] Model registry updated with {Count} models", _currentModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🌐 [CLOUD-SYNC] Failed to update model registry");
        }
    }

    /// <summary>
    /// Get current model information for external services
    /// </summary>
    public Dictionary<string, ModelInfo> GetCurrentModels()
    {
        lock (_syncLock)
        {
            return new Dictionary<string, ModelInfo>(_currentModels);
        }
    }

    /// <summary>
    /// Force immediate synchronization
    /// </summary>
    public Task ForceSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌐 [CLOUD-SYNC] Force sync requested");
        return SynchronizeModelsAsync(cancellationToken);
    }
}

#region Data Models

public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
    public long WorkflowRun { get; set; }
    public long Size { get; set; }
}

public class ModelRegistry
{
    private readonly System.Collections.Generic.List<ModelInfo> _models = new();
    
    public DateTime LastUpdated { get; set; }
    public System.Collections.Generic.IReadOnlyList<ModelInfo> Models => _models;
    public int TotalModels { get; set; }
    
    public void AddModel(ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _models.Add(model);
    }
}

public class GitHubWorkflowRunsResponse
{
    public System.Collections.Generic.IReadOnlyList<WorkflowRun> WorkflowRuns { get; init; } = System.Array.Empty<WorkflowRun>();
}

public class WorkflowRun
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HeadSha { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public long WorkflowId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GitHubArtifactsResponse
{
    public System.Collections.Generic.IReadOnlyList<Artifact> Artifacts { get; init; } = System.Array.Empty<Artifact>();
}

public class Artifact
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

#endregion