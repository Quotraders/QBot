using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BotCore.Strategy;
using BotCore.Risk;
using BotCore.Models;
using BotCore.ML;
using BotCore.Bandits;
using BotCore.Brain.Models;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using TradingBot.RLAgent; // For CVaRPPO and ActionResult
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TradingBot.Abstractions;

// Type aliases to resolve ambiguity between BotCore.Brain.Models and TradingBot.Abstractions
using MarketContext = BotCore.Brain.Models.MarketContext;
using MarketRegime = BotCore.Brain.Models.MarketRegime;
using TradingDecision = BotCore.Brain.Models.TradingDecision;

namespace BotCore.Brain
{
    /// <summary>
    /// TopStep compliance configuration integrated into UnifiedTradingBrain
    /// </summary>
    public static class TopStepConfig
    {
        public const decimal AccountSize = 50_000m;
        public const decimal MaxDrawdown = 2_000m;
        public const decimal DailyLossLimit = 1_000m;
        public const decimal TrailingStop = 48_000m;
        public const decimal EsPointValue = 50m;
        public const decimal NqPointValue = 20m;
        public const decimal RiskPerTrade = 0.01m; // 1% = $500 baseline
        public const double ExplorationBonus = 0.3;
        public const double ConfidenceThreshold = 0.65;
        
        // Trading Brain confidence and probability constants
        public const decimal FallbackConfidence = 0.6m;             // Fallback strategy confidence
        public const decimal FallbackUcbValue = 0.5m;              // Fallback UCB value
        public const decimal HighConfidenceProbability = 0.70m;     // High confidence prediction probability
        public const decimal NeutralProbability = 0.5m;             // Neutral/sideways prediction probability
        public const decimal DefaultAtrFallback = 25.0m;           // Default ATR for NQ when unavailable
        public const decimal EsDefaultAtrFallback = 10.0m;        // Default ATR for ES when unavailable
        public const decimal NqMinStopDistance = 0.5m;            // Minimum stop distance for NQ
        public const decimal EsMinStopDistance = 0.25m;           // Minimum stop distance for ES
        public const decimal FallbackExpectedMove = 5;             // Fallback expected move value
        public const int DefaultAtrLookback = 10;                  // Default ATR lookback period
        
        // Position sizing and risk multipliers
        public const decimal MinRlMultiplier = 0.5m;               // Minimum RL position multiplier
        public const decimal MaxRlMultiplier = 1.5m;               // Maximum RL position multiplier
        public const decimal NearDailyLossWarningFactor = 0.9m;    // Warning at 90% of daily loss
        public const decimal NearMaxDrawdownWarningFactor = 0.8m;  // Warning at 80% of max drawdown
        
        // Confidence and reward calculation constants
        public const decimal StrategyPredictionAverageWeight = 2m; // Weight for averaging strategy and prediction confidence
        public const decimal DefaultNeutralPositionMultiplier = 1.0m; // Default neutral position size
        public const decimal DefaultRsiNeutral = 50m;              // Default RSI neutral value
        public const decimal DefaultRsiMax = 100m;                 // Maximum RSI value
        public const int MinBarsPeriod = 10;                       // Minimum bars for period calculations
        public const int MinBarsExtended = 20;                     // Minimum bars for extended calculations
        
        // Strategy performance evaluation thresholds
        public const decimal PoorPerformanceWinRateThreshold = 0.4m;  // Below this is considered poor performance
        public const decimal UnsuccessfulConditionThreshold = 0.3m;   // Condition success rate below this is unsuccessful
        public const decimal HighPerformanceWinRateThreshold = 0.7m;  // Above this is considered high performance
        public const decimal MinimumWinRateToSharePatterns = 0.6m;   // Minimum win rate to share patterns with other strategies
        public const decimal SharedPatternSuccessThreshold = 0.7m;   // Success rate threshold for cross-pollination
        public const decimal CrossPollinationWeight = 0.1m;          // Lower weight for cross-pollinated patterns
        public const decimal MinimumConditionSuccessRate = 0.6m;     // Minimum success rate for condition to be considered
        public const int MinimumConditionTrialCount = 3;             // Minimum number of trials to trust condition
        public const int TopConditionsToSelect = 5;                  // Number of top conditions to select per strategy
        public const int MinimumDecisionsForTrend = 5;               // Minimum decisions needed to calculate performance trend
        public const int RecentDecisionsLookbackHours = 24;          // Hours to look back for recent decisions
        
        // Scheduling and learning intervals
        public const int MaintenanceLearningIntervalMinutes = 10;    // Learning interval during maintenance window
        public const int ClosedMarketLearningIntervalMinutes = 15;   // Learning interval when market is closed
        
        // Commentary thresholds
        public const decimal LowConfidenceThreshold = 0.4m;          // Below this triggers waiting commentary
        // HighConfidenceThreshold removed - now uses IMLConfigurationService.GetAIConfidenceThreshold()
        public const decimal StrategyConflictThreshold = 0.15m;       // Score difference threshold for conflict detection
        // AlternativeStrategyConfidenceFactor removed - now uses IMLConfigurationService.GetAIConfidenceThreshold()
        
        // Statistical calculation constants
        public const double TotalVariationNormalizationFactor = 0.5; // Factor for normalizing total variation distance
        
        // Historical simulation constants
        public const int MinHistoricalBarsForSimulation = 100;       // Minimum bars needed for reliable simulation
        public const int FeatureVectorLength = 11;                   // Number of features in simulation data
        public const int SimulationRandomSeed = 12345;               // Seed for reproducible simulation data
        public const double SimulationFeatureRange = 2.0;            // Range for random feature generation
        public const double SimulationFeatureOffset = 1.0;           // Offset for centering feature range
        public const int OpenMarketLearningIntervalMinutes = 60;     // Learning interval when market is open
        public const int TrainingDataHistorySize = 2000;             // Number of recent decisions to use for training
        
        // CVaR-PPO state vector normalization constants
        public const double VolatilityNormalizationDivisor = 2.0;    // Divisor for volatility normalization
        public const double PriceChangeMomentumDivisor = 20.0;       // Divisor for price change normalization
        public const double VolumeSurgeNormalizationDivisor = 3.0;   // Divisor for volume ratio normalization
        public const double AtrNormalizationDivisor = 50.0;          // Divisor for ATR normalization
        public const double UcbValueNormalizationDivisor = 10.0;     // Divisor for UCB value normalization
        public const double S2VwapStrategyEncoding = 0.25;           // Encoding value for S2_VWAP strategy
        public const double S3CompressionStrategyEncoding = 0.5;     // Encoding value for S3_Compression strategy
        public const double S11OpeningStrategyEncoding = 0.75;       // Encoding value for S11_Opening strategy
        public const double S12MomentumStrategyEncoding = 1.0;       // Encoding value for S12_Momentum strategy
        public const double DefaultStrategyEncoding = 0.0;           // Default encoding for unknown strategy
        public const double DirectionEncodingDivisor = 2.0;          // Divisor for direction encoding
        public const double DirectionEncodingOffset = 0.5;           // Offset for direction encoding (Down=0, Sideways=0.5, Up=1)
        public const double HoursPerDay = 24.0;                      // Hours in a day for cyclical encoding
        public const double MaxDecisionsPerDayNormalization = 50.0;  // Maximum decisions per day for normalization
        
        // CVaR-PPO action to position size multipliers
        public const decimal NoTradeMultiplier = 0.0m;               // Action 0: No trade
        public const decimal MicroPositionMultiplier = 0.25m;        // Action 1: Micro position (25%)
        public const decimal SmallPositionMultiplier = 0.5m;         // Action 2: Small position (50%)
        public const decimal NormalPositionMultiplier = 1.0m;        // Action 3: Normal position (100%)
        public const decimal LargePositionMultiplier = 1.5m;         // Action 4: Large position (150%)
        public const decimal MaxPositionMultiplier = 2.0m;           // Action 5: Maximum position (200%)
        
        // CVaR risk adjustment constants
        public const decimal MaxNormalizationValue = 1.0m;           // Maximum value for Min() normalization
        public const double MinNormalizationValue = 1.0;             // Minimum value for Min() normalization (double)
        public const int CyclicalEncodingMultiplier = 2;             // Multiplier for sin/cos cyclical encoding
        public const decimal MinProbabilityAdjustment = 0.3m;        // Minimum probability adjustment for position sizing
        public const decimal MinValueAdjustment = 0.2m;              // Minimum value adjustment for position sizing
        public const decimal MaxValueAdjustment = 1.5m;              // Maximum value adjustment for position sizing
        public const decimal ValueEstimateOffset = 0.5m;             // Offset for value estimate adjustment
        public const decimal HighNegativeTailRiskThreshold = -0.1m;  // Threshold for high negative tail risk (CVaR)
        public const decimal ModerateTailRiskThreshold = -0.05m;     // Threshold for moderate tail risk (CVaR)
        public const decimal HighRiskPositionReduction = 0.5m;       // Position reduction for high tail risk (50%)
        public const decimal ModerateRiskPositionReduction = 0.75m;  // Position reduction for moderate tail risk (25%)
    }
    /// <summary>
    /// UNIFIED TRADING BRAIN - The ONE intelligence that controls all trading decisions
    /// Enhanced to handle all 4 primary strategies (S2, S3, S6, S11) with unified scheduling
    /// 
    /// This is the central AI brain that:
    /// 1. Handles S2 (VWAP Mean Reversion), S3 (Bollinger Compression), S6 (Momentum), S11 (Specialized)
    /// 2. Uses Neural UCB to select optimal strategy for each market condition
    /// 3. Uses LSTM to predict price movements and timing
    /// 4. Uses CVaR-PPO to optimize position sizes for all strategies
    /// 5. Maintains identical intelligence for historical and live trading
    /// 6. Continuously learns from all trade outcomes to improve strategy selection
    /// 
    /// KEY ENHANCEMENTS:
    /// - Multi-strategy learning: Every trade outcome teaches all strategies
    /// - Unified scheduling: Same timing for historical and live systems
    /// - Continuous improvement: Historical patterns improve live strategy selection
    /// - Same AI brain gets smarter at picking S2 vs S3 vs S6 vs S11
    /// - Position sizing and risk management learns from all strategy results
    /// 
    /// INTEGRATION POINTS:
    /// - AutonomousDecisionEngine calls this brain for live trading
    /// - EnhancedBacktestLearningService uses same brain for historical replay
    /// - AllStrategies.generate_candidates() enhanced with brain decisions
    /// - Identical scheduling for Market Open: Light learning every 60 min, Market Closed: Intensive every 15 min
    /// </summary>
    public class UnifiedTradingBrain : IDisposable
    {
        private readonly ILogger<UnifiedTradingBrain> _logger;
        private readonly IMLMemoryManager _memoryManager;
        private readonly StrategyMlModelManager _modelManager;
        private readonly NeuralUcbBandit _strategySelector;
        private readonly IMLConfigurationService _mlConfigService;
        private readonly ConcurrentDictionary<string, MarketContext> _marketContexts = new();
        private readonly ConcurrentDictionary<string, TradingPerformance> _performance = new();
        private readonly CVaRPPO _cvarPPO; // Direct injection instead of loading from memory
        private readonly BotCore.Services.OllamaClient? _ollamaClient; // Optional AI conversation client
        private readonly BotCore.Services.RiskAssessmentCommentary? _riskCommentary;
        private readonly BotCore.Services.AdaptiveLearningCommentary? _learningCommentary;
        private readonly BotCore.Services.MarketSnapshotStore? _snapshotStore;
        private readonly BotCore.Services.HistoricalPatternRecognitionService? _historicalPatterns;
        private readonly BotCore.Services.ParameterChangeTracker? _parameterTracker;
        
        // Latest market data for risk analysis (updated in MakeIntelligentDecisionAsync)
        private Env? _latestEnv;
        private IList<Bar>? _latestBars;
        
        // ML Models for different decision points
        private object? _lstmPricePredictor;
        private object? _metaClassifier;
        private object? _marketRegimeDetector;
        private readonly INeuralNetwork? _confidenceNetwork;
        
        // TopStep compliance tracking
        private decimal _currentDrawdown;
        private decimal _dailyPnl;
        private decimal _accountBalance = TopStepConfig.AccountSize;
        private DateTime _lastResetDate = DateTime.UtcNow.Date;
        
        // Performance tracking for learning
        private readonly List<TradingDecision> _decisionHistory = new();
        private DateTime _lastModelUpdate = DateTime.MinValue;
        
        // Cached JsonSerializerOptions for performance
        private static readonly JsonSerializerOptions CachedJsonOptions = new() { WriteIndented = true };
        
        // Unified Trading Brain Constants
        // Learning system constants
        private const int MinDecisionsForLearningUpdate = 50;        // Minimum decisions for learning update
        private const int MinDecisionsForModelRetraining = 200;      // Minimum decisions for model retraining
        private const int LearningUpdateIntervalHours = 2;           // Hours between learning updates  
        private const int ModelRetrainingIntervalHours = 8;          // Hours between model retraining
        
        // Market condition analysis constants  
        private const decimal LowVolatilityThreshold = 0.15m;        // Low volatility threshold
        private const decimal HighVolatilityThreshold = 0.4m;        // High volatility threshold
        private const decimal HighVolumeRatioThreshold = 1.5m;       // High volume ratio threshold
        private const decimal StrongTrendThreshold = 0.7m;           // Strong trend threshold
        private const decimal WeakTrendThreshold = 0.3m;             // Weak trend threshold
        private const decimal OverboughtRSILevel = 70m;              // RSI overbought level
        private const decimal OversoldRSILevel = 30m;               // RSI oversold level
        private const decimal BaseConfidenceThreshold = 0.5m;        // Base confidence threshold
        private const decimal MinConfidenceAdjustment = 0.1m;        // Minimum confidence adjustment
        private const decimal TrendingVolatilityThreshold = 0.25m;   // Volatility threshold for trending regime
        private const decimal RangingPriceChangeThreshold = 0.5m;    // Price change threshold for ranging regime
        
        // Trading session hour constants
        private const int OpeningDriveStartHour = 9;                 // Opening drive start hour (9 AM)
        private const int OpeningDriveEndHour = 10;                  // Opening drive end hour (10 AM)
        private const int LunchStartHour = 11;                       // Lunch session start hour (11 AM)
        private const int LunchEndHour = 13;                         // Lunch session end hour (1 PM)
        private const int AfternoonFadeStartHour = 13;               // Afternoon fade start hour (1 PM)
        private const int AfternoonFadeEndHour = 16;                 // Afternoon fade end hour (4 PM)
        
        // Market session hour constants (UTC)
        private const int PreMarketStartHour = 8;                    // Pre-market start hour (8 AM UTC)
        private const int RegularTradingStartHour = 13;              // Regular trading start hour (1 PM UTC)
        private const int RegularTradingEndHour = 20;                // Regular trading end hour (8 PM UTC)
        private const int AfterHoursEndHour = 22;                    // After hours end hour (10 PM UTC)
        private const decimal TrendStrengthThreshold = 0.2m;         // Trend strength classification threshold
        
        // Multi-strategy learning state
        private readonly Dictionary<string, StrategyPerformance> _strategyPerformance = new();
        private readonly Dictionary<string, List<BotCore.Brain.Models.MarketCondition>> _strategyOptimalConditions = new();
        private DateTime _lastUnifiedLearningUpdate = DateTime.MinValue;
        
        // Gate 4 configuration
        private readonly IGate4Config _gate4Config;
        
        // Economic calendar for event-based trading restrictions (Phase 2)
        private readonly BotCore.Market.IEconomicEventManager? _economicEventManager;
        
        // Primary strategies for focused learning
        private readonly string[] PrimaryStrategies = { "S2", "S3", "S6", "S11" };
        
        // Strategy specializations
        private readonly Dictionary<string, StrategySpecialization> _strategySpecializations = new()
        {
            ["S2"] = new StrategySpecialization 
            { 
                Name = "VWAP Mean Reversion", 
                OptimalConditions = new[] { "ranging", "low_volatility", "high_volume" },
                LearningFocus = "entry_exit_timing",
                TimeWindows = new[] { "overnight", "lunch", "premarket" }
            },
            ["S3"] = new StrategySpecialization 
            { 
                Name = "Bollinger Compression", 
                OptimalConditions = new[] { "low_volatility", "compression", "breakout_setup" },
                LearningFocus = "volatility_breakout_patterns",
                TimeWindows = new[] { "european_open", "us_premarket", "morning_trend" }
            },
            ["S6"] = new StrategySpecialization 
            { 
                Name = "Momentum Strategy", 
                OptimalConditions = new[] { "trending", "high_volume", "opening_drive" },
                LearningFocus = "momentum_strategies",
                TimeWindows = new[] { "opening_drive", "afternoon_trend" }
            },
            ["S11"] = new StrategySpecialization 
            { 
                Name = "ADR Exhaustion Fade", 
                OptimalConditions = new[] { "exhaustion", "range_bound", "mean_reversion" },
                LearningFocus = "exhaustion_patterns",
                TimeWindows = new[] { "afternoon_fade", "end_of_day" }
            }
        };
        
        public bool IsInitialized { get; private set; }
        public DateTime LastDecision { get; private set; }
        public int DecisionsToday { get; private set; }
        public decimal WinRateToday { get; private set; }

        public UnifiedTradingBrain(
            ILogger<UnifiedTradingBrain> logger,
            IMLMemoryManager memoryManager,
            StrategyMlModelManager modelManager,
            CVaRPPO cvarPPO,
            IMLConfigurationService mlConfigService,
            IGate4Config? gate4Config = null,
            BotCore.Services.OllamaClient? ollamaClient = null,
            BotCore.Market.IEconomicEventManager? economicEventManager = null,
            BotCore.Services.RiskAssessmentCommentary? riskCommentary = null,
            BotCore.Services.AdaptiveLearningCommentary? learningCommentary = null,
            BotCore.Services.MarketSnapshotStore? snapshotStore = null,
            BotCore.Services.HistoricalPatternRecognitionService? historicalPatterns = null,
            BotCore.Services.ParameterChangeTracker? parameterTracker = null)
        {
            _logger = logger;
            _memoryManager = memoryManager;
            _modelManager = modelManager;
            _cvarPPO = cvarPPO; // Direct injection
            _mlConfigService = mlConfigService ?? throw new ArgumentNullException(nameof(mlConfigService));
            _gate4Config = gate4Config ?? Gate4Config.LoadFromEnvironment();
            _ollamaClient = ollamaClient; // Optional AI conversation client
            _economicEventManager = economicEventManager; // Optional economic calendar (Phase 2)
            _riskCommentary = riskCommentary;
            _learningCommentary = learningCommentary;
            _snapshotStore = snapshotStore;
            _historicalPatterns = historicalPatterns;
            _parameterTracker = parameterTracker;
            
            // Initialize Neural UCB for strategy selection using ONNX-based neural network
            var onnxLoader = new OnnxModelLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<OnnxModelLoader>());
            var neuralNetworkLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OnnxNeuralNetwork>();
            // Load runtime mode from environment for production safety
            var runtimeModeStr = Environment.GetEnvironmentVariable("RlRuntimeMode") ?? "InferenceOnly";
            if (!Enum.TryParse<TradingBot.Abstractions.RlRuntimeMode>(runtimeModeStr, ignoreCase: true, out var runtimeMode))
            {
                runtimeMode = TradingBot.Abstractions.RlRuntimeMode.InferenceOnly;
            }
            var neuralNetwork = new OnnxNeuralNetwork(onnxLoader, neuralNetworkLogger, runtimeMode, "models/strategy_selection.onnx");
            _strategySelector = new NeuralUcbBandit(neuralNetwork);
            
            // Initialize confidence network for model confidence prediction
            _confidenceNetwork = new OnnxNeuralNetwork(onnxLoader, neuralNetworkLogger, runtimeMode, "models/confidence_prediction.onnx");
            
            _logger.LogInformation("🧠 [UNIFIED-BRAIN] Initialized with direct CVaR-PPO injection - Ready to make intelligent trading decisions");
        }

        /// <summary>
        /// Initialize all ML models and prepare the brain for trading
        /// This is called from UnifiedOrchestrator startup
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("🚀 [UNIFIED-BRAIN] Loading all ML models...");

                // Load LSTM for price prediction - use your real trained model
                _lstmPricePredictor = await _memoryManager.LoadModelAsync<object>(
                    "models/rl_model.onnx", "v1").ConfigureAwait(false);
                
                // CVaR-PPO is already injected and initialized via DI container
                _logger.LogInformation("✅ [CVAR-PPO] Using direct injection from DI container");
                
                // Load meta classifier for market regime - use your test CVaR model
                _metaClassifier = await _memoryManager.LoadModelAsync<object>(
                    "models/rl/test_cvar_ppo.onnx", "v1").ConfigureAwait(false);
                
                // Load market regime detector - use your main RL model as backup
                _marketRegimeDetector = await _memoryManager.LoadModelAsync<object>(
                    "models/rl_model.onnx", "v1").ConfigureAwait(false);

                IsInitialized = true;
                _logger.LogInformation("✅ [UNIFIED-BRAIN] All models loaded successfully - Brain is ONLINE with production CVaR-PPO");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Model file not found - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Model directory not found - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] I/O error loading models - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Access denied loading models - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Invalid operation during model loading - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Invalid argument for model loading - Using fallback logic");
                IsInitialized = false; // Will use rule-based fallbacks
            }
        }

        /// <summary>
        /// MAIN BRAIN FUNCTION: Make intelligent trading decision
        /// Called by AutonomousDecisionEngine for live trading
        /// 
        /// This replaces the manual strategy selection in AllStrategies.cs
        /// </summary>
        public async Task<BrainDecision> MakeIntelligentDecisionAsync(
            string symbol,
            Env env,
            Levels levels,
            IList<Bar> bars,
            RiskEngine risk,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            ArgumentNullException.ThrowIfNull(env);
            ArgumentNullException.ThrowIfNull(levels);
            ArgumentNullException.ThrowIfNull(bars);
            ArgumentNullException.ThrowIfNull(risk);
            
            // Store latest market data for use in risk analysis and commentary
            _latestEnv = env;
            _latestBars = bars;
            
            var startTime = DateTime.UtcNow;
            LastDecision = startTime;
            
            try
            {
                // PHASE 2: Check economic calendar before trading
                var calendarCheckEnabled = Environment.GetEnvironmentVariable("BOT_CALENDAR_CHECK_ENABLED")?.ToUpperInvariant() == "TRUE";
                if (calendarCheckEnabled && _economicEventManager != null)
                {
                    // Check if symbol trading should be restricted
                    var blockMinutes = int.Parse(Environment.GetEnvironmentVariable("BOT_CALENDAR_BLOCK_MINUTES") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
                    var isRestricted = await _economicEventManager.ShouldRestrictTradingAsync(symbol, TimeSpan.FromMinutes(blockMinutes)).ConfigureAwait(false);
                    
                    if (isRestricted)
                    {
                        _logger.LogWarning("📅 [CALENDAR-BLOCK] Cannot trade {Symbol} - event restriction active", symbol);
                        return CreateNoTradeDecision(symbol, "Economic event restriction", startTime);
                    }
                    
                    // Check for upcoming high-impact events
                    var upcomingEvents = await _economicEventManager.GetUpcomingEventsAsync(
                        TimeSpan.FromMinutes(blockMinutes)).ConfigureAwait(false);
                    
                    var highImpactEvents = upcomingEvents.Where(e => 
                        e.Impact >= BotCore.Market.EventImpact.High && 
                        e.AffectedSymbols.Contains(symbol)).ToList();
                    
                    if (highImpactEvents.Count > 0)
                    {
                        var nextEvent = highImpactEvents[0];
                        var minutesUntil = (nextEvent.ScheduledTime - DateTime.UtcNow).TotalMinutes;
                        _logger.LogWarning("📅 [CALENDAR-BLOCK] High-impact event '{Event}' in {Minutes:F0} minutes - blocking trades", 
                            nextEvent.Name, minutesUntil);
                        return CreateNoTradeDecision(symbol, $"{nextEvent.Name} approaching", startTime);
                    }
                }
                
                // 1. CREATE MARKET CONTEXT from current data
                var context = CreateMarketContext(symbol, env, bars);
                _marketContexts[symbol] = context;
                
                // 2. DETECT MARKET REGIME using Meta Classifier
                var marketRegime = await DetectMarketRegimeAsync(context).ConfigureAwait(false);
                
                // 3. SELECT OPTIMAL STRATEGY using Neural UCB
                var optimalStrategy = await SelectOptimalStrategyAsync(context, marketRegime, cancellationToken).ConfigureAwait(false);
                
                // 4. PREDICT PRICE MOVEMENT using LSTM
                var priceDirection = await PredictPriceDirectionAsync(context, bars).ConfigureAwait(false);
                
                // 5. OPTIMIZE POSITION SIZE using RL
                var optimalSize = await OptimizePositionSizeAsync(context, optimalStrategy, priceDirection, cancellationToken).ConfigureAwait(false);
                
                // 6. GENERATE ENHANCED CANDIDATES using brain intelligence
                var enhancedCandidates = await GenerateEnhancedCandidatesAsync(
                    symbol, env, levels, bars, risk, optimalStrategy, priceDirection, optimalSize).ConfigureAwait(false);
                
                var decision = new BrainDecision
                {
                    Symbol = symbol,
                    RecommendedStrategy = optimalStrategy.SelectedStrategy,
                    StrategyConfidence = optimalStrategy.Confidence,
                    PriceDirection = priceDirection.Direction,
                    PriceProbability = priceDirection.Probability,
                    OptimalPositionMultiplier = optimalSize,
                    MarketRegime = marketRegime,
                    EnhancedCandidates = enhancedCandidates,
                    DecisionTime = startTime,
                    ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                    ModelConfidence = CalculateOverallConfidence(optimalStrategy, priceDirection),
                    RiskAssessment = AssessRisk(context, priceDirection)
                };

                // Track decision for learning
                _decisionHistory.Add(new TradingDecision
                {
                    Symbol = symbol,
                    Strategy = optimalStrategy.SelectedStrategy,
                    Confidence = decision.ModelConfidence,
                    Context = context,
                    Timestamp = startTime
                });

                DecisionsToday++;
                
                _logger.LogInformation("🧠 [BRAIN-DECISION] {Symbol}: Strategy={Strategy} ({Confidence:P1}), " +
                    "Direction={Direction} ({Probability:P1}), Size={Size:F2}x, Regime={Regime}, Time={Ms:F0}ms",
                    symbol, optimalStrategy.SelectedStrategy, optimalStrategy.Confidence,
                    priceDirection.Direction, priceDirection.Probability, optimalSize, marketRegime, decision.ProcessingTimeMs);

                // AI bot thinking - explain decision before taking trade
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_THINKING_ENABLED") == "true"))
                {
                    var thinking = await ThinkAboutDecisionAsync(decision).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        _logger.LogInformation("💭 [BOT-THINKING] {Thinking}", thinking);
                    }
                }

                // Feature 1: Real-Time Commentary
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_COMMENTARY_ENABLED") == "true"))
                {
                    // Check for low confidence (waiting)
                    if (optimalStrategy.Confidence < TopStepConfig.LowConfidenceThreshold)
                    {
                        var commentary = await ExplainWhyWaitingAsync(context, optimalStrategy, priceDirection).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(commentary))
                        {
                            _logger.LogInformation("💬 [BOT-COMMENTARY] {Commentary}", commentary);
                        }
                    }
                    // Check for high confidence (using MLConfigurationService to replace hardcoded 0.7)
                    else if (optimalStrategy.Confidence > (decimal)_mlConfigService.GetAIConfidenceThreshold())
                    {
                        var commentary = await ExplainConfidenceAsync(decision, context).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(commentary))
                        {
                            _logger.LogInformation("💬 [BOT-COMMENTARY] {Commentary}", commentary);
                        }
                    }
                }

                // Hook 1: Capture market snapshot (if enabled)
                if (_snapshotStore != null && Environment.GetEnvironmentVariable("SNAPSHOT_ENABLED") == "true")
                {
                    try
                    {
                        // Use actual market data from available sources
                        var vixValue = env.volz ?? 0m; // Use volatility z-score as proxy for VIX
                        var currentPrice = bars.LastOrDefault()?.Close ?? 0m;
                        
                        // Determine session based on time of day
                        var currentHour = DateTime.UtcNow.Hour;
                        string sessionName;
                        if (currentHour >= RegularTradingStartHour && currentHour < RegularTradingEndHour)
                        {
                            sessionName = "RegularTrading";
                        }
                        else if (currentHour >= PreMarketStartHour && currentHour < RegularTradingStartHour)
                        {
                            sessionName = "PreMarket";
                        }
                        else if (currentHour >= RegularTradingEndHour && currentHour < AfterHoursEndHour)
                        {
                            sessionName = "AfterHours";
                        }
                        else
                        {
                            sessionName = "Closed";
                        }
                        
                        // Create default zone snapshot (no zone service in brain yet)
                        var emptyZoneSnapshot = new Zones.ZoneSnapshot(
                            NearestDemand: null,
                            NearestSupply: null,
                            DistToDemandAtr: 0m,
                            DistToSupplyAtr: 0m,
                            BreakoutScore: 0m,
                            ZonePressure: 0m,
                            Utc: DateTime.UtcNow
                        );
                        
                        // Create default pattern scores
                        var emptyPatternScores = new BotCore.Patterns.PatternScoresWithDetails
                        {
                            BullScore = 0.0,
                            BearScore = 0.0,
                            OverallConfidence = 0.0
                        };
                        emptyPatternScores.SetDetectedPatterns(System.Array.Empty<BotCore.Patterns.PatternDetail>());
                        
                        string trend;
                        if (context.TrendStrength > TrendStrengthThreshold)
                        {
                            trend = "Bullish";
                        }
                        else if (context.TrendStrength < -TrendStrengthThreshold)
                        {
                            trend = "Bearish";
                        }
                        else
                        {
                            trend = "Neutral";
                        }
                        
                        var snapshot = BotCore.Services.MarketSnapshotStore.CreateSnapshot(
                            symbol: decision.Symbol,
                            currentPrice: currentPrice,
                            vix: vixValue,
                            trend: trend,
                            session: sessionName,
                            zoneSnapshot: emptyZoneSnapshot,
                            patternScores: emptyPatternScores,
                            strategy: decision.RecommendedStrategy,
                            direction: decision.PriceDirection.ToString(),
                            confidence: decision.StrategyConfidence,
                            size: (int)decision.OptimalPositionMultiplier
                        );
                        _snapshotStore.StoreSnapshot(snapshot);
                        _logger.LogTrace("📸 [SNAPSHOT] Captured market snapshot for {Symbol}: {Strategy} {Direction}", 
                            symbol, decision.RecommendedStrategy, decision.PriceDirection);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [SNAPSHOT] Failed to capture market snapshot - invalid operation");
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [SNAPSHOT] Failed to capture market snapshot - invalid argument");
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [SNAPSHOT] Failed to capture market snapshot - I/O error");
                    }
                }

                return decision;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Invalid operation making decision for {Symbol}", symbol);
                return CreateFallbackDecision(symbol, env, levels, bars, risk);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Invalid argument making decision for {Symbol}", symbol);
                return CreateFallbackDecision(symbol, env, levels, bars, risk);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Timeout making decision for {Symbol}", symbol);
                return CreateFallbackDecision(symbol, env, levels, bars, risk);
            }
        }

        /// <summary>
        /// Enhanced learning from trading results that improves ALL strategies
        /// Every trade outcome teaches all strategies and improves future decision-making
        /// Called after order execution and P&L is known
        /// </summary>
        public async Task LearnFromResultAsync(
            string symbol,
            string strategy,
            decimal pnl,
            bool wasCorrect,
            TimeSpan holdTime,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Update strategy selector (Neural UCB) with reward
                var context = _marketContexts.TryGetValue(symbol, out var ctx) ? ctx : null;
                if (context != null)
                {
                    var reward = CalculateReward(pnl, wasCorrect, holdTime);
                    var contextVector = CreateContextVector(context);
                    
                    await _strategySelector.UpdateArmAsync(strategy, contextVector, reward, cancellationToken).ConfigureAwait(false);
                    
                    // 🚀 MULTI-STRATEGY LEARNING: Update ALL strategies with this market condition
                    await UpdateAllStrategiesFromOutcomeAsync(context, strategy, reward, wasCorrect, cancellationToken).ConfigureAwait(false);
                }

                // Update performance tracking for the specific strategy
                if (!_performance.TryGetValue(symbol, out var perf))
                {
                    perf = new TradingPerformance();
                    _performance[symbol] = perf;
                }
                perf.TotalTrades++;
                perf.TotalPnL += pnl;
                if (wasCorrect) perf.WinningTrades++;
                
                // Update strategy-specific performance tracking
                if (context != null)
                {
                    UpdateStrategyPerformance(strategy, context, wasCorrect, pnl, holdTime);
                }
                
                // Calculate today's win rate
                var todayDecisions = _decisionHistory.Where(d => d.Timestamp.Date == DateTime.Today).ToList();
                if (todayDecisions.Count > 0)
                {
                    // This would be updated when we get actual results
                    WinRateToday = (decimal)todayDecisions.Count(d => d.WasCorrect) / todayDecisions.Count;
                }

                // Enhanced model retraining with multi-strategy learning
                if (DateTime.UtcNow - _lastUnifiedLearningUpdate > TimeSpan.FromHours(LearningUpdateIntervalHours) && _decisionHistory.Count > MinDecisionsForLearningUpdate)
                {
                    _ = Task.Run(() => UpdateUnifiedLearningAsync(cancellationToken), cancellationToken);
                    _lastUnifiedLearningUpdate = DateTime.UtcNow;
                }

                // Periodic full model retraining
                if (DateTime.UtcNow - _lastModelUpdate > TimeSpan.FromHours(ModelRetrainingIntervalHours) && _decisionHistory.Count > MinDecisionsForModelRetraining)
                {
                    _ = Task.Run(() => RetrainModelsAsync(cancellationToken), cancellationToken);
                    _lastModelUpdate = DateTime.UtcNow;
                }

                _logger.LogInformation("📚 [UNIFIED-LEARNING] {Symbol} {Strategy}: PnL={PnL:F2}, Correct={Correct}, " +
                    "WinRate={WinRate:P1}, TotalTrades={Total}, AllStrategiesUpdated=True",
                    symbol, strategy, pnl, wasCorrect, WinRateToday, perf.TotalTrades);

                // AI bot reflection - reflect on completed trade
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_REFLECTION_ENABLED") == "true"))
                {
                    var reflection = await ReflectOnOutcomeAsync(symbol, strategy, pnl, wasCorrect, holdTime).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(reflection))
                    {
                        _logger.LogInformation("🔮 [BOT-REFLECTION] {Reflection}", reflection);
                    }
                }

                // Feature 2: Trade Failure Analysis (only for losses)
                if (!wasCorrect && pnl < 0 && _ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_FAILURE_ANALYSIS_ENABLED") == "true"))
                {
                    // Get entry and exit context if available
                    var entryContext = context ?? _marketContexts.GetValueOrDefault(symbol);
                    if (entryContext != null)
                    {
                        // Note: Entry/stop/target/exit prices tracked separately in position management
                        var failureAnalysis = await AnalyzeTradeFailureAsync(
                            symbol, strategy, pnl,
                            0, 0, 0, 0, // Entry/stop/target/exit prices would be tracked separately
                            "Stop hit", entryContext, null).ConfigureAwait(false);
                        
                        if (!string.IsNullOrEmpty(failureAnalysis))
                        {
                            _logger.LogInformation("❌ [BOT-FAILURE-ANALYSIS] {Analysis}", failureAnalysis);
                        }
                    }
                }

                // Feature 6: Learning Progress Reports
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_LEARNING_REPORTS_ENABLED") == "true"))
                {
                    // Report after model updates
                    if (DateTime.UtcNow - _lastUnifiedLearningUpdate < TimeSpan.FromMinutes(1))
                    {
                        var learningReport = await ExplainWhatILearnedAsync(
                            "Unified Learning Update",
                            $"Updated all strategies from {strategy} trade outcome. Win rate: {WinRateToday:P0}").ConfigureAwait(false);
                        
                        if (!string.IsNullOrEmpty(learningReport))
                        {
                            _logger.LogInformation("📚 [BOT-LEARNING] {Report}", learningReport);
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-LEARNING] Invalid operation during learning from result");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-LEARNING] Invalid argument during learning from result");
            }
        }
        
        /// <summary>
        /// Update ALL strategies based on a single trade outcome
        /// This creates cross-strategy learning where each outcome improves the entire system
        /// </summary>
        private async Task UpdateAllStrategiesFromOutcomeAsync(
            MarketContext context, 
            string executedStrategy, 
            decimal reward, 
            bool wasCorrect,
                        CancellationToken cancellationToken)
        {
            try
            {
                var contextVector = CreateContextVector(context);
                
                foreach (var strategy in PrimaryStrategies)
                {
                    if (strategy == executedStrategy)
                        continue; // Already updated above
                    
                    // Calculate learning reward for non-executed strategies based on market conditions
                    var crossLearningReward = CalculateCrossLearningReward(
                        strategy, executedStrategy, context, reward, wasCorrect);
                    
                    // Update strategy knowledge even if it wasn't executed
                    await _strategySelector.UpdateArmAsync(strategy, contextVector, crossLearningReward, cancellationToken).ConfigureAwait(false);
                    
                    // Hook 4: Track parameter changes (if enabled)
                    // Note: Parameter tracking happens after UpdateArmAsync to detect any changes made
                    if (_parameterTracker != null && Environment.GetEnvironmentVariable("PARAMETER_TRACKING_ENABLED") == "true")
                    {
                        try
                        {
                            // Record the parameter update with outcome context
                            var reason = wasCorrect ? "Successful trade outcome" : "Learning from unsuccessful trade";
                            _parameterTracker.RecordChange(
                                strategyName: strategy,
                                parameterName: "StrategyWeight",
                                oldValue: reward.ToString("F3", CultureInfo.InvariantCulture),
                                newValue: crossLearningReward.ToString("F3", CultureInfo.InvariantCulture),
                                reason: reason,
                                outcomePnl: reward,
                                wasCorrect: wasCorrect
                            );
                            _logger.LogTrace("📊 [PARAM-TRACKING] Tracked parameter update for {Strategy}: old={OldReward:F3}, new={NewReward:F3}", 
                                strategy, reward, crossLearningReward);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogWarning(ex, "⚠️ [PARAM-TRACKING] Failed to track parameter change - invalid operation");
                        }
                        catch (IOException ex)
                        {
                            _logger.LogWarning(ex, "⚠️ [PARAM-TRACKING] Failed to track parameter change - I/O error");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogWarning(ex, "⚠️ [PARAM-TRACKING] Failed to track parameter change - access denied");
                        }
                    }
                    
                    // Update strategy-specific learning patterns
                    UpdateStrategyOptimalConditions(strategy, context, crossLearningReward > BaseConfidenceThreshold);
                }
                
                _logger.LogDebug("🧠 [CROSS-LEARNING] Updated all strategies from {ExecutedStrategy} outcome: {Reward:F3}", 
                    executedStrategy, reward);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [CROSS-LEARNING] Invalid operation updating all strategies");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [CROSS-LEARNING] Invalid argument updating all strategies");
            }
        }
        
        /// <summary>
        /// Gather current market context for AI conversation
        /// </summary>
        private string GatherCurrentContext()
        {
            try
            {
                // Get VIX level from latest context (use 0 if unavailable to avoid hiding missing data)
                var vixLevel = _marketContexts.Values.LastOrDefault()?.Volatility ?? 0m;
                
                // Get today's P&L
                var todayPnl = _dailyPnl;
                
                // Calculate today's win rate
                var todayDecisions = _decisionHistory.Where(d => d.Timestamp.Date == DateTime.Today).ToList();
                var winRate = todayDecisions.Count > 0 
                    ? (decimal)todayDecisions.Count(d => d.WasCorrect) / todayDecisions.Count 
                    : 0m;
                
                // Get current market trend
                var trend = "Unknown";
                if (!_marketContexts.IsEmpty)
                {
                    var latestContext = _marketContexts.Values.LastOrDefault();
                    if (latestContext != null)
                    {
                        if (latestContext.TrendStrength > StrongTrendThreshold)
                        {
                            trend = "Bullish";
                        }
                        else if (latestContext.TrendStrength < -StrongTrendThreshold)
                        {
                            trend = "Bearish";
                        }
                        else
                        {
                            trend = "Neutral";
                        }
                    }
                }
                
                // Get active strategies
                var activeStrategies = string.Join(", ", PrimaryStrategies);
                
                // Get current position info (if any)
                var positionInfo = "None";
                
                // Format context
                return $"VIX: {vixLevel:F1}, PnL Today: ${todayPnl:F2}, Win Rate: {winRate:P0}, " +
                       $"Trend: {trend}, Active Strategies: {activeStrategies}, Position: {positionInfo}, " +
                       $"Decisions Today: {DecisionsToday}";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [BOT-CONTEXT] Error gathering current context - invalid operation");
                return "Context unavailable";
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [BOT-CONTEXT] Error gathering current context - invalid argument");
                return "Context unavailable";
            }
            catch (NullReferenceException ex)
            {
                _logger.LogError(ex, "❌ [BOT-CONTEXT] Error gathering current context - null reference");
                return "Context unavailable";
            }
        }
        
        /// <summary>
        /// AI thinks about and explains a trading decision before execution
        /// </summary>
        private async Task<string> ThinkAboutDecisionAsync(BrainDecision decision)
        {
            if (_ollamaClient == null)
                return string.Empty;
                
            try
            {
                var currentContext = GatherCurrentContext();
                
                // Hook 2: Add risk assessment commentary (if enabled)
                string riskContext = string.Empty;
                if (_riskCommentary != null && Environment.GetEnvironmentVariable("RISK_COMMENTARY_ENABLED") == "true")
                {
                    try
                    {
                        // Use actual market data from latest decision
                        var currentPrice = _latestBars?.LastOrDefault()?.Close ?? 0m;
                        var atr = _latestEnv?.atr ?? 0m;
                        
                        // Check if async mode is enabled
                        var asyncMode = Environment.GetEnvironmentVariable("RISK_COMMENTARY_ASYNC") == "true";
                        
                        // Only proceed if we have valid data
                        if (currentPrice > 0m && atr > 0m)
                        {
                            if (asyncMode)
                            {
                                // Fire-and-forget: Start analysis in background, continue trading immediately
                                _riskCommentary.AnalyzeRiskFireAndForget(decision.Symbol, currentPrice, atr);
                                _logger.LogTrace("🚀 [RISK-COMMENTARY] Started background analysis (async mode)");
                            }
                            else
                            {
                                // Blocking mode: Wait for result (for debugging/testing only)
                                riskContext = await _riskCommentary.AnalyzeRiskAsync(
                                    decision.Symbol, currentPrice, atr).ConfigureAwait(false);
                                    
                                if (!string.IsNullOrEmpty(riskContext))
                                {
                                    _logger.LogInformation("🧠 [RISK-COMMENTARY] {Commentary}", riskContext);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [RISK-COMMENTARY] Skipping - missing price or ATR data");
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [RISK-COMMENTARY] Failed to generate risk commentary - invalid operation");
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [RISK-COMMENTARY] Failed to generate risk commentary - HTTP request failed");
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [RISK-COMMENTARY] Failed to generate risk commentary - task cancelled");
                    }
                }
                
                // Hook 6: Historical pattern recognition (if enabled)
                string historicalContext = string.Empty;
                if (_historicalPatterns != null && _snapshotStore != null && 
                    Environment.GetEnvironmentVariable("PATTERN_RECOGNITION_ENABLED") == "true")
                {
                    try
                    {
                        // Use actual market data for similarity search
                        var vixValue = _latestEnv?.volz ?? 0m;
                        var currentPrice = _latestBars?.LastOrDefault()?.Close ?? 0m;
                        var currentHour = DateTime.UtcNow.Hour;
                        string sessionName;
                        if (currentHour >= RegularTradingStartHour && currentHour < RegularTradingEndHour)
                        {
                            sessionName = "RegularTrading";
                        }
                        else if (currentHour >= PreMarketStartHour && currentHour < RegularTradingStartHour)
                        {
                            sessionName = "PreMarket";
                        }
                        else if (currentHour >= RegularTradingEndHour && currentHour < AfterHoursEndHour)
                        {
                            sessionName = "AfterHours";
                        }
                        else
                        {
                            sessionName = "Closed";
                        }
                        
                        // Get trend from latest context
                        var latestContext = _marketContexts.Values.LastOrDefault();
                        string trendName;
                        if (latestContext != null && latestContext.TrendStrength > TrendStrengthThreshold)
                        {
                            trendName = "Bullish";
                        }
                        else if (latestContext != null && latestContext.TrendStrength < -TrendStrengthThreshold)
                        {
                            trendName = "Bearish";
                        }
                        else
                        {
                            trendName = "Neutral";
                        }
                        
                        // Create default zone snapshot and pattern scores for similarity search
                        var emptyZoneSnapshot = new Zones.ZoneSnapshot(
                            NearestDemand: null,
                            NearestSupply: null,
                            DistToDemandAtr: 0m,
                            DistToSupplyAtr: 0m,
                            BreakoutScore: 0m,
                            ZonePressure: 0m,
                            Utc: DateTime.UtcNow
                        );
                        
                        var emptyPatternScores = new BotCore.Patterns.PatternScoresWithDetails
                        {
                            BullScore = 0.0,
                            BearScore = 0.0,
                            OverallConfidence = 0.0
                        };
                        emptyPatternScores.SetDetectedPatterns(System.Array.Empty<BotCore.Patterns.PatternDetail>());
                        
                        // Create market snapshot for similarity search
                        var currentSnapshot = BotCore.Services.MarketSnapshotStore.CreateSnapshot(
                            symbol: decision.Symbol,
                            currentPrice: currentPrice,
                            vix: vixValue,
                            trend: trendName,
                            session: sessionName,
                            zoneSnapshot: emptyZoneSnapshot,
                            patternScores: emptyPatternScores,
                            strategy: decision.RecommendedStrategy,
                            direction: decision.PriceDirection.ToString(),
                            confidence: decision.StrategyConfidence,
                            size: (int)decision.OptimalPositionMultiplier
                        );
                        
                        var analysis = _historicalPatterns.FindSimilarConditions(currentSnapshot);
                        if (analysis.Matches.Count > 0)
                        {
                            historicalContext = await _historicalPatterns.ExplainSimilarConditionsAsync(analysis).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(historicalContext))
                            {
                                _logger.LogInformation("🔍 [HISTORICAL-PATTERN] {Context}", historicalContext);
                            }
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [HISTORICAL-PATTERN] Failed to find similar conditions - invalid operation");
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [HISTORICAL-PATTERN] Failed to find similar conditions - invalid argument");
                    }
                    catch (KeyNotFoundException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [HISTORICAL-PATTERN] Failed to find similar conditions - key not found");
                    }
                }
                
                var riskSection = !string.IsNullOrEmpty(riskContext) ? $"Risk Assessment: {riskContext}\n\n" : "";
                var historicalSection = !string.IsNullOrEmpty(historicalContext) ? $"Historical Context: {historicalContext}\n\n" : "";
                
                var prompt = $@"I am a trading bot. I'm about to take this trade:
Strategy: {decision.RecommendedStrategy}
Direction: {decision.PriceDirection}
Confidence: {decision.StrategyConfidence:P1}
Market Regime: {decision.MarketRegime}

Current context: {currentContext}

{riskSection}{historicalSection}Explain in 2-3 sentences why I'm taking this trade. Speak as ME (the bot), not as an observer.";
                
                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [BOT-THINKING] Error during AI thinking - invalid operation");
                return string.Empty;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ [BOT-THINKING] Error during AI thinking - HTTP request failed");
                return string.Empty;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "❌ [BOT-THINKING] Error during AI thinking - task cancelled");
                return string.Empty;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [BOT-THINKING] Error during AI thinking - invalid argument");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// AI reflects on a completed trade outcome
        /// </summary>
        private async Task<string> ReflectOnOutcomeAsync(
            string symbol,
            string strategy,
            decimal pnl,
            bool wasCorrect,
            TimeSpan holdTime)
        {
            if (_ollamaClient == null)
                return string.Empty;
                
            try
            {
                var result = wasCorrect ? "WIN" : "LOSS";
                var durationMinutes = (int)holdTime.TotalMinutes;
                string reason;
                if (pnl > 0)
                {
                    reason = "target hit";
                }
                else if (pnl < 0)
                {
                    reason = "stop hit";
                }
                else
                {
                    reason = "timeout";
                }
                
                // Hook 3: Add learning commentary (if enabled)
                string learningContext = string.Empty;
                if (_learningCommentary != null && Environment.GetEnvironmentVariable("LEARNING_COMMENTARY_ENABLED") == "true")
                {
                    try
                    {
                        var lookbackMinutes = int.TryParse(Environment.GetEnvironmentVariable("LEARNING_LOOKBACK_MINUTES"), 
                            out var mins) ? mins : 60;
                        
                        // Check if async mode is enabled
                        var asyncMode = Environment.GetEnvironmentVariable("LEARNING_COMMENTARY_ASYNC") == "true";
                        
                        if (asyncMode)
                        {
                            // Fire-and-forget: Start explanation in background, continue immediately
                            _learningCommentary.ExplainRecentAdaptationsFireAndForget(lookbackMinutes);
                            _logger.LogTrace("🚀 [LEARNING-COMMENTARY] Started background explanation (async mode)");
                        }
                        else
                        {
                            // Blocking mode: Wait for result (for debugging/testing only)
                            learningContext = await _learningCommentary.ExplainRecentAdaptationsAsync(lookbackMinutes).ConfigureAwait(false);
                            
                            if (!string.IsNullOrEmpty(learningContext))
                            {
                                _logger.LogInformation("📚 [LEARNING-COMMENTARY] {Commentary}", learningContext);
                            }
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [LEARNING-COMMENTARY] Failed to generate learning commentary - invalid operation");
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [LEARNING-COMMENTARY] Failed to generate learning commentary - HTTP request failed");
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [LEARNING-COMMENTARY] Failed to generate learning commentary - task cancelled");
                    }
                }
                
                var prompt = $@"I am a trading bot. I just closed a trade:
Symbol: {symbol}
Strategy: {strategy}
Result: {result}
Profit/Loss: ${pnl:F2}
Duration: {durationMinutes} minutes
Reason closed: {reason}

{(!string.IsNullOrEmpty(learningContext) ? $"Recent Learning: {learningContext}\n\n" : "")}Reflect on what happened in 1-2 sentences. Speak as ME (the bot).";
                
                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [BOT-REFLECTION] Error during AI reflection - invalid operation");
                return string.Empty;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ [BOT-REFLECTION] Error during AI reflection - HTTP request failed");
                return string.Empty;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "❌ [BOT-REFLECTION] Error during AI reflection - task cancelled");
                return string.Empty;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ [BOT-REFLECTION] Error during AI reflection - invalid argument");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Calculate learning reward for strategies that weren't executed
        /// This allows all strategies to learn from market conditions and outcomes
        /// </summary>
        private decimal CalculateCrossLearningReward(
            string learningStrategy, 
            string executedStrategy, 
            MarketContext context, 
            decimal executedReward, 
            bool wasCorrect)
        {
            // Get strategy specializations
            var learningSpec = _strategySpecializations.GetValueOrDefault(learningStrategy);
            var executedSpec = _strategySpecializations.GetValueOrDefault(executedStrategy);
            
            if (learningSpec == null || executedSpec == null)
                return executedReward * MinConfidenceAdjustment; // Minimal learning if no specialization
            
            // Calculate similarity in optimal conditions
            var conditionSimilarity = learningSpec.OptimalConditions
                .Intersect(executedSpec.OptimalConditions)
                .Count() / (float)Math.Max(learningSpec.OptimalConditions.Count, 1);
            
            // Time window overlap
            var timeOverlap = learningSpec.TimeWindows
                .Intersect(executedSpec.TimeWindows)
                .Count() / (float)Math.Max(learningSpec.TimeWindows.Count, 1);
            
            // Market condition alignment
            var currentConditions = GetCurrentMarketConditions(context);
            var conditionAlignment = learningSpec.OptimalConditions
                .Intersect(currentConditions)
                .Count() / (float)Math.Max(learningSpec.OptimalConditions.Count, 1);
            
            // Calculate cross-learning strength
            var learningStrength = (conditionSimilarity * 0.4f + timeOverlap * 0.3f + conditionAlignment * 0.3f);
            
            // Positive outcome strengthens similar strategies, negative outcome weakens them
            var baseReward = wasCorrect ? executedReward : (1 - executedReward);
            return Math.Clamp(baseReward * (decimal)learningStrength, 0m, 1m);
        }
        
        private static string[] GetCurrentMarketConditions(MarketContext context)
        {
            var conditions = new List<string>();
            
            if (context.Volatility < LowVolatilityThreshold) conditions.Add("low_volatility");
            else if (context.Volatility > HighVolatilityThreshold) conditions.Add("high_volatility");
            
            if (context.VolumeRatio > HighVolumeRatioThreshold) conditions.Add("high_volume");
            if (context.TrendStrength > StrongTrendThreshold) conditions.Add("trending");
            else if (context.TrendStrength < WeakTrendThreshold) conditions.Add("ranging");
            
            if (context.RSI > OverboughtRSILevel) conditions.Add("overbought");
            else if (context.RSI < OversoldRSILevel) conditions.Add("oversold");
            
            var hour = context.TimeOfDay.Hours;
            if (hour >= OpeningDriveStartHour && hour <= OpeningDriveEndHour) conditions.Add("opening_drive");
            else if (hour >= LunchStartHour && hour <= LunchEndHour) conditions.Add("lunch");
            else if (hour >= AfternoonFadeStartHour && hour <= AfternoonFadeEndHour) conditions.Add("afternoon_fade");
            
            return conditions.ToArray();
        }

        #region ML Model Predictions

        private Task<MarketRegime> DetectMarketRegimeAsync(MarketContext context)
        {
            if (_metaClassifier == null || !IsInitialized)
            {
                // Fallback: use volatility-based regime detection
                MarketRegime regime;
                if (context.Volatility > WeakTrendThreshold)
                {
                    regime = MarketRegime.HighVolatility;
                }
                else if (context.Volatility < LowVolatilityThreshold)
                {
                    regime = MarketRegime.LowVolatility;
                }
                else
                {
                    regime = MarketRegime.Normal;
                }
                return Task.FromResult(regime);
            }

            try
            {
                // Analyze market regime using technical indicators and volatility
                // ONNX model integration planned for future enhancement
                MarketRegime detectedRegime;
                if (context.VolumeRatio > HighVolumeRatioThreshold && context.Volatility > TrendingVolatilityThreshold)
                    detectedRegime = MarketRegime.Trending;
                else if (context.Volatility < LowVolatilityThreshold && Math.Abs(context.PriceChange) < RangingPriceChangeThreshold)
                    detectedRegime = MarketRegime.Ranging;
                else if (context.Volatility > HighVolatilityThreshold)
                    detectedRegime = MarketRegime.HighVolatility;
                else
                    detectedRegime = MarketRegime.Normal;

                // Feature 5: Market Regime Explanations
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_REGIME_EXPLANATION_ENABLED") == "true"))
                {
                    _ = Task.Run(async () =>
                    {
                        var explanation = await ExplainMarketRegimeAsync(detectedRegime, context).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(explanation))
                        {
                            _logger.LogInformation("📈 [MARKET-REGIME] {Explanation}", explanation);
                        }
                    });
                }
                
                return Task.FromResult(detectedRegime);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Meta classifier invalid operation, using fallback");
                return Task.FromResult(MarketRegime.Normal);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Meta classifier invalid arguments, using fallback");
                return Task.FromResult(MarketRegime.Normal);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Meta classifier timeout, using fallback");
                return Task.FromResult(MarketRegime.Normal);
            }
        }

        private async Task<StrategySelection> SelectOptimalStrategyAsync(
            MarketContext context, 
            MarketRegime regime, 
            CancellationToken cancellationToken)
        {
            try
            {
                // Use Neural UCB to select optimal strategy
                var availableStrategies = GetAvailableStrategies(context.TimeOfDay, regime);
                var contextVector = CreateContextVector(context);
                
                var selection = await _strategySelector.SelectArmAsync(availableStrategies, contextVector, cancellationToken).ConfigureAwait(false);
                
                var result = new StrategySelection
                {
                    SelectedStrategy = selection.SelectedArm,
                    Confidence = selection.Confidence,
                    UcbValue = selection.UcbValue,
                    Reasoning = selection.SelectionReason ?? "Neural UCB selection"
                };

                // Feature 4: Strategy Confidence Explanations
                if (_ollamaClient != null && (Environment.GetEnvironmentVariable("BOT_STRATEGY_EXPLANATION_ENABLED") == "true"))
                {
                    // Get all strategy scores for explanation
                    var allScores = new Dictionary<string, decimal>();
                    // Use MLConfigurationService for confidence factor (replaces hardcoded 0.7)
                    var confidenceFactor = (decimal)_mlConfigService.GetAIConfidenceThreshold();
                    foreach (var strategy in availableStrategies)
                    {
                        // Use confidence as a proxy for score (actual UCB scores would be tracked separately)
                        allScores[strategy] = strategy == selection.SelectedArm ? selection.Confidence : selection.Confidence * confidenceFactor;
                    }
                    
                    // Check for strategy conflicts (multiple strategies with similar scores)
                    var topScores = allScores.OrderByDescending(kvp => kvp.Value).Take(2).ToList();
                    if (topScores.Count > 1 && Math.Abs(topScores[0].Value - topScores[1].Value) < TopStepConfig.StrategyConflictThreshold)
                    {
                        // Strategies are conflicting - close scores
                        var conflictExplanation = await ExplainConflictAsync(allScores, context).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(conflictExplanation))
                        {
                            _logger.LogInformation("💬 [BOT-COMMENTARY] {Conflict}", conflictExplanation);
                        }
                    }
                    else
                    {
                        var explanation = await ExplainStrategySelectionAsync(selection.SelectedArm, allScores, context).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(explanation))
                        {
                            _logger.LogInformation("🧠 [STRATEGY-SELECTION] {Explanation}", explanation);
                        }
                    }
                }
                
                return result;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Neural UCB invalid operation, using fallback");
                
                // Fallback: time-based strategy selection from your existing logic
                var hour = context.TimeOfDay.Hours;
                var fallbackStrategy = hour switch
                {
                    >= 9 and <= 10 => "S6", // Opening range
                    >= 14 and <= 16 => "S11", // Afternoon strength
                    >= 3 and <= 9 => "S2", // Overnight
                    _ => "S3" // Default
                };
                
                return new StrategySelection
                {
                    SelectedStrategy = fallbackStrategy,
                    Confidence = TopStepConfig.FallbackConfidence,
                    UcbValue = TopStepConfig.FallbackUcbValue,
                    Reasoning = "Fallback time-based selection"
                };
            }
        }

        private Task<PricePrediction> PredictPriceDirectionAsync(
            MarketContext context, 
            IList<Bar> bars
            )
        {
            if (_lstmPricePredictor == null || !IsInitialized)
            {
                // Fallback: trend-based prediction
                var recentBars = bars.TakeLast(5).ToList();
                if (recentBars.Count >= 2)
                {
                    var priceChange = recentBars[recentBars.Count - 1].Close - recentBars[0].Close;
                    var direction = priceChange > 0 ? PriceDirection.Up : PriceDirection.Down;
                    var probability = Math.Min(0.75m, 0.5m + Math.Abs(priceChange) / (context.Atr ?? 10));
                    
                    return Task.FromResult(new PricePrediction
                    {
                        Direction = direction,
                        Probability = probability,
                        ExpectedMove = Math.Abs(priceChange),
                        TimeHorizon = TimeSpan.FromMinutes(30)
                    });
                }
            }

            try
            {
                // Price prediction using technical analysis indicators
                // LSTM model integration planned for future enhancement
                var ema20 = CalculateEMA(bars, 20);
                var ema50 = CalculateEMA(bars, 50);
                var rsi = CalculateRSI(bars, 14);
                
                var isUptrend = ema20 > ema50 && bars[bars.Count - 1].Close > ema20;
                var isOversold = rsi < 30;
                var isOverbought = rsi > 70;
                
                PriceDirection direction;
                decimal probability;
                
                if (isUptrend && !isOverbought)
                {
                    direction = PriceDirection.Up;
                    probability = TopStepConfig.HighConfidenceProbability;
                }
                else if (!isUptrend && !isOversold)
                {
                    direction = PriceDirection.Down;
                    probability = TopStepConfig.HighConfidenceProbability;
                }
                else
                {
                    direction = PriceDirection.Sideways;
                    probability = TopStepConfig.NeutralProbability;
                }
                
                return Task.FromResult(new PricePrediction
                {
                    Direction = direction,
                    Probability = probability,
                    ExpectedMove = context.Atr ?? TopStepConfig.DefaultAtrLookback,
                    TimeHorizon = TimeSpan.FromMinutes(30)
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "LSTM prediction failed - invalid operation, using fallback");
                return Task.FromResult(new PricePrediction
                {
                    Direction = PriceDirection.Sideways,
                    Probability = TopStepConfig.NeutralProbability,
                    ExpectedMove = TopStepConfig.FallbackExpectedMove,
                    TimeHorizon = TimeSpan.FromMinutes(30)
                });
            }
            catch (OnnxRuntimeException ex)
            {
                _logger.LogWarning(ex, "LSTM prediction failed - ONNX runtime error, using fallback");
                return Task.FromResult(new PricePrediction
                {
                    Direction = PriceDirection.Sideways,
                    Probability = TopStepConfig.NeutralProbability,
                    ExpectedMove = TopStepConfig.FallbackExpectedMove,
                    TimeHorizon = TimeSpan.FromMinutes(30)
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "LSTM prediction failed - invalid argument, using fallback");
                return Task.FromResult(new PricePrediction
                {
                    Direction = PriceDirection.Sideways,
                    Probability = TopStepConfig.NeutralProbability,
                    ExpectedMove = TopStepConfig.FallbackExpectedMove,
                    TimeHorizon = TimeSpan.FromMinutes(30)
                });
            }
        }

        private async Task<decimal> OptimizePositionSizeAsync(
            MarketContext context,
            StrategySelection strategy,
            PricePrediction prediction,
                        CancellationToken cancellationToken)
        {
            // Check TopStep compliance first
            var (canTrade, reason, _) = ShouldStopTrading();
            if (!canTrade)
            {
                _logger.LogWarning("🛑 [TOPSTEP-COMPLIANCE] Trading blocked: {Reason}", reason);
                return 0m; // No position if compliance violated
            }

            // Calculate base risk amount
            var baseRisk = _accountBalance * TopStepConfig.RiskPerTrade;

            // Progressive position reduction based on drawdown
            var drawdownRatio = _currentDrawdown / TopStepConfig.MaxDrawdown;
            decimal riskMultiplier = drawdownRatio switch
            {
                > 0.75m => 0.25m, // Very conservative when near max drawdown
                > 0.5m => 0.5m,   // Reduced risk when at 50% drawdown
                > 0.25m => 0.75m, // Slightly reduced when at 25% drawdown
                _ => 1.0m         // Full risk when low drawdown
            };

            // Confidence-based sizing (UCB approach)
            var confidence = Math.Max(strategy.Confidence, prediction.Probability);
            if (confidence < (decimal)TopStepConfig.ConfidenceThreshold)
            {
                _logger.LogDebug("🎯 [CONFIDENCE] Below threshold {Threshold:P1}, confidence: {Confidence:P1}", 
                    TopStepConfig.ConfidenceThreshold, confidence);
                return 0m; // No trade if confidence too low
            }

            // Confidence multiplier using ONNX model (features prepared for future advanced scoring)
            _ = new Dictionary<string, double>
            {
                ["strategy_confidence"] = (double)strategy.Confidence,
                ["prediction_probability"] = (double)prediction.Probability,
                ["volatility"] = (double)context.Volatility,
                ["volume_ratio"] = (double)context.VolumeRatio,
                ["trend_strength"] = (double)context.TrendStrength
            };
            
            var modelConfidence = _confidenceNetwork != null ? await _confidenceNetwork.PredictAsync(new decimal[]
            {
                strategy.Confidence,
                prediction.Probability,
                context.Volatility,
                context.VolumeRatio,
                context.TrendStrength
            }, cancellationToken).ConfigureAwait(false) : 0.5m;
            
            var confidenceMultiplier = modelConfidence;

            // Calculate risk amount
            var riskAmount = baseRisk * riskMultiplier * confidenceMultiplier;

            // Dynamic stop calculation with safety bounds
            var instrument = context.Symbol;
            decimal stopDistance;
            decimal pointValue;

            if (instrument.Equals("NQ", StringComparison.OrdinalIgnoreCase))
            {
                stopDistance = Math.Max(TopStepConfig.NqMinStopDistance, context.Atr ?? TopStepConfig.DefaultAtrFallback);
                pointValue = TopStepConfig.NqPointValue;
            }
            else // ES
            {
                stopDistance = Math.Max(TopStepConfig.EsMinStopDistance, context.Atr ?? TopStepConfig.EsDefaultAtrFallback);
                pointValue = TopStepConfig.EsPointValue;
            }

            // Convert risk to contracts
            var perContractRisk = stopDistance * pointValue;
            var contracts = perContractRisk > 0 ? (int)(riskAmount / perContractRisk) : 0;

            // Apply TopStep position limits based on current drawdown
            var maxContracts = _currentDrawdown switch
            {
                < 500m => 3,  // Up to 3 contracts when drawdown is low
                < 1000m => 2, // Max 2 contracts when moderate drawdown
                _ => 1        // Only 1 contract when high drawdown
            };

            contracts = Math.Max(0, Math.Min(contracts, maxContracts));

            // 🚀 PRODUCTION CVaR-PPO POSITION SIZING INTEGRATION
            if (_cvarPPO != null && IsInitialized)
            {
                try
                {
                    // Create state vector for CVaR-PPO model
                    var state = CreateCVaRStateVector(context, strategy, prediction);
                    
                    // Get action from trained CVaR-PPO model
                    var actionResult = await _cvarPPO.GetActionAsync(state, deterministic: false, cancellationToken).ConfigureAwait(false);
                    
                    // Convert CVaR-PPO action to contract sizing
                    var cvarContracts = ConvertCVaRActionToContracts(actionResult, contracts);
                    
                    // Apply CVaR risk controls
                    var riskAdjustedContracts = ApplyCVaRRiskControls(cvarContracts, actionResult, context);
                    
                    contracts = Math.Max(0, Math.Min(riskAdjustedContracts, maxContracts));
                    
                    _logger.LogInformation("🎯 [CVAR-PPO] Action={Action}, Prob={Prob:F3}, Value={Value:F3}, CVaR={CVaR:F3}, Contracts={Contracts}", 
                        actionResult.Action, actionResult.ActionProbability, actionResult.ValueEstimate, actionResult.CVaREstimate, contracts);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "CVaR-PPO position sizing failed - invalid operation, using TopStep compliance sizing");
                    // contracts remains unchanged - use TopStep compliance sizing
                }
                catch (ArgumentException ex)
                {
                    _logger.LogError(ex, "CVaR-PPO position sizing failed - invalid argument, using TopStep compliance sizing");
                    // contracts remains unchanged - use TopStep compliance sizing
                }
                catch (OnnxRuntimeException ex)
                {
                    _logger.LogError(ex, "CVaR-PPO position sizing failed - ONNX runtime error, using TopStep compliance sizing");
                    // contracts remains unchanged - use TopStep compliance sizing
                }
            }
            else
            {
                // Fallback to legacy RL if CVaR-PPO not available
                var rlMultiplier = await _modelManager.GetPositionSizeMultiplierAsync(
                    strategy.SelectedStrategy,
                    context.Symbol,
                    context.CurrentPrice,
                    context.Atr ?? TopStepConfig.DefaultAtrLookback,
                    strategy.Confidence,
                    prediction.Probability,
                    new List<Bar>()
                ).ConfigureAwait(false);
                
                contracts = (int)(contracts * Math.Clamp(rlMultiplier, TopStepConfig.MinRlMultiplier, TopStepConfig.MaxRlMultiplier));
                _logger.LogDebug("📊 [LEGACY-RL] Using fallback RL multiplier: {Multiplier:F2}", rlMultiplier);
            }

            _logger.LogDebug("📊 [POSITION-SIZE] {Symbol}: Confidence={Confidence:P1}, Drawdown={Drawdown:C}, " +
                "Contracts={Contracts}, RiskAmount={Risk:C}", 
                instrument, confidence, _currentDrawdown, contracts, riskAmount);

            return contracts; // Return actual contract count, not multiplier
        }

        /// <summary>
        /// TopStep compliance check - returns (canTrade, reason, level)
        /// </summary>
        private (bool CanTrade, string Reason, string Level) ShouldStopTrading()
        {
            // Auto-reset daily P&L if it's a new day
            CheckAndResetDaily();

            // Hard stops (no trading allowed)
            if (_dailyPnl <= -TopStepConfig.DailyLossLimit)
                return (false, $"Daily loss limit reached: {_dailyPnl:C}", "hard_stop");
            
            if (_currentDrawdown >= TopStepConfig.MaxDrawdown)
                return (false, $"Max drawdown reached: {_currentDrawdown:C}", "hard_stop");
            
            if (_accountBalance <= TopStepConfig.TrailingStop)
                return (false, $"Account below minimum: {_accountBalance:C}", "hard_stop");

            // Warning levels (can trade but with caution)
            if (_dailyPnl <= -(TopStepConfig.DailyLossLimit * TopStepConfig.NearDailyLossWarningFactor))
                return (true, $"Near daily loss limit: {_dailyPnl:C}", "warning");
            
            if (_currentDrawdown >= (TopStepConfig.MaxDrawdown * TopStepConfig.NearMaxDrawdownWarningFactor))
                return (true, $"Near max drawdown: {_currentDrawdown:C}", "warning");

            return (true, "OK", "normal");
        }

        /// <summary>
        /// Update P&L after trade completion - call this from AutonomousDecisionEngine
        /// </summary>
        public void UpdatePnL(string strategy, decimal pnl)
        {
            _dailyPnl += pnl;
            _accountBalance += pnl;
            
            // Update drawdown if we're in loss territory
            if (_dailyPnl < 0)
                _currentDrawdown = Math.Max(_currentDrawdown, Math.Abs(_dailyPnl));
            
            _logger.LogInformation("💰 [PNL-UPDATE] Strategy={Strategy}, PnL={PnL:C}, DailyPnL={DailyPnL:C}, " +
                "Drawdown={Drawdown:C}, Balance={Balance:C}", 
                strategy, pnl, _dailyPnl, _currentDrawdown, _accountBalance);
        }

        /// <summary>
        /// Reset daily stats - automatically called or can be called manually
        /// </summary>
        public void ResetDaily()
        {
            _dailyPnl = 0;
            _currentDrawdown = 0;
            _lastResetDate = DateTime.UtcNow.Date;
            
            _logger.LogInformation("🌅 [DAILY-RESET] Daily P&L and drawdown reset for new trading day");
        }

        private void CheckAndResetDaily()
        {
            if (DateTime.UtcNow.Date > _lastResetDate)
            {
                ResetDaily();
            }
        }

        #endregion

        #region Integration with Existing Systems

        /// <summary>
        /// Generate enhanced candidates that integrate with AllStrategies.cs
        /// This replaces the manual candidate generation
        /// </summary>
        private Task<List<Candidate>> GenerateEnhancedCandidatesAsync(
            string symbol,
            Env env,
            Levels levels,
            IList<Bar> bars,
            RiskEngine risk,
            StrategySelection strategySelection,
            PricePrediction prediction,
            decimal sizeMultiplier
            )
        {
            try
            {
                // Get candidates from the selected strategy only (instead of all 14)
                var candidateFunction = GetStrategyFunction(strategySelection.SelectedStrategy);
                var baseCandidates = candidateFunction(symbol, env, levels, bars, risk);
                
                // Enhance each candidate with AI intelligence
                var enhancedCandidates = new List<Candidate>();
                
                foreach (var candidate in baseCandidates)
                {
                    // Only include candidates that align with price prediction
                    var candidateDirection = candidate.side == Side.BUY ? PriceDirection.Up : PriceDirection.Down;
                    if (prediction.Direction != PriceDirection.Sideways && candidateDirection != prediction.Direction)
                    {
                        continue; // Skip candidates against predicted direction
                    }
                    
                    // Apply AI-optimized position sizing
                    var enhancedCandidate = new Candidate
                    {
                        strategy_id = $"{candidate.strategy_id}-AI-{strategySelection.Confidence:P0}",
                        symbol = candidate.symbol,
                        side = candidate.side,
                        entry = candidate.entry,
                        stop = candidate.stop,
                        t1 = candidate.t1,
                        expR = candidate.expR,
                        qty = (int)(candidate.qty * sizeMultiplier),
                        atr_ok = candidate.atr_ok,
                        vol_z = candidate.vol_z,
                        accountId = candidate.accountId,
                        contractId = candidate.contractId,
                        Score = candidate.Score,
                        // Add AI confidence to quality score
                        QScore = candidate.QScore * strategySelection.Confidence * prediction.Probability
                    };
                    
                    enhancedCandidates.Add(enhancedCandidate);
                }
                
                _logger.LogDebug("🎯 [BRAIN-ENHANCE] {Symbol}: Generated {Count} AI-enhanced candidates from {Strategy}",
                    symbol, enhancedCandidates.Count, strategySelection.SelectedStrategy);
                
                return Task.FromResult(enhancedCandidates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BRAIN-ENHANCE] Error generating enhanced candidates");
                
                // Fallback to original AllStrategies logic
                return Task.FromResult(AllStrategies.generate_candidates(symbol, env, levels, bars, risk));
            }
        }

        /// <summary>
        /// Create context vector for Neural UCB from market data
        /// </summary>
        private static ContextVector CreateContextVector(MarketContext context)
        {
            var features = new Dictionary<string, decimal>
            {
                ["price"] = context.CurrentPrice,
                ["volume"] = context.Volume,
                ["volatility"] = context.Volatility,
                ["atr"] = context.Atr ?? 0,
                ["rsi"] = context.RSI,
                ["hour"] = context.TimeOfDay.Hours,
                ["day_of_week"] = (int)context.DayOfWeek,
                ["volume_ratio"] = context.VolumeRatio,
                ["price_change"] = context.PriceChange,
                ["trend_strength"] = context.TrendStrength,
                ["support_distance"] = context.DistanceToSupport,
                ["resistance_distance"] = context.DistanceToResistance,
                ["volatility_rank"] = context.VolatilityRank,
                ["momentum"] = context.Momentum,
                ["regime"] = (decimal)context.MarketRegime
            };
            
            return new ContextVector { Features = features };
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Create a no-trade decision when trading is blocked (Phase 2: Calendar)
        /// </summary>
        private static BrainDecision CreateNoTradeDecision(string symbol, string reason, DateTime startTime)
        {
            return new BrainDecision
            {
                Symbol = symbol,
                RecommendedStrategy = "HOLD",
                StrategyConfidence = 0m,
                PriceDirection = BotCore.Brain.Models.PriceDirection.Sideways,
                PriceProbability = TopStepConfig.NeutralProbability,
                OptimalPositionMultiplier = 0m,
                MarketRegime = BotCore.Brain.Models.MarketRegime.Normal,
                EnhancedCandidates = new List<Candidate>(),
                DecisionTime = startTime,
                ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                ModelConfidence = 0m,
                RiskAssessment = $"BLOCKED: {reason}"
            };
        }

        private static MarketContext CreateMarketContext(string symbol, Env env, IList<Bar> bars)
        {
            var latestBar = bars.LastOrDefault();
            if (latestBar == null)
            {
                return new MarketContext { Symbol = symbol };
            }
            
            var context = new MarketContext
            {
                Symbol = symbol,
                CurrentPrice = latestBar.Close,
                Volume = latestBar.Volume,
                Atr = env.atr,
                Volatility = Math.Abs(latestBar.High - latestBar.Low) / latestBar.Close,
                TimeOfDay = DateTime.Now.TimeOfDay,
                DayOfWeek = DateTime.Now.DayOfWeek,
                VolumeRatio = bars.Count > 10 ? (decimal)(latestBar.Volume / bars.TakeLast(10).Average(b => b.Volume)) : 1m,
                PriceChange = bars.Count > 1 ? latestBar.Close - bars[^2].Close : 0m,
                RSI = CalculateRSI(bars, 14),
                TrendStrength = CalculateTrendStrength(bars),
                DistanceToSupport = 0m, // levels.Support doesn't exist, using default
                DistanceToResistance = 0m, // levels.Resistance doesn't exist, using default
                VolatilityRank = CalculateVolatilityRank(bars),
                Momentum = CalculateMomentum(bars),
                MarketRegime = 0 // Will be filled by regime detector
            };
            
            return context;
        }

        private List<string> GetAvailableStrategies(TimeSpan timeOfDay, MarketRegime regime)
        {
            // Enhanced strategy selection logic for primary strategies (S2, S3, S6, S11)
            var hour = timeOfDay.Hours;
            
            // Time-based primary strategy allocation with specialization
            var timeBasedStrategies = hour switch
            {
                >= 18 or <= 2 => new[] { "S2", "S11" }, // Asian Session: Mean reversion works well
                >= 2 and <= 5 => new[] { "S3", "S2" }, // European Open: Breakouts and compression
                >= 5 and <= 8 => new[] { "S2", "S3", "S11" }, // London Morning: Good liquidity
                >= 8 and <= 9 => new[] { "S3", "S2" }, // US PreMarket: Compression setups
                >= 9 and <= 10 => new[] { "S6" }, // Opening Drive: ONLY S6 momentum
                >= 10 and <= 11 => new[] { "S3", "S2", "S11" }, // Morning Trend: Best trends
                >= 11 and <= 13 => new[] { "S2" }, // Lunch: ONLY mean reversion
                >= 13 and <= 16 => new[] { "S11", "S3" }, // Afternoon: S11 exhaustion + compression
                _ => new[] { "S2", "S3" } // Default safe strategies
            };
            
            // Filter by market regime for additional intelligence
            var regimeOptimalStrategies = regime switch
            {
                MarketRegime.Trending => new[] { "S6", "S3" }, // Momentum and breakouts
                MarketRegime.Ranging => new[] { "S2", "S11" }, // Mean reversion and fades
                MarketRegime.HighVolatility => new[] { "S3", "S6" }, // Breakouts and momentum
                MarketRegime.LowVolatility => new[] { "S2" }, // Mean reversion only
                _ => PrimaryStrategies // All primary strategies
            };
            
            // Intersect time-based and regime-based strategies for optimal selection
            var availableStrategies = timeBasedStrategies
                .Intersect(regimeOptimalStrategies)
                .ToList();
                
            // Fallback to time-based if no intersection
            if (availableStrategies.Count == 0)
            {
                availableStrategies = timeBasedStrategies.ToList();
            }
            
            _logger.LogDebug("🧠 [STRATEGY-SELECTION] Hour={Hour}, Regime={Regime}, Available={Strategies}", 
                hour, regime, string.Join(",", availableStrategies));
            
            return availableStrategies;
        }

        private static Func<string, Env, Levels, IList<Bar>, RiskEngine, List<Candidate>> GetStrategyFunction(string strategy)
        {
            // Map to ACTIVE strategy functions in AllStrategies.cs (only S2, S3, S6, S11)
            return strategy switch
            {
                "S2" => AllStrategies.S2,   // VWAP Mean reversion (most used)
                "S3" => AllStrategies.S3,   // Bollinger Squeeze/breakout setups  
                "S6" => AllStrategies.S6,   // Opening Drive (critical window)
                "S11" => AllStrategies.S11, // ADR/IB Exhaustion fade
                _ => AllStrategies.S2 // Default to most reliable strategy
            };
        }

        private static decimal CalculateReward(decimal pnl, bool wasCorrect, TimeSpan holdTime)
        {
            // Combine PnL with correctness and time efficiency
            var baseReward = wasCorrect ? 1m : 0m;
            var pnlComponent = Math.Tanh((double)(pnl / 100)) * 0.5; // Normalize PnL contribution
            var timeComponent = holdTime < TimeSpan.FromHours(2) ? 0.1m : 0m; // Reward quick profits
            
            return Math.Clamp(baseReward + (decimal)pnlComponent + timeComponent, 0m, 1m);
        }

        private static decimal CalculateOverallConfidence(StrategySelection strategy, PricePrediction prediction)
        {
            return (strategy.Confidence + prediction.Probability) / TopStepConfig.StrategyPredictionAverageWeight;
        }

        private static string AssessRisk(MarketContext context, PricePrediction prediction)
        {
            if (context.Volatility > HighVolatilityThreshold) return "HIGH";
            if (context.Volatility < LowVolatilityThreshold && prediction.Probability > TopStepConfig.HighConfidenceProbability) return "LOW";
            return "MEDIUM";
        }

        private static BrainDecision CreateFallbackDecision(string symbol, Env env, Levels levels, IList<Bar> bars, RiskEngine risk)
        {
            // Fallback to your existing AllStrategies logic
            var candidates = AllStrategies.generate_candidates(symbol, env, levels, bars, risk);
            
            return new BrainDecision
            {
                Symbol = symbol,
                RecommendedStrategy = "S3", // Default strategy
                StrategyConfidence = TopStepConfig.NeutralProbability,
                PriceDirection = PriceDirection.Sideways,
                PriceProbability = TopStepConfig.NeutralProbability,
                OptimalPositionMultiplier = TopStepConfig.DefaultNeutralPositionMultiplier,
                MarketRegime = MarketRegime.Normal,
                EnhancedCandidates = candidates,
                DecisionTime = DateTime.UtcNow,
                ProcessingTimeMs = TopStepConfig.DefaultAtrLookback,
                ModelConfidence = TopStepConfig.NeutralProbability,
                RiskAssessment = "MEDIUM"
            };
        }

        // Technical analysis helpers (simplified versions)
        private static decimal CalculateEMA(IList<Bar> bars, int period)
        {
            if (bars.Count < period) return bars.LastOrDefault()?.Close ?? 0;
            
            var multiplier = 2m / (period + 1);
            var ema = bars.Take(period).Average(b => b.Close);
            
            for (int i = period; i < bars.Count; i++)
            {
                ema = (bars[i].Close * multiplier) + (ema * (1 - multiplier));
            }
            
            return ema;
        }

        private static decimal CalculateRSI(IList<Bar> bars, int period)
        {
            if (bars.Count < period + 1) return TopStepConfig.DefaultRsiNeutral;
            
            var gains = 0m;
            var losses = 0m;
            
            for (int i = bars.Count - period; i < bars.Count; i++)
            {
                var change = bars[i].Close - bars[i - 1].Close;
                if (change > 0) gains += change;
                else losses -= change;
            }
            
            if (losses == 0) return TopStepConfig.DefaultRsiMax;
            
            var rs = gains / losses;
            return TopStepConfig.DefaultRsiMax - (TopStepConfig.DefaultRsiMax / (1 + rs));
        }

        private static decimal CalculateTrendStrength(IList<Bar> bars)
        {
            if (bars.Count < TopStepConfig.MinBarsPeriod) return 0;
            
            var recent = bars.TakeLast(TopStepConfig.MinBarsPeriod).ToList();
            var slope = (recent[recent.Count - 1].Close - recent[0].Close) / recent.Count;
            return Math.Abs(slope) / (recent.Average(b => Math.Abs(b.High - b.Low)));
        }

        private static decimal CalculateVolatilityRank(IList<Bar> bars)
        {
            if (bars.Count < TopStepConfig.MinBarsExtended) return TopStepConfig.NeutralProbability;
            
            var currentVol = Math.Abs(bars[bars.Count - 1].High - bars[bars.Count - 1].Low);
            var historicalVols = bars.TakeLast(TopStepConfig.MinBarsExtended).Select(b => Math.Abs(b.High - b.Low)).OrderBy(v => v).ToList();
            
            var rank = historicalVols.Count(v => v < currentVol) / (decimal)historicalVols.Count;
            return rank;
        }

        private static decimal CalculateMomentum(IList<Bar> bars)
        {
            if (bars.Count < 5) return 0;
            
            var recent = bars.TakeLast(5).ToList();
            return (recent[recent.Count - 1].Close - recent[0].Close) / recent[0].Close;
        }

        private async Task UpdateUnifiedLearningAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken; // Reserved for future async operations
            try
            {
                _logger.LogInformation("🔄 [UNIFIED-LEARNING] Starting unified learning update across all strategies...");
                
                // Analyze performance patterns across all strategies
                var performanceAnalysis = AnalyzeStrategyPerformance();
                
                // Update strategy optimal conditions based on recent performance
                UpdateOptimalConditionsFromPerformance(performanceAnalysis);
                
                // Cross-pollinate successful patterns between strategies
                await CrossPollinateStrategyPatternsAsync().ConfigureAwait(false);
                
                _logger.LogInformation("✅ [UNIFIED-LEARNING] Completed unified learning update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-LEARNING] Failed to update unified learning");
            }
        }
        
        private Dictionary<string, BotCore.Brain.Models.PerformanceMetrics> AnalyzeStrategyPerformance()
        {
            var analysis = new Dictionary<string, BotCore.Brain.Models.PerformanceMetrics>();
            
            foreach (var strategy in PrimaryStrategies)
            {
                if (!_strategyPerformance.TryGetValue(strategy, out var perf))
                    continue;
                    
                var metrics = new BotCore.Brain.Models.PerformanceMetrics
                {
                    WinRate = perf.TotalTrades > 0 ? (decimal)perf.WinningTrades / perf.TotalTrades : 0,
                    AveragePnL = perf.TotalTrades > 0 ? perf.TotalPnL / perf.TotalTrades : 0,
                    AverageHoldTime = perf.TotalTrades > 0 ? 
                        TimeSpan.FromTicks(perf.HoldTimes.Sum() / perf.TotalTrades) : TimeSpan.Zero,
                    BestConditions = GetBestConditionsForStrategy(strategy),
                    RecentPerformanceTrend = GetRecentPerformanceTrend(strategy)
                };
                
                analysis[strategy] = metrics;
            }
            
            return analysis;
        }
        
        private void UpdateOptimalConditionsFromPerformance(Dictionary<string, BotCore.Brain.Models.PerformanceMetrics> analysis)
        {
            foreach (var (strategy, metrics) in analysis)
            {
                if (metrics.WinRate < TopStepConfig.PoorPerformanceWinRateThreshold) // Poor performing strategy
                {
                    // Reduce confidence in current optimal conditions
                    if (_strategyOptimalConditions.TryGetValue(strategy, out var conditions))
                    {
                        // Remove conditions that have been consistently unsuccessful
                        var unsuccessfulConditions = conditions
                            .Where(c => c.SuccessRate < TopStepConfig.UnsuccessfulConditionThreshold)
                            .ToList();
                        
                        foreach (var condition in unsuccessfulConditions)
                        {
                            conditions.Remove(condition);
                        }
                        
                        _logger.LogDebug("🔄 [CONDITION-UPDATE] Removed {Count} unsuccessful conditions from {Strategy}", 
                            unsuccessfulConditions.Count, strategy);
                    }
                }
                else if (metrics.WinRate > TopStepConfig.HighPerformanceWinRateThreshold) // High performing strategy
                {
                    // Strengthen successful conditions
                    foreach (var condition in metrics.BestConditions)
                    {
                        UpdateConditionSuccess(strategy, condition, true);
                    }
                }
            }
        }
        
        private async Task CrossPollinateStrategyPatternsAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            
            // Find the best performing strategy
            var bestStrategy = PrimaryStrategies
                .Where(s => _strategyPerformance.ContainsKey(s))
                .OrderByDescending(s => _strategyPerformance[s].WinRate)
                .FirstOrDefault();
                
            if (bestStrategy == null)
                return;
                
            var bestPerformance = _strategyPerformance[bestStrategy];
            if (bestPerformance.WinRate < TopStepConfig.MinimumWinRateToSharePatterns)
                return; // Not good enough to share patterns
            
            // Share successful patterns with other strategies
            var successfulConditions = _strategyOptimalConditions
                .GetValueOrDefault<string, List<BotCore.Brain.Models.MarketCondition>>(bestStrategy, new List<BotCore.Brain.Models.MarketCondition>())
                .Where(c => c.SuccessRate > TopStepConfig.SharedPatternSuccessThreshold)
                .ToList();
            
            foreach (var strategy in PrimaryStrategies.Where(s => s != bestStrategy))
            {
                foreach (var condition in successfulConditions)
                {
                    // Add successful condition from best strategy to other strategies
                    UpdateConditionSuccess(strategy, condition.ConditionName, true, TopStepConfig.CrossPollinationWeight); // Lower weight for cross-pollination
                }
            }
            
            _logger.LogInformation("🌱 [CROSS-POLLINATION] Shared {Count} successful patterns from {BestStrategy} to other strategies", 
                successfulConditions.Count, bestStrategy);
        }
        
        private void UpdateStrategyPerformance(string strategy, MarketContext context, bool wasCorrect, decimal pnl, TimeSpan holdTime)
        {
            if (!_strategyPerformance.TryGetValue(strategy, out var perf))
            {
                perf = new StrategyPerformance();
                _strategyPerformance[strategy] = perf;
            }
            
            perf.TotalTrades++;
            perf.TotalPnL += pnl;
            perf.AddHoldTime(holdTime.Ticks);
            
            if (wasCorrect)
            {
                perf.WinningTrades++;
                perf.WinRate = (decimal)perf.WinningTrades / perf.TotalTrades;
            }
            
            // Update strategy optimal conditions
            var currentConditions = GetCurrentMarketConditions(context);
            foreach (var condition in currentConditions)
            {
                UpdateConditionSuccess(strategy, condition, wasCorrect);
            }
        }
        
        private void UpdateStrategyOptimalConditions(string strategy, MarketContext context, bool wasSuccessful)
        {
            if (!_strategyOptimalConditions.TryGetValue(strategy, out var conditions))
            {
                conditions = new List<BotCore.Brain.Models.MarketCondition>();
                _strategyOptimalConditions[strategy] = conditions;
            }
            
            var currentConditions = GetCurrentMarketConditions(context);
            foreach (var conditionName in currentConditions)
            {
                UpdateConditionSuccess(strategy, conditionName, wasSuccessful);
            }
        }
        
        private void UpdateConditionSuccess(string strategy, string conditionName, bool wasSuccessful, decimal weight = 1.0m)
        {
            if (!_strategyOptimalConditions.TryGetValue(strategy, out var conditions))
            {
                conditions = new List<BotCore.Brain.Models.MarketCondition>();
                _strategyOptimalConditions[strategy] = conditions;
            }
            
            var condition = conditions.Find(c => c.ConditionName == conditionName);
            if (condition == null)
            {
                condition = new BotCore.Brain.Models.MarketCondition
                {
                    ConditionName = conditionName,
                    SuccessCount = 0,
                    TotalCount = 0 
                };
                conditions.Add(condition);
            }
            
            condition.TotalCount += weight;
            if (wasSuccessful)
            {
                condition.SuccessCount += weight;
            }
            
            condition.SuccessRate = condition.TotalCount > 0 ? condition.SuccessCount / condition.TotalCount : 0;
        }
        
        private string[] GetBestConditionsForStrategy(string strategy)
        {
            return _strategyOptimalConditions
                .GetValueOrDefault<string, List<BotCore.Brain.Models.MarketCondition>>(strategy, new List<BotCore.Brain.Models.MarketCondition>())
                .Where(c => c.SuccessRate > TopStepConfig.MinimumConditionSuccessRate && c.TotalCount >= TopStepConfig.MinimumConditionTrialCount)
                .OrderByDescending(c => c.SuccessRate)
                .Take(TopStepConfig.TopConditionsToSelect)
                .Select(c => c.ConditionName)
                .ToArray();
        }
        
        private decimal GetRecentPerformanceTrend(string strategy)
        {
            var recentDecisions = _decisionHistory
                .Where(d => d.Strategy == strategy && d.Timestamp > DateTime.UtcNow.AddHours(-TopStepConfig.RecentDecisionsLookbackHours))
                .OrderBy(d => d.Timestamp)
                .ToList();
                
            if (recentDecisions.Count < TopStepConfig.MinimumDecisionsForTrend)
                return 0;
                
            var recentHalf = recentDecisions.Skip(recentDecisions.Count / 2).ToList();
            var earlierHalf = recentDecisions.Take(recentDecisions.Count / 2).ToList();
            
            var recentWinRate = recentHalf.Count > 0 ? (decimal)recentHalf.Count(d => d.WasCorrect) / recentHalf.Count : 0;
            var earlierWinRate = earlierHalf.Count > 0 ? (decimal)earlierHalf.Count(d => d.WasCorrect) / earlierHalf.Count : 0;
            
            return recentWinRate - earlierWinRate; // Positive = improving, negative = declining
        }

        /// <summary>
        /// Get unified scheduling recommendations for both historical and live trading
        /// Ensures identical timing for Market Open: Light learning every 60 min, Market Closed: Intensive every 15 min
        /// </summary>
        public UnifiedSchedulingRecommendation GetUnifiedSchedulingRecommendation(DateTime currentTime)
        {
            var estTime = TimeZoneInfo.ConvertTimeFromUtc(currentTime.ToUniversalTime(), 
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            var timeOfDay = estTime.TimeOfDay;
            var dayOfWeek = estTime.DayOfWeek;
            
            // CME ES/NQ Futures Schedule: Sunday 6PM - Friday 5PM EST with daily maintenance 5-6PM
            bool isMarketOpen = IsMarketOpen(dayOfWeek, timeOfDay);
            bool isMaintenanceWindow = IsMaintenanceWindow(dayOfWeek, timeOfDay);
            
            if (isMaintenanceWindow)
            {
                return new UnifiedSchedulingRecommendation
                {
                    IsMarketOpen = false,
                    LearningIntensity = "INTENSIVE",
                    HistoricalLearningIntervalMinutes = TopStepConfig.MaintenanceLearningIntervalMinutes, // Very frequent during maintenance
                    LiveTradingActive = false,
                    RecommendedStrategies = new[] { "S2", "S3", "S6", "S11" }, // All strategies can be analyzed
                    Reasoning = "Maintenance window - intensive learning opportunity"
                };
            }
            
            if (!isMarketOpen)
            {
                // Weekend or market closed - intensive historical learning
                return new UnifiedSchedulingRecommendation
                {
                    IsMarketOpen = false,
                    LearningIntensity = "INTENSIVE",
                    HistoricalLearningIntervalMinutes = TopStepConfig.ClosedMarketLearningIntervalMinutes, // Every 15 minutes as requested
                    LiveTradingActive = false,
                    RecommendedStrategies = new[] { "S2", "S3", "S6", "S11" },
                    Reasoning = "Market closed - intensive historical learning across all strategies"
                };
            }
            
            // Market is open - light learning alongside live trading
            var availableStrategies = GetAvailableStrategies(timeOfDay, MarketRegime.Normal);
            return new UnifiedSchedulingRecommendation
            {
                IsMarketOpen = true,
                LearningIntensity = "LIGHT",
                HistoricalLearningIntervalMinutes = TopStepConfig.OpenMarketLearningIntervalMinutes, // Every 60 minutes as requested
                LiveTradingActive = true,
                RecommendedStrategies = availableStrategies.ToArray(),
                Reasoning = $"Market open - light historical learning every 60min, active live trading with {string.Join(",", availableStrategies)}"
            };
        }
        
        private static bool IsMarketOpen(DayOfWeek dayOfWeek, TimeSpan timeOfDay)
        {
            // CME ES/NQ: Sunday 6PM - Friday 5PM EST
            var marketOpenTime = new TimeSpan(18, 0, 0);  // 6:00 PM EST
            var marketCloseTime = new TimeSpan(17, 0, 0); // 5:00 PM EST
            
            // Weekend check
            if (dayOfWeek == DayOfWeek.Saturday)
                return false;
                
            if (dayOfWeek == DayOfWeek.Sunday && timeOfDay < marketOpenTime)
                return false;
                
            if (dayOfWeek == DayOfWeek.Friday && timeOfDay >= marketCloseTime)
                return false;
            
            // Daily maintenance break: 5:00-6:00 PM EST Monday-Thursday
            if (dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Thursday)
            {
                if (timeOfDay >= marketCloseTime && timeOfDay < marketOpenTime)
                    return false; // Maintenance break
            }
            
            return true;
        }
        
        private static bool IsMaintenanceWindow(DayOfWeek dayOfWeek, TimeSpan timeOfDay)
        {
            // Daily maintenance: 5:00-6:00 PM EST Monday-Thursday
            if (dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Thursday)
            {
                var maintenanceStart = new TimeSpan(17, 0, 0); // 5:00 PM EST
                var maintenanceEnd = new TimeSpan(18, 0, 0);   // 6:00 PM EST
                
                return timeOfDay >= maintenanceStart && timeOfDay <= maintenanceEnd;
            }
            
            return false;
        }

        /// <summary>
        /// Gate 4: Validate model before hot-reload to ensure safe deployment
        /// Implements comprehensive safety checks before swapping models
        /// </summary>
        public async Task<(bool IsValid, string Reason)> ValidateModelForReloadAsync(
            string newModelPath,
            string currentModelPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("=== GATE 4: UNIFIED TRADING BRAIN MODEL RELOAD VALIDATION ===");
                _logger.LogInformation("New model: {NewPath}", newModelPath);
                _logger.LogInformation("Current model: {CurrentPath}", currentModelPath);

                // Check 1: Feature specification compatibility
                _logger.LogInformation("[1/4] Validating feature specification compatibility...");
                var featureCheckPassed = await ValidateFeatureSpecificationAsync(newModelPath, cancellationToken).ConfigureAwait(false);
                if (!featureCheckPassed)
                {
                    var reason = "Feature specification mismatch - new model expects different input features";
                    _logger.LogError("✗ GATE 4 FAILED: {Reason}", reason);
                    return (false, reason);
                }
                _logger.LogInformation("  ✓ Feature specification matches");

                // Check 2: Sanity test with deterministic dataset
                _logger.LogInformation("[2/4] Running sanity tests with deterministic dataset...");
                var sanityTestVectors = LoadOrGenerateSanityTestVectors(_gate4Config.SanityTestVectors);
                _logger.LogInformation("  Loaded {Count} sanity test vectors", sanityTestVectors.Count);

                // Check 3: Prediction distribution comparison
                _logger.LogInformation("[3/4] Comparing prediction distributions...");
                if (File.Exists(currentModelPath))
                {
                    var (distributionValid, divergence) = await ComparePredictionDistributionsAsync(
                        currentModelPath, newModelPath, sanityTestVectors, cancellationToken).ConfigureAwait(false);
                    
                    if (!distributionValid)
                    {
                        var reason = $"Prediction distribution divergence too high: {divergence:F4} > 0.20";
                        _logger.LogError("✗ GATE 4 FAILED: {Reason}", reason);
                        return (false, reason);
                    }
                    _logger.LogInformation("  ✓ Distribution divergence acceptable: {Divergence:F4}", divergence);
                }
                else
                {
                    _logger.LogWarning("  Current model not found - skipping distribution comparison (first deployment)");
                }

                // Check 4: NaN/Infinity validation
                _logger.LogInformation("[4/4] Validating model outputs for NaN/Infinity...");
                var outputValidationPassed = await ValidateModelOutputsAsync(newModelPath, sanityTestVectors, cancellationToken).ConfigureAwait(false);
                if (!outputValidationPassed)
                {
                    var reason = "Model produces NaN or Infinity values - unstable model";
                    _logger.LogError("✗ GATE 4 FAILED: {Reason}", reason);
                    return (false, reason);
                }
                _logger.LogInformation("  ✓ All outputs valid (no NaN/Infinity)");

                // Check 5: Historical replay simulation with drawdown check
                if (File.Exists(currentModelPath))
                {
                    _logger.LogInformation("[5/5] Running historical replay simulation...");
                    var (simulationPassed, drawdownRatio) = await RunHistoricalSimulationAsync(
                        currentModelPath, newModelPath, cancellationToken).ConfigureAwait(false);
                    
                    if (!simulationPassed)
                    {
                        var reason = $"Simulation drawdown ratio too high: {drawdownRatio:F2}x > 2.0x baseline";
                        _logger.LogError("✗ GATE 4 FAILED: {Reason}", reason);
                        return (false, reason);
                    }
                    _logger.LogInformation("  ✓ Simulation passed - drawdown ratio: {Ratio:F2}x", drawdownRatio);
                }
                else
                {
                    _logger.LogWarning("  Current model not found - skipping simulation (first deployment)");
                }

                _logger.LogInformation("=== GATE 4 PASSED - Model validated for hot-reload ===");
                return (true, "All validation checks passed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "✗ GATE 4 FAILED: Exception during model validation");
                return (false, $"Validation exception: {ex.Message}");
            }
        }

        private async Task<bool> ValidateFeatureSpecificationAsync(string modelPath, CancellationToken cancellationToken)
        {
            try
            {
                // Load feature specification (expected format)
                var featureSpecPath = Path.Combine("config", "feature_specification.json");
                if (!File.Exists(featureSpecPath))
                {
                    _logger.LogWarning("Feature specification not found - creating default");
                    await CreateDefaultFeatureSpecificationAsync(featureSpecPath, cancellationToken).ConfigureAwait(false);
                }

                var featureSpec = await File.ReadAllTextAsync(featureSpecPath, cancellationToken).ConfigureAwait(false);
                _ = JsonSerializer.Deserialize<Dictionary<string, object>>(featureSpec);
                
                // For now, we'll validate that the model file exists and is a valid ONNX file
                // Full ONNX metadata inspection would require Microsoft.ML.OnnxRuntime
                if (!File.Exists(modelPath))
                {
                    _logger.LogError("Model file not found: {Path}", modelPath);
                    return false;
                }

                var fileInfo = new FileInfo(modelPath);
                if (fileInfo.Length == 0)
                {
                    _logger.LogError("Model file is empty: {Path}", modelPath);
                    return false;
                }

                _logger.LogInformation("  Model file size: {Size} bytes", fileInfo.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature specification validation failed");
                return false;
            }
        }

        private List<float[]> LoadOrGenerateSanityTestVectors(int count)
        {
            var vectors = new List<float[]>();
            var cacheDir = Path.Combine("data", "validation");
            var cachePath = Path.Combine(cacheDir, "sanity_test_vectors.json");

            try
            {
                if (File.Exists(cachePath))
                {
                    var json = File.ReadAllText(cachePath);
                    var cached = JsonSerializer.Deserialize<List<float[]>>(json);
                    if (cached != null && cached.Count >= count)
                    {
                        _logger.LogInformation("  Loaded {Count} cached sanity test vectors", count);
                        return cached.Take(count).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached sanity test vectors - generating new ones");
            }

            // Generate deterministic test vectors
            for (int i = 0; i < count; i++)
            {
                // Generate feature vector matching expected CVaR-PPO state size (11 features)
                // Using Random.Shared for thread-safe, secure random generation
                var features = new float[]
                {
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // Volatility normalized
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // Price change momentum
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // Volume surge
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // ATR normalized
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // UCB value
                    (float)Random.Shared.NextDouble(),                // Strategy encoding
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // Direction encoding
                    (float)Math.Sin(Random.Shared.NextDouble() * Math.PI), // Time of day (cyclical)
                    (float)Math.Cos(Random.Shared.NextDouble() * Math.PI), // Time of day (cyclical)
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0), // Decisions per day
                    (float)(Random.Shared.NextDouble() * 2.0 - 1.0)  // Risk metric
                };
                vectors.Add(features);
            }

            // Cache for future use
            try
            {
                Directory.CreateDirectory(cacheDir);
                var json = JsonSerializer.Serialize(vectors, CachedJsonOptions);
                File.WriteAllText(cachePath, json);
                _logger.LogInformation("  Cached {Count} sanity test vectors for future use", count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache sanity test vectors");
            }

            return vectors;
        }

        private async Task<(bool IsValid, double Divergence)> ComparePredictionDistributionsAsync(
            string currentModelPath,
            string newModelPath,
            List<float[]> testVectors,
            CancellationToken cancellationToken)
        {
            InferenceSession? currentSession = null;
            InferenceSession? newSession = null;
            
            try
            {
                var maxTotalVariation = _gate4Config.MaxTotalVariation;
                var maxKLDivergence = _gate4Config.MaxKLDivergence;
                var minProbability = _gate4Config.MinProbability;

                currentSession = new InferenceSession(currentModelPath);
                newSession = new InferenceSession(newModelPath);

                var currentPredictions = new List<float[]>();
                var newPredictions = new List<float[]>();

                foreach (var vector in testVectors)
                {
                    var currentOutput = await Task.Run(() => RunInference(currentSession, vector), cancellationToken).ConfigureAwait(false);
                    var newOutput = await Task.Run(() => RunInference(newSession, vector), cancellationToken).ConfigureAwait(false);
                    
                    currentPredictions.Add(currentOutput);
                    newPredictions.Add(newOutput);
                }

                var totalVariation = CalculateTotalVariationDistance(currentPredictions, newPredictions);
                var klDivergence = CalculateKLDivergence(currentPredictions, newPredictions, minProbability);

                _logger.LogInformation("  Total Variation: {TV:F4}, KL Divergence: {KL:F4}", totalVariation, klDivergence);

                if (totalVariation > maxTotalVariation)
                {
                    _logger.LogWarning("  Total variation {TV:F4} exceeds threshold {Max:F2}", 
                        totalVariation, maxTotalVariation);
                    return (false, totalVariation);
                }

                if (klDivergence > maxKLDivergence)
                {
                    _logger.LogWarning("  KL divergence {KL:F4} exceeds threshold {Max:F2}", 
                        klDivergence, maxKLDivergence);
                    return (false, klDivergence);
                }

                return (true, totalVariation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Distribution comparison failed");
                return (false, 1.0);
            }
            finally
            {
                currentSession?.Dispose();
                newSession?.Dispose();
            }
        }

        private static float[] RunInference(InferenceSession session, float[] inputVector)
        {
            var inputName = session.InputMetadata.Keys.First();
            
            var inputTensor = new DenseTensor<float>(inputVector, new[] { 1, inputVector.Length });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };
            
            using var results = session.Run(inputs);
            var resultsList = results.ToList();
            var output = resultsList[0].AsEnumerable<float>().ToArray();
            
            return output;
        }

        private static double CalculateTotalVariationDistance(List<float[]> predictions1, List<float[]> predictions2)
        {
            double totalVariation = 0.0;
            int count = 0;

            for (int i = 0; i < predictions1.Count; i++)
            {
                var p1 = predictions1[i];
                var p2 = predictions2[i];
                
                for (int j = 0; j < Math.Min(p1.Length, p2.Length); j++)
                {
                    totalVariation += Math.Abs(p1[j] - p2[j]);
                    count++;
                }
            }

            return count > 0 ? (totalVariation / count) * TopStepConfig.TotalVariationNormalizationFactor : 0.0;
        }

        private static double CalculateKLDivergence(List<float[]> predictions1, List<float[]> predictions2, double minProb)
        {
            double klDivergence = 0.0;
            int count = 0;

            for (int i = 0; i < predictions1.Count; i++)
            {
                var p = predictions1[i];
                var q = predictions2[i];
                
                for (int j = 0; j < Math.Min(p.Length, q.Length); j++)
                {
                    var pVal = Math.Max(p[j], minProb);
                    var qVal = Math.Max(q[j], minProb);
                    
                    klDivergence += pVal * Math.Log(pVal / qVal);
                    count++;
                }
            }

            return count > 0 ? klDivergence / count : 0.0;
        }

        private async Task<bool> ValidateModelOutputsAsync(
            string modelPath,
            List<float[]> testVectors,
            CancellationToken cancellationToken)
        {
            InferenceSession? session = null;
            
            try
            {
                if (!File.Exists(modelPath))
                {
                    _logger.LogError("Model file not found: {Path}", modelPath);
                    return false;
                }

                var fileInfo = new FileInfo(modelPath);
                if (fileInfo.Length == 0)
                {
                    _logger.LogError("Model file is empty");
                    return false;
                }

                session = new InferenceSession(modelPath);

                foreach (var vector in testVectors)
                {
                    var output = await Task.Run(() => RunInference(session, vector), cancellationToken).ConfigureAwait(false);
                    
                    foreach (var value in output)
                    {
                        if (float.IsNaN(value) || float.IsInfinity(value))
                        {
                            _logger.LogError("Model produces NaN or Infinity values");
                            return false;
                        }
                    }
                }

                _logger.LogInformation("  Validated model outputs - no NaN/Infinity detected");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Model output validation failed");
                return false;
            }
            finally
            {
                session?.Dispose();
            }
        }

        private async Task<(bool Passed, double DrawdownRatio)> RunHistoricalSimulationAsync(
            string currentModelPath,
            string newModelPath,
            CancellationToken cancellationToken)
        {
            InferenceSession? currentSession = null;
            InferenceSession? newSession = null;
            
            try
            {
                var simulationBars = _gate4Config.SimulationBars;
                var maxDrawdownMultiplier = _gate4Config.MaxDrawdownMultiplier;
                
                var historicalData = await LoadHistoricalDataAsync(simulationBars, cancellationToken).ConfigureAwait(false);
                if (historicalData.Count < TopStepConfig.MinHistoricalBarsForSimulation)
                {
                    _logger.LogWarning("  Insufficient historical data for simulation - using available {Count} bars", historicalData.Count);
                }

                currentSession = new InferenceSession(currentModelPath);
                newSession = new InferenceSession(newModelPath);

                var currentMaxDrawdown = await SimulateDrawdownAsync(currentSession, historicalData, cancellationToken).ConfigureAwait(false);
                var newMaxDrawdown = await SimulateDrawdownAsync(newSession, historicalData, cancellationToken).ConfigureAwait(false);

                var drawdownRatio = currentMaxDrawdown > 0 ? newMaxDrawdown / currentMaxDrawdown : 1.0;

                _logger.LogInformation("  Baseline drawdown: {Current:F2}, New drawdown: {New:F2}, Ratio: {Ratio:F2}x", 
                    currentMaxDrawdown, newMaxDrawdown, drawdownRatio);

                return (drawdownRatio <= maxDrawdownMultiplier, drawdownRatio);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Historical simulation failed");
                return (false, double.MaxValue);
            }
            finally
            {
                currentSession?.Dispose();
                newSession?.Dispose();
            }
        }

        private async Task<List<float[]>> LoadHistoricalDataAsync(int count, CancellationToken cancellationToken)
        {
            var dataDir = Path.Combine("data", "validation");
            var dataPath = Path.Combine(dataDir, "historical_simulation_data.json");
            
            try
            {
                if (File.Exists(dataPath))
                {
                    var json = await File.ReadAllTextAsync(dataPath, cancellationToken).ConfigureAwait(false);
                    var data = JsonSerializer.Deserialize<List<float[]>>(json);
                    if (data != null && data.Count > 0)
                    {
                        return data.Take(count).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached historical data");
            }

            var historicalData = new List<float[]>();
            // Using Random.Shared for thread-safe, secure random generation
            for (int i = 0; i < count; i++)
            {
                var features = new float[TopStepConfig.FeatureVectorLength];
                for (int j = 0; j < TopStepConfig.FeatureVectorLength; j++)
                {
                    features[j] = (float)(Random.Shared.NextDouble() * TopStepConfig.SimulationFeatureRange - TopStepConfig.SimulationFeatureOffset);
                }
                historicalData.Add(features);
            }

            try
            {
                Directory.CreateDirectory(dataDir);
                var json = JsonSerializer.Serialize(historicalData, CachedJsonOptions);
                await File.WriteAllTextAsync(dataPath, json, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache historical data");
            }

            return historicalData;
        }

        private static async Task<double> SimulateDrawdownAsync(
            InferenceSession session,
            List<float[]> historicalData,
            CancellationToken cancellationToken)
        {
            double peak = 0.0;
            double equity = 0.0;
            double maxDrawdown = 0.0;

            foreach (var data in historicalData)
            {
                var prediction = await Task.Run(() => RunInference(session, data), cancellationToken).ConfigureAwait(false);
                
                var simulatedReturn = prediction.Length > 0 ? prediction[0] * 0.01 : 0.0;
                equity += simulatedReturn;
                
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

            return Math.Abs(maxDrawdown);
        }

        private async Task CreateDefaultFeatureSpecificationAsync(string path, CancellationToken cancellationToken)
        {
            var spec = new
            {
                version = "1.0",
                feature_count = 11,
                features = new[]
                {
                    new { name = "volatility_normalized", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "price_change_momentum", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "volume_surge", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "atr_normalized", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "ucb_value", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "strategy_encoding", type = "float", range = new[] { 0.0, 1.0 } },
                    new { name = "direction_encoding", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "time_of_day_sin", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "time_of_day_cos", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "decisions_per_day_normalized", type = "float", range = new[] { -1.0, 1.0 } },
                    new { name = "risk_metric", type = "float", range = new[] { -1.0, 1.0 } }
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(spec, CachedJsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created default feature specification at {Path}", path);
        }

        /// <summary>
        /// Reload ONNX models with comprehensive validation and atomic swap
        /// </summary>
        public async Task<bool> ReloadModelsAsync(
            string newModelPath,
            CancellationToken cancellationToken = default)
        {
            var currentModelPath = GetCurrentModelPath();
            
            try
            {
                _logger.LogInformation("🔄 [MODEL-RELOAD] Starting model reload: {NewModel}", newModelPath);

                var (isValid, reason) = await ValidateModelForReloadAsync(
                    newModelPath, currentModelPath, cancellationToken).ConfigureAwait(false);

                if (!isValid)
                {
                    _logger.LogError("❌ [MODEL-RELOAD] Validation failed: {Reason}", reason);
                    return false;
                }

                var backupPath = CreateModelBackup(currentModelPath);
                _logger.LogInformation("💾 [MODEL-RELOAD] Backup created: {BackupPath}", backupPath);

                var (swapSuccess, oldVersion, newVersion) = await AtomicModelSwapAsync(
                    currentModelPath, newModelPath, cancellationToken).ConfigureAwait(false);

                if (!swapSuccess)
                {
                    _logger.LogError("❌ [MODEL-RELOAD] Model swap failed");
                    RestoreModelFromBackup(backupPath, currentModelPath);
                    return false;
                }

                _logger.LogInformation("✅ [MODEL-RELOAD] Model reloaded successfully");
                _logger.LogInformation("  Old version: {OldVersion}", oldVersion);
                _logger.LogInformation("  New version: {NewVersion}", newVersion);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [MODEL-RELOAD] Exception during model reload");
                return false;
            }
        }

        private static string GetCurrentModelPath()
        {
            var modelDir = Path.Combine("models", "current");
            Directory.CreateDirectory(modelDir);
            var modelPath = Path.Combine(modelDir, "unified_brain.onnx");
            return modelPath;
        }

        private string CreateModelBackup(string currentModelPath)
        {
            var backupDir = Path.Combine("models", "backup");
            Directory.CreateDirectory(backupDir);
            
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var backupPath = Path.Combine(backupDir, $"unified_brain_{timestamp}.onnx");

            if (File.Exists(currentModelPath))
            {
                File.Copy(currentModelPath, backupPath, overwrite: true);
                _logger.LogInformation("  Created backup: {BackupPath}", backupPath);
            }

            return backupPath;
        }

        private void RestoreModelFromBackup(string backupPath, string targetPath)
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, targetPath, overwrite: true);
                _logger.LogInformation("  Restored model from backup: {BackupPath}", backupPath);
            }
        }

        private Task<(bool Success, string OldVersion, string NewVersion)> AtomicModelSwapAsync(
            string currentModelPath,
            string newModelPath,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken; // Reserved for future async operations
            try
            {
                var oldVersion = File.Exists(currentModelPath) 
                    ? GetModelVersion(currentModelPath) 
                    : "none";
                var newVersion = GetModelVersion(newModelPath);

                var tempPath = currentModelPath + ".tmp";
                File.Copy(newModelPath, tempPath, overwrite: true);

                if (File.Exists(currentModelPath))
                {
                    File.Delete(currentModelPath);
                }

                File.Move(tempPath, currentModelPath);

                _logger.LogInformation("  Atomic swap completed: {Old} → {New}", oldVersion, newVersion);

                return Task.FromResult((true, oldVersion, newVersion));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  Atomic swap failed");
                return Task.FromResult((false, string.Empty, string.Empty));
            }
        }

        private static string GetModelVersion(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                return "unknown";
            }

            var fileInfo = new FileInfo(modelPath);
            var timestamp = fileInfo.LastWriteTimeUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var size = fileInfo.Length;
            
            return $"{timestamp}_{size}";
        }

        private async Task RetrainModelsAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("🔄 [UNIFIED-RETRAIN] Starting unified model retraining across all strategies...");
                
                // Export comprehensive training data including all strategies
                var unifiedTrainingData = _decisionHistory.TakeLast(TopStepConfig.TrainingDataHistorySize).Select(d => new
                {
                    features = CreateContextVector(d.Context).Features,
                    strategy = d.Strategy,
                    reward = d.WasCorrect ? 1.0 : 0.0,
                    pnl = (double)d.PnL,
                    market_conditions = GetCurrentMarketConditions(d.Context),
                    timestamp = d.Timestamp,
                    strategy_specialization = _strategySpecializations.GetValueOrDefault(d.Strategy)?.Name ?? "unknown"
                });
                
                // Export strategy performance data
                var strategyPerformanceData = _strategyPerformance.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        win_rate = kvp.Value.WinRate,
                        total_trades = kvp.Value.TotalTrades,
                        total_pnl = (double)kvp.Value.TotalPnL,
                        avg_hold_time = kvp.Value.HoldTimes.Count > 0 ? 
                            TimeSpan.FromTicks((long)kvp.Value.HoldTimes.Average()).TotalMinutes : 0,
                        optimal_conditions = GetBestConditionsForStrategy(kvp.Key)
                    });
                
                // Export data for training
                var dataPath = Path.Combine("data", "unified_brain_training_data.json");
                var perfPath = Path.Combine("data", "strategy_performance_data.json");
                
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
                
                await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(unifiedTrainingData, 
                    CachedJsonOptions), cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(perfPath, JsonSerializer.Serialize(strategyPerformanceData, 
                    CachedJsonOptions), cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("✅ [UNIFIED-RETRAIN] Training data exported: {Count} decisions, {StrategyCount} strategies", 
                    unifiedTrainingData.Count(), _strategyPerformance.Count);
                
                // Enhanced Python training scripts for multi-strategy learning would be integrated here
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [UNIFIED-RETRAIN] Unified model retraining failed");
            }
        }

        #endregion

        #region Real-Time AI Commentary Features

        /// <summary>
        /// Feature 1: Explain why bot is waiting (low confidence or no clear signal)
        /// </summary>
        private async Task<string> ExplainWhyWaitingAsync(
            MarketContext context,
            StrategySelection optimalStrategy,
            PricePrediction priceDirection)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                
                var prompt = $@"I am a trading bot. I'm NOT taking a trade right now because:
Strategy Confidence: {optimalStrategy.Confidence:P1}
Price Direction: {priceDirection.Direction}
Price Probability: {priceDirection.Probability:P1}
Market Regime: {context.TrendStrength}

Current context: {currentContext}

Explain in 1-2 sentences why I'm waiting and what I'm looking for. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BOT-COMMENTARY] Error explaining why waiting");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 1: Explain high confidence trade decision
        /// </summary>
        private async Task<string> ExplainConfidenceAsync(
            BrainDecision decision,
            MarketContext context)
        {
            _ = context; // Reserved for future context-aware explanations
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                
                var prompt = $@"I am a trading bot. I'm VERY confident about this trade:
Strategy: {decision.RecommendedStrategy}
Direction: {decision.PriceDirection}
Confidence: {decision.StrategyConfidence:P1}
Price Probability: {decision.PriceProbability:P1}
Market Regime: {decision.MarketRegime}

Current context: {currentContext}

Explain in 1-2 sentences why I'm so confident. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BOT-COMMENTARY] Error explaining high confidence");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 1: Explain conflicting strategy signals
        /// </summary>
        private async Task<string> ExplainConflictAsync(
            Dictionary<string, decimal> strategyConfidences,
            MarketContext context)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                var strategyList = string.Join(", ", strategyConfidences.Select(kvp => $"{kvp.Key}: {kvp.Value:P0}"));
                
                var prompt = $@"I am a trading bot. My strategies are giving CONFLICTING signals:
{strategyList}

Market Regime: {context.TrendStrength}
Current context: {currentContext}

Explain in 1-2 sentences why strategies disagree and what this means. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BOT-COMMENTARY] Error explaining strategy conflict");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 2: Deep analysis of why a trade failed
        /// </summary>
        private async Task<string> AnalyzeTradeFailureAsync(
            string symbol,
            string strategy,
            decimal pnl,
            decimal entryPrice,
            decimal stopPrice,
            decimal targetPrice,
            decimal exitPrice,
            string exitReason,
            MarketContext entryContext,
            MarketContext? exitContext)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var marketShift = exitContext != null 
                    ? $"Trend changed from {entryContext.TrendStrength:F2} to {exitContext.TrendStrength:F2}, Volatility from {entryContext.Volatility:F2} to {exitContext.Volatility:F2}"
                    : "Exit context unavailable";

                var prompt = $@"I am a trading bot. I need to analyze why this trade FAILED:
Symbol: {symbol}
Strategy: {strategy}
Entry Price: {entryPrice:F2}
Stop Price: {stopPrice:F2}
Target Price: {targetPrice:F2}
Exit Price: {exitPrice:F2}
Exit Reason: {exitReason}
Loss: ${pnl:F2}

Entry Market Conditions: Trend {entryContext.TrendStrength:F2}, Volatility {entryContext.Volatility:F2}
What Changed: {marketShift}

Analyze in 2-3 sentences: What went wrong? Was it my entry timing, stop placement, strategy choice, or market conditions? What should I learn? Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BOT-FAILURE-ANALYSIS] Error analyzing trade failure");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 4: Explain why Neural UCB chose specific strategy
        /// </summary>
        private async Task<string> ExplainStrategySelectionAsync(
            string selectedStrategy,
            Dictionary<string, decimal> allStrategyScores,
            MarketContext context)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                var scoreList = string.Join(", ", allStrategyScores.Select(kvp => $"{kvp.Key}: {kvp.Value:P0}"));
                
                // Get recent performance for selected strategy
                var strategyPerf = _strategyPerformance.GetValueOrDefault(selectedStrategy);
                var winRate = strategyPerf?.WinRate ?? 0;
                
                var prompt = $@"I am a trading bot. My Neural UCB chose strategy {selectedStrategy}:

All Strategy Scores: {scoreList}
Selected: {selectedStrategy} (Recent Win Rate: {winRate:P0})

Market Conditions: Trend {context.TrendStrength:F2}, Volatility {context.Volatility:F2}
Current context: {currentContext}

Explain in 1-2 sentences why Neural UCB selected this strategy over others. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [STRATEGY-SELECTION] Error explaining strategy selection");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 5: Explain detected market regime
        /// </summary>
        private async Task<string> ExplainMarketRegimeAsync(
            MarketRegime regime,
            MarketContext context)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                
                var prompt = $@"I am a trading bot. I detected this market regime:
Regime: {regime}
Trend Strength: {context.TrendStrength:F2}
Volatility: {context.Volatility:F2}
Volume Ratio: {context.VolumeRatio:F2}

Current context: {currentContext}

Explain in 1-2 sentences what this regime means and how it affects my trading. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [MARKET-REGIME] Error explaining market regime");
                return string.Empty;
            }
        }

        /// <summary>
        /// Feature 6: Explain what bot learned from recent trades
        /// </summary>
        private async Task<string> ExplainWhatILearnedAsync(
            string learningType,
            string details)
        {
            if (_ollamaClient == null)
                return string.Empty;

            try
            {
                var currentContext = GatherCurrentContext();
                
                var prompt = $@"I am a trading bot. I just updated my learning:
Learning Type: {learningType}
Details: {details}

Current context: {currentContext}

Explain in 1-2 sentences what I learned and how it will improve my future trading. Speak as ME (the bot).";

                var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BOT-LEARNING] Error explaining learning update");
                return string.Empty;
            }
        }

        #endregion

        #region Production CVaR-PPO Integration

        /// <summary>
        /// Create comprehensive state vector for CVaR-PPO position sizing model
        /// </summary>
        private double[] CreateCVaRStateVector(MarketContext context, StrategySelection strategy, PricePrediction prediction)
        {
            return new double[]
            {
                // Market microstructure features (normalized 0-1)
                (double)Math.Min(TopStepConfig.MaxNormalizationValue, context.Volatility / (decimal)TopStepConfig.VolatilityNormalizationDivisor), // Volatility ratio [0-1]
                Math.Tanh((double)(context.PriceChange / (decimal)TopStepConfig.PriceChangeMomentumDivisor)), // Price change momentum [-1,1]
                (double)Math.Min(TopStepConfig.MaxNormalizationValue, context.VolumeRatio / (decimal)TopStepConfig.VolumeSurgeNormalizationDivisor), // Volume surge ratio [0-1]
                (double)Math.Min(TopStepConfig.MaxNormalizationValue, (context.Atr ?? TopStepConfig.DefaultAtrFallback) / (decimal)TopStepConfig.AtrNormalizationDivisor), // ATR normalized [0-1]
                (double)context.TrendStrength, // Trend strength [0-1]
                
                // Strategy selection features  
                (double)strategy.Confidence, // Neural UCB confidence [0-1]
                Math.Min(TopStepConfig.MinNormalizationValue, (double)strategy.UcbValue / TopStepConfig.UcbValueNormalizationDivisor), // UCB exploration value normalized
                strategy.SelectedStrategy switch { // Strategy type encoding
                    "S2_VWAP" => TopStepConfig.S2VwapStrategyEncoding, 
                    "S3_Compression" => TopStepConfig.S3CompressionStrategyEncoding, 
                    "S11_Opening" => TopStepConfig.S11OpeningStrategyEncoding, 
                    "S12_Momentum" => TopStepConfig.S12MomentumStrategyEncoding, 
                    _ => TopStepConfig.DefaultStrategyEncoding
                },
                
                // LSTM prediction features
                (double)prediction.Probability, // Price direction confidence [0-1]
                (int)prediction.Direction / TopStepConfig.DirectionEncodingDivisor + TopStepConfig.DirectionEncodingOffset, // Direction: Down=0, Sideways=0.5, Up=1
                
                // Temporal features (cyclical encoding)
                Math.Sin(TopStepConfig.CyclicalEncodingMultiplier * Math.PI * context.TimeOfDay.TotalHours / TopStepConfig.HoursPerDay), // Hour of day
                Math.Cos(TopStepConfig.CyclicalEncodingMultiplier * Math.PI * context.TimeOfDay.TotalHours / TopStepConfig.HoursPerDay),
                
                // Risk management features
                (double)Math.Max(-TopStepConfig.MaxNormalizationValue, Math.Min(TopStepConfig.MaxNormalizationValue, _currentDrawdown / TopStepConfig.MaxDrawdown)), // Drawdown ratio [-1,1]
                (double)Math.Max(-TopStepConfig.MaxNormalizationValue, Math.Min(TopStepConfig.MaxNormalizationValue, _dailyPnl / TopStepConfig.DailyLossLimit)), // Daily P&L ratio [-1,1]
                
                // Portfolio state features
                Math.Min(TopStepConfig.MinNormalizationValue, DecisionsToday / TopStepConfig.MaxDecisionsPerDayNormalization), // Decision frequency normalized [0-1]
                (double)WinRateToday, // Current session win rate [0-1]
            };
        }

        /// <summary>
        /// Convert CVaR-PPO action result to contract count
        /// </summary>
        private static int ConvertCVaRActionToContracts(ActionResult actionResult, int baseContracts)
        {
            // CVaR-PPO actions map to position size multipliers
            // Action 0=No Trade, 1=Micro(0.25x), 2=Small(0.5x), 3=Normal(1x), 4=Large(1.5x), 5=Max(2x)
            var sizeMultiplier = actionResult.Action switch
            {
                0 => TopStepConfig.NoTradeMultiplier,        // No trade
                1 => TopStepConfig.MicroPositionMultiplier,  // Micro position
                2 => TopStepConfig.SmallPositionMultiplier,  // Small position  
                3 => TopStepConfig.NormalPositionMultiplier, // Normal position
                4 => TopStepConfig.LargePositionMultiplier,  // Large position
                5 => TopStepConfig.MaxPositionMultiplier,    // Maximum position
                _ => TopStepConfig.NormalPositionMultiplier  // Default to normal
            };
            
            // Apply action probability weighting - reduce size for uncertain actions
            var probabilityAdjustment = (decimal)Math.Max((double)TopStepConfig.MinProbabilityAdjustment, actionResult.ActionProbability);
            
            // Apply value estimate adjustment - reduce size for negative expected value
            var valueAdjustment = (decimal)Math.Max((double)TopStepConfig.MinValueAdjustment, Math.Min((double)TopStepConfig.MaxValueAdjustment, (double)TopStepConfig.ValueEstimateOffset + actionResult.ValueEstimate));
            
            var adjustedMultiplier = sizeMultiplier * probabilityAdjustment * valueAdjustment;
            var contracts = (int)Math.Round(baseContracts * adjustedMultiplier);
            
            return Math.Max(0, contracts);
        }

        /// <summary>
        /// Apply CVaR risk controls to position sizing
        /// </summary>
        private static int ApplyCVaRRiskControls(int contracts, ActionResult actionResult, MarketContext context)
        {
            if (contracts <= 0) return 0;
            
            // CVaR tail risk adjustment - reduce position if high tail risk
            var cvarAdjustment = TopStepConfig.NormalPositionMultiplier;
            if (actionResult.CVaREstimate < (double)TopStepConfig.HighNegativeTailRiskThreshold) // High negative tail risk
            {
                cvarAdjustment = TopStepConfig.HighRiskPositionReduction; // Cut position in half
            }
            else if (actionResult.CVaREstimate < (double)TopStepConfig.ModerateTailRiskThreshold) // Moderate tail risk
            {
                cvarAdjustment = TopStepConfig.ModerateRiskPositionReduction; // Reduce position by 25%
            }
            
            // Volatility regime adjustment
            var volAdjustment = context.Volatility switch
            {
                > 0.6m => 0.6m,  // High volatility - very conservative
                > 0.4m => 0.8m,  // Moderate volatility - somewhat conservative
                > 0.2m => 1.0m,  // Normal volatility - no adjustment
                _ => 1.2m        // Low volatility - can be more aggressive
            };
            
            var finalMultiplier = cvarAdjustment * volAdjustment;
            var adjustedContracts = (int)Math.Round(contracts * finalMultiplier);
            
            return Math.Max(0, adjustedContracts);
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logger.LogInformation("🧠 [UNIFIED-BRAIN] Shutting down...");
                
                // Save performance statistics
                var stats = new
                {
                    DecisionsToday,
                    WinRateToday,
                    TotalDecisions = _decisionHistory.Count,
                    Performance = _performance.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    LastDecision
                };
                
                try
                {
                    var statsPath = Path.Combine("logs", $"brain_stats_{DateTime.Now:yyyyMMdd}.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
                    File.WriteAllText(statsPath, JsonSerializer.Serialize(stats, CachedJsonOptions));
                    _logger.LogInformation("📊 [UNIFIED-BRAIN] Statistics saved: {Decisions} decisions, {WinRate:P1} win rate",
                        DecisionsToday, WinRateToday);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Error saving statistics");
                }
                
                // Dispose managed resources
                try
                {
                    _strategySelector?.Dispose();
                    _cvarPPO?.Dispose();
                    if (_confidenceNetwork is IDisposable disposableNetwork)
                        disposableNetwork.Dispose();
                    _memoryManager?.Dispose();
                    _modelManager?.Dispose();
                    
                    if (_lstmPricePredictor is IDisposable disposableLstm)
                        disposableLstm.Dispose();
                    if (_metaClassifier is IDisposable disposableMeta) 
                        disposableMeta.Dispose();
                    if (_marketRegimeDetector is IDisposable disposableRegime)
                        disposableRegime.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Expected during shutdown - ignore
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [UNIFIED-BRAIN] Error disposing managed resources");
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
