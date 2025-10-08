using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Services;

/// <summary>
/// 🎯 STRATEGY PERFORMANCE ANALYZER 🎯
/// 
/// Analyzes performance of individual trading strategies (S2, S3, S6, S11) to enable
/// intelligent autonomous strategy selection. This component learns which strategies
/// work best under different market conditions and optimizes strategy allocation.
/// 
/// KEY FEATURES:
/// ✅ Strategy-specific performance tracking and analysis
/// ✅ Market condition correlation analysis
/// ✅ Time-based strategy performance patterns
/// ✅ Risk-adjusted strategy scoring
/// ✅ Autonomous strategy selection recommendations
/// ✅ Performance degradation detection
/// ✅ Strategy optimization insights
/// 
/// This enables the autonomous engine to:
/// - Automatically select the best strategy for current conditions
/// - Detect when a strategy is underperforming
/// - Learn optimal strategy switching patterns
/// - Optimize strategy parameters based on performance
/// - Adapt strategy selection to changing market conditions
/// </summary>
public class StrategyPerformanceAnalyzer
{
    private readonly ILogger<StrategyPerformanceAnalyzer> _logger;
    
    // Strategy tracking data
    private readonly Dictionary<string, StrategyAnalysis> _strategyAnalysis = new();
    private readonly Dictionary<string, Queue<StrategyPerformanceSnapshot>> _performanceHistory = new();
    private readonly Dictionary<AnalyzerMarketRegime, Dictionary<string, decimal>> _regimePerformance = new();
    private readonly Dictionary<TimeSpan, Dictionary<string, decimal>> _timeBasedPerformance = new();
    private readonly object _analysisLock = new();
    
    // Analysis parameters
    private const int MinTradesForAnalysis = 10;
    private const int PerformanceHistoryLimit = 1000;
    private const int RecentTradesWindow = 50;
    
    // Strategy scoring constants
    private const decimal DefaultStrategyScore = 0.5m;        // Default score for unknown strategy
    private const decimal BaseScoreWeight = 0.25m;            // Weight for base performance score
    private const decimal RegimeScoreWeight = 0.25m;          // Weight for regime-specific score
    private const decimal TimeScoreWeight = 0.20m;            // Weight for time-specific score
    private const decimal RecentScoreWeight = 0.20m;          // Weight for recent performance score
    private const decimal ConsistencyScoreWeight = 0.10m;     // Weight for consistency score
    
    // Performance alert thresholds
    private const decimal LowPerformanceThreshold = 0.3m;     // Threshold for low recent performance
    private const decimal GoodPerformanceThreshold = 0.6m;    // Threshold for good overall performance
    
    // Strategy characteristics - volatility ranges
    private const decimal S6VolatilityRangeLow = 0.5m;
    private const decimal S6VolatilityRangeHigh = 1.0m;
    private const decimal S11VolatilityRangeLow = 0.4m;
    private const decimal S11VolatilityRangeHigh = 0.9m;
    
    // Profit factor constants
    private const decimal MaxProfitFactorFallback = 99m;
    
    // Performance thresholds for scoring
    private const decimal VeryLowThreshold = 0.2m;
    private const decimal LowThreshold = 0.3m;
    private const decimal ModerateThreshold = 0.5m;
    private const decimal HighThreshold = 0.6m;
    private const decimal VeryHighThreshold = 0.8m;
    
    // PnL thresholds for performance categorization
    private const decimal SmallProfitThreshold = 200m;
    private const decimal MediumProfitThreshold = 500m;
    
    // Strategy suitability and optimization thresholds
    private const decimal StrongRecentPerformanceThreshold = 0.7m;
    private const decimal TimeOptimizationMultiplier = 2m;         // Best hour must be 2x better than worst
    private const decimal HighImpactTimeOptimizationScore = 0.8m;
    private const decimal NegativePnLThreshold = -100m;            // Threshold for poor regime performance
    private const decimal HighImpactRegimeOptimizationScore = 0.7m;
    private const decimal RiskRewardRatioThreshold = 1.5m;         // Average loss should not exceed 1.5x average win
    private const decimal VeryHighImpactRiskOptimizationScore = 0.9m;
    private const int MinTradesForEntryOptimization = 20;
    private const decimal HighImpactEntryOptimizationScore = 0.8m;
    private const int DefaultBestTradingHour = 10;                 // Default best trading hour
    private const int DefaultWorstTradingHour = 12;                // Default worst trading hour
    private const int MinSnapshotsForTrendAnalysis = 10;
    private const int TrendAnalysisRecentWindow = 10;
    private const int TrendAnalysisFirstHalfSize = 5;
    private const decimal TrendImprovementThreshold = 0.1m;        // 10% improvement to be considered improving
    private const decimal TrendDecliningThreshold = -0.1m;         // -10% to be considered declining
    private const decimal LargeProfitThreshold = 1000m;
    private const decimal SmallLossThreshold = 200m;
    private const decimal MediumLossThreshold = 400m;
    
    // Sample size requirements
    private const int MinSampleSizeForMediumConfidence = 10;
    private const int MinTradesForRecentAnalysis = 5;
    private const int MinTradesForConsistencyAnalysis = 10;
    private const int RecentTradesForScore = 20;
    
    // PnL normalization factors for scoring
    private const decimal ProfitabilityNormalizationFactor = 1000m;
    private const decimal RegimeScoreNormalizationBase = 500m;
    private const decimal RegimeScoreNormalizationRange = 1000m;
    private const decimal TimeScoreNormalizationBase = 200m;
    private const decimal TimeScoreNormalizationRange = 400m;
    private const decimal RecentPnLNormalizationBase = 300m;
    private const decimal RecentPnLNormalizationRange = 600m;
    private const decimal ConsistencyNormalizationFactor = 100m;
    
    // Loss thresholds for alerts
    private const decimal SignificantRecentLoss = -200m;
    
    // Score weights for composite scoring
    private const decimal ProfitabilityWeight = 0.3m;
    private const decimal WinRateWeight = 0.3m;
    private const decimal ProfitFactorWeight = 0.2m;
    private const decimal DrawdownWeight = 0.2m;
    private const decimal PnLScoreWeight = 0.6m;
    private const decimal WinRateScoreWeight = 0.4m;
    
    // Divisors for score normalization
    private const decimal ProfitFactorNormalizationDivisor = 2m;
    
    // Volatility adjustment multipliers
    private const decimal LowVolatilityThresholdMultiplier = 0.5m;
    private const decimal HighVolatilityThresholdMultiplier = 2m;
    private const decimal PoorVolatilityPenaltyMultiplier = 0.8m;
    
    // Confidence calculation factors
    private const decimal BaseConfidence = 0.5m;
    private const decimal ConfidenceGapMultiplier = 2m;
    
    // Time window tolerance for preferred times (in hours)
    private const double PreferredTimeToleranceHours = 1.0;
    
    // Time window constants for strategy optimization (in hours)
    private const int MorningSessionStartHour = 9;
    private const int MorningSessionStartMinute = 30;
    private const int AfternoonSessionStartHour = 12;
    private const int AfternoonSessionEndHour = 15;
    private const int AfternoonExtendedEndHour = 15;
    private const int AfternoonExtendedEndMinute = 30;
    private const int MarketCloseHour = 16;
    private const decimal LowWinRateThreshold = 0.35m;        // Threshold for low win rate alert
    private const decimal LowProfitFactorThreshold = 1.1m;    // Threshold for low profit factor alert
    private const decimal ExcessiveDrawdownRatio = 0.4m;      // Ratio for excessive drawdown alert
    private const int MinTradesForWinRateAlert = 20;          // Minimum trades for win rate alert
    private const int MinTradesForProfitFactorAlert = 15;     // Minimum trades for profit factor alert
    
    // Performance scoring multipliers
    private const decimal OptimalVolatilityBoostMultiplier = 1.1m; // Score boost for optimal volatility
    
    // Strategy recommendation constants
    private const int MaxRecommendationsToReturn = 5;       // Maximum recommendations to return
    
    // Strategy characteristics constants - Volatility ranges
    private const decimal S2VolatilityRangeLow = 0.3m;      // S2 strategy optimal volatility low
    private const decimal S2VolatilityRangeHigh = 0.7m;     // S2 strategy optimal volatility high
    private const decimal S3VolatilityRangeLow = 0.2m;      // S3 strategy optimal volatility low
    private const decimal S3VolatilityRangeHigh = 0.6m;     // S3 strategy optimal volatility high
    
    // Strategy definitions and characteristics
    private Dictionary<string, StrategyCharacteristics> _strategyCharacteristics = new();
    
    public StrategyPerformanceAnalyzer(ILogger<StrategyPerformanceAnalyzer> logger)
    {
        _logger = logger;
        
        InitializeStrategyAnalysis();
        InitializeStrategyCharacteristics();
        
        _logger.LogInformation("🎯 [STRATEGY-ANALYZER] Initialized - Strategy performance analysis and optimization ready");
    }
    
    /// <summary>
    /// Analyze strategy performance and update metrics
    /// </summary>
    public Task AnalyzeStrategyPerformanceAsync(string strategy, AnalyzerTradeOutcome[] trades, AnalyzerMarketRegime currentRegime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trades);
        
        lock (_analysisLock)
        {
            if (!_strategyAnalysis.ContainsKey(strategy))
            {
                InitializeStrategyAnalysis(strategy);
            }
            
            var analysis = _strategyAnalysis[strategy];
            
            // Update trade data
            foreach (var trade in trades)
            {
                analysis.AllTrades.Add(trade);
                
                // Update regime-based performance
                if (!_regimePerformance.TryGetValue(trade.AnalyzerMarketRegime, out var regimeDict))
                {
                    regimeDict = new Dictionary<string, decimal>();
                    _regimePerformance[trade.AnalyzerMarketRegime] = regimeDict;
                }
                if (!regimeDict.ContainsKey(strategy))
                {
                    regimeDict[strategy] = 0m;
                }
                regimeDict[strategy] += trade.PnL;
                
                // Update time-based performance
                var timeKey = new TimeSpan(trade.EntryTime.Hour, 0, 0);
                if (!_timeBasedPerformance.TryGetValue(timeKey, out var timeDict))
                {
                    timeDict = new Dictionary<string, decimal>();
                    _timeBasedPerformance[timeKey] = timeDict;
                }
                if (!timeDict.ContainsKey(strategy))
                {
                    timeDict[strategy] = 0m;
                }
                _timeBasedPerformance[timeKey][strategy] += trade.PnL;
            }
            
            // Update analysis metrics
            UpdateStrategyAnalysisMetrics(strategy);
            
            // Record performance snapshot
            RecordPerformanceSnapshot(strategy, currentRegime);
            
            _logger.LogDebug("🎯 [STRATEGY-ANALYZER] Updated analysis for {Strategy}: {Trades} trades, Score: {Score:F3}",
                strategy, analysis.AllTrades.Count, analysis.OverallScore);
        }

        // Generate insights outside the lock
        return GenerateStrategyInsightsAsync(strategy, cancellationToken);
    }
    
    /// <summary>
    /// Get performance score for a strategy under current conditions
    /// </summary>
    public async Task<decimal> GetStrategyScoreAsync(string strategy, AnalyzerMarketRegime regime, TimeSpan currentTime, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_analysisLock)
        {
            if (!_strategyAnalysis.TryGetValue(strategy, out var analysis))
            {
                return DefaultStrategyScore; // Default score for unknown strategy
            }
            
            // Multi-factor scoring
            var baseScore = analysis.OverallScore;
            var regimeScore = GetRegimeSpecificScore(strategy, regime);
            var timeScore = GetTimeSpecificScore(strategy, currentTime);
            var recentScore = GetRecentPerformanceScore(strategy);
            var consistencyScore = GetConsistencyScore(strategy);
            
            // Weighted combination
            var totalScore = 
                (baseScore * BaseScoreWeight) +
                (regimeScore * RegimeScoreWeight) +
                (timeScore * TimeScoreWeight) +
                (recentScore * RecentScoreWeight) +
                (consistencyScore * ConsistencyScoreWeight);
            
            _logger.LogDebug("🎯 [STRATEGY-ANALYZER] Score for {Strategy}: {Total:F3} (Base:{Base:F2}, Regime:{Regime:F2}, Time:{Time:F2}, Recent:{Recent:F2}, Consistency:{Consistency:F2})",
                strategy, totalScore, baseScore, regimeScore, timeScore, recentScore, consistencyScore);
            
            return Math.Max(0, Math.Min(1, totalScore));
        }
    }
    
    /// <summary>
    /// Get best strategy recommendation for current conditions
    /// </summary>
    public async Task<StrategyRecommendation> GetBestStrategyAsync(AnalyzerMarketRegime regime, TimeSpan currentTime, decimal volatility, CancellationToken cancellationToken = default)
    {
        var strategies = new[] { "S2", "S3", "S6", "S11" };
        var strategyScores = new Dictionary<string, decimal>();
        
        foreach (var strategy in strategies)
        {
            var score = await GetStrategyScoreAsync(strategy, regime, currentTime, cancellationToken).ConfigureAwait(false);
            
            // Apply volatility adjustments
            score = ApplyVolatilityAdjustment(strategy, score, volatility);
            
            strategyScores[strategy] = score;
        }
        
        var bestStrategy = strategyScores.OrderByDescending(kvp => kvp.Value).First();
        var confidence = CalculateRecommendationConfidence(strategyScores);
        
        var recommendation = new StrategyRecommendation
        {
            RecommendedStrategy = bestStrategy.Key,
            Score = bestStrategy.Value,
            Confidence = confidence,
            Reasoning = GenerateRecommendationReasoning(bestStrategy.Key, regime, currentTime, volatility)
        };
        
        // Copy alternative strategies to read-only collection
        var alternatives = strategyScores
            .Where(kvp => kvp.Key != bestStrategy.Key)
            .OrderByDescending(kvp => kvp.Value)
            .Take(2)
            .ToList();
            
        foreach (var alt in alternatives)
        {
            recommendation.AlternativeStrategies[alt.Key] = alt.Value;
        }
        
        return recommendation;
    }
    
    /// <summary>
    /// Detect if a strategy is underperforming and needs attention
    /// </summary>
    public async Task<List<StrategyAlert>> DetectPerformanceIssuesAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        var alerts = new List<StrategyAlert>();
        
        lock (_analysisLock)
        {
            foreach (var strategy in _strategyAnalysis.Keys)
            {
                var analysis = _strategyAnalysis[strategy];
                
                if (analysis.AllTrades.Count < MinTradesForAnalysis)
                    continue;
                
                // Check for performance degradation
                var recentPerformance = GetRecentPerformanceScore(strategy);
                if (recentPerformance < LowPerformanceThreshold && analysis.OverallScore > GoodPerformanceThreshold)
                {
                    alerts.Add(new StrategyAlert
                    {
                        Strategy = strategy,
                        AlertType = StrategyAlertType.PerformanceDegradation,
                        Severity = AlertSeverity.High,
                        Message = $"Strategy {strategy} showing significant performance degradation",
                        Recommendation = "Consider reducing allocation or reviewing parameters",
                        Timestamp = DateTime.UtcNow
                    });
                }
                
                // Check for low win rate
                if (analysis.WinRate < LowWinRateThreshold && analysis.AllTrades.Count > MinTradesForWinRateAlert)
                {
                    alerts.Add(new StrategyAlert
                    {
                        Strategy = strategy,
                        AlertType = StrategyAlertType.LowWinRate,
                        Severity = AlertSeverity.Medium,
                        Message = $"Strategy {strategy} has low win rate: {analysis.WinRate:P1}",
                        Recommendation = "Review entry criteria and market fit",
                        Timestamp = DateTime.UtcNow
                    });
                }
                
                // Check for poor profit factor
                if (analysis.ProfitFactor < LowProfitFactorThreshold && analysis.AllTrades.Count > MinTradesForProfitFactorAlert)
                {
                    alerts.Add(new StrategyAlert
                    {
                        Strategy = strategy,
                        AlertType = StrategyAlertType.PoorProfitFactor,
                        Severity = AlertSeverity.Medium,
                        Message = $"Strategy {strategy} has poor profit factor: {analysis.ProfitFactor:F2}",
                        Recommendation = "Improve risk-reward ratio or exit criteria",
                        Timestamp = DateTime.UtcNow
                    });
                }
                
                // Check for excessive drawdown
                if (analysis.MaxDrawdown > analysis.TotalPnL * ExcessiveDrawdownRatio && analysis.TotalPnL > 0)
                {
                    alerts.Add(new StrategyAlert
                    {
                        Strategy = strategy,
                        AlertType = StrategyAlertType.ExcessiveDrawdown,
                        Severity = AlertSeverity.High,
                        Message = $"Strategy {strategy} has excessive drawdown: ${analysis.MaxDrawdown:F2}",
                        Recommendation = "Implement better risk management",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
        
        return alerts.OrderByDescending(a => a.Severity).ToList();
    }
    
    /// <summary>
    /// Get optimization recommendations for a specific strategy
    /// </summary>
    public async Task<List<StrategyOptimization>> GetOptimizationRecommendationsAsync(string strategy, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        var recommendations = new List<StrategyOptimization>();
        
        lock (_analysisLock)
        {
            if (!_strategyAnalysis.TryGetValue(strategy, out var analysis) || 
                analysis.AllTrades.Count < MinTradesForAnalysis)
            {
                return recommendations;
            }
            
            // Analyze time-based patterns
            var timeOptimizations = AnalyzeTimeBasedOptimizations(strategy);
            recommendations.AddRange(timeOptimizations);
            
            // Analyze market regime patterns
            var regimeOptimizations = AnalyzeRegimeBasedOptimizations(strategy);
            recommendations.AddRange(regimeOptimizations);
            
            // Analyze risk management
            var riskOptimizations = AnalyzeRiskOptimizations(strategy);
            recommendations.AddRange(riskOptimizations);
            
            // Analyze entry/exit patterns
            var entryExitOptimizations = AnalyzeEntryExitOptimizations(strategy);
            recommendations.AddRange(entryExitOptimizations);
        }
        
        return recommendations.OrderByDescending(r => r.ImpactScore).Take(MaxRecommendationsToReturn).ToList();
    }
    
    /// <summary>
    /// Get detailed strategy analysis report
    /// </summary>
    public StrategyAnalysisReport GetStrategyReport(string strategy)
    {
        lock (_analysisLock)
        {
            if (!_strategyAnalysis.TryGetValue(strategy, out var analysis))
            {
                return new StrategyAnalysisReport { StrategyName = strategy };
            }
            
            return new StrategyAnalysisReport
            {
                StrategyName = strategy,
                TotalTrades = analysis.AllTrades.Count,
                TotalPnL = analysis.TotalPnL,
                WinRate = analysis.WinRate,
                ProfitFactor = analysis.ProfitFactor,
                OverallScore = analysis.OverallScore,
                MaxDrawdown = analysis.MaxDrawdown,
                AverageWin = analysis.AverageWin,
                AverageLoss = analysis.AverageLoss,
                BestMarketRegime = GetBestMarketRegime(strategy),
                WorstMarketRegime = GetWorstMarketRegime(strategy),
                BestTradingHour = GetBestTradingHour(strategy),
                WorstTradingHour = GetWorstTradingHour(strategy),
                RecentPerformanceTrend = GetRecentPerformanceTrend(strategy),
                PerformanceConsistency = GetConsistencyScore(strategy),
                Characteristics = _strategyCharacteristics.GetValueOrDefault(strategy, new StrategyCharacteristics())
            };
        }
    }
    
    private void InitializeStrategyAnalysis()
    {
        var strategies = new[] { "S2", "S3", "S6", "S11" };
        foreach (var strategy in strategies)
        {
            InitializeStrategyAnalysis(strategy);
        }
    }
    
    private void InitializeStrategyAnalysis(string strategy)
    {
        _strategyAnalysis[strategy] = new StrategyAnalysis
        {
            StrategyName = strategy,
            OverallScore = DefaultStrategyScore // Default neutral score
        };
        
        _performanceHistory[strategy] = new Queue<StrategyPerformanceSnapshot>();
    }
    
    private void InitializeStrategyCharacteristics()
    {
        _strategyCharacteristics = new Dictionary<string, StrategyCharacteristics>
        {
            ["S2"] = new StrategyCharacteristics
            {
                Name = "S2",
                Type = StrategyType.MeanReversion,
                BestMarketConditions = new[] { AnalyzerMarketRegime.Ranging, AnalyzerMarketRegime.LowVolatility },
                OptimalVolatilityRange = new[] { S2VolatilityRangeLow, S2VolatilityRangeHigh },
                PreferredTimeWindows = new[] { new TimeSpan(11, 0, 0), new TimeSpan(14, 0, 0) },
                Description = "Mean reversion strategy optimized for ranging markets"
            },
            ["S3"] = new StrategyCharacteristics
            {
                Name = "S3",
                Type = StrategyType.MeanReversion,
                BestMarketConditions = new[] { AnalyzerMarketRegime.Ranging, AnalyzerMarketRegime.LowVolatility },
                OptimalVolatilityRange = new[] { S3VolatilityRangeLow, S3VolatilityRangeHigh },
                PreferredTimeWindows = new[] { new TimeSpan(12, 0, 0), new TimeSpan(15, 0, 0) },
                Description = "Enhanced mean reversion with adaptive parameters"
            },
            ["S6"] = new StrategyCharacteristics
            {
                Name = "S6",
                Type = StrategyType.Momentum,
                BestMarketConditions = new[] { AnalyzerMarketRegime.Trending, AnalyzerMarketRegime.Volatile },
                OptimalVolatilityRange = new[] { S6VolatilityRangeLow, S6VolatilityRangeHigh },
                PreferredTimeWindows = new[] { new TimeSpan(MorningSessionStartHour, MorningSessionStartMinute, 0), new TimeSpan(AfternoonExtendedEndHour, AfternoonExtendedEndMinute, 0) },
                Description = "Momentum strategy for trending and volatile markets"
            },
            ["S11"] = new StrategyCharacteristics
            {
                Name = "S11",
                Type = StrategyType.TrendFollowing,
                BestMarketConditions = new[] { AnalyzerMarketRegime.Trending },
                OptimalVolatilityRange = new[] { S11VolatilityRangeLow, S11VolatilityRangeHigh },
                PreferredTimeWindows = new[] { new TimeSpan(MorningSessionStartHour, MorningSessionStartMinute, 0), new TimeSpan(MarketCloseHour, 0, 0) },
                Description = "Advanced trend following with multi-timeframe analysis"
            }
        };
    }
    
    private void UpdateStrategyAnalysisMetrics(string strategy)
    {
        var analysis = _strategyAnalysis[strategy];
        var trades = analysis.AllTrades;
        
        if (trades.Count == 0) return;
        
        // Basic metrics
        analysis.TotalPnL = trades.Sum(t => t.PnL);
        var winningTrades = trades.Where(t => t.PnL > 0).ToArray();
        var losingTrades = trades.Where(t => t.PnL < 0).ToArray();
        
        analysis.WinRate = trades.Count > 0 ? (decimal)winningTrades.Length / trades.Count : 0m;
        analysis.AverageWin = winningTrades.Length > 0 ? winningTrades.Average(t => t.PnL) : 0m;
        analysis.AverageLoss = losingTrades.Length > 0 ? losingTrades.Average(t => t.PnL) : 0m;
        
        // Profit factor
        var totalWins = winningTrades.Sum(t => t.PnL);
        var totalLosses = Math.Abs(losingTrades.Sum(t => t.PnL));
        analysis.ProfitFactor = totalLosses > 0 ? totalWins / totalLosses : totalWins > 0 ? MaxProfitFactorFallback : 0m;
        
        // Max drawdown
        analysis.MaxDrawdown = CalculateMaxDrawdown(trades);
        
        // Overall score calculation
        analysis.OverallScore = CalculateOverallScore(analysis);
    }
    
    private static decimal CalculateMaxDrawdown(List<AnalyzerTradeOutcome> trades)
    {
        if (trades.Count == 0) return 0m;
        
        var runningPnL = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;
        
        foreach (var trade in trades.OrderBy(t => t.EntryTime))
        {
            runningPnL += trade.PnL;
            if (runningPnL > peak) peak = runningPnL;
            
            var drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }
        
        return maxDrawdown;
    }
    
    private static decimal CalculateOverallScore(StrategyAnalysis analysis)
    {
        if (analysis.AllTrades.Count < MinTradesForRecentAnalysis) return ModerateThreshold;
        
        // Multi-factor scoring
        var profitabilityScore = analysis.TotalPnL > 0 ? Math.Min(1m, analysis.TotalPnL / ProfitabilityNormalizationFactor) : 0m;
        var winRateScore = analysis.WinRate;
        var profitFactorScore = Math.Min(1m, analysis.ProfitFactor / ProfitFactorNormalizationDivisor);
        var drawdownScore = analysis.TotalPnL > 0 ? Math.Max(0m, 1m - (analysis.MaxDrawdown / analysis.TotalPnL)) : 0m;
        
        return (profitabilityScore * ProfitabilityWeight) + (winRateScore * WinRateWeight) + 
               (profitFactorScore * ProfitFactorWeight) + (drawdownScore * DrawdownWeight);
    }
    
    private void RecordPerformanceSnapshot(string strategy, AnalyzerMarketRegime regime)
    {
        var analysis = _strategyAnalysis[strategy];
        
        var snapshot = new StrategyPerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            TotalPnL = analysis.TotalPnL,
            WinRate = analysis.WinRate,
            ProfitFactor = analysis.ProfitFactor,
            OverallScore = analysis.OverallScore,
            AnalyzerMarketRegime = regime,
            TradeCount = analysis.AllTrades.Count
        };
        
        _performanceHistory[strategy].Enqueue(snapshot);
        
        // Keep limited history
        while (_performanceHistory[strategy].Count > PerformanceHistoryLimit)
        {
            _performanceHistory[strategy].Dequeue();
        }
    }
    
    private Task GenerateStrategyInsightsAsync(string strategy, CancellationToken cancellationToken = default)
    {
        // Generate insights based on recent performance changes
        var analysis = _strategyAnalysis[strategy];
        
        if (analysis.AllTrades.Count >= MinTradesForAnalysis)
        {
            var recentTrades = analysis.AllTrades.TakeLast(RecentTradesWindow).ToArray();
            var recentPnL = recentTrades.Sum(t => t.PnL);
            var recentWinRate = recentTrades.Count(t => t.PnL > 0) / (decimal)recentTrades.Length;
            
            if (recentPnL > 0 && recentWinRate > HighThreshold)
            {
                _logger.LogInformation("✨ [STRATEGY-ANALYZER] {Strategy} showing strong recent performance: ${PnL:F2}, WR: {WinRate:P1}",
                    strategy, recentPnL, recentWinRate);
            }
            else if (recentPnL < SignificantRecentLoss || recentWinRate < LowThreshold)
            {
                _logger.LogWarning("⚠️ [STRATEGY-ANALYZER] {Strategy} showing weak recent performance: ${PnL:F2}, WR: {WinRate:P1}",
                    strategy, recentPnL, recentWinRate);
            }
        }

        return Task.CompletedTask;
    }
    
    private decimal GetRegimeSpecificScore(string strategy, AnalyzerMarketRegime regime)
    {
        if (!_regimePerformance.TryGetValue(regime, out var regimeDict) || 
            !regimeDict.ContainsKey(strategy))
        {
            // Use strategy characteristics for unknown regimes
            var characteristics = _strategyCharacteristics.GetValueOrDefault(strategy);
            if (characteristics != null && characteristics.BestMarketConditions.Contains(regime))
            {
                return VeryHighThreshold; // High score for theoretically good conditions
            }
            return ModerateThreshold; // Neutral score
        }
        
        var regimePnL = _regimePerformance[regime][strategy];
        
        // Normalize score based on performance in this regime
        return Math.Max(0m, Math.Min(1m, (regimePnL + RegimeScoreNormalizationBase) / RegimeScoreNormalizationRange));
    }
    
    private decimal GetTimeSpecificScore(string strategy, TimeSpan currentTime)
    {
        var hour = new TimeSpan(currentTime.Hours, 0, 0);
        
        if (!_timeBasedPerformance.TryGetValue(hour, out var hourDict) || 
            !hourDict.ContainsKey(strategy))
        {
            // Use strategy characteristics for unknown times
            var characteristics = _strategyCharacteristics.GetValueOrDefault(strategy);
            if (characteristics?.PreferredTimeWindows != null)
            {
                var isPreferredTime = characteristics.PreferredTimeWindows.Any(window => 
                    Math.Abs((window - currentTime).TotalHours) < PreferredTimeToleranceHours);
                return isPreferredTime ? VeryHighThreshold : ModerateThreshold;
            }
            return HighThreshold; // Slightly positive default
        }
        
        var timePnL = _timeBasedPerformance[hour][strategy];
        
        // Normalize score based on performance at this time
        return Math.Max(0m, Math.Min(1m, (timePnL + TimeScoreNormalizationBase) / TimeScoreNormalizationRange));
    }
    
    private decimal GetRecentPerformanceScore(string strategy)
    {
        if (!_strategyAnalysis.TryGetValue(strategy, out var analysis))
            return ModerateThreshold;
        if (analysis.AllTrades.Count < MinTradesForConsistencyAnalysis)
            return ModerateThreshold;
        
        var recentTrades = analysis.AllTrades.TakeLast(Math.Min(RecentTradesForScore, analysis.AllTrades.Count / 2)).ToArray();
        var recentPnL = recentTrades.Sum(t => t.PnL);
        var recentWinRate = recentTrades.Count(t => t.PnL > 0) / (decimal)recentTrades.Length;
        
        // Combine P&L and win rate for recent performance score
        var pnlScore = Math.Max(0m, Math.Min(1m, (recentPnL + RecentPnLNormalizationBase) / RecentPnLNormalizationRange));
        var winRateScore = recentWinRate;
        
        return (pnlScore * PnLScoreWeight) + (winRateScore * WinRateScoreWeight);
    }
    
    private decimal GetConsistencyScore(string strategy)
    {
        if (!_strategyAnalysis.TryGetValue(strategy, out var analysis))
            return ModerateThreshold;
        if (analysis.AllTrades.Count < MinTradesForConsistencyAnalysis)
            return ModerateThreshold;
        
        var tradePnLs = analysis.AllTrades.Select(t => t.PnL).ToArray();
        var avgPnL = tradePnLs.Average();
        var variance = tradePnLs.Sum(pnl => Math.Pow((double)(pnl - avgPnL), 2)) / tradePnLs.Length;
        var stdDev = (decimal)Math.Sqrt(variance);
        
        // Lower standard deviation = higher consistency
        var consistencyScore = Math.Max(0m, 1m - (stdDev / ConsistencyNormalizationFactor));
        
        return Math.Max(0m, Math.Min(1m, consistencyScore));
    }
    
    private decimal ApplyVolatilityAdjustment(string strategy, decimal baseScore, decimal volatility)
    {
        var characteristics = _strategyCharacteristics.GetValueOrDefault(strategy);
        if (characteristics?.OptimalVolatilityRange == null)
            return baseScore;
        
        var minVol = characteristics.OptimalVolatilityRange[0];
        var maxVol = characteristics.OptimalVolatilityRange[1];
        
        if (volatility >= minVol && volatility <= maxVol)
        {
            return baseScore * OptimalVolatilityBoostMultiplier; // Boost score for optimal volatility
        }
        else if (volatility < minVol * LowVolatilityThresholdMultiplier || volatility > maxVol * HighVolatilityThresholdMultiplier)
        {
            return baseScore * PoorVolatilityPenaltyMultiplier; // Reduce score for poor volatility fit
        }
        
        return baseScore;
    }
    
    private static decimal CalculateRecommendationConfidence(Dictionary<string, decimal> strategyScores)
    {
        var scores = strategyScores.Values.OrderByDescending(s => s).ToArray();
        if (scores.Length < 2) return BaseConfidence;
        
        var topScore = scores[0];
        var secondScore = scores[1];
        
        // Higher confidence when there's a clear winner
        var gap = topScore - secondScore;
        return Math.Min(1m, BaseConfidence + (gap * ConfidenceGapMultiplier));
    }
    
    private string GenerateRecommendationReasoning(string strategy, AnalyzerMarketRegime regime, TimeSpan currentTime, decimal volatility)
    {
        var characteristics = _strategyCharacteristics.GetValueOrDefault(strategy);
        var reasons = new List<string>();
        
        if (characteristics?.BestMarketConditions.Contains(regime) == true)
        {
            reasons.Add($"optimal for {regime} markets");
        }
        
        if (characteristics?.PreferredTimeWindows?.Any(window => 
            Math.Abs((window - currentTime).TotalHours) < 1) == true)
        {
            reasons.Add("preferred trading time");
        }
        
        if (characteristics?.OptimalVolatilityRange != null &&
            volatility >= characteristics.OptimalVolatilityRange[0] &&
            volatility <= characteristics.OptimalVolatilityRange[1])
        {
            reasons.Add("suitable volatility conditions");
        }
        
        var recentScore = GetRecentPerformanceScore(strategy);
        if (recentScore > StrongRecentPerformanceThreshold)
        {
            reasons.Add("strong recent performance");
        }
        
        return reasons.Any() ? string.Join(", ", reasons) : "general performance metrics";
    }
    
    private List<StrategyOptimization> AnalyzeTimeBasedOptimizations(string strategy)
    {
        var optimizations = new List<StrategyOptimization>();
        
        // Analyze performance by hour to identify optimal trading times
        var hourlyPerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.EntryTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (hourlyPerformance.Any())
        {
            var bestHour = hourlyPerformance.OrderByDescending(kvp => kvp.Value).First();
            var worstHour = hourlyPerformance.OrderBy(kvp => kvp.Value).First();
            
            if (bestHour.Value > worstHour.Value * TimeOptimizationMultiplier)
            {
                optimizations.Add(new StrategyOptimization
                {
                    Strategy = strategy,
                    Type = "TIME_FOCUS",
                    Description = $"Focus trading during hour {bestHour.Key} (best performance: ${bestHour.Value:F2})",
                    ImpactScore = HighImpactTimeOptimizationScore,
                    Implementation = $"Increase allocation during {bestHour.Key}:00-{bestHour.Key + 1}:00"
                });
            }
        }
        
        return optimizations;
    }
    
    private List<StrategyOptimization> AnalyzeRegimeBasedOptimizations(string strategy)
    {
        var optimizations = new List<StrategyOptimization>();
        
        // Find best and worst performing regimes
        var regimePerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.AnalyzerMarketRegime)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        if (regimePerformance.Any())
        {
            var bestRegime = regimePerformance.OrderByDescending(kvp => kvp.Value).First();
            var worstRegime = regimePerformance.OrderBy(kvp => kvp.Value).First();
            
            if (bestRegime.Value > 0 && worstRegime.Value < NegativePnLThreshold)
            {
                optimizations.Add(new StrategyOptimization
                {
                    Strategy = strategy,
                    Type = "REGIME_FILTER",
                    Description = $"Avoid trading during {worstRegime.Key} regime (performance: ${worstRegime.Value:F2})",
                    ImpactScore = HighImpactRegimeOptimizationScore,
                    Implementation = $"Add market regime filter to avoid {worstRegime.Key} conditions"
                });
            }
        }
        
        return optimizations;
    }
    
    private List<StrategyOptimization> AnalyzeRiskOptimizations(string strategy)
    {
        var optimizations = new List<StrategyOptimization>();
        var analysis = _strategyAnalysis[strategy];
        
        if (analysis.AverageLoss != 0 && Math.Abs(analysis.AverageLoss) > analysis.AverageWin * RiskRewardRatioThreshold)
        {
            optimizations.Add(new StrategyOptimization
            {
                Strategy = strategy,
                Type = "RISK_REWARD",
                Description = $"Improve risk-reward ratio (AvgWin: ${analysis.AverageWin:F2}, AvgLoss: ${analysis.AverageLoss:F2})",
                ImpactScore = VeryHighImpactRiskOptimizationScore,
                Implementation = "Tighten stop losses or extend profit targets"
            });
        }
        
        return optimizations;
    }
    
    private List<StrategyOptimization> AnalyzeEntryExitOptimizations(string strategy)
    {
        var optimizations = new List<StrategyOptimization>();
        var analysis = _strategyAnalysis[strategy];
        
        if (analysis.WinRate < LowWinRateThreshold && analysis.AllTrades.Count > MinTradesForEntryOptimization)
        {
            optimizations.Add(new StrategyOptimization
            {
                Strategy = strategy,
                Type = "ENTRY_CRITERIA",
                Description = $"Low win rate ({analysis.WinRate:P1}) suggests entry criteria need refinement",
                ImpactScore = HighImpactEntryOptimizationScore,
                Implementation = "Review and tighten entry conditions"
            });
        }
        
        return optimizations;
    }
    
    private AnalyzerMarketRegime GetBestMarketRegime(string strategy)
    {
        var regimePerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.AnalyzerMarketRegime)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        return regimePerformance.Any() 
            ? regimePerformance.OrderByDescending(kvp => kvp.Value).First().Key
            : AnalyzerMarketRegime.Unknown;
    }
    
    private AnalyzerMarketRegime GetWorstMarketRegime(string strategy)
    {
        var regimePerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.AnalyzerMarketRegime)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        return regimePerformance.Any()
            ? regimePerformance.OrderBy(kvp => kvp.Value).First().Key
            : AnalyzerMarketRegime.Unknown;
    }
    
    private int GetBestTradingHour(string strategy)
    {
        var hourlyPerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.EntryTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        return hourlyPerformance.Any()
            ? hourlyPerformance.OrderByDescending(kvp => kvp.Value).First().Key
            : DefaultBestTradingHour;
    }
    
    private int GetWorstTradingHour(string strategy)
    {
        var hourlyPerformance = _strategyAnalysis[strategy].AllTrades
            .GroupBy(t => t.EntryTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.PnL));
        
        return hourlyPerformance.Any()
            ? hourlyPerformance.OrderBy(kvp => kvp.Value).First().Key
            : DefaultWorstTradingHour;
    }
    
    private PerformanceTrend GetRecentPerformanceTrend(string strategy)
    {
        if (!_performanceHistory.TryGetValue(strategy, out var history) ||
            history.Count < MinSnapshotsForTrendAnalysis)
        {
            return PerformanceTrend.Stable;
        }
        
        var recent = history.TakeLast(TrendAnalysisRecentWindow).ToArray();
        var firstHalf = recent.Take(TrendAnalysisFirstHalfSize).Average(s => s.OverallScore);
        var secondHalf = recent.Skip(TrendAnalysisFirstHalfSize).Average(s => s.OverallScore);
        
        var improvement = secondHalf - firstHalf;
        
        if (improvement > TrendImprovementThreshold) return PerformanceTrend.Improving;
        if (improvement < TrendDecliningThreshold) return PerformanceTrend.Declining;
        return PerformanceTrend.Stable;
    }
}

// Supporting classes and enums for strategy analysis
public class StrategyAnalysis
{
    public string StrategyName { get; set; } = "";
    public List<AnalyzerTradeOutcome> AllTrades { get; } = new();
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal OverallScore { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
}

public class StrategyPerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal OverallScore { get; set; }
    public AnalyzerMarketRegime AnalyzerMarketRegime { get; set; }
    public int TradeCount { get; set; }
}

public class StrategyCharacteristics
{
    public string Name { get; set; } = "";
    public StrategyType Type { get; set; }
    public AnalyzerMarketRegime[] BestMarketConditions { get; set; } = Array.Empty<AnalyzerMarketRegime>();
    public decimal[] OptimalVolatilityRange { get; set; } = Array.Empty<decimal>();
    public TimeSpan[] PreferredTimeWindows { get; set; } = Array.Empty<TimeSpan>();
    public string Description { get; set; } = "";
}

public class StrategyRecommendation
{
    public string RecommendedStrategy { get; set; } = "";
    public decimal Score { get; set; }
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public Dictionary<string, decimal> AlternativeStrategies { get; } = new();
}

public class StrategyAlert
{
    public string Strategy { get; set; } = "";
    public StrategyAlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class StrategyOptimization
{
    public string Strategy { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal ImpactScore { get; set; }
    public string Implementation { get; set; } = "";
}

public class StrategyAnalysisReport
{
    public string StrategyName { get; set; } = "";
    public int TotalTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal OverallScore { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public AnalyzerMarketRegime BestMarketRegime { get; set; }
    public AnalyzerMarketRegime WorstMarketRegime { get; set; }
    public int BestTradingHour { get; set; }
    public int WorstTradingHour { get; set; }
    public PerformanceTrend RecentPerformanceTrend { get; set; }
    public decimal PerformanceConsistency { get; set; }
    public StrategyCharacteristics Characteristics { get; set; } = new();
}

public enum StrategyType
{
    TrendFollowing,
    MeanReversion,
    Momentum,
    Arbitrage
}

public enum StrategyAlertType
{
    PerformanceDegradation,
    LowWinRate,
    PoorProfitFactor,
    ExcessiveDrawdown
}

public enum AlertSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum PerformanceTrend
{
    Declining,
    Stable,
    Improving
}

/// <summary>
/// Market regime classification for StrategyPerformanceAnalyzer
/// </summary>
public enum AnalyzerMarketRegime
{
    Unknown,
    Trending,
    Ranging,
    Volatile,
    LowVolatility
}

/// <summary>
/// Trade outcome for strategy analysis
/// </summary>
public class AnalyzerTradeOutcome
{
    public string Strategy { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public int Size { get; set; }
    public decimal Confidence { get; set; }
    public AnalyzerMarketRegime MarketRegime { get; set; }
    public AnalyzerMarketRegime AnalyzerMarketRegime { get; set; }
    public decimal PnL { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }
    public bool IsWin => PnL > 0;
    public decimal RMultiple { get; set; }
    public TimeSpan Duration => ExitTime.HasValue ? ExitTime.Value - EntryTime : TimeSpan.Zero;
}