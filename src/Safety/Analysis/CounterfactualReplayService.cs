using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Abstractions;
using TradingBot.Backtest;
using Trading.Safety.Journaling;

namespace TradingBot.Safety.Analysis
{
    /// <summary>
    /// Counterfactual replay service that nightly replays signals blocked by S7/Zone/Pattern gates
    /// Simulates entries to verify they would have lost money and generates audit reports
    /// </summary>
    public class CounterfactualReplayService : BackgroundService
    {
        private readonly ILogger<CounterfactualReplayService> _logger;
        private readonly ITradeJournal _tradeJournal;
        private readonly IExecutionSimulator _executionSimulator;
        private readonly CounterfactualReplayConfig _config;
        private readonly Timer _nightlyTimer;

        public CounterfactualReplayService(
            ILogger<CounterfactualReplayService> logger,
            ITradeJournal tradeJournal,
            IExecutionSimulator executionSimulator,
            IOptions<CounterfactualReplayConfig> config)
        {
            _logger = logger;
            _tradeJournal = tradeJournal;
            _executionSimulator = executionSimulator;
            _config = config.Value;
            
            // Schedule nightly execution at configured time
            var nextRun = GetNextRunTime();
            var delay = nextRun - DateTime.UtcNow;
            _nightlyTimer = new Timer(ExecuteNightlyReplay, null, delay, TimeSpan.FromHours(24));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[COUNTERFACTUAL] Counterfactual replay service started");
            
            // Keep service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Execute nightly counterfactual replay analysis
        /// </summary>
        private async void ExecuteNightlyReplay(object? state)
        {
            try
            {
                _logger.LogInformation("[COUNTERFACTUAL] Starting nightly counterfactual replay analysis");
                
                var analysisDate = DateTime.UtcNow.Date.AddDays(-1); // Previous trading day
                var blockedSignals = await LoadBlockedSignalsAsync(analysisDate).ConfigureAwait(false);
                
                if (!blockedSignals.Any())
                {
                    _logger.LogInformation("[COUNTERFACTUAL] No blocked signals found for {Date}", analysisDate);
                    return;
                }

                var replayResults = new List<CounterfactualResult>();
                
                foreach (var signal in blockedSignals)
                {
                    var result = await SimulateBlockedSignalAsync(signal).ConfigureAwait(false);
                    if (result != null)
                        replayResults.Add(result);
                }

                var auditReport = GenerateAuditReport(replayResults, analysisDate);
                await SaveAuditReportAsync(auditReport).ConfigureAwait(false);
                
                _logger.LogInformation("[COUNTERFACTUAL] Completed replay analysis: {TotalSignals} signals, {SavedLoss:C} saved loss, {MissedProfit:C} missed profit",
                    replayResults.Count, auditReport.TotalSavedLoss, auditReport.TotalMissedProfit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error during nightly counterfactual replay");
            }
        }

        /// <summary>
        /// Load blocked signals from gate logs for a specific date
        /// </summary>
        private async Task<List<BlockedSignal>> LoadBlockedSignalsAsync(DateTime date)
        {
            var blockedSignals = new List<BlockedSignal>();
            
            try
            {
                // Load from S7 gate logs
                var s7BlockedSignals = await LoadS7BlockedSignalsAsync(date).ConfigureAwait(false);
                blockedSignals.AddRange(s7BlockedSignals);
                
                // Load from Zone gate logs
                var zoneBlockedSignals = await LoadZoneBlockedSignalsAsync(date).ConfigureAwait(false);
                blockedSignals.AddRange(zoneBlockedSignals);
                
                // Load from Pattern gate logs
                var patternBlockedSignals = await LoadPatternBlockedSignalsAsync(date).ConfigureAwait(false);
                blockedSignals.AddRange(patternBlockedSignals);

                _logger.LogDebug("[COUNTERFACTUAL] Loaded {Count} blocked signals for {Date}", blockedSignals.Count, date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error loading blocked signals for {Date}", date);
            }

            return blockedSignals;
        }

        /// <summary>
        /// Load S7-blocked signals from gate logs
        /// </summary>
        private async Task<List<BlockedSignal>> LoadS7BlockedSignalsAsync(DateTime date)
        {
            var blockedSignals = new List<BlockedSignal>();
            var logPath = Path.Combine(_config.GateLogsPath, "s7", $"s7_blocks_{date:yyyyMMdd}.json");
            
            if (!File.Exists(logPath))
                return blockedSignals;

            try
            {
                var json = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
                var s7Blocks = JsonSerializer.Deserialize<List<S7BlockLog>>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (s7Blocks != null)
                {
                    blockedSignals.AddRange(s7Blocks.Select(block => new BlockedSignal
                    {
                        Timestamp = block.Timestamp,
                        Symbol = block.Symbol,
                        Strategy = block.Strategy,
                        Signal = block.Signal,
                        BlockReason = $"S7_{block.S7State}",
                        BlockType = GateType.S7,
                        ExpectedEntry = block.ExpectedEntry,
                        ExpectedExit = block.ExpectedExit,
                        RiskAmount = block.RiskAmount,
                        Metadata = block.Metadata
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error loading S7 blocked signals from {Path}", logPath);
            }

            return blockedSignals;
        }

        /// <summary>
        /// Load Zone-blocked signals from gate logs
        /// </summary>
        private async Task<List<BlockedSignal>> LoadZoneBlockedSignalsAsync(DateTime date)
        {
            var blockedSignals = new List<BlockedSignal>();
            var logPath = Path.Combine(_config.GateLogsPath, "zone", $"zone_blocks_{date:yyyyMMdd}.json");
            
            if (!File.Exists(logPath))
                return blockedSignals;

            try
            {
                var json = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
                var zoneBlocks = JsonSerializer.Deserialize<List<ZoneBlockLog>>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (zoneBlocks != null)
                {
                    blockedSignals.AddRange(zoneBlocks.Select(block => new BlockedSignal
                    {
                        Timestamp = block.Timestamp,
                        Symbol = block.Symbol,
                        Strategy = block.Strategy,
                        Signal = block.Signal,
                        BlockReason = $"Zone_{block.ZoneType}_{block.ZoneScore:F2}",
                        BlockType = GateType.Zone,
                        ExpectedEntry = block.ExpectedEntry,
                        ExpectedExit = block.ExpectedExit,
                        RiskAmount = block.RiskAmount,
                        Metadata = block.Metadata
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error loading Zone blocked signals from {Path}", logPath);
            }

            return blockedSignals;
        }

        /// <summary>
        /// Load Pattern-blocked signals from gate logs
        /// </summary>
        private async Task<List<BlockedSignal>> LoadPatternBlockedSignalsAsync(DateTime date)
        {
            var blockedSignals = new List<BlockedSignal>();
            var logPath = Path.Combine(_config.GateLogsPath, "pattern", $"pattern_blocks_{date:yyyyMMdd}.json");
            
            if (!File.Exists(logPath))
                return blockedSignals;

            try
            {
                var json = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
                var patternBlocks = JsonSerializer.Deserialize<List<PatternBlockLog>>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (patternBlocks != null)
                {
                    blockedSignals.AddRange(patternBlocks.Select(block => new BlockedSignal
                    {
                        Timestamp = block.Timestamp,
                        Symbol = block.Symbol,
                        Strategy = block.Strategy,
                        Signal = block.Signal,
                        BlockReason = $"Pattern_{block.PatternType}_{block.Confidence:F2}",
                        BlockType = GateType.Pattern,
                        ExpectedEntry = block.ExpectedEntry,
                        ExpectedExit = block.ExpectedExit,
                        RiskAmount = block.RiskAmount,
                        Metadata = block.Metadata
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error loading Pattern blocked signals from {Path}", logPath);
            }

            return blockedSignals;
        }

        /// <summary>
        /// Simulate a blocked signal to determine if blocking was beneficial
        /// </summary>
        private async Task<CounterfactualResult?> SimulateBlockedSignalAsync(BlockedSignal signal)
        {
            try
            {
                _logger.LogDebug("[COUNTERFACTUAL] Simulating blocked signal: {Symbol} {Strategy} at {Timestamp}",
                    signal.Symbol, signal.Strategy, signal.Timestamp);

                // Load historical quotes for simulation period
                var quotes = await LoadHistoricalQuotesAsync(signal.Symbol, signal.Timestamp, signal.ExpectedExit).ConfigureAwait(false);
                if (!quotes.Any())
                {
                    _logger.LogWarning("[COUNTERFACTUAL] No historical quotes available for {Symbol} from {Start} to {End}",
                        signal.Symbol, signal.Timestamp, signal.ExpectedExit);
                    return null;
                }

                // Set up simulation state
                var simState = new SimState();
                _executionSimulator.ResetState(simState);

                // Create entry order based on signal
                var entryOrder = CreateEntryOrder(signal);
                var entryQuote = GetQuoteAtTime(quotes, signal.Timestamp);
                
                if (entryQuote == null)
                {
                    _logger.LogWarning("[COUNTERFACTUAL] No quote available for entry at {Timestamp}", signal.Timestamp);
                    return null;
                }

                // Simulate entry
                var entryFill = await _executionSimulator.SimulateOrderAsync(entryOrder, entryQuote, simState).ConfigureAwait(false);
                if (entryFill == null)
                {
                    return new CounterfactualResult
                    {
                        Signal = signal,
                        EntryExecuted = false,
                        PnL = 0,
                        Outcome = CounterfactualOutcome.NoEntry,
                        Analysis = "Entry order could not be filled"
                    };
                }

                // Simulate position management until exit
                var exitQuote = GetQuoteAtTime(quotes, signal.ExpectedExit);
                if (exitQuote == null)
                {
                    _logger.LogWarning("[COUNTERFACTUAL] No quote available for exit at {Timestamp}", signal.ExpectedExit);
                    return null;
                }

                // Create exit order and execute
                var exitOrder = CreateExitOrder(signal, entryFill);
                var exitFill = await _executionSimulator.SimulateOrderAsync(exitOrder, exitQuote, simState).ConfigureAwait(false);

                var finalPnL = simState.RealizedPnL + simState.UnrealizedPnL - simState.TotalCommissions;
                var outcome = DetermineOutcome(finalPnL, signal.RiskAmount);

                return new CounterfactualResult
                {
                    Signal = signal,
                    EntryExecuted = true,
                    EntryPrice = entryFill.FillPrice,
                    ExitPrice = exitFill?.FillPrice ?? exitQuote.Last,
                    PnL = finalPnL,
                    Outcome = outcome,
                    Analysis = GenerateAnalysis(signal, finalPnL, outcome)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error simulating blocked signal {Symbol} {Strategy}",
                    signal.Symbol, signal.Strategy);
                return null;
            }
        }

        /// <summary>
        /// Generate comprehensive audit report summarizing saved losses vs missed profits
        /// </summary>
        private CounterfactualAuditReport GenerateAuditReport(List<CounterfactualResult> results, DateTime analysisDate)
        {
            var report = new CounterfactualAuditReport
            {
                AnalysisDate = analysisDate,
                GeneratedAt = DateTime.UtcNow,
                TotalSignalsAnalyzed = results.Count
            };

            foreach (var result in results)
            {
                if (result.EntryExecuted)
                {
                    if (result.PnL < 0)
                    {
                        report.TotalSavedLoss += Math.Abs(result.PnL);
                        report.SavedLossCount++;
                    }
                    else
                    {
                        report.TotalMissedProfit += result.PnL;
                        report.MissedProfitCount++;
                    }
                }
                else
                {
                    report.NoEntryCount++;
                }

                // Categorize by gate type
                report.GateTypeSummary.TryAdd(result.Signal.BlockType, new GateTypeSummary());
                var gateSum = report.GateTypeSummary[result.Signal.BlockType];
                gateSum.TotalBlocks++;
                if (result.PnL < 0) gateSum.SavedLoss += Math.Abs(result.PnL);
                else gateSum.MissedProfit += result.PnL;
            }

            // Calculate effectiveness metrics
            report.GateEffectivenessRatio = report.TotalSavedLoss / Math.Max(report.TotalMissedProfit, 1);
            report.BlockAccuracyRatio = (decimal)report.SavedLossCount / Math.Max(report.SavedLossCount + report.MissedProfitCount, 1);

            return report;
        }

        /// <summary>
        /// Save audit report to state/audits/gate_saves.json
        /// </summary>
        private async Task SaveAuditReportAsync(CounterfactualAuditReport report)
        {
            try
            {
                var auditsDir = Path.Combine("state", "audits");
                Directory.CreateDirectory(auditsDir);

                var fileName = $"gate_saves_{report.AnalysisDate:yyyyMMdd}.json";
                var filePath = Path.Combine(auditsDir, fileName);

                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

                // Also update the latest report
                var latestPath = Path.Combine(auditsDir, "gate_saves_latest.json");
                await File.WriteAllTextAsync(latestPath, json).ConfigureAwait(false);

                _logger.LogInformation("[COUNTERFACTUAL] Saved audit report to {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COUNTERFACTUAL] Error saving audit report");
            }
        }

        // Helper methods

        private DateTime GetNextRunTime()
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, now.Day, _config.NightlyRunHour, _config.NightlyRunMinute, 0, DateTimeKind.Utc);
            
            if (nextRun <= now)
                nextRun = nextRun.AddDays(1);
                
            return nextRun;
        }

        private async Task<List<Quote>> LoadHistoricalQuotesAsync(string symbol, DateTime start, DateTime end)
        {
            // Implementation would load from historical data source
            // For now, return empty list as this would integrate with market data provider
            await Task.CompletedTask;
            return new List<Quote>();
        }

        private Quote? GetQuoteAtTime(List<Quote> quotes, DateTime timestamp)
        {
            return quotes.FirstOrDefault(q => Math.Abs((q.Timestamp - timestamp).TotalSeconds) < 60);
        }

        private OrderSpec CreateEntryOrder(BlockedSignal signal)
        {
            var side = signal.Signal.ToUpperInvariant() switch
            {
                "BUY" or "LONG" => OrderSide.Buy,
                "SELL" or "SHORT" => OrderSide.Sell,
                _ => OrderSide.Buy
            };

            return new OrderSpec(
                signal.Symbol,
                OrderType.Market,
                side,
                CalculatePositionSize(signal.RiskAmount, signal.ExpectedEntry),
                null,
                null,
                TimeInForce.Day,
                signal.Timestamp);
        }

        private OrderSpec CreateExitOrder(BlockedSignal signal, FillResult entryFill)
        {
            var exitSide = entryFill.FilledQuantity > 0 ? OrderSide.Sell : OrderSide.Buy;

            return new OrderSpec(
                signal.Symbol,
                OrderType.Market,
                exitSide,
                Math.Abs(entryFill.FilledQuantity),
                null,
                null,
                TimeInForce.Day,
                signal.ExpectedExit);
        }

        private decimal CalculatePositionSize(decimal riskAmount, decimal entryPrice)
        {
            // Simple position sizing based on risk amount
            // In production this would use proper risk management
            return Math.Max(1, Math.Floor(riskAmount / (entryPrice * 0.01m))); // 1% stop
        }

        private CounterfactualOutcome DetermineOutcome(decimal pnl, decimal riskAmount)
        {
            if (pnl < -riskAmount * 0.5m) return CounterfactualOutcome.LargeLoss;
            if (pnl < 0) return CounterfactualOutcome.SmallLoss;
            if (pnl < riskAmount * 0.5m) return CounterfactualOutcome.SmallProfit;
            return CounterfactualOutcome.LargeProfit;
        }

        private string GenerateAnalysis(BlockedSignal signal, decimal pnl, CounterfactualOutcome outcome)
        {
            var direction = pnl >= 0 ? "profit" : "loss";
            return $"Signal blocked by {signal.BlockReason} would have resulted in {direction} of {Math.Abs(pnl):C}. Outcome: {outcome}";
        }

        public override void Dispose()
        {
            _nightlyTimer?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Configuration for counterfactual replay service
    /// </summary>
    public class CounterfactualReplayConfig
    {
        public string GateLogsPath { get; set; } = "state/gates";
        public int NightlyRunHour { get; set; } = 2; // 2 AM UTC
        public int NightlyRunMinute { get; set; } = 0;
        public int HistoricalDataDays { get; set; } = 7;
    }

    /// <summary>
    /// Represents a signal that was blocked by gating logic
    /// </summary>
    public class BlockedSignal
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty;
        public string BlockReason { get; set; } = string.Empty;
        public GateType BlockType { get; set; }
        public decimal ExpectedEntry { get; set; }
        public DateTime ExpectedExit { get; set; }
        public decimal RiskAmount { get; set; }
        public Dictionary<string, object> Metadata { get; } = new();
    }

    /// <summary>
    /// Result of counterfactual simulation
    /// </summary>
    public class CounterfactualResult
    {
        public BlockedSignal Signal { get; set; } = new();
        public bool EntryExecuted { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal PnL { get; set; }
        public CounterfactualOutcome Outcome { get; set; }
        public string Analysis { get; set; } = string.Empty;
    }

    /// <summary>
    /// Comprehensive audit report for gate effectiveness
    /// </summary>
    public class CounterfactualAuditReport
    {
        public DateTime AnalysisDate { get; set; }
        public DateTime GeneratedAt { get; set; }
        public int TotalSignalsAnalyzed { get; set; }
        public int SavedLossCount { get; set; }
        public int MissedProfitCount { get; set; }
        public int NoEntryCount { get; set; }
        public decimal TotalSavedLoss { get; set; }
        public decimal TotalMissedProfit { get; set; }
        public decimal GateEffectivenessRatio { get; set; }
        public decimal BlockAccuracyRatio { get; set; }
        public Dictionary<GateType, GateTypeSummary> GateTypeSummary { get; } = new();
    }

    /// <summary>
    /// Summary for each gate type
    /// </summary>
    public class GateTypeSummary
    {
        public int TotalBlocks { get; set; }
        public decimal SavedLoss { get; set; }
        public decimal MissedProfit { get; set; }
    }

    // Enums and supporting types
    public enum GateType { S7, Zone, Pattern }
    public enum CounterfactualOutcome { NoEntry, LargeLoss, SmallLoss, SmallProfit, LargeProfit }

    // Gate log data structures
    public class S7BlockLog
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty;
        public string S7State { get; set; } = string.Empty;
        public decimal ExpectedEntry { get; set; }
        public DateTime ExpectedExit { get; set; }
        public decimal RiskAmount { get; set; }
        public Dictionary<string, object> Metadata { get; } = new();
    }

    public class ZoneBlockLog
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty;
        public string ZoneType { get; set; } = string.Empty;
        public decimal ZoneScore { get; set; }
        public decimal ExpectedEntry { get; set; }
        public DateTime ExpectedExit { get; set; }
        public decimal RiskAmount { get; set; }
        public Dictionary<string, object> Metadata { get; } = new();
    }

    public class PatternBlockLog
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty;
        public string PatternType { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public decimal ExpectedEntry { get; set; }
        public DateTime ExpectedExit { get; set; }
        public decimal RiskAmount { get; set; }
        public Dictionary<string, object> Metadata { get; } = new();
    }
}