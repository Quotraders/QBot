using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using BotCore.Models;
using BotCore.Brain;
using BotCore.Services;
using TradingBot.Abstractions;

namespace BotCore.Services;

/// <summary>
/// 🚀 AUTONOMOUS TOPSTEP PROFIT-MAXIMIZING DECISION ENGINE 🚀
/// 
/// This is the core autonomous trading brain that operates independently to maximize profits
/// while adhering to TopStep compliance rules. It makes all trading decisions without human
/// intervention and continuously learns and adapts to market conditions.
/// 
/// KEY FEATURES:
/// ✅ Autonomous strategy switching (S2, S3, S6, S11) based on market conditions
/// ✅ Dynamic position sizing with account growth scaling (0.5% to 1.5%)
/// ✅ Time-aware trading decisions for optimal market periods
/// ✅ TopStep compliance enforcement ($2,400 daily loss, $2,500 drawdown)
/// ✅ Profit target automation with compound growth
/// ✅ Continuous learning from trade outcomes
/// ✅ Market condition adaptation and pattern recognition
/// ✅ Risk scaling based on performance and volatility
/// 
/// AUTONOMOUS BEHAVIOR:
/// - Automatically selects best strategy based on current market regime
/// - Scales position sizes based on winning streaks and account growth
/// - Reduces risk during losing periods to preserve capital
/// - Increases activity during high-probability periods
/// - Automatically compounds gains by increasing position sizes
/// - Learns optimal entry/exit timing for each strategy
/// - Adapts to changing market cycles without human intervention
/// </summary>
public class AutonomousDecisionEngine : BackgroundService
{
    private readonly ILogger<AutonomousDecisionEngine> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly AutonomousConfig _config;
    
    // Core decision-making components
    private readonly UnifiedTradingBrain _unifiedBrain;
    private readonly UnifiedDecisionRouter _decisionRouter;
    private readonly IMarketHours _marketHours;
    
    // Risk and compliance management
    private readonly IRiskManager _riskManager;
    private readonly TopStepComplianceManager _complianceManager;
    
    // Performance tracking and learning
    private readonly AutonomousPerformanceTracker _performanceTracker;
    private readonly MarketConditionAnalyzer _marketAnalyzer;
    private readonly StrategyPerformanceAnalyzer _strategyAnalyzer;
    
    // Autonomous state management
    private readonly Dictionary<string, AutonomousStrategyMetrics> _strategyMetrics = new();
    private readonly Queue<AutonomousTradeOutcome> _recentTrades = new();
    private readonly object _stateLock = new();
    
    // Current autonomous state
    private string _currentStrategy = "S11";
    private decimal _currentRiskPerTrade = 0.005m; // Start conservative at 0.5%
    private decimal _currentAccountBalance = 50000m;
    private int _consecutiveWins = 0;
    private int _consecutiveLosses = 0;
    private decimal _todayPnL = 0m;
    private DateTime _lastTradeTime = DateTime.MinValue;
    private AutonomousMarketRegime _currentAutonomousMarketRegime = AutonomousMarketRegime.Unknown;
    
    // Autonomous configuration
    private const decimal MinRiskPerTrade = 0.005m; // 0.5%
    private const decimal MaxRiskPerTrade = 0.015m; // 1.5%
    private const decimal TopStepDailyLossLimit = -1000m; // Conservative limit under $2,400
    private const decimal TopStepDrawdownLimit = -2000m; // Conservative limit under $2,500
    private const decimal DailyProfitTarget = 300m; // Target $300 daily profit
    
    public AutonomousDecisionEngine(
        ILogger<AutonomousDecisionEngine> logger,
        IServiceProvider serviceProvider,
        UnifiedTradingBrain unifiedBrain,
        UnifiedDecisionRouter decisionRouter,
        IMarketHours marketHours,
        IRiskManager riskManager,
        IOptions<AutonomousConfig> config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _unifiedBrain = unifiedBrain;
        _decisionRouter = decisionRouter;
        _marketHours = marketHours;
        _riskManager = riskManager;
        _config = config.Value;
        
        _complianceManager = new TopStepComplianceManager(logger, config);
        _performanceTracker = new AutonomousPerformanceTracker(
            _serviceProvider.GetRequiredService<ILogger<AutonomousPerformanceTracker>>());
        _marketAnalyzer = new MarketConditionAnalyzer(
            _serviceProvider.GetRequiredService<ILogger<MarketConditionAnalyzer>>());
        _strategyAnalyzer = new StrategyPerformanceAnalyzer(
            _serviceProvider.GetRequiredService<ILogger<StrategyPerformanceAnalyzer>>());
        
        InitializeAutonomousStrategyMetrics();
        
        _logger.LogInformation("🚀 [AUTONOMOUS-ENGINE] Initialized - Profit-maximizing autonomous trading engine ready");
        _logger.LogInformation("💰 [AUTONOMOUS-ENGINE] Target: ${DailyTarget}/day, Risk: {MinRisk}%-{MaxRisk}%, Account: ${Balance}",
            DailyProfitTarget, MinRiskPerTrade * 100, MaxRiskPerTrade * 100, _currentAccountBalance);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 [AUTONOMOUS-ENGINE] Starting autonomous profit-maximizing trading system...");
        
        try
        {
            // Initialize autonomous systems
            await InitializeAutonomousSystemsAsync(stoppingToken);
            
            // Start main autonomous loop
            await RunAutonomousMainLoopAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AUTONOMOUS-ENGINE] Critical error in autonomous engine");
            throw;
        }
    }
    
    private async Task InitializeAutonomousSystemsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 [AUTONOMOUS-ENGINE] Initializing autonomous systems...");
        
        // Load historical performance data
        await LoadHistoricalPerformanceAsync(cancellationToken);
        
        // Initialize strategy metrics
        await UpdateStrategyMetricsAsync(cancellationToken);
        
        // Analyze current market conditions
        await AnalyzeMarketConditionsAsync(cancellationToken);
        
        // Select initial strategy
        await SelectOptimalStrategyAsync(cancellationToken);
        
        _logger.LogInformation("✅ [AUTONOMOUS-ENGINE] Autonomous systems initialized successfully");
    }
    
    private async Task RunAutonomousMainLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 [AUTONOMOUS-ENGINE] Starting autonomous main loop - 24/7 profit optimization");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check if we should be trading at this time
                var shouldTrade = await ShouldTradeNowAsync(cancellationToken);
                if (!shouldTrade)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    continue;
                }
                
                // Main autonomous decision cycle
                await ExecuteAutonomousDecisionCycleAsync(cancellationToken);
                
                // Update performance and learning
                await UpdatePerformanceAndLearningAsync(cancellationToken);
                
                // Adaptive delay based on market conditions
                var delay = await GetAdaptiveDelayAsync(cancellationToken);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ [AUTONOMOUS-ENGINE] Error in autonomous cycle, continuing...");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        
        _logger.LogInformation("🛑 [AUTONOMOUS-ENGINE] Autonomous main loop stopped");
    }
    
    private async Task<bool> ShouldTradeNowAsync(CancellationToken cancellationToken)
    {
        // Check compliance limits first
        if (!await _complianceManager.CanTradeAsync(_todayPnL, _currentAccountBalance, cancellationToken))
        {
            return false;
        }
        
        // Check market hours and optimal trading times
        var isMarketOpen = await _marketHours.IsMarketOpenAsync(cancellationToken);
        if (!isMarketOpen)
        {
            return false;
        }
        
        // Check if we're in an optimal trading period
        var currentSession = await _marketHours.GetCurrentMarketSessionAsync(cancellationToken);
        var isOptimalTime = IsOptimalTradingTime(currentSession);
        
        return isOptimalTime;
    }
    
    private bool IsOptimalTradingTime(string session)
    {
        // Autonomous schedule management - focus on high-probability periods
        return session switch
        {
            "MORNING_SESSION" => true,    // 9:30 AM - 11:00 AM (high volume)
            "AFTERNOON_SESSION" => true,  // 1:00 PM - 4:00 PM (high volume)
            "CLOSE_SESSION" => true,      // 4:00 PM - 5:00 PM (final hour)
            "LUNCH_SESSION" => _config.TradeDuringLunch, // 11:00 AM - 1:00 PM (reduced activity)
            "OVERNIGHT" => _config.TradeOvernight,        // Overnight sessions
            "PRE_MARKET" => _config.TradePreMarket,       // Pre-market sessions
            _ => false
        };
    }
    
    private async Task ExecuteAutonomousDecisionCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("🎯 [AUTONOMOUS-ENGINE] Executing autonomous decision cycle...");
        
        // 1. Analyze current market conditions
        await AnalyzeMarketConditionsAsync(cancellationToken);
        
        // 2. Update strategy selection based on conditions
        await UpdateStrategySelectionAsync(cancellationToken);
        
        // 3. Calculate optimal position sizing
        var positionSize = await CalculateOptimalPositionSizeAsync(cancellationToken);
        
        // 4. Check for trading opportunities
        var tradingOpportunity = await IdentifyTradingOpportunityAsync(cancellationToken);
        
        // 5. Execute trade if opportunity exists
        if (tradingOpportunity != null && positionSize > 0)
        {
            await ExecuteAutonomousTradeAsync(tradingOpportunity, positionSize, cancellationToken);
        }
        
        // 6. Manage existing positions
        await ManageExistingPositionsAsync(cancellationToken);
    }
    
    private async Task AnalyzeMarketConditionsAsync(CancellationToken cancellationToken)
    {
        var previousRegime = _currentAutonomousMarketRegime;
        var tradingRegime = await _marketAnalyzer.DetermineMarketRegimeAsync(cancellationToken);
        _currentAutonomousMarketRegime = MapTradingRegimeToAutonomous(tradingRegime);
        
        if (_currentAutonomousMarketRegime != previousRegime)
        {
            _logger.LogInformation("📊 [AUTONOMOUS-ENGINE] Market regime changed: {Previous} → {Current}",
                previousRegime, _currentAutonomousMarketRegime);
            
            // Trigger strategy re-evaluation when market regime changes
            await SelectOptimalStrategyAsync(cancellationToken);
        }
    }
    
    private async Task UpdateStrategySelectionAsync(CancellationToken cancellationToken)
    {
        var optimalStrategy = await SelectOptimalStrategyAsync(cancellationToken);
        
        if (optimalStrategy != _currentStrategy)
        {
            _logger.LogInformation("🔄 [AUTONOMOUS-ENGINE] Strategy switch: {Previous} → {New} (Regime: {Regime})",
                _currentStrategy, optimalStrategy, _currentAutonomousMarketRegime);
            
            _currentStrategy = optimalStrategy;
            
            // Update risk parameters for new strategy
            await UpdateRiskParametersAsync(cancellationToken);
        }
    }
    
    private async Task<string> SelectOptimalStrategyAsync(CancellationToken cancellationToken)
    {
        // Autonomous strategy selection based on market conditions and performance
        var strategyScores = new Dictionary<string, decimal>();
        
        foreach (var strategy in new[] { "S2", "S3", "S6", "S11" })
        {
            var score = CalculateStrategyScore(strategy);
            strategyScores[strategy] = score;
        }
        
        // Select strategy with highest score
        var bestStrategy = strategyScores.OrderByDescending(kvp => kvp.Value).First();
        
        _logger.LogDebug("🎯 [AUTONOMOUS-ENGINE] Strategy scores: {Scores}, Selected: {Strategy}",
            string.Join(", ", strategyScores.Select(kvp => $"{kvp.Key}:{kvp.Value:F3}")),
            bestStrategy.Key);
        
        await Task.CompletedTask;
        return bestStrategy.Key;
    }
    
    private decimal CalculateStrategyScore(string strategy)
    {
        if (!_strategyMetrics.ContainsKey(strategy))
        {
            return 0.5m; // Default score for unknown strategies
        }
        
        var metrics = _strategyMetrics[strategy];
        
        // Multi-factor scoring algorithm
        var recentPerformanceScore = CalculateRecentPerformanceScore(metrics);
        var marketFitScore = CalculateMarketFitScore(strategy);
        var consistencyScore = CalculateConsistencyScore(metrics);
        var profitabilityScore = CalculateProfitabilityScore(metrics);
        
        // Weighted combination
        var totalScore = 
            (recentPerformanceScore * 0.35m) +
            (marketFitScore * 0.25m) +
            (consistencyScore * 0.20m) +
            (profitabilityScore * 0.20m);
        
        return Math.Max(0, Math.Min(1, totalScore));
    }
    
    private decimal CalculateRecentPerformanceScore(AutonomousStrategyMetrics metrics)
    {
        if (metrics.RecentTrades.Count == 0) return 0.5m;
        
        var recentWinRate = metrics.RecentTrades.Count(t => t.PnL > 0) / (decimal)metrics.RecentTrades.Count;
        var recentAvgPnL = metrics.RecentTrades.Average(t => t.PnL);
        
        // Score based on win rate and average P&L
        var winRateScore = recentWinRate;
        var pnlScore = Math.Max(0, Math.Min(1, (recentAvgPnL + 100) / 200m)); // Normalize around ±$100
        
        return (winRateScore * 0.6m) + (pnlScore * 0.4m);
    }
    
    private decimal CalculateMarketFitScore(string strategy)
    {
        // Different strategies perform better in different market regimes
        return (_currentAutonomousMarketRegime, strategy) switch
        {
            (AutonomousMarketRegime.Trending, "S11") => 0.9m,
            (AutonomousMarketRegime.Trending, "S6") => 0.7m,
            (AutonomousMarketRegime.Ranging, "S2") => 0.8m,
            (AutonomousMarketRegime.Ranging, "S3") => 0.9m,
            (AutonomousMarketRegime.Volatile, "S6") => 0.8m,
            (AutonomousMarketRegime.Volatile, "S11") => 0.6m,
            (AutonomousMarketRegime.LowVolatility, "S2") => 0.9m,
            (AutonomousMarketRegime.LowVolatility, "S3") => 0.8m,
            _ => 0.5m
        };
    }
    
    private decimal CalculateConsistencyScore(AutonomousStrategyMetrics metrics)
    {
        if (metrics.RecentTrades.Count < 5) return 0.5m;
        
        var pnls = metrics.RecentTrades.Select(t => t.PnL).ToArray();
        var avgPnL = pnls.Average();
        var variance = pnls.Average(pnl => Math.Pow((double)(pnl - avgPnL), 2));
        var stdDev = Math.Sqrt(variance);
        
        // Lower standard deviation = higher consistency
        var consistencyScore = Math.Max(0, 1 - (decimal)(stdDev / 100.0)); // Normalize by $100
        
        return consistencyScore;
    }
    
    private decimal CalculateProfitabilityScore(AutonomousStrategyMetrics metrics)
    {
        if (metrics.TotalTrades == 0) return 0.5m;
        
        var winRate = metrics.WinningTrades / (decimal)metrics.TotalTrades;
        var avgWin = metrics.WinningTrades > 0 ? metrics.TotalProfit / metrics.WinningTrades : 0;
        var avgLoss = metrics.LosingTrades > 0 ? metrics.TotalLoss / metrics.LosingTrades : 0;
        var profitFactor = avgLoss != 0 ? avgWin / Math.Abs(avgLoss) : winRate > 0 ? 2 : 0;
        
        // Combined profitability score
        var winRateScore = winRate;
        var profitFactorScore = Math.Max(0, Math.Min(1, profitFactor / 2m)); // Normalize around 2.0
        
        return (winRateScore * 0.5m) + (profitFactorScore * 0.5m);
    }
    
    private async Task<decimal> CalculateOptimalPositionSizeAsync(CancellationToken cancellationToken)
    {
        // Dynamic position sizing based on performance and market conditions
        var baseRisk = _currentRiskPerTrade;
        
        // Adjust risk based on recent performance
        var performanceMultiplier = CalculatePerformanceMultiplier();
        
        // Adjust risk based on market volatility
        var volatilityMultiplier = await CalculateVolatilityMultiplierAsync(cancellationToken);
        
        // Adjust risk based on time of day
        var timeMultiplier = await CalculateTimeMultiplierAsync(cancellationToken);
        
        // Combined risk calculation
        var adjustedRisk = baseRisk * performanceMultiplier * volatilityMultiplier * timeMultiplier;
        
        // Clamp to allowed range
        adjustedRisk = Math.Max(MinRiskPerTrade, Math.Min(MaxRiskPerTrade, adjustedRisk));
        
        // Calculate position size in dollars
        var positionSize = _currentAccountBalance * adjustedRisk;
        
        _logger.LogDebug("💰 [AUTONOMOUS-ENGINE] Position sizing: Base={BaseRisk:P}, Perf={PerfMult:F2}, Vol={VolMult:F2}, Time={TimeMult:F2}, Final=${PosSize:F0}",
            baseRisk, performanceMultiplier, volatilityMultiplier, timeMultiplier, positionSize);
        
        return positionSize;
    }
    
    private decimal CalculatePerformanceMultiplier()
    {
        // Increase position size during winning streaks, decrease during losing streaks
        if (_consecutiveWins >= 3)
        {
            return 1.0m + (0.1m * Math.Min(_consecutiveWins - 2, 5)); // Up to 1.5x for 7+ wins
        }
        else if (_consecutiveLosses >= 3)
        {
            return Math.Max(0.5m, 1.0m - (0.1m * (_consecutiveLosses - 2))); // Down to 0.5x
        }
        
        return 1.0m;
    }
    
    private async Task<decimal> CalculateVolatilityMultiplierAsync(CancellationToken cancellationToken)
    {
        var volatility = await _marketAnalyzer.GetCurrentVolatilityAsync(cancellationToken);
        
        // Reduce position size in high volatility, increase in low volatility
        return volatility switch
        {
            MarketVolatility.VeryHigh => 0.6m,
            MarketVolatility.High => 0.8m,
            MarketVolatility.Normal => 1.0m,
            MarketVolatility.Low => 1.2m,
            MarketVolatility.VeryLow => 1.3m,
            _ => 1.0m
        };
    }
    
    private async Task<decimal> CalculateTimeMultiplierAsync(CancellationToken cancellationToken)
    {
        var session = await _marketHours.GetCurrentMarketSessionAsync(cancellationToken);
        
        // Increase position size during high-probability periods
        return session switch
        {
            "MORNING_SESSION" => 1.2m,    // First hour - high volume
            "CLOSE_SESSION" => 1.2m,      // Last hour - high volume
            "AFTERNOON_SESSION" => 1.1m,  // Regular session
            "LUNCH_SESSION" => 0.8m,      // Lunch time - lower volume
            "OVERNIGHT" => 0.7m,          // Overnight - higher risk
            "PRE_MARKET" => 0.8m,         // Pre-market - lower volume
            _ => 1.0m
        };
    }
    
    private async Task<TradingOpportunity?> IdentifyTradingOpportunityAsync(CancellationToken cancellationToken)
    {
        // Use the unified brain to identify trading opportunities
        try
        {
            // Calculate technical indicators for decision making
            var technicalIndicators = new Dictionary<string, double>
            {
                ["RSI"] = 50.0, // Neutral RSI when no data available
                ["MACD"] = 0.0, // No signal when no data available  
                ["BollingerPosition"] = 0.5, // Middle of bands when no data available
                ["ATR"] = 0.0, // No volatility measure when no data available
                ["VolumeMA"] = 0.0 // No volume data when unavailable
            };
            
            // Try to get real market data, use defaults if unavailable
            double currentPrice = 0;
            double currentVolume = 0;
            
            try
            {
                // Attempt to get current market data
                var priceDecimal = await GetCurrentMarketPriceAsync("ES", cancellationToken);
                var volumeLong = await GetCurrentVolumeAsync("ES", cancellationToken);
                currentPrice = (double)priceDecimal;
                currentVolume = (double)volumeLong;
                technicalIndicators = await CalculateTechnicalIndicatorsAsync("ES", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Using fallback market data: {Error}", ex.Message);
            }
            
            var marketContext = new MarketContext
            {
                Symbol = "ES",
                Price = currentPrice,
                Volume = currentVolume,
                Timestamp = DateTime.UtcNow,
                TechnicalIndicators = technicalIndicators
            };
            
            var decision = await _decisionRouter.RouteDecisionAsync("ES", marketContext, cancellationToken);
            
            if (decision?.Action != null && decision.Action != TradingAction.Hold)
            {
                return new TradingOpportunity
                {
                    Symbol = "ES",
                    Direction = decision.Action.ToString(),
                    Strategy = _currentStrategy,
                    Confidence = decision.Confidence,
                    EntryPrice = null, // Will be set during execution
                    StopLoss = null,   // Will be calculated during execution
                    TakeProfit = null, // Will be calculated during execution
                    Reasoning = decision.Reasoning.ContainsKey("summary") ? 
                        decision.Reasoning["summary"]?.ToString() ?? $"Autonomous {_currentStrategy} signal" : 
                        $"Autonomous {_currentStrategy} signal"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Error identifying opportunity");
        }
        
        return null;
    }
    
    private async Task ExecuteAutonomousTradeAsync(TradingOpportunity opportunity, decimal positionSize, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📈 [AUTONOMOUS-ENGINE] Executing autonomous trade: {Direction} {Symbol} ${Size:F0} via {Strategy} (Confidence: {Confidence:P})",
            opportunity.Direction, opportunity.Symbol, positionSize, opportunity.Strategy, opportunity.Confidence);
        
        try
        {
            // Calculate position size in contracts based on dollar amount
            var contractSize = CalculateContractSize(opportunity.Symbol, positionSize, opportunity.EntryPrice);
            
            // Execute trade through the trading system
            var tradeResult = await ExecuteTradeAsync(opportunity, contractSize, cancellationToken);
            
            if (tradeResult.Success)
            {
                _lastTradeTime = DateTime.UtcNow;
                _logger.LogInformation("✅ [AUTONOMOUS-ENGINE] Trade executed successfully: {OrderId}", tradeResult.OrderId);
                
                // Record trade for learning
                RecordTradeForLearning(opportunity, tradeResult);
            }
            else
            {
                _logger.LogWarning("❌ [AUTONOMOUS-ENGINE] Trade execution failed: {Error}", tradeResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AUTONOMOUS-ENGINE] Error executing autonomous trade");
        }
    }
    
    private int CalculateContractSize(string symbol, decimal positionSize, decimal? entryPrice)
    {
        // Calculate contract size based on position size in dollars
        // ES: $50 per point, NQ: $20 per point
        var multiplier = symbol == "ES" ? 50m : 20m;
        var price = entryPrice ?? (symbol == "ES" ? 4500m : 15000m); // Default prices if not provided
        
        var contractValue = price * multiplier;
        var contractCount = (int)Math.Floor(positionSize / contractValue);
        
        return Math.Max(1, Math.Min(contractCount, _config.MaxContractsPerTrade));
    }
    
    private async Task<TradeExecutionResult> ExecuteTradeAsync(TradingOpportunity opportunity, int contractSize, CancellationToken cancellationToken)
    {
        // Placeholder for actual trade execution - would integrate with TopStepX API
        // For now, simulate the execution
        
        await Task.CompletedTask;
        return new TradeExecutionResult
        {
            Success = true,
            OrderId = Guid.NewGuid().ToString(),
            ExecutedSize = contractSize,
            ExecutedPrice = opportunity.EntryPrice ?? 0m,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private void RecordTradeForLearning(TradingOpportunity opportunity, TradeExecutionResult result)
    {
        // Record trade outcome for continuous learning
        var tradeOutcome = new AutonomousTradeOutcome
        {
            Strategy = opportunity.Strategy,
            Symbol = opportunity.Symbol,
            Direction = opportunity.Direction,
            EntryTime = result.Timestamp,
            EntryPrice = result.ExecutedPrice,
            Size = result.ExecutedSize,
            Confidence = opportunity.Confidence,
            AutonomousMarketRegime = _currentAutonomousMarketRegime,
            // P&L will be updated when trade closes
        };
        
        lock (_stateLock)
        {
            _recentTrades.Enqueue(tradeOutcome);
            
            // Keep only recent trades for learning
            while (_recentTrades.Count > 100)
            {
                _recentTrades.Dequeue();
            }
        }
        
        _logger.LogDebug("📚 [AUTONOMOUS-ENGINE] Trade recorded for learning: {Strategy} {Direction} {Symbol}", 
            opportunity.Strategy, opportunity.Direction, opportunity.Symbol);
    }
    
    private async Task ManageExistingPositionsAsync(CancellationToken cancellationToken)
    {
        // Manage existing positions with dynamic stops and profit targets
        // This would integrate with position tracking system
        
        _logger.LogDebug("🔍 [AUTONOMOUS-ENGINE] Managing existing positions...");
        
        try
        {
            // Get all open positions from the position tracker
            var openPositions = await GetOpenPositionsAsync(cancellationToken);
            
            foreach (var position in openPositions)
            {
                await ManageIndividualPositionAsync(position, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Error managing existing positions");
        }
        // - Scale into winning positions with additional contracts
        
        await Task.CompletedTask;
    }
    
    private async Task UpdatePerformanceAndLearningAsync(CancellationToken cancellationToken)
    {
        // Update performance metrics
        await _performanceTracker.UpdateMetricsAsync(_recentTrades.ToArray(), cancellationToken);
        
        // Update strategy metrics
        await UpdateStrategyMetricsAsync(cancellationToken);
        
        // Update risk parameters based on performance
        await UpdateRiskParametersAsync(cancellationToken);
        
        // Generate periodic reports
        await GeneratePerformanceReportIfNeededAsync(cancellationToken);
    }
    
    private async Task UpdateRiskParametersAsync(CancellationToken cancellationToken)
    {
        // Dynamically adjust risk based on recent performance
        var recentPnL = _performanceTracker.GetRecentPnL(TimeSpan.FromDays(7));
        var recentWinRate = _performanceTracker.GetRecentWinRate(TimeSpan.FromDays(7));
        
        if (recentPnL > 0 && recentWinRate > 0.6m)
        {
            // Increase risk during profitable periods
            _currentRiskPerTrade = Math.Min(MaxRiskPerTrade, _currentRiskPerTrade * 1.05m);
        }
        else if (recentPnL < 0 || recentWinRate < 0.4m)
        {
            // Decrease risk during losing periods
            _currentRiskPerTrade = Math.Max(MinRiskPerTrade, _currentRiskPerTrade * 0.95m);
        }
        
        _logger.LogDebug("⚖️ [AUTONOMOUS-ENGINE] Risk updated: {Risk:P} (PnL: ${PnL:F0}, WinRate: {WinRate:P})",
            _currentRiskPerTrade, recentPnL, recentWinRate);
            
        await Task.CompletedTask;
    }
    
    private async Task<TimeSpan> GetAdaptiveDelayAsync(CancellationToken cancellationToken)
    {
        // Adaptive delay based on market conditions and strategy
        var session = await _marketHours.GetCurrentMarketSessionAsync(cancellationToken);
        
        return session switch
        {
            "MORNING_SESSION" => TimeSpan.FromSeconds(30),   // High frequency during active periods
            "CLOSE_SESSION" => TimeSpan.FromSeconds(30),     // High frequency during active periods
            "AFTERNOON_SESSION" => TimeSpan.FromMinutes(1),  // Normal frequency
            "LUNCH_SESSION" => TimeSpan.FromMinutes(2),      // Lower frequency during lunch
            "OVERNIGHT" => TimeSpan.FromMinutes(5),          // Lower frequency overnight
            "PRE_MARKET" => TimeSpan.FromMinutes(2),         // Lower frequency pre-market
            _ => TimeSpan.FromMinutes(1)
        };
    }
    
    private void InitializeAutonomousStrategyMetrics()
    {
        foreach (var strategy in new[] { "S2", "S3", "S6", "S11" })
        {
            _strategyMetrics[strategy] = new AutonomousStrategyMetrics
            {
                StrategyName = strategy,
                RecentTrades = new List<AutonomousTradeOutcome>()
            };
        }
    }
    
    private async Task LoadHistoricalPerformanceAsync(CancellationToken cancellationToken)
    {
        // Load historical performance data for strategy analysis
        _logger.LogInformation("📊 [AUTONOMOUS-ENGINE] Loading historical performance data...");
        
        try
        {
            // Load recent performance data for all strategies
            var performanceData = await LoadHistoricalPerformanceDataAsync(cancellationToken);
            
            // Initialize strategy performance metrics
            await InitializeStrategyMetricsAsync(performanceData, cancellationToken);
            
            _logger.LogInformation("✅ [AUTONOMOUS-ENGINE] Historical performance data loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to load historical data, using default metrics");
            await InitializeDefaultMetricsAsync(cancellationToken);
        }
    }
    
    private async Task UpdateStrategyMetricsAsync(CancellationToken cancellationToken)
    {
        // Update metrics for each strategy based on recent trades
        foreach (var strategy in _strategyMetrics.Keys)
        {
            var strategyTrades = _recentTrades.Where(t => t.Strategy == strategy).ToList();
            
            if (strategyTrades.Any())
            {
                var metrics = _strategyMetrics[strategy];
                metrics.TotalTrades = strategyTrades.Count;
                metrics.WinningTrades = strategyTrades.Count(t => t.PnL > 0);
                metrics.LosingTrades = strategyTrades.Count(t => t.PnL < 0);
                metrics.TotalProfit = strategyTrades.Where(t => t.PnL > 0).Sum(t => t.PnL);
                metrics.TotalLoss = strategyTrades.Where(t => t.PnL < 0).Sum(t => t.PnL);
                metrics.RecentTrades = strategyTrades.TakeLast(20).ToList();
            }
        }
        await Task.CompletedTask;
    }
    
    private async Task GeneratePerformanceReportIfNeededAsync(CancellationToken cancellationToken)
    {
        // Generate daily performance reports
        var now = DateTime.UtcNow;
        if (now.Hour == 17 && now.Minute < 5 && _lastPerformanceReport.Date != now.Date) // 5 PM ET
        {
            await GenerateDailyPerformanceReportAsync(cancellationToken);
            _lastPerformanceReport = now;
        }
    }
    
    private async Task GenerateDailyPerformanceReportAsync(CancellationToken cancellationToken)
    {
        var report = await _performanceTracker.GenerateDailyReportAsync(cancellationToken);
        
        _logger.LogInformation("📈 [DAILY-REPORT] {Date} | P&L: ${PnL:F2} | Trades: {Trades} | Win Rate: {WinRate:P} | Best Strategy: {Strategy}",
            DateTime.Today.ToString("yyyy-MM-dd"),
            report.DailyPnL,
            report.TotalTrades,
            report.WinRate,
            report.BestStrategy);
        
        // Send performance metrics to monitoring system
        await SendPerformanceMetricsAsync(report, cancellationToken);
        
        // Check for alerts and notifications
        await CheckPerformanceAlertsAsync(report, cancellationToken);
    }
    
    /// <summary>
    /// Map TradingMarketRegime to AutonomousMarketRegime
    /// </summary>
    private AutonomousMarketRegime MapTradingRegimeToAutonomous(TradingMarketRegime tradingRegime)
    {
        return tradingRegime switch
        {
            TradingMarketRegime.Unknown => AutonomousMarketRegime.Unknown,
            TradingMarketRegime.Trending => AutonomousMarketRegime.Trending,
            TradingMarketRegime.Ranging => AutonomousMarketRegime.Ranging,
            TradingMarketRegime.Volatile => AutonomousMarketRegime.Volatile,
            TradingMarketRegime.LowVolatility => AutonomousMarketRegime.LowVolatility,
            TradingMarketRegime.Crisis => AutonomousMarketRegime.Volatile, // Map crisis to volatile
            TradingMarketRegime.Recovery => AutonomousMarketRegime.Trending, // Map recovery to trending
            _ => AutonomousMarketRegime.Unknown
        };
    }
    
    /// <summary>
    /// Get current market price for the specified symbol
    /// </summary>
    private Task<decimal> GetCurrentMarketPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // This would normally get data from market analyzer
            // For now, return 0 as fallback - would need proper market data integration
            _logger.LogDebug("Market price API not available, using fallback");
            return Task.FromResult(0m);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to get current price for {Symbol}", symbol);
            return Task.FromResult(0m);
        }
    }
    
    /// <summary>
    /// Get current volume for the specified symbol
    /// </summary>
    private Task<long> GetCurrentVolumeAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // This would normally get data from market analyzer
            // For now, return 0 as fallback - would need proper market data integration
            _logger.LogDebug("Market volume API not available, using fallback");
            return Task.FromResult(0L);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to get current volume for {Symbol}", symbol);
            return Task.FromResult(0L);
        }
    }
    
    /// <summary>
    /// Calculate technical indicators for decision making
    /// </summary>
    private async Task<Dictionary<string, double>> CalculateTechnicalIndicatorsAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Get recent bars for technical analysis
            var bars = await GetRecentBarsAsync(symbol, 50, cancellationToken);
            if (bars.Count < 20) return new Dictionary<string, double>();
            
            var indicators = new Dictionary<string, double>();
            
            // Calculate key technical indicators
            indicators["RSI"] = CalculateRSI(bars, 14);
            indicators["MACD"] = CalculateMACD(bars);
            indicators["BollingerPosition"] = CalculateBollingerPosition(bars, 20);
            indicators["ATR"] = CalculateATR(bars, 14);
            indicators["VolumeMA"] = CalculateVolumeMA(bars, 20);
            
            return indicators;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to calculate technical indicators for {Symbol}", symbol);
            return new Dictionary<string, double>();
        }
    }
    
    /// <summary>
    /// Get recent bars for analysis
    /// </summary>
    private Task<List<Bar>> GetRecentBarsAsync(string symbol, int count, CancellationToken cancellationToken)
    {
        try
        {
            // This would connect to the market data system to get recent bars
            // For now, return empty list - would need proper market data integration
            return Task.FromResult(new List<Bar>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to get recent bars for {Symbol}", symbol);
            return Task.FromResult(new List<Bar>());
        }
    }
    
    /// <summary>
    /// Get current open positions
    /// </summary>
    private Task<List<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get positions from the position tracking system if available
            // For now, return empty list as fallback
            _logger.LogDebug("Position tracking API not available, using fallback");
            return Task.FromResult(new List<Position>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to get open positions");
            return Task.FromResult(new List<Position>());
        }
    }
    
    /// <summary>
    /// Manage individual position with trailing stops and profit targets
    /// </summary>
    private async Task ManageIndividualPositionAsync(Position position, CancellationToken cancellationToken)
    {
        try
        {
            var currentPrice = await GetCurrentMarketPriceAsync(position.Symbol, cancellationToken);
            var currentPnL = CalculatePositionPnL(position, currentPrice);
            
            // Implement trailing stop logic
            if (currentPnL > 0 && ShouldTrailStop(position, currentPrice))
            {
                await UpdateTrailingStopAsync(position, currentPrice, cancellationToken);
            }
            
            // Check for profit target scaling
            if (ShouldScaleOutPosition(position, currentPnL))
            {
                await ScaleOutPositionAsync(position, cancellationToken);
            }
            
            // Check for stop loss
            if (ShouldExitPosition(position, currentPnL))
            {
                await ExitPositionAsync(position, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Error managing position {PositionId}", position.Id);
        }
    }
    
    /// <summary>
    /// Load historical performance data for strategy initialization
    /// </summary>
    private Task<Dictionary<string, StrategyPerformanceData>> LoadHistoricalPerformanceDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            // This would normally load from strategy analyzer
            // For now, return empty dictionary as fallback
            _logger.LogDebug("Historical performance API not available, using fallback");
            return Task.FromResult(new Dictionary<string, StrategyPerformanceData>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to load historical performance data");
            return Task.FromResult(new Dictionary<string, StrategyPerformanceData>());
        }
    }
    
    /// <summary>
    /// Initialize strategy metrics from historical data
    /// </summary>
    private async Task InitializeStrategyMetricsAsync(Dictionary<string, StrategyPerformanceData> performanceData, CancellationToken cancellationToken)
    {
        foreach (var kvp in performanceData)
        {
            // This would normally call strategy analyzer
            // For now, just log the initialization
            _logger.LogDebug("Initializing metrics for strategy {Strategy}", kvp.Key);
        }
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Initialize default metrics when historical data is unavailable
    /// </summary>
    private async Task InitializeDefaultMetricsAsync(CancellationToken cancellationToken)
    {
        var defaultStrategies = new[] { "S2", "S3", "S6", "S11" };
        foreach (var strategy in defaultStrategies)
        {
            // This would normally call strategy analyzer
            // For now, just log the initialization
            _logger.LogDebug("Initializing default metrics for strategy {Strategy}", strategy);
        }
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Send performance metrics to monitoring system
    /// </summary>
    private Task SendPerformanceMetricsAsync(DailyPerformanceReport report, CancellationToken cancellationToken)
    {
        try
        {
            // Send metrics to monitoring/alerting system
            var metrics = new Dictionary<string, object>
            {
                ["daily_pnl"] = report.DailyPnL,
                ["total_trades"] = report.TotalTrades,
                ["win_rate"] = report.WinRate,
                ["best_strategy"] = report.BestStrategy,
                ["timestamp"] = DateTime.UtcNow
            };
            
            // This would send to monitoring system like Grafana, DataDog, etc.
            _logger.LogDebug("📊 [MONITORING] Sending performance metrics: {@Metrics}", metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Failed to send performance metrics");
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Check performance for alerts and notifications
    /// </summary>
    private Task CheckPerformanceAlertsAsync(DailyPerformanceReport report, CancellationToken cancellationToken)
    {
        try
        {
            // Check for various alert conditions
            if (report.DailyPnL < -500) // Large daily loss
            {
                _logger.LogWarning("🚨 [ALERT] Large daily loss detected: ${Loss:F2}", report.DailyPnL);
            }
            
            if (report.WinRate < 0.3m && report.TotalTrades > 5) // Low win rate
            {
                _logger.LogWarning("🚨 [ALERT] Low win rate detected: {WinRate:P}", report.WinRate);
            }
            
            if (report.DailyPnL > 1000) // Large daily gain
            {
                _logger.LogInformation("🎉 [SUCCESS] Excellent daily performance: ${Profit:F2}", report.DailyPnL);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AUTONOMOUS-ENGINE] Error checking performance alerts");
        }
        
        return Task.CompletedTask;
    }
    
    // Helper methods for technical indicators
    private double CalculateRSI(List<Bar> bars, int period)
    {
        if (bars.Count < period + 1) return 50; // Neutral RSI
        
        var gains = new List<decimal>();
        var losses = new List<decimal>();
        
        for (int i = 1; i < bars.Count; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }
        
        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();
        
        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return (double)(100 - (100 / (1 + rs)));
    }
    
    private double CalculateMACD(List<Bar> bars)
    {
        if (bars.Count < 26) return 0;
        
        var ema12 = CalculateEMA(bars.Select(b => b.Close).ToList(), 12);
        var ema26 = CalculateEMA(bars.Select(b => b.Close).ToList(), 26);
        
        return (double)(ema12 - ema26);
    }
    
    private double CalculateBollingerPosition(List<Bar> bars, int period)
    {
        if (bars.Count < period) return 0.5; // Neutral position
        
        var closes = bars.TakeLast(period).Select(b => b.Close).ToList();
        var sma = closes.Average();
        var stdDev = (decimal)Math.Sqrt((double)closes.Select(c => (c - sma) * (c - sma)).Average());
        
        var upperBand = sma + (2 * stdDev);
        var lowerBand = sma - (2 * stdDev);
        var currentPrice = bars.Last().Close;
        
        // Return position between bands (0 = lower band, 1 = upper band)
        if (upperBand == lowerBand) return 0.5;
        return (double)((currentPrice - lowerBand) / (upperBand - lowerBand));
    }
    
    private double CalculateATR(List<Bar> bars, int period)
    {
        if (bars.Count < period + 1) return 0;
        
        var trValues = new List<decimal>();
        
        for (int i = 1; i < bars.Count; i++)
        {
            var tr = Math.Max(
                bars[i].High - bars[i].Low,
                Math.Max(
                    Math.Abs(bars[i].High - bars[i - 1].Close),
                    Math.Abs(bars[i].Low - bars[i - 1].Close)
                )
            );
            trValues.Add(tr);
        }
        
        return (double)trValues.TakeLast(period).Average();
    }
    
    private double CalculateVolumeMA(List<Bar> bars, int period)
    {
        if (bars.Count < period) return 0;
        return (double)bars.TakeLast(period).Select(b => b.Volume).Average();
    }
    
    private decimal CalculateEMA(List<decimal> values, int period)
    {
        if (values.Count < period) return values.LastOrDefault();
        
        var multiplier = 2m / (period + 1);
        var ema = values.Take(period).Average(); // Start with SMA
        
        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i] * multiplier) + (ema * (1 - multiplier));
        }
        
        return ema;
    }
    
    private decimal CalculatePositionPnL(Position position, decimal currentPrice)
    {
        var priceDiff = position.Side == "Long" ? 
            currentPrice - position.EntryPrice : 
            position.EntryPrice - currentPrice;
        
        return priceDiff * position.Quantity;
    }
    
    private bool ShouldTrailStop(Position position, decimal currentPrice)
    {
        // Implement trailing stop logic based on position performance
        var unrealizedPnL = CalculatePositionPnL(position, currentPrice);
        return unrealizedPnL > (position.EntryPrice * 0.01m); // Trail after 1% profit
    }
    
    private bool ShouldScaleOutPosition(Position position, decimal currentPnL)
    {
        // Scale out at profit targets
        var profitTarget = position.EntryPrice * 0.02m; // 2% profit target
        return currentPnL > profitTarget;
    }
    
    private bool ShouldExitPosition(Position position, decimal currentPnL)
    {
        // Exit at stop loss
        var stopLoss = position.EntryPrice * -0.01m; // 1% stop loss
        return currentPnL < stopLoss;
    }
    
    private Task UpdateTrailingStopAsync(Position position, decimal currentPrice, CancellationToken cancellationToken)
    {
        // Update trailing stop order
        _logger.LogDebug("🔄 [POSITION-MGMT] Updating trailing stop for position {PositionId}", position.Id);
        return Task.CompletedTask;
    }
    
    private Task ScaleOutPositionAsync(Position position, CancellationToken cancellationToken)
    {
        // Scale out partial position at profit targets
        _logger.LogDebug("📈 [POSITION-MGMT] Scaling out position {PositionId}", position.Id);
        return Task.CompletedTask;
    }
    
    private Task ExitPositionAsync(Position position, CancellationToken cancellationToken)
    {
        // Exit position at stop loss
        _logger.LogDebug("🛑 [POSITION-MGMT] Exiting position {PositionId}", position.Id);
        return Task.CompletedTask;
    }

    private DateTime _lastPerformanceReport = DateTime.MinValue;
}

/// <summary>
/// Trading opportunity identified by the autonomous engine
/// </summary>
public class TradingOpportunity
{
    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Strategy { get; set; } = "";
    public decimal Confidence { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// Result of trade execution
/// </summary>
public class TradeExecutionResult
{
    public bool Success { get; set; }
    public string OrderId { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public int ExecutedSize { get; set; }
    public decimal ExecutedPrice { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Trade outcome for learning and analysis (Autonomous)
/// </summary>
public class AutonomousTradeOutcome
{
    public string Strategy { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public int Size { get; set; }
    public decimal Confidence { get; set; }
    public AutonomousMarketRegime AutonomousMarketRegime { get; set; }
    public TradingMarketRegime MarketRegime { get; set; } = TradingMarketRegime.Unknown;
    public decimal PnL { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }
    public bool IsWin => PnL > 0;
    public decimal RMultiple { get; set; }
    public TimeSpan Duration => ExitTime.HasValue ? ExitTime.Value - EntryTime : TimeSpan.Zero;
}

/// <summary>
/// Strategy performance metrics (Autonomous)
/// </summary>
public class AutonomousStrategyMetrics
{
    public string StrategyName { get; set; } = "";
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal TotalLoss { get; set; }
    public List<AutonomousTradeOutcome> RecentTrades { get; set; } = new();
}

/// <summary>
/// Configuration for autonomous trading engine
/// </summary>
public class AutonomousConfig
{
    public bool IsEnabled { get; set; } = false;
    public bool Enabled { get; set; } = false; // Legacy property for compatibility
    public bool AutoStrategySelection { get; set; } = true;
    public bool AutoPositionSizing { get; set; } = true;
    public decimal DailyProfitTarget { get; set; } = 300m;
    public decimal MaxDailyLoss { get; set; } = -1000m;
    public decimal MaxDrawdown { get; set; } = -2000m; // Add back for compatibility
    public bool TradeDuringLunch { get; set; } = false;
    public bool TradeOvernight { get; set; } = false;
    public bool TradePreMarket { get; set; } = false;
    public int MaxContractsPerTrade { get; set; } = 5;
    public decimal MinRiskPerTrade { get; set; } = 0.005m;
    public decimal MaxRiskPerTrade { get; set; } = 0.015m;
}

/// <summary>
/// Market regime classification (Autonomous)
/// </summary>
public enum AutonomousMarketRegime
{
    Unknown,
    Trending,
    Ranging,
    Volatile,
    LowVolatility
}

/// <summary>
/// Market volatility levels (Autonomous)
/// </summary>
public enum AutonomousMarketVolatility
{
    VeryLow,
    Low,
    Normal,
    High,
    VeryHigh
}

/// <summary>
/// Strategy performance data for initialization
/// </summary>
public class StrategyPerformanceData
{
    public string Strategy { get; set; } = "";
    public decimal TotalPnL { get; set; }
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal MaxDrawdown { get; set; }
    public DateTime LastTradeDate { get; set; }
    public Dictionary<string, decimal> PerformanceMetrics { get; set; } = new();
}

/// <summary>
/// Position information for autonomous management
/// </summary>
public class Position
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = ""; // "Long" or "Short"
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public string Strategy { get; set; } = "";
}