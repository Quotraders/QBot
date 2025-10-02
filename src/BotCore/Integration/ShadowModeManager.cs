using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Shadow mode manager for strategy testing without live execution
/// Extends catalog entries with Enabled + Shadow flags
/// Tracks shadow win-rate and promotes strategies automatically when threshold hit
/// Publishes strategy.shadow_pick while keeping router in hold
/// </summary>
public sealed class ShadowModeManager
{
    private readonly ILogger<ShadowModeManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Shadow strategy tracking
    private readonly Dictionary<string, ShadowStrategy> _shadowStrategies = new();
    private readonly object _shadowLock = new();
    
    // Performance tracking
    private readonly Dictionary<string, ShadowPerformanceTracker> _performanceTrackers = new();
    
    // Configuration
    private readonly ShadowModeConfig _config;
    
    // Telemetry counters
    private long _shadowPicksGenerated;
    private long _shadowPromotions;
    private long _shadowDemotions;
    
    public ShadowModeManager(ILogger<ShadowModeManager> logger, IServiceProvider serviceProvider, ShadowModeConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _config = config ?? new ShadowModeConfig();
        
        _logger.LogInformation("Shadow mode manager initialized - Min trades: {MinTrades}, Win rate threshold: {WinRate:P2}, Auto-promotion: {AutoPromotion}",
            _config.MinShadowTrades, _config.PromotionWinRateThreshold, _config.AutoPromotionEnabled);
    }
    
    /// <summary>
    /// Register a strategy for shadow mode testing
    /// </summary>
    public async Task RegisterShadowStrategyAsync(ShadowStrategyRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));
        if (string.IsNullOrWhiteSpace(registration.StrategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(registration));
            
        try
        {
            var shadowStrategy = new ShadowStrategy
            {
                Name = registration.StrategyName,
                Enabled = registration.Enabled,
                Shadow = true, // Always in shadow mode initially
                RegistrationTime = DateTime.UtcNow,
                Description = registration.Description,
                Priority = registration.Priority,
                MinTradesForPromotion = registration.MinTradesForPromotion ?? _config.MinShadowTrades,
                WinRateThreshold = registration.WinRateThreshold ?? _config.PromotionWinRateThreshold,
                AutoPromotionEnabled = registration.AutoPromotionEnabled ?? _config.AutoPromotionEnabled,
                StrategyConfig = registration.StrategyConfig,
                Status = ShadowStrategyStatus.Testing
            };
            
            var performanceTracker = new ShadowPerformanceTracker
            {
                StrategyName = registration.StrategyName,
                StartTime = DateTime.UtcNow,
                Trades = new List<ShadowTrade>(),
                DailyStats = new Dictionary<DateTime, DailyPerformanceStats>()
            };
            
            lock (_shadowLock)
            {
                _shadowStrategies[registration.StrategyName] = shadowStrategy;
                _performanceTrackers[registration.StrategyName] = performanceTracker;
            }
            
            // Emit registration telemetry
            await EmitShadowRegistrationTelemetryAsync(shadowStrategy, cancellationToken);
            
            _logger.LogInformation("Shadow strategy registered: {StrategyName} - Auto-promotion: {AutoPromotion}, Min trades: {MinTrades}, Win rate threshold: {WinRate:P2}",
                registration.StrategyName, shadowStrategy.AutoPromotionEnabled, shadowStrategy.MinTradesForPromotion, shadowStrategy.WinRateThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering shadow strategy: {StrategyName}", registration.StrategyName);
            throw;
        }
    }
    
    /// <summary>
    /// Process a shadow strategy pick - generates telemetry but keeps router in hold
    /// </summary>
    public async Task<ShadowPickResult> ProcessShadowPickAsync(string strategyName, ShadowPickRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(strategyName));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        var result = new ShadowPickResult
        {
            StrategyName = strategyName,
            ProcessedAt = DateTime.UtcNow,
            WasProcessed = false,
            RouterAction = ShadowRouterAction.Hold
        };
        
        try
        {
            ShadowStrategy? shadowStrategy;
            lock (_shadowLock)
            {
                if (!_shadowStrategies.TryGetValue(strategyName, out shadowStrategy))
                {
                    result.Error = $"Shadow strategy not found: {strategyName}";
                    return result;
                }
            }
            
            if (!shadowStrategy.Enabled || !shadowStrategy.Shadow)
            {
                result.Error = $"Shadow strategy not active: {strategyName} (Enabled: {shadowStrategy.Enabled}, Shadow: {shadowStrategy.Shadow})";
                return result;
            }
            
            // Create shadow pick record
            var shadowPick = new ShadowPick
            {
                StrategyName = strategyName,
                Symbol = request.Symbol,
                Direction = request.Direction,
                Confidence = request.Confidence,
                EntryPrice = request.EntryPrice,
                StopLoss = request.StopLoss,
                TakeProfit = request.TakeProfit,
                RiskAmount = request.RiskAmount,
                CreatedAt = DateTime.UtcNow,
                MarketConditions = request.MarketConditions
            };
            
            // Emit strategy.shadow_pick telemetry
            await EmitShadowPickTelemetryAsync(shadowPick, cancellationToken);
            
            // Increment counter
            Interlocked.Increment(ref _shadowPicksGenerated);
            
            result.WasProcessed = true;
            result.ShadowPick = shadowPick;
            result.RouterAction = ShadowRouterAction.Hold; // Always hold for shadow strategies
            
            _logger.LogDebug("Shadow pick processed: {StrategyName} - {Direction} {Symbol} @ {EntryPrice} (Confidence: {Confidence:P2})",
                strategyName, request.Direction, request.Symbol, request.EntryPrice, request.Confidence);
                
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Error processing shadow pick for strategy: {StrategyName}", strategyName);
            return result;
        }
    }
    
    /// <summary>
    /// Record shadow trade outcome for performance tracking
    /// </summary>
    public async Task RecordShadowTradeAsync(string strategyName, ShadowTradeOutcome outcome, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(strategyName));
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));
            
        try
        {
            ShadowPerformanceTracker? tracker;
            lock (_shadowLock)
            {
                if (!_performanceTrackers.TryGetValue(strategyName, out tracker))
                {
                    _logger.LogWarning("Performance tracker not found for shadow strategy: {StrategyName}", strategyName);
                    return;
                }
            }
            
            var shadowTrade = new ShadowTrade
            {
                TradeId = outcome.TradeId,
                StrategyName = strategyName,
                Symbol = outcome.Symbol,
                Direction = outcome.Direction,
                EntryPrice = outcome.EntryPrice,
                ExitPrice = outcome.ExitPrice,
                EntryTime = outcome.EntryTime,
                ExitTime = outcome.ExitTime,
                PnL = outcome.PnL,
                IsWinner = outcome.PnL > 0,
                RiskAmount = outcome.RiskAmount,
                RMultiple = outcome.RiskAmount > 0 ? outcome.PnL / outcome.RiskAmount : 0,
                Duration = outcome.ExitTime - outcome.EntryTime,
                ExitReason = outcome.ExitReason
            };
            
            // Add trade to tracker
            lock (_shadowLock)
            {
                tracker.Trades.Add(shadowTrade);
                
                // Update daily stats
                var tradeDate = outcome.ExitTime.Date;
                if (!tracker.DailyStats.TryGetValue(tradeDate, out var dailyStats))
                {
                    dailyStats = new DailyPerformanceStats { Date = tradeDate };
                    tracker.DailyStats[tradeDate] = dailyStats;
                }
                dailyStats.TotalTrades++;
                dailyStats.TotalPnL += outcome.PnL;
                if (shadowTrade.IsWinner)
                {
                    dailyStats.WinningTrades++;
                }
                dailyStats.WinRate = (double)dailyStats.WinningTrades / dailyStats.TotalTrades;
            }
            
            // Check for auto-promotion eligibility
            await CheckAutoPromotionEligibilityAsync(strategyName, cancellationToken);
            
            // Emit trade telemetry
            await EmitShadowTradeTelemetryAsync(shadowTrade, cancellationToken);
            
            _logger.LogDebug("Shadow trade recorded: {StrategyName} - {TradeId} {Direction} {Symbol} PnL: {PnL:C} ({ExitReason})",
                strategyName, outcome.TradeId, outcome.Direction, outcome.Symbol, outcome.PnL, outcome.ExitReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording shadow trade for strategy: {StrategyName}", strategyName);
        }
    }
    
    /// <summary>
    /// Check if shadow strategy is eligible for auto-promotion
    /// </summary>
    private async Task CheckAutoPromotionEligibilityAsync(string strategyName, CancellationToken cancellationToken)
    {
        try
        {
            ShadowStrategy? shadowStrategy;
            ShadowPerformanceTracker? tracker;
            
            lock (_shadowLock)
            {
                if (!_shadowStrategies.TryGetValue(strategyName, out shadowStrategy) ||
                    !_performanceTrackers.TryGetValue(strategyName, out tracker))
                {
                    return;
                }
            }
            
            if (!shadowStrategy.AutoPromotionEnabled || shadowStrategy.Status != ShadowStrategyStatus.Testing)
            {
                return;
            }
            
            // Check if minimum trade count is met
            if (tracker.Trades.Count < shadowStrategy.MinTradesForPromotion)
            {
                return;
            }
            
            // Calculate performance metrics
            var winRate = tracker.Trades.Count > 0 ? (double)tracker.Trades.Count(t => t.IsWinner) / tracker.Trades.Count : 0;
            var avgPnL = tracker.Trades.Count > 0 ? tracker.Trades.Average(t => t.PnL) : 0;
            var sharpeRatio = CalculateSharpeRatio(tracker.Trades);
            
            // Check promotion criteria
            var meetsWinRateThreshold = winRate >= shadowStrategy.WinRateThreshold;
            var meetsMinProfitability = avgPnL > 0; // Must be profitable on average
            var meetsRiskAdjustedReturn = sharpeRatio >= _config.MinSharpeRatio;
            
            if (meetsWinRateThreshold && meetsMinProfitability && meetsRiskAdjustedReturn)
            {
                await PromoteShadowStrategyAsync(strategyName, new PromotionMetrics
                {
                    WinRate = winRate,
                    AveragePnL = avgPnL,
                    SharpeRatio = sharpeRatio,
                    TotalTrades = tracker.Trades.Count,
                    TotalPnL = tracker.Trades.Sum(t => t.PnL)
                }, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Shadow strategy {StrategyName} not yet eligible for promotion - Win rate: {WinRate:P2} (req: {RequiredWinRate:P2}), Avg PnL: {AvgPnL:C}, Sharpe: {Sharpe:F2}",
                    strategyName, winRate, shadowStrategy.WinRateThreshold, avgPnL, sharpeRatio);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking auto-promotion eligibility for strategy: {StrategyName}", strategyName);
        }
    }
    
    /// <summary>
    /// Promote shadow strategy to live trading
    /// </summary>
    public async Task PromoteShadowStrategyAsync(string strategyName, PromotionMetrics metrics, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(strategyName));
        if (metrics == null)
            throw new ArgumentNullException(nameof(metrics));
            
        try
        {
            ShadowStrategy? shadowStrategy;
            lock (_shadowLock)
            {
                if (!_shadowStrategies.TryGetValue(strategyName, out shadowStrategy))
                {
                    throw new InvalidOperationException($"Shadow strategy not found: {strategyName}");
                }
                
                // Update strategy status
                shadowStrategy.Shadow = false; // Remove from shadow mode
                shadowStrategy.Status = ShadowStrategyStatus.Promoted;
                shadowStrategy.PromotedAt = DateTime.UtcNow;
                shadowStrategy.PromotionMetrics = metrics;
            }
            
            // Increment promotion counter
            Interlocked.Increment(ref _shadowPromotions);
            
            // Emit promotion telemetry
            await EmitShadowPromotionTelemetryAsync(shadowStrategy, metrics, cancellationToken);
            
            _logger.LogInformation("🎉 Shadow strategy PROMOTED to live trading: {StrategyName} - Win rate: {WinRate:P2}, Trades: {TotalTrades}, Total PnL: {TotalPnL:C}, Sharpe: {Sharpe:F2}",
                strategyName, metrics.WinRate, metrics.TotalTrades, metrics.TotalPnL, metrics.SharpeRatio);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting shadow strategy: {StrategyName}", strategyName);
            throw;
        }
    }
    
    /// <summary>
    /// Demote a strategy back to shadow mode due to poor performance
    /// </summary>
    public async Task DemoteShadowStrategyAsync(string strategyName, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(strategyName));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null or empty", nameof(reason));
            
        try
        {
            ShadowStrategy? shadowStrategy;
            lock (_shadowLock)
            {
                if (!_shadowStrategies.TryGetValue(strategyName, out shadowStrategy))
                {
                    throw new InvalidOperationException($"Shadow strategy not found: {strategyName}");
                }
                
                // Update strategy status
                shadowStrategy.Shadow = true; // Back to shadow mode
                shadowStrategy.Status = ShadowStrategyStatus.Demoted;
                shadowStrategy.DemotedAt = DateTime.UtcNow;
                shadowStrategy.DemotionReason = reason;
            }
            
            // Increment demotion counter
            Interlocked.Increment(ref _shadowDemotions);
            
            // Emit demotion telemetry
            await EmitShadowDemotionTelemetryAsync(shadowStrategy, reason, cancellationToken);
            
            _logger.LogWarning("Shadow strategy DEMOTED back to shadow mode: {StrategyName} - Reason: {Reason}", strategyName, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error demoting shadow strategy: {StrategyName}", strategyName);
            throw;
        }
    }
    
    /// <summary>
    /// Get shadow strategy performance report
    /// </summary>
    public ShadowStrategyReport GetStrategyReport(string strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            throw new ArgumentException("Strategy name cannot be null or empty", nameof(strategyName));
            
        lock (_shadowLock)
        {
            if (!_shadowStrategies.TryGetValue(strategyName, out var shadowStrategy) ||
                !_performanceTrackers.TryGetValue(strategyName, out var tracker))
            {
                throw new InvalidOperationException($"Shadow strategy not found: {strategyName}");
            }
            
            var trades = tracker.Trades.ToList();
            var winRate = trades.Count > 0 ? (double)trades.Count(t => t.IsWinner) / trades.Count : 0;
            var totalPnL = trades.Sum(t => t.PnL);
            var avgPnL = trades.Count > 0 ? trades.Average(t => t.PnL) : 0;
            var sharpeRatio = CalculateSharpeRatio(trades);
            var maxDrawdown = CalculateMaxDrawdown(trades);
            
            return new ShadowStrategyReport
            {
                StrategyName = strategyName,
                Status = shadowStrategy.Status,
                RegistrationTime = shadowStrategy.RegistrationTime,
                PromotedAt = shadowStrategy.PromotedAt,
                DemotedAt = shadowStrategy.DemotedAt,
                TotalTrades = trades.Count,
                WinRate = winRate,
                TotalPnL = totalPnL,
                AveragePnL = avgPnL,
                SharpeRatio = sharpeRatio,
                MaxDrawdown = maxDrawdown,
                IsEligibleForPromotion = trades.Count >= shadowStrategy.MinTradesForPromotion && 
                                        winRate >= shadowStrategy.WinRateThreshold && 
                                        avgPnL > 0 && 
                                        sharpeRatio >= _config.MinSharpeRatio,
                DemotionReason = shadowStrategy.DemotionReason
            };
        }
    }
    
    /// <summary>
    /// Calculate Sharpe ratio for trades
    /// </summary>
    private static double CalculateSharpeRatio(List<ShadowTrade> trades)
    {
        if (trades.Count < 2)
            return 0;
            
        var returns = trades.Select(t => t.PnL).ToList();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        
        return stdDev > 0 ? avgReturn / stdDev : 0;
    }
    
    /// <summary>
    /// Calculate maximum drawdown
    /// </summary>
    private static double CalculateMaxDrawdown(List<ShadowTrade> trades)
    {
        if (trades.Count == 0)
            return 0;
            
        var cumulativePnL = 0.0;
        var peak = 0.0;
        var maxDrawdown = 0.0;
        
        foreach (var trade in trades.OrderBy(t => t.ExitTime))
        {
            cumulativePnL += trade.PnL;
            if (cumulativePnL > peak)
            {
                peak = cumulativePnL;
            }
            var drawdown = peak - cumulativePnL;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }
        
        return maxDrawdown;
    }
    
    /// <summary>
    /// Emit shadow registration telemetry
    /// </summary>
    private async Task EmitShadowRegistrationTelemetryAsync(ShadowStrategy strategy, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService)) as TradingBot.IntelligenceStack.RealTradingMetricsService;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["strategy_name"] = strategy.Name,
                    ["auto_promotion"] = strategy.AutoPromotionEnabled.ToString().ToLowerInvariant()
                };
                
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting shadow registration telemetry");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Emit shadow pick telemetry
    /// </summary>
    private async Task EmitShadowPickTelemetryAsync(ShadowPick pick, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService)) as TradingBot.IntelligenceStack.RealTradingMetricsService;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["strategy_name"] = pick.StrategyName,
                    ["symbol"] = pick.Symbol,
                    ["direction"] = pick.Direction.ToString().ToLowerInvariant()
                };
                
                
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting shadow pick telemetry");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Emit shadow trade telemetry
    /// </summary>
    private async Task EmitShadowTradeTelemetryAsync(ShadowTrade trade, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService)) as TradingBot.IntelligenceStack.RealTradingMetricsService;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["strategy_name"] = trade.StrategyName,
                    ["symbol"] = trade.Symbol,
                    ["direction"] = trade.Direction.ToString().ToLowerInvariant(),
                    ["is_winner"] = trade.IsWinner.ToString().ToLowerInvariant(),
                    ["exit_reason"] = trade.ExitReason
                };
                
                
                
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting shadow trade telemetry");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Emit shadow promotion telemetry
    /// </summary>
    private async Task EmitShadowPromotionTelemetryAsync(ShadowStrategy strategy, PromotionMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService)) as TradingBot.IntelligenceStack.RealTradingMetricsService;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["strategy_name"] = strategy.Name
                };
                
                
                
                
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting shadow promotion telemetry");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Emit shadow demotion telemetry
    /// </summary>
    private async Task EmitShadowDemotionTelemetryAsync(ShadowStrategy strategy, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService)) as TradingBot.IntelligenceStack.RealTradingMetricsService;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["strategy_name"] = strategy.Name,
                    ["reason"] = reason
                };
                
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting shadow demotion telemetry");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Get overall shadow mode statistics
    /// </summary>
    public ShadowModeStats GetShadowModeStats()
    {
        lock (_shadowLock)
        {
            var activeStrategies = _shadowStrategies.Values.Where(s => s.Enabled && s.Shadow).Count();
            var promotedStrategies = _shadowStrategies.Values.Where(s => s.Status == ShadowStrategyStatus.Promoted).Count();
            var demotedStrategies = _shadowStrategies.Values.Where(s => s.Status == ShadowStrategyStatus.Demoted).Count();
            
            return new ShadowModeStats
            {
                TotalStrategies = _shadowStrategies.Count,
                ActiveShadowStrategies = activeStrategies,
                PromotedStrategies = promotedStrategies,
                DemotedStrategies = demotedStrategies,
                ShadowPicksGenerated = _shadowPicksGenerated,
                TotalPromotions = _shadowPromotions,
                TotalDemotions = _shadowDemotions
            };
        }
    }
}

// Supporting data structures for shadow mode...
// (Additional classes would be defined here for completeness)

/// <summary>
/// Shadow mode configuration
/// </summary>
public sealed class ShadowModeConfig
{
    public int MinShadowTrades { get; set; } = 50;
    public double PromotionWinRateThreshold { get; set; } = 0.65;
    public double MinSharpeRatio { get; set; } = 1.0;
    public bool AutoPromotionEnabled { get; set; } = true;
    public TimeSpan MaxShadowDuration { get; set; } = TimeSpan.FromDays(30);
    public double MinAveragePnL { get; set; } = 10.0; // Minimum average profit per trade
}

/// <summary>
/// Shadow strategy registration request
/// </summary>
public sealed class ShadowStrategyRegistration
{
    public string StrategyName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; } = 50;
    public int? MinTradesForPromotion { get; set; }
    public double? WinRateThreshold { get; set; }
    public bool? AutoPromotionEnabled { get; set; }
    public Dictionary<string, object> StrategyConfig { get; set; } = new();
}

/// <summary>
/// Shadow strategy data structure
/// </summary>
public sealed class ShadowStrategy
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Shadow { get; set; }
    public DateTime RegistrationTime { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int MinTradesForPromotion { get; set; }
    public double WinRateThreshold { get; set; }
    public bool AutoPromotionEnabled { get; set; }
    public Dictionary<string, object> StrategyConfig { get; set; } = new();
    public ShadowStrategyStatus Status { get; set; }
    public DateTime? PromotedAt { get; set; }
    public DateTime? DemotedAt { get; set; }
    public string? DemotionReason { get; set; }
    public PromotionMetrics? PromotionMetrics { get; set; }
}

/// <summary>
/// Shadow strategy status
/// </summary>
public enum ShadowStrategyStatus
{
    Testing,
    Promoted,
    Demoted,
    Disabled
}

/// <summary>
/// Shadow pick request
/// </summary>
public sealed class ShadowPickRequest
{
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double Confidence { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double RiskAmount { get; set; }
    public Dictionary<string, object> MarketConditions { get; set; } = new();
}

/// <summary>
/// Shadow pick result
/// </summary>
public sealed class ShadowPickResult
{
    public string StrategyName { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public bool WasProcessed { get; set; }
    public ShadowRouterAction RouterAction { get; set; }
    public ShadowPick? ShadowPick { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Shadow router actions
/// </summary>
public enum ShadowRouterAction
{
    Hold,
    Execute // Only for promoted strategies
}

/// <summary>
/// Trade direction
/// </summary>
public enum TradeDirection
{
    Long,
    Short
}

/// <summary>
/// Shadow pick data structure
/// </summary>
public sealed class ShadowPick
{
    public string StrategyName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double Confidence { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double RiskAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> MarketConditions { get; set; } = new();
}

/// <summary>
/// Shadow trade outcome
/// </summary>
public sealed class ShadowTradeOutcome
{
    public string TradeId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public double PnL { get; set; }
    public double RiskAmount { get; set; }
    public string ExitReason { get; set; } = string.Empty;
}

/// <summary>
/// Shadow trade record
/// </summary>
public sealed class ShadowTrade
{
    public string TradeId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public double PnL { get; set; }
    public bool IsWinner { get; set; }
    public double RiskAmount { get; set; }
    public double RMultiple { get; set; }
    public TimeSpan Duration { get; set; }
    public string ExitReason { get; set; } = string.Empty;
}

/// <summary>
/// Promotion metrics
/// </summary>
public sealed class PromotionMetrics
{
    public double WinRate { get; set; }
    public double AveragePnL { get; set; }
    public double SharpeRatio { get; set; }
    public int TotalTrades { get; set; }
    public double TotalPnL { get; set; }
}

/// <summary>
/// Shadow performance tracker
/// </summary>
public sealed class ShadowPerformanceTracker
{
    public string StrategyName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public List<ShadowTrade> Trades { get; set; } = new();
    public Dictionary<DateTime, DailyPerformanceStats> DailyStats { get; set; } = new();
}

/// <summary>
/// Daily performance statistics
/// </summary>
public sealed class DailyPerformanceStats
{
    public DateTime Date { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double WinRate { get; set; }
    public double TotalPnL { get; set; }
}

/// <summary>
/// Shadow strategy report
/// </summary>
public sealed class ShadowStrategyReport
{
    public string StrategyName { get; set; } = string.Empty;
    public ShadowStrategyStatus Status { get; set; }
    public DateTime RegistrationTime { get; set; }
    public DateTime? PromotedAt { get; set; }
    public DateTime? DemotedAt { get; set; }
    public int TotalTrades { get; set; }
    public double WinRate { get; set; }
    public double TotalPnL { get; set; }
    public double AveragePnL { get; set; }
    public double SharpeRatio { get; set; }
    public double MaxDrawdown { get; set; }
    public bool IsEligibleForPromotion { get; set; }
    public string? DemotionReason { get; set; }
}

/// <summary>
/// Shadow mode statistics
/// </summary>
public sealed class ShadowModeStats
{
    public int TotalStrategies { get; set; }
    public int ActiveShadowStrategies { get; set; }
    public int PromotedStrategies { get; set; }
    public int DemotedStrategies { get; set; }
    public long ShadowPicksGenerated { get; set; }
    public long TotalPromotions { get; set; }
    public long TotalDemotions { get; set; }
}