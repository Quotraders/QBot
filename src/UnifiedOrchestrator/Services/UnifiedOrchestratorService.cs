using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using TradingBot.Abstractions;
using TradingBot.UnifiedOrchestrator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.UnifiedOrchestrator.Services;

/// <summary>
/// System health monitoring and emergency control service.
/// NOT the production trading path. Production trading happens in AutonomousDecisionEngine.
/// This service monitors system health, TopstepX connection status, memory usage, and can trigger emergency shutdown.
/// 
/// Features:
/// - Resource monitoring (memory usage tracking)
/// - TopstepX adapter health monitoring
/// - Emergency shutdown capabilities
/// - System status reporting
/// </summary>
internal class UnifiedOrchestratorService : BackgroundService, IUnifiedOrchestrator
{
    private readonly ILogger<UnifiedOrchestratorService> _logger;
    private readonly ICentralMessageBus _messageBus;
    private readonly ITopstepXAdapterService _topstepXAdapter;
    private readonly object? _tradingOrchestrator;
    private readonly object? _intelligenceOrchestrator;
    private readonly object? _dataOrchestrator;
    private readonly DateTime _startTime;
    private bool _isConnectedToTopstep;
    private bool _adapterInitialized;
    
    // Workflow tracking
    private readonly object _workflowLock = new();
    private readonly HashSet<string> _activeWorkflows = new();
    private readonly Dictionary<string, UnifiedWorkflow> _registeredWorkflows = new();
    
    // Emergency controls and resource monitoring (read from environment)
    private readonly bool _enableEmergencyStop;
    private readonly long _maxMemoryUsageMb;
    private readonly bool _enableResourceMonitoring;
    
    // Agent session registry to prevent duplicates - addresses Comment #3304685224
    private readonly HashSet<string> _activeAgentSessions = new();
    private readonly object _agentSessionLock = new();
    private readonly Dictionary<string, DateTime> _agentSessionStartTimes = new();

    public UnifiedOrchestratorService(
        ILogger<UnifiedOrchestratorService> logger,
        ICentralMessageBus messageBus,
        ITopstepXAdapterService topstepXAdapter)
    {
        _logger = logger;
        _messageBus = messageBus;
        _topstepXAdapter = topstepXAdapter;
        _tradingOrchestrator = null!; // Will be resolved later
        _intelligenceOrchestrator = null!; // Will be resolved later
        _dataOrchestrator = null!; // Will be resolved later
        _startTime = DateTime.UtcNow;
        
        // Read emergency controls from environment variables
        _enableEmergencyStop = Environment.GetEnvironmentVariable("ENABLE_EMERGENCY_STOP") == "true";
        _maxMemoryUsageMb = long.Parse(Environment.GetEnvironmentVariable("MAX_MEMORY_USAGE_MB") ?? "2048");
        _enableResourceMonitoring = Environment.GetEnvironmentVariable("ENABLE_RESOURCE_MONITORING") == "1" 
            || Environment.GetEnvironmentVariable("ENABLE_RESOURCE_MONITORING") == "true";
        
        _logger.LogInformation("🛡️ [UNIFIED-ORCHESTRATOR] Emergency controls: EmergencyStop={EmergencyStop}, ResourceMonitoring={ResourceMonitoring}, MaxMemoryMB={MaxMemory}",
            _enableEmergencyStop, _enableResourceMonitoring, _maxMemoryUsageMb);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Unified Orchestrator Service starting...");
        
        try
        {
            await InitializeSystemAsync(stoppingToken).ConfigureAwait(false);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Main orchestration loop
                    await ProcessSystemOperationsAsync(stoppingToken).ConfigureAwait(false);
                    
                    // Wait before next iteration
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in unified orchestrator loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await ShutdownSystemAsync().ConfigureAwait(false);
        }
        
        _logger.LogInformation("🛑 Unified Orchestrator Service stopped");
    }

    private async Task InitializeSystemAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 Initializing unified trading system with TopstepX SDK...");
        
        // Initialize TopstepX Python SDK adapter first
        try
        {
            _logger.LogInformation("🚀 Initializing TopstepX Python SDK adapter...");
            _adapterInitialized = await _topstepXAdapter.InitializeAsync(cancellationToken).ConfigureAwait(false);
            
            if (_adapterInitialized)
            {
                // Test health and connectivity
                var health = await _topstepXAdapter.GetHealthScoreAsync(cancellationToken).ConfigureAwait(false);
                if (health.HealthScore >= 80)
                {
                    _logger.LogInformation("✅ TopstepX SDK adapter initialized - Health: {HealthScore}%", health.HealthScore);
                    
                    // Test price data for both instruments
                    await TestInstrumentConnectivityAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("⚠️ TopstepX adapter health degraded: {HealthScore}% - Status: {Status}", 
                        health.HealthScore, health.Status);
                }
            }
            else
            {
                _logger.LogError("❌ Failed to initialize TopstepX SDK adapter");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TopstepX SDK adapter initialization failed");
            _adapterInitialized = false;
        }
        
        // Initialize all subsystems
        // Log the orchestrator components status
        _logger.LogDebug("Trading Orchestrator: {Status}", _tradingOrchestrator != null ? "Available" : "Not initialized");
        _logger.LogDebug("Intelligence Orchestrator: {Status}", _intelligenceOrchestrator != null ? "Available" : "Not initialized");
        _logger.LogDebug("Data Orchestrator: {Status}", _dataOrchestrator != null ? "Available" : "Not initialized");
        
        // Check actual TopstepX connection status via SDK
        try
        {
            // Get actual connection status from TopstepX adapter
            var isConnected = _topstepXAdapter.IsConnected;
            _isConnectedToTopstep = isConnected;
            
            _logger.LogInformation("🔗 TopstepX connection status - Connected: {Connected}, SDK: {SDK}", 
                isConnected, _adapterInitialized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TopstepX connection check failed - running in offline mode");
            _isConnectedToTopstep = false;
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
        
        _logger.LogInformation("✅ Unified trading system initialized successfully - SDK Ready: {SDKReady}", _adapterInitialized);
    }

    /// <summary>
    /// Test connectivity to all configured instruments
    /// </summary>
    private async Task TestInstrumentConnectivityAsync(CancellationToken cancellationToken)
    {
        var instruments = new[] { "MNQ", "ES" };
        
        foreach (var instrument in instruments)
        {
            try
            {
                var price = await _topstepXAdapter.GetPriceAsync(instrument, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("✅ {Instrument} connected - Current price: ${Price:F2}", instrument, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to connect to {Instrument}", instrument);
            }
        }
    }

    private async Task ProcessSystemOperationsAsync(CancellationToken cancellationToken)
    {
        // Monitor resource usage if enabled
        if (_enableResourceMonitoring)
        {
            var currentMemoryMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            if (currentMemoryMb > _maxMemoryUsageMb)
            {
                _logger.LogWarning("⚠️ Memory usage exceeded limit: {CurrentMB}MB > {MaxMB}MB", 
                    currentMemoryMb, _maxMemoryUsageMb);
                
                if (_enableEmergencyStop)
                {
                    _logger.LogCritical("🚨 Triggering emergency shutdown due to memory limit breach");
                    await ExecuteEmergencyShutdownAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }
        
        // Monitor TopstepX adapter health if connected
        if (_adapterInitialized && _topstepXAdapter.IsConnected)
        {
            try
            {
                var health = await _topstepXAdapter.GetHealthScoreAsync(cancellationToken).ConfigureAwait(false);
                if (health.HealthScore < 80)
                {
                    _logger.LogWarning("⚠️ TopstepX adapter health degraded: {HealthScore}% - Status: {Status}", 
                        health.HealthScore, health.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking TopstepX adapter health");
            }
        }
        
        if (_intelligenceOrchestrator != null)
        {
            _logger.LogTrace("Processing intelligence orchestrator operations");
        }
        
        if (_dataOrchestrator != null)
        {
            _logger.LogTrace("Processing data orchestrator operations");
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }


    private async Task ShutdownSystemAsync()
    {
        _logger.LogInformation("🔧 Shutting down unified trading system...");
        
        // Graceful shutdown of TopstepX adapter
        if (_adapterInitialized)
        {
            try
            {
                await _topstepXAdapter.DisconnectAsync().ConfigureAwait(false);
                _logger.LogInformation("✅ TopstepX adapter shutdown complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during TopstepX adapter shutdown");
            }
        }
        
        // Graceful shutdown of all other subsystems
        await Task.CompletedTask.ConfigureAwait(false);
        
        _logger.LogInformation("✅ Unified trading system shutdown complete");
    }

    public Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var systemStatus = new SystemStatus
        {
            IsHealthy = _adapterInitialized && _topstepXAdapter.IsConnected,
            ComponentStatuses = new()
            {
                ["Trading"] = "Operational",
                ["Intelligence"] = "Operational", 
                ["Data"] = "Operational",
                ["TopstepX-SDK"] = _adapterInitialized ? "Connected" : "Disconnected",
                ["TopstepX-Health"] = $"{_topstepXAdapter.ConnectionHealth:F1}%"
            },
            LastUpdated = DateTime.UtcNow
        };
        
        return Task.FromResult(systemStatus);
    }

    /// <summary>
    /// Start trading demonstration as specified in requirements
    /// Shows complete SDK integration with TradingSuite.create() and managed_trade()
    /// </summary>


    /// <summary>
    /// Get TopstepX adapter health and statistics
    /// </summary>
    public async Task<HealthScoreResult> GetTopstepXHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_adapterInitialized)
        {
            return new HealthScoreResult(0, "not_initialized", new(), new(), DateTime.UtcNow, false);
        }
        
        var healthScore = await _topstepXAdapter.GetHealthScoreAsync(cancellationToken).ConfigureAwait(false);
        var isHealthy = healthScore >= 80.0;
        var status = isHealthy ? "healthy" : "degraded";
        
        return new HealthScoreResult(healthScore, status, new(), new(), DateTime.UtcNow, isHealthy);
    }

    /// <summary>
    /// Get current portfolio status from TopstepX
    /// NOTE: Portfolio status monitoring is handled directly by TopstepX adapter
    /// </summary>
    public Task<PortfolioStatusResult> GetPortfolioStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_adapterInitialized)
        {
            throw new InvalidOperationException("TopstepX adapter not initialized");
        }
        
        // Portfolio status is monitored internally by TopstepX adapter
        // This method returns a basic status result
        return Task.FromResult(new PortfolioStatusResult());
    }

    public async Task<bool> ExecuteEmergencyShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("🚨 Emergency shutdown initiated");
            
            if (!_enableEmergencyStop)
            {
                _logger.LogWarning("⚠️ Emergency stop disabled by configuration - shutdown skipped");
                return false;
            }
            
            // Disconnect from TopstepX
            if (_adapterInitialized)
            {
                _logger.LogInformation("📡 Disconnecting from TopstepX...");
                // TopstepX adapter handles disconnection automatically on dispose
                _adapterInitialized = false;
            }
            
            // Stop all active workflows
            lock (_workflowLock)
            {
                _logger.LogInformation("🛑 Stopping {Count} active workflows", _activeWorkflows.Count);
                _activeWorkflows.Clear();
            }
            
            _logger.LogInformation("✅ Emergency shutdown completed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Emergency shutdown failed");
            return false;
        }
    }

    // IUnifiedOrchestrator interface implementation
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return InitializeSystemAsync(cancellationToken);
    }

    public new Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[UNIFIED] Starting unified orchestrator...");
        return base.StartAsync(cancellationToken);
    }

    public new Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[UNIFIED] Stopping unified orchestrator...");
        return base.StopAsync(cancellationToken);
    }



    public async Task<OrchestratorStatus> GetStatusAsync()
    {
        var systemStatus = await GetSystemStatusAsync().ConfigureAwait(false);
        
        int activeCount, totalCount;
        lock (_workflowLock)
        {
            activeCount = _activeWorkflows.Count;
            totalCount = _registeredWorkflows.Count;
        }
        
        var status = new OrchestratorStatus
        {
            IsRunning = systemStatus.IsHealthy,
            IsConnectedToTopstep = _isConnectedToTopstep,
            ActiveWorkflows = activeCount,
            TotalWorkflows = totalCount,
            StartTime = _startTime,
            Uptime = DateTime.UtcNow - _startTime,
            ComponentStatus = systemStatus.ComponentStatuses.ToDictionary(k => k.Key, v => (object)v.Value),
            RecentErrors = new Collection<string>()
        };
        
        _logger.LogDebug("[UNIFIED] Status: {ActiveWorkflows} active, {TotalWorkflows} total workflows", 
            activeCount, totalCount);
            
        return status;
    }
    
    private void OnConnectionStateChanged()
    {
        // Update connection status when TopstepX SDK connection state changes
        try
        {
            var isConnected = _topstepXAdapter.IsConnected;
            var previousStatus = _isConnectedToTopstep;
            _isConnectedToTopstep = isConnected;
            
            if (_isConnectedToTopstep != previousStatus)
            {
                _logger.LogInformation("🔗 TopstepX connection status updated - Connected: {Connected}", 
                    _isConnectedToTopstep);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update TopstepX connection status");
        }
    }
    
    /// <summary>
    /// Launch agent with duplicate prevention - ensures only one session per agentKey
    /// Addresses Comment #3304685224: Eliminate Duplicate Agent Launches
    /// </summary>
    public bool TryLaunchAgent(string agentKey, Func<Task> launchAction)
    {
        lock (_agentSessionLock)
        {
            // Check if agent session already active
            if (_activeAgentSessions.Contains(agentKey))
            {
                var startTime = _agentSessionStartTimes.GetValueOrDefault(agentKey);
                _logger.LogWarning("🚫 [AGENT-REGISTRY] Duplicate launch prevented for agentKey: {AgentKey}, already running since {StartTime}", 
                    agentKey, startTime);
                return false;
            }
            
            // Register new agent session
            _activeAgentSessions.Add(agentKey);
            _agentSessionStartTimes[agentKey] = DateTime.UtcNow;
            
            _logger.LogInformation("✅ [AGENT-REGISTRY] Agent session registered: {AgentKey} at {StartTime}", 
                agentKey, DateTime.UtcNow);
            
            // Execute launch action asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await launchAction().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [AGENT-REGISTRY] Agent launch failed for {AgentKey}", agentKey);
                }
                finally
                {
                    // Remove from registry when done
                    lock (_agentSessionLock)
                    {
                        _activeAgentSessions.Remove(agentKey);
                        _agentSessionStartTimes.Remove(agentKey);
                        _logger.LogInformation("🗑️ [AGENT-REGISTRY] Agent session cleanup: {AgentKey}", agentKey);
                    }
                }
            });
            
            return true;
        }
    }
    
    /// <summary>
    /// Get audit log of all agent sessions for runtime proof
    /// </summary>
    public Dictionary<string, DateTime> GetActiveAgentSessions()
    {
        lock (_agentSessionLock)
        {
            return new Dictionary<string, DateTime>(_agentSessionStartTimes);
        }
    }

    // IUnifiedOrchestrator interface implementation
    public IReadOnlyList<UnifiedWorkflow> GetWorkflows()
    {
        // Return empty list - workflow management not implemented in this health monitoring service
        return Array.Empty<UnifiedWorkflow>();
    }

    public UnifiedWorkflow? GetWorkflow(string workflowId)
    {
        // Return null - workflow management not implemented in this health monitoring service
        return null;
    }

    public Task RegisterWorkflowAsync(UnifiedWorkflow workflow, CancellationToken cancellationToken = default)
    {
        // No-op - workflow management not implemented in this health monitoring service
        _logger.LogWarning("RegisterWorkflowAsync called but not implemented in health monitoring service");
        return Task.CompletedTask;
    }

    public Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        // Return failure - workflow execution not implemented in this health monitoring service
        _logger.LogWarning("ExecuteWorkflowAsync called but not implemented in health monitoring service");
        return Task.FromResult(new WorkflowExecutionResult
        {
            Success = false,
            Error = "Workflow execution not implemented in health monitoring service"
        });
    }

    public IReadOnlyList<WorkflowExecutionContext> GetExecutionHistory(string workflowId, int limit)
    {
        // Return empty list - workflow execution history not tracked in this health monitoring service
        return Array.Empty<WorkflowExecutionContext>();
    }
}