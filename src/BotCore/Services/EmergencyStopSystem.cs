using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using BotCore.Services;
using TradingBot.Abstractions;
using TopstepX.Bot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Emergency Stop System - Critical Safety Component
    /// Monitors for critical system failures only
    /// Kill switch is MANUAL or triggered by true system failures (not performance degradation)
    /// Integrates with ProductionKillSwitchService for coordinated shutdown
    /// </summary>
    public class EmergencyStopSystem : BackgroundService
    {
        private readonly ILogger<EmergencyStopSystem> _logger;
        private readonly EmergencyStopConfiguration _config;
        private readonly ProductionKillSwitchService _killSwitchService;
        private readonly string _killFilePath;
        private readonly CancellationTokenSource _emergencyStopSource;
        
        private FileSystemWatcher? _fileWatcher;
        private volatile bool _isEmergencyStop;
        
        public event EventHandler<EmergencyStopEventArgs>? EmergencyStopTriggered;
        
        public bool IsEmergencyStop => _isEmergencyStop;
        public CancellationToken EmergencyStopToken => _emergencyStopSource.Token;
        
        public EmergencyStopSystem(
            ILogger<EmergencyStopSystem> logger,
            IOptions<EmergencyStopConfiguration> config,
            ProductionKillSwitchService killSwitchService)
        {
            _logger = logger;
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _killSwitchService = killSwitchService ?? throw new ArgumentNullException(nameof(killSwitchService));
            _emergencyStopSource = new CancellationTokenSource();
            
            // Validate configuration
            _config.Validate();
            
            // Use kill file path from configuration (coordinated with KillSwitchService)
            _killFilePath = Path.Combine(Directory.GetCurrentDirectory(), "kill.txt"); // Fallback default
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Initial check for kill.txt
                CheckKillFile();
                
                // Setup file system watcher
                SetupFileWatcher();
                
                _logger.LogInformation("🛡️ Emergency Stop System initialized - monitoring {KillFile}", _killFilePath);
                
                // Keep monitoring until cancellation
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_config.MonitoringIntervalMs, stoppingToken).ConfigureAwait(false);
                    
                    // Periodic check in case file watcher fails
                    if (!_isEmergencyStop)
                    {
                        CheckKillFile();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ Emergency Stop System failed - access denied");
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "❌ Emergency Stop System failed - directory not found");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "❌ Emergency Stop System failed - I/O error");
            }
            catch (SecurityException ex)
            {
                _logger.LogError(ex, "❌ Emergency Stop System failed - security error");
            }
            finally
            {
                _fileWatcher?.Dispose();
            }
        }
        
        private void SetupFileWatcher()
        {
            try
            {
                var directory = Path.GetDirectoryName(_killFilePath) ?? Directory.GetCurrentDirectory();
                _fileWatcher = new FileSystemWatcher(directory, "kill.txt");
                
                _fileWatcher.Created += OnKillFileChanged;
                _fileWatcher.Changed += OnKillFileChanged;
                _fileWatcher.EnableRaisingEvents = true;
                
                _logger.LogDebug("📂 File watcher setup for {Directory}", directory);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Failed to setup file watcher - invalid argument");
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "❌ Failed to setup file watcher - directory not found");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ Failed to setup file watcher - access denied");
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError(ex, "❌ Failed to setup file watcher - not supported");
            }
        }
        
        private void OnKillFileChanged(object sender, FileSystemEventArgs e)
        {
            _logger.LogWarning("🚨 Kill file detected: {EventType}", e.ChangeType);
            CheckKillFile();
        }
        
        private void CheckKillFile()
        {
            try
            {
                if (File.Exists(_killFilePath))
                {
                    TriggerEmergencyStop("kill.txt file detected");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ Error checking kill file - access denied");
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "❌ Error checking kill file - directory not found");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "❌ Error checking kill file - I/O error");
            }
        }
        
        /// <summary>
        /// Manually trigger emergency stop
        /// </summary>
        public void TriggerEmergencyStop(string reason)
        {
            if (_isEmergencyStop) return;
            
            _isEmergencyStop = true;
            _emergencyStopSource.Cancel();
            
            _logger.LogCritical("🛑 [EMERGENCY-STOP] EMERGENCY STOP TRIGGERED: {Reason}", reason);
            
            // Create kill file to signal emergency stop to all processes
            try
            {
                var killFilePath = Path.Combine(_config.EmergencyLogDirectory, "kill.txt");
                var killFileContent = $"""
                    EMERGENCY STOP ACTIVATED
                    ========================
                    Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                    Reason: {reason}
                    Process ID: {Environment.ProcessId}
                    """;
                    
                File.WriteAllText(killFilePath, killFileContent);
                _logger.LogCritical("🛑 [EMERGENCY-STOP] Kill file created: {KillFilePath}", killFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-STOP] Failed to create kill file");
            }
            
            // Integrate with ProductionKillSwitchService for coordinated shutdown
            try
            {
                _killSwitchService.EnforceDryRunMode($"Emergency stop: {reason}");
                _logger.LogInformation("✅ [EMERGENCY-STOP] Successfully coordinated with kill switch service");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-STOP] Failed to coordinate with kill switch service");
            }
            
            var eventArgs = new EmergencyStopEventArgs
            {
                Reason = reason,
                Timestamp = DateTime.UtcNow
            };
            
            EmergencyStopTriggered?.Invoke(this, eventArgs);
            
            // Create emergency log file if enabled
            if (_config.EnableEmergencyLogging)
            {
                CreateEmergencyLog(reason);
            }
        }
        
        private void CreateEmergencyLog(string reason)
        {
            try
            {
                // Ensure emergency log directory exists
                Directory.CreateDirectory(_config.EmergencyLogDirectory);
                
                var logPath = Path.Combine(_config.EmergencyLogDirectory, $"emergency_stop_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                var logContent = $"""
                    EMERGENCY STOP EVENT
                    ====================
                    Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                    Reason: {reason}
                    Process ID: {Environment.ProcessId}
                    Machine: {Environment.MachineName}
                    User: {Environment.UserName}
                    
                    ACTIONS REQUIRED:
                    - Verify all positions are closed
                    - Check for pending orders
                    - Review trading logs
                    - Investigate root cause before restart
                    
                    GUARDRAIL STATUS:
                    - Kill switch active: {ProductionKillSwitchService.IsKillSwitchActive()}
                    - DRY_RUN mode: {ProductionKillSwitchService.IsDryRunMode()}
                    """;
                    
                File.WriteAllText(logPath, logContent);
                _logger.LogInformation("📋 [EMERGENCY-STOP] Emergency log created: {LogPath}", logPath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-STOP] Failed to create emergency log - IO error");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-STOP] Failed to create emergency log - access denied");
            }
            catch (SecurityException ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-STOP] Failed to create emergency log - security error");
            }
        }
        
        /// <summary>
        /// Reset emergency stop (requires manual intervention)
        /// </summary>
        public async Task<bool> ResetEmergencyStopAsync()
        {
            try
            {
                // Remove kill.txt if it exists
                if (File.Exists(_killFilePath))
                {
                    File.Delete(_killFilePath);
                    _logger.LogInformation("🗑️ kill.txt removed");
                }
                
                // Wait a moment
                await Task.Delay(_config.MonitoringIntervalMs).ConfigureAwait(false);
                
                // Reset state
                _isEmergencyStop = false;
                
                _logger.LogWarning("🔄 Emergency stop reset - system ready");
                return true;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "❌ Failed to reset emergency stop - IO error");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ Failed to reset emergency stop - access denied");
                return false;
            }
        }
        
        public override void Dispose()
        {
            _fileWatcher?.Dispose();
            _emergencyStopSource?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}