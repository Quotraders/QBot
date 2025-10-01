using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using TradingBot.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services;

/// <summary>
/// Production-ready kill switch service that enforces DRY_RUN mode when kill.txt exists
/// Following agent guardrails: "kill.txt always forces DRY_RUN"
/// Implements IKillSwitchWatcher for compatibility with legacy Safety module integrations
/// </summary>
public class ProductionKillSwitchService : IHostedService, IKillSwitchWatcher, IDisposable
{
    private readonly ILogger<ProductionKillSwitchService> _logger;
    private readonly KillSwitchConfiguration _config;
    private readonly FileSystemWatcher _fileWatcher;
    private readonly Timer _periodicCheck;
    private volatile bool _disposed;
    
    private static KillSwitchConfiguration? _staticConfig;

    // IKillSwitchWatcher implementation - for compatibility with legacy Safety module integrations
    public event EventHandler<KillSwitchToggledEventArgs>? KillSwitchToggled;
    public event EventHandler? OnKillSwitchActivated;
    
    // IKillSwitchWatcher property implementation (explicit to avoid conflict with static method)
    bool IKillSwitchWatcher.IsKillSwitchActive => IsKillSwitchActive();

    public ProductionKillSwitchService(ILogger<ProductionKillSwitchService> logger, IOptions<KillSwitchConfiguration> config)
    {
        _logger = logger;
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _staticConfig = _config; // Store for static methods
        
        // Validate configuration
        _config.Validate();
        
        // Resolve kill file path (support relative and absolute paths)
        var killFilePath = ResolveKillFilePath(_config.FilePath);
        var killFileDirectory = Path.GetDirectoryName(killFilePath) ?? Directory.GetCurrentDirectory();
        var killFileName = Path.GetFileName(killFilePath);
        
        // Ensure directory exists
        Directory.CreateDirectory(killFileDirectory);
        
        // Watch for kill file
        _fileWatcher = new FileSystemWatcher(killFileDirectory, killFileName)
        {
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        
        _fileWatcher.Created += OnKillFileDetected;
        _fileWatcher.Changed += OnKillFileDetected;
        
        // Periodic check as backup in case file watcher fails
        _periodicCheck = new Timer(PeriodicKillFileCheck, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_config.CheckIntervalMs));
        
        _logger.LogInformation("🛡️ [KILL-SWITCH] Production kill switch service initialized - monitoring for {File}", killFilePath);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🟢 [KILL-SWITCH] Kill switch monitoring started");
        CheckKillFileOnStartup();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔴 [KILL-SWITCH] Kill switch monitoring stopped");
        return Task.CompletedTask;
    }

    private void CheckKillFileOnStartup()
    {
        var killFilePath = ResolveKillFilePath(_config.FilePath);
        if (File.Exists(killFilePath))
        {
            _logger.LogCritical("🔴 [KILL-SWITCH] KILL FILE DETECTED ON STARTUP - Forcing DRY_RUN mode");
            EnforceDryRunMode("Startup detection");
        }
    }

    private void OnKillFileDetected(object sender, FileSystemEventArgs e)
    {
        _logger.LogCritical("🔴 [KILL-SWITCH] KILL FILE DETECTED - {EventType} - Forcing DRY_RUN mode", e.ChangeType);
        EnforceDryRunMode($"File event: {e.ChangeType}");
    }

    private void PeriodicKillFileCheck(object? state)
    {
        try
        {
            var killFilePath = ResolveKillFilePath(_config.FilePath);
            if (File.Exists(killFilePath))
            {
                _logger.LogCritical("🔴 [KILL-SWITCH] KILL FILE DETECTED (periodic check) - Forcing DRY_RUN mode");
                EnforceDryRunMode("Periodic check");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [KILL-SWITCH] Error during periodic kill file check");
        }
    }

    public void EnforceDryRunMode(string detectionMethod)
    {
        try
        {
            // Force DRY_RUN environment variable
            Environment.SetEnvironmentVariable("DRY_RUN", "true");
            Environment.SetEnvironmentVariable("EXECUTE", "false");
            Environment.SetEnvironmentVariable("AUTO_EXECUTE", "false");
            
            _logger.LogCritical("🛡️ [KILL-SWITCH] DRY_RUN MODE ENFORCED - Detection: {Method}", detectionMethod);
            _logger.LogCritical("🛡️ [KILL-SWITCH] All execution flags disabled for safety");
            
            // Create DRY_RUN marker file if enabled
            if (_config.CreateDryRunMarker)
            {
                CreateDryRunMarker(detectionMethod);
            }
            
            // Log kill file contents if available for debugging
            LogKillFileContents();
            
            // Fire IKillSwitchWatcher events for compatibility with legacy Safety module integrations
            try
            {
                KillSwitchToggled?.Invoke(this, new KillSwitchToggledEventArgs(true));
                OnKillSwitchActivated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception eventEx)
            {
                _logger.LogError(eventEx, "❌ [KILL-SWITCH] Error firing kill switch events");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [KILL-SWITCH] Critical error while enforcing DRY_RUN mode");
        }
    }

    private void CreateDryRunMarker(string reason)
    {
        try
        {
            var markerPath = ResolveKillFilePath(_config.DryRunMarkerPath);
            var markerDirectory = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrEmpty(markerDirectory))
            {
                Directory.CreateDirectory(markerDirectory);
            }
            
            var markerContent = $"""
                DRY_RUN MODE ENFORCED
                =====================
                Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                Reason: {reason}
                Process ID: {Environment.ProcessId}
                Kill File: {ResolveKillFilePath(_config.FilePath)}
                """;
                
            File.WriteAllText(markerPath, markerContent);
            _logger.LogInformation("📝 [KILL-SWITCH] DRY_RUN marker created: {MarkerPath}", markerPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [KILL-SWITCH] Could not create DRY_RUN marker file");
        }
    }

    private void LogKillFileContents()
    {
        try
        {
            var killFilePath = ResolveKillFilePath(_config.FilePath);
            if (File.Exists(killFilePath))
            {
                var contents = File.ReadAllText(killFilePath);
                if (!string.IsNullOrWhiteSpace(contents))
                {
                    _logger.LogInformation("📝 [KILL-SWITCH] Kill file contents: {Contents}", contents.Trim());
                }
                
                var fileInfo = new FileInfo(killFilePath);
                _logger.LogInformation("📅 [KILL-SWITCH] Kill file created: {Created}, modified: {Modified}", 
                    fileInfo.CreationTime, fileInfo.LastWriteTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [KILL-SWITCH] Could not read kill file contents");
        }
    }

    /// <summary>
    /// Resolve kill file path (support relative and absolute paths)
    /// </summary>
    private static string ResolveKillFilePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
    }

    /// <summary>
    /// Check if kill file exists (for use by other services)
    /// </summary>
    public static bool IsKillSwitchActive()
    {
        var config = _staticConfig;
        if (config == null)
        {
            // Fallback to default behavior if config not available
            return File.Exists("kill.txt");
        }
        
        var killFilePath = ResolveKillFilePath(config.FilePath);
        return File.Exists(killFilePath);
    }

    /// <summary>
    /// Get the current execution mode based on environment and kill switch
    /// Following guardrails: DRY_RUN precedence
    /// </summary>
    public static bool IsDryRunMode()
    {
        // Kill switch always forces DRY_RUN
        if (IsKillSwitchActive())
        {
            return true;
        }
        
        // Check environment variables with DRY_RUN precedence
        var dryRun = Environment.GetEnvironmentVariable("DRY_RUN");
        if (dryRun?.ToLowerInvariant() == "true")
        {
            return true;
        }
        
        var execute = Environment.GetEnvironmentVariable("EXECUTE");
        var autoExecute = Environment.GetEnvironmentVariable("AUTO_EXECUTE");
        
        // Default to DRY_RUN if execution flags are not explicitly true
        return execute?.ToLowerInvariant() != "true" && autoExecute?.ToLowerInvariant() != "true";
    }

    // IKillSwitchWatcher interface implementation
    public Task<bool> IsKillSwitchActiveAsync()
    {
        return Task.FromResult(IsKillSwitchActive());
    }

    public Task StartWatchingAsync()
    {
        // Already started in StartAsync, this is for interface compatibility
        return Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                _fileWatcher?.Dispose();
                _periodicCheck?.Dispose();
                
                _logger.LogDebug("🗑️ [KILL-SWITCH] Kill switch service disposed");
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown - ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🗑️ [KILL-SWITCH] Error disposing resources");
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}