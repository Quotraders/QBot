using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Abstractions;

namespace BotCore.Execution
{
    /// <summary>
    /// S7 Order Type Selector - intelligent order type selection for S7 strategy
    /// Implements audit-clean execution logic with fail-closed behavior
    /// Uses maker Limit/PostOnly when appropriate, escalates to aggressive orders when needed
    /// </summary>
    public sealed class S7OrderTypeSelector
    {
        private readonly ILogger<S7OrderTypeSelector> _logger;
        private readonly S7ExecutionConfiguration _config;

        public S7OrderTypeSelector(
            ILogger<S7OrderTypeSelector> logger,
            IOptions<S7ExecutionConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Resolve optimal order type based on execution intent and microstructure
        /// Implements fail-closed decision logic with comprehensive audit logging
        /// </summary>
        public async Task<OrderTypeRecommendation> ResolveOrderTypeAsync(
            ExecutionIntent intent, 
            MicrostructureSnapshot microstructure, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentNullException.ThrowIfNull(microstructure);

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                // Validate inputs for audit compliance
                intent.ValidateForExecution();
                microstructure.ValidateForExecution();

                // Start with base recommendation
                var recommendation = new OrderTypeRecommendation
                {
                    RequestId = intent.RequestId,
                    Symbol = intent.Symbol,
                    RecommendedOrderType = "LIMIT",
                    RecommendedPrice = CalculatePrice(intent, microstructure),
                    Timestamp = DateTime.UtcNow
                };

                // Apply S7-specific decision logic
                ApplyS7RiskStateLogic(intent, microstructure, recommendation);
                ApplyZoneBasedLogic(intent, microstructure, recommendation);
                ApplyBreakoutLogic(intent, microstructure, recommendation);
                ApplyLatencyLogic(intent, microstructure, recommendation);
                ApplyQueueLogic(intent, microstructure, recommendation);

                // Final validation and audit logging
                recommendation.ValidateRecommendation();
                
                _logger.LogInformation("[S7-ORDER-SELECTOR] {Symbol} {Side}: {OrderType} @ {Price:F2} - Reasoning: {Reasoning}", 
                    intent.Symbol, intent.Side, recommendation.RecommendedOrderType, 
                    recommendation.RecommendedPrice, string.Join("; ", recommendation.Reasoning));

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7-ORDER-SELECTOR] [AUDIT-VIOLATION] Order type selection failed for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    intent.Symbol);
                
                // Fail-closed: throw exception to prevent execution with unknown order type
                throw new InvalidOperationException($"[S7-ORDER-SELECTOR] Critical failure selecting order type for '{intent.Symbol}': {ex.Message}", ex);
            }
        }

        private void ApplyS7RiskStateLogic(ExecutionIntent intent, MicrostructureSnapshot microstructure, OrderTypeRecommendation recommendation)
        {
            switch (intent.S7RiskState?.ToUpperInvariant())
            {
                case "RISKON":
                    // RiskOn state requires aggressive execution
                    if (intent.UrgencyScore > _config.RiskOnUrgencyThreshold)
                    {
                        recommendation.RecommendedOrderType = "IOC";
                        recommendation.AddReasoning($"S7 RiskOn state with urgency {intent.UrgencyScore:F2} > threshold {_config.RiskOnUrgencyThreshold:F2}");
                    }
                    else
                    {
                        recommendation.RecommendedOrderType = "LIMIT";
                        recommendation.RecommendedPrice = CalculateAggressivePrice(intent, microstructure);
                        recommendation.AddReasoning("S7 RiskOn state with marketable limit");
                    }
                    break;

                case "RISKOFF":
                    // RiskOff state favors conservative maker orders
                    recommendation.RecommendedOrderType = "POST_ONLY";
                    recommendation.AddReasoning("S7 RiskOff state favors maker orders");
                    break;

                case "TRANSITION":
                    // Transition state uses moderate aggression
                    recommendation.RecommendedOrderType = "LIMIT";
                    recommendation.AddReasoning("S7 Transition state uses standard limit orders");
                    break;

                default:
                    _logger.LogWarning("[S7-ORDER-SELECTOR] [AUDIT-VIOLATION] Unknown S7RiskState: {RiskState} for {Symbol}", 
                        intent.S7RiskState, intent.Symbol);
                    recommendation.AddReasoning($"Unknown S7RiskState: {intent.S7RiskState}");
                    break;
            }
        }

        private void ApplyZoneBasedLogic(ExecutionIntent intent, MicrostructureSnapshot microstructure, OrderTypeRecommendation recommendation)
        {
            // Inside zone bands favors maker orders when not in RiskOn state
            if (microstructure.ZoneRole == "NEUTRAL" && intent.S7RiskState != "RISKON")
            {
                if (recommendation.RecommendedOrderType != "IOC") // Don't override IOC from RiskOn
                {
                    recommendation.RecommendedOrderType = "POST_ONLY";
                    recommendation.AddReasoning("Inside zone bands with neutral role favors maker orders");
                }
            }

            // Near zone boundaries requires careful consideration
            if (microstructure.DistanceToZoneAtr.HasValue && (double)microstructure.DistanceToZoneAtr.Value < _config.ZoneProximityThresholdAtr)
            {
                var distance = microstructure.DistanceToZoneAtr.Value;
                recommendation.AddReasoning($"Near zone boundary (distance: {distance:F2} ATR)");
            }
        }

        private void ApplyBreakoutLogic(ExecutionIntent intent, MicrostructureSnapshot microstructure, OrderTypeRecommendation recommendation)
        {
            if (microstructure.ZoneBreakoutScore.HasValue && microstructure.ZoneBreakoutScore.Value > _config.BreakoutScoreThreshold)
            {
                // High breakout probability requires aggressive execution
                recommendation.RecommendedOrderType = "IOC";
                recommendation.RecommendedPrice = CalculateAggressivePrice(intent, microstructure);
                recommendation.AddReasoning($"High breakout score {microstructure.ZoneBreakoutScore.Value:F3} > threshold {_config.BreakoutScoreThreshold:F3}");
            }
        }

        private static void ApplyLatencyLogic(ExecutionIntent intent, MicrostructureSnapshot microstructure, OrderTypeRecommendation recommendation)
        {
            if (intent.LatencyGuardBreached)
            {
                // Latency guard breach requires fastest execution
                recommendation.RecommendedOrderType = "MARKET";
                recommendation.AddReasoning("Latency guard breached - using market orders");
            }
        }

        private void ApplyQueueLogic(ExecutionIntent intent, MicrostructureSnapshot microstructure, OrderTypeRecommendation recommendation)
        {
            if (microstructure.EstimatedQueueEta.HasValue && microstructure.EstimatedQueueEta.Value > _config.MaxQueueEtaSeconds)
            {
                // Poor queue conditions require aggressive execution
                if (recommendation.RecommendedOrderType == "POST_ONLY" || recommendation.RecommendedOrderType == "LIMIT")
                {
                    recommendation.RecommendedOrderType = "IOC";
                    recommendation.RecommendedPrice = CalculateAggressivePrice(intent, microstructure);
                }
                recommendation.AddReasoning($"Queue ETA {microstructure.EstimatedQueueEta.Value:F1}s > SLA {_config.MaxQueueEtaSeconds:F1}s");
            }
        }

        private static decimal CalculatePrice(ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            // Default to mid-price for limit orders
            return microstructure.MidPrice;
        }

        private decimal CalculateAggressivePrice(ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            // Add small offset to ensure execution for aggressive orders
            var offset = microstructure.MidPrice * (decimal)_config.AggressivePriceOffsetBps / 10000m;
            
            return intent.Side.ToUpperInvariant() switch
            {
                "BUY" => microstructure.AskPrice + offset,
                "SELL" => microstructure.BidPrice - offset,
                _ => microstructure.MidPrice
            };
        }
    }

    /// <summary>
    /// Configuration for S7 execution behavior
    /// All parameters must be config-driven with bounds validation
    /// </summary>
    public sealed class S7ExecutionConfiguration
    {
        public double RiskOnUrgencyThreshold { get; set; } = 0.7;
        public double BreakoutScoreThreshold { get; set; } = 0.8;
        public double MaxQueueEtaSeconds { get; set; } = 30.0;
        public double ZoneProximityThresholdAtr { get; set; } = 1.0;
        public double AggressivePriceOffsetBps { get; set; } = 1.0;
        public int MaxChildOrders { get; set; } = 3;
        public double ChildOrderSlicingThreshold { get; set; } = 1000.0; // Minimum size to slice
    }

    /// <summary>
    /// Order type recommendation with audit trail
    /// </summary>
    public sealed class OrderTypeRecommendation
    {
        private readonly List<string> _reasoning = new();
        private readonly Dictionary<string, object> _metadata = new();

        public Guid RequestId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string RecommendedOrderType { get; set; } = string.Empty;
        public decimal RecommendedPrice { get; set; }
        public IReadOnlyList<string> Reasoning => _reasoning;
        public DateTime Timestamp { get; set; }
        public IReadOnlyDictionary<string, object> Metadata => _metadata;

        // Allow controlled mutation of reasoning during construction
        internal void AddReasoning(string reason) => _reasoning.Add(reason);
        internal void AddMetadata(string key, object value) => _metadata[key] = value;

        public void ValidateRecommendation()
        {
            if (string.IsNullOrWhiteSpace(Symbol))
                throw new InvalidOperationException("[ORDER-TYPE-RECOMMENDATION] Symbol is required");
            if (string.IsNullOrWhiteSpace(RecommendedOrderType))
                throw new InvalidOperationException("[ORDER-TYPE-RECOMMENDATION] RecommendedOrderType is required");
            if (RecommendedPrice <= 0)
                throw new InvalidOperationException("[ORDER-TYPE-RECOMMENDATION] RecommendedPrice must be positive");
            if (Reasoning.Count == 0)
                throw new InvalidOperationException("[ORDER-TYPE-RECOMMENDATION] Reasoning is required for audit compliance");
        }
    }
}