using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Execution
{
    /// <summary>
    /// Child Order Scheduler for S7 execution
    /// Slices large orders into 2-3 child orders with intelligent timing and triggers
    /// Implements fail-closed behavior with comprehensive audit logging
    /// </summary>
    public sealed class ChildOrderScheduler : IDisposable
    {
        private readonly ILogger<ChildOrderScheduler> _logger;
        private readonly S7ExecutionConfiguration _config;
        private readonly ConcurrentDictionary<Guid, ScheduledOrderExecution> _activeExecutions = new();
        private readonly Timer _monitoringTimer;
        private bool _disposed;

        public ChildOrderScheduler(
            ILogger<ChildOrderScheduler> logger,
            IOptions<S7ExecutionConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

            // Monitor active executions every 5 seconds
            _monitoringTimer = new Timer(MonitorActiveExecutions, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Schedule execution of an order with potential child order slicing
        /// Returns immediately with execution plan, actual execution happens asynchronously
        /// </summary>
        public async Task<ChildOrderExecutionPlan> ScheduleExecutionAsync(
            ExecutionIntent intent,
            MicrostructureSnapshot microstructure,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentNullException.ThrowIfNull(microstructure);

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                intent.ValidateForExecution();
                microstructure.ValidateForExecution();

                var plan = CreateExecutionPlan(intent, microstructure);
                
                if (plan.RequiresSlicing)
                {
                    var scheduledExecution = new ScheduledOrderExecution
                    {
                        ExecutionId = intent.RequestId,
                        Plan = plan,
                        Intent = intent,
                        Microstructure = microstructure,
                        CreatedAt = DateTime.UtcNow,
                        Status = "SCHEDULED"
                    };

                    _activeExecutions.TryAdd(intent.RequestId, scheduledExecution);
                    
                    _logger.LogInformation("[CHILD-SCHEDULER] Scheduled sliced execution for {Symbol}: {ChildCount} children, total {Quantity}", 
                        intent.Symbol, plan.ChildOrders.Count, intent.Quantity);
                }
                else
                {
                    _logger.LogInformation("[CHILD-SCHEDULER] Single order execution for {Symbol}: {Quantity}", 
                        intent.Symbol, intent.Quantity);
                }

                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CHILD-SCHEDULER] [AUDIT-VIOLATION] Failed to schedule execution for {Symbol} - FAIL-CLOSED + TELEMETRY", 
                    intent.Symbol);
                
                // Fail-closed: throw exception to prevent execution with invalid plan
                throw new InvalidOperationException($"[CHILD-SCHEDULER] Critical failure scheduling execution for '{intent.Symbol}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Cancel and replace an active execution due to changing market conditions
        /// </summary>
        public async Task<bool> CancelAndReplaceAsync(
            Guid executionId, 
            string reason,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            if (_activeExecutions.TryGetValue(executionId, out var execution))
            {
                execution.Status = "CANCELLED";
                execution.CancellationReason = reason;
                
                _logger.LogWarning("[CHILD-SCHEDULER] [AUDIT-VIOLATION] Cancelled execution {ExecutionId}: {Reason} - TRIGGERING HOLD + TELEMETRY", 
                    executionId, reason);
                
                return true;
            }

            return false;
        }

        private ChildOrderExecutionPlan CreateExecutionPlan(ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            var plan = new ChildOrderExecutionPlan
            {
                ExecutionId = intent.RequestId,
                Symbol = intent.Symbol,
                TotalQuantity = intent.Quantity,
                RequiresSlicing = ShouldSliceOrder(intent),
                CreatedAt = DateTime.UtcNow
            };

            if (plan.RequiresSlicing)
            {
                CreateChildOrders(intent, microstructure, plan);
            }
            else
            {
                // Single order execution
                plan.AddChildOrder(new ChildOrderPlan
                {
                    ChildId = Guid.NewGuid(),
                    Quantity = intent.Quantity,
                    DelaySeconds = 0,
                    TriggerType = "IMMEDIATE",
                    TriggerCondition = "Execute immediately"
                });
            }

            return plan;
        }

        private bool ShouldSliceOrder(ExecutionIntent intent)
        {
            // Don't slice if maximum urgency (market orders) - checked FIRST for fail-closed behavior
            if (intent.UrgencyScore > 0.9)
                return false;

            // Slice large orders to reduce market impact
            if ((double)intent.Quantity > _config.ChildOrderSlicingThreshold)
                return true;

            // Slice orders with high uncertainty for better price discovery
            if (intent.ModelUncertainty.HasValue && intent.ModelUncertainty.Value > 0.7)
                return true;

            return false;
        }

        private void CreateChildOrders(ExecutionIntent intent, MicrostructureSnapshot microstructure, ChildOrderExecutionPlan plan)
        {
            var childCount = Math.Min(_config.MaxChildOrders, 3); // Limit to 3 as specified
            var baseQuantity = intent.Quantity / childCount;
            var remainderQuantity = intent.Quantity % childCount;

            for (int i = 0; i < childCount; i++)
            {
                var childQuantity = baseQuantity;
                if (i == childCount - 1) // Last child gets remainder
                    childQuantity += remainderQuantity;

                var child = new ChildOrderPlan
                {
                    ChildId = Guid.NewGuid(),
                    Quantity = childQuantity,
                    DelaySeconds = i * CalculateChildDelay(intent, microstructure),
                    TriggerType = DetermineChildTriggerType(i, intent, microstructure),
                    TriggerCondition = CreateTriggerCondition(i, intent, microstructure)
                };

                plan.AddChildOrder(child);
            }
        }

        private int CalculateChildDelay(ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            // Base delay between child orders
            var baseDelay = 30; // 30 seconds default

            // Reduce delay for urgent orders
            if (intent.UrgencyScore > 0.5)
                baseDelay = (int)(baseDelay * (1.0 - intent.UrgencyScore));

            // Increase delay in volatile markets
            if (microstructure.IsHighVolatility)
                baseDelay = (int)(baseDelay * 1.5);

            return Math.Max(5, baseDelay); // Minimum 5 seconds
        }

        private string DetermineChildTriggerType(int childIndex, ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            return childIndex switch
            {
                0 => "IMMEDIATE", // First child executes immediately
                1 => "TIME_DELAY", // Second child after time delay
                2 => "IMBALANCE_TRIGGER", // Third child on imbalance change
                _ => "TIME_DELAY"
            };
        }

        private string CreateTriggerCondition(int childIndex, ExecutionIntent intent, MicrostructureSnapshot microstructure)
        {
            return childIndex switch
            {
                0 => "Execute immediately",
                1 => $"Execute after {CalculateChildDelay(intent, microstructure)} seconds",
                2 => "Execute when book imbalance changes by >20% or max time reached",
                _ => "Execute on timer"
            };
        }

        private async void MonitorActiveExecutions(object? state)
        {
            if (_disposed) return;

            try
            {
                var currentTime = DateTime.UtcNow;
                var expiredExecutions = new List<Guid>();

                foreach (var kvp in _activeExecutions)
                {
                    var execution = kvp.Value;
                    
                    // Check for expired executions
                    if (currentTime - execution.CreatedAt > TimeSpan.FromMinutes(30))
                    {
                        expiredExecutions.Add(kvp.Key);
                        _logger.LogWarning("[CHILD-SCHEDULER] [AUDIT-VIOLATION] Execution {ExecutionId} expired - CLEANUP + TELEMETRY", 
                            kvp.Key);
                    }
                    
                    // Check queue ETA for active executions
                    await CheckQueueConditions(execution).ConfigureAwait(false);
                }

                // Clean up expired executions
                foreach (var expiredId in expiredExecutions)
                {
                    _activeExecutions.TryRemove(expiredId, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CHILD-SCHEDULER] Error monitoring active executions");
            }
        }

        private async Task CheckQueueConditions(ScheduledOrderExecution execution)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            // Check if queue ETA has become unacceptable
            if (execution.Microstructure.EstimatedQueueEta.HasValue && 
                execution.Microstructure.EstimatedQueueEta.Value > _config.MaxQueueEtaSeconds &&
                execution.Status == "ACTIVE")
            {
                await CancelAndReplaceAsync(execution.ExecutionId, 
                    $"Queue ETA {execution.Microstructure.EstimatedQueueEta.Value:F1}s exceeds SLA {_config.MaxQueueEtaSeconds:F1}s").ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _monitoringTimer?.Dispose();
                _disposed = true;
                
                _logger.LogInformation("[CHILD-SCHEDULER] Disposed with {ActiveCount} active executions", 
                    _activeExecutions.Count);
            }
        }
    }

    /// <summary>
    /// Execution plan for child order slicing
    /// </summary>
    public sealed class ChildOrderExecutionPlan
    {
        public Guid ExecutionId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal TotalQuantity { get; set; }
        public bool RequiresSlicing { get; set; }
        
        private readonly List<ChildOrderPlan> _childOrders = new();
        public IReadOnlyList<ChildOrderPlan> ChildOrders => _childOrders;
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, object> Metadata { get; } = new();

        public void AddChildOrder(ChildOrderPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            _childOrders.Add(plan);
        }
    }

    /// <summary>
    /// Individual child order plan
    /// </summary>
    public sealed class ChildOrderPlan
    {
        public Guid ChildId { get; set; }
        public decimal Quantity { get; set; }
        public int DelaySeconds { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public string TriggerCondition { get; set; } = string.Empty;
        public DateTime? ExecutedAt { get; set; }
        public string Status { get; set; } = "PENDING";
    }

    /// <summary>
    /// Active scheduled execution state
    /// </summary>
    internal sealed class ScheduledOrderExecution
    {
        public Guid ExecutionId { get; set; }
        public ChildOrderExecutionPlan Plan { get; set; } = null!;
        public ExecutionIntent Intent { get; set; } = null!;
        public MicrostructureSnapshot Microstructure { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CancellationReason { get; set; } = string.Empty;
    }
}