using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Abstractions;

namespace TradingBot.Safety.Explainability
{
    /// <summary>
    /// Explainability stamp service that stores compact JSON evidence for each trading decision
    /// Captures key evidence: zone score, pattern probabilities, S7 state, execution readiness, risk adjustments
    /// </summary>
    public class ExplainabilityStampService : IExplainabilityStampService
    {
        private readonly ILogger<ExplainabilityStampService> _logger;
        private readonly ExplainabilityConfig _config;
        private readonly object _writeLock = new();

        public ExplainabilityStampService(
            ILogger<ExplainabilityStampService> logger,
            IOptions<ExplainabilityConfig> config)
        {
            _logger = logger;
            _config = config.Value;
            
            // Ensure state/explain directory exists
            Directory.CreateDirectory(_config.ExplainabilityPath);
        }

        /// <summary>
        /// Create and store explainability stamp for a trading decision
        /// </summary>
        public async Task StampDecisionAsync(
            TradingDecision decision,
            DecisionContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var timestamp = DateTime.UtcNow;
                var stamp = CreateExplainabilityStamp(decision, context, timestamp);
                
                var fileName = $"{timestamp:yyyyMMdd_HHmmss_fff}_{decision.Symbol}.json";
                var filePath = Path.Combine(_config.ExplainabilityPath, fileName);

                var json = JsonSerializer.Serialize(stamp, new JsonSerializerOptions
                {
                    WriteIndented = _config.IndentedJson,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                lock (_writeLock)
                {
                    await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
                }

                // Log at debug level to avoid noise in production
                _logger.LogDebug("[EXPLAINABILITY] Stamped decision {Decision} for {Symbol} to {FilePath}",
                    decision.Action, decision.Symbol, fileName);

                // Clean up old stamps if configured
                if (_config.MaxStampAgeHours > 0)
                {
                    await CleanupOldStampsAsync(timestamp).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXPLAINABILITY] Error creating explainability stamp for {Symbol} {Action}",
                    decision.Symbol, decision.Action);
            }
        }

        /// <summary>
        /// Get explainability stamps for a specific symbol and time range
        /// </summary>
        public async Task<List<ExplainabilityStamp>> GetStampsAsync(
            string symbol,
            DateTime fromTime,
            DateTime toTime,
            CancellationToken cancellationToken = default)
        {
            var stamps = new List<ExplainabilityStamp>();

            try
            {
                var files = Directory.GetFiles(_config.ExplainabilityPath, $"*_{symbol}.json");
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (TryParseTimestampFromFileName(fileName, out var fileTime) &&
                        fileTime >= fromTime && fileTime <= toTime)
                    {
                        var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                        var stamp = JsonSerializer.Deserialize<ExplainabilityStamp>(json, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        
                        if (stamp != null)
                            stamps.Add(stamp);
                    }
                }

                stamps.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXPLAINABILITY] Error retrieving stamps for {Symbol}", symbol);
            }

            return stamps;
        }

        /// <summary>
        /// Get decision audit trail for analysis
        /// </summary>
        public async Task<DecisionAuditTrail> GetDecisionAuditTrailAsync(
            string symbol,
            DateTime date,
            CancellationToken cancellationToken = default)
        {
            var auditTrail = new DecisionAuditTrail
            {
                Symbol = symbol,
                Date = date,
                Decisions = new List<ExplainabilityStamp>()
            };

            try
            {
                var fromTime = date.Date;
                var toTime = date.Date.AddDays(1).AddTicks(-1);
                
                auditTrail.Decisions = await GetStampsAsync(symbol, fromTime, toTime, cancellationToken).ConfigureAwait(false);
                
                // Calculate summary statistics
                auditTrail.TotalDecisions = auditTrail.Decisions.Count;
                auditTrail.GoDecisions = auditTrail.Decisions.Count(d => d.Decision == "GO");
                auditTrail.HoldDecisions = auditTrail.Decisions.Count(d => d.Decision == "HOLD");
                auditTrail.AverageConfidence = auditTrail.Decisions.Count > 0 
                    ? auditTrail.Decisions.Average(d => d.Evidence.Confidence) 
                    : 0;

                _logger.LogInformation("[EXPLAINABILITY] Generated audit trail for {Symbol} on {Date}: {Total} decisions",
                    symbol, date, auditTrail.TotalDecisions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXPLAINABILITY] Error generating audit trail for {Symbol} on {Date}", symbol, date);
            }

            return auditTrail;
        }

        /// <summary>
        /// Create a comprehensive explainability stamp
        /// </summary>
        private ExplainabilityStamp CreateExplainabilityStamp(
            TradingDecision decision,
            DecisionContext context,
            DateTime timestamp)
        {
            return new ExplainabilityStamp
            {
                Timestamp = timestamp,
                Symbol = decision.Symbol,
                Decision = decision.Action.ToString(),
                Evidence = new DecisionEvidence
                {
                    Confidence = (double)decision.Confidence,
                    ZoneScore = ExtractZoneScore(context),
                    PatternProbabilities = ExtractPatternProbabilities(context),
                    S7State = ExtractS7State(context),
                    ExecutionReadiness = ExtractExecutionReadiness(context),
                    RiskAdjustments = ExtractRiskAdjustments(context),
                    ModelInputs = ExtractModelInputs(context),
                    MarketConditions = ExtractMarketConditions(context),
                    Qualifiers = ExtractQualifiers(context)
                },
                Metadata = new Dictionary<string, object>
                {
                    ["strategy"] = context.Strategy ?? "Unknown",
                    ["model_version"] = context.ModelVersion ?? "Unknown",
                    ["execution_mode"] = context.ExecutionMode ?? "Unknown",
                    ["risk_mode"] = context.RiskMode ?? "Unknown",
                    ["data_quality"] = context.DataQuality ?? "Unknown"
                }
            };
        }

        /// <summary>
        /// Extract zone score from decision context
        /// </summary>
        private ZoneScoreEvidence ExtractZoneScore(DecisionContext context)
        {
            return new ZoneScoreEvidence
            {
                PrimaryZone = GetContextValue<string>(context, "primary_zone", "Unknown"),
                ZoneStrength = GetContextValue<double>(context, "zone_strength", 0.0),
                ZoneType = GetContextValue<string>(context, "zone_type", "Unknown"),
                SupportResistanceLevel = GetContextValue<decimal>(context, "support_resistance_level", 0m),
                ZoneConfidence = GetContextValue<double>(context, "zone_confidence", 0.0),
                TimeInZone = GetContextValue<TimeSpan>(context, "time_in_zone", TimeSpan.Zero)
            };
        }

        /// <summary>
        /// Extract pattern probabilities from decision context
        /// </summary>
        private PatternProbabilities ExtractPatternProbabilities(DecisionContext context)
        {
            return new PatternProbabilities
            {
                BullishProbability = GetContextValue<double>(context, "bullish_probability", 0.0),
                BearishProbability = GetContextValue<double>(context, "bearish_probability", 0.0),
                NeutralProbability = GetContextValue<double>(context, "neutral_probability", 0.0),
                BreakoutProbability = GetContextValue<double>(context, "breakout_probability", 0.0),
                ReversalProbability = GetContextValue<double>(context, "reversal_probability", 0.0),
                ContinuationProbability = GetContextValue<double>(context, "continuation_probability", 0.0),
                PrimaryPattern = GetContextValue<string>(context, "primary_pattern", "Unknown"),
                PatternConfidence = GetContextValue<double>(context, "pattern_confidence", 0.0),
                ShadowSignal = GetContextValue<bool>(context, "shadow_signal", false)
            };
        }

        /// <summary>
        /// Extract S7 state from decision context
        /// </summary>
        private S7StateEvidence ExtractS7State(DecisionContext context)
        {
            return new S7StateEvidence
            {
                CurrentState = GetContextValue<string>(context, "s7_current_state", "Unknown"),
                StateConfidence = GetContextValue<double>(context, "s7_state_confidence", 0.0),
                TransitionProbability = GetContextValue<double>(context, "s7_transition_probability", 0.0),
                PreviousState = GetContextValue<string>(context, "s7_previous_state", "Unknown"),
                StateHistory = GetContextValue<List<string>>(context, "s7_state_history", new List<string>()),
                AlignmentScore = GetContextValue<double>(context, "s7_alignment_score", 0.0),
                GateStatus = GetContextValue<string>(context, "s7_gate_status", "Unknown")
            };
        }

        /// <summary>
        /// Extract execution readiness from decision context
        /// </summary>
        private ExecutionReadiness ExtractExecutionReadiness(DecisionContext context)
        {
            return new ExecutionReadiness
            {
                LiquidityScore = GetContextValue<double>(context, "liquidity_score", 0.0),
                SpreadQuality = GetContextValue<double>(context, "spread_quality", 0.0),
                OrderBookDepth = GetContextValue<double>(context, "order_book_depth", 0.0),
                MarketImpactEstimate = GetContextValue<double>(context, "market_impact_estimate", 0.0),
                ExecutionCostEstimate = GetContextValue<decimal>(context, "execution_cost_estimate", 0m),
                OptimalOrderType = GetContextValue<string>(context, "optimal_order_type", "Market"),
                QueuePosition = GetContextValue<int>(context, "queue_position", 0),
                EstimatedFillTime = GetContextValue<TimeSpan>(context, "estimated_fill_time", TimeSpan.Zero)
            };
        }

        /// <summary>
        /// Extract risk adjustments from decision context
        /// </summary>
        private RiskAdjustments ExtractRiskAdjustments(DecisionContext context)
        {
            return new RiskAdjustments
            {
                PositionSizeAdjustment = GetContextValue<double>(context, "position_size_adjustment", 1.0),
                VolatilityAdjustment = GetContextValue<double>(context, "volatility_adjustment", 1.0),
                CorrelationAdjustment = GetContextValue<double>(context, "correlation_adjustment", 1.0),
                DrawdownAdjustment = GetContextValue<double>(context, "drawdown_adjustment", 1.0),
                TimeOfDayAdjustment = GetContextValue<double>(context, "time_of_day_adjustment", 1.0),
                MaxRiskPerTrade = GetContextValue<decimal>(context, "max_risk_per_trade", 0m),
                RiskBudgetRemaining = GetContextValue<decimal>(context, "risk_budget_remaining", 0m),
                OverallRiskScore = GetContextValue<double>(context, "overall_risk_score", 0.0)
            };
        }

        /// <summary>
        /// Extract model inputs from decision context
        /// </summary>
        private ModelInputs ExtractModelInputs(DecisionContext context)
        {
            return new ModelInputs
            {
                FeatureCount = GetContextValue<int>(context, "feature_count", 0),
                MissingFeatures = GetContextValue<List<string>>(context, "missing_features", new List<string>()),
                FeatureDriftScore = GetContextValue<double>(context, "feature_drift_score", 0.0),
                ModelLatency = GetContextValue<double>(context, "model_latency_ms", 0.0),
                EnsembleWeights = GetContextValue<Dictionary<string, double>>(context, "ensemble_weights", new Dictionary<string, double>()),
                TopFeatures = GetContextValue<List<string>>(context, "top_features", new List<string>()),
                FeatureImportance = GetContextValue<Dictionary<string, double>>(context, "feature_importance", new Dictionary<string, double>())
            };
        }

        /// <summary>
        /// Extract market conditions from decision context
        /// </summary>
        private MarketConditions ExtractMarketConditions(DecisionContext context)
        {
            return new MarketConditions
            {
                Volatility = GetContextValue<double>(context, "volatility", 0.0),
                Volume = GetContextValue<long>(context, "volume", 0L),
                Spread = GetContextValue<decimal>(context, "spread", 0m),
                MarketRegime = GetContextValue<string>(context, "market_regime", "Unknown"),
                TimeOfDay = GetContextValue<string>(context, "time_of_day", "Unknown"),
                TradingSession = GetContextValue<string>(context, "trading_session", "Unknown"),
                EconomicEvents = GetContextValue<List<string>>(context, "economic_events", new List<string>()),
                MarketSentiment = GetContextValue<double>(context, "market_sentiment", 0.0)
            };
        }

        /// <summary>
        /// Extract qualifiers from decision context
        /// </summary>
        private List<string> ExtractQualifiers(DecisionContext context)
        {
            var qualifiers = new List<string>();
            
            // Add various qualifiers based on context
            if (GetContextValue<bool>(context, "dry_run_mode", false))
                qualifiers.Add("DRY_RUN");
            
            if (GetContextValue<bool>(context, "high_volatility", false))
                qualifiers.Add("HIGH_VOLATILITY");
            
            if (GetContextValue<bool>(context, "low_liquidity", false))
                qualifiers.Add("LOW_LIQUIDITY");
            
            if (GetContextValue<bool>(context, "pattern_promoted", false))
                qualifiers.Add("PATTERN_PROMOTED");
            
            if (GetContextValue<bool>(context, "model_rollback", false))
                qualifiers.Add("MODEL_ROLLBACK");
            
            if (GetContextValue<bool>(context, "feature_drift_detected", false))
                qualifiers.Add("FEATURE_DRIFT");
            
            if (GetContextValue<bool>(context, "execution_queue_breach", false))
                qualifiers.Add("QUEUE_ETA_BREACH");

            // Add custom qualifiers from context
            var customQualifiers = GetContextValue<List<string>>(context, "custom_qualifiers", new List<string>());
            qualifiers.AddRange(customQualifiers);

            return qualifiers;
        }

        /// <summary>
        /// Helper method to safely extract values from decision context
        /// </summary>
        private T GetContextValue<T>(DecisionContext context, string key, T defaultValue)
        {
            try
            {
                if (context.AdditionalData.TryGetValue(key, out var value) && value is T typedValue)
                    return typedValue;
                
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Parse timestamp from explainability file name
        /// </summary>
        private bool TryParseTimestampFromFileName(string fileName, out DateTime timestamp)
        {
            timestamp = default;
            
            try
            {
                // Expected format: yyyyMMdd_HHmmss_fff_SYMBOL
                var parts = fileName.Split('_');
                if (parts.Length >= 3)
                {
                    var dateStr = parts[0];
                    var timeStr = parts[1];
                    var msStr = parts[2];
                    
                    var fullTimestamp = $"{dateStr}_{timeStr}_{msStr}";
                    return DateTime.TryParseExact(fullTimestamp, "yyyyMMdd_HHmmss_fff", null, 
                        System.Globalization.DateTimeStyles.None, out timestamp);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }

        /// <summary>
        /// Clean up old explainability stamps
        /// </summary>
        private async Task CleanupOldStampsAsync(DateTime currentTime)
        {
            try
            {
                var cutoffTime = currentTime.AddHours(-_config.MaxStampAgeHours);
                var files = Directory.GetFiles(_config.ExplainabilityPath, "*.json");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (TryParseTimestampFromFileName(fileName, out var fileTime) && fileTime < cutoffTime)
                    {
                        await Task.Run(() => File.Delete(file)).ConfigureAwait(false);
                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation("[EXPLAINABILITY] Cleaned up {Count} old explainability stamps", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXPLAINABILITY] Error cleaning up old stamps");
            }
        }
    }

    /// <summary>
    /// Interface for explainability stamp service
    /// </summary>
    public interface IExplainabilityStampService
    {
        Task StampDecisionAsync(TradingDecision decision, DecisionContext context, CancellationToken cancellationToken = default);
        Task<List<ExplainabilityStamp>> GetStampsAsync(string symbol, DateTime fromTime, DateTime toTime, CancellationToken cancellationToken = default);
        Task<DecisionAuditTrail> GetDecisionAuditTrailAsync(string symbol, DateTime date, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration for explainability service
    /// </summary>
    public class ExplainabilityConfig
    {
        public string ExplainabilityPath { get; set; } = "state/explain";
        public bool IndentedJson { get; set; } = false;
        public int MaxStampAgeHours { get; set; } = 72; // 3 days
    }

    /// <summary>
    /// Comprehensive explainability stamp for a trading decision
    /// </summary>
    public class ExplainabilityStamp
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public DecisionEvidence Evidence { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Comprehensive evidence for a trading decision
    /// </summary>
    public class DecisionEvidence
    {
        public double Confidence { get; set; }
        public ZoneScoreEvidence ZoneScore { get; set; } = new();
        public PatternProbabilities PatternProbabilities { get; set; } = new();
        public S7StateEvidence S7State { get; set; } = new();
        public ExecutionReadiness ExecutionReadiness { get; set; } = new();
        public RiskAdjustments RiskAdjustments { get; set; } = new();
        public ModelInputs ModelInputs { get; set; } = new();
        public MarketConditions MarketConditions { get; set; } = new();
        public List<string> Qualifiers { get; set; } = new();
    }

    /// <summary>
    /// Zone score evidence
    /// </summary>
    public class ZoneScoreEvidence
    {
        public string PrimaryZone { get; set; } = string.Empty;
        public double ZoneStrength { get; set; }
        public string ZoneType { get; set; } = string.Empty;
        public decimal SupportResistanceLevel { get; set; }
        public double ZoneConfidence { get; set; }
        public TimeSpan TimeInZone { get; set; }
    }

    /// <summary>
    /// Pattern probabilities
    /// </summary>
    public class PatternProbabilities
    {
        public double BullishProbability { get; set; }
        public double BearishProbability { get; set; }
        public double NeutralProbability { get; set; }
        public double BreakoutProbability { get; set; }
        public double ReversalProbability { get; set; }
        public double ContinuationProbability { get; set; }
        public string PrimaryPattern { get; set; } = string.Empty;
        public double PatternConfidence { get; set; }
        public bool ShadowSignal { get; set; }
    }

    /// <summary>
    /// S7 state evidence
    /// </summary>
    public class S7StateEvidence
    {
        public string CurrentState { get; set; } = string.Empty;
        public double StateConfidence { get; set; }
        public double TransitionProbability { get; set; }
        public string PreviousState { get; set; } = string.Empty;
        public List<string> StateHistory { get; set; } = new();
        public double AlignmentScore { get; set; }
        public string GateStatus { get; set; } = string.Empty;
    }

    /// <summary>
    /// Execution readiness
    /// </summary>
    public class ExecutionReadiness
    {
        public double LiquidityScore { get; set; }
        public double SpreadQuality { get; set; }
        public double OrderBookDepth { get; set; }
        public double MarketImpactEstimate { get; set; }
        public decimal ExecutionCostEstimate { get; set; }
        public string OptimalOrderType { get; set; } = string.Empty;
        public int QueuePosition { get; set; }
        public TimeSpan EstimatedFillTime { get; set; }
    }

    /// <summary>
    /// Risk adjustments
    /// </summary>
    public class RiskAdjustments
    {
        public double PositionSizeAdjustment { get; set; }
        public double VolatilityAdjustment { get; set; }
        public double CorrelationAdjustment { get; set; }
        public double DrawdownAdjustment { get; set; }
        public double TimeOfDayAdjustment { get; set; }
        public decimal MaxRiskPerTrade { get; set; }
        public decimal RiskBudgetRemaining { get; set; }
        public double OverallRiskScore { get; set; }
    }

    /// <summary>
    /// Model inputs
    /// </summary>
    public class ModelInputs
    {
        public int FeatureCount { get; set; }
        public List<string> MissingFeatures { get; set; } = new();
        public double FeatureDriftScore { get; set; }
        public double ModelLatency { get; set; }
        public Dictionary<string, double> EnsembleWeights { get; set; } = new();
        public List<string> TopFeatures { get; set; } = new();
        public Dictionary<string, double> FeatureImportance { get; set; } = new();
    }

    /// <summary>
    /// Market conditions
    /// </summary>
    public class MarketConditions
    {
        public double Volatility { get; set; }
        public long Volume { get; set; }
        public decimal Spread { get; set; }
        public string MarketRegime { get; set; } = string.Empty;
        public string TimeOfDay { get; set; } = string.Empty;
        public string TradingSession { get; set; } = string.Empty;
        public List<string> EconomicEvents { get; set; } = new();
        public double MarketSentiment { get; set; }
    }

    /// <summary>
    /// Decision audit trail for analysis
    /// </summary>
    public class DecisionAuditTrail
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public List<ExplainabilityStamp> Decisions { get; set; } = new();
        public int TotalDecisions { get; set; }
        public int GoDecisions { get; set; }
        public int HoldDecisions { get; set; }
        public double AverageConfidence { get; set; }
    }

    /// <summary>
    /// Context for trading decisions containing all relevant information
    /// </summary>
    public class DecisionContext
    {
        public string? Strategy { get; set; }
        public string? ModelVersion { get; set; }
        public string? ExecutionMode { get; set; }
        public string? RiskMode { get; set; }
        public string? DataQuality { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}