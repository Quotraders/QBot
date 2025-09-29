using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Execution
{
    /// <summary>
    /// Bracket Adjustment Service for S7 execution
    /// Dynamically adjusts stop loss and take profit levels based on uncertainty metrics
    /// Uses conformal intervals and model uncertainty for adaptive bracket sizing
    /// </summary>
    public sealed class BracketAdjustmentService
    {
        private readonly ILogger<BracketAdjustmentService> _logger;
        private readonly BracketConfiguration _config;

        public BracketAdjustmentService(
            ILogger<BracketAdjustmentService> logger,
            IOptions<BracketConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Calculate adaptive bracket levels based on uncertainty metrics from feature layer
        /// Returns stop loss and take profit levels with audit trail
        /// </summary>
        public async Task<BracketRecommendation> CalculateBracketLevelsAsync(
            ExecutionIntent intent,
            MicrostructureSnapshot microstructure,
            ConformalPredictionInterval conformalInterval,
            CancellationToken cancellationToken = default)
        {
            if (intent == null)
                throw new ArgumentNullException(nameof(intent));
            if (microstructure == null)
                throw new ArgumentNullException(nameof(microstructure));
            if (conformalInterval == null)
                throw new ArgumentNullException(nameof(conformalInterval));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                intent.ValidateForExecution();
                microstructure.ValidateForExecution();
                conformalInterval.ValidateInterval();

                var recommendation = new BracketRecommendation
                {
                    RequestId = intent.RequestId,
                    Symbol = intent.Symbol,
                    EntryPrice = microstructure.MidPrice,
                    Timestamp = DateTime.UtcNow
                };

                // Calculate base bracket distances using volatility
                var baseStopDistance = CalculateBaseStopDistance(microstructure);
                var baseTakeDistance = CalculateBaseTakeDistance(microstructure);

                // Apply uncertainty-based adjustments
                ApplyConformalAdjustments(conformalInterval, ref baseStopDistance, ref baseTakeDistance, recommendation);
                ApplyModelUncertaintyAdjustments(intent, ref baseStopDistance, ref baseTakeDistance, recommendation);
                ApplyPatternReliabilityAdjustments(intent, ref baseStopDistance, ref baseTakeDistance, recommendation);
                ApplyVolatilityAdjustments(microstructure, ref baseStopDistance, ref baseTakeDistance, recommendation);

                // Calculate final levels
                CalculateFinalBracketLevels(intent, recommendation.EntryPrice, baseStopDistance, baseTakeDistance, recommendation);

                // Validate and audit log
                recommendation.ValidateRecommendation();
                
                _logger.LogInformation("[BRACKET-ADJUSTMENT] {Symbol} {Side}: SL={StopLoss:F2} ({StopDistance:F1} pts), TP={TakeProfit:F2} ({TakeDistance:F1} pts) - Reasoning: {Reasoning}", 
                    intent.Symbol, intent.Side, recommendation.StopLossPrice, baseStopDistance, 
                    recommendation.TakeProfitPrice, baseTakeDistance, string.Join("; ", recommendation.Reasoning));

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] Failed to calculate bracket levels for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    intent.Symbol);
                
                // Fail-closed: throw exception to prevent execution with invalid brackets
                throw new InvalidOperationException($"[BRACKET-ADJUSTMENT] Critical failure calculating brackets for '{intent.Symbol}': {ex.Message}", ex);
            }
        }

        private decimal CalculateBaseStopDistance(MicrostructureSnapshot microstructure)
        {
            // Base stop distance using ATR or volatility measure
            var atrMultiplier = _config.BaseStopAtrMultiplier;
            
            // Estimate ATR from spread and volatility rank
            var estimatedAtr = microstructure.SpreadBps * (decimal)microstructure.VolatilityRank * 0.01m;
            
            return Math.Max(_config.MinStopDistancePoints, estimatedAtr * atrMultiplier);
        }

        private decimal CalculateBaseTakeDistance(MicrostructureSnapshot microstructure)
        {
            // Base take profit using risk-reward ratio
            var baseStopDistance = CalculateBaseStopDistance(microstructure);
            return baseStopDistance * _config.BaseRiskRewardRatio;
        }

        private void ApplyConformalAdjustments(ConformalPredictionInterval interval, ref decimal stopDistance, ref decimal takeDistance, BracketRecommendation recommendation)
        {
            // Wider conformal intervals indicate higher uncertainty - expand brackets
            var intervalWidth = interval.UpperBound - interval.LowerBound;
            var intervalMultiplier = 1.0m + (intervalWidth * _config.ConformalIntervalSensitivity);

            stopDistance *= intervalMultiplier;
            takeDistance *= intervalMultiplier;

            recommendation.Reasoning.Add($"Conformal interval width {intervalWidth:F3} -> bracket multiplier {intervalMultiplier:F2}");
        }

        private void ApplyModelUncertaintyAdjustments(ExecutionIntent intent, ref decimal stopDistance, ref decimal takeDistance, BracketRecommendation recommendation)
        {
            if (intent.ModelUncertainty.HasValue)
            {
                // Higher model uncertainty -> wider brackets
                var uncertaintyMultiplier = 1.0m + (decimal)intent.ModelUncertainty.Value * _config.ModelUncertaintySensitivity;
                
                stopDistance *= uncertaintyMultiplier;
                takeDistance *= uncertaintyMultiplier;

                recommendation.Reasoning.Add($"Model uncertainty {intent.ModelUncertainty.Value:F3} -> bracket multiplier {uncertaintyMultiplier:F2}");
            }
        }

        private void ApplyPatternReliabilityAdjustments(ExecutionIntent intent, ref decimal stopDistance, ref decimal takeDistance, BracketRecommendation recommendation)
        {
            if (intent.PatternReliability.HasValue)
            {
                // Lower pattern reliability -> wider stop, tighter take profit
                var reliabilityFactor = (decimal)intent.PatternReliability.Value;
                var stopMultiplier = 1.0m + (1.0m - reliabilityFactor) * _config.PatternReliabilitySensitivity;
                var takeMultiplier = _config.TakeMultiplierBase + reliabilityFactor * _config.TakeMultiplierRange;

                stopDistance *= stopMultiplier;
                takeDistance *= takeMultiplier;

                recommendation.Reasoning.Add($"Pattern reliability {intent.PatternReliability.Value:F3} -> stop x{stopMultiplier:F2}, take x{takeMultiplier:F2}");
            }
        }

        private void ApplyVolatilityAdjustments(MicrostructureSnapshot microstructure, ref decimal stopDistance, ref decimal takeDistance, BracketRecommendation recommendation)
        {
            if (microstructure.IsHighVolatility)
            {
                // High volatility -> wider brackets
                stopDistance *= _config.HighVolatilityMultiplier;
                takeDistance *= _config.HighVolatilityMultiplier;

                recommendation.Reasoning.Add($"High volatility -> bracket multiplier {_config.HighVolatilityMultiplier:F2}");
            }

            // Apply volatility rank adjustment using configuration
            var volRankMultiplier = _config.VolRankMultiplierBase + (decimal)microstructure.VolatilityRank * _config.VolRankMultiplierRange;
            stopDistance *= volRankMultiplier;
            takeDistance *= volRankMultiplier;

            recommendation.Reasoning.Add($"Volatility rank {microstructure.VolatilityRank:F2} -> bracket multiplier {volRankMultiplier:F2}");
        }

        private void CalculateFinalBracketLevels(ExecutionIntent intent, decimal entryPrice, decimal stopDistance, decimal takeDistance, BracketRecommendation recommendation)
        {
            switch (intent.Side.ToUpperInvariant())
            {
                case "BUY":
                    recommendation.StopLossPrice = entryPrice - stopDistance;
                    recommendation.TakeProfitPrice = entryPrice + takeDistance;
                    break;

                case "SELL":
                    recommendation.StopLossPrice = entryPrice + stopDistance;
                    recommendation.TakeProfitPrice = entryPrice - takeDistance;
                    break;

                default:
                    throw new InvalidOperationException($"[BRACKET-ADJUSTMENT] Invalid side: {intent.Side}");
            }

            // Apply minimum/maximum bracket constraints
            ApplyBracketConstraints(recommendation);
        }

        private void ApplyBracketConstraints(BracketRecommendation recommendation)
        {
            // Ensure brackets are not too tight or too wide
            var entryPrice = recommendation.EntryPrice;
            var maxStopDistance = entryPrice * _config.MaxStopLossPercentage / 100m;
            var minStopDistance = _config.MinStopDistancePoints;

            // Constrain stop loss
            var currentStopDistance = Math.Abs(recommendation.StopLossPrice - entryPrice);
            if (currentStopDistance > maxStopDistance)
            {
                var constrainedStopDistance = maxStopDistance;
                recommendation.StopLossPrice = recommendation.StopLossPrice > entryPrice ? 
                    entryPrice + constrainedStopDistance : entryPrice - constrainedStopDistance;
                recommendation.Reasoning.Add($"Stop loss constrained to max {_config.MaxStopLossPercentage}%");
            }
            else if (currentStopDistance < minStopDistance)
            {
                recommendation.StopLossPrice = recommendation.StopLossPrice > entryPrice ? 
                    entryPrice + minStopDistance : entryPrice - minStopDistance;
                recommendation.Reasoning.Add($"Stop loss expanded to min {minStopDistance} points");
            }

            // Ensure take profit maintains reasonable risk-reward
            var stopDistance = Math.Abs(recommendation.StopLossPrice - entryPrice);
            var minTakeDistance = stopDistance * _config.MinRiskRewardRatio;
            var currentTakeDistance = Math.Abs(recommendation.TakeProfitPrice - entryPrice);

            if (currentTakeDistance < minTakeDistance)
            {
                recommendation.TakeProfitPrice = recommendation.TakeProfitPrice > entryPrice ? 
                    entryPrice + minTakeDistance : entryPrice - minTakeDistance;
                recommendation.Reasoning.Add($"Take profit expanded to maintain min R:R {_config.MinRiskRewardRatio:F1}");
            }
        }
    }

    /// <summary>
    /// Configuration for bracket adjustment behavior - NO HARDCODED DEFAULTS (fail-closed requirement)
    /// </summary>
    public sealed class BracketConfiguration
    {
        public decimal BaseStopAtrMultiplier { get; set; }
        public decimal BaseRiskRewardRatio { get; set; }
        public decimal MinRiskRewardRatio { get; set; }
        public decimal MinStopDistancePoints { get; set; }
        public decimal MaxStopLossPercentage { get; set; }
        
        public decimal ConformalIntervalSensitivity { get; set; }
        public decimal ModelUncertaintySensitivity { get; set; }
        public decimal PatternReliabilitySensitivity { get; set; }
        public decimal HighVolatilityMultiplier { get; set; }
        
        // Take profit multiplier range settings (fail-closed requirement)
        public decimal TakeMultiplierBase { get; set; }
        public decimal TakeMultiplierRange { get; set; }
        
        // Volatility rank multiplier range settings (fail-closed requirement)
        public decimal VolRankMultiplierBase { get; set; }
        public decimal VolRankMultiplierRange { get; set; }

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (BaseStopAtrMultiplier <= 0 || BaseRiskRewardRatio <= 0 || MinRiskRewardRatio <= 0)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] Multiplier and ratio values must be positive - FAIL-CLOSED");
            if (MinStopDistancePoints <= 0 || MaxStopLossPercentage <= 0)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] Distance and percentage values must be positive - FAIL-CLOSED");
            if (MinRiskRewardRatio > BaseRiskRewardRatio)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] MinRiskRewardRatio cannot exceed BaseRiskRewardRatio - FAIL-CLOSED");
            if (ConformalIntervalSensitivity <= 0 || ModelUncertaintySensitivity <= 0 || PatternReliabilitySensitivity <= 0)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] Sensitivity values must be positive - FAIL-CLOSED");
            if (HighVolatilityMultiplier <= 1.0m)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] HighVolatilityMultiplier must be greater than 1.0 - FAIL-CLOSED");
            if (TakeMultiplierBase <= 0 || TakeMultiplierRange <= 0)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] Take multiplier values must be positive - FAIL-CLOSED");
            if (VolRankMultiplierBase <= 0 || VolRankMultiplierRange <= 0)
                throw new InvalidOperationException("[BRACKET-ADJUSTMENT] [AUDIT-VIOLATION] VolRank multiplier values must be positive - FAIL-CLOSED");
        }
    }

    /// <summary>
    /// Conformal prediction interval from feature layer
    /// </summary>
    public sealed class ConformalPredictionInterval
    {
        public decimal LowerBound { get; set; }
        public decimal UpperBound { get; set; }
        public double ConfidenceLevel { get; set; } = 0.95; // 95% confidence
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public void ValidateInterval()
        {
            if (UpperBound <= LowerBound)
                throw new InvalidOperationException("[CONFORMAL-INTERVAL] UpperBound must be greater than LowerBound");
            if (ConfidenceLevel <= 0 || ConfidenceLevel >= 1)
                throw new InvalidOperationException("[CONFORMAL-INTERVAL] ConfidenceLevel must be between 0 and 1");
            if (Timestamp < DateTime.UtcNow.AddMinutes(-10))
                throw new InvalidOperationException("[CONFORMAL-INTERVAL] Conformal interval is stale");
        }
    }

    /// <summary>
    /// Bracket recommendation with audit trail
    /// </summary>
    public sealed class BracketRecommendation
    {
        public Guid RequestId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public List<string> Reasoning { get; } = new();
        public DateTime Timestamp { get; set; }

        public void ValidateRecommendation()
        {
            if (string.IsNullOrWhiteSpace(Symbol))
                throw new InvalidOperationException("[BRACKET-RECOMMENDATION] Symbol is required");
            if (EntryPrice <= 0 || StopLossPrice <= 0 || TakeProfitPrice <= 0)
                throw new InvalidOperationException("[BRACKET-RECOMMENDATION] All prices must be positive");
            if (Reasoning.Count == 0)
                throw new InvalidOperationException("[BRACKET-RECOMMENDATION] Reasoning is required for audit compliance");
        }
    }
}