using Microsoft.Extensions.Logging;
using BotCore.Brain;
using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services;

/// <summary>
/// Feature 3: AI-generated daily and weekly performance summaries
/// </summary>
public class BotPerformanceReporter
{
    private readonly ILogger<BotPerformanceReporter> _logger;
    private readonly OllamaClient? _ollamaClient;

    // Track summary history
    private DateTime _lastDailySummary = DateTime.MinValue;
    private DateTime _lastWeeklySummary = DateTime.MinValue;
    
    // Performance tracking
    private readonly List<TradeRecord> _todayTrades = new();
    private readonly List<TradeRecord> _weekTrades = new();
    
    // Summary timing constants
    private const int DaysInWeek = 7; // Days in a week for weekly summary threshold
    private const double DefaultDailySummaryHour = 17.0; // 5:00 PM EST - futures market close
    private const double DefaultWeeklySummaryHour = 18.0; // 6:00 PM EST - end of futures week on Friday
    
    public BotPerformanceReporter(
        ILogger<BotPerformanceReporter> logger,
        OllamaClient? ollamaClient = null)
    {
        _logger = logger;
        _ollamaClient = ollamaClient;
    }

    /// <summary>
    /// Record a trade for performance tracking
    /// </summary>
    public void RecordTrade(string symbol, string strategy, decimal pnl, bool wasCorrect, DateTime timestamp)
    {
        var record = new TradeRecord
        {
            Symbol = symbol,
            Strategy = strategy,
            PnL = pnl,
            WasCorrect = wasCorrect,
            Timestamp = timestamp
        };
        
        if (timestamp.Date == DateTime.Today)
        {
            _todayTrades.Add(record);
        }
        
        // Keep only current week
        var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        if (timestamp >= weekStart)
        {
            _weekTrades.Add(record);
        }
    }

    /// <summary>
    /// Generate daily performance summary at futures market close (5:00 PM EST)
    /// </summary>
    public async Task<string> GenerateDailySummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_ollamaClient == null)
            return string.Empty;

        try
        {
            // Clean up old trades
            _todayTrades.RemoveAll(t => t.Timestamp.Date != DateTime.Today);
            
            if (_todayTrades.Count == 0)
            {
                _logger.LogInformation("📊 [BOT-DAILY-SUMMARY] No trades today");
                return string.Empty;
            }

            // Calculate performance metrics
            var totalTrades = _todayTrades.Count;
            var winningTrades = _todayTrades.Count(t => t.WasCorrect);
            var winRate = (decimal)winningTrades / totalTrades;
            var totalPnL = _todayTrades.Sum(t => t.PnL);
            var avgWin = _todayTrades.Exists(t => t.PnL > 0) 
                ? _todayTrades.Where(t => t.PnL > 0).Average(t => t.PnL) : 0;
            var avgLoss = _todayTrades.Exists(t => t.PnL < 0) 
                ? _todayTrades.Where(t => t.PnL < 0).Average(t => t.PnL) : 0;

            // Strategy breakdown
            var strategyBreakdown = _todayTrades
                .GroupBy(t => t.Strategy)
                .Select(g => new
                {
                    Strategy = g.Key,
                    Count = g.Count(),
                    WinRate = (decimal)g.Count(t => t.WasCorrect) / g.Count(),
                    PnL = g.Sum(t => t.PnL)
                })
                .OrderByDescending(s => s.PnL)
                .ToList();

            var bestStrategy = strategyBreakdown.FirstOrDefault();
            var worstStrategy = strategyBreakdown.LastOrDefault();

            var strategyPerformance = string.Join(", ", strategyBreakdown.Select(s => 
                $"{s.Strategy}: {s.Count} trades, {s.WinRate:P0} win rate, ${s.PnL:F2}"));

            var prompt = $@"I am a trading bot. Here's my performance TODAY:

Total Trades: {totalTrades}
Win Rate: {winRate:P0} ({winningTrades} wins, {totalTrades - winningTrades} losses)
Total P&L: ${totalPnL:F2}
Average Win: ${avgWin:F2}
Average Loss: ${avgLoss:F2}

Strategy Performance:
{strategyPerformance}

Best Strategy: {bestStrategy?.Strategy} (${bestStrategy?.PnL:F2})
Worst Strategy: {worstStrategy?.Strategy} (${worstStrategy?.PnL:F2})

Market Close Analysis: What did I do well today? What should I improve tomorrow? Give me 2-3 actionable insights. Speak as ME (the bot).";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            
            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogInformation("📊 [BOT-DAILY-SUMMARY] {Summary}", response);
                _lastDailySummary = DateTime.Now;
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [BOT-DAILY-SUMMARY] Error generating daily summary");
            return string.Empty;
        }
    }

    /// <summary>
    /// Generate weekly performance summary on Friday at end of futures week (6:00 PM EST)
    /// </summary>
    public async Task<string> GenerateWeeklySummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_ollamaClient == null)
            return string.Empty;

        try
        {
            // Clean up old trades (keep only current week)
            var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            _weekTrades.RemoveAll(t => t.Timestamp < weekStart);
            
            if (_weekTrades.Count == 0)
            {
                _logger.LogInformation("📈 [BOT-WEEKLY-SUMMARY] No trades this week");
                return string.Empty;
            }

            // Calculate weekly metrics
            var totalTrades = _weekTrades.Count;
            var winningTrades = _weekTrades.Count(t => t.WasCorrect);
            var winRate = (decimal)winningTrades / totalTrades;
            var totalPnL = _weekTrades.Sum(t => t.PnL);
            
            // Calculate Sharpe ratio (simplified)
            var returns = _weekTrades.Select(t => (double)t.PnL).ToList();
            var avgReturn = returns.Average();
            var stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
            var sharpeRatio = stdDev > 0 ? avgReturn / stdDev : 0;

            // Calculate max drawdown
            var runningPnL = 0m;
            var peak = 0m;
            var maxDrawdown = 0m;
            foreach (var trade in _weekTrades.OrderBy(t => t.Timestamp))
            {
                runningPnL += trade.PnL;
                if (runningPnL > peak) peak = runningPnL;
                var drawdown = peak - runningPnL;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }

            // Strategy comparison
            var strategyComparison = _weekTrades
                .GroupBy(t => t.Strategy)
                .Select(g => new
                {
                    Strategy = g.Key,
                    Count = g.Count(),
                    WinRate = (decimal)g.Count(t => t.WasCorrect) / g.Count(),
                    PnL = g.Sum(t => t.PnL),
                    AvgPnL = g.Average(t => t.PnL)
                })
                .OrderByDescending(s => s.PnL)
                .ToList();

            var strategyBreakdown = string.Join("\n", strategyComparison.Select(s => 
                $"  {s.Strategy}: {s.Count} trades, {s.WinRate:P0} win rate, ${s.PnL:F2} total, ${s.AvgPnL:F2} avg"));

            // Daily breakdown
            var dailyPnL = _weekTrades
                .GroupBy(t => t.Timestamp.DayOfWeek)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}: ${g.Sum(t => t.PnL):F2}")
                .ToList();

            var prompt = $@"I am a trading bot. Here's my performance THIS WEEK:

Weekly Summary:
Total Trades: {totalTrades}
Win Rate: {winRate:P0} ({winningTrades} wins, {totalTrades - winningTrades} losses)
Total P&L: ${totalPnL:F2}
Sharpe Ratio: {sharpeRatio:F2}
Max Drawdown: ${maxDrawdown:F2}

Strategy Performance:
{strategyBreakdown}

Daily P&L:
{string.Join(", ", dailyPnL)}

Week-End Analysis: What patterns emerged this week? Which strategies excelled and why? What should I adjust for next week? Give me 3-4 strategic insights. Speak as ME (the bot).";

            var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            
            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogInformation("📈 [BOT-WEEKLY-SUMMARY] {Summary}", response);
                _lastWeeklySummary = DateTime.Now;
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [BOT-WEEKLY-SUMMARY] Error generating weekly summary");
            return string.Empty;
        }
    }

    /// <summary>
    /// Check if it's time to generate daily summary (5:00 PM EST - futures market close)
    /// </summary>
    public bool ShouldGenerateDailySummary()
    {
        var now = DateTime.Now;
        var summaryTime = GetDailySummaryTime();
        
        // Check if we've passed summary time today and haven't generated one yet
        return now.TimeOfDay >= summaryTime.TimeOfDay 
            && _lastDailySummary.Date < DateTime.Today;
    }

    /// <summary>
    /// Check if it's time to generate weekly summary (Friday 6:00 PM EST - end of futures week)
    /// </summary>
    public bool ShouldGenerateWeeklySummary()
    {
        var now = DateTime.Now;
        var weeklySummaryTime = GetWeeklySummaryTime();
        
        // Check if it's Friday, past weekly summary time, and we haven't generated one this week
        return now.DayOfWeek == DayOfWeek.Friday 
            && now.TimeOfDay >= weeklySummaryTime.TimeOfDay 
            && (now - _lastWeeklySummary).TotalDays >= DaysInWeek;
    }

    private static DateTime GetDailySummaryTime()
    {
        // Default to 5:00 PM EST (17:00) - futures market close
        var timeStr = Environment.GetEnvironmentVariable("DAILY_SUMMARY_TIME") ?? "17:00";
        if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var time))
        {
            return DateTime.Today.Add(time);
        }
        return DateTime.Today.AddHours(DefaultDailySummaryHour); // 5:00 PM EST
    }

    private static DateTime GetWeeklySummaryTime()
    {
        // Default to 6:00 PM EST (18:00) - end of futures week on Friday
        var timeStr = Environment.GetEnvironmentVariable("WEEKLY_SUMMARY_TIME") ?? "18:00";
        if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var time))
        {
            return DateTime.Today.Add(time);
        }
        return DateTime.Today.AddHours(DefaultWeeklySummaryHour); // 6:00 PM EST
    }

    private sealed class TradeRecord
    {
        public string Symbol { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public decimal PnL { get; set; }
        public bool WasCorrect { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
