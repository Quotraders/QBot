using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BotCore.Models;
using BotCore.Services;

namespace BotCore.Integrations
{
    /// <summary>
    /// Enhanced strategy integration that automatically collects training data from all strategy executions.
    /// Bridges the existing strategy system with the new RL training data collection.
    /// </summary>
    public static class EnhancedStrategyIntegration
    {
        // Trading calculation constants
        private const decimal DefaultStopLossMultiplier = 0.99m; // 1% below entry
        private const decimal DefaultTakeProfitMultiplier = 1.02m; // 2% above entry
        private const decimal DefaultVixLevel = 20m; // Baseline VIX level
        private const decimal HighVolatilityThreshold = 0.01m; // 1% price range
        private const decimal LowVolatilityThreshold = 0.005m; // 0.5% price range
        private const decimal NeutralRsiValue = 50m; // Neutral RSI baseline
        
        /// <summary>
        /// Enhance a strategy signal with comprehensive data collection for RL training.
        /// Call this whenever a strategy generates a signal.
        /// </summary>
        public static async Task<string?> CollectSignalDataAsync(
            ILogger logger,
            IEnhancedTrainingDataService trainingService,
            StrategySignal signal,
            Bar currentBar,
            MarketSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(trainingService);
            ArgumentNullException.ThrowIfNull(signal);
            ArgumentNullException.ThrowIfNull(currentBar);
            
            try
            {
                // Convert strategy signal to training data format
                var signalData = ConvertToTrainingSignalData(signal, currentBar, snapshot);

                // Record the trade data for RL training
                var tradeId = await trainingService.RecordTradeAsync(signalData).ConfigureAwait(false);

                logger.LogDebug("[EnhancedIntegration] Collected signal data for {Strategy}: {TradeId}",
                    signal.Strategy, tradeId);

                return tradeId;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid operation while collecting signal data for {Strategy}",
                    signal.Strategy);
                return null;
            }
            catch (ArgumentException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid argument while collecting signal data for {Strategy}",
                    signal.Strategy);
                return null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                logger.LogError(ex, "[EnhancedIntegration] Unexpected error collecting signal data for {Strategy}",
                    signal.Strategy);
                return null;
            }
        }

        /// <summary>
        /// Record trade outcome for RL training when a trade closes.
        /// Call this whenever a trade is closed/exited.
        /// </summary>
        public static async Task RecordTradeOutcomeAsync(
            ILogger logger,
            IEnhancedTrainingDataService trainingService,
            string tradeId,
            decimal entryPrice,
            decimal exitPrice,
            decimal pnl,
            bool isWin,
            DateTime exitTime,
            TimeSpan holdingTime)
        {
            ArgumentNullException.ThrowIfNull(trainingService);
            
            try
            {
                var outcomeData = new TradeOutcomeData
                {
                    IsWin = isWin,
                    ActualPnl = pnl,
                    ExitPrice = exitPrice,
                    ExitTime = exitTime,
                    HoldingTimeMinutes = (decimal)holdingTime.TotalMinutes,
                    ActualRMultiple = CalculateRMultiple(entryPrice, exitPrice, pnl),
                    MaxDrawdown = Math.Min(0, pnl) // Simplified - could be enhanced with real-time tracking
                };

                await trainingService.RecordTradeResultAsync(tradeId, outcomeData).ConfigureAwait(false);

                logger.LogDebug("[EnhancedIntegration] Recorded trade outcome for {TradeId}: {Result} (R={RMultiple:F2})",
                    tradeId, isWin ? "WIN" : "LOSS", outcomeData.ActualRMultiple);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid operation while recording trade outcome for {TradeId}", tradeId);
            }
            catch (ArgumentException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid argument while recording trade outcome for {TradeId}", tradeId);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                logger.LogError(ex, "[EnhancedIntegration] Unexpected error recording trade outcome for {TradeId}", tradeId);
            }
        }

        /// <summary>
        /// Enhanced signal processing that integrates with both existing collectors and new training system.
        /// Call this as part of the strategy execution pipeline.
        /// </summary>
        public static async Task<EnhancedSignalResult> ProcessSignalWithDataCollectionAsync(
            ILogger logger,
            IEnhancedTrainingDataService trainingService,
            StrategySignal signal,
            Bar currentBar,
            MarketSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(signal);
            
            var result = new EnhancedSignalResult
            {
                OriginalSignal = signal,
                TradeId = null,
                Success = false
            };

            try
            {
                // Process with enhanced training data collection
                result.TradeId = await CollectSignalDataAsync(logger, trainingService, signal, currentBar, snapshot).ConfigureAwait(false);
                result.Success = !string.IsNullOrEmpty(result.TradeId);

                logger.LogInformation("[EnhancedIntegration] Processed signal for {Strategy} - TradeId: {TradeId}",
                    signal.Strategy, result.TradeId ?? "N/A");

                return result;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid operation while processing signal with data collection");
                return result;
            }
            catch (ArgumentException ex)
            {
                logger.LogError(ex, "[EnhancedIntegration] Invalid argument while processing signal with data collection");
                return result;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                logger.LogError(ex, "[EnhancedIntegration] Unexpected error processing signal with data collection");
                return result;
            }
        }

        private static TradeSignalData ConvertToTrainingSignalData(StrategySignal signal, Bar currentBar, MarketSnapshot snapshot)
        {
            return new TradeSignalData
            {
                Id = signal.ClientOrderId ?? Guid.NewGuid().ToString(),
                Symbol = signal.Symbol,
                Direction = DetermineDirection(signal),
                Entry = signal.LimitPrice ?? currentBar.Close,
                Size = signal.Size,
                Strategy = signal.Strategy,
                StopLoss = currentBar.Close * DefaultStopLossMultiplier, // Simplified stop loss
                TakeProfit = currentBar.Close * DefaultTakeProfitMultiplier, // Simplified take profit
                Regime = DetermineMarketRegime(currentBar),
                Atr = CalculateAtr(currentBar),
                Rsi = CalculateRsi(),
                Ema20 = currentBar.Close, // Simplified - could use actual EMA
                Ema50 = currentBar.Close, // Simplified - could use actual EMA
                Momentum = CalculateMomentum(currentBar),
                TrendStrength = CalculateTrendStrength(currentBar),
                VixLevel = DefaultVixLevel // Default VIX level - could be fetched from market data
            };
        }

        private static string DetermineDirection(StrategySignal signal)
        {
            if (signal.Side == SignalSide.Buy) return "BUY";
            if (signal.Side == SignalSide.Sell) return "SELL";
            return "HOLD";
        }

        private static string DetermineMarketRegime(Bar currentBar)
        {
            var range = currentBar.High - currentBar.Low;
            var price = currentBar.Close;

            if (range / price > HighVolatilityThreshold) return "HighVol";
            if (range / price < LowVolatilityThreshold) return "LowVol";
            return "Range";
        }

        private static decimal CalculateRMultiple(decimal entryPrice, decimal exitPrice, decimal pnl)
        {
            if (entryPrice == 0) return 0;

            // Estimate R-multiple based on typical risk parameters
            var estimatedRisk = entryPrice * 0.01m; // 1% risk assumption
            var rMultiple = pnl / estimatedRisk;

            return rMultiple;
        }

        // Helper calculation methods (simplified - could be enhanced with actual technical analysis)
        private static decimal CalculateAtr(Bar bar) => (bar.High - bar.Low);
        private static decimal CalculateRsi() => NeutralRsiValue; // Neutral RSI - could be calculated from price history
        private static decimal CalculateMomentum(Bar bar) => (bar.Close - bar.Open) / bar.Open;
        private static decimal CalculateTrendStrength(Bar bar) => Math.Abs(bar.Close - bar.Open) / bar.Open;
    }

    /// <summary>
    /// Result of enhanced signal processing with data collection
    /// </summary>
    public class EnhancedSignalResult
    {
        public StrategySignal OriginalSignal { get; set; } = null!;
        public string? TradeId { get; set; }
        public bool Success { get; set; }
    }
}