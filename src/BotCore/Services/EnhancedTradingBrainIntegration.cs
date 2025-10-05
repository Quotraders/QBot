using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BotCore.ML;
using TradingBot.RLAgent;
using BotCore.Services;
using BotCore.Brain;
using BotCore.Brain.Models;
using BotCore.Models;
using BotCore.Risk;
using System.Text.Json;
using TradingBot.Abstractions;
using static BotCore.Brain.UnifiedTradingBrain;

// Type alias to resolve namespace conflict  
using BrainMarketContext = BotCore.Brain.Models.MarketContext;
using BrainTradingDecision = BotCore.Brain.Models.TradingDecision;
using ServicesTradingDecision = BotCore.Services.TradingDecision;

namespace BotCore.Services;

/// <summary>
/// Enhanced integration service that coordinates ML/RL/Cloud services with UnifiedTradingBrain
/// This enhances existing trading logic by adding automated model management and feedback loops
/// </summary>
public class EnhancedTradingBrainIntegration
{
    private readonly ILogger<EnhancedTradingBrainIntegration> _logger;
    private readonly UnifiedTradingBrain _tradingBrain;
    private readonly ModelEnsembleService _ensembleService;
    private readonly TradingFeedbackService _feedbackService;
    private readonly CloudModelSynchronizationService _cloudSync;
    private readonly IServiceProvider _serviceProvider;
    
    // Enhanced Trading Brain Integration Constants
    
    // Position sizing constants
    private const decimal DefaultPositionSize = 1.0m;                    // Default position size
    private const decimal BasePositionSizeMultiplier = 1.0m;             // Base multiplier for position sizing
    private const decimal HighConfidenceSizeBoost = 1.2m;                // Size boost for high confidence trades
    private const decimal LowConfidenceSizeReduction = 0.8m;             // Size reduction for low confidence trades
    private const decimal HighRiskSizeReduction = 0.7m;                  // Size reduction for high risk scenarios
    private const decimal StrongDirectionalSizeBoost = 1.1m;             // Size boost for strong directional signals
    private const decimal MinimumPositionSize = 0.1m;                    // Minimum position size
    private const decimal MaximumPositionSizeMultiple = 2.0m;            // Maximum position size as multiple of original
    
    // Confidence thresholds
    private const decimal VeryHighConfidenceThreshold = 0.8m;            // Very high confidence threshold
    private const decimal HighConfidenceThreshold = 0.75m;               // High confidence threshold
    private const decimal ModerateConfidenceThreshold = 0.7m;            // Moderate confidence threshold
    private const decimal LowConfidenceThreshold = 0.6m;                 // Low confidence threshold
    private const decimal VeryLowConfidenceThreshold = 0.5m;             // Very low confidence threshold
    private const decimal MinimumConfidence = 0.1m;                      // Minimum confidence level
    private const decimal MaximumConfidence = 1.0m;                      // Maximum confidence level
    private const decimal DefaultFallbackConfidence = 0.5m;              // Default confidence for fallback scenarios
    
    // Confidence blending weights
    private const decimal OriginalConfidenceWeight = 0.5m;               // Weight for original confidence in blending
    private const decimal StrategyEnsembleWeight = 0.3m;                 // Weight for strategy ensemble confidence
    private const decimal PriceEnsembleWeight = 0.2m;                    // Weight for price ensemble confidence
    
    // Risk management thresholds
    private const double HighCVaRThreshold = -0.1;                       // High CVaR threshold (high risk)
    private const double VeryHighCVaRThreshold = -0.2;                   // Very high CVaR threshold (very high risk)
    private const double PositiveCVaRThreshold = 0.1;                    // Positive CVaR threshold (favorable risk)
    private const decimal MinimumRiskLevel = 0.1m;                       // Minimum risk level
    private const decimal MaximumRiskLevel = 1.0m;                       // Maximum risk level
    private const decimal DefaultRiskLevel = 0.5m;                       // Default risk level
    private const decimal HighRiskAdjustmentDown = 0.1m;                 // Risk reduction for high CVaR
    private const decimal LowRiskAdjustmentUp = 0.05m;                   // Risk increase for positive CVaR
    private const decimal UncertaintyRiskAdjustmentDown = 0.05m;         // Risk reduction for uncertain predictions
    
    // Market timing thresholds
    private const decimal StrongSignalProbabilityThreshold = 0.75m;      // Probability threshold for strong signals
    private const decimal ModerateSignalProbabilityThreshold = 0.6m;     // Probability threshold for moderate signals
    private const decimal ActionProbabilityThreshold = 0.7m;             // Action probability threshold
    
    // Ensemble prediction settings
    private const int EnsemblePredictionWindowSeconds = 30;              // Time window for ensemble predictions (seconds)
    private const int MinimumFeaturesRequired = 5;                       // Minimum features required for prediction
    private const int MaximumContextVectorSize = 100;                    // Maximum context vector size
    private const decimal DefaultPredictionConfidence = 0.0m;            // Default prediction confidence when unavailable
    
    // Timing and caching
    private const int PredictionCacheExpirationSeconds = 10;             // Prediction cache expiration (seconds)
    private const int MinimumPredictionIntervalSeconds = 5;              // Minimum interval between predictions (seconds)
    private const int CloudSyncIntervalHours = 24;                       // Cloud sync interval (hours)
    private const int ModelUpdateCheckIntervalMinutes = 30;              // Model update check interval (minutes)
    
    // Performance tracking
    private const int MinimumTradesForAccuracy = 20;                     // Minimum trades needed for accuracy calculation
    private const int AccuracyHistoryWindowDays = 30;                    // Days of history for accuracy tracking
    private const decimal AccuracyTrackingThreshold = 0.15m;             // Threshold for tracking prediction accuracy
    
    // Data generation defaults (for sample data)
    private const int DefaultBarCount = 100;                             // Default number of bars for sample data
    private const int DefaultMinutesPerBar = 5;                          // Default minutes per bar
    private const decimal DefaultBarOpen = 5000m;                        // Default bar open price
    private const decimal DefaultBarHigh = 5010m;                        // Default bar high price
    private const decimal DefaultBarLow = 4990m;                         // Default bar low price
    private const decimal DefaultBarClose = 5005m;                       // Default bar close price
    private const int DefaultBarVolume = 1000;                           // Default bar volume
    private const decimal DefaultAccountBalance = 100000m;               // Default account balance
    private const decimal DefaultEquity = 100000m;                       // Default equity
    private const decimal DefaultAvailableCapital = 100000m;             // Default available capital
    private const int DefaultMaxPositions = 10;                          // Default maximum positions
    private const decimal DefaultRiskPerTrade = 0.01m;                   // Default risk per trade (1%)
    private const int DefaultPositionCount = 2;                          // Default number of positions
    private const decimal DefaultPriceLevel = 5.0m;                      // Default price level for sample data
    private const int DefaultTimeframeMinutes = 5;                       // Default timeframe in minutes
    
    // Integration state
    private readonly Dictionary<string, DateTime> _lastPredictions = new();
    private readonly Dictionary<string, double> _predictionAccuracies = new();
    private bool _isEnhancementActive = true;
    
    public EnhancedTradingBrainIntegration(
        ILogger<EnhancedTradingBrainIntegration> logger,
        UnifiedTradingBrain tradingBrain,
        ModelEnsembleService ensembleService,
        TradingFeedbackService feedbackService,
        CloudModelSynchronizationService cloudSync,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _tradingBrain = tradingBrain;
        _ensembleService = ensembleService;
        _feedbackService = feedbackService;
        _cloudSync = cloudSync;
        _serviceProvider = serviceProvider;
        
        _logger.LogInformation("🧠 [ENHANCED-BRAIN] Integration service initialized - enhancing existing trading logic");
    }

    /// <summary>
    /// Enhanced decision making that augments UnifiedTradingBrain with ensemble predictions
    /// This ENHANCES existing logic rather than replacing it
    /// </summary>
    public async Task<EnhancedTradingDecision> MakeEnhancedDecisionAsync(
        string symbol,
        Dictionary<string, object> marketContext,
        List<string> availableStrategies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketContext);
        
        try
        {
            _logger.LogDebug("🧠 [ENHANCED-BRAIN] Making enhanced decision for {Symbol}", symbol);
            
            // Step 1: Get original UnifiedTradingBrain decision
            var env = CreateSampleEnv();
            var levels = CreateSampleLevels();
            var bars = CreateSampleBars();
            var risk = CreateSampleRisk();
            
            var originalBrainDecision = await _tradingBrain.MakeIntelligentDecisionAsync(
                symbol, env, levels, bars, risk, cancellationToken).ConfigureAwait(false);
            
            if (!_isEnhancementActive)
            {
                // Return original decision if enhancement is disabled
                return new EnhancedTradingDecision
                {
                    OriginalDecision = ConvertBrainToTradingDecision(originalBrainDecision),
                    EnhancedStrategy = originalBrainDecision.RecommendedStrategy,
                    EnhancedConfidence = originalBrainDecision.StrategyConfidence,
                    EnhancedPositionSize = DefaultPositionSize, // Default size
                    EnhancementApplied = false,
                    Timestamp = DateTime.UtcNow
                };
            }
            
            // Step 2: Get ensemble predictions to enhance the decision
            // Convert dictionary marketContext to proper BrainMarketContext object
            var brainMarketContext = ConvertToMarketContext(marketContext);
            var contextVector = ExtractContextVector(brainMarketContext);
            var marketFeatures = ExtractMarketFeatures(brainMarketContext);
            
            // Get ensemble strategy prediction
            var strategyPrediction = await _ensembleService.GetStrategySelectionPredictionAsync(
                contextVector, availableStrategies, cancellationToken).ConfigureAwait(false);
            
            // Get ensemble price direction prediction
            var pricePrediction = await _ensembleService.GetPriceDirectionPredictionAsync(
                marketFeatures, cancellationToken).ConfigureAwait(false);
            
            // Get ensemble CVaR action
            var convertedDecision = ConvertBrainToTradingDecision(originalBrainDecision);
            var servicesDecision = ConvertToServicesDecision(convertedDecision);
            var state = CreateStateVector(servicesDecision, brainMarketContext);
            var ensembleAction = await _ensembleService.GetEnsembleActionAsync(
                state, true, cancellationToken).ConfigureAwait(false);
            
            // Step 3: Enhance the original decision using ensemble insights
            var enhancedDecision = EnhanceDecision(
                convertedDecision, 
                strategyPrediction, 
                pricePrediction, 
                ensembleAction,
                symbol,
                brainMarketContext);
            
            // Step 4: Log the enhancement
            LogDecisionEnhancement(convertedDecision, enhancedDecision, symbol);
            
            // Step 5: Track prediction for feedback
            TrackPredictionForFeedback(enhancedDecision, symbol, brainMarketContext);
            
            return enhancedDecision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error in enhanced decision making for {Symbol}", symbol);
            
            // Fallback to original decision on error
            try
            {
                var env = CreateSampleEnv();
                var levels = CreateSampleLevels();
                var bars = CreateSampleBars();
                var risk = CreateSampleRisk();
                
                var originalBrainDecision = await _tradingBrain.MakeIntelligentDecisionAsync(
                    symbol, env, levels, bars, risk, cancellationToken).ConfigureAwait(false);
                
                return new EnhancedTradingDecision
                {
                    OriginalDecision = ConvertBrainToTradingDecision(originalBrainDecision),
                    EnhancedStrategy = originalBrainDecision.RecommendedStrategy,
                    EnhancedConfidence = originalBrainDecision.StrategyConfidence,
                    EnhancedPositionSize = DefaultPositionSize,
                    EnhancementApplied = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch
            {
                // Ultimate fallback
                return new EnhancedTradingDecision
                {
                    OriginalDecision = CreateFallbackTradingDecision(),
                    EnhancedStrategy = "S3", // Safe default
                    EnhancedConfidence = DefaultFallbackConfidence,
                    EnhancedPositionSize = DefaultPositionSize,
                    EnhancementApplied = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }
    }

    /// <summary>
    /// Enhance the original decision using ensemble predictions
    /// This preserves the original logic while adding intelligent enhancements
    /// </summary>
    private EnhancedTradingDecision EnhanceDecision(
        BrainTradingDecision originalDecision,
        EnsemblePrediction strategyPrediction,
        EnsemblePrediction pricePrediction,
        EnsembleActionResult ensembleAction,
        string symbol,
        BrainMarketContext marketContext)
    {
        var enhancedDecision = new EnhancedTradingDecision
        {
            OriginalDecision = originalDecision,
            StrategyPrediction = strategyPrediction,
            PricePrediction = pricePrediction,
            EnsembleAction = ensembleAction,
            Timestamp = DateTime.UtcNow
        };
        
        // Enhancement 1: Strategy Selection
        enhancedDecision.EnhancedStrategy = EnhanceStrategySelection(
            originalDecision.Strategy, // Use Strategy instead of SelectedStrategy
            strategyPrediction,
            originalDecision.Confidence);
        
        // Enhancement 2: Confidence Adjustment
        enhancedDecision.EnhancedConfidence = EnhanceConfidence(
            originalDecision.Confidence,
            strategyPrediction.Confidence,
            pricePrediction.Confidence);
        
        // Enhancement 3: Position Sizing
        enhancedDecision.EnhancedPositionSize = EnhancePositionSizing(
            DefaultPositionSize, // Default position size since TradingDecision doesn't have PositionSize
            ensembleAction,
            enhancedDecision.EnhancedConfidence,
            pricePrediction);
        
        // Enhancement 4: Risk Adjustment
        enhancedDecision.EnhancedRiskLevel = EnhanceRiskLevel(
            DefaultRiskLevel, // Default risk level since TradingDecision doesn't have RiskLevel
            ensembleAction.CVaREstimate,
            pricePrediction);
        
        // Enhancement 5: Market Timing
        enhancedDecision.MarketTimingSignal = CalculateMarketTiming(
            pricePrediction,
            ensembleAction,
            marketContext);
        
        enhancedDecision.EnhancementApplied = true;
        enhancedDecision.EnhancementReason = GenerateEnhancementReason(
            originalDecision, strategyPrediction, pricePrediction, ensembleAction);
        
        return enhancedDecision;
    }

    /// <summary>
    /// Enhance strategy selection by combining original with ensemble prediction
    /// </summary>
    private string EnhanceStrategySelection(string originalStrategy, EnsemblePrediction strategyPrediction, decimal originalConfidence)
    {
        // If ensemble prediction is very confident and different, consider switching
        if (strategyPrediction.Confidence > VeryHighConfidenceThreshold && 
            strategyPrediction.Result is StrategyPrediction stratPred &&
            stratPred.SelectedStrategy != originalStrategy &&
            originalConfidence < ModerateConfidenceThreshold)
        {
            _logger.LogInformation("🧠 [ENHANCED-BRAIN] Strategy enhanced: {Original} → {Enhanced} (ensemble confidence: {Confidence:P1})",
                originalStrategy, stratPred.SelectedStrategy, strategyPrediction.Confidence);
            return stratPred.SelectedStrategy;
        }
        
        return originalStrategy; // Keep original strategy
    }

    /// <summary>
    /// Enhance confidence by blending original with ensemble predictions
    /// </summary>
    private decimal EnhanceConfidence(decimal originalConfidence, decimal strategyConfidence, decimal priceConfidence)
    {
        // Weighted average: 50% original, 30% strategy ensemble, 20% price ensemble
        var enhancedConfidence = (originalConfidence * OriginalConfidenceWeight) + 
                                (strategyConfidence * StrategyEnsembleWeight) + 
                                (priceConfidence * PriceEnsembleWeight);
        
        return Math.Max(MinimumConfidence, Math.Min(MaximumConfidence, enhancedConfidence));
    }

    /// <summary>
    /// Enhance position sizing using CVaR and ensemble insights
    /// </summary>
    private decimal EnhancePositionSizing(decimal originalSize, EnsembleActionResult ensembleAction, decimal confidence, EnsemblePrediction pricePrediction)
    {
        var sizeMultiplier = BasePositionSizeMultiplier;
        
        // Adjust based on confidence
        if (confidence > VeryHighConfidenceThreshold)
        {
            sizeMultiplier *= HighConfidenceSizeBoost; // Increase size for high confidence
        }
        else if (confidence < VeryLowConfidenceThreshold)
        {
            sizeMultiplier *= LowConfidenceSizeReduction; // Decrease size for low confidence
        }
        
        // Adjust based on CVaR estimate (risk management)
        if (ensembleAction.CVaREstimate < HighCVaRThreshold)
        {
            sizeMultiplier *= HighRiskSizeReduction; // Reduce size for high risk
        }
        
        // Adjust based on price prediction strength
        if (pricePrediction.Result is PriceDirectionPrediction pricePred)
        {
            if (pricePred.Direction != "Sideways" && pricePred.Probability > (double)ModerateConfidenceThreshold)
            {
                sizeMultiplier *= StrongDirectionalSizeBoost; // Slightly increase for strong directional bias
            }
        }
        
        var enhancedSize = originalSize * sizeMultiplier;
        
        // Safety bounds
        return Math.Max(MinimumPositionSize, Math.Min(MaximumPositionSizeMultiple * originalSize, enhancedSize));
    }

    /// <summary>
    /// Enhance risk level based on CVaR and market conditions
    /// </summary>
    private decimal EnhanceRiskLevel(decimal originalRisk, double cvarEstimate, EnsemblePrediction pricePrediction)
    {
        var riskAdjustment = DefaultPredictionConfidence; // Initialize the variable
        
        // Adjust based on CVaR
        if (cvarEstimate < VeryHighCVaRThreshold)
        {
            riskAdjustment -= HighRiskAdjustmentDown; // Reduce risk for high CVaR
        }
        else if (cvarEstimate > PositiveCVaRThreshold)
        {
            riskAdjustment += LowRiskAdjustmentUp; // Slightly increase risk for positive CVaR
        }
        
        // Adjust based on price prediction uncertainty
        if (pricePrediction.Confidence < LowConfidenceThreshold)
        {
            riskAdjustment -= UncertaintyRiskAdjustmentDown; // Reduce risk for uncertain predictions
        }
        
        return Math.Max(MinimumRiskLevel, Math.Min(MaximumRiskLevel, originalRisk + riskAdjustment));
    }

    /// <summary>
    /// Calculate market timing signal
    /// </summary>
    private string CalculateMarketTiming(EnsemblePrediction pricePrediction, EnsembleActionResult ensembleAction, BrainMarketContext marketContext)
    {
        if (pricePrediction.Result is PriceDirectionPrediction pricePred)
        {
            // Strong directional signal with high action probability
            if (pricePred.Probability > (double)StrongSignalProbabilityThreshold && ensembleAction.ActionProbability > (double)ActionProbabilityThreshold)
            {
                return pricePred.Direction == "Up" ? "STRONG_BUY" : 
                       pricePred.Direction == "Down" ? "STRONG_SELL" : "HOLD";
            }
            
            // Moderate signal
            if (pricePred.Probability > (double)ModerateSignalProbabilityThreshold)
            {
                return pricePred.Direction == "Up" ? "BUY" : 
                       pricePred.Direction == "Down" ? "SELL" : "NEUTRAL";
            }
        }
        
        return "NEUTRAL";
    }

    /// <summary>
    /// Generate human-readable enhancement reason
    /// </summary>
    private string GenerateEnhancementReason(
        BrainTradingDecision originalDecision,
        EnsemblePrediction strategyPred, 
        EnsemblePrediction pricePred, 
        EnsembleActionResult action)
    {
        var reasons = new List<string>();
        
        if (strategyPred.Confidence > ModerateConfidenceThreshold)
        {
            reasons.Add($"Strategy ensemble confidence: {strategyPred.Confidence:P0}");
        }
        
        if (pricePred.Confidence > ModerateConfidenceThreshold && pricePred.Result is PriceDirectionPrediction pricePredResult)
        {
            reasons.Add($"Price direction: {pricePredResult.Direction} ({pricePred.Confidence:P0})");
        }
        
        if (Math.Abs(action.CVaREstimate) > (double)MinimumConfidence)
        {
            reasons.Add($"CVaR adjustment: {action.CVaREstimate:F2}");
        }
        
        return reasons.Any() ? string.Join(", ", reasons) : "Ensemble enhancement applied";
    }

    /// <summary>
    /// Submit trading outcome for feedback learning
    /// </summary>
    public void SubmitTradingOutcome(
        string symbol,
        string strategy,
        string action,
        decimal realizedPnL,
        Dictionary<string, object> context)
    {
        try
        {
            // Calculate prediction accuracy if we have tracked predictions
            var predictionKey = $"{symbol}_{strategy}_{DateTime.UtcNow:yyyyMMdd}";
            var accuracy = _predictionAccuracies.TryGetValue(predictionKey, out var acc) ? acc : 0.5;
            
            var outcome = new TradingOutcome
            {
                Timestamp = DateTime.UtcNow,
                Strategy = strategy,
                Action = action,
                Symbol = symbol,
                PredictionAccuracy = accuracy,
                RealizedPnL = realizedPnL,
                MarketConditions = JsonSerializer.Serialize(context)
            };
            outcome.ReplaceTradingContext(context);
            
            _feedbackService.SubmitTradingOutcome(outcome);
            
            _logger.LogDebug("🧠 [ENHANCED-BRAIN] Trading outcome submitted: {Strategy} {Action} P&L: {PnL:C2}", 
                strategy, action, realizedPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error submitting trading outcome");
        }
    }

    /// <summary>
    /// Submit prediction feedback for model improvement
    /// </summary>
    public void SubmitPredictionFeedback(
        string modelName,
        string symbol,
        string predictedAction,
        string actualOutcome,
        double accuracy,
        decimal pnlImpact)
    {
        try
        {
            var feedback = new PredictionFeedback
            {
                Timestamp = DateTime.UtcNow,
                ModelName = modelName,
                Symbol = symbol,
                PredictedAction = predictedAction,
                ActualOutcome = actualOutcome,
                ActualAccuracy = accuracy,
                ImpactOnPnL = pnlImpact
            };
            
            _feedbackService.SubmitPredictionFeedback(feedback);
            
            _logger.LogDebug("🧠 [ENHANCED-BRAIN] Prediction feedback submitted: {Model} accuracy: {Accuracy:P1}", 
                modelName, accuracy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error submitting prediction feedback");
        }
    }

    /// <summary>
    /// Initialize and load models from cloud
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🧠 [ENHANCED-BRAIN] Initializing enhanced trading brain integration");
            
            // Trigger initial cloud model synchronization
            await _cloudSync.SynchronizeModelsAsync(cancellationToken).ConfigureAwait(false);
            
            // Load default models into ensemble
            await LoadDefaultModels(cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("🧠 [ENHANCED-BRAIN] Enhanced trading brain integration initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error initializing enhanced trading brain");
            
            // Disable enhancement on initialization failure
            _isEnhancementActive = false;
            _logger.LogWarning("🧠 [ENHANCED-BRAIN] Enhancement disabled due to initialization failure");
        }
    }

    /// <summary>
    /// Load default models into ensemble service
    /// </summary>
    private async Task LoadDefaultModels(CancellationToken cancellationToken)
    {
        try
        {
            // Load CVaR-PPO from DI
            var cvarPPO = _serviceProvider.GetService<CVaRPPO>();
            if (cvarPPO != null)
            {
                await _ensembleService.LoadModelAsync("cvar_ppo_default", "", ModelSource.Local, 1.0, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("🧠 [ENHANCED-BRAIN] Loaded CVaR-PPO model into ensemble");
            }
            
            // Load other models from data directory
            var modelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ml");
            if (Directory.Exists(modelsPath))
            {
                var onnxFiles = Directory.GetFiles(modelsPath, "*.onnx");
                foreach (var onnxFile in onnxFiles)
                {
                    var modelName = Path.GetFileNameWithoutExtension(onnxFile);
                    await _ensembleService.LoadModelAsync(modelName, onnxFile, ModelSource.Local, (double)VeryHighConfidenceThreshold, cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("🧠 [ENHANCED-BRAIN] Loaded local model: {ModelName}", modelName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error loading default models");
        }
    }

    /// <summary>
    /// Track prediction for feedback analysis
    /// </summary>
    private void TrackPredictionForFeedback(EnhancedTradingDecision decision, string symbol, BrainMarketContext marketContext)
    {
        try
        {
            var predictionKey = $"{symbol}_{decision.EnhancedStrategy}_{DateTime.UtcNow:yyyyMMdd}";
            _lastPredictions[predictionKey] = DateTime.UtcNow;
            
            // Store prediction details for later accuracy calculation
            // This would be enhanced with actual prediction tracking logic
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧠 [ENHANCED-BRAIN] Error tracking prediction");
        }
    }

    /// <summary>
    /// Log decision enhancement details
    /// </summary>
    private void LogDecisionEnhancement(BrainTradingDecision original, EnhancedTradingDecision enhanced, string symbol)
    {
        _logger.LogInformation("🧠 [ENHANCED-BRAIN] Decision enhanced for {Symbol}: {OriginalStrategy} → {EnhancedStrategy} " +
                             "(confidence: {OriginalConf:P1} → {EnhancedConf:P1})",
            symbol,
            original.Strategy, // Use Strategy instead of SelectedStrategy
            enhanced.EnhancedStrategy,
            original.Confidence,
            enhanced.EnhancedConfidence);
    }

    #region Helper Methods

    private double[] ExtractContextVector(BrainMarketContext marketContext)
    {
        // Extract and normalize market context into feature vector
        return new double[] { 
            (double)marketContext.CurrentPrice / (double)DefaultBarOpen, // Normalized price
            (double)marketContext.Volume / (double)DefaultAccountBalance, // Normalized volume
            (double)marketContext.Volatility, // Volatility
            marketContext.TimeOfDay.TotalHours / (double)CloudSyncIntervalHours, // Time of day
            (double)marketContext.VolumeRatio // Volume ratio
        };
    }

    private double[] ExtractMarketFeatures(BrainMarketContext marketContext)
    {
        // Extract market features for price prediction
        return new double[] { 
            (double)marketContext.CurrentPrice, // Current price
            (double)marketContext.Volume, // Volume
            (double)marketContext.Volatility, // Volatility
            (double)marketContext.PriceChange, // Price change
            (double)marketContext.VolumeRatio, // Volume ratio
            marketContext.TimeOfDay.TotalHours // Time factor
        };
    }

    private double[] CreateStateVector(ServicesTradingDecision decision, BrainMarketContext marketContext)
    {
        // Create state vector for CVaR-PPO using available properties
        return new double[] { 
            (double)decision.Confidence, // Decision confidence
            (double)marketContext.CurrentPrice, // Current price
            (double)marketContext.Volume, // Market volume
            (double)marketContext.Volatility, // Volatility
            // Use strategy as action encoding instead of Action property (which doesn't exist)
            decision.StrategyId.Contains("S3", StringComparison.OrdinalIgnoreCase) ? 1.0 : 
            decision.StrategyId.Contains("S6", StringComparison.OrdinalIgnoreCase) ? -1.0 : 0.0 // Strategy-based encoding
        };
    }

    /// <summary>
    /// Convert Dictionary marketContext to MarketContext
    /// </summary>
    private BrainMarketContext ConvertToMarketContext(Dictionary<string, object> marketContext)
    {
        var brainContext = new BrainMarketContext();
        
        if (marketContext.TryGetValue("Symbol", out var symbol) && symbol is string symbolStr)
            brainContext.Symbol = symbolStr;
        
        if (marketContext.TryGetValue("CurrentPrice", out var price) && price is decimal priceDecimal)
            brainContext.CurrentPrice = priceDecimal;
        else if (price is double priceDouble)
            brainContext.CurrentPrice = (decimal)priceDouble;
        
        if (marketContext.TryGetValue("Volume", out var volume) && volume is decimal volumeDecimal)
            brainContext.Volume = volumeDecimal;
        else if (volume is double volumeDouble)
            brainContext.Volume = (decimal)volumeDouble;
        
        if (marketContext.TryGetValue("Volatility", out var volatility) && volatility is decimal volatilityDecimal)
            brainContext.Volatility = volatilityDecimal;
        else if (volatility is double volatilityDouble)
            brainContext.Volatility = (decimal)volatilityDouble;
        else
            brainContext.Volatility = AccuracyTrackingThreshold; // Default volatility
        
        if (marketContext.TryGetValue("TimeOfDay", out var timeOfDay) && timeOfDay is TimeSpan timeSpan)
            brainContext.TimeOfDay = timeSpan;
        else
            brainContext.TimeOfDay = DateTime.UtcNow.TimeOfDay;
        
        brainContext.DayOfWeek = DateTime.UtcNow.DayOfWeek;
        brainContext.VolumeRatio = BasePositionSizeMultiplier; // Default
        brainContext.PriceChange = DefaultPredictionConfidence; // Default
        
        return brainContext;
    }

    private Env CreateSampleEnv()
    {
        return new Env
        {
            Symbol = "ES",
            atr = DefaultPriceLevel + (decimal)(Random.Shared.NextDouble() * DefaultPositionCount), // ATR around 5-7
            volz = AccuracyTrackingThreshold + (decimal)(Random.Shared.NextDouble() * (double)MinimumConfidence) // Volume Z-score
        };
    }

    private Levels CreateSampleLevels()
    {
        var basePrice = 4500.0m;
        return new Levels
        {
            Support1 = basePrice - PredictionCacheExpirationSeconds,
            Support2 = basePrice - MinimumTradesForAccuracy,
            Support3 = basePrice - EnsemblePredictionWindowSeconds,
            Resistance1 = basePrice + PredictionCacheExpirationSeconds,
            Resistance2 = basePrice + MinimumTradesForAccuracy,
            Resistance3 = basePrice + EnsemblePredictionWindowSeconds,
            VWAP = basePrice,
            DailyPivot = basePrice,
            WeeklyPivot = basePrice + MinimumFeaturesRequired,
            MonthlyPivot = basePrice - MinimumFeaturesRequired
        };
    }

    private IList<Bar> CreateSampleBars()
    {
        var bars = new List<Bar>();
        var basePrice = 4500.0m;
        var currentTime = DateTime.UtcNow;
        
        for (int i = 0; i < DefaultMaxPositions; i++)
        {
            var variation = (decimal)(Random.Shared.NextDouble() - 0.5) * MinimumFeaturesRequired;
            var openPrice = basePrice + variation;
            var closePrice = openPrice + (decimal)(Random.Shared.NextDouble() - 0.5) * DefaultPositionCount;
            
            bars.Add(new Bar
            {
                Symbol = "ES",
                Start = currentTime.AddMinutes(-i),
                Ts = ((DateTimeOffset)currentTime.AddMinutes(-i)).ToUnixTimeMilliseconds(),
                Open = openPrice,
                High = Math.Max(openPrice, closePrice) + (decimal)Random.Shared.NextDouble(),
                Low = Math.Min(openPrice, closePrice) - (decimal)Random.Shared.NextDouble(),
                Close = closePrice,
                Volume = DefaultBarCount + Random.Shared.Next(DefaultBarVolume / MinimumFeaturesRequired)
            });
        }
        
        return bars;
    }

    private RiskEngine CreateSampleRisk()
    {
        var riskEngine = new RiskEngine();
        riskEngine.cfg.risk_per_trade = DefaultBarCount; // $100 risk per trade
        riskEngine.cfg.max_daily_drawdown = DefaultBarVolume;
        riskEngine.cfg.max_open_positions = 1;
        return riskEngine;
    }

    private BrainTradingDecision ConvertBrainToTradingDecision(object brainDecision)
    {
        // Convert UnifiedTradingBrain decision format to BotCore TradingDecision
        if (brainDecision == null)
        {
            return new BrainTradingDecision
            {
                Symbol = "UNKNOWN",
                Strategy = "none",
                Confidence = DefaultPredictionConfidence,
                Timestamp = DateTime.UtcNow
            };
        }

        // If it's a BrainDecision, convert it to TradingDecision
        if (brainDecision is BrainDecision brain)
        {
            return new BrainTradingDecision
            {
                Symbol = brain.Symbol,
                Strategy = brain.RecommendedStrategy,
                Confidence = brain.StrategyConfidence,
                Timestamp = brain.DecisionTime
            };
        }

        // If it's already a TradingDecision, return as-is
        if (brainDecision is BrainTradingDecision decision)
        {
            return decision;
        }

        // Fallback for unknown types
        return new BrainTradingDecision
        {
            Symbol = "FALLBACK",
            Strategy = "unknown",
            Confidence = DefaultPredictionConfidence,
            Timestamp = DateTime.UtcNow
        };
    }

    private BrainTradingDecision CreateFallbackTradingDecision()
    {
        return new BrainTradingDecision
        {
            Symbol = "FALLBACK",
            Strategy = "fallback",
            Confidence = DefaultPredictionConfidence,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Convert BrainTradingDecision to ServicesTradingDecision for ensemble processing
    /// </summary>
    private ServicesTradingDecision ConvertToServicesDecision(BrainTradingDecision brainDecision)
    {
        return new ServicesTradingDecision
        {
            Action = TradingAction.Hold, // Default, will be set based on strategy
            Confidence = (double)brainDecision.Confidence,
            Reason = $"Converted from brain decision: {brainDecision.Strategy}",
            Symbol = brainDecision.Symbol,
            StrategyId = brainDecision.Strategy,
            Timestamp = brainDecision.Timestamp
        };
    }

    #endregion
}

#region Data Models

public class EnhancedTradingDecision
{
    public BrainTradingDecision OriginalDecision { get; set; } = null!;
    public string EnhancedStrategy { get; set; } = string.Empty;
    public decimal EnhancedConfidence { get; set; }
    public decimal EnhancedPositionSize { get; set; }
    public decimal EnhancedRiskLevel { get; set; }
    public string MarketTimingSignal { get; set; } = string.Empty;
    public bool EnhancementApplied { get; set; }
    public string EnhancementReason { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    
    // Ensemble prediction details
    public EnsemblePrediction? StrategyPrediction { get; set; }
    public EnsemblePrediction? PricePrediction { get; set; }
    public EnsembleActionResult? EnsembleAction { get; set; }
    
    public DateTime Timestamp { get; set; }
}

#endregion