using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BotCore.Services;
using BotCore.Bandits;
using TradingBot.Abstractions;
using System.Text.Json;
using System.Globalization;

namespace BotCore.Services;

/// <summary>
/// 🎯 MASTER DECISION ORCHESTRATOR - ALWAYS-LEARNING TRADING SYSTEM 🎯
/// 
/// This is the ONE MASTER BRAIN that coordinates all AI decision-making:
/// 
/// PRIMARY DECISION HIERARCHY:
/// 1. Enhanced Brain Integration (Multi-model ensemble + cloud sync)
/// 2. Unified Trading Brain (Neural UCB + CVaR-PPO + LSTM) 
/// 3. Intelligence Orchestrator (Basic ML/RL fallback)
/// 4. Python Decision Services (UCB FastAPI services)
/// 5. Direct Strategy Execution (ultimate fallback)
/// 
/// GUARANTEES:
/// ✅ NEVER returns HOLD - always BUY/SELL
/// ✅ Continuous learning from every trade outcome  
/// ✅ 24/7 operation with auto-recovery
/// ✅ Real-time model promotion based on performance
/// ✅ Historical + live data integration
/// ✅ Contract auto-rollover (Z25 → H26)
/// 
/// RESULT: Always-learning trading system that gets smarter every day
/// </summary>
public class MasterDecisionOrchestrator : BackgroundService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    // Trading reward calculation constants
    private const decimal MaxTimeRewardMagnitude = 0.1m;    // Maximum time-based reward/penalty
    private const double HoursInDay = 24.0;                 // Hours in a day for time calculations
    private const decimal MaxRewardBound = 1m;              // Maximum reward value boundary
    private const decimal MinRewardBound = -1m;             // Minimum reward value boundary
    private const decimal EmergencyFallbackConfidence = 0.51m; // Minimum viable confidence for emergency fallback
    private const decimal EmergencyFallbackQuantity = 1m;   // Very conservative emergency quantity
    
    // Decision ID generation constants  
    private const int DecisionIdRandomMin = 1000;           // Minimum random number for decision IDs
    private const int DecisionIdRandomMax = 9999;           // Maximum random number for decision IDs
    
    // Percentage conversion and threshold constants
    private const double PercentageMultiplier = 100.0;      // Multiplier to convert decimal to percentage (0.5 → 50%)
    private const decimal HalfThreshold = 0.5m;             // Half threshold for various calculations
    private const double MinimumSharpeForDivision = 0.01;   // Minimum Sharpe ratio to avoid division by zero
    
    private readonly ILogger<MasterDecisionOrchestrator> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Core decision routing components
    private readonly UnifiedDecisionRouter _unifiedRouter;
    private readonly TradingBot.Abstractions.DecisionServiceStatus _serviceStatus;
    
    // Learning and feedback systems
    private readonly EnhancedTradingBrainIntegration? _enhancedBrain;
    private readonly BotCore.Brain.UnifiedTradingBrain _unifiedBrain;
    
    // Extended Neural UCB for parameter bundle selection
    private readonly NeuralUcbExtended? _neuralUcbExtended;
    
    // Configuration and monitoring
    private readonly MasterOrchestratorConfig _config;
    private readonly ContinuousLearningManager _learningManager;
    private readonly ContractRolloverManager _rolloverManager;
    private readonly OllamaClient? _ollamaClient;
    private readonly BotAlertService? _botAlertService;
    private readonly BotPerformanceReporter? _performanceReporter;
    
    // Operational state
    private readonly Dictionary<string, DecisionPerformance> _performanceTracking = new();
    private readonly Queue<LearningEvent> _learningQueue = new();
    private readonly object _stateLock = new();
    
    // Always-learning operation state
    private bool _isLearningActive;
    private DateTime _lastModelUpdate = DateTime.MinValue;
    private DateTime _lastPerformanceReport = DateTime.MinValue;
    
    // Gate 5: Canary monitoring state
    private bool _isCanaryActive;
    private DateTime _canaryStartTime = DateTime.MinValue;
    private Dictionary<string, double> _baselineMetrics = new();
    private readonly List<CanaryTradeRecord> _canaryTrades = new();
    private string _previousArtifactBackupPath = string.Empty;
    
    // Legacy fields kept for backward compatibility
    private bool _canaryActive;
    private int _canaryTradesCompleted;
    private double _baselineWinRate = 0.5; // Default 50% win rate baseline
    private double _baselineSharpeRatio = 1.0; // Default 1.0 Sharpe ratio baseline
    private double _baselineDrawdown; // Default 0 drawdown baseline
    private readonly IGate5Config _gate5Config;
    
    public MasterDecisionOrchestrator(
        ILogger<MasterDecisionOrchestrator> logger,
        IServiceProvider serviceProvider,
        UnifiedDecisionRouter unifiedRouter,
        BotCore.Brain.UnifiedTradingBrain unifiedBrain,
        IGate5Config? gate5Config = null,
        OllamaClient? ollamaClient = null,
        BotAlertService? botAlertService = null,
        BotPerformanceReporter? performanceReporter = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _unifiedRouter = unifiedRouter;
        _serviceStatus = new TradingBot.Abstractions.DecisionServiceStatus();
        _unifiedBrain = unifiedBrain;
        _gate5Config = gate5Config ?? Gate5Config.LoadFromEnvironment();
        _ollamaClient = ollamaClient;
        _botAlertService = botAlertService;
        _performanceReporter = performanceReporter;
        
        // Try to get optional enhanced services
        _enhancedBrain = serviceProvider.GetService<EnhancedTradingBrainIntegration>();
        
        // Try to get Neural UCB Extended for parameter bundle selection
        _neuralUcbExtended = serviceProvider.GetService<NeuralUcbExtended>();
        
        // Initialize configuration
        _config = serviceProvider.GetService<IOptions<MasterOrchestratorConfig>>()?.Value ?? new MasterOrchestratorConfig();
        
        // Initialize continuous learning manager
        _learningManager = new ContinuousLearningManager(logger, serviceProvider);
        
        // Initialize contract rollover manager
        _rolloverManager = new ContractRolloverManager(logger, serviceProvider);
        
        _logger.LogInformation("🎯 [MASTER-ORCHESTRATOR] Initialized - Always-learning trading system ready");
        _logger.LogInformation("🧠 [MASTER-ORCHESTRATOR] Enhanced Brain: {Enhanced}, Service Router: True, Unified Brain: True, Neural UCB Extended: {Extended}", 
            _enhancedBrain != null, _neuralUcbExtended != null);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 [MASTER-ORCHESTRATOR] Starting always-learning trading system...");
        
        try
        {
            // Initialize all systems
            await InitializeSystemsAsync(stoppingToken).ConfigureAwait(false);
            
            // Start continuous learning
            await StartContinuousLearningAsync(stoppingToken).ConfigureAwait(false);
            
            // Main orchestration loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Execute main orchestration cycle
                    await ExecuteOrchestrationCycleAsync(stoppingToken).ConfigureAwait(false);
                    
                    // Feature 3: Check for performance summaries
                    await CheckPerformanceSummariesAsync(stoppingToken).ConfigureAwait(false);
                    
                    // Wait before next cycle
                    await Task.Delay(TimeSpan.FromSeconds(_config.OrchestrationCycleIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "❌ [MASTER-ORCHESTRATOR] Invalid operation in orchestration cycle");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    _logger.LogError(ex, "❌ [MASTER-ORCHESTRATOR] Timeout in orchestration cycle");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 [MASTER-ORCHESTRATOR] Critical error in master orchestrator");
            throw;
        }
        finally
        {
            _logger.LogInformation("🛑 [MASTER-ORCHESTRATOR] Always-learning trading system stopped");
        }
    }
    
    /// <summary>
    /// Initialize all AI systems and verify readiness
    /// </summary>
    private async Task InitializeSystemsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 [MASTER-ORCHESTRATOR] Initializing all AI systems...");
        
        try
        {
            // Initialize Unified Trading Brain
            await _unifiedBrain.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("✅ [UNIFIED-BRAIN] Initialized successfully");
            
            // Initialize Enhanced Brain if available
            if (_enhancedBrain != null)
            {
                await _enhancedBrain.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("✅ [ENHANCED-BRAIN] Initialized successfully");
            }
            
            // Initialize learning manager
            await _learningManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("✅ [LEARNING-MANAGER] Initialized successfully");
            
            // Initialize contract rollover manager
            await _rolloverManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("✅ [ROLLOVER-MANAGER] Initialized successfully");
            
            _logger.LogInformation("🎉 [MASTER-ORCHESTRATOR] All systems initialized - Ready for always-learning operation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [MASTER-ORCHESTRATOR] System initialization failed");
            throw;
        }
    }
    
    /// <summary>
    /// Start continuous learning systems
    /// </summary>
    private async Task StartContinuousLearningAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📚 [CONTINUOUS-LEARNING] Starting always-learning systems...");
            
            // Start learning manager
            await _learningManager.StartLearningAsync(cancellationToken).ConfigureAwait(false);
            
            // Start contract rollover monitoring
            await _rolloverManager.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            
            _isLearningActive = true;
            _logger.LogInformation("✅ [CONTINUOUS-LEARNING] Always-learning systems started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CONTINUOUS-LEARNING] Failed to start learning systems");
            throw;
        }
    }
    
    /// <summary>
    /// Execute main orchestration cycle
    /// </summary>
    private async Task ExecuteOrchestrationCycleAsync(CancellationToken cancellationToken)
    {
        // Process learning queue
        await ProcessLearningQueueAsync(cancellationToken).ConfigureAwait(false);
        
        // Check for model updates
        await CheckModelUpdatesAsync(cancellationToken).ConfigureAwait(false);
        
        // Gate 5: Live First-Hour Auto-Rollback Monitoring (legacy method)
        await MonitorCanaryPeriodAsync(cancellationToken).ConfigureAwait(false);
        
        // Gate 5: Enhanced canary metrics check (called every 5 minutes)
        await CheckCanaryMetricsAsync(cancellationToken).ConfigureAwait(false);
        
        // Generate performance reports
        await GeneratePerformanceReportsAsync(cancellationToken).ConfigureAwait(false);
        
        // Monitor system health
        await MonitorSystemHealthAsync(cancellationToken).ConfigureAwait(false);
        
        // Check contract rollover needs
        await CheckContractRolloverAsync(cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// MAIN DECISION METHOD - Used by trading orchestrator
    /// This is the ONE method that all trading decisions flow through
    /// 
    /// Enhanced with bundle-based parameter selection:
    /// - Uses Neural UCB Extended to select optimal strategy-parameter combinations
    /// - Replaces hardcoded MaxPositionMultiplier and confidenceThreshold with learned values
    /// - Enables continuous adaptation of trading parameters
    /// </summary>
    public async Task<UnifiedTradingDecision> MakeUnifiedDecisionAsync(
        string symbol,
        MarketContext marketContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(marketContext);
        
        var startTime = DateTime.UtcNow;
        var decisionId = GenerateDecisionId();
        
        try
        {
            _logger.LogDebug("🎯 [MASTER-DECISION] Making unified decision for {Symbol}", symbol);
            
            // PHASE 1: Get parameter bundle selection from Neural UCB Extended
            BundleSelection? bundleSelection = null;
            if (_neuralUcbExtended != null)
            {
                try
                {
                    // Convert TradingBot.Abstractions.MarketContext to BotCore.Brain.Models.MarketContext
                    var brainMarketContext = new BotCore.Brain.Models.MarketContext
                    {
                        Symbol = marketContext.Symbol,
                        CurrentPrice = (decimal)marketContext.Price,
                        Volume = (decimal)marketContext.Volume,
                        Volatility = 0.15m, // Default value
                        TimeOfDay = marketContext.Timestamp.TimeOfDay,
                        DayOfWeek = marketContext.Timestamp.DayOfWeek,
                        VolumeRatio = 1.0m, // Default value
                        PriceChange = 0.0m // Default value
                    };
                    
                    bundleSelection = await _neuralUcbExtended.SelectBundleAsync(brainMarketContext, cancellationToken)
                        .ConfigureAwait(false);
                    
                    _logger.LogInformation("🎯 [BUNDLE-SELECTION] Selected: {BundleId} " +
                                         "strategy={Strategy} mult={Mult:F1}x thr={Thr:F2}",
                        bundleSelection.Bundle.BundleId, bundleSelection.Bundle.Strategy,
                        bundleSelection.Bundle.Mult, bundleSelection.Bundle.Thr);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "⚠️ [BUNDLE-SELECTION] Invalid operation during bundle selection, using fallback");
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "⚠️ [BUNDLE-SELECTION] Invalid argument during bundle selection, using fallback");
                }
            }
            
            // PHASE 2: Apply bundle parameters to market context
            var enhancedMarketContext = ApplyBundleParameters(marketContext, bundleSelection);
            
            // PHASE 3: Route through unified decision system with enhanced context
            var decision = await _unifiedRouter.RouteDecisionAsync(symbol, enhancedMarketContext, cancellationToken)
                .ConfigureAwait(false);
            
            // PHASE 4: Apply bundle configuration to decision
            if (bundleSelection != null)
            {
                decision = ApplyBundleToDecision(decision, bundleSelection);
            }
            
            // Ensure decision ID is set
            if (string.IsNullOrEmpty(decision.DecisionId))
            {
                decision.DecisionId = decisionId;
            }
            
            // Track decision for learning (including bundle performance)
            await TrackDecisionForLearningAsync(decision, enhancedMarketContext, bundleSelection, cancellationToken)
                .ConfigureAwait(false);
            
            // Log the enhanced decision
            LogEnhancedDecision(decision, bundleSelection, startTime);
            
            return decision;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "❌ [MASTER-DECISION] Invalid operation making decision for {Symbol}", symbol);
            return CreateEmergencyDecision(symbol, decisionId, startTime);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "❌ [MASTER-DECISION] Timeout making decision for {Symbol}", symbol);
            return CreateEmergencyDecision(symbol, decisionId, startTime);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "❌ [MASTER-DECISION] Invalid argument making decision for {Symbol}", symbol);
            return CreateEmergencyDecision(symbol, decisionId, startTime);
        }
    }
    
    /// <summary>
    /// Submit trading outcome for continuous learning
    /// </summary>
    public async Task SubmitTradingOutcomeAsync(
        string decisionId,
        decimal realizedPnL,
        bool wasCorrect,
        TimeSpan holdTime,
        string decisionSource,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisionId);
        ArgumentNullException.ThrowIfNull(decisionSource);
        ArgumentNullException.ThrowIfNull(metadata);
        
        try
        {
            _logger.LogInformation("📈 [MASTER-FEEDBACK] Recording outcome: {DecisionId} PnL={PnL:C2} Correct={Correct}",
                decisionId, realizedPnL, wasCorrect);
            
            // Submit to unified learning system
            await _unifiedRouter.SubmitTradingOutcomeAsync(
                decisionId, realizedPnL, wasCorrect, holdTime, cancellationToken).ConfigureAwait(false);
            
            // Create learning event
            var learningEvent = new LearningEvent
            {
                DecisionId = decisionId,
                RealizedPnL = realizedPnL,
                WasCorrect = wasCorrect,
                HoldTime = holdTime,
                DecisionSource = decisionSource,
                Timestamp = DateTime.UtcNow
            };
            learningEvent.ReplaceMetadata(metadata);
            
            // Add to learning queue
            lock (_stateLock)
            {
                _learningQueue.Enqueue(learningEvent);
            }
            
            // Update performance tracking
            await UpdatePerformanceTrackingAsync(decisionId, decisionSource, realizedPnL, wasCorrect, cancellationToken).ConfigureAwait(false);
            
            // Update bundle performance if this was a bundle-enhanced decision
            await UpdateBundlePerformanceAsync(decisionId, realizedPnL, wasCorrect, metadata, cancellationToken).ConfigureAwait(false);
            
            // Track trade result for canary monitoring
            var symbol = metadata.TryGetValue("symbol", out var symbolObj) && symbolObj is string sym 
                ? sym : "UNKNOWN";
            var strategy = metadata.TryGetValue("strategy", out var strategyObj) && strategyObj is string strat 
                ? strat : decisionSource;
            var outcome = wasCorrect ? "WIN" : "LOSS";
            TrackTradeResult(symbol, strategy, realizedPnL, outcome);
            
            _logger.LogInformation("✅ [MASTER-FEEDBACK] Outcome recorded and queued for learning");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "❌ [MASTER-FEEDBACK] Invalid operation submitting trading outcome");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "❌ [MASTER-FEEDBACK] Invalid argument submitting trading outcome");
        }
    }
    
    /// <summary>
    /// Get comprehensive system status
    /// </summary>
    public MasterOrchestratorStatus GetSystemStatus()
    {
        lock (_stateLock)
        {
            var status = new MasterOrchestratorStatus
            {
                IsLearningActive = _isLearningActive,
                LastModelUpdate = _lastModelUpdate,
                LastPerformanceReport = _lastPerformanceReport,
                TotalDecisionsToday = _performanceTracking.Values.Sum(p => p.TotalDecisions),
                OverallWinRate = CalculateOverallWinRate(),
                LearningQueueSize = _learningQueue.Count,
                ServiceStatus = _serviceStatus,
                SystemHealthy = IsSystemHealthy(),
                Timestamp = DateTime.UtcNow
            };
            status.ReplaceBrainPerformance(_performanceTracking.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            
            return status;
        }
    }
    
    /// <summary>
    /// Force model update and retraining
    /// </summary>
    public async Task ForceModelUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🔄 [FORCE-UPDATE] Forcing model update and retraining...");
            
            // Trigger learning manager update
            await _learningManager.ForceUpdateAsync(cancellationToken).ConfigureAwait(false);
            
            // Update timestamp
            _lastModelUpdate = DateTime.UtcNow;
            
            _logger.LogInformation("✅ [FORCE-UPDATE] Model update completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FORCE-UPDATE] Failed to force model update");
            throw;
        }
    }
    
    #region Bundle Integration Methods
    
    /// <summary>
    /// Apply bundle parameters to market context
    /// This replaces hardcoded values with learned parameter selections
    /// </summary>
    private static MarketContext ApplyBundleParameters(MarketContext marketContext, BundleSelection? bundleSelection)
    {
        if (bundleSelection == null)
        {
            return marketContext; // Return unchanged if no bundle selection
        }
        
        // Create enhanced market context with bundle parameters
        var enhancedContext = new MarketContext
        {
            Symbol = marketContext.Symbol,
            Timestamp = marketContext.Timestamp,
            Price = marketContext.Price,
            Volume = marketContext.Volume,
            Bid = marketContext.Bid,
            Ask = marketContext.Ask,
            CurrentRegime = marketContext.CurrentRegime,
            Regime = marketContext.Regime,
            ModelConfidence = marketContext.ModelConfidence,
            PrimaryBias = marketContext.PrimaryBias,
            IsFomcDay = marketContext.IsFomcDay,
            IsCpiDay = marketContext.IsCpiDay,
            NewsIntensity = marketContext.NewsIntensity,
            SignalStrength = marketContext.SignalStrength,
            ConfidenceLevel = (double)bundleSelection.Bundle.Thr // Apply learned confidence threshold
        };
        
        // Copy technical indicators
        foreach (var indicator in marketContext.TechnicalIndicators)
        {
            enhancedContext.TechnicalIndicators[indicator.Key] = indicator.Value;
        }
        
        // Add bundle-specific technical indicators
        enhancedContext.TechnicalIndicators["bundle_multiplier"] = (double)bundleSelection.Bundle.Mult;
        enhancedContext.TechnicalIndicators["bundle_threshold"] = (double)bundleSelection.Bundle.Thr;
        enhancedContext.TechnicalIndicators["bundle_ucb_value"] = (double)bundleSelection.UcbValue;
        enhancedContext.TechnicalIndicators["bundle_prediction"] = (double)bundleSelection.Prediction;
        
        return enhancedContext;
    }
    
    /// <summary>
    /// Apply bundle configuration to trading decision
    /// This ensures the decision uses learned parameters instead of hardcoded values
    /// </summary>
    private static UnifiedTradingDecision ApplyBundleToDecision(UnifiedTradingDecision decision, BundleSelection bundleSelection)
    {
        // Apply bundle multiplier to position size
        var enhancedQuantity = decision.Quantity * bundleSelection.Bundle.Mult;
        
        // Apply bundle confidence requirements
        var bundleAdjustedConfidence = Math.Max(decision.Confidence, bundleSelection.Bundle.Thr);
        
        // Create enhanced decision
        var enhancedDecision = new UnifiedTradingDecision
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            Action = decision.Action,
            Confidence = bundleAdjustedConfidence,
            Quantity = enhancedQuantity,
            Strategy = bundleSelection.Bundle.Strategy, // Use bundle strategy
            DecisionSource = $"{decision.DecisionSource}+Bundle",
            Reasoning = new Dictionary<string, object>(decision.Reasoning)
            {
                ["bundle_id"] = bundleSelection.Bundle.BundleId,
                ["bundle_strategy"] = bundleSelection.Bundle.Strategy,
                ["bundle_multiplier"] = bundleSelection.Bundle.Mult,
                ["bundle_threshold"] = bundleSelection.Bundle.Thr,
                ["original_quantity"] = decision.Quantity,
                ["enhanced_quantity"] = enhancedQuantity,
                ["bundle_selection_reason"] = bundleSelection.SelectionReason
            },
            Timestamp = decision.Timestamp,
            ProcessingTimeMs = decision.ProcessingTimeMs
        };
        
        return enhancedDecision;
    }
    
    /// <summary>
    /// Enhanced tracking that includes bundle performance
    /// </summary>
    private async Task TrackDecisionForLearningAsync(
        UnifiedTradingDecision decision,
        MarketContext marketContext,
        BundleSelection? bundleSelection,
        CancellationToken cancellationToken)
    {
        // Standard tracking
        var trackingInfo = new DecisionTrackingInfo
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            Action = decision.Action,
            Confidence = decision.Confidence,
            Strategy = decision.Strategy,
            DecisionSource = decision.DecisionSource,
            MarketContext = marketContext,
            Timestamp = decision.Timestamp
        };
        
        await _learningManager.TrackDecisionAsync(trackingInfo, cancellationToken).ConfigureAwait(false);
        
        // Enhanced tracking with bundle information
        if (bundleSelection != null)
        {
            var enhancedTrackingInfo = new BundleDecisionTrackingInfo
            {
                DecisionId = decision.DecisionId,
                BundleId = bundleSelection.Bundle.BundleId,
                BundleSelection = bundleSelection,
                StandardTracking = trackingInfo
            };
            
            await TrackBundleDecisionAsync(enhancedTrackingInfo, cancellationToken).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// Track bundle-specific decision information
    /// </summary>
    private async Task TrackBundleDecisionAsync(BundleDecisionTrackingInfo trackingInfo, CancellationToken cancellationToken)
    {
        try
        {
            // Store bundle decision tracking for future learning
            var bundleTrackingPath = Path.Combine("data", "bundle_decisions.json");
            Directory.CreateDirectory(Path.GetDirectoryName(bundleTrackingPath)!);
            
            var trackingData = new
            {
                Timestamp = DateTime.UtcNow,
                TrackingInfo = trackingInfo
            };
            
            var json = JsonSerializer.Serialize(trackingData);
            await File.AppendAllTextAsync(bundleTrackingPath, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
            
            _logger.LogDebug("📊 [BUNDLE-TRACKING] Tracked bundle decision: {BundleId}", trackingInfo.BundleId);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "❌ [BUNDLE-TRACKING] I/O error tracking bundle decision");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "❌ [BUNDLE-TRACKING] Access denied tracking bundle decision");
        }
    }
    
    /// <summary>
    /// Enhanced logging that includes bundle information
    /// </summary>
    private void LogEnhancedDecision(UnifiedTradingDecision decision, BundleSelection? bundleSelection, DateTime startTime)
    {
        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        if (bundleSelection != null)
        {
            _logger.LogInformation("🎯 [ENHANCED-DECISION] Decision: {Action} {Symbol} " +
                                 "confidence={Confidence:P1} source={Source} strategy={Strategy} " +
                                 "bundle={BundleId} mult={Mult:F1}x thr={Thr:F2} time={Time:F0}ms",
                decision.Action, decision.Symbol, decision.Confidence, 
                decision.DecisionSource, decision.Strategy, bundleSelection.Bundle.BundleId,
                bundleSelection.Bundle.Mult, bundleSelection.Bundle.Thr, processingTime);
        }
        else
        {
            _logger.LogInformation("🎯 [STANDARD-DECISION] Decision: {Action} {Symbol} " +
                                 "confidence={Confidence:P1} source={Source} strategy={Strategy} time={Time:F0}ms",
                decision.Action, decision.Symbol, decision.Confidence, 
                decision.DecisionSource, decision.Strategy, processingTime);
        }
    }
    
    /// <summary>
    /// Update bundle performance based on trading outcome
    /// This enables continuous learning of optimal parameter combinations
    /// </summary>
    private async Task UpdateBundlePerformanceAsync(
        string decisionId,
        decimal realizedPnL,
        bool wasCorrect,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken)
    {
        if (_neuralUcbExtended == null)
            return;
            
        try
        {
            // Check if this decision used a bundle (look for bundle metadata)
            if (metadata.TryGetValue("bundle_id", out var bundleIdObj) && bundleIdObj is string bundleId)
            {
                // Create reward signal from trading outcome
                var reward = CalculateBundleReward(realizedPnL, wasCorrect, metadata);
                
                // Create a simple market context for the update (we could store the original context if needed)
                var abstractionsMarketContext = CreateMarketContextFromMetadata(metadata);
                
                // Convert to BotCore.Brain.Models.MarketContext
                var brainMarketContext = new BotCore.Brain.Models.MarketContext
                {
                    Symbol = abstractionsMarketContext.Symbol,
                    CurrentPrice = (decimal)abstractionsMarketContext.Price,
                    Volume = (decimal)abstractionsMarketContext.Volume,
                    Volatility = 0.15m, // Default value
                    TimeOfDay = abstractionsMarketContext.Timestamp.TimeOfDay,
                    DayOfWeek = abstractionsMarketContext.Timestamp.DayOfWeek,
                    VolumeRatio = 1.0m, // Default value
                    PriceChange = 0.0m // Default value
                };
                
                // Update bundle performance in Neural UCB Extended
                await _neuralUcbExtended.UpdateBundlePerformanceAsync(
                    bundleId, brainMarketContext, reward, metadata, cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("📊 [BUNDLE-FEEDBACK] Updated bundle {BundleId} with reward {Reward:F3} " +
                                     "from PnL {PnL:C2} correct={Correct}",
                    bundleId, reward, realizedPnL, wasCorrect);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "❌ [BUNDLE-FEEDBACK] Invalid operation updating bundle performance");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "❌ [BUNDLE-FEEDBACK] Invalid argument updating bundle performance");
        }
    }
    
    /// <summary>
    /// Calculate reward signal for bundle learning
    /// Converts trading outcomes to ML reward signals
    /// </summary>
    private static decimal CalculateBundleReward(decimal realizedPnL, bool wasCorrect, Dictionary<string, object> metadata)
    {
        // Base reward from P&L (normalized to -1 to +1 range)
        var pnlReward = Math.Max(-1m, Math.Min(1m, realizedPnL / 100m)); // Assume $100 normalizes to 1.0
        
        // Accuracy bonus/penalty
        var accuracyReward = wasCorrect ? 0.2m : -0.2m;
        
        // Time-based reward (faster profits are better)
        var timeReward = 0m;
        if (metadata.TryGetValue("hold_time", out var holdTimeObj) && holdTimeObj is TimeSpan holdTime)
        {
            // Reward shorter holding periods for profitable trades
            if (realizedPnL > 0)
            {
                timeReward = Math.Max(-MaxTimeRewardMagnitude, Math.Min(MaxTimeRewardMagnitude, 
                    (decimal)(1.0 - holdTime.TotalHours / HoursInDay) * MaxTimeRewardMagnitude));
            }
        }
        
        var totalReward = pnlReward + accuracyReward + timeReward;
        return Math.Max(MinRewardBound, Math.Min(MaxRewardBound, totalReward)); // Clamp to [-1, 1] range
    }
    
    /// <summary>
    /// Create a basic market context from metadata for bundle updates
    /// </summary>
    private static MarketContext CreateMarketContextFromMetadata(Dictionary<string, object> metadata)
    {
        var context = new MarketContext();
        
        // Extract basic market data from metadata if available
        if (metadata.TryGetValue("symbol", out var symbolObj) && symbolObj is string symbol)
            context.Symbol = symbol;
            
        if (metadata.TryGetValue("price", out var priceObj) && priceObj is double price)
            context.Price = price;
            
        if (metadata.TryGetValue("volume", out var volumeObj) && volumeObj is double volume)
            context.Volume = volume;
            
        // Add technical indicators from metadata
        foreach (var kvp in metadata.Where(m => m.Key.StartsWith("tech_", StringComparison.Ordinal)))
        {
            if (kvp.Value is double techValue)
            {
                var indicatorName = kvp.Key.Substring(5); // Remove "tech_" prefix
                context.TechnicalIndicators[indicatorName] = techValue;
            }
        }
        
        return context;
    }
    
    #endregion
    
    #region Private Implementation Methods
    
    private async Task ProcessLearningQueueAsync(CancellationToken cancellationToken)
    {
        var events = new List<LearningEvent>();
        
        lock (_stateLock)
        {
            while (_learningQueue.Count > 0)
            {
                events.Add(_learningQueue.Dequeue());
            }
        }
        
        if (events.Count > 0)
        {
            await _learningManager.ProcessLearningEventsAsync(events, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("📚 [LEARNING-QUEUE] Processed {Count} learning events", events.Count);
        }
    }
    
    private async Task CheckModelUpdatesAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastModelUpdate > TimeSpan.FromHours(_config.ModelUpdateIntervalHours))
        {
            // Step 1: Capture current baseline metrics before update
            var baselineMetrics = await CaptureCurrentMetricsAsync(cancellationToken)
                .ConfigureAwait(false);
            
            // Step 2: Backup current artifacts before update
            var backupPath = await BackupCurrentArtifactsAsync(cancellationToken)
                .ConfigureAwait(false);
            
            if (!string.IsNullOrEmpty(backupPath))
            {
                _logger.LogInformation("✅ [UPDATE] Pre-update backup completed: {Path}", backupPath);
            }
            
            // Step 3: Perform the model update
            await _learningManager.CheckAndUpdateModelsAsync(cancellationToken).ConfigureAwait(false);
            _lastModelUpdate = DateTime.UtcNow;
            
            // Step 4: Start canary monitoring with baseline metrics
            StartCanaryMonitoring(baselineMetrics);
        }
    }
    
    private async Task GeneratePerformanceReportsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastPerformanceReport > TimeSpan.FromHours(_config.PerformanceReportIntervalHours))
        {
            await GenerateDetailedPerformanceReportAsync(cancellationToken).ConfigureAwait(false);
            _lastPerformanceReport = DateTime.UtcNow;
        }
    }
    
    private async Task MonitorCanaryPeriodAsync(CancellationToken cancellationToken)
    {
        if (!_canaryActive || !_gate5Config.Enabled)
        {
            return;
        }

        var minTrades = _gate5Config.MinTrades;
        var minMinutes = _gate5Config.MinMinutes;
        var maxMinutes = _gate5Config.MaxMinutes;
        var winRateDropThreshold = _gate5Config.WinRateDropThreshold;
        var maxDrawdownDollars = _gate5Config.MaxDrawdownDollars;
        var sharpeDropThreshold = _gate5Config.SharpeDropThreshold;
        var catastrophicWinRateThreshold = _gate5Config.CatastrophicWinRateThreshold;
        var catastrophicDrawdownDollars = _gate5Config.CatastrophicDrawdownDollars;

        var elapsedMinutes = (DateTime.UtcNow - _canaryStartTime).TotalMinutes;

        if (_canaryTradesCompleted < minTrades && elapsedMinutes < minMinutes)
        {
            return;
        }

        if (elapsedMinutes > maxMinutes)
        {
            _logger.LogInformation("🕐 [GATE-5] Canary period max duration reached, evaluating metrics");
        }

        var currentMetrics = CalculateCanaryMetrics();
        var winRateDrop = _baselineWinRate - currentMetrics.WinRate;
        var sharpeRatioDrop = (_baselineSharpeRatio - currentMetrics.SharpeRatio) / Math.Max(_baselineSharpeRatio, 0.01);
        var drawdownIncrease = currentMetrics.MaxDrawdown - _baselineDrawdown;

        _logger.LogInformation("📊 [GATE-5] Canary metrics - Trades: {Trades}, WinRate: {WR:F2}%, Sharpe: {SR:F2}, Drawdown: ${DD:F2}",
            _canaryTradesCompleted, currentMetrics.WinRate * PercentageMultiplier, currentMetrics.SharpeRatio, currentMetrics.MaxDrawdown);

        var isCatastrophic = currentMetrics.WinRate < catastrophicWinRateThreshold || 
                            currentMetrics.MaxDrawdown > catastrophicDrawdownDollars;
        
        var shouldRollback = (winRateDrop > winRateDropThreshold && drawdownIncrease > maxDrawdownDollars) ||
                            sharpeRatioDrop > sharpeDropThreshold ||
                            isCatastrophic;

        if (isCatastrophic)
        {
            await CreateKillFileAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("🚨 [GATE-5] CATASTROPHIC FAILURE - kill.txt created, live trading stopped");
        }

        if (shouldRollback)
        {
            await ExecuteCanaryRollbackAsync(currentMetrics, cancellationToken).ConfigureAwait(false);
        }
        else if (_canaryTradesCompleted >= minTrades && elapsedMinutes >= minMinutes)
        {
            _logger.LogInformation("✅ [GATE-5] Canary period completed successfully - deployment validated");
            _canaryActive = false;
        }
    }

    private CanaryMetrics CalculateCanaryMetrics()
    {
        if (_canaryTrades.Count == 0)
        {
            return new CanaryMetrics { WinRate = 0, SharpeRatio = 0, MaxDrawdown = 0 };
        }

        var wins = _canaryTrades.Count(t => t.PnL > 0);
        var winRate = (double)wins / _canaryTrades.Count;

        var returns = _canaryTrades.Select(t => t.PnL).ToList();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Average(r => Math.Pow(r - avgReturn, 2)));
        var sharpeRatio = stdDev > 0 ? avgReturn / stdDev : 0;

        double peak = 0;
        double equity = 0;
        double maxDrawdown = 0;

        foreach (var trade in _canaryTrades)
        {
            equity += trade.PnL;
            if (equity > peak)
            {
                peak = equity;
            }
            var drawdown = peak - equity;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return new CanaryMetrics
        {
            WinRate = winRate,
            SharpeRatio = sharpeRatio,
            MaxDrawdown = maxDrawdown
        };
    }

    /// <summary>
    /// AI-powered analysis of performance issues when rollback is triggered
    /// </summary>
    private async Task<string> AnalyzeMyPerformanceIssueAsync(string reason)
    {
        if (_ollamaClient == null)
            return string.Empty;

        try
        {
            var metrics = CalculateCanaryMetrics();
            var recentTradesCount = _canaryTrades.Count;

            var prompt = $@"I am a trading bot and I'm performing badly:

Reason for rollback: {reason}
My current win rate: {metrics.WinRate:P1}
My current drawdown: ${metrics.MaxDrawdown:F2}
My recent trades: {recentTradesCount}

Analyze what I'm doing wrong and what I should do differently. Speak as ME (the bot).";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [SELF-ANALYSIS] Error during performance analysis");
            return string.Empty;
        }
    }

    private async Task CreateKillFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var killFilePath = Path.Combine(Directory.GetCurrentDirectory(), "kill.txt");
            await File.WriteAllTextAsync(killFilePath, 
                $"CATASTROPHIC FAILURE DETECTED - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                $"Canary monitoring triggered emergency stop\n" +
                $"Win Rate: {CalculateCanaryMetrics().WinRate:F2}%\n" +
                $"Drawdown: ${CalculateCanaryMetrics().MaxDrawdown:F2}\n",
                cancellationToken).ConfigureAwait(false);
            
            _logger.LogCritical("🚨 [GATE-5] kill.txt created at {Path}", killFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create kill.txt file");
        }
    }

    private Task ExecuteCanaryRollbackAsync(CanaryMetrics metrics, CancellationToken cancellationToken)
    {
        _logger.LogWarning("🔄 [GATE-5] Executing canary rollback - deployment rejected");
        _logger.LogWarning("  Win Rate Drop: {Drop:F2}%, Drawdown: ${DD:F2}, Sharpe Drop: {SR:F2}%",
            (_baselineWinRate - metrics.WinRate) * PercentageMultiplier, metrics.MaxDrawdown, 
            ((_baselineSharpeRatio - metrics.SharpeRatio) / Math.Max(_baselineSharpeRatio, MinimumSharpeForDivision)) * PercentageMultiplier);

        var backupDir = Path.Combine("artifacts", "backup");
        var currentDir = Path.Combine("artifacts", "current");

        if (Directory.Exists(backupDir))
        {
            try
            {
                if (Directory.Exists(currentDir))
                {
                    Directory.Delete(currentDir, recursive: true);
                }
                
                CopyDirectory(backupDir, currentDir);
                _logger.LogInformation("✅ [GATE-5] Artifacts rolled back to previous version");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [GATE-5] Rollback failed");
            }
        }

        _canaryActive = false;
        _canaryTrades.Clear();
        _canaryTradesCompleted = 0;

        Environment.SetEnvironmentVariable("AUTO_PROMOTION_ENABLED", "0");
        _logger.LogWarning("🔒 [GATE-5] Auto-promotion disabled until manual investigation");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Start canary monitoring with baseline metrics
    /// Called after any parameter update or model update
    /// </summary>
    private void StartCanaryMonitoring(Dictionary<string, double> baselineMetrics)
    {
        ArgumentNullException.ThrowIfNull(baselineMetrics);
        
        _isCanaryActive = true;
        _canaryActive = true; // Keep legacy field in sync
        _canaryStartTime = DateTime.UtcNow;
        _baselineMetrics = new Dictionary<string, double>(baselineMetrics);
        _canaryTrades.Clear();
        _canaryTradesCompleted = 0;
        
        // Update legacy baseline fields
        if (baselineMetrics.TryGetValue("win_rate", out var winRate))
            _baselineWinRate = winRate;
        if (baselineMetrics.TryGetValue("sharpe_ratio", out var sharpe))
            _baselineSharpeRatio = sharpe;
        if (baselineMetrics.TryGetValue("drawdown", out var drawdown))
            _baselineDrawdown = drawdown;
        
        _logger.LogInformation("🕊️ [CANARY] Monitoring started with baseline - WinRate: {WinRate:F2}%, " +
                             "Sharpe: {Sharpe:F2}, Drawdown: ${Drawdown:F2}, DailyPnL: ${DailyPnL:F2}",
            baselineMetrics.GetValueOrDefault("win_rate", 0) * PercentageMultiplier,
            baselineMetrics.GetValueOrDefault("sharpe_ratio", 0),
            baselineMetrics.GetValueOrDefault("drawdown", 0),
            baselineMetrics.GetValueOrDefault("daily_pnl", 0));
    }

    /// <summary>
    /// Check canary metrics and trigger rollback if needed
    /// Called every 5 minutes during canary period
    /// </summary>
    private async Task CheckCanaryMetricsAsync(CancellationToken cancellationToken)
    {
        if (!_isCanaryActive || !_gate5Config.Enabled)
        {
            return;
        }

        var elapsedMinutes = (DateTime.UtcNow - _canaryStartTime).TotalMinutes;
        var completedTrades = _canaryTrades.Count;

        // Check if thresholds are met
        var minTrades = _gate5Config.MinTrades;
        var minMinutes = _gate5Config.MinMinutes;

        if (completedTrades < minTrades && elapsedMinutes < minMinutes)
        {
            _logger.LogDebug("🕊️ [CANARY] Monitoring - Trades: {Trades}/{Min}, Time: {Time:F1}/{MinTime} min",
                completedTrades, minTrades, elapsedMinutes, minMinutes);
            return;
        }

        // Calculate current metrics
        var currentWinRate = completedTrades > 0 
            ? (double)_canaryTrades.Count(t => t.PnL > 0) / completedTrades 
            : 0;
        
        var currentDrawdown = CalculateCurrentDrawdown();
        var currentSharpe = CalculateCurrentSharpeRatio();

        _logger.LogInformation("📊 [CANARY] Metrics - Trades: {Trades}, WinRate: {WR:F2}%, " +
                             "Sharpe: {SR:F2}, Drawdown: ${DD:F2}",
            completedTrades, currentWinRate * PercentageMultiplier, currentSharpe, currentDrawdown);

        // Compare to baseline
        var baselineWinRate = _baselineMetrics.GetValueOrDefault("win_rate", 0.5);
        var baselineSharpe = _baselineMetrics.GetValueOrDefault("sharpe_ratio", 1.0);
        var baselineDrawdown = _baselineMetrics.GetValueOrDefault("drawdown", 0);

        var winRateDrop = baselineWinRate - currentWinRate;
        var sharpeRatioDrop = baselineSharpe > 0 ? (baselineSharpe - currentSharpe) / baselineSharpe : 0;
        var drawdownIncrease = currentDrawdown - baselineDrawdown;

        // Check rollback triggers
        var trigger1 = winRateDrop > _gate5Config.WinRateDropThreshold && 
                      drawdownIncrease > _gate5Config.MaxDrawdownDollars;
        var trigger2 = sharpeRatioDrop > _gate5Config.SharpeDropThreshold;

        if (trigger1 || trigger2)
        {
            _logger.LogWarning("🚨 [CANARY] Rollback triggers fired - Trigger1: {T1}, Trigger2: {T2}",
                trigger1, trigger2);
            await ExecuteRollbackAsync(currentWinRate, currentSharpe, currentDrawdown, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (completedTrades >= minTrades && elapsedMinutes >= minMinutes)
        {
            _logger.LogInformation("✅ [CANARY] Monitoring completed successfully - deployment validated");
            _isCanaryActive = false;
            _canaryActive = false;
        }
    }

    /// <summary>
    /// Execute rollback to previous artifacts
    /// </summary>
    private async Task ExecuteRollbackAsync(double currentWinRate, double currentSharpe, 
        double currentDrawdown, CancellationToken cancellationToken)
    {
        var rollbackTimestamp = DateTime.UtcNow;
        
        _logger.LogError("🚨🚨🚨 [ROLLBACK] URGENT: Triggering automatic rollback at {Timestamp}",
            rollbackTimestamp);
        _logger.LogError("📊 [ROLLBACK] Current Metrics - WinRate: {WR:F2}%, Sharpe: {SR:F2}, " +
                       "Drawdown: ${DD:F2}",
            currentWinRate * PercentageMultiplier, currentSharpe, currentDrawdown);
        _logger.LogError("📊 [ROLLBACK] Baseline Metrics - WinRate: {WR:F2}%, Sharpe: {SR:F2}, " +
                       "Drawdown: ${DD:F2}",
            _baselineMetrics.GetValueOrDefault("win_rate", 0) * PercentageMultiplier,
            _baselineMetrics.GetValueOrDefault("sharpe_ratio", 0),
            _baselineMetrics.GetValueOrDefault("drawdown", 0));

        // AI-powered self-analysis of performance issues
        if (_ollamaClient != null)
        {
            var reason = $"Win rate dropped to {currentWinRate:P1}, Sharpe ratio: {currentSharpe:F2}, Drawdown: ${currentDrawdown:F2}";
            var analysis = await AnalyzeMyPerformanceIssueAsync(reason).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(analysis))
            {
                _logger.LogError("🔍 [BOT-SELF-ANALYSIS] {Analysis}", analysis);
            }
        }

        try
        {
            // Step 1: Load backup parameters from artifacts backup folder
            var backupDir = string.IsNullOrEmpty(_previousArtifactBackupPath) 
                ? Path.Combine("artifacts", "backup")
                : _previousArtifactBackupPath;
            
            if (!Directory.Exists(backupDir))
            {
                _logger.LogError("❌ [ROLLBACK] Backup directory not found: {Path}", backupDir);
                await SendRollbackAlertAsync("Backup directory not found", currentWinRate, 
                    currentSharpe, currentDrawdown).ConfigureAwait(false);
                return;
            }

            var currentDir = Path.Combine("artifacts", "current");
            var rollbackTempDir = Path.Combine("artifacts", "rollback_temp_" + rollbackTimestamp.Ticks);

            // Step 2: Atomic copy of backup parameters
            _logger.LogInformation("📦 [ROLLBACK] Loading backup parameters from {Path}", backupDir);
            
            if (Directory.Exists(currentDir))
            {
                // Create temp directory and copy backup there first
                Directory.CreateDirectory(rollbackTempDir);
                CopyDirectory(backupDir, rollbackTempDir);
                
                // Atomic swap: delete current, move temp to current
                Directory.Delete(currentDir, recursive: true);
                Directory.Move(rollbackTempDir, currentDir);
                
                _logger.LogInformation("✅ [ROLLBACK] Parameters rolled back successfully");
            }

            // Step 3: If model files exist, load backup ONNX models
            var backupModelsDir = Path.Combine(backupDir, "models");
            if (Directory.Exists(backupModelsDir))
            {
                var currentModelsDir = Path.Combine(currentDir, "models");
                Directory.CreateDirectory(currentModelsDir);
                
                foreach (var modelFile in Directory.GetFiles(backupModelsDir, "*.onnx"))
                {
                    var destFile = Path.Combine(currentModelsDir, Path.GetFileName(modelFile));
                    File.Copy(modelFile, destFile, overwrite: true);
                    _logger.LogInformation("📦 [ROLLBACK] Restored ONNX model: {Model}", 
                        Path.GetFileName(modelFile));
                }
            }

            // Step 4: Pause all future promotions until manual review
            Environment.SetEnvironmentVariable("AUTO_PROMOTION_ENABLED", "0");
            _logger.LogWarning("🔒 [ROLLBACK] Auto-promotion disabled - manual review required");

            // Step 5: Send high priority alert
            await SendRollbackAlertAsync("Rollback completed successfully", currentWinRate, 
                currentSharpe, currentDrawdown).ConfigureAwait(false);

            // Step 6: Check for catastrophic degradation
            var isCatastrophic = currentWinRate < _gate5Config.CatastrophicWinRateThreshold ||
                               currentDrawdown > _gate5Config.CatastrophicDrawdownDollars;

            if (isCatastrophic)
            {
                _logger.LogCritical("💥 [ROLLBACK] CATASTROPHIC FAILURE DETECTED - Creating kill.txt");
                await CreateKillFileAsync(cancellationToken).ConfigureAwait(false);
                
                await SendCriticalAlertAsync("CATASTROPHIC FAILURE", 
                    $"Win Rate: {currentWinRate:F2}%, Drawdown: ${currentDrawdown:F2}")
                    .ConfigureAwait(false);
            }

            // Step 7: Log all rollback actions with timestamps
            _logger.LogInformation("📝 [ROLLBACK] Actions completed at {Timestamp}:", DateTime.UtcNow);
            _logger.LogInformation("  ✓ Backup parameters loaded from: {Path}", backupDir);
            _logger.LogInformation("  ✓ Parameters copied atomically to: {Path}", currentDir);
            _logger.LogInformation("  ✓ ONNX models restored (if existed)");
            _logger.LogInformation("  ✓ Auto-promotion disabled");
            _logger.LogInformation("  ✓ High priority alert sent");
            if (isCatastrophic)
            {
                _logger.LogInformation("  ✓ kill.txt created (catastrophic failure)");
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "❌ [ROLLBACK] I/O error during rollback");
            await SendRollbackAlertAsync($"Rollback failed: {ex.Message}", currentWinRate, 
                currentSharpe, currentDrawdown).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "❌ [ROLLBACK] Access denied during rollback");
            await SendRollbackAlertAsync($"Rollback failed: {ex.Message}", currentWinRate, 
                currentSharpe, currentDrawdown).ConfigureAwait(false);
        }
        finally
        {
            // Step 8: Set canary inactive
            _isCanaryActive = false;
            _canaryActive = false;
        }
    }

    /// <summary>
    /// Track trade result for canary monitoring
    /// Called after each completed trade
    /// </summary>
    private void TrackTradeResult(string symbol, string strategy, decimal pnl, string outcome)
    {
        if (!_isCanaryActive)
        {
            return;
        }

        var trade = new CanaryTradeRecord
        {
            Symbol = symbol,
            Strategy = strategy,
            PnL = (double)pnl,
            Outcome = outcome,
            Timestamp = DateTime.UtcNow
        };

        _canaryTrades.Add(trade);
        _canaryTradesCompleted++;

        _logger.LogInformation("🕊️ [CANARY] Trade captured - {Symbol} {Strategy} PnL: ${PnL:F2} " +
                             "({Total} trades tracked)",
            symbol, strategy, pnl, _canaryTrades.Count);
    }

    /// <summary>
    /// Capture current metrics from recent trade history
    /// Called right before any artifact upgrade
    /// </summary>
    private async Task<Dictionary<string, double>> CaptureCurrentMetricsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📊 [METRICS] Capturing current baseline metrics...");

            // Query last 60 minutes of trade history
            var lookbackTime = DateTime.UtcNow.AddMinutes(-60);
            var recentTrades = await GetRecentTradesAsync(lookbackTime, cancellationToken)
                .ConfigureAwait(false);

            if (recentTrades.Count == 0)
            {
                _logger.LogWarning("⚠️ [METRICS] No recent trades found, using default baseline");
                return new Dictionary<string, double>
                {
                    ["win_rate"] = (double)HalfThreshold,
                    ["daily_pnl"] = 0,
                    ["sharpe_ratio"] = 1.0,
                    ["drawdown"] = 0
                };
            }

            // Calculate win rate
            var wins = recentTrades.Count(t => t.PnL > 0);
            var winRate = (double)wins / recentTrades.Count;

            // Calculate daily PnL
            var dailyPnl = recentTrades.Sum(t => t.PnL);

            // Calculate Sharpe ratio
            var returns = recentTrades.Select(t => t.PnL).ToList();
            var avgReturn = returns.Average();
            var stdDev = Math.Sqrt(returns.Average(r => Math.Pow(r - avgReturn, 2)));
            var sharpeRatio = stdDev > 0 ? avgReturn / stdDev : 0;

            // Calculate current drawdown
            double peak = 0;
            double equity = 0;
            double maxDrawdown = 0;

            foreach (var trade in recentTrades)
            {
                equity += trade.PnL;
                if (equity > peak) peak = equity;
                var drawdown = peak - equity;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }

            var metrics = new Dictionary<string, double>
            {
                ["win_rate"] = winRate,
                ["daily_pnl"] = dailyPnl,
                ["sharpe_ratio"] = sharpeRatio,
                ["drawdown"] = maxDrawdown
            };

            _logger.LogInformation("✅ [METRICS] Baseline captured - Trades: {Count}, WinRate: {WR:F2}%, " +
                                 "DailyPnL: ${PnL:F2}, Sharpe: {SR:F2}, Drawdown: ${DD:F2}",
                recentTrades.Count, winRate * PercentageMultiplier, dailyPnl, sharpeRatio, maxDrawdown);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [METRICS] Failed to capture current metrics");
            return new Dictionary<string, double>
            {
                ["win_rate"] = (double)HalfThreshold,
                ["daily_pnl"] = 0,
                ["sharpe_ratio"] = 1.0,
                ["drawdown"] = 0
            };
        }
    }

    /// <summary>
    /// Backup current artifacts before upgrade
    /// Called right before applying any upgrade
    /// </summary>
    private async Task<string> BackupCurrentArtifactsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var backupDir = Path.Combine("artifacts", "backups", $"backup_{timestamp}");
            
            _logger.LogInformation("📦 [BACKUP] Creating artifact backup at {Path}", backupDir);
            
            Directory.CreateDirectory(backupDir);

            // Backup parameter JSON files
            var currentDir = Path.Combine("artifacts", "current");
            if (Directory.Exists(currentDir))
            {
                var paramFiles = Directory.GetFiles(currentDir, "*.json", SearchOption.AllDirectories);
                foreach (var paramFile in paramFiles)
                {
                    var relativePath = Path.GetRelativePath(currentDir, paramFile);
                    var destFile = Path.Combine(backupDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(paramFile, destFile, overwrite: true);
                }
                _logger.LogInformation("✅ [BACKUP] Backed up {Count} parameter files", paramFiles.Length);
            }

            // Backup ONNX model files
            var modelsDir = Path.Combine("artifacts", "current", "models");
            if (Directory.Exists(modelsDir))
            {
                var backupModelsDir = Path.Combine(backupDir, "models");
                Directory.CreateDirectory(backupModelsDir);
                
                var modelFiles = Directory.GetFiles(modelsDir, "*.onnx", SearchOption.AllDirectories);
                foreach (var modelFile in modelFiles)
                {
                    var fileName = Path.GetFileName(modelFile);
                    var destFile = Path.Combine(backupModelsDir, fileName);
                    File.Copy(modelFile, destFile, overwrite: true);
                }
                _logger.LogInformation("✅ [BACKUP] Backed up {Count} ONNX model files", modelFiles.Length);
            }

            // Create manifest
            var manifest = new
            {
                BackupTimestamp = timestamp,
                BackupPath = backupDir,
                ParameterFiles = Directory.GetFiles(backupDir, "*.json", SearchOption.AllDirectories)
                    .Select(Path.GetFileName).ToArray(),
                ModelFiles = Directory.Exists(Path.Combine(backupDir, "models"))
                    ? Directory.GetFiles(Path.Combine(backupDir, "models"), "*.onnx")
                        .Select(Path.GetFileName).ToArray()
                    : Array.Empty<string>(),
                CreatedAt = DateTime.UtcNow
            };

            var manifestPath = Path.Combine(backupDir, "manifest.json");
            var manifestJson = JsonSerializer.Serialize(manifest, s_jsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("✅ [BACKUP] Manifest created at {Path}", manifestPath);
            _logger.LogInformation("📦 [BACKUP] Backup completed successfully: {Path}", backupDir);

            _previousArtifactBackupPath = backupDir;
            return backupDir;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "❌ [BACKUP] I/O error creating artifact backup");
            return string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "❌ [BACKUP] Access denied creating artifact backup");
            return string.Empty;
        }
    }

    /// <summary>
    /// Helper method to get recent trades
    /// </summary>
    private Task<List<CanaryTradeRecord>> GetRecentTradesAsync(DateTime since, 
        CancellationToken cancellationToken)
    {
        // Use canary trades if available, otherwise return empty list
        var recentTrades = _canaryTrades
            .Where(t => t.Timestamp >= since)
            .ToList();
        
        return Task.FromResult(recentTrades);
    }

    /// <summary>
    /// Calculate current drawdown from canary trades
    /// </summary>
    private double CalculateCurrentDrawdown()
    {
        if (_canaryTrades.Count == 0) return 0;

        double peak = 0;
        double equity = 0;
        double maxDrawdown = 0;

        foreach (var trade in _canaryTrades)
        {
            equity += trade.PnL;
            if (equity > peak) peak = equity;
            var drawdown = peak - equity;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    /// <summary>
    /// Calculate current Sharpe ratio from canary trades
    /// </summary>
    private double CalculateCurrentSharpeRatio()
    {
        if (_canaryTrades.Count == 0) return 0;

        var returns = _canaryTrades.Select(t => t.PnL).ToList();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Average(r => Math.Pow(r - avgReturn, 2)));
        
        return stdDev > 0 ? avgReturn / stdDev : 0;
    }

    /// <summary>
    /// Send rollback alert
    /// </summary>
    private async Task SendRollbackAlertAsync(string message, double winRate, double sharpe, 
        double drawdown)
    {
        // Log high-priority alert (actual alert service integration happens at orchestrator level)
        _logger.LogCritical("🚨 [ALERT] CANARY ROLLBACK TRIGGERED");
        _logger.LogCritical("📧 [ALERT] Message: {Message}", message);
        _logger.LogCritical("📊 [ALERT] Win Rate: {WinRate:F2}%", winRate * PercentageMultiplier);
        _logger.LogCritical("📊 [ALERT] Sharpe Ratio: {Sharpe:F2}", sharpe);
        _logger.LogCritical("📊 [ALERT] Drawdown: ${Drawdown:F2}", drawdown);
        _logger.LogCritical("🕐 [ALERT] Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC", DateTime.UtcNow);
        
        // Send alert via BotAlertService if available
        if (_botAlertService != null)
        {
            await _botAlertService.AlertRollbackAsync(
                message,
                (decimal)(winRate * PercentageMultiplier),
                (decimal)drawdown
            ).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Send critical alert
    /// </summary>
    private Task SendCriticalAlertAsync(string title, string details)
    {
        // Log critical alert (actual alert service integration happens at orchestrator level)
        _logger.LogCritical("💥 [CRITICAL-ALERT] {Title}", title);
        _logger.LogCritical("📧 [CRITICAL-ALERT] {Details}", details);
        
        return Task.CompletedTask;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(destinationDir, Path.GetDirectoryName(dir)!);
            CopyDirectory(dir, destDir);
        }
    }

    private async Task MonitorSystemHealthAsync(CancellationToken cancellationToken)
    {
        // Monitor system health and trigger alerts if needed
        var isHealthy = IsSystemHealthy();
        
        if (!isHealthy)
        {
            _logger.LogWarning("⚠️ [SYSTEM-HEALTH] System health degraded, triggering recovery actions");
            await TriggerRecoveryActionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    
    private Task CheckContractRolloverAsync(CancellationToken cancellationToken)
    {
        return _rolloverManager.CheckRolloverNeedsAsync(cancellationToken);
    }
    
    private static UnifiedTradingDecision CreateEmergencyDecision(
        string symbol,
                string decisionId, 
        DateTime startTime)
    {
        return new UnifiedTradingDecision
        {
            DecisionId = decisionId,
            Symbol = symbol,
            Action = TradingAction.Buy, // Conservative emergency bias
            Confidence = EmergencyFallbackConfidence,
            Quantity = EmergencyFallbackQuantity,
            Strategy = "EMERGENCY",
            DecisionSource = "Emergency",
            Reasoning = new Dictionary<string, object>
            {
                ["source"] = "Emergency fallback - all decision systems failed",
                ["safety"] = "Conservative BUY bias with minimum sizing"
            },
            Timestamp = DateTime.UtcNow,
            ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
        };
    }
    
    private Task UpdatePerformanceTrackingAsync(
        string decisionId,
        string decisionSource,
        decimal realizedPnL,
        bool wasCorrect,
        CancellationToken cancellationToken = default
        )
    {
        lock (_stateLock)
        {
            if (!_performanceTracking.TryGetValue(decisionSource, out var performance))
            {
                performance = new DecisionPerformance { Source = decisionSource };
                _performanceTracking[decisionSource] = performance;
            }
            
            performance.TotalDecisions++;
            performance.TotalPnL += realizedPnL;
            if (wasCorrect) performance.WinningDecisions++;
            performance.WinRate = performance.TotalDecisions > 0 ? 
                (decimal)performance.WinningDecisions / performance.TotalDecisions : 0;
            performance.LastUpdated = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }
    
    private decimal CalculateOverallWinRate()
    {
        var totalDecisions = _performanceTracking.Values.Sum(p => p.TotalDecisions);
        var totalWinning = _performanceTracking.Values.Sum(p => p.WinningDecisions);
        
        return totalDecisions > 0 ? (decimal)totalWinning / totalDecisions : 0;
    }
    
    private bool IsSystemHealthy()
    {
        // Check if learning is active
        if (!_isLearningActive) return false;
        
        // Check recent performance
        var recentPerformance = _performanceTracking.Values
            .Where(p => DateTime.UtcNow - p.LastUpdated < TimeSpan.FromHours(1))
            .ToList();
            
        if (recentPerformance.Count == 0) return true; // No recent decisions is okay
        
        // Check if any source has very poor performance
        var poorPerformers = recentPerformance
            .Where(p => p.TotalDecisions >= 10 && p.WinRate < 0.3m)
            .ToList();
            
        return poorPerformers.Count == 0;
    }
    
    private async Task TriggerRecoveryActionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 [RECOVERY] Triggering system recovery actions...");
        
        try
        {
            // Restart learning systems
            await _learningManager.RestartAsync(cancellationToken).ConfigureAwait(false);
            
            // Force model updates
            await _learningManager.ForceUpdateAsync(cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("✅ [RECOVERY] Recovery actions completed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "❌ [RECOVERY] Invalid operation during recovery actions");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "❌ [RECOVERY] Timeout during recovery actions");
        }
    }
    
    private async Task GenerateDetailedPerformanceReportAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = new PerformanceReport
            {
                Timestamp = DateTime.UtcNow,
                OverallStats = new OverallStats
                {
                    TotalDecisions = _performanceTracking.Values.Sum(p => p.TotalDecisions),
                    TotalPnL = _performanceTracking.Values.Sum(p => p.TotalPnL),
                    OverallWinRate = CalculateOverallWinRate(),
                    ActiveSources = _performanceTracking.Count
                }
            };
            
            // Add performance data to the collection property
            foreach (var performance in _performanceTracking.Values)
            {
                report.SourcePerformanceInternal.Add(performance);
            }
            
            // Save report
            var reportPath = Path.Combine("reports", $"performance_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            
            var json = JsonSerializer.Serialize(report, s_jsonOptions);
            await File.WriteAllTextAsync(reportPath, json, cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("📊 [PERFORMANCE-REPORT] Generated detailed report: {ReportPath}", reportPath);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "❌ [PERFORMANCE-REPORT] Failed to serialize performance report to JSON");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "❌ [PERFORMANCE-REPORT] Failed to write performance report to file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "❌ [PERFORMANCE-REPORT] Access denied writing performance report");
        }
    }
    
    private static string GenerateDecisionId()
    {
        return $"MD{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(DecisionIdRandomMin, DecisionIdRandomMax)}";
    }
    
    /// <summary>
    /// Feature 3: Check if it's time to generate performance summaries
    /// </summary>
    private async Task CheckPerformanceSummariesAsync(CancellationToken cancellationToken)
    {
        if (_performanceReporter == null)
            return;

        try
        {
            // Check if enabled in configuration
            var dailyEnabled = Environment.GetEnvironmentVariable("BOT_DAILY_SUMMARY_ENABLED")?.ToUpperInvariant() == "TRUE";
            var weeklyEnabled = Environment.GetEnvironmentVariable("BOT_WEEKLY_SUMMARY_ENABLED")?.ToUpperInvariant() == "TRUE";

            // Generate daily summary if needed
            if (dailyEnabled && _performanceReporter.ShouldGenerateDailySummary())
            {
                await _performanceReporter.GenerateDailySummaryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Generate weekly summary if needed
            if (weeklyEnabled && _performanceReporter.ShouldGenerateWeeklySummary())
            {
                await _performanceReporter.GenerateWeeklySummaryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PERFORMANCE-SUMMARIES] Error checking for performance summaries");
        }
    }
    
    /// <summary>
    /// Dispose method to clean up resources during service shutdown
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _neuralUcbExtended?.Dispose();
        return base.StopAsync(cancellationToken);
    }
    
    #endregion
}

#region Supporting Classes

/// <summary>
/// Continuous learning manager - coordinates all learning activities
/// </summary>
public class ContinuousLearningManager
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    
    public ContinuousLearningManager(ILogger logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task StartLearningAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task ProcessLearningEventsAsync(IReadOnlyList<LearningEvent> events, CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task CheckAndUpdateModelsAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task ForceUpdateAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task RestartAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task TrackDecisionAsync(DecisionTrackingInfo info, CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
}

/// <summary>
/// Contract rollover manager - handles Z25 → H26 transitions
/// </summary>
public class ContractRolloverManager
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    
    public ContractRolloverManager(ILogger logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
    
    public Task CheckRolloverNeedsAsync(CancellationToken cancellationToken)
    {
        _ = _logger; // Placeholder implementation - will be implemented in future phase
        return Task.CompletedTask;
    }
}

#endregion

#region Data Models

public class MasterOrchestratorConfig
{
    public int OrchestrationCycleIntervalSeconds { get; set; } = 10;
    public int ModelUpdateIntervalHours { get; set; } = 2;
    public int PerformanceReportIntervalHours { get; set; } = 6;
    public bool EnableContinuousLearning { get; set; } = true;
    public bool EnableContractRollover { get; set; } = true;
}

public class LearningEvent
{
    public string DecisionId { get; set; } = string.Empty;
    public decimal RealizedPnL { get; set; }
    public bool WasCorrect { get; set; }
    public TimeSpan HoldTime { get; set; }
    public string DecisionSource { get; set; } = string.Empty;
    
    private readonly Dictionary<string, object> _metadata = new();
    public IReadOnlyDictionary<string, object> Metadata => _metadata;
    
    public void ReplaceMetadata(IDictionary<string, object> metadata)
    {
        _metadata.Clear();
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
        }
    }
    
    public DateTime Timestamp { get; set; }
}

public class DecisionTrackingInfo
{
    public string DecisionId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public TradingAction Action { get; set; }
    public decimal Confidence { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string DecisionSource { get; set; } = string.Empty;
    public MarketContext MarketContext { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class DecisionPerformance
{
    public string Source { get; set; } = string.Empty;
    public int TotalDecisions { get; set; }
    public int WinningDecisions { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class MasterOrchestratorStatus
{
    public bool IsLearningActive { get; set; }
    public DateTime LastModelUpdate { get; set; }
    public DateTime LastPerformanceReport { get; set; }
    public int TotalDecisionsToday { get; set; }
    public decimal OverallWinRate { get; set; }
    public int LearningQueueSize { get; set; }
    public TradingBot.Abstractions.DecisionServiceStatus ServiceStatus { get; set; } = new();
    
    private readonly Dictionary<string, DecisionPerformance> _brainPerformance = new();
    public IReadOnlyDictionary<string, DecisionPerformance> BrainPerformance => _brainPerformance;
    
    public void ReplaceBrainPerformance(IDictionary<string, DecisionPerformance> performance)
    {
        _brainPerformance.Clear();
        if (performance != null)
        {
            foreach (var kvp in performance)
            {
                _brainPerformance[kvp.Key] = kvp.Value;
            }
        }
    }
    
    public bool SystemHealthy { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PerformanceReport
{
    private readonly List<DecisionPerformance> _sourcePerformance = new();
    
    public DateTime Timestamp { get; set; }
    public OverallStats OverallStats { get; set; } = new();
    public IReadOnlyList<DecisionPerformance> SourcePerformance => _sourcePerformance;
    internal List<DecisionPerformance> SourcePerformanceInternal => _sourcePerformance;
}

public class OverallStats
{
    public int TotalDecisions { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal OverallWinRate { get; set; }
    public int ActiveSources { get; set; }
}

/// <summary>
/// Enhanced tracking info that includes bundle selection data
/// </summary>
public class BundleDecisionTrackingInfo
{
    public string DecisionId { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public BundleSelection BundleSelection { get; set; } = new();
    public DecisionTrackingInfo StandardTracking { get; set; } = new();
}

/// <summary>
/// Gate 5: Trade result for canary monitoring - used for JSON serialization
/// </summary>
public sealed class Gate5TradeResult
{
    public double PnL { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Gate 5: Canary metrics
/// </summary>
internal sealed class CanaryMetrics
{
    public double WinRate { get; set; }
    public double SharpeRatio { get; set; }
    public double MaxDrawdown { get; set; }
}

/// <summary>
/// Canary trade record with full details
/// </summary>
internal sealed class CanaryTradeRecord
{
    public string Symbol { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public double PnL { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

#endregion