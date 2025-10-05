using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Monitoring;

/// <summary>
/// Monitors live performance of current parameters and triggers automatic rollback if degradation detected.
/// Runs hourly during market hours to track rolling performance metrics.
/// </summary>
public class ParameterPerformanceMonitor : BackgroundService
{
    private readonly ILogger<ParameterPerformanceMonitor> _logger;
    private readonly string _artifactsPath;
    
    // Configuration
    private const double DegradationThreshold = 0.20; // 20% drop triggers rollback
    private const int ConsecutiveDaysThreshold = 3;   // Must see degradation for 3 days
    private const int RollingDays = 3;                // Calculate metrics over last 3 days
    private const int CheckIntervalMinutes = 60;      // Run every hour
    
    // Performance tracking
    private readonly ConcurrentDictionary<string, StrategyPerformance> _performanceTracking;
    private readonly ConcurrentDictionary<string, int> _degradationDayCount;
    private readonly ConcurrentDictionary<string, List<TradeMetric>> _recentTrades;
    
    public ParameterPerformanceMonitor(ILogger<ParameterPerformanceMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Get artifacts path from environment or default
        _artifactsPath = Environment.GetEnvironmentVariable("ARTIFACTS_PATH") 
                         ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "artifacts");
        
        _performanceTracking = new ConcurrentDictionary<string, StrategyPerformance>();
        _degradationDayCount = new ConcurrentDictionary<string, int>();
        _recentTrades = new ConcurrentDictionary<string, List<TradeMetric>>();
        
        _logger.LogInformation("[PARAM-MONITOR] Initialized with artifacts path: {Path}", _artifactsPath);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PARAM-MONITOR] Starting parameter performance monitoring");
        
        // Initial delay to let system start up
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Only run during market hours
                if (IsMarketOpen())
                {
                    await CheckAllStrategiesPerformanceAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("[PARAM-MONITOR] Market closed, skipping check");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PARAM-MONITOR] Error in monitoring loop");
            }
            
            // Wait for next check interval
            await Task.Delay(TimeSpan.FromMinutes(CheckIntervalMinutes), stoppingToken).ConfigureAwait(false);
        }
        
        _logger.LogInformation("[PARAM-MONITOR] Stopped parameter performance monitoring");
    }
    
    private async Task CheckAllStrategiesPerformanceAsync(CancellationToken cancellationToken)
    {
        var strategies = new[] { "S2", "S3", "S6", "S11" };
        
        foreach (var strategy in strategies)
        {
            try
            {
                await CheckStrategyPerformanceAsync(strategy, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PARAM-MONITOR] Error checking {Strategy} performance", strategy);
            }
        }
    }
    
    private async Task CheckStrategyPerformanceAsync(string strategy, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[PARAM-MONITOR] Checking {Strategy} performance", strategy);
        
        // Load baseline Sharpe from parameter file
        var baselineSharpe = await LoadBaselineSharpeAsync(strategy, cancellationToken).ConfigureAwait(false);
        if (baselineSharpe <= 0)
        {
            _logger.LogWarning("[PARAM-MONITOR] No baseline Sharpe found for {Strategy}, skipping", strategy);
            return;
        }
        
        // Calculate current rolling Sharpe ratio
        var currentSharpe = CalculateRollingSharpe(strategy, RollingDays);
        
        _logger.LogInformation(
            "[PARAM-MONITOR] {Strategy} - Baseline Sharpe: {Baseline:F3}, Current Sharpe: {Current:F3}",
            strategy, baselineSharpe, currentSharpe
        );
        
        // Check for degradation
        var degradationPct = (baselineSharpe - currentSharpe) / baselineSharpe;
        
        if (degradationPct > DegradationThreshold)
        {
            // Increment consecutive degradation counter
            var count = _degradationDayCount.AddOrUpdate(strategy, 1, (_, old) => old + 1);
            
            _logger.LogWarning(
                "[PARAM-MONITOR] {Strategy} degraded by {Degradation:P1} (day {Count}/{Threshold})",
                strategy, degradationPct, count, ConsecutiveDaysThreshold
            );
            
            // Trigger rollback if threshold reached
            if (count >= ConsecutiveDaysThreshold)
            {
                _logger.LogError(
                    "[PARAM-MONITOR] ROLLBACK TRIGGERED for {Strategy} after {Days} consecutive days of degradation",
                    strategy, count
                );
                
                await TriggerRollbackAsync(strategy, baselineSharpe, currentSharpe, cancellationToken).ConfigureAwait(false);
                
                // Reset counter after rollback
                _degradationDayCount.TryRemove(strategy, out _);
            }
        }
        else
        {
            // Performance acceptable, reset counter
            if (_degradationDayCount.TryRemove(strategy, out var oldCount) && oldCount > 0)
            {
                _logger.LogInformation(
                    "[PARAM-MONITOR] {Strategy} performance recovered, reset degradation counter (was {Count})",
                    strategy, oldCount
                );
            }
        }
    }
    
    private async Task<double> LoadBaselineSharpeAsync(string strategy, CancellationToken cancellationToken)
    {
        var paramFile = Path.Combine(_artifactsPath, "current", "parameters", $"{strategy}_parameters.json");
        
        if (!File.Exists(paramFile))
        {
            _logger.LogWarning("[PARAM-MONITOR] Parameter file not found: {File}", paramFile);
            return 0;
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(paramFile, cancellationToken).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("optimized_sharpe", out var sharpeElement))
            {
                return sharpeElement.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("baseline_sharpe", out var baselineElement))
            {
                return baselineElement.GetDouble();
            }
            
            _logger.LogWarning("[PARAM-MONITOR] No Sharpe ratio found in parameter file: {File}", paramFile);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PARAM-MONITOR] Error loading baseline Sharpe from {File}", paramFile);
            return 0;
        }
    }
    
    private double CalculateRollingSharpe(string strategy, int days)
    {
        // Get recent trades for this strategy
        if (!_recentTrades.TryGetValue(strategy, out var trades) || trades.Count == 0)
        {
            _logger.LogDebug("[PARAM-MONITOR] No trade data for {Strategy}, cannot calculate Sharpe", strategy);
            return 0;
        }
        
        // Filter trades from last N days
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var recentTradesList = trades.Where(t => t.Timestamp >= cutoffDate).ToList();
        
        if (recentTradesList.Count < 2)
        {
            _logger.LogDebug("[PARAM-MONITOR] Insufficient trades for {Strategy} (need 2+, have {Count})", 
                strategy, recentTradesList.Count);
            return 0;
        }
        
        // Calculate returns
        var returns = recentTradesList.Select(t => t.ReturnPct).ToArray();
        
        // Sharpe ratio = mean / std * sqrt(252) for annualization
        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / (returns.Length - 1);
        var std = Math.Sqrt(variance);
        
        if (std == 0)
        {
            return 0;
        }
        
        var sharpe = mean / std * Math.Sqrt(252); // Annualized
        
        return sharpe;
    }
    
    private async Task TriggerRollbackAsync(
        string strategy,
        double baselineSharpe,
        double currentSharpe,
        CancellationToken cancellationToken)
    {
        _logger.LogCritical(
            "[PARAM-MONITOR] ========================================" + Environment.NewLine +
            "AUTOMATIC ROLLBACK TRIGGERED" + Environment.NewLine +
            "========================================" + Environment.NewLine +
            "Strategy: {Strategy}" + Environment.NewLine +
            "Baseline Sharpe: {Baseline:F3}" + Environment.NewLine +
            "Current Sharpe: {Current:F3}" + Environment.NewLine +
            "Degradation: {Degradation:P1}" + Environment.NewLine +
            "========================================",
            strategy, baselineSharpe, currentSharpe, (baselineSharpe - currentSharpe) / baselineSharpe
        );
        
        try
        {
            // Copy previous parameters to current
            var previousFile = Path.Combine(_artifactsPath, "previous", "parameters", $"{strategy}_parameters.json");
            var currentFile = Path.Combine(_artifactsPath, "current", "parameters", $"{strategy}_parameters.json");
            
            if (!File.Exists(previousFile))
            {
                _logger.LogError("[PARAM-MONITOR] Previous parameter file not found: {File}", previousFile);
                return;
            }
            
            // Backup current (failed) parameters before rollback
            var failedFile = Path.Combine(
                _artifactsPath, 
                "rollback", 
                "parameters", 
                $"{strategy}_parameters_failed_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
            );
            
            var rollbackDir = Path.GetDirectoryName(failedFile);
            if (rollbackDir != null)
            {
                Directory.CreateDirectory(rollbackDir);
            }
            
            if (File.Exists(currentFile))
            {
                File.Copy(currentFile, failedFile, overwrite: true);
                _logger.LogInformation("[PARAM-MONITOR] Backed up failed parameters to: {File}", failedFile);
            }
            
            // Perform rollback
            File.Copy(previousFile, currentFile, overwrite: true);
            _logger.LogInformation("[PARAM-MONITOR] Rolled back {Strategy} parameters from previous version", strategy);
            
            // Set environment variable to indicate rollback is active
            Environment.SetEnvironmentVariable("PARAMETER_ROLLBACK_ACTIVE", "true");
            Environment.SetEnvironmentVariable($"PARAMETER_ROLLBACK_{strategy}", DateTime.UtcNow.ToString("O"));
            
            // Log rollback event
            await LogRollbackEventAsync(strategy, baselineSharpe, currentSharpe, cancellationToken).ConfigureAwait(false);
            
            _logger.LogCritical(
                "[PARAM-MONITOR] ROLLBACK COMPLETE for {Strategy}. Previous parameters restored.",
                strategy
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PARAM-MONITOR] ERROR during rollback for {Strategy}", strategy);
        }
    }
    
    private async Task LogRollbackEventAsync(
        string strategy,
        double baselineSharpe,
        double currentSharpe,
        CancellationToken cancellationToken)
    {
        var logDir = Path.Combine(_artifactsPath, "rollback", "events");
        Directory.CreateDirectory(logDir);
        
        var logFile = Path.Combine(logDir, $"rollback_{strategy}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        
        var rollbackEvent = new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            strategy,
            baseline_sharpe = baselineSharpe,
            current_sharpe = currentSharpe,
            degradation_pct = (baselineSharpe - currentSharpe) / baselineSharpe,
            consecutive_days = ConsecutiveDaysThreshold,
            action = "AUTOMATIC_ROLLBACK"
        };
        
        var json = JsonSerializer.Serialize(rollbackEvent, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(logFile, json, cancellationToken).ConfigureAwait(false);
        
        _logger.LogInformation("[PARAM-MONITOR] Rollback event logged to: {File}", logFile);
    }
    
    /// <summary>
    /// Track a trade for performance monitoring.
    /// This should be called by the trading engine after each trade completes.
    /// </summary>
    public void TrackTrade(string strategy, double returnPct, DateTime timestamp)
    {
        var metric = new TradeMetric
        {
            Timestamp = timestamp,
            ReturnPct = returnPct
        };
        
        _recentTrades.AddOrUpdate(
            strategy,
            _ => new List<TradeMetric> { metric },
            (_, existing) =>
            {
                existing.Add(metric);
                
                // Keep only last 30 days of trades
                var cutoff = DateTime.UtcNow.AddDays(-30);
                return existing.Where(t => t.Timestamp >= cutoff).ToList();
            }
        );
        
        _logger.LogDebug("[PARAM-MONITOR] Tracked trade for {Strategy}: {Return:P2}", strategy, returnPct);
    }
    
    private bool IsMarketOpen()
    {
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")
        );
        
        // Market closed on weekends
        if (nowEt.DayOfWeek == DayOfWeek.Saturday || nowEt.DayOfWeek == DayOfWeek.Sunday)
        {
            return false;
        }
        
        var time = nowEt.TimeOfDay;
        
        // Market hours: 9:30 AM - 4:00 PM ET
        var marketOpen = new TimeSpan(9, 30, 0);
        var marketClose = new TimeSpan(16, 0, 0);
        
        return time >= marketOpen && time < marketClose;
    }
    
    private class StrategyPerformance
    {
        public double BaselineSharpe { get; set; }
        public double CurrentSharpe { get; set; }
        public DateTime LastCheck { get; set; }
    }
    
    private class TradeMetric
    {
        public DateTime Timestamp { get; set; }
        public double ReturnPct { get; set; }
    }
}
