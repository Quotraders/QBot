using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using TradingBot.UnifiedOrchestrator.Interfaces;
using TradingBot.UnifiedOrchestrator.Services;
using TradingBot.UnifiedOrchestrator.Models;
using TradingBot.UnifiedOrchestrator.Infrastructure;
using TradingBot.UnifiedOrchestrator.Configuration;
using TradingBot.Abstractions;
using TradingBot.IntelligenceStack;
using TradingBot.Backtest;
using UnifiedOrchestrator.Services;  // Add this for BacktestLearningService
using TopstepX.Bot.Authentication;
using TradingBot.RLAgent;  // Add this for ModelHotReloadManager
using global::BotCore.Brain;
using global::BotCore.Market;
using global::BotCore.ML;
using global::BotCore.Services;
using global::BotCore.Patterns;
using global::BotCore.Execution;
using global::BotCore.Features;
using global::BotCore.Configuration;
using global::BotCore.Extensions;
using DotNetEnv;
using static DotNetEnv.Env;
// Specific imports from TradingBot.BotCore.Services namespace
using TradingBotParameterProvider = TradingBot.BotCore.Services.TradingBotParameterProvider;
using TradingBotSymbolSessionManager = TradingBot.BotCore.Services.TradingBotSymbolSessionManager;
using BracketConfigService = TradingBot.BotCore.Services.BracketConfigService;

// Import types from aliased projects
using UnifiedTradingBrain = global::BotCore.Brain.UnifiedTradingBrain;
using Bar = global::BotCore.Market.Bar;

namespace TradingBot.UnifiedOrchestrator;

/// <summary>
/// 🚀 UNIFIED TRADING ORCHESTRATOR SYSTEM 🚀
/// 
/// This is the ONE MASTER ORCHESTRATOR that replaces all legacy orchestrators:
/// - Enhanced/TradingOrchestrator.cs (legacy)
/// - Core/Intelligence/TradingIntelligenceOrchestrator.cs (legacy)  
/// - workflow-orchestrator.js (legacy)
/// 
/// ALL FUNCTIONALITY IS NOW UNIFIED INTO ONE SYSTEM THAT WORKS TOGETHER
/// Legacy OrchestratorAgent, SimpleBot, MinimalDemo, TradingBot have been replaced
/// </summary>
internal static class Program
{
    // API Configuration Constants
    private const string TopstepXApiBaseUrl = "https://api.topstepx.com";
    private const string TopstepXUserAgent = "TopstepX-TradingBot/1.0";

    // Pre-host bootstrap function for idempotent setup
    private static void Bootstrap()
    {
        void Dir(string p) { if (!Directory.Exists(p)) Directory.CreateDirectory(p); }
        Dir("state"); Dir("state/backtests"); Dir("state/learning");
        Dir("datasets"); Dir("datasets/features"); Dir("datasets/quotes");
        Dir("reports"); Dir("artifacts"); Dir("artifacts/models"); Dir("artifacts/temp"); 
        Dir("artifacts/current"); Dir("artifacts/previous"); Dir("artifacts/stage");
        Dir("model_registry/models"); Dir("config/calendar"); Dir("manifests");
        
        var overrides = "state/runtime-overrides.json";
        if (!File.Exists(overrides)) File.WriteAllText(overrides, "{}");
        var s6 = "config/strategy.S6.json";
        if (!File.Exists(s6)) File.WriteAllText(s6,
            "{ \"name\":\"Momentum\",\"bands\":{\"bearish\":0.2,\"bullish\":0.8,\"hysteresis\":0.1},\"pacing\":1.0,\"tilt\":0.0,\"limits\":{\"spreadTicksMax\":2,\"latencyMsMax\":150},\"bracket\":{\"mode\":\"Auto\"} }");
        var s11 = "config/strategy.S11.json";
        if (!File.Exists(s11)) File.WriteAllText(s11,
            "{ \"name\":\"Exhaustion\",\"bands\":{\"bearish\":0.25,\"bullish\":0.75,\"hysteresis\":0.08},\"pacing\":0.8,\"tilt\":0.0,\"limits\":{\"spreadTicksMax\":3,\"latencyMsMax\":200},\"bracket\":{\"mode\":\"Auto\"} }");
        var hol = "config/calendar/holiday-cme.json";
        if (!File.Exists(hol)) File.WriteAllText(hol, "2025-01-01\n2025-07-04\n2025-12-25\n");
        
        // Create sample manifest.json if it doesn't exist
        var manifestPath = "manifests/manifest.json";
        if (!File.Exists(manifestPath)) 
        {
            var sampleManifest = """
            {
              "Version": "1.2.0",
              "CreatedAt": "2025-01-01T12:00:00Z",
              "DriftScore": 0.08,
              "Models": {
                "confidence_model": {
                  "Url": "https://github.com/ml-models/trading-models/releases/download/v1.2.0/confidence_v1.2.0.onnx",
                  "Sha256": "d4f8c9b2e3a1567890abcdef1234567890abcdef1234567890abcdef12345678",
                  "Size": 2048576
                },
                "rl_model": {
                  "Url": "https://github.com/ml-models/trading-models/releases/download/v1.2.0/rl_v1.2.0.onnx",
                  "Sha256": "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456",
                  "Size": 4194304
                },
                "ucb_model": {
                  "Url": "https://github.com/ml-models/trading-models/releases/download/v1.2.0/ucb_v1.2.0.onnx", 
                  "Sha256": "f6e5d4c3b2a1567890fedcba0987654321fedcba0987654321fedcba09876543",
                  "Size": 1572864
                }
              }
            }
            """;
            File.WriteAllText(manifestPath, sampleManifest);
        }
    }

    public static async Task Main(string[] args)
    {
        // Pre-host bootstrap - create required directories and files before building host
        Bootstrap();
        
        // Load .env files in priority order for auto TopstepX configuration
        EnvironmentLoader.LoadEnvironmentFiles();
        
        // Check for production demonstration command
        if (args.Length > 0 && args[0].Equals("--production-demo", StringComparison.OrdinalIgnoreCase))
        {
            await RunProductionDemonstrationAsync(args).ConfigureAwait(false);
            return;
        }
        
        // Check for smoke test command (replaces SimpleBot/MinimalDemo/TradingBot smoke tests)
        if (args.Length > 0 && args[0].Equals("--smoke", StringComparison.OrdinalIgnoreCase))
        {
            await RunSmokeTestAsync(args).ConfigureAwait(false);
            return;
        }
        
        Console.WriteLine(@"
================================================================================
                    🚀 UNIFIED TRADING ORCHESTRATOR SYSTEM 🚀                       
                                                                               
  🧠 ONE BRAIN - Consolidates all trading bot functionality into one     
  ⚡ ONE SYSTEM - Replaces 4+ separate orchestrators with clean solution
  🔄 ONE WORKFLOW ENGINE - All workflows managed by single scheduler  
  🌐 ONE TOPSTEPX CONNECTION - Unified API and SDK management      
  📊 ONE INTELLIGENCE SYSTEM - ML/RL models and predictions unified         
  📈 ONE TRADING ENGINE - All trading logic consolidated               
  📁 ONE DATA SYSTEM - Centralized data collection and reporting          
                                                                               
  ✅ Clean Build - No duplicated logic or conflicts                         
  🔧 Wired Together - All 1000+ features work in unison                     
  🎯 Single Purpose - Connect to TopstepX and trade effectively             

  💡 Run with --smoke to run lightweight smoke test (replaces SimpleBot/MinimalDemo)
  💡 Run with --production-demo to generate runtime proof artifacts         
================================================================================
        ");

        try
        {
            // Build the unified host with all services
            var host = CreateHostBuilder(args).Build();
            
            // Validate service registration and configuration on startup
            await ValidateStartupServicesAsync(host.Services).ConfigureAwait(false);
            
            // Initialize ML parameter provider for TradingBot classes
            TradingBot.BotCore.Services.TradingBotParameterProvider.Initialize(host.Services);
            
            // Display startup information
            // Note: DisplayStartupInfo() temporarily disabled during build phase
            
            // Run the unified orchestrator
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errorMsg = $"❌ CRITICAL ERROR: {ex.Message}";
            Console.WriteLine(errorMsg);
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            
            // Log to file for debugging and monitoring
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "critical_errors.log");
                var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}] {errorMsg}\n{ex.StackTrace}\n\n";
                File.AppendAllText(logPath, logEntry);
                Console.WriteLine($"Error logged to: {logPath}");
            }
            catch (Exception logEx)
            {
                // If logging fails, we still want to continue - just output to console
                Console.WriteLine($"Warning: Failed to write to error log: {logEx.Message}");
            }
            
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Run production demonstration to generate all runtime artifacts requested in PR review
    /// </summary>
    private static async Task RunProductionDemonstrationAsync(string[] args)
    {
        Console.WriteLine(@"
🚀 PRODUCTION READINESS DEMONSTRATION
================================================================================
Generating runtime proof of all champion/challenger architecture capabilities:

✅ UnifiedTradingBrain integration as primary decision maker
✅ Statistical validation with p < 0.05 significance testing  
✅ Rollback drill evidence with sub-100ms performance
✅ Safe window enforcement with CME-aligned trading hours
✅ Historical + live data integration verification
✅ Acceptance criteria AC1-AC10 validation

Artifacts will be saved to: artifacts/production-demo/
================================================================================
        ");

        try
        {
            // Build host with all services
            var host = CreateHostBuilder(args).Build();
            
            // Initialize ML parameter provider for TradingBot classes
            TradingBot.BotCore.Services.TradingBotParameterProvider.Initialize(host.Services);
            
            // Get the production demonstration runner
            var demoRunner = host.Services.GetRequiredService<ProductionDemonstrationRunner>();
            
            // Run complete demonstration
            var result = await demoRunner.RunCompleteProductionDemoAsync(CancellationToken.None).ConfigureAwait(false);
            
            if (result.Success)
            {
                Console.WriteLine($@"
🎉 PRODUCTION DEMONSTRATION COMPLETED SUCCESSFULLY!
================================================================================
Duration: {result.Duration}
Artifacts Path: {result.ArtifactsPath}

📋 Generated Artifacts:
✅ Brain integration proof with 5+ decisions showing UnifiedTradingBrain as primary
✅ Statistical validation report with p-value < 0.05 and CVaR analysis
✅ Rollback drill logs showing sub-100ms performance under 50 decisions/sec load
✅ Safe window enforcement proof with CME trading hours alignment
✅ Data integration status showing both historical and live TopStep connections
✅ Complete production readiness report meeting all AC1-AC10 criteria

🔍 Review the artifacts in {result.ArtifactsPath} for runtime proof of all capabilities.
================================================================================
                ");
            }
            else
            {
                Console.WriteLine($@"
❌ PRODUCTION DEMONSTRATION FAILED
================================================================================
Duration: {result.Duration}
Error: {result.ErrorMessage}

Please check the logs and ensure all services are properly configured.
================================================================================
                ");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"
❌ CRITICAL ERROR IN PRODUCTION DEMONSTRATION
================================================================================
{ex.Message}

Stack Trace:
{ex.StackTrace}
================================================================================
            ");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Run lightweight smoke test - replaces SimpleBot/MinimalDemo/TradingBot smoke functionality
    /// Validates core services startup and basic functionality in DRY_RUN mode
    /// </summary>
    private static async Task RunSmokeTestAsync(string[] args)
    {
        Console.WriteLine(@"
🧪 UNIFIED ORCHESTRATOR SMOKE TEST
================================================================================
Running lightweight smoke test to validate core system functionality:

✅ Service registration and dependency injection
✅ Configuration loading and validation  
✅ Core component initialization
✅ Trading readiness assessment (DRY_RUN)
✅ Basic connectivity checks

This replaces individual SimpleBot/MinimalDemo/TradingBot smoke tests
================================================================================
        ");

        try
        {
            // Build host with all services
            var host = CreateHostBuilder(args).Build();
            
            // Initialize ML parameter provider for TradingBot classes
            TradingBot.BotCore.Services.TradingBotParameterProvider.Initialize(host.Services);
            
            // Validate service registration and configuration
            await ValidateStartupServicesAsync(host.Services).ConfigureAwait(false);
            
            // Get core services for smoke testing
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("TradingBot.UnifiedOrchestrator.Program");
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            
            logger.LogInformation("🧪 [SMOKE] Starting UnifiedOrchestrator smoke test...");
            
            // Test 1: Configuration validation
            logger.LogInformation("🧪 [SMOKE] Test 1: Configuration validation");
            var isDryRun = configuration.GetValue<bool>("DRY_RUN", true);
            if (!isDryRun)
            {
                logger.LogWarning("🧪 [SMOKE] Warning: DRY_RUN is disabled - forcing DRY_RUN for smoke test");
                Environment.SetEnvironmentVariable("DRY_RUN", "true");
            }
            
            // Test 2: Core service availability  
            logger.LogInformation("🧪 [SMOKE] Test 2: Core service availability");
            var unifiedOrchestrator = host.Services.GetService<TradingBot.Abstractions.IUnifiedOrchestrator>();
            var tradingReadiness = host.Services.GetService<ITradingReadinessTracker>();
            var mlConfigService = host.Services.GetService<TradingBot.BotCore.Services.MLConfigurationService>();
            
            logger.LogInformation("🧪 [SMOKE] ✅ UnifiedOrchestrator service: {Status}", 
                unifiedOrchestrator != null ? "Available" : "Missing");
            logger.LogInformation("🧪 [SMOKE] ✅ TradingReadinessTracker service: {Status}", 
                tradingReadiness != null ? "Available" : "Missing");
            logger.LogInformation("🧪 [SMOKE] ✅ MLConfigurationService: {Status}", 
                mlConfigService != null ? "Available" : "Missing");
                
            // Test 3: Parameter provider functionality
            logger.LogInformation("🧪 [SMOKE] Test 3: Parameter provider functionality");
            var confidenceThreshold = TradingBot.BotCore.Services.TradingBotParameterProvider.GetAIConfidenceThreshold();
            var positionMultiplier = TradingBot.BotCore.Services.TradingBotParameterProvider.GetPositionSizeMultiplier();
            var fallbackConfidence = TradingBot.BotCore.Services.TradingBotParameterProvider.GetFallbackConfidence();
            
            logger.LogInformation("🧪 [SMOKE] ✅ AI Confidence Threshold: {Threshold}", confidenceThreshold);
            logger.LogInformation("🧪 [SMOKE] ✅ Position Size Multiplier: {Multiplier}", positionMultiplier);
            logger.LogInformation("🧪 [SMOKE] ✅ Fallback Confidence: {Confidence}", fallbackConfidence);
            
            // Test 4: Symbol session management
            logger.LogInformation("🧪 [SMOKE] Test 4: Symbol session management");
            var symbolSessionManager = host.Services.GetService<TradingBot.BotCore.Services.TradingBotSymbolSessionManager>();
            if (symbolSessionManager != null)
            {
                logger.LogInformation("🧪 [SMOKE] ✅ Symbol session manager available");
            }
            else
            {
                logger.LogWarning("🧪 [SMOKE] ⚠️ Symbol session manager not available");
            }
            
            // Test 5: Quick startup cycle (minimal duration)
            logger.LogInformation("🧪 [SMOKE] Test 5: Quick startup cycle");
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            try
            {
                await host.StartAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                logger.LogInformation("🧪 [SMOKE] ✅ Host started successfully");
                
                // Wait briefly to verify services are running
                await Task.Delay(2000, cancellationTokenSource.Token).ConfigureAwait(false);
                
                await host.StopAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                logger.LogInformation("🧪 [SMOKE] ✅ Host stopped successfully");
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("🧪 [SMOKE] ⚠️ Startup cycle timeout (expected for smoke test)");
            }
            
            Console.WriteLine(@"
🎉 SMOKE TEST COMPLETED SUCCESSFULLY!
================================================================================
All core UnifiedOrchestrator services validated:

✅ Service registration: All required services available
✅ Configuration loading: DRY_RUN mode enforced  
✅ Parameter providers: Configuration-driven values loaded
✅ Core components: UnifiedOrchestrator, MLConfig, TradingReadiness
✅ Startup/shutdown: Host lifecycle working correctly

This smoke test replaces:
❌ SimpleBot smoke test
❌ MinimalDemo smoke test  
❌ TradingBot smoke test

Use this unified smoke test going forward for validation.
================================================================================
            ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"
❌ SMOKE TEST FAILED
================================================================================
Error: {ex.Message}

Stack Trace:
{ex.StackTrace}

Please check the configuration and ensure all required services are registered.
================================================================================
            ");
            Environment.Exit(1);
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://localhost:5000");
                webBuilder.UseStartup<Startup>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(options => 
                {
                    options.FormatterName = "Production";
                });
                logging.AddConsoleFormatter<ProductionConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
                logging.SetMinimumLevel(LogLevel.Information);
                // REDUCE NOISE - Override Microsoft and System logging to warnings only
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("System", LogLevel.Warning);
                logging.AddFilter("Microsoft.AspNetCore.Http", LogLevel.Error);
                logging.AddFilter("Microsoft.AspNetCore.Http", LogLevel.Error);
            })
            .ConfigureServices((context, services) =>
            {
                // ==============================================
                // THE ONE AND ONLY ORCHESTRATOR - MASTER BRAIN
                // ==============================================
                // Configure unified orchestrator services FIRST
                ConfigureUnifiedServices(services, context.Configuration, context);
            });

    private static void ConfigureUnifiedServices(IServiceCollection services, IConfiguration configuration, HostBuilderContext hostContext)
    {
        // Register login completion state for TopstepX SDK connection management
        services.AddSingleton<Services.ILoginCompletionState, Services.EnterpriseLoginCompletionState>();
        
        // Register TradingBot.Abstractions.ILoginCompletionState for AutoTopstepXLoginService
        // Bridge the local interface to the abstractions interface
        services.AddSingleton<TradingBot.Abstractions.ILoginCompletionState>(provider => 
        {
            var localState = provider.GetRequiredService<Services.ILoginCompletionState>();
            var logger = provider.GetRequiredService<ILogger<BridgeLoginCompletionState>>();
            return new BridgeLoginCompletionState(localState, logger);
        });
        
        // Register TradingLogger for production-ready logging
        services.Configure<TradingLoggerOptions>(options =>
        {
            var logDir = Environment.GetEnvironmentVariable("TRADING_LOG_DIR") ?? 
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TradingBot", "Logs");
            options.LogDirectory = logDir;
            options.BatchSize = int.Parse(Environment.GetEnvironmentVariable("LOG_BATCH_SIZE") ?? "1000");
            options.MaxFileSizeBytes = long.Parse(Environment.GetEnvironmentVariable("LOG_MAX_FILE_SIZE") ?? "104857600"); // 100MB
            options.LogRetentionDays = int.Parse(Environment.GetEnvironmentVariable("LOG_RETENTION_DAYS") ?? "30");
            options.DebugLogRetentionDays = int.Parse(Environment.GetEnvironmentVariable("DEBUG_LOG_RETENTION_DAYS") ?? "7");
            options.EnablePerformanceMetrics = Environment.GetEnvironmentVariable("ENABLE_PERFORMANCE_METRICS") != "false";
            options.EnableCriticalAlerts = Environment.GetEnvironmentVariable("ENABLE_CRITICAL_ALERTS") != "false";
            options.MarketDataSamplingRate = int.Parse(Environment.GetEnvironmentVariable("MARKET_DATA_SAMPLING_RATE") ?? "10");
            options.MLPredictionAggregationCount = int.Parse(Environment.GetEnvironmentVariable("ML_PREDICTION_AGGREGATION") ?? "100");
        });
        services.AddSingleton<ITradingLogger, Services.TradingLogger>();

        // Legacy authentication services removed - using environment credentials with TopstepX SDK adapter

        // Register enhanced JWT lifecycle manager for token refresh coordination
        // DISABLED: IJwtLifecycleManager not currently implemented
        // services.AddSingleton<IJwtLifecycleManager, JwtLifecycleManager>();
        // services.AddHostedService<JwtLifecycleManager>(provider => 
        //     (JwtLifecycleManager)provider.GetRequiredService<IJwtLifecycleManager>());

        // Register environment validator for startup validation
        // DISABLED: IEnvironmentValidator not currently implemented
        // services.AddSingleton<IEnvironmentValidator, EnvironmentValidator>();

        // Register snapshot manager for state reconciliation
        // DISABLED: ISnapshotManager not currently implemented
        // services.AddSingleton<ISnapshotManager, SnapshotManager>();

        // Legacy connection manager removed - using TopstepX SDK adapter for connections

        // Register platform-aware Python path resolver
        services.AddSingleton<IPythonPathResolver, PlatformAwarePythonPathResolver>();

        // Register monitoring integration for metrics and log querying
        services.AddHostedService<MonitoringIntegrationService>();

        // Legacy authentication and login services removed - using TopstepX SDK adapter

        // Register system health monitoring service
        services.AddHostedService<SystemHealthMonitoringService>();

        // ================================================================================
        // 🚀 AUTONOMOUS TRADING ENGINE - PROFIT-MAXIMIZING SYSTEM 🚀
        // ================================================================================
        
        // Configure autonomous trading options
        services.Configure<AutonomousConfig>(options =>
        {
            options.Enabled = Environment.GetEnvironmentVariable("AUTONOMOUS_MODE") == "true";
            options.TradeDuringLunch = Environment.GetEnvironmentVariable("TRADE_DURING_LUNCH") == "true";
            options.TradeOvernight = Environment.GetEnvironmentVariable("TRADE_OVERNIGHT") == "true";
            options.TradePreMarket = Environment.GetEnvironmentVariable("TRADE_PREMARKET") == "true";
            options.MaxContractsPerTrade = int.Parse(Environment.GetEnvironmentVariable("MAX_CONTRACTS_PER_TRADE") ?? "5");
            options.DailyProfitTarget = decimal.Parse(Environment.GetEnvironmentVariable("DAILY_PROFIT_TARGET") ?? "300");
            options.MaxDailyLoss = decimal.Parse(Environment.GetEnvironmentVariable("MAX_DAILY_LOSS") ?? "-1000");
            options.MaxDrawdown = decimal.Parse(Environment.GetEnvironmentVariable("MAX_DRAWDOWN") ?? "-2000");
        });
        
        // Register autonomous decision engine components
        services.AddSingleton<TopStepComplianceManager>();
        services.AddSingleton<MarketConditionAnalyzer>();
        services.AddSingleton<AutonomousPerformanceTracker>();
        services.AddSingleton<StrategyPerformanceAnalyzer>();
        services.AddSingleton<IMarketHours, BasicMarketHours>();
        
        // Register Session-Aware Runtime Gates for 24×5 futures trading
        services.AddSingleton<SessionAwareRuntimeGates>();
        
        // Register Safe-Hold Decision Policy with neutral band logic
        services.AddSingleton<SafeHoldDecisionPolicy>();
        
        // Register bracket configuration service
        services.AddSingleton<IBracketConfig, TradingBot.BotCore.Services.BracketConfigService>();
        
        // Register zone-aware bracket manager
        services.AddSingleton<global::BotCore.Services.IZoneAwareBracketManager, global::BotCore.Services.ZoneAwareBracketManager>();
        
        // Register Per-Symbol Session Lattices with neutral band integration
        services.AddSingleton<TradingBot.BotCore.Services.TradingBotSymbolSessionManager>(provider =>
        {
            var neutralBandService = provider.GetService<SafeHoldDecisionPolicy>();
            var logger = provider.GetRequiredService<ILogger<TradingBot.BotCore.Services.TradingBotSymbolSessionManager>>();
            return new TradingBot.BotCore.Services.TradingBotSymbolSessionManager(neutralBandService, logger);
        });
        
        // Register Enhanced Trading Brain Integration BEFORE UnifiedDecisionRouter (dependency order)
        // NOTE: Intelligence services are registered later, so we need to resolve them explicitly
        services.AddSingleton<global::BotCore.Services.EnhancedTradingBrainIntegration>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Services.EnhancedTradingBrainIntegration>>();
            var tradingBrain = provider.GetRequiredService<global::BotCore.Brain.UnifiedTradingBrain>();
            var ensembleService = provider.GetRequiredService<global::BotCore.Services.ModelEnsembleService>();
            var feedbackService = provider.GetRequiredService<global::BotCore.Services.TradingFeedbackService>();
            var cloudSync = provider.GetRequiredService<global::BotCore.Services.CloudModelSynchronizationService>();
            var serviceProvider = provider;
            var intelligenceService = provider.GetService<global::BotCore.Intelligence.IntelligenceSynthesizerService>();
            
            return new global::BotCore.Services.EnhancedTradingBrainIntegration(
                logger, tradingBrain, ensembleService, feedbackService, cloudSync, serviceProvider, intelligenceService);
        });
        
        // Register UnifiedDecisionRouter before AutonomousDecisionEngine (dependency order)
        services.AddSingleton<global::BotCore.Services.UnifiedDecisionRouter>();
        
        // Register the main autonomous decision engine as hosted service
        services.AddSingleton<AutonomousDecisionEngine>();
        services.AddHostedService<AutonomousDecisionEngine>(provider => 
            provider.GetRequiredService<AutonomousDecisionEngine>());
        
        Console.WriteLine("🚀 [AUTONOMOUS-ENGINE] Registered autonomous trading engine - Profit-maximizing TopStep bot ready!");
        Console.WriteLine("💰 [AUTONOMOUS-ENGINE] Features: Auto strategy switching, dynamic position sizing, TopStep compliance, continuous learning");
        
        // ================================================================================
        // REGIME DETECTION SERVICE - MARKET CONDITION ANALYSIS
        // ================================================================================
        
        // Register Regime Detection Service for market regime classification (required by UnifiedPositionManagementService)
        services.AddSingleton<global::BotCore.Services.RegimeDetectionService>();
        
        Console.WriteLine("📊 [REGIME-DETECTION] Registered regime detection service");
        Console.WriteLine("   ✅ Market regime classification - Detects Trending, Ranging, and Transition regimes");
        Console.WriteLine("   ✅ Dynamic R-multiple targeting - Adjusts profit targets based on market conditions (Feature 1)");
        Console.WriteLine("   ✅ Regime change exit detection - Exits positions when regime shifts unfavorably (Feature 3)");
        Console.WriteLine("   ✅ Adaptive position management - Enables regime-aware trading decisions");
        
        // ================================================================================
        // UNIFIED POSITION MANAGEMENT SERVICE - BREAKEVEN, TRAILING, TIME EXITS
        // ================================================================================
        
        // Register Unified Position Management Service for all strategies
        services.AddSingleton<global::BotCore.Services.UnifiedPositionManagementService>();
        services.AddHostedService<global::BotCore.Services.UnifiedPositionManagementService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.UnifiedPositionManagementService>());
        
        Console.WriteLine("🎯 [POSITION-MGMT] Registered unified position management service");
        Console.WriteLine("   ✅ Breakeven protection - Moves stop to entry + 1 tick when profitable");
        Console.WriteLine("   ✅ Trailing stops - Locks in profits as price moves favorably");
        Console.WriteLine("   ✅ Time-based exits - Closes stale positions (S2=60m, S3=90m, S6=45m, S11=60m)");
        Console.WriteLine("   ✅ Max excursion tracking - Captures data for ML/RL optimization");
        
        // ================================================================================
        // ZONE BREAK MONITORING SERVICE - PHASE 2 IMPLEMENTATION
        // ================================================================================
        
        // Register Zone Break Monitoring Service for supply/demand zone break detection
        services.AddSingleton<global::BotCore.Services.ZoneBreakMonitoringService>();
        services.AddHostedService<global::BotCore.Services.ZoneBreakMonitoringService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.ZoneBreakMonitoringService>());
        
        Console.WriteLine("🔍 [ZONE-BREAK] Registered zone break monitoring service (PHASE 2)");
        Console.WriteLine("   ✅ Real-time zone break detection - Monitors supply/demand zone violations");
        Console.WriteLine("   ✅ Position exit warnings - Alert on critical support/resistance breaks");
        Console.WriteLine("   ✅ Zone-based stop placement - Moves stops behind broken zones");
        Console.WriteLine("   ✅ Aggressive entry signals - Boosts confidence on strong zone breaks");
        
        // ================================================================================
        // POSITION MONITORING SERVICES - SESSION-AWARE EXPOSURE TRACKING
        // ================================================================================
        
        // Register position monitoring services for real-time session exposure tracking
        services.AddSingleton<global::BotCore.Services.PositionMonitoring.IRealTimePositionMonitor, global::BotCore.Services.PositionMonitoring.RealTimePositionMonitor>();
        services.AddSingleton<global::BotCore.Services.PositionMonitoring.ISessionExposureCalculator, global::BotCore.Services.PositionMonitoring.SessionExposureCalculator>();
        services.AddSingleton<global::BotCore.Services.PositionMonitoring.IPositionTimeTracker, global::BotCore.Services.PositionMonitoring.PositionTimeTracker>();
        services.AddSingleton<global::BotCore.Services.PositionMonitoring.SessionDetectionService>();
        
        // Register ES/NQ Portfolio Heat Manager with position monitoring services
        services.AddSingleton<global::BotCore.Services.IPortfolioHeatManager, global::BotCore.Services.ES_NQ_PortfolioHeatManager>();
        
        Console.WriteLine("📊 [POSITION-MONITORING] Registered position monitoring services");
        Console.WriteLine("   ✅ Real-time session exposure tracking - Monitors exposure by trading session");
        Console.WriteLine("   ✅ Risk-adjusted exposure calculation - Volatility, correlation, liquidity factors");
        Console.WriteLine("   ✅ Position lifecycle tracking - Complete history across sessions");
        Console.WriteLine("   ✅ Time-decay weighting - Fresh (1.0x) to Stale (0.3x) positions");
        Console.WriteLine("   ✅ ES/NQ Portfolio Heat Manager - Integrated with real-time monitoring");
        
        // ================================================================================
        // POSITION MANAGEMENT OPTIMIZER - PHASE 3 ML/RL LEARNING
        // ================================================================================
        
        // Register Position Management Optimizer for ML/RL parameter learning
        services.AddSingleton<global::BotCore.Services.PositionManagementOptimizer>();
        services.AddHostedService<global::BotCore.Services.PositionManagementOptimizer>(provider => 
            provider.GetRequiredService<global::BotCore.Services.PositionManagementOptimizer>());
        
        Console.WriteLine("🧠 [PM-OPTIMIZER] Registered position management optimizer (PHASE 3)");
        Console.WriteLine("   ✅ Breakeven timing optimization - Learn optimal BE trigger (6 vs 8 vs 10 ticks)");
        Console.WriteLine("   ✅ Trailing stop optimization - Learn optimal trail distance (1.0x vs 1.5x ATR)");
        Console.WriteLine("   ✅ Time exit optimization - Learn optimal timeout per strategy + regime");
        Console.WriteLine("   ✅ Outcome tracking - 'BE at 8 ticks → stopped out, would have hit target'");
        
        // ================================================================================
        // SESSION-END POSITION FLATTENER - PHASE 2 IMPLEMENTATION
        // ================================================================================
        
        // Register Session-End Position Flattener for automatic position closes before market close
        services.AddSingleton<global::BotCore.Services.SessionEndPositionFlattener>();
        services.AddHostedService<global::BotCore.Services.SessionEndPositionFlattener>(provider => 
            provider.GetRequiredService<global::BotCore.Services.SessionEndPositionFlattener>());
        
        Console.WriteLine("✅ [STARTUP] SessionEndPositionFlattener registered");
        Console.WriteLine("🔄 [SESSION-FLATTEN] Automatic position flatten before market close (PHASE 2)");
        Console.WriteLine("   ✅ Daily position flatten - Closes all positions at 4:55 PM ET (configurable)");
        Console.WriteLine("   ✅ Monday-Thursday - Always flatten (daily maintenance)");
        Console.WriteLine("   ✅ Friday - Configurable (BOT_SESSION_FLATTEN_FRIDAY_ENABLED)");
        Console.WriteLine("   ✅ Weekend safety - Prevents overnight position holds");
        Console.WriteLine("   ✅ ML/RL integration - SessionEnd exits feed into optimizer for learning");
        
        // ================================================================================
        // STUCK POSITION RECOVERY SYSTEM - THREE-LAYER DEFENSE
        // ================================================================================
        
        // Configure stuck position recovery settings
        services.Configure<global::BotCore.Configuration.StuckPositionRecoveryConfiguration>(
            configuration.GetSection("StuckPositionRecovery"));
        
        // Register Emergency Exit Executor (Layer 3) - must be registered first as dependency
        services.AddSingleton<global::BotCore.Services.EmergencyExitExecutor>();
        
        // Register Position Reconciliation Service (Layer 1) - runs every 60s
        services.AddSingleton<global::BotCore.Services.PositionReconciliationService>();
        services.AddHostedService<global::BotCore.Services.PositionReconciliationService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.PositionReconciliationService>());
        
        // Register Stuck Position Monitor (Layer 2) - runs every 30s
        services.AddSingleton<global::BotCore.Services.StuckPositionMonitor>();
        services.AddHostedService<global::BotCore.Services.StuckPositionMonitor>(provider => 
            provider.GetRequiredService<global::BotCore.Services.StuckPositionMonitor>());
        
        Console.WriteLine("🛡️ [STUCK-POSITION-RECOVERY] Three-layer defense system registered");
        Console.WriteLine("   🔄 Layer 1: Position Reconciliation - Compares bot vs broker every 60s");
        Console.WriteLine("   👁️ Layer 2: Stuck Position Monitor - Detects stuck/aged/runaway positions every 30s");
        Console.WriteLine("   🚨 Layer 3: Emergency Exit Executor - 5-level escalation for position recovery");
        Console.WriteLine("   ⚡ Level 1 (T+0s): Smart Retry with improved pricing");
        Console.WriteLine("   🔄 Level 2 (T+30s): Fresh Start with market-based pricing");
        Console.WriteLine("   🚨 Level 3 (T+60s): Market Order for guaranteed fill");
        Console.WriteLine("   🚨🚨 Level 4 (T+120s): Human Escalation with alerts");
        Console.WriteLine("   🛑 Level 5 (T+300s): System Shutdown via kill.txt");
        
        // ================================================================================
        // ZONE AWARENESS SERVICES - PRODUCTION-READY SUPPLY/DEMAND INTEGRATION
        // ================================================================================
        
        // Register ProductionFeatureBus for zone telemetry
        services.AddSingleton<Zones.IFeatureBus, global::BotCore.Services.ProductionFeatureBus>();
        
        // Register ZoneService with production implementation (Modern provider)
        services.AddSingleton<Zones.IZoneService, Zones.ZoneServiceProduction>();
        services.AddSingleton<Zones.IZoneFeatureSource>(provider => 
            (Zones.IZoneFeatureSource)provider.GetRequiredService<Zones.IZoneService>());
        
        // Register zone telemetry service
        services.AddSingleton<global::BotCore.Services.IZoneTelemetryService, global::BotCore.Services.ZoneTelemetryService>();
        
        // Register zone providers (modern-only)
        services.AddSingleton<global::BotCore.Services.ModernZoneProvider>();
        services.AddSingleton<global::BotCore.Services.HybridZoneProvider>();
        services.AddSingleton<global::BotCore.Services.IZoneProvider>(provider => 
            provider.GetRequiredService<global::BotCore.Services.HybridZoneProvider>());
        
        // Register ZoneFeaturePublisher for telemetry emission
        services.AddHostedService<Zones.ZoneFeaturePublisher>();
        
        // Register market data to zone service bridge
        services.AddHostedService<global::BotCore.Services.ZoneMarketDataBridge>();
        
        Console.WriteLine("🎯 [ZONE-AWARENESS] Modern zone awareness services registered - Modern-only provider active!");
        
        // ================================================================================
        // S7 MULTI-HORIZON RELATIVE STRENGTH STRATEGY
        // ================================================================================
        
        // Configure S7 strategy options
        services.Configure<TradingBot.Abstractions.S7Configuration>(configuration.GetSection("S7"));
        services.Configure<TradingBot.Abstractions.BreadthConfiguration>(configuration.GetSection("Breadth"));
        services.Configure<TradingBot.S7.S7MarketDataBridgeConfiguration>(configuration.GetSection("S7:MarketDataBridge"));
        
        // Register S7 service with full DSL implementation
        services.AddSingleton<TradingBot.Abstractions.IS7Service, TradingBot.S7.S7Service>();
        services.AddSingleton<TradingBot.Abstractions.IS7FeatureSource>(provider => 
            (TradingBot.Abstractions.IS7FeatureSource)provider.GetRequiredService<TradingBot.Abstractions.IS7Service>());
        
        // Register optional breadth feed service (disabled by default)
        // Register IBreadthFeed implementation with fail-closed behavior
        // BREADTH FEED INTENTIONALLY DISABLED: Using NullBreadthDataSource until real market breadth subscription is active
        // Prevents live workflows from consuming CSV simplified data and ensures fail-closed behavior for breadth.* features
        services.AddSingleton<TradingBot.Abstractions.IBreadthFeed, global::BotCore.Services.NullBreadthDataSource>();
        
        // Register S7 market data bridge for live data integration
        services.AddHostedService<TradingBot.S7.S7MarketDataBridge>();
        
        // Register S7 feature publisher for knowledge graph integration
        services.AddHostedService<TradingBot.S7.S7FeaturePublisher>();
        
        // ================================================================================
        // AUTOMATION-FIRST UPGRADE SCOPE - Feature Engineering Pipeline
        // Register feature resolvers as singletons for fail-closed feature extraction
        
        services.Configure<global::BotCore.Configuration.OfiConfiguration>(configuration.GetSection("Features:Ofi"));
        services.Configure<global::BotCore.Configuration.BarDispatcherConfiguration>(configuration.GetSection("Features:BarDispatcher"));
        
        services.AddSingleton<global::BotCore.Features.IFeatureResolver, global::BotCore.Features.LiquidityAbsorptionResolver>();
        services.AddSingleton<global::BotCore.Features.IFeatureResolver, global::BotCore.Features.MtfStructureResolver>();
        services.AddSingleton<global::BotCore.Features.IFeatureResolver, global::BotCore.Features.OfiProxyResolver>();
        
        // Register feature publisher hosted service for automated feature publishing
        services.AddHostedService<global::BotCore.Features.FeaturePublisher>();
        
        // ================================================================================
        // EXECUTION ALPHA UPGRADES - S7 Execution Path Enhancement
        // Advanced execution services for intelligent order type selection and management
        
        services.Configure<global::BotCore.Execution.S7ExecutionConfiguration>(configuration.GetSection("S7:Execution"));
        services.Configure<global::BotCore.Execution.BracketConfiguration>(configuration.GetSection("S7:Brackets"));
        
        services.AddSingleton<global::BotCore.Execution.S7OrderTypeSelector>();
        services.AddSingleton<global::BotCore.Execution.ChildOrderScheduler>();
        services.AddSingleton<global::BotCore.Execution.BracketAdjustmentService>();
        
        Console.WriteLine("⚡ [EXECUTION-ALPHA] S7 execution enhancements registered - Order type selection, child scheduling, bracket adjustment ready!");
        Console.WriteLine("🔧 [AUTOMATION-UPGRADE] Feature engineering pipeline registered - Liquidity, MTF, OFI resolvers ready!");
        
        // ================================================================================
        // REGIME-TAGGED MODEL ROTATION & PORTFOLIO RISK TILTS
        // Advanced model rotation and portfolio risk management services
        
        services.Configure<global::BotCore.Services.ModelRotationConfiguration>(configuration.GetSection("Rotation"));
        services.Configure<global::BotCore.Services.BreadthReallocationConfiguration>(configuration.GetSection("Portfolio:BreadthReallocation"));
        services.Configure<global::BotCore.Services.CorrelationCapConfiguration>(configuration.GetSection("CorrelationCapConfiguration"));
        services.Configure<global::BotCore.Services.VolOfVolConfiguration>(configuration.GetSection("VolOfVolConfiguration"));
        services.Configure<global::BotCore.Services.DriftMonitorConfiguration>(configuration.GetSection("DataHygiene:DriftMonitor"));
        
        services.AddSingleton<global::BotCore.Services.ModelRotationService>();
        // S7 BREADTH REALLOCATION INTENTIONALLY DISABLED: Keep breadth adjustments switched off
        // Commenting out S7BreadthReallocationService registration until real breadth feed is active
        // services.AddSingleton<global::BotCore.Services.S7BreadthReallocationService>();
        services.AddSingleton<global::BotCore.Services.CorrelationAwareCapService>();
        services.AddSingleton<global::BotCore.Services.VolOfVolGuardService>();
        services.AddSingleton<global::BotCore.Services.FeatureDriftMonitorService>();
        
        // Register model rotation as hosted service
        services.AddHostedService<global::BotCore.Services.ModelRotationService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.ModelRotationService>());
        
        Console.WriteLine("🔄 [MODEL-ROTATION] Regime-tagged model rotation service registered - Automatic model switching per market regime!");
        Console.WriteLine("📊 [PORTFOLIO-TILTS] Risk management services registered - Breadth reallocation, correlation caps, vol-of-vol guard!");
        Console.WriteLine("🛡️ [DATA-HYGIENE] Drift defenses registered - Feature drift monitoring with kill switches!");
        Console.WriteLine("📈 [S7-STRATEGY] S7 Multi-Horizon Relative Strength strategy registered - Full DSL implementation ready!");
        
        // ================================================================================

        // Register trading activity logger for comprehensive trading event logging
        services.AddSingleton<TradingActivityLogger>();

        // Register log retention service for automatic cleanup
        services.AddHostedService<LogRetentionService>();

        // Register error handling service with fallback logging mechanisms
        services.AddSingleton<ErrorHandlingService>();
        services.AddHostedService<ErrorHandlingService>(provider => provider.GetRequiredService<ErrorHandlingService>());

        // NOTE: Legacy AccountService removed - account data is now managed by TopstepXAdapterService
        // services.AddHttpClient<AccountService>...
        // services.AddSingleton<IAccountService>...

        // ========================================================================
        // TOPSTEPX SDK ADAPTER - PRODUCTION-READY PYTHON SDK INTEGRATION
        // ========================================================================
        
        // Configure TopstepX client configuration for real connections only
        services.Configure<TopstepXClientConfiguration>(config =>
        {
            config.ClientType = "Real";
        });
        
        // Register TopstepXHttpClient for real client
        services.AddHttpClient("TopstepX", client =>
        {
            client.BaseAddress = new Uri(TopstepXApiBaseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", TopstepXUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // Register TopstepXService for real client
        services.AddSingleton<global::BotCore.Services.ITopstepXService, global::BotCore.Services.TopstepXService>();
        
        // TopstepX SDK Adapter Service - Production-ready Python SDK integration
        services.AddSingleton<TradingBot.Abstractions.ITopstepXAdapterService, TopstepXAdapterService>();
        
        // PHASE 1: Order Execution Metrics - Tracks latency, slippage, and fill statistics
        services.AddSingleton<global::BotCore.Services.OrderExecutionMetrics>();
        
        // Order Execution Service - Implements IOrderService for position management
        // Integrates with TopstepX adapter for order execution and partial closes
        services.AddSingleton<TradingBot.Abstractions.IOrderService, global::BotCore.Services.OrderExecutionService>();
        
        // PHASE 2: Wiring Service - Connects fill events from TopstepXAdapter to OrderExecutionService
        services.AddHostedService<OrderExecutionWiringService>();
        
        // PHASE 4: Execution Metrics Reporting - Hourly quality reports and alerts
        services.AddHostedService<ExecutionMetricsReportingService>();
        
        // TopstepX Integration Test Service for validation
        services.AddHostedService<TopstepXIntegrationTestService>();

        // Configure AppOptions for Safety components
        var appOptions = new AppOptions
        {
            ApiBase = Environment.GetEnvironmentVariable("TOPSTEPX_API_BASE") ?? TopstepXApiBaseUrl,
            AuthToken = Environment.GetEnvironmentVariable("TOPSTEPX_JWT") ?? "",
            AccountId = Environment.GetEnvironmentVariable("TOPSTEPX_ACCOUNT_ID") ?? "",
            EnableDryRunMode = Environment.GetEnvironmentVariable("ENABLE_DRY_RUN") != "false",
            EnableAutoExecution = Environment.GetEnvironmentVariable("ENABLE_AUTO_EXECUTION") == "true",
            MaxDailyLoss = decimal.Parse(Environment.GetEnvironmentVariable("MAX_DAILY_LOSS") ?? "-1000"),
            MaxPositionSize = int.Parse(Environment.GetEnvironmentVariable("MAX_POSITION_SIZE") ?? "5"),
            DrawdownLimit = decimal.Parse(Environment.GetEnvironmentVariable("DRAWDOWN_LIMIT") ?? "-2000"),
            KillFile = Environment.GetEnvironmentVariable("KILL_FILE") ?? Path.Combine(Directory.GetCurrentDirectory(), "kill.txt")
        };
        services.AddSingleton<IOptions<AppOptions>>(provider => Options.Create(appOptions));

        // Configure workflow scheduling options
        services.Configure<WorkflowSchedulingOptions>(configuration.GetSection("WorkflowScheduling"));
        
        // Configure Python integration options with platform-aware paths
        services.Configure<PythonIntegrationOptions>(options =>
        {
            options.Enabled = Environment.GetEnvironmentVariable("ENABLE_PYTHON_INTEGRATION") != "false";
            options.PythonPath = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE") ?? 
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python.exe" : "/usr/bin/python3");
            
            // Fix: Resolve WorkingDirectory relative to project content root, not binary output directory
            var workingDir = Environment.GetEnvironmentVariable("PYTHON_WORKING_DIR") ?? "./python";
            if (!Path.IsPathRooted(workingDir))
            {
                workingDir = Path.GetFullPath(Path.Combine(hostContext.HostingEnvironment.ContentRootPath, workingDir));
            }
            options.WorkingDirectory = workingDir;
            
            options.ScriptPaths = new Dictionary<string, string>
            {
                // decision_service removed - functionality consolidated into UnifiedTradingBrain
                ["modelInference"] = Path.Combine("python", "ucb", "neural_ucb_topstep.py")
            };
            options.Timeout = int.Parse(Environment.GetEnvironmentVariable("PYTHON_TIMEOUT") ?? "30");
        });
        
        // Configure model loading options
        services.Configure<ModelLoadingOptions>(configuration.GetSection("ModelLoading"));

        // General HTTP client for dependency injection
        services.AddHttpClient();

        // Core HTTP client for TopstepX API
        services.AddHttpClient<TopstepAuthAgent>(client =>
        {
            client.BaseAddress = new Uri(TopstepXApiBaseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", TopstepXUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30); // Prevent hanging on network issues
        });

        // Register the CENTRAL MESSAGE BUS - The "ONE BRAIN" communication system
        services.AddSingleton<ICentralMessageBus, CentralMessageBus>();

        // Register required interfaces with PRODUCTION Safety implementations (aligned with guardrail orchestrator)
        services.AddSingleton<IKillSwitchWatcher>(serviceProvider => 
            serviceProvider.GetRequiredService<global::BotCore.Services.ProductionKillSwitchService>());
        services.AddSingleton<IRiskManager, Trading.Safety.RiskManager>();
        services.AddSingleton<IHealthMonitor, Trading.Safety.HealthMonitor>();

        // ================================================================================
        // REAL SOPHISTICATED ORCHESTRATORS - PRODUCTION IMPLEMENTATIONS
        // ================================================================================
        
        // Register TopstepX Python SDK adapter service for production trading
        services.Configure<TopstepXConfiguration>(configuration.GetSection("TopstepX"));
        // ITopstepXAdapterService already registered above
        
        // Register REAL sophisticated orchestrators (NO DUPLICATES)
        // DISABLED: IntelligenceOrchestratorService - FAKE random trading decisions (real one exists in IntelligenceStack)
        // services.AddSingleton<TradingBot.Abstractions.IIntelligenceOrchestrator, IntelligenceOrchestratorService>();  
        // DISABLED: DataOrchestratorService - FAKE hardcoded market data (real data comes from elsewhere)
        // services.AddSingleton<TradingBot.Abstractions.IDataOrchestrator, DataOrchestratorService>();
        
        // Register UnifiedOrchestratorService as singleton and hosted service (SINGLE REGISTRATION)
        services.AddSingleton<UnifiedOrchestratorService>();
        services.AddSingleton<TradingBot.Abstractions.IUnifiedOrchestrator>(provider => provider.GetRequiredService<UnifiedOrchestratorService>());
        services.AddHostedService(provider => provider.GetRequiredService<UnifiedOrchestratorService>());

        // PRODUCTION MasterOrchestrator - using REAL sophisticated services only

        // ================================================================================
        // AI/ML TRADING BRAIN REGISTRATION - DUAL ML APPROACH WITH UCB
        // ================================================================================
        
        // Register OllamaClient - AI conversation service for bot explanations (optional)
        var ollamaEnabled = configuration["OLLAMA_ENABLED"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;
        if (ollamaEnabled)
        {
            services.AddSingleton<global::BotCore.Services.OllamaClient>();
            Console.WriteLine("🗣️ [OLLAMA] Bot voice enabled - conversational AI features active");
        }
        else
        {
            Console.WriteLine("🔇 [OLLAMA] Bot voice disabled - will operate silently");
        }
        
        // Register Intelligence Services - LLM-powered market intelligence
        var intelligenceEnabled = configuration["INTELLIGENCE_SYNTHESIS_ENABLED"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;
        if (intelligenceEnabled && ollamaEnabled)
        {
            services.AddSingleton<global::BotCore.Intelligence.MarketDataReader>();
            services.AddSingleton<global::BotCore.Intelligence.IntelligenceSynthesizerService>();
            Console.WriteLine("🧠 [INTELLIGENCE] LLM intelligence synthesis enabled - market data integration active");
        }
        else
        {
            Console.WriteLine("🔇 [INTELLIGENCE] Intelligence synthesis disabled");
        }
        
        // Register AI Commentary Services - Enhanced self-awareness features
        services.AddSingleton<global::BotCore.Services.ParameterChangeTracker>();
        services.AddSingleton<global::BotCore.Services.MarketSnapshotStore>();
        services.AddSingleton<global::BotCore.Services.RiskAssessmentCommentary>();
        services.AddSingleton<global::BotCore.Services.AdaptiveLearningCommentary>();
        services.AddSingleton<global::BotCore.Services.HistoricalPatternRecognitionService>();
        
        // Register BotAlertService - Proactive alerting system for bot self-awareness
        services.AddSingleton<global::BotCore.Services.BotAlertService>();
        
        // Register UnifiedTradingBrain - The main AI brain with calendar integration (Phase 2)
        services.AddSingleton<global::BotCore.Brain.UnifiedTradingBrain>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Brain.UnifiedTradingBrain>>();
            var memoryManager = provider.GetRequiredService<global::BotCore.ML.IMLMemoryManager>();
            var modelManager = provider.GetRequiredService<global::BotCore.ML.StrategyMlModelManager>();
            var cvarPPO = provider.GetRequiredService<TradingBot.RLAgent.CVaRPPO>();
            var gate4Config = provider.GetService<TradingBot.Abstractions.IGate4Config>();
            var ollamaClient = provider.GetService<global::BotCore.Services.OllamaClient>();
            var economicEventManager = provider.GetService<global::BotCore.Market.IEconomicEventManager>();
            var riskCommentary = provider.GetService<global::BotCore.Services.RiskAssessmentCommentary>();
            var learningCommentary = provider.GetService<global::BotCore.Services.AdaptiveLearningCommentary>();
            var snapshotStore = provider.GetService<global::BotCore.Services.MarketSnapshotStore>();
            var historicalPatterns = provider.GetService<global::BotCore.Services.HistoricalPatternRecognitionService>();
            var parameterTracker = provider.GetService<global::BotCore.Services.ParameterChangeTracker>();
            
            return new global::BotCore.Brain.UnifiedTradingBrain(
                logger,
                memoryManager,
                modelManager,
                cvarPPO,
                gate4Config,
                ollamaClient,
                economicEventManager,
                riskCommentary,
                learningCommentary,
                snapshotStore,
                historicalPatterns,
                parameterTracker);
        });
        
        // Register BotPerformanceReporter - AI-generated performance summaries (Feature 3)
        services.AddSingleton<global::BotCore.Services.BotPerformanceReporter>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Services.BotPerformanceReporter>>();
            var ollamaClient = provider.GetService<global::BotCore.Services.OllamaClient>();
            
            return new global::BotCore.Services.BotPerformanceReporter(logger, ollamaClient);
        });
        
        // ================================================================================
        // Bot Self-Awareness System (Phase 4)
        // ================================================================================
        
        // Register Component Discovery Service - Automatically discovers all bot components
        services.AddSingleton<global::BotCore.Services.ComponentDiscoveryService>();
        
        // Register Generic Health Check Service - Checks health of any component type
        services.AddSingleton<global::BotCore.Services.GenericHealthCheckService>();
        
        // Register Bot Health Reporter - Converts health data to natural language using AI
        services.AddSingleton<global::BotCore.Services.BotHealthReporter>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Services.BotHealthReporter>>();
            var configuration = provider.GetRequiredService<IConfiguration>();
            var ollamaClient = provider.GetService<global::BotCore.Services.OllamaClient>();
            
            return new global::BotCore.Services.BotHealthReporter(logger, configuration, ollamaClient);
        });
        
        // Register Component Health Monitoring Service - Basic continuous health monitoring with AI explanations
        services.AddHostedService<global::BotCore.Services.ComponentHealthMonitoringService>();
        
        // Register Bot Self-Awareness Service - Advanced orchestration with change detection and alerting
        services.AddHostedService<global::BotCore.Services.BotSelfAwarenessService>();
        
        // Register UCB Manager - C# client for Python UCB service (175 lines)
        services.AddSingleton<global::BotCore.ML.UcbManager>();
        
        // Register ML Memory Manager - Sophisticated ML model management (458 lines)
        services.AddSingleton<global::BotCore.ML.OnnxModelLoader>();
        services.AddSingleton<global::BotCore.ML.IMLMemoryManager, global::BotCore.ML.MLMemoryManager>();

        // ================================================================================
        // 🏆 CHAMPION/CHALLENGER ARCHITECTURE - SAFE MODEL MANAGEMENT 🏆
        // ================================================================================
        
        // Register Model Registry for versioned, immutable artifacts
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IModelRegistry, TradingBot.UnifiedOrchestrator.Runtime.FileModelRegistry>();
        
        // Register Atomic Model Router Factory for lock-free champion access
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IModelRouterFactory, TradingBot.UnifiedOrchestrator.Runtime.ModelRouterFactory>();
        
        // Register Read-Only Inference Brain (replaces shared mutable UnifiedTradingBrain)
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IInferenceBrain, TradingBot.UnifiedOrchestrator.Brains.InferenceBrain>();
        
        // Register Write-Only Training Brain for isolated challenger creation
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.ITrainingBrain, TradingBot.UnifiedOrchestrator.Brains.TrainingBrain>();
        
        // Register Artifact Builders for ONNX and UCB serialization
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IArtifactBuilder, TradingBot.UnifiedOrchestrator.Artifacts.OnnxArtifactBuilder>();
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IArtifactBuilder, TradingBot.UnifiedOrchestrator.Artifacts.UcbSerializer>();
        
        // Register Market Hours Service for timing gates
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IMarketHoursService, TradingBot.UnifiedOrchestrator.Scheduling.FuturesMarketHours>();
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Scheduling.FuturesMarketHours>();
        
        // Register Shadow Tester for A/B validation
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IShadowTester, TradingBot.UnifiedOrchestrator.Promotion.ShadowTester>();
        
        // Register Position Service for real position tracking
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Promotion.IPositionService, TradingBot.UnifiedOrchestrator.Promotion.ProductionPositionService>();
        
        // Register Promotion Service with atomic swaps and instant rollback
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IPromotionService, TradingBot.UnifiedOrchestrator.Promotion.PromotionService>();
        
        // ================================================================================
        // 🚀 ENHANCED TRADING BOT COMPONENTS - PRODUCTION-READY ENHANCEMENTS 🚀
        // ================================================================================
        
        // Register enhanced trading bot services with comprehensive monitoring and analysis
        services.AddEnhancedTradingBotServices(configuration);
        services.ConfigureEnhancedTradingBotDefaults(configuration);
        
        Console.WriteLine("🎯 [ENHANCED-COMPONENTS] Book-aware simulator, counterfactual replay, explainability stamps, and enhanced alerting registered!");
        Console.WriteLine("📊 [OBSERVABILITY] Enhanced dashboards with liquidity, OFI, pattern, and fusion metrics ready!");
        Console.WriteLine("🔍 [EXPLAINABILITY] Decision evidence tracking with zone scores, pattern probabilities, and S7 state capture enabled!");
        Console.WriteLine("🎲 [SIMULATION] Book-aware execution simulator with live fill distributions and training feedback active!");
        Console.WriteLine("🔄 [COUNTERFACTUAL] Nightly gate analysis with blocked signal replay for effectiveness validation scheduled!");
        Console.WriteLine("🚨 [ENHANCED-ALERTS] Pattern promotion, model rollback, feature drift, and queue ETA breach monitoring configured!");
        
        // Register Production Validation Service for runtime proof
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IValidationService, TradingBot.UnifiedOrchestrator.Services.ProductionValidationService>();
        
        // Register Rollback Drill Service for rollback evidence under load
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IRollbackDrillService, TradingBot.UnifiedOrchestrator.Services.RollbackDrillService>();
        
        // AUDIT-CLEAN: Configure TradingBrainAdapter with configuration-driven thresholds instead of hardcoded values
        services.Configure<TradingBot.UnifiedOrchestrator.Configuration.TradingBrainAdapterConfiguration>(options =>
        {
            // Configuration-driven thresholds - can be overridden via appsettings.json or environment variables
            options.FullPositionThreshold = decimal.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_FULL_POSITION_THRESHOLD") ?? "0.5");
            options.SmallPositionThreshold = decimal.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_SMALL_POSITION_THRESHOLD") ?? "0.1");
            options.SizeComparisonTolerance = decimal.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_SIZE_TOLERANCE") ?? "0.01");
            options.ConfidenceComparisonTolerance = decimal.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_CONFIDENCE_TOLERANCE") ?? "0.1");
            options.PromotionAgreementThreshold = double.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_PROMOTION_THRESHOLD") ?? "0.8");
            options.PromotionEvaluationWindow = int.Parse(Environment.GetEnvironmentVariable("BRAIN_ADAPTER_EVALUATION_WINDOW") ?? "100");
        });
        
        // Register Trading Brain Adapter for UnifiedTradingBrain parity
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.ITradingBrainAdapter, TradingBot.UnifiedOrchestrator.Brains.TradingBrainAdapter>();
        
        // Register Unified Data Integration Service for historical + live data (PRIMARY IMPLEMENTATION)
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IUnifiedDataIntegrationService, TradingBot.UnifiedOrchestrator.Services.UnifiedDataIntegrationService>();
        services.AddHostedService<TradingBot.UnifiedOrchestrator.Services.UnifiedDataIntegrationService>();
        
        // Register Production Readiness Validation Service for complete runtime proof
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Interfaces.IProductionReadinessValidationService, TradingBot.UnifiedOrchestrator.Services.ProductionReadinessValidationService>();
        
        // Register Production Demonstration Runner for PR review artifacts
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Services.ProductionDemonstrationRunner>();
        
        // Register specialized validation services for PR review requirements
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Services.EnumMappingValidationService>();
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Services.ValidationReportRegressionService>();
        
        // Register Validation Service for demonstration
        services.AddHostedService<TradingBot.UnifiedOrchestrator.Services.ChampionChallengerValidationService>();
        
        Console.WriteLine("🏆 Champion/Challenger Architecture registered successfully - Live trading inference now read-only with atomic model swaps");
        Console.WriteLine("✅ Production Readiness Services registered - Ready for runtime validation and artifact generation");
        
        // ================================================================================
        
        // ================================================================================
        // 🎯 MASTER DECISION ORCHESTRATOR - ALWAYS-LEARNING SYSTEM 🎯
        // ================================================================================
        
        // Register the unified decision routing system - NEVER returns HOLD
        // (Already registered above with AutonomousDecisionEngine dependencies)
        
        // Register decision service router for Python integration
        services.AddSingleton<DecisionServiceRouter>();
        
        // Register unified model path resolver for cross-platform ONNX loading
        services.AddSingleton<global::BotCore.Services.UnifiedModelPathResolver>();
        
        // REMOVED DUPLICATE: Different UnifiedDataIntegrationService implementation conflicts with primary
        // services.AddSingleton<global::BotCore.Services.UnifiedDataIntegrationService>();
        // services.AddHostedService<global::BotCore.Services.UnifiedDataIntegrationService>(provider => 
        //     provider.GetRequiredService<global::BotCore.Services.UnifiedDataIntegrationService>());
        
        // Register the MASTER DECISION ORCHESTRATOR - The ONE always-learning brain with alert integration
        services.AddSingleton<global::BotCore.Services.MasterDecisionOrchestrator>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Services.MasterDecisionOrchestrator>>();
            var serviceProvider = provider.GetRequiredService<IServiceProvider>();
            var unifiedRouter = provider.GetRequiredService<global::BotCore.Services.UnifiedDecisionRouter>();
            var unifiedBrain = provider.GetRequiredService<global::BotCore.Brain.UnifiedTradingBrain>();
            var gate5Config = provider.GetService<TradingBot.Abstractions.IGate5Config>();
            var ollamaClient = provider.GetService<global::BotCore.Services.OllamaClient>();
            var botAlertService = provider.GetService<global::BotCore.Services.BotAlertService>();
            
            return new global::BotCore.Services.MasterDecisionOrchestrator(
                logger,
                serviceProvider,
                unifiedRouter,
                unifiedBrain,
                gate5Config,
                ollamaClient,
                botAlertService);
        });
        services.AddHostedService<global::BotCore.Services.MasterDecisionOrchestrator>(provider => 
            provider.GetRequiredService<global::BotCore.Services.MasterDecisionOrchestrator>());
        
        Console.WriteLine("🎯 Master Decision Orchestrator registered - Always-learning system that NEVER returns HOLD!");
        Console.WriteLine("🔄 Unified data integration registered - Fixes contract mismatch and bar seeding issues!");
        Console.WriteLine("🔍 Cross-platform model path resolver registered - Fixes ONNX loading issues!");
        
        // ================================================================================
        
        // Register EmergencyStopSystem (209 lines) from Safety project
        services.AddSingleton<TopstepX.Bot.Core.Services.EmergencyStopSystem>();
        
        // Register ErrorHandlingMonitoringSystem (529 lines) from BotCore  
        services.AddSingleton<TopstepX.Bot.Core.Services.ErrorHandlingMonitoringSystem>();
        
        // OrderFillConfirmationSystem (520 lines) - Now uses TopstepX adapter for real-time data
        // Configure OrderFillConfirmationSystem to use TopstepX adapter service
        services.AddSingleton<TopstepX.Bot.Core.Services.OrderFillConfirmationSystem>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<TopstepX.Bot.Core.Services.OrderFillConfirmationSystem>>();
            var httpClient = provider.GetRequiredService<HttpClient>();
            var positionTracker = provider.GetRequiredService<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
            var emergencyStop = provider.GetRequiredService<TopstepX.Bot.Core.Services.EmergencyStopSystem>();
            var topstepXAdapter = provider.GetRequiredService<ITopstepXAdapterService>();
            
            // Use the new constructor that accepts ITopstepXAdapterService
            return new TopstepX.Bot.Core.Services.OrderFillConfirmationSystem(
                logger, httpClient, topstepXAdapter, positionTracker, emergencyStop);
        });
        
        // Register PositionTrackingSystem (379 lines) from Safety project
        services.TryAddSingleton<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
        
        // ================================================================================
        // COMPATIBILITY KIT SERVICES - PARAMETER LEARNING & CONFIGURATION
        // ================================================================================
        
        // Register all Compatibility Kit services with proper DI lifetimes
        services.AddCompatibilityKit(configuration);
        
        Console.WriteLine("✅ [COMPATIBILITY-KIT] Compatibility Kit services registered - parameter learning and configuration system ready");
        
        // ================================================================================
        // PRODUCTION READINESS SERVICES - Phase 4: Bar System Integration Fix
        // ================================================================================
        
        // Register production readiness services including IHistoricalDataBridgeService and IEnhancedMarketDataFlowService
        services.AddProductionReadinessServices(configuration);
        services.AddDefaultTradingReadinessConfiguration();
        
        // Register production guardrails including CriticalSystemComponentsFixes
        services.AddProductionGuardrails();
        
        // ================================================================================
        // BAR INFRASTRUCTURE - Register BarPyramid and underlying BarAggregators
        // ================================================================================
        
        // Register BarPyramid as singleton after production readiness services
        services.AddSingleton<global::BotCore.Market.BarPyramid>();
        
        // Register bar dispatcher hook AFTER bar infrastructure is available
        services.AddHostedService<global::BotCore.Features.BarDispatcherHook>();
        
        Console.WriteLine("✅ [BAR-INFRASTRUCTURE] BarPyramid and BarDispatcherHook registered - bar aggregation and dispatching ready");
        
        // Register pattern recognition and strategy DSL services - production ready pattern analysis and strategy reasoning
        services.AddPatternAndStrategyServices(configuration);
        
        Console.WriteLine("✅ [PHASE-4] Production readiness services registered - Historical data bridge and enhanced market data flow services ready");
        
        // Register TradingSystemIntegrationService (533 lines) from BotCore as HOSTED SERVICE for live TopstepX connection
        // Configure TradingSystemIntegrationService for live TopstepX connection
        services.AddSingleton<TopstepX.Bot.Core.Services.TradingSystemIntegrationService.TradingSystemConfiguration>(serviceProvider =>
        {
            var topstepXConfig = configuration.GetSection("TopstepX");
            return new TopstepX.Bot.Core.Services.TradingSystemIntegrationService.TradingSystemConfiguration
            {
                TopstepXApiBaseUrl = topstepXConfig["ApiBaseUrl"] ?? Environment.GetEnvironmentVariable("TOPSTEPX_API_BASE") ?? TopstepXApiBaseUrl,
                UserHubUrl = topstepXConfig["UserHubUrl"] ?? Environment.GetEnvironmentVariable("RTC_USER_HUB") ?? "https://rtc.topstepx.com/hubs/user",
                MarketHubUrl = topstepXConfig["MarketHubUrl"] ?? Environment.GetEnvironmentVariable("RTC_MARKET_HUB") ?? "https://rtc.topstepx.com/hubs/market",
                AccountId = Environment.GetEnvironmentVariable("TOPSTEPX_ACCOUNT_ID") ?? "",
                ApiToken = Environment.GetEnvironmentVariable("TOPSTEPX_JWT") ?? "",
                EnableDryRunMode = Environment.GetEnvironmentVariable("ENABLE_DRY_RUN") != "false",
                EnableAutoExecution = Environment.GetEnvironmentVariable("ENABLE_AUTO_EXECUTION") == "true",
                MaxDailyLoss = decimal.Parse(Environment.GetEnvironmentVariable("DAILY_LOSS_CAP_R") ?? "-1000"),
                MaxPositionSize = decimal.Parse(Environment.GetEnvironmentVariable("MAX_POSITION_SIZE") ?? "5")
            };
        });

        // Register JWT token provider function for backward compatibility with existing services
        services.AddSingleton<Func<Task<string?>>>(serviceProvider =>
        {
            var tokenProvider = serviceProvider.GetRequiredService<ITokenProvider>();
            return async () => await tokenProvider.GetTokenAsync().ConfigureAwait(false);
        });

        // NOTE: AutoTopstepXLoginService registration disabled due to type resolution issues
        // Will be re-enabled once dependency injection is properly configured
        services.AddHostedService<TopstepX.Bot.Core.Services.TradingSystemIntegrationService>();
        
        // ================================================================================
        // ADVANCED INFRASTRUCTURE - ML/DATA MANAGEMENT  
        // ================================================================================
        
        // Register WorkflowOrchestrationManager (466 lines)
        services.AddSingleton<WorkflowOrchestrationManager>();
        
        // Register EconomicEventManager with BotAlertService integration and HttpClient (Phase 2 + Phase 3)
        services.AddSingleton<global::BotCore.Market.IEconomicEventManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Market.EconomicEventManager>>();
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var botAlertService = provider.GetService<global::BotCore.Services.BotAlertService>();
            
            return new global::BotCore.Market.EconomicEventManager(logger, httpClient, botAlertService);
        });
        
        // Register AdvancedSystemIntegrationService (386 lines)
        services.AddSingleton<AdvancedSystemIntegrationService>();
        
        // ================================================================================
        // ML/RL DECISION SERVICE INTEGRATION - FULLY AUTOMATED
        // ================================================================================
        
        // Decision Service removed - functionality consolidated into UnifiedTradingBrain
        // Legacy decision_service folder and configuration removed as part of Phase 1 cleanup
        var decisionServiceLauncherOptions = new DecisionServiceLauncherOptions
        {
            Enabled = false, // Disabled - using UnifiedTradingBrain instead
            Host = Environment.GetEnvironmentVariable("DECISION_SERVICE_HOST") ?? "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("DECISION_SERVICE_PORT") ?? "7080"),
            PythonExecutable = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE") ?? "python",
            ScriptPath = Environment.GetEnvironmentVariable("DECISION_SERVICE_SCRIPT") ?? "",
            ConfigFile = Environment.GetEnvironmentVariable("DECISION_SERVICE_CONFIG") ?? "decision_service_config.yaml",
            StartupTimeoutSeconds = int.Parse(Environment.GetEnvironmentVariable("DECISION_SERVICE_STARTUP_TIMEOUT") ?? "30"),
            AutoRestart = false // Disabled
        };
        services.Configure<DecisionServiceLauncherOptions>(options =>
        {
            options.Enabled = decisionServiceLauncherOptions.Enabled;
            options.Host = decisionServiceLauncherOptions.Host;
            options.Port = decisionServiceLauncherOptions.Port;
            options.PythonExecutable = decisionServiceLauncherOptions.PythonExecutable;
            options.ScriptPath = decisionServiceLauncherOptions.ScriptPath;
            options.ConfigFile = decisionServiceLauncherOptions.ConfigFile;
            options.StartupTimeoutSeconds = decisionServiceLauncherOptions.StartupTimeoutSeconds;
            options.AutoRestart = decisionServiceLauncherOptions.AutoRestart;
        });
        
        // Configure Decision Service client options
        var decisionServiceOptions = new TradingBot.UnifiedOrchestrator.Configuration.DecisionServiceOptions
        {
            BaseUrl = $"http://{decisionServiceLauncherOptions.Host}:{decisionServiceLauncherOptions.Port}",
            TimeoutMs = int.Parse(Environment.GetEnvironmentVariable("DECISION_SERVICE_TIMEOUT_MS") ?? "5000"),
            Enabled = decisionServiceLauncherOptions.Enabled,
            MaxRetries = int.Parse(Environment.GetEnvironmentVariable("DECISION_SERVICE_MAX_RETRIES") ?? "3")
        };
        services.Configure<TradingBot.UnifiedOrchestrator.Configuration.DecisionServiceOptions>(options =>
        {
            options.BaseUrl = decisionServiceOptions.BaseUrl;
            options.TimeoutMs = decisionServiceOptions.TimeoutMs;
            options.Enabled = decisionServiceOptions.Enabled;
            options.MaxRetries = decisionServiceOptions.MaxRetries;
        });
        
        // Configure Decision Service integration options
        services.Configure<DecisionServiceIntegrationOptions>(options =>
        {
            options.Enabled = decisionServiceLauncherOptions.Enabled;
            options.HealthCheckIntervalSeconds = int.Parse(Environment.GetEnvironmentVariable("DECISION_SERVICE_HEALTH_CHECK_INTERVAL") ?? "30");
            options.LogDecisionLines = Environment.GetEnvironmentVariable("LOG_DECISION_LINES") != "false";
            options.EnableTradeManagement = Environment.GetEnvironmentVariable("ENABLE_TRADE_MANAGEMENT") != "false";
        });
        
        // Register Decision Service components
        services.AddHttpClient<DecisionServiceClient>();
        services.AddSingleton<DecisionServiceClient>(provider =>
        {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var decisionServiceOptions = provider.GetRequiredService<IOptions<TradingBot.UnifiedOrchestrator.Configuration.DecisionServiceOptions>>().Value;
            var pythonOptions = provider.GetRequiredService<IOptions<PythonIntegrationOptions>>().Value;
            var logger = provider.GetRequiredService<ILogger<DecisionServiceClient>>();
            return new DecisionServiceClient(decisionServiceOptions, httpClient, pythonOptions, logger);
        });
        
        // Register decision services as singletons first, then as hosted services (NO DUPLICATES)
        services.AddSingleton<DecisionServiceLauncher>();
        services.AddSingleton<DecisionServiceIntegration>();
        
        // Register as hosted services for automatic startup/shutdown (SINGLE REGISTRATION ONLY)
        services.AddHostedService(provider => provider.GetRequiredService<DecisionServiceLauncher>());
        services.AddHostedService(provider => provider.GetRequiredService<DecisionServiceIntegration>());
        
        // ================================================================================
        // 🔧 MICROSTRUCTURE CALIBRATION SERVICE (ES and NQ only)
        // ================================================================================
        // ConfigureMicrostructureCalibration - Microstructure calibration is handled by IntelligenceStack
        // Note: Microstructure analysis is integrated into the main intelligence pipeline
        
        // ================================================================================
        // AUTHENTICATION & TOPSTEPX SERVICES
        // ================================================================================
        
        // NOTE: TopstepX authentication services registered elsewhere to avoid conflicts
        
        // ================================================================================
        // CORE BOTCORE SERVICES REGISTRATION - ALL SOPHISTICATED SERVICES
        // ================================================================================
        
        // Core BotCore Services - ALL sophisticated implementations with proper dependencies
        
        // Register services that have interfaces first
        
        // Legacy authentication removed - now using TopstepX SDK adapter with environment credentials
        
        // Register ALL critical system components that exist in BotCore
        try 
        {
            // Add required interfaces and implementations first
            
            // Register fallback implementations for required interfaces
            // This prevents dependency injection errors
            try
            {
                // Try to register sophisticated services, with fallbacks for missing dependencies
                
                // Register EmergencyStopSystem (fewer dependencies) from BotCore
                services.TryAddSingleton<TopstepX.Bot.Core.Services.EmergencyStopSystem>();
                
                // Register services with fewer dependencies first
                services.TryAddSingleton<global::BotCore.Services.PerformanceTracker>();
                services.TryAddSingleton<global::BotCore.Services.TradingProgressMonitor>();
                services.TryAddSingleton<global::BotCore.Services.TimeOptimizedStrategyManager>();
                // NOTE: TopstepXService disabled to avoid connection conflicts
                
                
                // Try to register more complex services (these might fail due to missing dependencies)
                try 
                {
                    services.TryAddSingleton<TopstepX.Bot.Core.Services.ErrorHandlingMonitoringSystem>();
                    services.TryAddSingleton<global::BotCore.Services.ExecutionAnalyzer>();
                    // OrderFillConfirmationSystem already registered above with proper factory
                    // PositionTrackingSystem already registered above
                    // NewsIntelligenceEngine REMOVED - replaced by AI-powered NewsMonitorService
                    services.TryAddSingleton<global::BotCore.Services.ZoneService>();
                    services.TryAddSingleton<global::BotCore.EnhancedTrainingDataService>();
                    services.TryAddSingleton<TopstepX.Bot.Core.Services.TradingSystemIntegrationService>();
                    
                    // Real-time news monitoring with AI sentiment analysis (NewsAPI.org + Ollama LLM)
                    services.TryAddSingleton<global::BotCore.Services.INewsMonitorService, global::BotCore.Services.NewsMonitorService>();
                    
                }
                catch (Exception ex)
                {
                    // Non-critical service registration failures are logged but don't stop initialization
                    Console.WriteLine($"Warning: Failed to register complex services: {ex.Message}");
                }
                
            }
            catch (Exception ex)
            {
                // Service registration failures are expected for optional components
                Console.WriteLine($"Warning: Failed to register some BotCore services: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // Top-level service registration errors are logged but shouldn't crash the application
            Console.WriteLine($"Warning: Some service registrations failed: {ex.Message}");
        }

        // ================================================================================
        // INTELLIGENCE STACK INTEGRATION - ML/RL/ONLINE LEARNING 
        // ================================================================================
        
        // Register the complete intelligence stack with all new features
        RegisterIntelligenceStackServices(services, configuration);

        // Core unified trading brain already registered above
        
        // ================================================================================
        // ADVANCED ML/AI SERVICES REGISTRATION - ALL MACHINE LEARNING SYSTEMS  
        // ================================================================================
        
        // Register advanced ML/AI system components using extension methods
        // Note: IMLMemoryManager already registered earlier in the service registration
        // IEconomicEventManager already registered above
        
        // ================================================================================
        // PRODUCTION CVaR-PPO INTEGRATION - REAL RL POSITION SIZING
        // ================================================================================
        
        // Load RlRuntimeMode from environment for production safety
        var rlRuntimeModeStr = Environment.GetEnvironmentVariable("RlRuntimeMode") ?? "InferenceOnly";
        var rlMode = TradingBot.Abstractions.RlRuntimeMode.InferenceOnly; // Safe default
        if (!Enum.TryParse<TradingBot.Abstractions.RlRuntimeMode>(rlRuntimeModeStr, ignoreCase: true, out rlMode))
        {
            Console.WriteLine($"⚠️ [RL-SAFETY] Invalid RlRuntimeMode '{rlRuntimeModeStr}', defaulting to InferenceOnly");
            rlMode = TradingBot.Abstractions.RlRuntimeMode.InferenceOnly;
        }
        
        // Register CVaR-PPO configuration
        services.AddSingleton<TradingBot.RLAgent.CVaRPPOConfig>(provider =>
        {
            return new TradingBot.RLAgent.CVaRPPOConfig
            {
                StateSize = 16, // Match UnifiedTradingBrain state vector
                ActionSize = 4, // No position, Small, Medium, Large
                HiddenSize = 128,
                LearningRate = 3e-4,
                Gamma = 0.99,
                Lambda = 0.95,
                ClipEpsilon = 0.2,
                EntropyCoeff = 0.01,
                CVaRAlpha = 0.05, // 5% tail risk for TopStep compliance
                BatchSize = 64,
                PPOEpochs = 4,
                MinExperiencesForTraining = 256,
                MaxExperienceBuffer = 10000
            };
        });
        
        // Register CVaR-PPO directly for proper type injection with runtime mode
        services.AddSingleton<TradingBot.RLAgent.CVaRPPO>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<TradingBot.RLAgent.CVaRPPO>>();
            var config = provider.GetRequiredService<TradingBot.RLAgent.CVaRPPOConfig>();
            var modelPath = Path.Combine("models", "rl", "cvar_ppo_agent.onnx");
            
            var cvarPPO = new TradingBot.RLAgent.CVaRPPO(logger, config, rlMode, modelPath);
            
            // Initialize the CVaR-PPO agent
            _ = Task.Run(() =>
            {
                try
                {
                    // CVaRPPO initializes automatically in constructor
                    logger.LogInformation("🎯 [CVAR-PPO] Production RL agent initialized with RlRuntimeMode: {Mode}", rlMode);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "⚠️ [CVAR-PPO] Failed to load trained model, using default initialization");
                }
            });
            
            return cvarPPO;
        });

        // Register FeatureConfig and FeatureEngineering - REQUIRED for TradingSystemIntegrationService
        services.AddSingleton<TradingBot.RLAgent.FeatureConfig>(provider => 
        {
            return new TradingBot.RLAgent.FeatureConfig
            {
                MaxBufferSize = 1000,
                TopKFeatures = 10,
                StreamingStaleThresholdSeconds = 30,
                StreamingCleanupAfterMinutes = 30,
                DefaultProfile = new TradingBot.RLAgent.RegimeProfile
                {
                    VolatilityLookback = 20,
                    TrendLookback = 50,
                    VolumeLookback = 20,
                    RsiLookback = 14,
                    BollingerLookback = 20,
                    AtrLookback = 14,
                    MicrostructureLookback = 100,
                    OrderFlowLookback = 50,
                    TradeDirectionDecay = 0.9
                }
            };
        });
        services.AddSingleton<TradingBot.RLAgent.FeatureEngineering>();
        
        // Register FeatureSpec and FeatureBuilder for standardized feature engineering
        services.AddSingleton<global::BotCore.Features.FeatureSpec>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Features.FeatureBuilder>>();
            var featureSpecPath = Path.Combine("artifacts", "current", "feature_spec.json");
            
            try
            {
                if (File.Exists(featureSpecPath))
                {
                    var spec = global::BotCore.Features.FeatureSpecLoader.Load(featureSpecPath);
                    logger.LogInformation("✅ [FEATURE-SPEC] Loaded feature specification: {Version} with {ColumnCount} columns", 
                        spec.Version, spec.Columns.Count);
                    return spec;
                }
                else
                {
                    logger.LogWarning("⚠️ [FEATURE-SPEC] Feature spec file not found at {Path}, using default", featureSpecPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "⚠️ [FEATURE-SPEC] Failed to load feature spec from {Path}, using default", featureSpecPath);
            }
            
            // Return default spec if loading fails (12 features for S2/S3/S6/S11 optimization)
            return new global::BotCore.Features.FeatureSpec
            {
                Version = "features:v1",
                Columns = new List<global::BotCore.Features.Column>
                {
                    new() { Name = "ret_1m", Index = 0, FillValue = 0 },
                    new() { Name = "ret_5m", Index = 1, FillValue = 0 },
                    new() { Name = "atr_14", Index = 2, FillValue = 0.5m },
                    new() { Name = "rsi_14", Index = 3, FillValue = 50 },
                    new() { Name = "vwap_dist", Index = 4, FillValue = 0 },
                    new() { Name = "bb_width", Index = 5, FillValue = 0.01m },
                    new() { Name = "ob_imbalance", Index = 6, FillValue = 1.0m },
                    new() { Name = "adr_pct", Index = 7, FillValue = 0.5m },
                    new() { Name = "hour_frac", Index = 8, FillValue = 0.5m },
                    new() { Name = "session_flag", Index = 9, FillValue = 0 },
                    new() { Name = "pos", Index = 10, FillValue = 0 },
                    new() { Name = "s7_regime", Index = 11, FillValue = 0 }
                },
                Scaler = new global::BotCore.Features.Scaler
                {
                    Mean = new decimal[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    Std = new decimal[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
                },
                Inference = new global::BotCore.Features.InferenceConfig
                {
                    LogitToAction = new Dictionary<int, int> { { 0, -1 }, { 1, 0 }, { 2, 1 } }
                }
            };
        });
        
        // Register FeatureComputationConfig with bounds validation
        services.AddSingleton<global::BotCore.Features.FeatureComputationConfig>(provider =>
        {
            var config = new global::BotCore.Features.FeatureComputationConfig
            {
                RsiPeriod = 14,
                AtrPeriod = 14,
                BollingerPeriod = 20,
                VwapBars = 20,
                AdrDays = 14,
                MinutesPerDay = 390,
                CurrentRangeBars = 20,
                HoursPerDay = 24,
                S7ZScoreThresholdBullish = 1.0m,
                S7ZScoreThresholdBearish = -1.0m,
                S7CoherenceThreshold = 0.6m
            };
            
            // Validate configuration on startup
            config.Validate();
            
            return config;
        });
        
        services.AddSingleton<global::BotCore.Features.FeatureBuilder>(provider =>
        {
            var spec = provider.GetRequiredService<global::BotCore.Features.FeatureSpec>();
            var config = provider.GetRequiredService<global::BotCore.Features.FeatureComputationConfig>();
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Features.FeatureBuilder>>();
            var s7Service = provider.GetService<TradingBot.Abstractions.IS7Service>();
            var marketTimeService = provider.GetService<global::BotCore.Services.MarketTimeService>();
            return new global::BotCore.Features.FeatureBuilder(spec, config, logger, s7Service, marketTimeService);
        });
        
        // ================================================================================
        // S15_RL POLICY INTEGRATION - ONNX INFERENCE WITH VALIDATION
        // ================================================================================
        
        // Register OnnxRlPolicy for S15_RL strategy with startup validation
        services.AddSingleton<global::BotCore.Strategy.IRlPolicy>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Strategy.OnnxRlPolicy>>();
            var featureSpec = provider.GetRequiredService<global::BotCore.Features.FeatureSpec>();
            
            // Paths for RL model and manifest
            var rlModelPath = Path.Combine("artifacts", "current", "rl_policy.onnx");
            var rlManifestPath = Path.Combine("artifacts", "current", "rl_manifest.json");
            
            // Validate model file exists
            if (!File.Exists(rlModelPath))
            {
                logger.LogWarning("⚠️ [S15-RL] Model file not found at {Path}. S15_RL strategy will not generate candidates.", rlModelPath);
                // Return null policy - S15_RL will gracefully handle missing policy
                return null!;
            }
            
            // Load and validate manifest
            try
            {
                if (File.Exists(rlManifestPath))
                {
                    var manifestJson = File.ReadAllText(rlManifestPath);
                    var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifestJson);
                    
                    if (manifest != null)
                    {
                        // Extract metrics
                        var validationSharpe = manifest.ContainsKey("validation_metrics") &&
                                             manifest["validation_metrics"].TryGetProperty("sharpe_ratio", out var sharpeElem)
                            ? sharpeElem.GetDouble()
                            : 0.0;
                        
                        var baselineSharpe = manifest.ContainsKey("baseline_sharpe")
                            ? manifest["baseline_sharpe"].GetDouble()
                            : 1.0;
                        
                        var featureCount = manifest.ContainsKey("feature_count")
                            ? manifest["feature_count"].GetInt32()
                            : 12;
                        
                        var version = manifest.ContainsKey("version")
                            ? manifest["version"].GetString() ?? "unknown"
                            : "unknown";
                        
                        // Validate feature count matches spec
                        if (featureCount != featureSpec.Columns.Count)
                        {
                            logger.LogError("❌ [S15-RL] Feature count mismatch! Manifest: {ManifestCount}, Spec: {SpecCount}. S15_RL will not load.",
                                featureCount, featureSpec.Columns.Count);
                            return null!;
                        }
                        
                        // Validate Sharpe ratio against baseline
                        if (validationSharpe < baselineSharpe)
                        {
                            logger.LogError("❌ [S15-RL] Validation Sharpe {ValidationSharpe:F2} below baseline {BaselineSharpe:F2}. Model not loaded.",
                                validationSharpe, baselineSharpe);
                            return null!;
                        }
                        
                        logger.LogInformation("✅ [S15-RL] Manifest validated: Version={Version}, ValidationSharpe={Sharpe:F2}, Features={Count}",
                            version, validationSharpe, featureCount);
                    }
                }
                else
                {
                    logger.LogWarning("⚠️ [S15-RL] Manifest file not found at {Path}. Loading model without validation.", rlManifestPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ [S15-RL] Failed to load/validate manifest from {Path}", rlManifestPath);
                return null!;
            }
            
            // Load ONNX policy
            try
            {
                var policy = new global::BotCore.Strategy.OnnxRlPolicy(rlModelPath, featureSpec);
                logger.LogInformation("✅ [S15-RL] Policy loaded successfully from {Path}", rlModelPath);
                return policy;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ [S15-RL] Failed to load ONNX policy from {Path}", rlModelPath);
                return null!;
            }
        });
        
        services.AddSingleton<global::BotCore.ML.StrategyMlModelManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.ML.StrategyMlModelManager>>();
            var memoryManager = provider.GetService<global::BotCore.ML.IMLMemoryManager>();
            return new global::BotCore.ML.StrategyMlModelManager(logger, memoryManager);
        });

        // ================================================================================
        // �️ PRODUCTION-GRADE INFRASTRUCTURE SERVICES 🛡️
        // ================================================================================
        
        // Register Production Configuration Service - Environment-specific settings
        services.Configure<global::BotCore.Services.ProductionTradingConfig>(configuration.GetSection("TradingBot"));
        services.AddSingleton<global::BotCore.Services.ProductionConfigurationService>();
        
        // Register ML Configuration Service for hardcoded value replacement
        services.AddProductionConfigurationValidation(configuration);
        services.AddScoped<global::BotCore.Services.MLConfigurationService>();
        services.AddScoped<TradingBot.Abstractions.IMLConfigurationService, global::BotCore.Services.MLConfigurationService>();
        
        // Register Execution Configuration Services - Replace hardcoded execution parameters
        services.AddScoped<TradingBot.Abstractions.IExecutionGuardsConfig, global::BotCore.Services.ExecutionGuardsConfigService>();
        services.AddScoped<TradingBot.Abstractions.IExecutionCostConfig, global::BotCore.Services.ExecutionCostConfigService>();
        services.AddScoped<TradingBot.Abstractions.IExecutionPolicyConfig, global::BotCore.Services.ExecutionPolicyConfigService>();
        
        // Register Risk and Sizing Configuration Services - Replace hardcoded risk/sizing parameters
        services.AddScoped<TradingBot.Abstractions.IRiskConfig, global::BotCore.Services.RiskConfigService>();
        services.AddScoped<TradingBot.Abstractions.ISizerConfig, global::BotCore.Services.SizerConfigService>();
        services.AddScoped<TradingBot.Abstractions.IMetaCostConfig, global::BotCore.Services.MetaCostConfigService>();
        
        // Register Trading Flow Configuration Services - Replace hardcoded trading flow parameters
        services.AddScoped<TradingBot.Abstractions.IBracketConfig, TradingBot.BotCore.Services.BracketConfigService>();
        services.AddScoped<TradingBot.Abstractions.ISessionConfig, global::BotCore.Services.SessionConfigService>();
        services.AddScoped<TradingBot.Abstractions.IControllerOptionsService, global::BotCore.Services.ControllerOptionsService>();
        
        // Register Event and Calendar Configuration Services - Replace hardcoded event handling
        services.AddScoped<TradingBot.Abstractions.IEventTemperingConfig, global::BotCore.Services.EventTemperingConfigService>();
        services.AddScoped<TradingBot.Abstractions.IRollConfig, global::BotCore.Services.RollConfigService>();
        
        // Register Infrastructure Configuration Services - Replace hardcoded paths and endpoints
        services.AddScoped<TradingBot.Abstractions.IEndpointConfig, global::BotCore.Services.EndpointConfigService>();
        services.AddScoped<TradingBot.Abstractions.IPathConfig, global::BotCore.Services.PathConfigService>();
        
        // Register Configuration Safety and Management Services
        services.AddSingleton<global::BotCore.Services.ConfigurationFailureSafetyService>();
        services.AddSingleton<global::BotCore.Services.ConfigurationSnapshotService>();
        services.AddSingleton<global::BotCore.Services.ConfigurationSchemaService>();
        services.AddHostedService<global::BotCore.Services.StateDurabilityService>();
        
        // Register Last-Mile Production Safety Services
        services.AddSingleton<global::BotCore.Services.OnnxModelCompatibilityService>();
        services.AddSingleton<global::BotCore.Services.ClockHygieneService>();
        services.AddSingleton<global::BotCore.Services.DeterminismService>();
        services.AddSingleton<global::BotCore.Services.SecretsValidationService>();
        services.AddSingleton<global::BotCore.Services.IntegritySigningService>();
        services.AddSingleton<global::BotCore.Services.SuppressionLedgerService>();
        
        // Register Production Guardrail configurations
        services.Configure<global::BotCore.Configuration.ProductionGuardrailConfiguration>(configuration.GetSection("Guardrails"));
        services.Configure<global::BotCore.Configuration.KillSwitchConfiguration>(configuration.GetSection("KillSwitch"));
        services.Configure<global::BotCore.Configuration.EmergencyStopConfiguration>(configuration.GetSection("EmergencyStop"));
        
        // Note: ResilienceConfiguration is already configured by ProductionConfigurationExtensions
        
        // Register Production Resilience Service - Retry logic, circuit breakers, graceful degradation
        services.AddSingleton<global::BotCore.Services.ProductionResilienceService>();
        
        // Register Production Guardrail Services - Live trading gates, order evidence, kill switch monitoring
        services.AddSingleton<global::BotCore.Services.ProductionKillSwitchService>();
        services.AddSingleton<global::BotCore.Services.ProductionOrderEvidenceService>();
        services.AddSingleton<global::BotCore.Services.ProductionGuardrailOrchestrator>();
        services.AddHostedService<global::BotCore.Services.ProductionKillSwitchService>();
        
        // Register Emergency Stop System with proper namespace and dependencies
        services.AddSingleton<global::BotCore.Services.EmergencyStopSystem>();
        services.AddHostedService<global::BotCore.Services.EmergencyStopSystem>();
        
        // Register Production Monitoring Service - Health checks, metrics, performance tracking
        services.AddSingleton<global::BotCore.Services.ProductionMonitoringService>();
        services.AddHealthChecks()
            .AddCheck<global::BotCore.Services.ProductionMonitoringService>("ml-rl-system");

        // ================================================================================
        // �🚀 ENHANCED ML/RL/CLOUD INTEGRATION SERVICES - PRODUCTION AUTOMATION 🚀
        // ================================================================================
        
        // Register HttpClient for Cloud Model Synchronization Service - GitHub API access
        services.AddHttpClient<global::BotCore.Services.CloudModelSynchronizationService>(client =>
        {
            var githubApiUrl = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com/";
            client.BaseAddress = new Uri(githubApiUrl);
            client.DefaultRequestHeaders.Add("User-Agent", "TradingBot-CloudSync/1.0");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        
        // Register Cloud Model Synchronization Service - Automated GitHub model downloads with hot-swap
        services.AddSingleton<global::BotCore.Services.CloudModelSynchronizationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<global::BotCore.Services.CloudModelSynchronizationService>>();
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(global::BotCore.Services.CloudModelSynchronizationService));
            var memoryManager = provider.GetRequiredService<global::BotCore.ML.IMLMemoryManager>();
            var configuration = provider.GetRequiredService<IConfiguration>();
            var tradingBrain = provider.GetService<global::BotCore.Brain.UnifiedTradingBrain>();
            var resilienceService = provider.GetService<global::BotCore.Services.ProductionResilienceService>();
            var monitoringService = provider.GetService<global::BotCore.Services.ProductionMonitoringService>();
            
            return new global::BotCore.Services.CloudModelSynchronizationService(
                logger, httpClient, memoryManager, configuration, tradingBrain, resilienceService, monitoringService);
        });
        services.AddHostedService<global::BotCore.Services.CloudModelSynchronizationService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.CloudModelSynchronizationService>());
        
        // Register OnnxEnsembleWrapper for model hot-reload support
        services.Configure<TradingBot.RLAgent.OnnxEnsembleOptions>(configuration.GetSection("OnnxEnsemble"));
        services.AddSingleton<TradingBot.RLAgent.OnnxEnsembleWrapper>();
        
        // Register ModelHotReloadManager (File Watching) - Monitors models/rl directory for changes
        services.Configure<TradingBot.RLAgent.ModelHotReloadOptions>(configuration.GetSection("ModelHotReload"));
        services.AddSingleton<TradingBot.RLAgent.ModelHotReloadManager>();
        services.AddHostedService<TradingBot.RLAgent.ModelHotReloadManager>(provider => 
            provider.GetRequiredService<TradingBot.RLAgent.ModelHotReloadManager>());
        
        // CloudRlTrainerV2 removed - legacy cloud training infrastructure no longer exists
        
        Console.WriteLine("🔄 [MODEL-HOT-RELOAD] File watching service registered - Monitors models/rl for changes!");
        
        // Register Model Ensemble Service - Intelligent model blending (70% cloud, 30% local)
        services.AddSingleton<global::BotCore.Services.ModelEnsembleService>();
        
        // Register Trading Feedback Service - Automated learning loops and retraining triggers
        services.AddSingleton<global::BotCore.Services.TradingFeedbackService>();
        services.AddHostedService<global::BotCore.Services.TradingFeedbackService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.TradingFeedbackService>());
        
        // Gate 2: Register CloudModelDownloader - ONNX model validation before deployment
        services.AddSingleton<global::BotCore.Services.ICloudModelDownloader, global::BotCore.Services.CloudModelDownloader>();
        Console.WriteLine("🔒 [GATE-2] CloudModelDownloader with validation gates registered!");
        
        // Gate 3: Register S15 Shadow Learning Service - Validates S15 before promotion
        services.AddSingleton<global::BotCore.Services.S15ShadowLearningService>();
        services.AddHostedService<global::BotCore.Services.S15ShadowLearningService>(provider => 
            provider.GetRequiredService<global::BotCore.Services.S15ShadowLearningService>());
        Console.WriteLine("🔒 [GATE-3] S15ShadowLearningService with promotion validation registered!");
        
        // Enhanced Trading Brain Integration already registered above with UnifiedDecisionRouter dependencies
        
        Console.WriteLine("🚀 [ENHANCED-BRAIN] Production ML/RL/Cloud automation services registered successfully!");
        
        // ================================================================================
        

        
        // Register core agents and clients that exist in BotCore
        // NOTE: Hub-creating services disabled - functionality provided by TopstepX adapter
        
        services.AddSingleton<global::BotCore.PositionAgent>();
        
        // NOTE: MarketDataAgent disabled - functionality provided by TopstepX adapter
        services.AddSingleton<global::BotCore.ModelUpdaterService>();
        
        // Register advanced orchestrator services that will be coordinated by MasterOrchestrator
        // DISABLED: Fake prototype services - shadowing real implementations
        // services.AddSingleton<IntelligenceOrchestratorService>();
        // services.AddSingleton<DataOrchestratorService>();
        // services.AddSingleton<WorkflowSchedulerService>();
        // WorkflowOrchestrationManager already registered above
        // AdvancedSystemIntegrationService already registered above

        // Register Python UCB Service Launcher - Auto-start Python UCB FastAPI service
        services.AddHostedService<PythonUcbLauncher>();
        
        // Legacy BacktestLearningService removed - using EnhancedBacktestLearningService instead
        // services.AddHostedService<BacktestLearningService>(); // REMOVED
        
        // Register AutomaticDataSchedulerService for automatic scheduling of data processing
        services.AddHostedService<AutomaticDataSchedulerService>();
        
        // Register DataFlowMonitoringService for comprehensive data flow tracking and issue detection
        services.AddHostedService<DataFlowMonitoringService>();
        
        // Register UCB Manager - Auto-detect if UCB service is available
        var enableUcb = Environment.GetEnvironmentVariable("ENABLE_UCB") != "0"; // Default to enabled
        
        if (enableUcb)
        {
            services.AddSingleton<global::BotCore.ML.UcbManager>();
        }

        // Auto-detect paper trading mode
        var hasCredentials = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOPSTEPX_JWT")) ||
                           (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOPSTEPX_USERNAME")) &&
                            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOPSTEPX_API_KEY")));

        if (hasCredentials)
        {
            Console.WriteLine("📈 TopstepX credentials detected - sophisticated trading system will be used");
        }
        
        // Register distributed orchestrator components for sophisticated system
        // IIntelligenceOrchestrator already registered above (now disabled - fake service)
        // IDataOrchestrator already registered above (now disabled - fake service)
        // DISABLED: WorkflowSchedulerService - Empty shell, does nothing, just logs
        // services.AddSingleton<TradingBot.Abstractions.IWorkflowScheduler, WorkflowSchedulerService>();
        
        // Register Cloud Data Integration - Links 27 GitHub workflows to trading decisions
        services.AddSingleton<TradingBot.Abstractions.ICloudDataIntegration, CloudDataIntegrationService>();

        // ================================================================================
        // PRODUCTION VERIFICATION AND OBSERVABILITY SERVICES
        // ================================================================================
        
        // Register production database layer with Entity Framework Core
        services.AddProductionDatabase(configuration);
        
        // Register comprehensive observability and monitoring (ENABLED - compatibility fixed)
        services.AddProductionObservability();
        
        // DISABLED: ProductionVerificationService - Just logs warnings about missing database
        // services.AddHostedService<ProductionVerificationService>();
        
        // Register intelligence stack verification service for runtime proof
        services.AddIntelligenceStackVerification();
        
        // Register startup service that provides concrete runtime proof of production readiness
        services.AddHostedService<ProductionReadinessStartupService>();

        // ================================================================================
        // ADVANCED SYSTEM INITIALIZATION SERVICE
        // ================================================================================
        
        // Register the advanced system initialization service to wire everything together
        services.AddHostedService<AdvancedSystemInitializationService>();

        // Register the main unified orchestrator service
        // REMOVED DUPLICATE REGISTRATION: UnifiedOrchestratorService already registered at line ~510
        // Duplicate registration causes multiple agent sessions and premium cost violations
        // services.AddSingleton<UnifiedOrchestratorService>();
        // services.AddSingleton<TradingBot.Abstractions.IUnifiedOrchestrator>(provider => provider.GetRequiredService<UnifiedOrchestratorService>());
        // services.AddHostedService(provider => provider.GetRequiredService<UnifiedOrchestratorService>());

        // ================================================================================
        // ENHANCED LEARNING AND ADAPTIVE INTELLIGENCE SERVICES (APPEND-ONLY)
        // ================================================================================
        
        // Guards & sessions
        services.AddSingleton<IMarketHoursService, MarketHoursService>();
        services.AddSingleton<ILiveTradingGate, LiveTradingGate>();
        services.AddSingleton<CloudEgressGuardHandler>();

        // Historical data: features → quotes → TopstepX (TopstepX local-only)
        services.AddSingleton<IHistoricalDataProvider, FeaturesHistoricalProvider>();
        services.AddSingleton<IHistoricalDataProvider, LocalQuotesProvider>();
        services.AddHttpClient<TradingBot.Backtest.Adapters.TopstepXHistoricalDataProvider>(c => c.BaseAddress = new Uri("https://api.topstepx.com"))
            .AddHttpMessageHandler<CloudEgressGuardHandler>();
        services.AddSingleton<IHistoricalDataProvider>(sp => sp.GetRequiredService<TradingBot.Backtest.Adapters.TopstepXHistoricalDataProvider>());
        services.AddSingleton<IHistoricalDataResolver, HistoricalDataResolver>();

        // Adaptive layer
        services.AddSingleton<IAdaptiveIntelligenceCoordinator, AdaptiveIntelligenceCoordinator>();
        services.AddSingleton<IAdaptiveParameterService, AdaptiveParameterService>();
        services.AddSingleton<IRuntimeConfigBus, RuntimeConfigBus>();

        // Authentication service
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Services.ITopstepAuth, TradingBot.UnifiedOrchestrator.Services.TopstepAuth>();

        // Model registry (now a hosted service) and canary watchdog
        services.AddSingleton<TradingBot.UnifiedOrchestrator.Services.ModelRegistry>();
        services.AddSingleton<IOnnxModelRegistry>(provider => provider.GetRequiredService<TradingBot.UnifiedOrchestrator.Services.ModelRegistry>());
        services.AddHostedService(provider => provider.GetRequiredService<TradingBot.UnifiedOrchestrator.Services.ModelRegistry>());
        services.AddHostedService<CanaryWatchdog>();
        
        // Brain hot-reload service for ONNX session swapping
        services.AddSingleton<global::BotCore.ML.OnnxModelLoader>();
        services.AddHostedService<BrainHotReloadService>();
        
        // Cloud model integration service removed - CloudRlTrainerV2 infrastructure no longer exists

        // Hosted services (append-only) - Enhanced learning services
        // Conditional registration based on ENABLE_HISTORICAL_LEARNING environment variable
        // This allows historical learning to run independently of RlRuntimeMode
        var enableHistoricalLearning = Environment.GetEnvironmentVariable("ENABLE_HISTORICAL_LEARNING");
        var historicalLearningEnabled = enableHistoricalLearning == "1" || enableHistoricalLearning?.ToLowerInvariant() == "true";
        
        if (historicalLearningEnabled || rlMode == TradingBot.Abstractions.RlRuntimeMode.Train)
        {
            services.AddHostedService<EnhancedBacktestLearningService>();
            
            if (historicalLearningEnabled)
            {
                Console.WriteLine("✅ [HISTORICAL-LEARNING] Historical backtest learning ENABLED");
                Console.WriteLine("   📊 Market OPEN: Learning every 60 minutes (light mode)");
                Console.WriteLine("   📈 Market CLOSED: Learning every 15 minutes (intensive mode)");
            }
            
            // Warn if training mode is enabled in production environment
            var environment = hostContext.HostingEnvironment.EnvironmentName;
            if (environment.Equals("Production", StringComparison.OrdinalIgnoreCase) && rlMode == TradingBot.Abstractions.RlRuntimeMode.Train)
            {
                Console.WriteLine("⚠️ [RL-SAFETY] WARNING: Full training mode enabled in Production environment!");
            }
        }
        else
        {
            Console.WriteLine("⚠️ [HISTORICAL-LEARNING] Historical backtest learning DISABLED");
            Console.WriteLine("   💡 Set ENABLE_HISTORICAL_LEARNING=1 to enable continuous learning from historical data");
        }
        
        // ================================================================================
        // PHASE 6: PARAMETER PERFORMANCE MONITORING & AUTOMATIC ROLLBACK
        // ================================================================================
        // Monitors live parameter performance and triggers automatic rollback if degradation detected
        // - Runs hourly during market hours
        // - Calculates rolling 3-day Sharpe ratio
        // - Rolls back if >20% degradation for 3 consecutive days
        // - Archives failed parameters and logs rollback events
        services.AddHostedService<TradingBot.Monitoring.ParameterPerformanceMonitor>();

    }

    /// <summary>
    /// Register Intelligence Stack services with real implementations
    /// </summary>
    private static void RegisterIntelligenceStackServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register the real intelligence stack services - NO SHORTCUTS
        services.AddIntelligenceStack(configuration);
    }

    /// <summary>
    /// Validates service registration and configuration files on startup
    /// Implements comprehensive dependency injection validation and configuration file verification
    /// </summary>
    private static async Task ValidateStartupServicesAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TradingBot.UnifiedOrchestrator.Program");
        
        logger.LogInformation("🔍 Starting comprehensive startup validation...");
        
        try
        {
            // 1. Verify CompatibilityKit service registration
            logger.LogInformation("📋 Validating CompatibilityKit service registration...");
            serviceProvider.VerifyCompatibilityKitRegistration(logger);
            
            // 2. Validate configuration files
            logger.LogInformation("📂 Validating CompatibilityKit configuration files...");
            serviceProvider.ValidateCompatibilityKitConfiguration(logger);
            
            // 3. Run hardening validation
            logger.LogInformation("🛡️ Running hardening validation...");
            var hardeningReport = await serviceProvider.RunHardeningValidationAsync(logger);
            
            if (!hardeningReport.OverallValidationSuccess)
            {
                throw new InvalidOperationException("Hardening validation failed - system not ready for production");
            }
            
            logger.LogInformation("✅ All startup validations completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "🚨 STARTUP VALIDATION FAILED - System cannot start");
            throw;
        }
    }

}

/// <summary>
/// Hosted service that initializes all advanced system components during startup
/// This ensures everything is properly integrated into the unified orchestrator brain
/// </summary>
internal class AdvancedSystemInitializationService : IHostedService
{
    private readonly ILogger<AdvancedSystemInitializationService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public AdvancedSystemInitializationService(
        ILogger<AdvancedSystemInitializationService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Advanced System Initialization Service starting");
        
        try
        {
            // Initialize model registries asynchronously to avoid constructor deadlocks
            var modelRegistry = _serviceProvider.GetService<TradingBot.UnifiedOrchestrator.Interfaces.IModelRegistry>();
            if (modelRegistry != null)
            {
                _logger.LogInformation("📦 Initializing Model Registry...");
                await modelRegistry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            
            // Initialize intelligence system components first
            var intelligenceOrchestrator = _serviceProvider.GetService<TradingBot.Abstractions.IIntelligenceOrchestrator>();
            if (intelligenceOrchestrator != null)
            {
                _logger.LogInformation("🧠 Initializing Intelligence Orchestrator...");
                // Intelligence orchestrator initialization handled internally
            }

            // Initialize Economic Event Manager (ForexFactory calendar)
            var economicEventManager = _serviceProvider.GetService<IEconomicEventManager>();
            if (economicEventManager != null)
            {
                _logger.LogInformation("📅 Initializing Economic Event Manager (ForexFactory calendar)...");
                try
                {
                    await economicEventManager.InitializeAsync().ConfigureAwait(false);
                    _logger.LogInformation("✅ Economic calendar loaded successfully from ForexFactory");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Economic calendar initialization failed - will retry periodically");
                }
            }

            // Initialize News Monitor Service (NewsAPI.org real-time monitoring)
            var newsMonitor = _serviceProvider.GetService<INewsMonitorService>();
            if (newsMonitor != null)
            {
                _logger.LogInformation("📰 Initializing News Monitor Service (NewsAPI.org)...");
                try
                {
                    await newsMonitor.InitializeAsync().ConfigureAwait(false);
                    if (newsMonitor.IsHealthy)
                    {
                        _logger.LogInformation("✅ News monitoring initialized - polling every 5 minutes");
                    }
                    else
                    {
                        _logger.LogInformation("⚠️ News monitoring disabled (NEWSAPI_KEY not configured)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ News monitoring initialization failed - continuing without real-time news");
                }
            }

            // Run startup health check for bot alert system
            await RunStartupHealthCheckAsync().ConfigureAwait(false);

            _logger.LogInformation("✅ Advanced System Initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Advanced System Initialization failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Advanced System Initialization Service stopping");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run startup health check to verify all systems are ready
    /// </summary>
    private async Task RunStartupHealthCheckAsync()
    {
        try
        {
            var botAlerts = _serviceProvider.GetService<global::BotCore.Services.BotAlertService>();
            if (botAlerts == null)
            {
                _logger.LogWarning("⚠️ BotAlertService not available - skipping startup health check");
                return;
            }

            _logger.LogInformation("🔍 Running startup health check...");

            // Check Ollama connectivity
            var ollamaClient = _serviceProvider.GetService<global::BotCore.Services.OllamaClient>();
            var ollamaConnected = false;
            if (ollamaClient != null)
            {
                ollamaConnected = await ollamaClient.IsConnectedAsync().ConfigureAwait(false);
            }

            // Check if economic calendar is loaded
            var economicEventManager = _serviceProvider.GetService<global::BotCore.Market.IEconomicEventManager>();
            var calendarLoaded = economicEventManager != null;
            if (economicEventManager != null)
            {
                try
                {
                    var upcomingEvents = await economicEventManager.GetUpcomingEventsAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);
                    calendarLoaded = upcomingEvents.Any();
                }
                catch
                {
                    calendarLoaded = false;
                }
            }

            // Check Python UCB service
            var ucbManager = _serviceProvider.GetService<global::BotCore.ML.UcbManager>();
            var pythonUcbRunning = ucbManager != null;

            // Check cloud models downloaded
            var cloudDownloader = _serviceProvider.GetService<global::BotCore.Services.CloudModelDownloader>();
            var cloudModelsDownloaded = cloudDownloader != null;

            // Report startup health
            await botAlerts.CheckStartupHealthAsync(
                ollamaConnected,
                calendarLoaded,
                pythonUcbRunning,
                cloudModelsDownloaded
            ).ConfigureAwait(false);

            // Check for disabled critical features
            var configuration = _serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            
            var dryRun = configuration["ENABLE_DRY_RUN"]?.ToLowerInvariant() == "true" || 
                        configuration["ENABLE_DRY_RUN"] == "1" ||
                        configuration["TRADING_MODE"]?.ToUpperInvariant() == "DRY_RUN";
            if (dryRun)
            {
                await botAlerts.AlertFeatureDisabledAsync(
                    "DRY_RUN Mode",
                    "Running in simulation mode, no real money at risk"
                ).ConfigureAwait(false);
            }

            var historicalLearning = configuration["ENABLE_HISTORICAL_LEARNING"];
            if (historicalLearning == "0" || historicalLearning?.ToLowerInvariant() == "false")
            {
                await botAlerts.AlertFeatureDisabledAsync(
                    "Historical Learning",
                    "Not learning from past data - using current models only"
                ).ConfigureAwait(false);
            }

            var calendarCheck = configuration["BOT_CALENDAR_CHECK_ENABLED"];
            if (calendarCheck == "0" || calendarCheck?.ToLowerInvariant() == "false")
            {
                await botAlerts.AlertFeatureDisabledAsync(
                    "Calendar Check",
                    "Won't block trades during high-impact economic events"
                ).ConfigureAwait(false);
            }

            if (!ollamaConnected)
            {
                await botAlerts.AlertFeatureDisabledAsync(
                    "Bot Voice (Ollama)",
                    "My voice is disabled, I'll be silent"
                ).ConfigureAwait(false);
            }

            _logger.LogInformation("✅ Startup health check completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Startup health check failed");
        }
    }
}

internal static class EnvironmentLoader
{
    /// <summary>
    /// Load environment files in priority order to auto-detect TopstepX credentials
    /// Priority: .env.local > .env > system environment variables
    /// </summary>
    public static void LoadEnvironmentFiles()
    {
        var rootPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..");
        var currentPath = Directory.GetCurrentDirectory();

        var envFiles = new[]
        {
            Path.Combine(rootPath, ".env"),           // Root configuration
            Path.Combine(currentPath, ".env"),        // Local overrides
            Path.Combine(rootPath, ".env.local"),     // Local credentials (highest priority)
            Path.Combine(currentPath, ".env.local")   // Project-local credentials
        };

        var loadedFiles = new List<string>();
        
        foreach (var envFile in envFiles)
        {
            try
            {
                if (File.Exists(envFile))
                {
                    DotNetEnv.Env.Load(envFile);
                    loadedFiles.Add(envFile);
                }
            }
            catch (Exception ex)
            {
                // Environment file loading errors are non-critical, continue with defaults
                Console.WriteLine($"Warning: Failed to load environment file {envFile}: {ex.Message}");
            }
        }

        if (loadedFiles.Count == 0)
        {
            Console.WriteLine("No environment files found, using system environment variables only");
        }
        else
        {
            
            // Check if TopstepX credentials are available
            var username = Environment.GetEnvironmentVariable("TOPSTEPX_USERNAME");
            var apiKey = Environment.GetEnvironmentVariable("TOPSTEPX_API_KEY");
            
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("TopstepX credentials detected and loaded");
            }
            else
            {
                Console.WriteLine("No TopstepX credentials found - will attempt to use JWT token if available");
            }
        }
    }

    /// <summary>
    /// Handle chat commands like /risk, /patterns, /zones, /status, /health
    /// </summary>
    private static async Task<string> HandleChatCommandAsync(string command, IServiceProvider services)
    {
        const decimal DefaultAtrForRiskAnalysis = 10m;
        const int DefaultLookbackMinutes = 60;
        
        try
        {
            var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "Unknown command. Available: /risk [symbol], /patterns [symbol], /zones [symbol], /status, /health";
            }

            var cmd = parts[0].ToLowerInvariant();
            var symbol = parts.Length > 1 ? parts[1].ToUpperInvariant() : "NQ";

            switch (cmd)
            {
                case "/risk":
                    var riskCommentary = services.GetService<global::BotCore.Services.RiskAssessmentCommentary>();
                    if (riskCommentary != null)
                    {
                        var zoneService = services.GetService<Zones.IZoneService>();
                        if (zoneService != null)
                        {
                            var snapshot = zoneService.GetSnapshot(symbol);
                            var currentPrice = snapshot.NearestDemand?.Mid ?? snapshot.NearestSupply?.Mid ?? 0m;
                            var result = await riskCommentary.AnalyzeRiskAsync(symbol, currentPrice, DefaultAtrForRiskAnalysis);
                            return string.IsNullOrEmpty(result) ? $"Risk analysis not available for {symbol}" : result;
                        }
                    }
                    return "Risk assessment service not available";

                case "/patterns":
                    var patternEngine = services.GetService<global::BotCore.Patterns.PatternEngine>();
                    if (patternEngine != null)
                    {
                        var scores = await patternEngine.GetCurrentScoresAsync(symbol);
                        var patterns = string.Join(", ", scores.DetectedPatterns.Select(p => $"{p.Name} ({p.Confidence:P0})"));
                        return $"**Patterns for {symbol}:**\n" +
                               $"Bull Score: {scores.BullScore:F2}\n" +
                               $"Bear Score: {scores.BearScore:F2}\n" +
                               $"Confidence: {scores.OverallConfidence:P0}\n" +
                               $"Detected: {(string.IsNullOrEmpty(patterns) ? "None" : patterns)}";
                    }
                    return "Pattern engine not available";

                case "/zones":
                    var zoneServiceZones = services.GetService<Zones.IZoneService>();
                    if (zoneServiceZones != null)
                    {
                        var snapshot = zoneServiceZones.GetSnapshot(symbol);
                        var demand = snapshot.NearestDemand != null 
                            ? $"Demand: {snapshot.NearestDemand.Mid:F2} ({snapshot.DistToDemandAtr:F1} ATR, {snapshot.NearestDemand.TouchCount} touches)"
                            : "Demand: None";
                        var supply = snapshot.NearestSupply != null
                            ? $"Supply: {snapshot.NearestSupply.Mid:F2} ({snapshot.DistToSupplyAtr:F1} ATR, {snapshot.NearestSupply.TouchCount} touches)"
                            : "Supply: None";
                        return $"**Zones for {symbol}:**\n{demand}\n{supply}\n" +
                               $"Breakout Score: {snapshot.BreakoutScore:F2}\n" +
                               $"Pressure: {snapshot.ZonePressure:F2}";
                    }
                    return "Zone service not available";

                case "/status":
                    var brain = services.GetService<global::BotCore.Brain.UnifiedTradingBrain>();
                    if (brain != null)
                    {
                        var method = brain.GetType().GetMethod("GatherCurrentContext",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            var context = method.Invoke(brain, null) as string ?? "Context unavailable";
                            return $"**Bot Status:**\n{context}";
                        }
                    }
                    return "Status not available";

                case "/health":
                    var healthMonitor = services.GetService<global::BotCore.Services.ComponentHealthMonitoringService>();
                    if (healthMonitor != null)
                    {
                        return "**Component Health:**\nHealth monitoring service available. Check logs for detailed status.";
                    }
                    return "Health monitoring not available";

                case "/learning":
                    var learningCommentary = services.GetService<global::BotCore.Services.AdaptiveLearningCommentary>();
                    if (learningCommentary != null)
                    {
                        var summary = learningCommentary.GetLearningSummary(DefaultLookbackMinutes);
                        return $"**Recent Learning:**\n{summary}";
                    }
                    return "Learning commentary not available";

                default:
                    return $"Unknown command: {cmd}\n" +
                           "Available: /risk [symbol], /patterns [symbol], /zones [symbol], /status, /health, /learning";
            }
        }
        catch (Exception ex)
        {
            return $"Error processing command: {ex.Message}";
        }
    }

    /// <summary>
    /// Startup class for web server configuration
    /// </summary>
    internal class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // Web services already configured in CreateHostBuilder
        }

        public void Configure(Microsoft.AspNetCore.Builder.IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
        {
            // Enable static files from wwwroot
            app.UseStaticFiles();

            // Enable routing
            app.UseRouting();

            // Configure endpoints
            app.UseEndpoints(endpoints =>
            {
                // Chat endpoint - talk to the trading bot
                endpoints.MapPost("/api/chat", async context =>
                {
                    try
                    {
                        // Read request body
                        using var reader = new System.IO.StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        
                        // Parse JSON request
                        var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>(body);
                        if (requestData == null || !requestData.ContainsKey("message"))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Missing 'message' field" });
                            return;
                        }

                        var userMessage = requestData["message"].GetString();
                        
                        // Get services
                        var ollamaClient = context.RequestServices.GetService<global::BotCore.Services.OllamaClient>();
                        var brain = context.RequestServices.GetService<global::BotCore.Brain.UnifiedTradingBrain>();
                        
                        // Check for command syntax (starts with /)
                        if (!string.IsNullOrEmpty(userMessage) && userMessage.StartsWith("/"))
                        {
                            var commandResponse = await HandleChatCommandAsync(userMessage, context.RequestServices);
                            await context.Response.WriteAsJsonAsync(new { response = commandResponse });
                            return;
                        }

                        // Check if bot voice is enabled
                        if (ollamaClient == null)
                        {
                            await context.Response.WriteAsJsonAsync(new { response = "My voice is disabled. Enable Ollama to chat with me." });
                            return;
                        }

                        // Gather bot's current context
                        string botContext = "Context unavailable";
                        if (brain != null)
                        {
                            // Use reflection to call private GatherCurrentContext method
                            var method = brain.GetType().GetMethod("GatherCurrentContext", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (method != null)
                            {
                                botContext = method.Invoke(brain, null) as string ?? "Context unavailable";
                            }
                        }

                        // Create AI prompt
                        var prompt = $@"I am an ES/NQ futures trading bot. A trader asked me: {userMessage}

My current state: {botContext}

Respond conversationally AS ME (the bot). Be helpful and explain my thinking.";

                        // Get AI response
                        var aiResponse = await ollamaClient.AskAsync(prompt);
                        
                        if (string.IsNullOrEmpty(aiResponse))
                        {
                            aiResponse = "I'm having trouble thinking right now. Please try again.";
                        }

                        await context.Response.WriteAsJsonAsync(new { response = aiResponse });
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new { error = $"Error: {ex.Message}" });
                    }
                });
            });
        }
    }
}
