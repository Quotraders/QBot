using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using System.Globalization;

namespace BotCore.Services;

/// <summary>
/// 📈 AUTONOMOUS PERFORMANCE TRACKER 📈
/// 
/// Tracks and analyzes trading performance to enable continuous learning
/// and optimization of the autonomous trading engine. Provides real-time
/// performance metrics, learning insights, and optimization recommendations.
/// 
/// KEY FEATURES:
/// ✅ Real-time P&L tracking and analysis
/// ✅ Win rate and performance metric calculation
/// ✅ Strategy-specific performance analysis
/// ✅ Time-based performance patterns
/// ✅ Risk-adjusted performance metrics
/// ✅ Continuous learning from trade outcomes
/// ✅ Performance-based parameter optimization
/// 
/// This enables the autonomous engine to:
/// - Learn from every trade outcome
/// - Optimize strategy selection based on performance
/// - Adjust risk parameters dynamically
/// - Identify best trading times and conditions
/// - Generate actionable performance insights
/// </summary>
public class AutonomousPerformanceTracker
{
    private readonly ILogger<AutonomousPerformanceTracker> _logger;
    
    // Performance analysis constants
    private const decimal HourlyPerformanceMultiplierThreshold = 2;
    private const int TimeOptimizationPriority = 6;
    private const int RiskRewardOptimizationPriority = 8;
    private const decimal RiskRewardRatioThreshold = 1.5m;
    private const int MinimumTradesForAnalysis = 20;
    private const decimal SessionPerformanceThreshold = 1.5m;
    private const int RiskManagementOptimizationPriority = 10; // Priority for high drawdown risk management
    
    // Win rate thresholds
    private const decimal DefaultWinRateNoTrades = 0.5m;
    private const decimal ExcellentWinRateThreshold = 0.7m;
    private const decimal LowWinRateThreshold = 0.4m;
    
    // Performance history limits
    private const int MaxRecentInsightsToReturn = 20;
    private const int MaxOptimizationRecommendations = 10;
    private const int MaxPerformanceSnapshots = 1000;
    private const int MaxInsightsPerStrategy = 100;
    
    // Strategy analysis thresholds
    private const int MinTradesForUnderperformanceAnalysis = 20;
    private const int MinTradesForProfitFactorAnalysis = 10;
    private const decimal LowProfitFactorThreshold = 1.2m;
    private const decimal RecentPerformanceImprovementRatio = 0.1m;
    private const int StrategyUnderperformPriority = 9;
    private const int LowProfitFactorPriority = 8;
    private const int RecentImprovementPriority = 7;
    
    // Sharpe ratio and risk calculations
    private const int MinTradesForSharpeRatio = 30;
    private const int TradingDaysPerYear = 252; // Annualization factor
    
    // Drawdown analysis
    private const int MinTradesForDrawdownAnalysis = 10;
    private const decimal HighDrawdownRatio = 0.3m;
    
    // Profit factor fallback
    private const decimal MaxProfitFactorWhenNoLosses = 99m;
    
    // Performance tracking collections
    private readonly List<AutonomousTradeOutcome> _allTrades = new();
    private readonly Dictionary<string, List<AutonomousTradeOutcome>> _tradesByStrategy = new();
    private readonly Dictionary<string, List<AutonomousTradeOutcome>> _tradesBySymbol = new();
    private readonly Dictionary<DateTime, decimal> _dailyPnL = new();
    private readonly Queue<PerformanceSnapshot> _performanceHistory = new();
    private readonly object _trackingLock = new();
    
    // Current performance state
    private decimal _totalPnL;
    private decimal _todayPnL;
    private decimal _weekPnL;
    private decimal _monthPnL;
    private int _totalTrades;
    private int _winningTrades;
    private int _losingTrades;
    private decimal _largestWin;
    private decimal _largestLoss;
    private decimal _avgWin;
    private decimal _avgLoss;
    private decimal _winRate;
    private decimal _profitFactor;
    private decimal _sharpeRatio;
    private decimal _maxDrawdown;
    private DateTime _lastUpdateTime = DateTime.MinValue;
    
    // Learning and optimization
    private readonly Dictionary<string, StrategyLearning> _strategyLearning = new();

    public AutonomousPerformanceTracker(ILogger<AutonomousPerformanceTracker> logger)
    {
        _logger = logger;
        
        InitializeStrategyLearning();
        
        _logger.LogInformation("📈 [PERFORMANCE-TRACKER] Initialized - Real-time performance tracking and learning ready");
    }
    
    /// <summary>
    /// Record a new trade outcome for analysis
    /// </summary>
    public Task RecordTradeAsync(AutonomousTradeOutcome trade, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trade);
        
        lock (_trackingLock)
        {
            // Add to collections
            _allTrades.Add(trade);
            
            // Organize by strategy
            if (!_tradesByStrategy.TryGetValue(trade.Strategy, out var strategyTrades))
            {
                strategyTrades = new List<AutonomousTradeOutcome>();
                _tradesByStrategy[trade.Strategy] = strategyTrades;
            }
            strategyTrades.Add(trade);
            
            // Organize by symbol
            if (!_tradesBySymbol.TryGetValue(trade.Symbol, out var symbolTrades))
            {
                symbolTrades = new List<AutonomousTradeOutcome>();
                _tradesBySymbol[trade.Symbol] = symbolTrades;
            }
            symbolTrades.Add(trade);
            
            // Update daily P&L tracking
            var tradeDate = trade.EntryTime.Date;
            if (!_dailyPnL.ContainsKey(tradeDate))
            {
                _dailyPnL[tradeDate] = 0m;
            }
            _dailyPnL[tradeDate] += trade.PnL;
            
            // Update performance metrics
            UpdatePerformanceMetrics();
            
            _logger.LogDebug("📊 [PERFORMANCE-TRACKER] Trade recorded: {Strategy} {Symbol} ${PnL:F2} (Total: {Trades} trades, ${TotalPnL:F2})",
                trade.Strategy, trade.Symbol, trade.PnL, _totalTrades, _totalPnL);
        }

        // Record learning insights outside the lock
        return RecordLearningInsightAsync(trade, cancellationToken);
    }
    
    /// <summary>
    /// Update performance metrics from current trade data
    /// </summary>
    public async Task UpdateMetricsAsync(AutonomousTradeOutcome[] recentTrades, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recentTrades);
        
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_trackingLock)
        {
            foreach (var trade in recentTrades)
            {
                if (!_allTrades.Exists(t => t.EntryTime == trade.EntryTime && t.Strategy == trade.Strategy))
                {
                    _allTrades.Add(trade);
                }
            }
            
            UpdatePerformanceMetrics();
            _lastUpdateTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Get recent P&L for specified time period
    /// </summary>
    public decimal GetRecentPnL(TimeSpan period)
    {
        lock (_trackingLock)
        {
            var cutoffTime = DateTime.UtcNow - period;
            return _allTrades
                .Where(t => t.EntryTime >= cutoffTime)
                .Sum(t => t.PnL);
        }
    }
    
    /// <summary>
    /// Get recent win rate for specified time period
    /// </summary>
    public decimal GetRecentWinRate(TimeSpan period)
    {
        lock (_trackingLock)
        {
            var recentTrades = _allTrades
                .Where(t => t.EntryTime >= DateTime.UtcNow - period)
                .ToArray();
            
            if (recentTrades.Length == 0) return DefaultWinRateNoTrades; // Default 50% if no trades
            
            var wins = recentTrades.Count(t => t.PnL > 0);
            return (decimal)wins / recentTrades.Length;
        }
    }
    
    /// <summary>
    /// Get performance metrics for specific strategy
    /// </summary>
    public StrategyPerformance GetStrategyPerformance(string strategy)
    {
        lock (_trackingLock)
        {
            if (!_tradesByStrategy.TryGetValue(strategy, out var trades))
            {
                return new StrategyPerformance { StrategyName = strategy };
            }
            var winningTrades = trades.Where(t => t.PnL > 0).ToArray();
            var losingTrades = trades.Where(t => t.PnL < 0).ToArray();
            
            return new StrategyPerformance
            {
                StrategyName = strategy,
                TotalTrades = trades.Count,
                WinningTrades = winningTrades.Length,
                LosingTrades = losingTrades.Length,
                TotalPnL = trades.Sum(t => t.PnL),
                WinRate = trades.Count > 0 ? (decimal)winningTrades.Length / trades.Count : 0m,
                AvgWin = winningTrades.Length > 0 ? winningTrades.Average(t => t.PnL) : 0m,
                AvgLoss = losingTrades.Length > 0 ? losingTrades.Average(t => t.PnL) : 0m,
                LargestWin = winningTrades.Length > 0 ? winningTrades.Max(t => t.PnL) : 0m,
                LargestLoss = losingTrades.Length > 0 ? losingTrades.Min(t => t.PnL) : 0m,
                ProfitFactor = CalculateProfitFactor(winningTrades, losingTrades),
                RecentPerformance = GetRecentStrategyPerformance(strategy, TimeSpan.FromDays(7))
            };
        }
    }
    
    /// <summary>
    /// Get current performance summary
    /// </summary>
    public AutonomousPerformanceSummary GetCurrentPerformance()
    {
        lock (_trackingLock)
        {
            return new AutonomousPerformanceSummary
            {
                TotalPnL = _totalPnL,
                TodayPnL = _todayPnL,
                WeekPnL = _weekPnL,
                MonthPnL = _monthPnL,
                TotalTrades = _totalTrades,
                WinningTrades = _winningTrades,
                LosingTrades = _losingTrades,
                WinRate = _winRate,
                AvgWin = _avgWin,
                AvgLoss = _avgLoss,
                LargestWin = _largestWin,
                LargestLoss = _largestLoss,
                ProfitFactor = _profitFactor,
                SharpeRatio = _sharpeRatio,
                MaxDrawdown = _maxDrawdown,
                LastUpdateTime = _lastUpdateTime,
                BestStrategy = GetBestPerformingStrategy(),
                WorstStrategy = GetWorstPerformingStrategy()
            };
        }
    }
    
    /// <summary>
    /// Generate daily performance report
    /// </summary>
    public async Task<DailyPerformanceReport> GenerateDailyReportAsync(CancellationToken cancellationToken = default)
    {
        AutonomousTradeOutcome[] todayTrades;
        DateTime today;
        
        lock (_trackingLock)
        {
            today = DateTime.Today;
            todayTrades = _allTrades.Where(t => t.EntryTime.Date == today).ToArray();
        }
        
        // Generate insights outside the lock
        var tradingInsights = await GenerateTradingInsightsAsync(todayTrades, cancellationToken).ConfigureAwait(false);
        
        lock (_trackingLock)
        {
            var report = new DailyPerformanceReport
            {
                Date = today,
                DailyPnL = todayTrades.Sum(t => t.PnL),
                TotalTrades = todayTrades.Length,
                WinningTrades = todayTrades.Count(t => t.PnL > 0),
                LosingTrades = todayTrades.Count(t => t.PnL < 0),
                WinRate = todayTrades.Length > 0 ? (decimal)todayTrades.Count(t => t.PnL > 0) / todayTrades.Length : 0m,
                LargestWin = todayTrades.Length > 0 ? todayTrades.Where(t => t.PnL > 0).DefaultIfEmpty().Max(t => t?.PnL ?? 0m) : 0m,
                LargestLoss = todayTrades.Length > 0 ? todayTrades.Where(t => t.PnL < 0).DefaultIfEmpty().Min(t => t?.PnL ?? 0m) : 0m,
                BestStrategy = GetBestPerformingStrategyForDay(today),
                WorstStrategy = GetWorstPerformingStrategyForDay(today)
            };
            
            // Add insights to the collection property
            // Add trading insights
            report.ReplaceTradingInsights(tradingInsights);
            
            // Add recommendations to the collection property
            var recommendations = GenerateOptimizationRecommendations();
            // Add optimization recommendations
            report.ReplaceOptimizationRecommendations(recommendations);
            
            return report;
        }
    }
    
    /// <summary>
    /// Get learning insights for strategy optimization
    /// </summary>
    public IReadOnlyList<LearningInsight> GetLearningInsights(string strategy = "")
    {
        lock (_trackingLock)
        {
            var insights = new List<LearningInsight>();
            
            if (string.IsNullOrEmpty(strategy))
            {
                // Get insights for all strategies
                foreach (var strategyLearning in _strategyLearning.Values)
                {
                    insights.AddRange(strategyLearning.Insights);
                }
            }
            else if (_strategyLearning.TryGetValue(strategy, out var strategyLearning))
            {
                insights.AddRange(strategyLearning.Insights);
            }
            
            return insights.OrderByDescending(i => i.Timestamp).Take(MaxRecentInsightsToReturn).ToList();
        }
    }
    
    /// <summary>
    /// Get performance-based recommendations for parameter optimization
    /// </summary>
    public IReadOnlyList<OptimizationRecommendation> GetOptimizationRecommendations()
    {
        lock (_trackingLock)
        {
            var recommendations = new List<OptimizationRecommendation>();
            
            // Analyze strategy performance
            foreach (var strategy in _tradesByStrategy.Keys)
            {
                var performance = GetStrategyPerformance(strategy);
                var strategyRecommendations = AnalyzeStrategyForOptimization(performance);
                recommendations.AddRange(strategyRecommendations);
            }
            
            // Analyze time-based patterns
            var timeRecommendations = AnalyzeTimeBasedPatterns();
            recommendations.AddRange(timeRecommendations);
            
            // Analyze risk management
            var riskRecommendations = AnalyzeRiskManagement();
            recommendations.AddRange(riskRecommendations);
            
            return recommendations.OrderByDescending(r => r.Priority).Take(MaxOptimizationRecommendations).ToList();
        }
    }
    
    private void UpdatePerformanceMetrics()
    {
        if (_allTrades.Count == 0) return;
        
        // Basic metrics
        _totalTrades = _allTrades.Count;
        _totalPnL = _allTrades.Sum(t => t.PnL);
        
        var winningTrades = _allTrades.Where(t => t.PnL > 0).ToArray();
        var losingTrades = _allTrades.Where(t => t.PnL < 0).ToArray();
        
        _winningTrades = winningTrades.Length;
        _losingTrades = losingTrades.Length;
        _winRate = (decimal)_winningTrades / _totalTrades; // _totalTrades > 0 is guaranteed by early return
        
        _avgWin = _winningTrades > 0 ? winningTrades.Average(t => t.PnL) : 0m;
        _avgLoss = _losingTrades > 0 ? losingTrades.Average(t => t.PnL) : 0m;
        
        _largestWin = _winningTrades > 0 ? winningTrades.Max(t => t.PnL) : 0m;
        _largestLoss = _losingTrades > 0 ? losingTrades.Min(t => t.PnL) : 0m;
        
        _profitFactor = CalculateProfitFactor(winningTrades, losingTrades);
        
        // Time-based P&L
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        _todayPnL = _allTrades.Where(t => t.EntryTime.Date == today).Sum(t => t.PnL);
        _weekPnL = _allTrades.Where(t => t.EntryTime.Date >= weekStart).Sum(t => t.PnL);
        _monthPnL = _allTrades.Where(t => t.EntryTime.Date >= monthStart).Sum(t => t.PnL);
        
        // Advanced metrics
        _sharpeRatio = CalculateSharpeRatio();
        _maxDrawdown = CalculateMaxDrawdown();
        
        // Save performance snapshot
        _performanceHistory.Enqueue(new PerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            TotalPnL = _totalPnL,
            TotalTrades = _totalTrades,
            WinRate = _winRate,
            SharpeRatio = _sharpeRatio
        });
        
        // Keep limited history
        while (_performanceHistory.Count > MaxPerformanceSnapshots)
        {
            _performanceHistory.Dequeue();
        }
    }
    
    private static decimal CalculateProfitFactor(AutonomousTradeOutcome[] winningTrades, AutonomousTradeOutcome[] losingTrades)
    {
        var totalWins = winningTrades.Sum(t => t.PnL);
        var totalLosses = Math.Abs(losingTrades.Sum(t => t.PnL));
        
        if (totalLosses > 0) return totalWins / totalLosses;
        if (totalWins > 0) return MaxProfitFactorWhenNoLosses;
        return 0m;
    }
    
    private decimal CalculateSharpeRatio()
    {
        if (_allTrades.Count < MinTradesForSharpeRatio) return 0m; // Need minimum trades for reliable calculation
        
        var returns = _allTrades.Select(t => t.PnL).ToArray();
        var avgReturn = returns.Average();
        var stdDev = CalculateStandardDeviation(returns);
        
        return stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(TradingDaysPerYear) : 0m; // Annualized
    }
    
    private decimal CalculateMaxDrawdown()
    {
        if (_allTrades.Count == 0) return 0m;
        
        var runningPnL = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;
        
        foreach (var trade in _allTrades.OrderBy(t => t.EntryTime))
        {
            runningPnL += trade.PnL;
            if (runningPnL > peak) peak = runningPnL;
            
            var drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }
        
        return maxDrawdown;
    }
    
    private static decimal CalculateStandardDeviation(decimal[] values)
    {
        if (values.Length <= 1) return 0m;
        
        var average = values.Average();
        var sumOfSquares = values.Sum(val => Math.Pow((double)(val - average), 2));
        var variance = sumOfSquares / (values.Length - 1);
        
        return (decimal)Math.Sqrt(variance);
    }
    
    private string GetBestPerformingStrategy()
    {
        if (_tradesByStrategy.Count == 0) return "N/A";
        
        return _tradesByStrategy
            .OrderByDescending(kvp => kvp.Value.Sum(t => t.PnL))
            .First().Key;
    }
    
    private string GetWorstPerformingStrategy()
    {
        if (_tradesByStrategy.Count == 0) return "N/A";
        
        return _tradesByStrategy
            .OrderBy(kvp => kvp.Value.Sum(t => t.PnL))
            .First().Key;
    }
    
    private void InitializeStrategyLearning()
    {
        var strategies = new[] { "S2", "S3", "S6", "S11" };
        foreach (var strategy in strategies)
        {
            _strategyLearning[strategy] = new StrategyLearning
            {
                StrategyName = strategy
            };
        }
    }
    
    private Task RecordLearningInsightAsync(AutonomousTradeOutcome trade, CancellationToken cancellationToken = default)
    {
        // Generate learning insights from trade outcome
        var insight = new LearningInsight
        {
            Timestamp = DateTime.UtcNow,
            Strategy = trade.Strategy,
            AutonomousTradeOutcome = trade,
            InsightType = trade.PnL > 0 ? "SUCCESS_PATTERN" : "LOSS_PATTERN",
            Description = $"{trade.Strategy} {trade.Direction} {trade.Symbol} resulted in ${trade.PnL:F2}",
            Confidence = trade.Confidence
        };
        
        var conditions = new Dictionary<string, object>
        {
            ["Regime"] = trade.MarketRegime.ToString(),
            ["EntryTime"] = trade.EntryTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["Direction"] = trade.Direction
        };
        insight.ReplaceMarketConditions(conditions);
        
        if (_strategyLearning.TryGetValue(trade.Strategy, out var learning))
        {
            learning.AddInsight(insight);
            
            // Keep limited insights per strategy
            while (learning.Insights.Count > MaxInsightsPerStrategy)
            {
                var insights = learning.Insights.Skip(1).ToList();
                learning.ReplaceInsights(insights);
            }
        }

        return Task.CompletedTask;
    }
    
    private decimal GetRecentStrategyPerformance(string strategy, TimeSpan period)
    {
        if (!_tradesByStrategy.TryGetValue(strategy, out var trades)) return 0m;
        
        var cutoffTime = DateTime.UtcNow - period;
        return trades
            .Where(t => t.EntryTime >= cutoffTime)
            .Sum(t => t.PnL);
    }
    
    private string GetBestPerformingStrategyForDay(DateTime date)
    {
        var dayTrades = _allTrades.Where(t => t.EntryTime.Date == date).ToArray();
        if (dayTrades.Length == 0) return "N/A";
        
        var strategyPnL = dayTrades
            .GroupBy(t => t.Strategy)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (strategyPnL.Count == 0) return "N/A";
        
        return strategyPnL.OrderByDescending(kvp => kvp.Value).First().Key;
    }
    
    private string GetWorstPerformingStrategyForDay(DateTime date)
    {
        var dayTrades = _allTrades.Where(t => t.EntryTime.Date == date).ToArray();
        if (dayTrades.Length == 0) return "N/A";
        
        var strategyPnL = dayTrades
            .GroupBy(t => t.Strategy)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (strategyPnL.Count == 0) return "N/A";
        
        return strategyPnL.OrderBy(kvp => kvp.Value).First().Key;
    }
    
    private static Task<List<string>> GenerateTradingInsightsAsync(AutonomousTradeOutcome[] trades, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var insights = new List<string>();
        
        if (trades.Length == 0)
        {
            insights.Add("No trades executed today");
            return Task.FromResult(insights);
        }
        
        // Performance insights
        var winRate = (decimal)trades.Count(t => t.PnL > 0) / trades.Length;
        if (winRate > ExcellentWinRateThreshold)
        {
            insights.Add($"Excellent win rate today: {winRate:P1}");
        }
        else if (winRate < LowWinRateThreshold)
        {
            insights.Add($"Low win rate today: {winRate:P1} - review strategy selection");
        }
        
        // Strategy insights
        var strategyPerformance = trades.GroupBy(t => t.Strategy)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (strategyPerformance.Count > 0)
        {
            var bestStrategy = strategyPerformance.OrderByDescending(kvp => kvp.Value).First();
            var worstStrategy = strategyPerformance.OrderBy(kvp => kvp.Value).First();
            
            insights.Add($"Best strategy: {bestStrategy.Key} (${bestStrategy.Value:F2})");
            if (worstStrategy.Value < 0)
            {
                insights.Add($"Worst strategy: {worstStrategy.Key} (${worstStrategy.Value:F2})");
            }
        }
        
        // Time-based insights
        var morningTrades = trades.Where(t => t.EntryTime.Hour >= 9 && t.EntryTime.Hour < 12).ToArray();
        var afternoonTrades = trades.Where(t => t.EntryTime.Hour >= 13 && t.EntryTime.Hour < 16).ToArray();
        
        if (morningTrades.Length > 0 && afternoonTrades.Length > 0)
        {
            var morningPnL = morningTrades.Sum(t => t.PnL);
            var afternoonPnL = afternoonTrades.Sum(t => t.PnL);
            
            if (morningPnL > afternoonPnL * SessionPerformanceThreshold)
            {
                insights.Add("Morning session significantly outperformed afternoon");
            }
            else if (afternoonPnL > morningPnL * SessionPerformanceThreshold)
            {
                insights.Add("Afternoon session significantly outperformed morning");
            }
        }
        
        return Task.FromResult(insights);
    }
    
    private List<OptimizationRecommendation> GenerateOptimizationRecommendations()
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // Strategy-specific recommendations
        foreach (var strategy in _tradesByStrategy.Keys)
        {
            var performance = GetStrategyPerformance(strategy);
            var strategyRecs = AnalyzeStrategyForOptimization(performance);
            recommendations.AddRange(strategyRecs);
        }
        
        return recommendations.OrderByDescending(r => r.Priority).ToList();
    }
    
    private static List<OptimizationRecommendation> AnalyzeStrategyForOptimization(StrategyPerformance performance)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        if (performance.WinRate < LowWinRateThreshold && performance.TotalTrades > MinTradesForUnderperformanceAnalysis)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "STRATEGY_UNDERPERFORM",
                Priority = StrategyUnderperformPriority,
                Description = $"Strategy {performance.StrategyName} has low win rate: {performance.WinRate:P1}",
                Action = "Consider reducing allocation or reviewing entry criteria",
                Strategy = performance.StrategyName
            });
        }
        
        if (performance.ProfitFactor < LowProfitFactorThreshold && performance.TotalTrades > MinTradesForProfitFactorAnalysis)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "PROFIT_FACTOR_LOW",
                Priority = LowProfitFactorPriority,
                Description = $"Strategy {performance.StrategyName} has low profit factor: {performance.ProfitFactor:F2}",
                Action = "Review risk management and exit criteria",
                Strategy = performance.StrategyName
            });
        }
        
        if (performance.RecentPerformance > performance.TotalPnL * RecentPerformanceImprovementRatio)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "RECENT_IMPROVEMENT",
                Priority = RecentImprovementPriority,
                Description = $"Strategy {performance.StrategyName} showing recent improvement",
                Action = "Consider increasing allocation",
                Strategy = performance.StrategyName
            });
        }
        
        return recommendations;
    }
    
    private List<OptimizationRecommendation> AnalyzeTimeBasedPatterns()
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // Analyze performance by hour
        var hourlyPerformance = _allTrades
            .GroupBy(t => t.EntryTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (hourlyPerformance.Count > 0)
        {
            var bestHour = hourlyPerformance.OrderByDescending(kvp => kvp.Value).First();
            var worstHour = hourlyPerformance.OrderBy(kvp => kvp.Value).First();
            
            if (bestHour.Value > worstHour.Value * HourlyPerformanceMultiplierThreshold)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = "TIME_OPTIMIZATION",
                    Priority = TimeOptimizationPriority,
                    Description = $"Hour {bestHour.Key} performs significantly better than hour {worstHour.Key}",
                    Action = "Focus trading activity during higher-performing hours",
                    Strategy = "ALL"
                });
            }
        }
        
        return recommendations;
    }
    
    private List<OptimizationRecommendation> AnalyzeRiskManagement()
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        if (_maxDrawdown > _totalPnL * HighDrawdownRatio && _totalTrades > MinTradesForDrawdownAnalysis)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "RISK_MANAGEMENT",
                Priority = RiskManagementOptimizationPriority,
                Description = $"High maximum drawdown: ${_maxDrawdown:F2}",
                Action = "Reduce position sizes or improve stop loss management",
                Strategy = "ALL"
            });
        }
        
        if (_avgLoss > _avgWin * RiskRewardRatioThreshold && _totalTrades > MinimumTradesForAnalysis)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "RISK_REWARD",
                Priority = RiskRewardOptimizationPriority,
                Description = "Average loss significantly exceeds average win",
                Action = "Improve profit targets or tighten stop losses",
                Strategy = "ALL"
            });
        }
        
        return recommendations;
    }
}

/// <summary>
/// Strategy-specific performance metrics
/// </summary>
public class StrategyPerformance
{
    public string StrategyName { get; set; } = "";
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal RecentPerformance { get; set; }
}

/// <summary>
/// Overall performance summary (Autonomous)
/// </summary>
public class AutonomousPerformanceSummary
{
    public decimal TotalPnL { get; set; }
    public decimal TodayPnL { get; set; }
    public decimal WeekPnL { get; set; }
    public decimal MonthPnL { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public string BestStrategy { get; set; } = "";
    public string WorstStrategy { get; set; } = "";
}

/// <summary>
/// Daily performance report
/// </summary>
public class DailyPerformanceReport
{
    public DateTime Date { get; set; }
    public decimal DailyPnL { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public string BestStrategy { get; set; } = "";
    public string WorstStrategy { get; set; } = "";
    public IReadOnlyList<string> TradingInsights => _tradingInsights;
    public IReadOnlyList<OptimizationRecommendation> OptimizationRecommendations => _optimizationRecommendations;
    
    private readonly List<string> _tradingInsights = new();
    private readonly List<OptimizationRecommendation> _optimizationRecommendations = new();
    
    public void ReplaceTradingInsights(IEnumerable<string> items)
    {
        _tradingInsights.Clear();
        if (items != null) _tradingInsights.AddRange(items);
    }
    
    public void ReplaceOptimizationRecommendations(IEnumerable<OptimizationRecommendation> items)
    {
        _optimizationRecommendations.Clear();
        if (items != null) _optimizationRecommendations.AddRange(items);
    }
}

/// <summary>
/// Learning insight from trade analysis
/// </summary>
public class LearningInsight
{
    public DateTime Timestamp { get; set; }
    public string Strategy { get; set; } = "";
    public AutonomousTradeOutcome AutonomousTradeOutcome { get; set; } = new();
    public string InsightType { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Confidence { get; set; }
    
    private readonly Dictionary<string, object> _marketConditions = new();
    public IReadOnlyDictionary<string, object> MarketConditions => _marketConditions;
    
    public void ReplaceMarketConditions(IDictionary<string, object> conditions)
    {
        _marketConditions.Clear();
        if (conditions != null)
        {
            foreach (var kvp in conditions)
            {
                _marketConditions[kvp.Key] = kvp.Value;
            }
        }
    }
}

/// <summary>
/// Strategy learning data
/// </summary>
public class StrategyLearning
{
    public string StrategyName { get; set; } = "";
    
    private readonly List<LearningInsight> _insights = new();
    public IReadOnlyList<LearningInsight> Insights => _insights;
    
    public void ReplaceInsights(IEnumerable<LearningInsight> insights)
    {
        _insights.Clear();
        if (insights != null)
        {
            _insights.AddRange(insights);
        }
    }
    
    public void AddInsight(LearningInsight insight)
    {
        if (insight != null)
        {
            _insights.Add(insight);
        }
    }
}

/// <summary>
/// Performance snapshot for tracking
/// </summary>
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public decimal TotalPnL { get; set; }
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal SharpeRatio { get; set; }
}

/// <summary>
/// Optimization recommendation
/// </summary>
public class OptimizationRecommendation
{
    public string Type { get; set; } = "";
    public int Priority { get; set; }
    public string Description { get; set; } = "";
    public string Action { get; set; } = "";
    public string Strategy { get; set; } = "";
}

/// <summary>
/// Optimization insight for autonomous performance tracking
/// </summary>
public class OptimizationInsight
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal ImpactScore { get; set; }
}