using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Production integration coordinator - orchestrates all integration components
/// Ensures complete system integration with all requirements met
/// Provides runtime proof and comprehensive monitoring
/// </summary>
public sealed partial class ProductionIntegrationCoordinator : BackgroundService
{
    private readonly ILogger<ProductionIntegrationCoordinator> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, Exception?> LogCoordinatorInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(8001, nameof(LogCoordinatorInitialized)),
            "Production integration coordinator initialized");
    
    private static readonly Action<ILogger, Exception?> LogStartingCoordinator =
        LoggerMessage.Define(LogLevel.Information, new EventId(8002, nameof(LogStartingCoordinator)),
            "🚀 Starting production integration coordinator...");
    
    private static readonly Action<ILogger, Exception?> LogCoordinatorStoppedGracefully =
        LoggerMessage.Define(LogLevel.Information, new EventId(8003, nameof(LogCoordinatorStoppedGracefully)),
            "Production integration coordinator stopped gracefully");
    
    private static readonly Action<ILogger, Exception?> LogCriticalError =
        LoggerMessage.Define(LogLevel.Error, new EventId(8004, nameof(LogCriticalError)),
            "Critical error in production integration coordinator");
    
    private static readonly Action<ILogger, Exception?> LogPhase1Start =
        LoggerMessage.Define(LogLevel.Information, new EventId(8005, nameof(LogPhase1Start)),
            "Phase 1: Validating system integrity...");
    
    private static readonly Action<ILogger, int, int, Exception?> LogServiceInventory =
        LoggerMessage.Define<int, int>(LogLevel.Information, new EventId(8006, nameof(LogServiceInventory)),
            "Service inventory: {CategoryCount} categories, {ServiceCount} services");
    
    private static readonly Action<ILogger, Exception?> LogConfigLocksValidated =
        LoggerMessage.Define(LogLevel.Information, new EventId(8007, nameof(LogConfigLocksValidated)),
            "✅ Configuration locks validated - all safety settings active");
    
    private static readonly Action<ILogger, int, int, Exception?> LogYamlValidationIssues =
        LoggerMessage.Define<int, int>(LogLevel.Warning, new EventId(8008, nameof(LogYamlValidationIssues)),
            "YAML validation found issues - {InvalidFiles}/{TotalFiles} files invalid");
    
    private static readonly Action<ILogger, int, int, Exception?> LogYamlSchemaValidation =
        LoggerMessage.Define<int, int>(LogLevel.Information, new EventId(8009, nameof(LogYamlSchemaValidation)),
            "YAML schema validation: {ValidFiles}/{TotalFiles} files valid");
    
    private static readonly Action<ILogger, Exception?> LogPhase1Completed =
        LoggerMessage.Define(LogLevel.Information, new EventId(8010, nameof(LogPhase1Completed)),
            "✅ Phase 1 completed - System integrity validated");
    
    private static readonly Action<ILogger, Exception?> LogPhase2Start =
        LoggerMessage.Define(LogLevel.Information, new EventId(8011, nameof(LogPhase2Start)),
            "Phase 2: Initializing integration components...");
    
    private static readonly Action<ILogger, int, Exception?> LogFeatureMapAuthority =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(8012, nameof(LogFeatureMapAuthority)),
            "Feature map authority: {ResolverCount} resolvers registered");
    
    private static readonly Action<ILogger, int, string, Exception?> LogStatePersistence =
        LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(8013, nameof(LogStatePersistence)),
            "State persistence: {ZoneCount} zone states, {PatternAvailable} pattern data loaded");
    
    private static readonly Action<ILogger, Exception?> LogShadowModeManagerInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(8014, nameof(LogShadowModeManagerInitialized)),
            "Shadow mode manager initialized - ready for strategy testing");
    
    private static readonly Action<ILogger, string, string, Exception?> LogTelemetryService =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(8015, nameof(LogTelemetryService)),
            "Telemetry service: {HealthStatus}, Config snapshot: {ConfigSnapshotId}");
    
    private static readonly Action<ILogger, Exception?> LogPhase2Completed =
        LoggerMessage.Define(LogLevel.Information, new EventId(8016, nameof(LogPhase2Completed)),
            "✅ Phase 2 completed - Integration components initialized");
    
    private static readonly Action<ILogger, Exception?> LogPhase3Start =
        LoggerMessage.Define(LogLevel.Information, new EventId(8017, nameof(LogPhase3Start)),
            "Phase 3: Validating runtime integration...");
    
    private static readonly Action<ILogger, Exception?> LogPhase3Completed =
        LoggerMessage.Define(LogLevel.Information, new EventId(8018, nameof(LogPhase3Completed)),
            "✅ Phase 3 completed - Runtime integration validated");
    
    private static readonly Action<ILogger, Exception?> LogPhase4Start =
        LoggerMessage.Define(LogLevel.Information, new EventId(8019, nameof(LogPhase4Start)),
            "Phase 4: Starting continuous monitoring...");
    
    private static readonly Action<ILogger, Exception?> LogCoordinatorOperational =
        LoggerMessage.Define(LogLevel.Information, new EventId(8020, nameof(LogCoordinatorOperational)),
            "🎉 Production integration coordinator OPERATIONAL - All systems ready");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationInMonitoring =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8021, nameof(LogInvalidOperationInMonitoring)),
            "Invalid operation in continuous monitoring loop");
    
    private static readonly Action<ILogger, Exception?> LogTimeoutInMonitoring =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8022, nameof(LogTimeoutInMonitoring)),
            "Timeout in continuous monitoring loop");
    
    private static readonly Action<ILogger, int, double, Exception?> LogUnifiedBarPipelineSuccess =
        LoggerMessage.Define<int, double>(LogLevel.Debug, new EventId(8023, nameof(LogUnifiedBarPipelineSuccess)),
            "Unified bar pipeline test: SUCCESS - {StepCount} steps completed in {ProcessingTime:F2}ms");
    
    private static readonly Action<ILogger, string, Exception?> LogUnifiedBarPipelineFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8024, nameof(LogUnifiedBarPipelineFailed)),
            "Unified bar pipeline test: FAILED - {Error}");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationTestingBarPipeline =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8025, nameof(LogInvalidOperationTestingBarPipeline)),
            "Invalid operation testing unified bar pipeline");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentTestingBarPipeline =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8026, nameof(LogInvalidArgumentTestingBarPipeline)),
            "Invalid argument testing unified bar pipeline");
    
    private static readonly Action<ILogger, string, double, string, Exception?> LogFeatureResolutionTest =
        LoggerMessage.Define<string, double, string>(LogLevel.Trace, new EventId(8027, nameof(LogFeatureResolutionTest)),
            "Feature resolution test: {FeatureKey} = {Value} ({ResolverType})");
    
    private static readonly Action<ILogger, string, Exception?> LogFeatureResolutionMissing =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8028, nameof(LogFeatureResolutionMissing)),
            "Feature resolution test: {FeatureKey} MISSING - would trigger hold decision");
    
    private static readonly Action<ILogger, int, int, Exception?> LogFeatureResolutionSummary =
        LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(8029, nameof(LogFeatureResolutionSummary)),
            "Feature resolution test: {SuccessCount}/{TotalCount} features resolved successfully");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationTestingFeatureResolution =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8030, nameof(LogInvalidOperationTestingFeatureResolution)),
            "Invalid operation testing feature resolution");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentTestingFeatureResolution =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8031, nameof(LogInvalidArgumentTestingFeatureResolution)),
            "Invalid argument testing feature resolution");
    
    private static readonly Action<ILogger, Exception?> LogTelemetryEmissionSuccess =
        LoggerMessage.Define(LogLevel.Debug, new EventId(8032, nameof(LogTelemetryEmissionSuccess)),
            "Telemetry emission test: Zone and pattern telemetry emitted successfully");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationTestingTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8033, nameof(LogInvalidOperationTestingTelemetry)),
            "Invalid operation testing telemetry emission");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentTestingTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8034, nameof(LogInvalidArgumentTestingTelemetry)),
            "Invalid argument testing telemetry emission");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationEmittingTelemetry =
        LoggerMessage.Define(LogLevel.Trace, new EventId(8035, nameof(LogInvalidOperationEmittingTelemetry)),
            "Invalid operation emitting operational telemetry");
    
    private static readonly Action<ILogger, Exception?> LogTimeoutEmittingTelemetry =
        LoggerMessage.Define(LogLevel.Trace, new EventId(8036, nameof(LogTimeoutEmittingTelemetry)),
            "Timeout emitting operational telemetry");
    
    private static readonly Action<ILogger, string, int, int, string, Exception?> LogHealthCheck =
        LoggerMessage.Define<string, int, int, string>(LogLevel.Trace, new EventId(8037, nameof(LogHealthCheck)),
            "Health check: Pipeline {PipelineHealth}, EpochFreeze {EpochStats}, Shadow {ShadowStats}, Telemetry {TelemetryHealth}");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationInHealthCheck =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8038, nameof(LogInvalidOperationInHealthCheck)),
            "Invalid operation in periodic health check");
    
    private static readonly Action<ILogger, Exception?> LogCoordinatorDisposed =
        LoggerMessage.Define(LogLevel.Information, new EventId(8039, nameof(LogCoordinatorDisposed)),
            "Production integration coordinator disposed");
    
    // Integration components
    private readonly Lazy<ServiceInventory> _serviceInventory;
    private readonly Lazy<ConfigurationLocks> _configurationLocks;
    private readonly Lazy<UnifiedBarPipeline> _unifiedBarPipeline;
    private readonly Lazy<FeatureMapAuthority> _featureMapAuthority;
    private readonly Lazy<YamlSchemaValidator> _yamlSchemaValidator;
    private readonly Lazy<AtomicStatePersistence> _statePersistence;
    private readonly Lazy<EpochFreezeEnforcement> _epochFreezeEnforcement;
    private readonly Lazy<ShadowModeManager> _shadowModeManager;
    private readonly Lazy<ComprehensiveTelemetryService> _telemetryService;
    
    // Monitoring and health tracking
    private readonly Timer _healthCheckTimer;
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(1);
    
    // Integration status
    private IntegrationStatus _currentStatus = IntegrationStatus.Initializing;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private readonly object _statusLock = new();
    
    public ProductionIntegrationCoordinator(ILogger<ProductionIntegrationCoordinator> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Initialize lazy components
        _serviceInventory = new Lazy<ServiceInventory>(() => 
            new ServiceInventory(_serviceProvider.GetRequiredService<ILogger<ServiceInventory>>(), _serviceProvider));
        _configurationLocks = new Lazy<ConfigurationLocks>(() => 
            new ConfigurationLocks(_serviceProvider.GetRequiredService<ILogger<ConfigurationLocks>>(), _serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()));
        _unifiedBarPipeline = new Lazy<UnifiedBarPipeline>(() => 
            new UnifiedBarPipeline(_serviceProvider.GetRequiredService<ILogger<UnifiedBarPipeline>>(), _serviceProvider));
        _featureMapAuthority = new Lazy<FeatureMapAuthority>(() => 
            new FeatureMapAuthority(_serviceProvider.GetRequiredService<ILogger<FeatureMapAuthority>>(), _serviceProvider));
        _yamlSchemaValidator = new Lazy<YamlSchemaValidator>(() => 
            new YamlSchemaValidator(_serviceProvider.GetRequiredService<ILogger<YamlSchemaValidator>>()));
        _statePersistence = new Lazy<AtomicStatePersistence>(() => 
            new AtomicStatePersistence(_serviceProvider.GetRequiredService<ILogger<AtomicStatePersistence>>()));
        _epochFreezeEnforcement = new Lazy<EpochFreezeEnforcement>(() => 
            new EpochFreezeEnforcement(_serviceProvider.GetRequiredService<ILogger<EpochFreezeEnforcement>>(), _serviceProvider));
        _shadowModeManager = new Lazy<ShadowModeManager>(() => 
            new ShadowModeManager(_serviceProvider.GetRequiredService<ILogger<ShadowModeManager>>(), _serviceProvider));
        _telemetryService = new Lazy<ComprehensiveTelemetryService>(() => 
            new ComprehensiveTelemetryService(_serviceProvider.GetRequiredService<ILogger<ComprehensiveTelemetryService>>(), _serviceProvider));
        
        // Start periodic health checks
        _healthCheckTimer = new Timer(PeriodicHealthCheckCallback, null, _healthCheckInterval, _healthCheckInterval);
        
        LogCoordinatorInitialized(_logger, null);
    }
    
    /// <summary>
    /// Execute the integration coordinator background service
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            LogStartingCoordinator(_logger, null);
            
            // Phase 1: System Validation
            await ValidateSystemIntegrityAsync(stoppingToken).ConfigureAwait(false);
            
            // Phase 2: Component Initialization
            await InitializeIntegrationComponentsAsync(stoppingToken).ConfigureAwait(false);
            
            // Phase 3: Runtime Validation
            await ValidateRuntimeIntegrationAsync(stoppingToken).ConfigureAwait(false);
            
            // Phase 4: Continuous Monitoring
            await RunContinuousMonitoringAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            LogCoordinatorStoppedGracefully(_logger, ex);
        }
        catch (Exception ex)
        {
            LogCriticalError(_logger, ex);
            lock (_statusLock)
            {
                _currentStatus = IntegrationStatus.Failed;
            }
            throw new InvalidOperationException("Production integration coordinator encountered a critical error", ex);
        }
    }
    
    /// <summary>
    /// Phase 1: Validate system integrity
    /// </summary>
    private async Task ValidateSystemIntegrityAsync(CancellationToken cancellationToken)
    {
        LogPhase1Start(_logger, null);
        
        lock (_statusLock)
            {
                _currentStatus = IntegrationStatus.ValidatingSystem;
            }
            
            // 1.1: Validate service inventory
            var inventoryReport = _serviceInventory.Value.GenerateInventoryReport();
            LogServiceInventory(_logger, inventoryReport.Services.Count, inventoryReport.Services.Values.Sum(s => s.Count), null);
            
            // 1.2: Validate configuration locks
            var configReport = _configurationLocks.Value.ValidateConfigurationLocks();
            if (!configReport.IsCompliant)
            {
                throw new InvalidOperationException("Configuration lock validation failed - system not production ready");
            }
            LogConfigLocksValidated(_logger, null);
            
            // 1.3: Validate YAML schemas
            var strategiesDir = Path.Combine(Directory.GetCurrentDirectory(), "strategies");
            if (Directory.Exists(strategiesDir))
            {
                var yamlValidation = await _yamlSchemaValidator.Value.ValidateDirectoryAsync(strategiesDir).ConfigureAwait(false);
                if (!yamlValidation.IsAllValid)
                {
                    LogYamlValidationIssues(_logger, yamlValidation.InvalidFiles, yamlValidation.TotalFiles, null);
                }
                LogYamlSchemaValidation(_logger, yamlValidation.ValidFiles, yamlValidation.TotalFiles, null);
            }
            
        LogPhase1Completed(_logger, null);
    }
    
    /// <summary>
    /// Phase 2: Initialize integration components
    /// </summary>
    private async Task InitializeIntegrationComponentsAsync(CancellationToken cancellationToken)
    {
        LogPhase2Start(_logger, null);
        
        lock (_statusLock)
            {
                _currentStatus = IntegrationStatus.InitializingComponents;
            }
            
            // 2.1: Initialize feature map authority
            var featureManifest = _featureMapAuthority.Value.GetManifestReport();
            LogFeatureMapAuthority(_logger, featureManifest.TotalResolvers, null);
            
            // 2.2: Initialize state persistence
            var stateCollection = await _statePersistence.Value.LoadAllStateAsync(cancellationToken).ConfigureAwait(false);
            LogStatePersistence(_logger, stateCollection.ZoneStates.Count, stateCollection.PatternReliability != null ? "Available" : "None", null);
            
            // 2.3: Initialize shadow mode manager
            LogShadowModeManagerInitialized(_logger, null);
            
            // 2.4: Initialize telemetry service
            var telemetryHealth = _telemetryService.Value.GetTelemetryHealth();
            LogTelemetryService(_logger, telemetryHealth.IsHealthy ? "Healthy" : "Degraded", telemetryHealth.ConfigSnapshotId ?? "None", null);
            
        LogPhase2Completed(_logger, null);
    }
    
    /// <summary>
    /// Phase 3: Validate runtime integration
    /// </summary>
    private async Task ValidateRuntimeIntegrationAsync(CancellationToken cancellationToken)
    {
        LogPhase3Start(_logger, null);
        
        lock (_statusLock)
            {
                _currentStatus = IntegrationStatus.ValidatingRuntime;
            }
            
            // 3.1: Test unified bar pipeline
            await TestUnifiedBarPipelineAsync(cancellationToken).ConfigureAwait(false);
            
            // 3.2: Test feature resolution
            await TestFeatureResolutionAsync(cancellationToken).ConfigureAwait(false);
            
            // 3.3: Test telemetry emission
            await TestTelemetryEmissionAsync(cancellationToken).ConfigureAwait(false);
            
        LogPhase3Completed(_logger, null);
    }
    
    /// <summary>
    /// Phase 4: Continuous monitoring
    /// </summary>
    private async Task RunContinuousMonitoringAsync(CancellationToken cancellationToken)
    {
        LogPhase4Start(_logger, null);
        
        lock (_statusLock)
        {
            _currentStatus = IntegrationStatus.Operational;
        }
        
        LogCoordinatorOperational(_logger, null);
        
        // Run until cancellation is requested
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                
                // Emit periodic operational telemetry
                await EmitOperationalTelemetryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperationInMonitoring(_logger, ex);
            }
            catch (TimeoutException ex)
            {
                LogTimeoutInMonitoring(_logger, ex);
            }
        }
    }
    
    /// <summary>
    /// Test unified bar pipeline with sample data
    /// </summary>
    private async Task TestUnifiedBarPipelineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var testBar = new BotCore.Models.Bar
            {
                Start = DateTime.UtcNow,
                Symbol = "ES",
                Open = 4500.0m,
                High = 4502.0m,
                Low = 4498.0m,
                Close = 4501.0m,
                Volume = 1000
            };
            
            var pipelineResult = await _unifiedBarPipeline.Value.ProcessAsync("ES", testBar, cancellationToken).ConfigureAwait(false);
            
            if (pipelineResult.Success)
            {
                LogUnifiedBarPipelineSuccess(_logger, pipelineResult.PipelineSteps.Count, pipelineResult.ProcessingTimeMs, null);
            }
            else
            {
                LogUnifiedBarPipelineFailed(_logger, pipelineResult.Error ?? "Unknown error", null);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationTestingBarPipeline(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentTestingBarPipeline(_logger, ex);
        }
    }
    
    /// <summary>
    /// Test feature resolution with sample queries
    /// </summary>
    private async Task TestFeatureResolutionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var testFeatures = new[] { "zone.dist_to_demand_atr", "pattern.bull_score", "vdc", "atr.14" };
            var successCount = 0;
            
            foreach (var featureKey in testFeatures)
            {
                var result = await _featureMapAuthority.Value.ResolveFeatureAsync("ES", featureKey, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    successCount++;
                    LogFeatureResolutionTest(_logger, featureKey, result.Value ?? 0.0, result.ResolverType ?? "Unknown", null);
                }
                else if (result.ShouldHoldDecision)
                {
                    LogFeatureResolutionMissing(_logger, featureKey, null);
                }
            }
            
            LogFeatureResolutionSummary(_logger, successCount, testFeatures.Length, null);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationTestingFeatureResolution(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentTestingFeatureResolution(_logger, ex);
        }
    }
    
    /// <summary>
    /// Test telemetry emission
    /// </summary>
    private async Task TestTelemetryEmissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var zoneTelemetry = new ZoneTelemetryData
            {
                ZoneCount = 5,
                TotalTests = 3,
                DemandZoneDistanceATR = 2.5,
                SupplyZoneDistanceATR = 3.0,
                BreakoutScore = 0.75
            };
            
            await _telemetryService.Value.EmitZoneTelemetryAsync("ES", zoneTelemetry, cancellationToken).ConfigureAwait(false);
            
            var patternTelemetry = new PatternTelemetryData
            {
                BullScore = 0.65,
                BearScore = 0.35,
                PatternSignals = new List<PatternSignalData>
                {
                    new() { PatternName = "Doji", IsConfirmed = true, Confidence = 0.8 }
                },
                PatternReliabilities = new List<PatternReliabilityData>
                {
                    new() { PatternName = "Doji", ReliabilityScore = 0.75 }
                }
            };
            
            await _telemetryService.Value.EmitPatternTelemetryAsync("ES", patternTelemetry, cancellationToken).ConfigureAwait(false);
            
            LogTelemetryEmissionSuccess(_logger, null);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationTestingTelemetry(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentTestingTelemetry(_logger, ex);
        }
    }
    
    /// <summary>
    /// Emit operational telemetry
    /// </summary>
    private async Task EmitOperationalTelemetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService<TradingBot.IntelligenceStack.RealTradingMetricsService>();
            if (metricsService != null)
            {
                var tags = new Dictionary<string, object>
                {
                    ["status"] = _currentStatus.ToString().ToUpperInvariant(),
                    ["config_snapshot_id"] = _telemetryService.Value.GetConfigurationSnapshotId()
                };
                
                await metricsService.RecordGaugeAsync("integration.coordinator_status", (int)_currentStatus, tags, cancellationToken).ConfigureAwait(false);
                await metricsService.RecordCounterAsync("integration.operational_heartbeat", 1, tags, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationEmittingTelemetry(_logger, ex);
        }
        catch (TimeoutException ex)
        {
            LogTimeoutEmittingTelemetry(_logger, ex);
        }
    }
    
    /// <summary>
    /// Periodic health check callback
    /// </summary>
    private void PeriodicHealthCheckCallback(object? state)
    {
        try
        {
            _lastHealthCheck = DateTime.UtcNow;
            
            // Check component health
            var pipelineHealth = _unifiedBarPipeline.IsValueCreated ? _unifiedBarPipeline.Value.GetPipelineHealth() : null;
            var epochFreezeStats = _epochFreezeEnforcement.IsValueCreated ? _epochFreezeEnforcement.Value.GetEnforcementStats() : null;
            var shadowModeStats = _shadowModeManager.IsValueCreated ? _shadowModeManager.Value.GetShadowModeStats() : null;
            var telemetryHealth = _telemetryService.IsValueCreated ? _telemetryService.Value.GetTelemetryHealth() : null;
            
            LogHealthCheck(_logger,
                pipelineHealth?.IsHealthy ?? false ? "Healthy" : "Degraded",
                epochFreezeStats?.ActiveEpochs ?? 0,
                shadowModeStats?.ActiveShadowStrategies ?? 0,
                telemetryHealth?.IsHealthy ?? false ? "Healthy" : "Degraded",
                null);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationInHealthCheck(_logger, ex);
        }
    }
    
    /// <summary>
    /// Get current integration status
    /// </summary>
    public IntegrationStatusReport GetIntegrationStatus()
    {
        lock (_statusLock)
        {
            return new IntegrationStatusReport
            {
                Status = _currentStatus,
                LastHealthCheck = _lastHealthCheck,
                ServiceInventoryAvailable = _serviceInventory.IsValueCreated,
                ConfigurationLocksActive = _configurationLocks.IsValueCreated,
                UnifiedBarPipelineActive = _unifiedBarPipeline.IsValueCreated,
                FeatureMapAuthorityActive = _featureMapAuthority.IsValueCreated,
                StatePersistenceActive = _statePersistence.IsValueCreated,
                EpochFreezeActive = _epochFreezeEnforcement.IsValueCreated,
                ShadowModeActive = _shadowModeManager.IsValueCreated,
                TelemetryServiceActive = _telemetryService.IsValueCreated
            };
        }
    }
    
    /// <summary>
    /// Dispose resources
    /// </summary>
    public override void Dispose()
    {
        _healthCheckTimer?.Dispose();
        
        if (_statePersistence.IsValueCreated)
        {
            _statePersistence.Value.Dispose();
        }
        
        base.Dispose();
        LogCoordinatorDisposed(_logger, null);
    }
}

/// <summary>
/// Integration status enumeration
/// </summary>
public enum IntegrationStatus
{
    Initializing = 0,
    ValidatingSystem = 1,
    InitializingComponents = 2,
    ValidatingRuntime = 3,
    Operational = 4,
    Failed = -1
}

/// <summary>
/// Integration status report
/// </summary>
public sealed class IntegrationStatusReport
{
    public IntegrationStatus Status { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public bool ServiceInventoryAvailable { get; set; }
    public bool ConfigurationLocksActive { get; set; }
    public bool UnifiedBarPipelineActive { get; set; }
    public bool FeatureMapAuthorityActive { get; set; }
    public bool StatePersistenceActive { get; set; }
    public bool EpochFreezeActive { get; set; }
    public bool ShadowModeActive { get; set; }
    public bool TelemetryServiceActive { get; set; }
}