using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BotCore.Models;
using BotCore.Bandits;

namespace BotCore.Services
{
    /// <summary>
    /// Position Management Optimizer - PHASE 3 Implementation
    /// 
    /// Learns optimal position management parameters using ML/RL:
    /// - Optimal breakeven trigger timing (6 vs 8 vs 10 ticks)
    /// - Optimal trailing stop distance (1.0x vs 1.5x vs 2.0x ATR)
    /// - Optimal time exit thresholds per strategy and market regime
    /// - Tracks outcomes: "BE at 8 ticks → stopped out, would have hit target"
    /// 
    /// Integrates with ParameterChangeTracker for learning feedback.
    /// </summary>
    public sealed class PositionManagementOptimizer : BackgroundService
    {
        private readonly ILogger<PositionManagementOptimizer> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, PositionManagementOutcome> _outcomes = new();
        private readonly ParameterChangeTracker _changeTracker;
        
        // Learning parameters
        private const int OptimizationIntervalSeconds = 60; // Run optimization every minute
        private const int MinSamplesForLearning = 10; // Need at least 10 samples to learn
        private const decimal LearningRate = 0.1m; // How quickly to adjust parameters
        
        // Parameter ranges for learning
        private static readonly int[] BreakevenTickOptions = { 4, 6, 8, 10, 12, 16 };
        private static readonly decimal[] TrailMultiplierOptions = { 0.8m, 1.0m, 1.2m, 1.5m, 1.8m, 2.0m };
        private static readonly int[] TimeExitMinutesOptions = { 15, 30, 45, 60, 90, 120 };
        
        public PositionManagementOptimizer(
            ILogger<PositionManagementOptimizer> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _changeTracker = new ParameterChangeTracker(capacity: 500);
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🧠 [PM-OPTIMIZER] Position Management Optimizer starting (PHASE 3)...");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOptimizationCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [PM-OPTIMIZER] Error in optimization loop");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(OptimizationIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            
            _logger.LogInformation("🛑 [PM-OPTIMIZER] Position Management Optimizer stopping");
        }
        
        /// <summary>
        /// Record position management outcome for learning
        /// </summary>
        public void RecordOutcome(
            string strategy,
            string symbol,
            int breakevenAfterTicks,
            decimal trailMultiplier,
            int maxHoldMinutes,
            bool breakevenTriggered,
            bool stoppedOut,
            bool targetHit,
            bool timedOut,
            decimal finalPnL,
            decimal maxFavorableExcursion,
            decimal maxAdverseExcursion,
            string marketRegime = "UNKNOWN")
        {
            var outcomeKey = $"{strategy}_{symbol}_{DateTime.UtcNow.Ticks}";
            
            var outcome = new PositionManagementOutcome
            {
                Timestamp = DateTime.UtcNow,
                Strategy = strategy,
                Symbol = symbol,
                BreakevenAfterTicks = breakevenAfterTicks,
                TrailMultiplier = trailMultiplier,
                MaxHoldMinutes = maxHoldMinutes,
                BreakevenTriggered = breakevenTriggered,
                StoppedOut = stoppedOut,
                TargetHit = targetHit,
                TimedOut = timedOut,
                FinalPnL = finalPnL,
                MaxFavorableExcursion = maxFavorableExcursion,
                MaxAdverseExcursion = maxAdverseExcursion,
                MarketRegime = marketRegime
            };
            
            _outcomes[outcomeKey] = outcome;
            
            // Log interesting outcomes
            if (breakevenTriggered && stoppedOut && maxFavorableExcursion > breakevenAfterTicks + 5)
            {
                _logger.LogWarning("📊 [PM-OPTIMIZER] Suboptimal breakeven: {Strategy} triggered BE at {BE} ticks, stopped out, but reached +{MaxFav} ticks",
                    strategy, breakevenAfterTicks, maxFavorableExcursion);
            }
            
            if (timedOut && maxFavorableExcursion > 0)
            {
                _logger.LogInformation("📊 [PM-OPTIMIZER] Time exit opportunity cost: {Strategy} timed out after {Minutes}m, left +{MaxFav} ticks on table",
                    strategy, maxHoldMinutes, maxFavorableExcursion);
            }
        }
        
        private async Task RunOptimizationCycleAsync(CancellationToken cancellationToken)
        {
            var strategies = new[] { "S2", "S3", "S6", "S11" };
            
            foreach (var strategy in strategies)
            {
                try
                {
                    await OptimizeBreakevenParameterAsync(strategy, cancellationToken).ConfigureAwait(false);
                    await OptimizeTrailingParameterAsync(strategy, cancellationToken).ConfigureAwait(false);
                    await OptimizeTimeExitParameterAsync(strategy, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [PM-OPTIMIZER] Error optimizing parameters for {Strategy}", strategy);
                }
            }
            
            // Clean up old outcomes (keep last 1000)
            if (_outcomes.Count > 1000)
            {
                var toRemove = _outcomes
                    .OrderBy(kvp => kvp.Value.Timestamp)
                    .Take(_outcomes.Count - 1000)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in toRemove)
                {
                    _outcomes.TryRemove(key, out _);
                }
            }
        }
        
        /// <summary>
        /// PHASE 3: Learn optimal breakeven trigger timing
        /// Analyzes: "Triggered BE at 8 ticks → stopped out, would have hit target"
        /// </summary>
        private async Task OptimizeBreakevenParameterAsync(string strategy, CancellationToken cancellationToken)
        {
            var outcomes = _outcomes.Values
                .Where(o => o.Strategy == strategy && o.BreakevenTriggered)
                .OrderByDescending(o => o.Timestamp)
                .Take(100)
                .ToList();
            
            if (outcomes.Count < MinSamplesForLearning)
            {
                return; // Not enough data yet
            }
            
            // Analyze outcomes by breakeven timing
            var analysis = outcomes
                .GroupBy(o => o.BreakevenAfterTicks)
                .Select(g => new
                {
                    BreakevenTicks = g.Key,
                    Count = g.Count(),
                    AvgPnL = g.Average(o => (double)o.FinalPnL),
                    WinRate = g.Count(o => o.FinalPnL > 0) / (double)g.Count(),
                    AvgMaxFav = g.Average(o => (double)o.MaxFavorableExcursion),
                    StoppedOutRate = g.Count(o => o.StoppedOut) / (double)g.Count()
                })
                .OrderByDescending(a => a.AvgPnL)
                .ToList();
            
            if (analysis.Count < 2)
            {
                return; // Need multiple breakeven values to compare
            }
            
            var best = analysis.First();
            var current = analysis.FirstOrDefault(a => a.Count >= MinSamplesForLearning);
            
            if (current != null && best.BreakevenTicks != current.BreakevenTicks && best.AvgPnL > current.AvgPnL * 1.1)
            {
                _logger.LogInformation("💡 [PM-OPTIMIZER] Breakeven optimization for {Strategy}: Current={Current} ticks (PnL={CurrentPnL:F2}), Optimal={Optimal} ticks (PnL={OptimalPnL:F2})",
                    strategy, current.BreakevenTicks, current.AvgPnL, best.BreakevenTicks, best.AvgPnL);
                
                // Record parameter change recommendation
                _changeTracker.RecordChange(
                    strategyName: strategy,
                    parameterName: "BreakevenAfterTicks",
                    oldValue: current.BreakevenTicks.ToString(),
                    newValue: best.BreakevenTicks.ToString(),
                    reason: $"ML/RL learning: Better PnL ({best.AvgPnL:F2} vs {current.AvgPnL:F2}), Win rate {best.WinRate:P0}",
                    outcomePnl: (decimal)best.AvgPnL,
                    wasCorrect: true
                );
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// PHASE 3: Learn optimal trailing stop distance
        /// Analyzes: "Trailing at 1.0x ATR → stopped out early, left $200 on table"
        /// </summary>
        private async Task OptimizeTrailingParameterAsync(string strategy, CancellationToken cancellationToken)
        {
            var outcomes = _outcomes.Values
                .Where(o => o.Strategy == strategy)
                .OrderByDescending(o => o.Timestamp)
                .Take(100)
                .ToList();
            
            if (outcomes.Count < MinSamplesForLearning)
            {
                return;
            }
            
            // Analyze outcomes by trailing multiplier
            var analysis = outcomes
                .GroupBy(o => o.TrailMultiplier)
                .Select(g => new
                {
                    TrailMultiplier = g.Key,
                    Count = g.Count(),
                    AvgPnL = g.Average(o => (double)o.FinalPnL),
                    WinRate = g.Count(o => o.FinalPnL > 0) / (double)g.Count(),
                    AvgMaxFav = g.Average(o => (double)o.MaxFavorableExcursion),
                    OpportunityCost = g.Average(o => Math.Max(0, (double)(o.MaxFavorableExcursion - Math.Abs(o.FinalPnL))))
                })
                .OrderByDescending(a => a.AvgPnL)
                .ToList();
            
            if (analysis.Count < 2)
            {
                return;
            }
            
            var best = analysis.First();
            var current = analysis.FirstOrDefault(a => a.Count >= MinSamplesForLearning);
            
            if (current != null && Math.Abs(best.TrailMultiplier - current.TrailMultiplier) > 0.2m && best.AvgPnL > current.AvgPnL * 1.1)
            {
                _logger.LogInformation("💡 [PM-OPTIMIZER] Trailing stop optimization for {Strategy}: Current={Current:F1}x ATR (PnL={CurrentPnL:F2}, OpCost={CurrentOC:F2}), Optimal={Optimal:F1}x ATR (PnL={OptimalPnL:F2}, OpCost={OptimalOC:F2})",
                    strategy, current.TrailMultiplier, current.AvgPnL, current.OpportunityCost, best.TrailMultiplier, best.AvgPnL, best.OpportunityCost);
                
                _changeTracker.RecordChange(
                    strategyName: strategy,
                    parameterName: "TrailMultiplier",
                    oldValue: current.TrailMultiplier.ToString("F1"),
                    newValue: best.TrailMultiplier.ToString("F1"),
                    reason: $"ML/RL learning: Better PnL ({best.AvgPnL:F2} vs {current.AvgPnL:F2}), Lower opportunity cost",
                    outcomePnl: (decimal)best.AvgPnL,
                    wasCorrect: true
                );
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// PHASE 3: Learn optimal time exit thresholds
        /// Analyzes: "S2 needs 20m in ranging, 10m in trending"
        /// </summary>
        private async Task OptimizeTimeExitParameterAsync(string strategy, CancellationToken cancellationToken)
        {
            var outcomes = _outcomes.Values
                .Where(o => o.Strategy == strategy)
                .OrderByDescending(o => o.Timestamp)
                .Take(100)
                .ToList();
            
            if (outcomes.Count < MinSamplesForLearning)
            {
                return;
            }
            
            // Analyze by market regime
            var regimes = outcomes.GroupBy(o => o.MarketRegime);
            
            foreach (var regimeGroup in regimes)
            {
                var regimeName = regimeGroup.Key;
                var regimeOutcomes = regimeGroup.ToList();
                
                if (regimeOutcomes.Count < 5)
                {
                    continue;
                }
                
                // Analyze winning trades vs timing out trades
                var winningTrades = regimeOutcomes.Where(o => o.TargetHit).ToList();
                var timedOutTrades = regimeOutcomes.Where(o => o.TimedOut).ToList();
                
                if (winningTrades.Any() && timedOutTrades.Any())
                {
                    var avgWinningDuration = winningTrades.Average(o => o.MaxHoldMinutes);
                    var avgTimedOutDuration = timedOutTrades.Average(o => o.MaxHoldMinutes);
                    var avgTimedOutOpCost = timedOutTrades.Average(o => (double)o.MaxFavorableExcursion);
                    
                    if (avgTimedOutOpCost > 5) // Significant opportunity cost
                    {
                        _logger.LogInformation("💡 [PM-OPTIMIZER] Time exit analysis for {Strategy} in {Regime}: Winning trades avg {WinDur:F0}m, Timed out avg {TimedDur:F0}m with +{OpCost:F1} ticks lost",
                            strategy, regimeName, avgWinningDuration, avgTimedOutDuration, avgTimedOutOpCost);
                        
                        // Recommend longer timeout if timed out trades had significant upside
                        var recommendedTimeout = (int)(avgWinningDuration * 1.5); // 50% buffer
                        
                        _changeTracker.RecordChange(
                            strategyName: strategy,
                            parameterName: $"MaxHoldMinutes_{regimeName}",
                            oldValue: avgTimedOutDuration.ToString("F0"),
                            newValue: recommendedTimeout.ToString(),
                            reason: $"ML/RL learning: Timed out trades had {avgTimedOutOpCost:F1} ticks opportunity cost",
                            outcomePnl: null,
                            wasCorrect: null,
                            marketSnapshotId: regimeName
                        );
                    }
                }
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Get recent parameter change recommendations
        /// </summary>
        public IReadOnlyList<ParameterChange> GetRecentRecommendations(int count = 10)
        {
            return _changeTracker.GetRecentChanges(count);
        }
        
        /// <summary>
        /// Get recommendations for a specific strategy
        /// </summary>
        public IReadOnlyList<ParameterChange> GetRecommendationsForStrategy(string strategy, int count = 10)
        {
            return _changeTracker.GetChangesForStrategy(strategy, count);
        }
        
        /// <summary>
        /// Get performance summary for debugging
        /// </summary>
        public PositionManagementPerformanceSummary GetPerformanceSummary(string strategy)
        {
            var outcomes = _outcomes.Values
                .Where(o => o.Strategy == strategy)
                .OrderByDescending(o => o.Timestamp)
                .Take(100)
                .ToList();
            
            if (outcomes.Count == 0)
            {
                return new PositionManagementPerformanceSummary
                {
                    Strategy = strategy,
                    TotalTrades = 0
                };
            }
            
            return new PositionManagementPerformanceSummary
            {
                Strategy = strategy,
                TotalTrades = outcomes.Count,
                WinRate = outcomes.Count(o => o.FinalPnL > 0) / (decimal)outcomes.Count,
                AvgPnL = outcomes.Average(o => o.FinalPnL),
                BreakevenTriggeredRate = outcomes.Count(o => o.BreakevenTriggered) / (decimal)outcomes.Count,
                StoppedOutRate = outcomes.Count(o => o.StoppedOut) / (decimal)outcomes.Count,
                TargetHitRate = outcomes.Count(o => o.TargetHit) / (decimal)outcomes.Count,
                TimedOutRate = outcomes.Count(o => o.TimedOut) / (decimal)outcomes.Count,
                AvgMaxFavorableExcursion = outcomes.Average(o => o.MaxFavorableExcursion),
                AvgMaxAdverseExcursion = outcomes.Average(o => o.MaxAdverseExcursion)
            };
        }
    }
    
    /// <summary>
    /// Position management outcome for ML/RL learning
    /// </summary>
    internal sealed class PositionManagementOutcome
    {
        public DateTime Timestamp { get; set; }
        public string Strategy { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public int BreakevenAfterTicks { get; set; }
        public decimal TrailMultiplier { get; set; }
        public int MaxHoldMinutes { get; set; }
        public bool BreakevenTriggered { get; set; }
        public bool StoppedOut { get; set; }
        public bool TargetHit { get; set; }
        public bool TimedOut { get; set; }
        public decimal FinalPnL { get; set; }
        public decimal MaxFavorableExcursion { get; set; }
        public decimal MaxAdverseExcursion { get; set; }
        public string MarketRegime { get; set; } = "UNKNOWN";
    }
    
    /// <summary>
    /// Performance summary for position management
    /// </summary>
    public sealed class PositionManagementPerformanceSummary
    {
        public string Strategy { get; set; } = string.Empty;
        public int TotalTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal AvgPnL { get; set; }
        public decimal BreakevenTriggeredRate { get; set; }
        public decimal StoppedOutRate { get; set; }
        public decimal TargetHitRate { get; set; }
        public decimal TimedOutRate { get; set; }
        public decimal AvgMaxFavorableExcursion { get; set; }
        public decimal AvgMaxAdverseExcursion { get; set; }
    }
}
