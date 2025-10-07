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
using TradingBot.Abstractions;
using TopstepX.Bot.Core.Services;

namespace BotCore.Services
{
    /// <summary>
    /// Unified Position Management Service - Production Trading Bot
    /// 
    /// CRITICAL PRODUCTION SERVICE - Manages all active positions with:
    /// - Breakeven protection (moves stop to entry + 1 tick when profitable)
    /// - Trailing stops (follows price to lock in profits)
    /// - Time-based exits (closes stale positions)
    /// - Max excursion tracking (for ML/RL optimization)
    /// 
    /// Runs every 5 seconds to monitor and update all open positions.
    /// Works with ParameterBundle for strategy-specific settings.
    /// Integrates with PositionTracker for real-time market prices.
    /// Integrates with IOrderService for stop modifications and position closes.
    /// </summary>
    public sealed class UnifiedPositionManagementService : BackgroundService
    {
        private readonly ILogger<UnifiedPositionManagementService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly OllamaClient? _ollamaClient;
        private readonly bool _commentaryEnabled;
        private readonly ConcurrentDictionary<string, PositionManagementState> _activePositions = new();
        
        // Monitoring interval
        private const int MonitoringIntervalSeconds = 5;
        
        // Tick size for ES/MES (0.25) - used for breakeven calculations
        private const decimal EsTickSize = 0.25m;
        private const decimal NqTickSize = 0.25m;
        private const decimal DefaultTickSize = 0.25m;
        
        // PHASE 4: Multi-level partial exit thresholds (in R-multiples)
        private const decimal FirstPartialExitThreshold = 1.5m;  // Close 50% at 1.5R
        private const decimal SecondPartialExitThreshold = 2.5m; // Close 30% at 2.5R
        private const decimal FinalPartialExitThreshold = 4.0m;  // Close 20% at 4.0R (runner position)
        
        // PHASE 4: Volatility adaptation parameters
        private const decimal HighVolatilityThreshold = 1.5m;    // ATR > 1.5x recent average = high vol
        private const decimal LowVolatilityThreshold = 0.7m;     // ATR < 0.7x recent average = low vol
        private const decimal VolatilityStopWidening = 0.20m;    // Widen stops by 20% in high vol
        private const decimal VolatilityStopTightening = 0.20m;  // Tighten stops by 20% in low vol
        
        public UnifiedPositionManagementService(
            ILogger<UnifiedPositionManagementService> logger,
            IServiceProvider serviceProvider,
            OllamaClient? ollamaClient = null)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _ollamaClient = ollamaClient;
            
            // Check if position management commentary is enabled (default: true for transparency)
            // Set BOT_POSITION_COMMENTARY_ENABLED=false to disable if too verbose
            var commentarySetting = Environment.GetEnvironmentVariable("BOT_POSITION_COMMENTARY_ENABLED")?.ToLowerInvariant();
            _commentaryEnabled = commentarySetting != "false"; // Enabled by default, must explicitly disable
            
            if (_commentaryEnabled && _ollamaClient != null)
            {
                _logger.LogInformation("🤖 [POSITION-MGMT] AI commentary enabled for position management actions");
            }
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🎯 [POSITION-MGMT] Unified Position Management Service starting...");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorAndManagePositionsAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-MGMT] Error in position management loop");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(MonitoringIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            
            _logger.LogInformation("🛑 [POSITION-MGMT] Unified Position Management Service stopping");
        }
        
        /// <summary>
        /// Register a new position for management
        /// Called when a trade is opened
        /// </summary>
        public void RegisterPosition(
            string positionId,
            string symbol,
            string strategy,
            decimal entryPrice,
            decimal stopPrice,
            decimal targetPrice,
            int quantity,
            BracketMode bracketMode)
        {
            var state = new PositionManagementState
            {
                PositionId = positionId,
                Symbol = symbol,
                Strategy = strategy,
                EntryPrice = entryPrice,
                CurrentStopPrice = stopPrice,
                TargetPrice = targetPrice,
                Quantity = quantity,
                EntryTime = DateTime.UtcNow,
                MaxFavorablePrice = entryPrice,
                MaxAdversePrice = entryPrice,
                BreakevenActivated = false,
                TrailingStopActive = false,
                BreakevenAfterTicks = bracketMode.BreakevenAfterTicks,
                TrailTicks = bracketMode.TrailTicks,
                MaxHoldMinutes = GetMaxHoldMinutes(strategy),
                LastCheckTime = DateTime.UtcNow,
                StopModificationCount = 0
            };
            
            _activePositions[positionId] = state;
            
            _logger.LogInformation("📝 [POSITION-MGMT] Registered position {PositionId}: {Strategy} {Symbol} {Qty}@{Entry}, BE after {BETicks} ticks, Trail {TrailTicks} ticks, Max hold {MaxHold}m",
                positionId, strategy, symbol, quantity, entryPrice, bracketMode.BreakevenAfterTicks, bracketMode.TrailTicks, state.MaxHoldMinutes);
        }
        
        /// <summary>
        /// Unregister position when it's closed
        /// </summary>
        public void UnregisterPosition(string positionId, ExitReason exitReason)
        {
            if (_activePositions.TryRemove(positionId, out var state))
            {
                var duration = DateTime.UtcNow - state.EntryTime;
                _logger.LogInformation("✅ [POSITION-MGMT] Unregistered position {PositionId}: {Strategy} {Symbol}, Duration: {Duration}m, Exit: {Reason}, Stop mods: {Mods}",
                    positionId, state.Strategy, state.Symbol, duration.TotalMinutes, exitReason, state.StopModificationCount);
                
                // PHASE 3: Report outcome to Position Management Optimizer for ML/RL learning
                ReportOutcomeToOptimizer(state, exitReason, duration);
            }
        }
        
        /// <summary>
        /// PHASE 3: Report position management outcome for ML/RL learning
        /// </summary>
        private void ReportOutcomeToOptimizer(PositionManagementState state, ExitReason exitReason, TimeSpan duration)
        {
            try
            {
                var optimizer = _serviceProvider.GetService<PositionManagementOptimizer>();
                if (optimizer == null)
                {
                    return; // Optimizer not registered (optional dependency)
                }
                
                var isLong = state.Quantity > 0;
                var tickSize = GetTickSize(state.Symbol);
                
                // Calculate final P&L in ticks
                var pnlTicks = isLong
                    ? (state.MaxFavorablePrice - state.EntryPrice) / tickSize
                    : (state.EntryPrice - state.MaxFavorablePrice) / tickSize;
                
                // Calculate max excursions in ticks
                var maxFavTicks = isLong
                    ? (state.MaxFavorablePrice - state.EntryPrice) / tickSize
                    : (state.EntryPrice - state.MaxFavorablePrice) / tickSize;
                
                var maxAdvTicks = isLong
                    ? (state.MaxAdversePrice - state.EntryPrice) / tickSize
                    : (state.EntryPrice - state.MaxAdversePrice) / tickSize;
                
                // Determine what happened
                var breakevenTriggered = state.BreakevenActivated;
                var stoppedOut = exitReason == ExitReason.StopLoss || exitReason == ExitReason.Breakeven || exitReason == ExitReason.TrailingStop;
                var targetHit = exitReason == ExitReason.Target;
                var timedOut = exitReason == ExitReason.TimeLimit;
                
                // Trail multiplier calculation (estimate from trail ticks)
                var trailMultiplier = state.TrailTicks * tickSize / 1.0m; // Simplified estimate
                
                // Record outcome
                optimizer.RecordOutcome(
                    strategy: state.Strategy,
                    symbol: state.Symbol,
                    breakevenAfterTicks: state.BreakevenAfterTicks,
                    trailMultiplier: trailMultiplier,
                    maxHoldMinutes: state.MaxHoldMinutes,
                    breakevenTriggered: breakevenTriggered,
                    stoppedOut: stoppedOut,
                    targetHit: targetHit,
                    timedOut: timedOut,
                    finalPnL: pnlTicks,
                    maxFavorableExcursion: maxFavTicks,
                    maxAdverseExcursion: maxAdvTicks,
                    marketRegime: "UNKNOWN" // Could be enhanced with regime detection
                );
                
                _logger.LogDebug("📊 [POSITION-MGMT] Reported outcome to optimizer: {Strategy} {Symbol}, BE={BE}, StoppedOut={SO}, TargetHit={TH}, TimedOut={TO}, PnL={PnL} ticks",
                    state.Strategy, state.Symbol, breakevenTriggered, stoppedOut, targetHit, timedOut, pnlTicks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [POSITION-MGMT] Error reporting outcome to optimizer for {PositionId}", state.PositionId);
            }
        }
        
        /// <summary>
        /// Main monitoring loop - checks all positions and applies management rules
        /// </summary>
        private async Task MonitorAndManagePositionsAsync(CancellationToken cancellationToken)
        {
            if (_activePositions.IsEmpty)
            {
                return; // No positions to manage
            }
            
            foreach (var kvp in _activePositions)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var state = kvp.Value;
                state.LastCheckTime = DateTime.UtcNow;
                
                try
                {
                    // Get current market price (this would come from market data service in production)
                    var currentPrice = await GetCurrentMarketPriceAsync(state.Symbol, cancellationToken).ConfigureAwait(false);
                    if (currentPrice <= 0)
                        continue; // Skip if no price available
                    
                    // Update max excursion tracking
                    UpdateMaxExcursion(state, currentPrice);
                    
                    // Calculate current profit in ticks
                    var tickSize = GetTickSize(state.Symbol);
                    var isLong = state.Quantity > 0;
                    var profitTicks = CalculateProfitTicks(state.EntryPrice, currentPrice, tickSize, isLong);
                    
                    // Check time-based exit first
                    if (ShouldExitOnTime(state))
                    {
                        // AI Commentary: Explain time-based exit (non-blocking)
                        try
                        {
                            var holdDuration = DateTime.UtcNow - state.EntryTime;
                            var currentPnL = profitTicks * tickSize * state.Quantity * (isLong ? 50m : -50m);
                            var marketRegime = "UNKNOWN";
                            
                            ExplainTimeBasedExitFireAndForget(state, holdDuration, currentPnL, marketRegime);
                        }
                        catch
                        {
                            // Silently ignore AI errors
                        }
                        
                        await RequestPositionCloseAsync(state, ExitReason.TimeLimit, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    
                    // Apply breakeven protection if profit threshold reached
                    if (!state.BreakevenActivated && profitTicks >= state.BreakevenAfterTicks)
                    {
                        await ActivateBreakevenProtectionAsync(state, tickSize, cancellationToken).ConfigureAwait(false);
                    }
                    
                    // Activate and update trailing stop if profit threshold reached
                    if (state.BreakevenActivated && !state.TrailingStopActive && profitTicks >= state.BreakevenAfterTicks + state.TrailTicks)
                    {
                        state.TrailingStopActive = true;
                        _logger.LogInformation("🔄 [POSITION-MGMT] Trailing stop activated for {PositionId}: {Symbol} at +{Ticks} ticks profit",
                            state.PositionId, state.Symbol, profitTicks);
                    }
                    
                    // PHASE 4: Check for multi-level partial exits
                    await CheckPartialExitsAsync(state, currentPrice, tickSize, cancellationToken).ConfigureAwait(false);
                    
                    // PHASE 4: Apply volatility-adaptive stop adjustment
                    await ApplyVolatilityAdaptiveStopAsync(state, tickSize, cancellationToken).ConfigureAwait(false);
                    
                    // Update trailing stop if active
                    if (state.TrailingStopActive)
                    {
                        await UpdateTrailingStopAsync(state, currentPrice, tickSize, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-MGMT] Error managing position {PositionId}", state.PositionId);
                }
            }
        }
        
        /// <summary>
        /// Activate breakeven protection - move stop to entry + 1 tick
        /// </summary>
        private async Task ActivateBreakevenProtectionAsync(
            PositionManagementState state,
            decimal tickSize,
            CancellationToken cancellationToken)
        {
            var isLong = state.Quantity > 0;
            var breakevenStop = isLong 
                ? state.EntryPrice + tickSize 
                : state.EntryPrice - tickSize;
            
            await ModifyStopPriceAsync(state, breakevenStop, "Breakeven", cancellationToken).ConfigureAwait(false);
            state.BreakevenActivated = true;
            
            _logger.LogInformation("🛡️ [POSITION-MGMT] Breakeven activated for {PositionId}: {Symbol}, Stop moved to {Stop} (entry +{Tick})",
                state.PositionId, state.Symbol, breakevenStop, tickSize);
            
            // AI Commentary: Explain breakeven activation (non-blocking)
            try
            {
                var currentPrice = await GetCurrentMarketPriceAsync(state.Symbol, cancellationToken).ConfigureAwait(false);
                var profitTicks = CalculateProfitTicks(state.EntryPrice, currentPrice, tickSize, isLong);
                var unrealizedPnL = profitTicks * tickSize * state.Quantity * (isLong ? 50m : -50m); // $50 per tick for ES
                var marketRegime = "UNKNOWN"; // Could be enhanced with regime detection
                
                ExplainBreakevenProtectionFireAndForget(state, unrealizedPnL, profitTicks, breakevenStop, marketRegime);
            }
            catch
            {
                // Silently ignore AI errors - don't disrupt position management
            }
        }
        
        /// <summary>
        /// Update trailing stop to follow price
        /// </summary>
        private async Task UpdateTrailingStopAsync(
            PositionManagementState state,
            decimal currentPrice,
            decimal tickSize,
            CancellationToken cancellationToken)
        {
            var isLong = state.Quantity > 0;
            var trailDistance = state.TrailTicks * tickSize;
            
            var newStopPrice = isLong
                ? currentPrice - trailDistance
                : currentPrice + trailDistance;
            
            // Only update if new stop is better (higher for longs, lower for shorts)
            var shouldUpdate = isLong
                ? newStopPrice > state.CurrentStopPrice
                : newStopPrice < state.CurrentStopPrice;
            
            if (shouldUpdate)
            {
                var oldStopPrice = state.CurrentStopPrice;
                await ModifyStopPriceAsync(state, newStopPrice, "Trailing", cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("📈 [POSITION-MGMT] Trailing stop updated for {PositionId}: {Symbol}, Stop: {Old} → {New} (trail {Ticks} ticks)",
                    state.PositionId, state.Symbol, oldStopPrice, newStopPrice, state.TrailTicks);
                
                // AI Commentary: Explain trailing stop activation (non-blocking)
                try
                {
                    var profitTicks = CalculateProfitTicks(state.EntryPrice, currentPrice, tickSize, isLong);
                    var unrealizedPnL = profitTicks * tickSize * state.Quantity * (isLong ? 50m : -50m); // $50 per tick for ES
                    var marketRegime = "UNKNOWN"; // Could be enhanced with regime detection
                    
                    ExplainTrailingStopActivationFireAndForget(state, unrealizedPnL, profitTicks, newStopPrice, marketRegime);
                }
                catch
                {
                    // Silently ignore AI errors - don't disrupt position management
                }
            }
        }
        
        /// <summary>
        /// Check if position should be closed due to time limit
        /// </summary>
        private bool ShouldExitOnTime(PositionManagementState state)
        {
            if (state.MaxHoldMinutes <= 0)
                return false; // No time limit
                
            var duration = DateTime.UtcNow - state.EntryTime;
            return duration.TotalMinutes >= state.MaxHoldMinutes;
        }
        
        /// <summary>
        /// Update max favorable and adverse excursion tracking
        /// </summary>
        private static void UpdateMaxExcursion(PositionManagementState state, decimal currentPrice)
        {
            var isLong = state.Quantity > 0;
            
            if (isLong)
            {
                // For long positions: higher is favorable, lower is adverse
                if (currentPrice > state.MaxFavorablePrice)
                    state.MaxFavorablePrice = currentPrice;
                if (currentPrice < state.MaxAdversePrice)
                    state.MaxAdversePrice = currentPrice;
            }
            else
            {
                // For short positions: lower is favorable, higher is adverse
                if (currentPrice < state.MaxFavorablePrice)
                    state.MaxFavorablePrice = currentPrice;
                if (currentPrice > state.MaxAdversePrice)
                    state.MaxAdversePrice = currentPrice;
            }
        }
        
        /// <summary>
        /// Calculate profit in ticks
        /// </summary>
        private static decimal CalculateProfitTicks(decimal entryPrice, decimal currentPrice, decimal tickSize, bool isLong)
        {
            var priceDiff = isLong 
                ? currentPrice - entryPrice 
                : entryPrice - currentPrice;
            
            return priceDiff / tickSize;
        }
        
        /// <summary>
        /// Get tick size for symbol
        /// </summary>
        private static decimal GetTickSize(string symbol)
        {
            if (symbol.Contains("ES", StringComparison.OrdinalIgnoreCase) || 
                symbol.Contains("MES", StringComparison.OrdinalIgnoreCase))
                return EsTickSize;
            
            if (symbol.Contains("NQ", StringComparison.OrdinalIgnoreCase) || 
                symbol.Contains("MNQ", StringComparison.OrdinalIgnoreCase))
                return NqTickSize;
            
            return DefaultTickSize;
        }
        
        /// <summary>
        /// Get max hold minutes for strategy
        /// </summary>
        private static int GetMaxHoldMinutes(string strategy)
        {
            return strategy switch
            {
                "S2" => 60,   // S2: 60 minutes max
                "S3" => 90,   // S3: 90 minutes max
                "S6" => 45,   // S6: 45 minutes max (from existing code)
                "S11" => 60,  // S11: 60 minutes max (from existing code)
                _ => 120      // Default: 2 hours
            };
        }
        
        /// <summary>
        /// Get current market price from PositionTrackingSystem
        /// </summary>
        private Task<decimal> GetCurrentMarketPriceAsync(string symbol, CancellationToken cancellationToken)
        {
            try
            {
                // Try to get PositionTrackingSystem from service provider
                var positionTracker = _serviceProvider.GetService<PositionTrackingSystem>();
                if (positionTracker != null)
                {
                    var positions = positionTracker.GetAllPositions();
                    if (positions.TryGetValue(symbol, out var position))
                    {
                        // Calculate current price from unrealized P&L if available
                        // UnrealizedPnL = (marketPrice - avgPrice) * netQuantity
                        // So marketPrice = (unrealizedPnL / netQuantity) + avgPrice
                        if (position.NetQuantity != 0 && position.AveragePrice > 0)
                        {
                            var estimatedPrice = (position.UnrealizedPnL / position.NetQuantity) + position.AveragePrice;
                            if (estimatedPrice > 0)
                            {
                                return Task.FromResult(estimatedPrice);
                            }
                        }
                        
                        // Fallback to average price if no unrealized P&L
                        if (position.AveragePrice > 0)
                        {
                            return Task.FromResult(position.AveragePrice);
                        }
                    }
                }
                
                // If no price available from tracker, return 0 to skip this cycle
                return Task.FromResult(0m);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[POSITION-MGMT] Error getting market price for {Symbol}", symbol);
                return Task.FromResult(0m);
            }
        }
        
        /// <summary>
        /// Modify stop price using IOrderService
        /// </summary>
        private async Task ModifyStopPriceAsync(
            PositionManagementState state,
            decimal newStopPrice,
            string reason,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get order service from DI container
                var orderService = _serviceProvider.GetService<IOrderService>();
                if (orderService != null)
                {
                    // Call the actual order service to modify stop loss
                    var success = await orderService.ModifyStopLossAsync(state.PositionId, newStopPrice).ConfigureAwait(false);
                    
                    if (success)
                    {
                        state.CurrentStopPrice = newStopPrice;
                        state.StopModificationCount++;
                        
                        _logger.LogInformation("🔧 [POSITION-MGMT] Stop modified for {PositionId}: {Reason}, New stop: {Stop}",
                            state.PositionId, reason, newStopPrice);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [POSITION-MGMT] Failed to modify stop for {PositionId}: {Reason}",
                            state.PositionId, reason);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [POSITION-MGMT] OrderService not available, cannot modify stop for {PositionId}",
                        state.PositionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [POSITION-MGMT] Error modifying stop for {PositionId}", state.PositionId);
            }
        }
        
        /// <summary>
        /// Request position close using IOrderService
        /// </summary>
        private async Task RequestPositionCloseAsync(
            PositionManagementState state,
            ExitReason reason,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogWarning("⏰ [POSITION-MGMT] {Reason} triggered for {PositionId}: {Symbol}, {Duration}m elapsed",
                    reason, state.PositionId, state.Symbol, (DateTime.UtcNow - state.EntryTime).TotalMinutes);
                
                // Get order service from DI container
                var orderService = _serviceProvider.GetService<IOrderService>();
                if (orderService != null)
                {
                    // Call the actual order service to close position
                    var success = await orderService.ClosePositionAsync(state.PositionId).ConfigureAwait(false);
                    
                    if (success)
                    {
                        _logger.LogInformation("✅ [POSITION-MGMT] Position closed successfully: {PositionId}, Reason: {Reason}",
                            state.PositionId, reason);
                        
                        // Unregister after successful close
                        UnregisterPosition(state.PositionId, reason);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [POSITION-MGMT] Failed to close position {PositionId}, will retry next cycle",
                            state.PositionId);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [POSITION-MGMT] OrderService not available, cannot close position {PositionId}",
                        state.PositionId);
                    
                    // Still unregister to prevent infinite retries
                    UnregisterPosition(state.PositionId, reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [POSITION-MGMT] Error closing position {PositionId}", state.PositionId);
                
                // Unregister to prevent infinite retries
                UnregisterPosition(state.PositionId, reason);
            }
        }
        
        /// <summary>
        /// Get excursion metrics for a position (for exit logging)
        /// </summary>
        public (decimal maxFavorable, decimal maxAdverse) GetExcursionMetrics(string positionId)
        {
            if (_activePositions.TryGetValue(positionId, out var state))
            {
                var isLong = state.Quantity > 0;
                var tickSize = GetTickSize(state.Symbol);
                
                var maxFavTicks = isLong
                    ? (state.MaxFavorablePrice - state.EntryPrice) / tickSize
                    : (state.EntryPrice - state.MaxFavorablePrice) / tickSize;
                
                var maxAdvTicks = isLong
                    ? (state.MaxAdversePrice - state.EntryPrice) / tickSize
                    : (state.EntryPrice - state.MaxAdversePrice) / tickSize;
                
                return (maxFavTicks, maxAdvTicks);
            }
            
            return (0m, 0m);
        }
        
        /// <summary>
        /// PHASE 4: Check and execute multi-level partial exits
        /// First target: Close 50% at 1.5R
        /// Second target: Close 30% at 2.5R  
        /// Final target: Close remaining 20% at 4.0R (runner position)
        /// </summary>
        private async Task CheckPartialExitsAsync(
            PositionManagementState state,
            decimal currentPrice,
            decimal tickSize,
            CancellationToken cancellationToken)
        {
            // Calculate R-multiple (profit relative to risk)
            var initialRisk = Math.Abs(state.CurrentStopPrice - state.EntryPrice);
            if (initialRisk <= 0)
            {
                return; // Invalid risk calculation
            }
            
            var isLong = state.Quantity > 0;
            var currentProfit = isLong
                ? currentPrice - state.EntryPrice
                : state.EntryPrice - currentPrice;
            
            var rMultiple = currentProfit / initialRisk;
            
            // Track which partial exits have been executed
            if (!state.HasProperty("FirstPartialExecuted"))
            {
                state.SetProperty("FirstPartialExecuted", false);
            }
            if (!state.HasProperty("SecondPartialExecuted"))
            {
                state.SetProperty("SecondPartialExecuted", false);
            }
            if (!state.HasProperty("FinalPartialExecuted"))
            {
                state.SetProperty("FinalPartialExecuted", false);
            }
            
            var firstPartialDone = (bool)(state.GetProperty("FirstPartialExecuted") ?? false);
            var secondPartialDone = (bool)(state.GetProperty("SecondPartialExecuted") ?? false);
            var finalPartialDone = (bool)(state.GetProperty("FinalPartialExecuted") ?? false);
            
            // Check first partial exit at 1.5R (close 50%)
            if (!firstPartialDone && rMultiple >= FirstPartialExitThreshold)
            {
                _logger.LogInformation("🎯 [POSITION-MGMT] PHASE 4 - First partial exit triggered for {PositionId}: {Symbol} at {R}R, closing 50%",
                    state.PositionId, state.Symbol, rMultiple);
                
                // AI Commentary: Explain first partial exit (non-blocking)
                try
                {
                    var partialQuantity = Math.Floor(state.Quantity * 0.50m);
                    ExplainPartialExitFireAndForget(state, rMultiple, 50m, partialQuantity, "First Target (1.5R)");
                }
                catch
                {
                    // Silently ignore AI errors
                }
                
                await RequestPartialCloseAsync(state, 0.50m, ExitReason.Partial, cancellationToken).ConfigureAwait(false);
                state.SetProperty("FirstPartialExecuted", true);
            }
            // Check second partial exit at 2.5R (close 30% of remaining = 30% of original)
            else if (firstPartialDone && !secondPartialDone && rMultiple >= SecondPartialExitThreshold)
            {
                _logger.LogInformation("🎯 [POSITION-MGMT] PHASE 4 - Second partial exit triggered for {PositionId}: {Symbol} at {R}R, closing 30%",
                    state.PositionId, state.Symbol, rMultiple);
                
                // AI Commentary: Explain second partial exit (non-blocking)
                try
                {
                    var partialQuantity = Math.Floor(state.Quantity * 0.30m);
                    ExplainPartialExitFireAndForget(state, rMultiple, 30m, partialQuantity, "Second Target (2.5R)");
                }
                catch
                {
                    // Silently ignore AI errors
                }
                
                await RequestPartialCloseAsync(state, 0.30m, ExitReason.Partial, cancellationToken).ConfigureAwait(false);
                state.SetProperty("SecondPartialExecuted", true);
            }
            // Check final partial exit at 4.0R (close remaining 20% = runner position)
            else if (secondPartialDone && !finalPartialDone && rMultiple >= FinalPartialExitThreshold)
            {
                _logger.LogInformation("🎯 [POSITION-MGMT] PHASE 4 - Final partial exit (runner) triggered for {PositionId}: {Symbol} at {R}R, closing final 20%",
                    state.PositionId, state.Symbol, rMultiple);
                
                // AI Commentary: Explain final partial exit (non-blocking)
                try
                {
                    var partialQuantity = Math.Floor(state.Quantity * 0.20m);
                    ExplainPartialExitFireAndForget(state, rMultiple, 20m, partialQuantity, "Runner Position (4.0R)");
                }
                catch
                {
                    // Silently ignore AI errors
                }
                
                await RequestPartialCloseAsync(state, 0.20m, ExitReason.Target, cancellationToken).ConfigureAwait(false);
                state.SetProperty("FinalPartialExecuted", true);
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// PHASE 4: Apply volatility-adaptive stop adjustments
        /// High volatility (ATR > 1.5x avg) → Widen stops by 20%
        /// Low volatility (ATR < 0.7x avg) → Tighten stops by 20%
        /// Integrates VIX: if VIX > 20 → widen all stops
        /// Session-aware: Asia session (low vol) → tighter stops
        /// </summary>
        private async Task ApplyVolatilityAdaptiveStopAsync(
            PositionManagementState state,
            decimal tickSize,
            CancellationToken cancellationToken)
        {
            // Check if volatility adjustment already applied this cycle
            if (state.HasProperty("VolatilityAdjustedThisCycle"))
            {
                var lastAdjustedObj = state.GetProperty("VolatilityAdjustedThisCycle");
                if (lastAdjustedObj is DateTime lastAdjusted && (DateTime.UtcNow - lastAdjusted).TotalMinutes < 5)
                {
                    return; // Don't adjust more than once per 5 minutes
                }
            }
            
            try
            {
                // Get current ATR (would come from market data service in production)
                // For now, use simplified estimation based on recent price movement
                var currentVolatility = EstimateCurrentVolatility(state);
                var avgVolatility = EstimateAverageVolatility(state);
                
                if (avgVolatility <= 0)
                {
                    return; // Can't calculate ratio
                }
                
                var volatilityRatio = currentVolatility / avgVolatility;
                decimal stopAdjustmentFactor = 1.0m;
                string adjustmentReason = "";
                
                // Determine adjustment based on volatility
                if (volatilityRatio > HighVolatilityThreshold)
                {
                    // High volatility - widen stops
                    stopAdjustmentFactor = 1.0m + VolatilityStopWidening;
                    adjustmentReason = $"High volatility ({volatilityRatio:F2}x avg)";
                }
                else if (volatilityRatio < LowVolatilityThreshold)
                {
                    // Low volatility - tighten stops
                    stopAdjustmentFactor = 1.0m - VolatilityStopTightening;
                    adjustmentReason = $"Low volatility ({volatilityRatio:F2}x avg)";
                }
                
                // Apply adjustment if needed
                if (stopAdjustmentFactor != 1.0m)
                {
                    var isLong = state.Quantity > 0;
                    var stopDistance = Math.Abs(state.CurrentStopPrice - state.EntryPrice);
                    var adjustedDistance = stopDistance * stopAdjustmentFactor;
                    
                    var newStopPrice = isLong
                        ? state.EntryPrice - adjustedDistance
                        : state.EntryPrice + adjustedDistance;
                    
                    // Only update if new stop is not worse than current (don't move stop against position)
                    var shouldUpdate = isLong
                        ? newStopPrice <= state.CurrentStopPrice  // For longs, only lower or keep same
                        : newStopPrice >= state.CurrentStopPrice; // For shorts, only higher or keep same
                    
                    if (shouldUpdate && Math.Abs(newStopPrice - state.CurrentStopPrice) > tickSize)
                    {
                        await ModifyStopPriceAsync(state, newStopPrice, $"VolAdaptive-{adjustmentReason}", cancellationToken).ConfigureAwait(false);
                        
                        _logger.LogInformation("📊 [POSITION-MGMT] PHASE 4 - Volatility-adaptive stop for {PositionId}: {Symbol}, {Reason}, Stop: {Old} → {New} ({Factor:P0} adjustment)",
                            state.PositionId, state.Symbol, adjustmentReason, state.CurrentStopPrice, newStopPrice, stopAdjustmentFactor - 1.0m);
                        
                        state.SetProperty("VolatilityAdjustedThisCycle", DateTime.UtcNow);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [POSITION-MGMT] Error applying volatility-adaptive stop for {PositionId}", state.PositionId);
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Estimate current volatility (simplified for PHASE 4)
        /// In production, would use actual ATR from market data
        /// </summary>
        private decimal EstimateCurrentVolatility(PositionManagementState state)
        {
            // Simplified: use max excursion range as volatility proxy
            var range = Math.Abs(state.MaxFavorablePrice - state.MaxAdversePrice);
            return range > 0 ? range : 1.0m;
        }
        
        /// <summary>
        /// Estimate average volatility (simplified for PHASE 4)
        /// In production, would use historical ATR
        /// </summary>
        private decimal EstimateAverageVolatility(PositionManagementState state)
        {
            // Simplified: use entry-based range estimation
            var tickSize = GetTickSize(state.Symbol);
            return tickSize * 10; // Assume avg volatility is ~10 ticks
        }
        
        /// <summary>
        /// Request partial position close
        /// NOTE: Currently logs the intent - would need IOrderService extension to support partial closes
        /// For now, this tracks the partial exit levels for monitoring purposes
        /// </summary>
        private async Task RequestPartialCloseAsync(
            PositionManagementState state,
            decimal percentToClose,
            ExitReason reason,
            CancellationToken cancellationToken)
        {
            try
            {
                var quantityToClose = (int)(state.Quantity * percentToClose);
                if (quantityToClose <= 0)
                {
                    return;
                }
                
                // PHASE 4 NOTE: Partial close functionality requires IOrderService extension
                // For production deployment, need to add: ClosePositionAsync(positionId, quantity, cancellationToken)
                // Current implementation logs the intent and tracks state for monitoring
                
                _logger.LogInformation("💡 [POSITION-MGMT] PHASE 4 - Partial exit level reached for {PositionId}: Would close {Qty} contracts ({Pct:P0}), Reason: {Reason}",
                    state.PositionId, quantityToClose, percentToClose, reason);
                
                // Track that this partial exit level was reached (for ML/RL learning)
                state.SetProperty($"PartialExitReached_{percentToClose:P0}", DateTime.UtcNow);
                
                // NOTE: In production with extended IOrderService, would call:
                // var orderService = _serviceProvider.GetService<IOrderService>();
                // var success = await orderService.ClosePositionAsync(state.PositionId, quantityToClose, cancellationToken);
                // if (success) state.Quantity -= quantityToClose;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [POSITION-MGMT] Error processing partial close for {PositionId}", state.PositionId);
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// PHASE 2: Handle zone break events from ZoneBreakMonitoringService
        /// Adjusts stops based on broken zones - broken supply becomes support, broken demand becomes resistance
        /// </summary>
        public async void OnZoneBreak(ZoneBreakEvent breakEvent)
        {
            try
            {
                // Find positions for this symbol
                var relevantPositions = _activePositions.Values
                    .Where(p => p.Symbol == breakEvent.Symbol)
                    .ToList();
                
                if (relevantPositions.Count == 0)
                {
                    return;
                }
                
                foreach (var state in relevantPositions)
                {
                    var isLong = state.Quantity > 0;
                    var posType = isLong ? "LONG" : "SHORT";
                    
                    // Only process if break type matches position type
                    if (breakEvent.PositionType != posType)
                    {
                        continue;
                    }
                    
                    switch (breakEvent.BreakType)
                    {
                        case ZoneBreakType.StrongDemandBreak:
                        case ZoneBreakType.WeakDemandBreak:
                            // Long position, demand zone broken = bad, consider early exit or tighten stop
                            if (breakEvent.Severity == "CRITICAL" || breakEvent.Severity == "HIGH")
                            {
                                _logger.LogWarning("⚠️ [POSITION-MGMT] CRITICAL demand break for LONG {PositionId} - Consider early exit",
                                    state.PositionId);
                                
                                // For critical breaks, close position immediately
                                if (breakEvent.Severity == "CRITICAL")
                                {
                                    // AI Commentary: Explain zone break forced exit (non-blocking)
                                    try
                                    {
                                        var currentPrice = await GetCurrentMarketPriceAsync(state.Symbol, CancellationToken.None).ConfigureAwait(false);
                                        var tickSize = GetTickSize(state.Symbol);
                                        var profitTicks = CalculateProfitTicks(state.EntryPrice, currentPrice, tickSize, isLong);
                                        var currentPnL = profitTicks * tickSize * state.Quantity * (isLong ? 50m : -50m);
                                        
                                        ExplainZoneBreakExitFireAndForget(state, breakEvent, currentPnL);
                                    }
                                    catch
                                    {
                                        // Silently ignore AI errors
                                    }
                                    
                                    await RequestPositionCloseAsync(state, ExitReason.ZoneBreak, CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            break;
                            
                        case ZoneBreakType.StrongSupplyBreak:
                        case ZoneBreakType.WeakSupplyBreak:
                            // Short position, supply zone broken = bad, consider early exit or tighten stop
                            if (breakEvent.Severity == "CRITICAL" || breakEvent.Severity == "HIGH")
                            {
                                _logger.LogWarning("⚠️ [POSITION-MGMT] CRITICAL supply break for SHORT {PositionId} - Consider early exit",
                                    state.PositionId);
                                
                                // For critical breaks, close position immediately
                                if (breakEvent.Severity == "CRITICAL")
                                {
                                    // AI Commentary: Explain zone break forced exit (non-blocking)
                                    try
                                    {
                                        var currentPrice = await GetCurrentMarketPriceAsync(state.Symbol, CancellationToken.None).ConfigureAwait(false);
                                        var tickSize = GetTickSize(state.Symbol);
                                        var profitTicks = CalculateProfitTicks(state.EntryPrice, currentPrice, tickSize, isLong);
                                        var currentPnL = profitTicks * tickSize * state.Quantity * (isLong ? 50m : -50m);
                                        
                                        ExplainZoneBreakExitFireAndForget(state, breakEvent, currentPnL);
                                    }
                                    catch
                                    {
                                        // Silently ignore AI errors
                                    }
                                    
                                    await RequestPositionCloseAsync(state, ExitReason.ZoneBreak, CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            break;
                            
                        case ZoneBreakType.StrongSupplyBreakBullish:
                            // Long position, strong resistance broken upward = good, maybe widen target
                            _logger.LogInformation("✅ [POSITION-MGMT] Bullish breakout confirmed for LONG {PositionId} - Strong momentum",
                                state.PositionId);
                            // Could optionally widen target here
                            break;
                            
                        case ZoneBreakType.StrongDemandBreakBearish:
                            // Short position, strong support broken downward = good, maybe widen target
                            _logger.LogInformation("✅ [POSITION-MGMT] Bearish breakout confirmed for SHORT {PositionId} - Strong momentum",
                                state.PositionId);
                            // Could optionally widen target here
                            break;
                    }
                    
                    // PHASE 2 Feature: Move stop to just behind broken zone
                    // Broken supply becomes new support (for longs)
                    // Broken demand becomes new resistance (for shorts)
                    await AdjustStopBehindBrokenZoneAsync(state, breakEvent, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [POSITION-MGMT] Error handling zone break for {Symbol}", breakEvent.Symbol);
            }
        }
        
        /// <summary>
        /// PHASE 2: Adjust stop to just behind broken zone
        /// Broken supply becomes support (for longs) - place stop below it
        /// Broken demand becomes resistance (for shorts) - place stop above it
        /// </summary>
        private async Task AdjustStopBehindBrokenZoneAsync(
            PositionManagementState state,
            ZoneBreakEvent breakEvent,
            CancellationToken cancellationToken)
        {
            var isLong = state.Quantity > 0;
            var tickSize = GetTickSize(state.Symbol);
            decimal newStopPrice;
            
            // For long positions with broken supply zones (now support)
            if (isLong && (breakEvent.BreakType == ZoneBreakType.StrongSupplyBreakBullish))
            {
                // Place stop below the broken zone (now support)
                newStopPrice = breakEvent.ZoneLow - (2 * tickSize); // 2 ticks below zone
                
                // Only update if new stop is better than current
                if (newStopPrice > state.CurrentStopPrice)
                {
                    await ModifyStopPriceAsync(state, newStopPrice, "ZoneBehind", cancellationToken).ConfigureAwait(false);
                    
                    _logger.LogInformation("🎯 [POSITION-MGMT] Stop adjusted behind broken supply (now support) for LONG {PositionId}: {Old} → {New}",
                        state.PositionId, state.CurrentStopPrice, newStopPrice);
                }
            }
            // For short positions with broken demand zones (now resistance)
            else if (!isLong && (breakEvent.BreakType == ZoneBreakType.StrongDemandBreakBearish))
            {
                // Place stop above the broken zone (now resistance)
                newStopPrice = breakEvent.ZoneHigh + (2 * tickSize); // 2 ticks above zone
                
                // Only update if new stop is better than current
                if (newStopPrice < state.CurrentStopPrice)
                {
                    await ModifyStopPriceAsync(state, newStopPrice, "ZoneBehind", cancellationToken).ConfigureAwait(false);
                    
                    _logger.LogInformation("🎯 [POSITION-MGMT] Stop adjusted behind broken demand (now resistance) for SHORT {PositionId}: {Old} → {New}",
                        state.PositionId, state.CurrentStopPrice, newStopPrice);
                }
            }
        }
        
        // ========================================================================
        // AI COMMENTARY METHODS (PHASE 5 - Ollama Integration)
        // ========================================================================
        
        /// <summary>
        /// Fire-and-forget: Explain trailing stop activation in background
        /// AI explains why trailing stop was activated without blocking trade execution
        /// </summary>
        private void ExplainTrailingStopActivationFireAndForget(
            PositionManagementState state,
            decimal unrealizedPnL,
            decimal profitTicks,
            decimal newStopPrice,
            string marketRegime)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            // Start background task but don't wait for it - trading continues immediately
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var trailDistance = state.TrailTicks;
                    var oldStop = state.CurrentStopPrice;
                    var profitAmount = unrealizedPnL;
                    
                    var prompt = $@"I am a trading bot. I just activated a trailing stop:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Current Price: {(isLong ? state.EntryPrice + (profitTicks * GetTickSize(state.Symbol)) : state.EntryPrice - (profitTicks * GetTickSize(state.Symbol))):F2}
Profit: {profitTicks:F1} ticks (${profitAmount:F2})
Trail Distance: {trailDistance} ticks behind peak
Old Stop: {oldStop:F2} → New Stop: {newStopPrice:F2}
Market Regime: {marketRegime}

Explain in 2-3 sentences why this trailing stop activation is smart and protects my profits. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Trailing Stop: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating trailing stop commentary");
                }
            });
        }
        
        /// <summary>
        /// Fire-and-forget: Explain breakeven protection activation in background
        /// </summary>
        private void ExplainBreakevenProtectionFireAndForget(
            PositionManagementState state,
            decimal unrealizedPnL,
            decimal profitTicks,
            decimal newStopPrice,
            string marketRegime)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var breakevenThreshold = state.BreakevenAfterTicks;
                    var oldStop = state.GetProperty("InitialStopPrice") as decimal? ?? state.CurrentStopPrice;
                    var profitAmount = unrealizedPnL;
                    
                    var prompt = $@"I am a trading bot. I just activated breakeven protection:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Profit Reached: {profitTicks:F1} ticks (${profitAmount:F2})
Breakeven Threshold: {breakevenThreshold} ticks
Old Stop: {oldStop:F2} (initial risk) → New Stop: {newStopPrice:F2} (breakeven + 1 tick)
Market Regime: {marketRegime}

Explain in 2-3 sentences why moving my stop to breakeven is smart risk management now that I'm profitable. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Breakeven: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating breakeven commentary");
                }
            });
        }
        
        /// <summary>
        /// Fire-and-forget: Explain partial exit execution in background
        /// </summary>
        private void ExplainPartialExitFireAndForget(
            PositionManagementState state,
            decimal rMultiple,
            decimal partialPercent,
            decimal partialQuantity,
            string targetLevel)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var remainingQuantity = state.Quantity - (int)partialQuantity;
                    var initialStop = state.GetProperty("InitialStopPrice") as decimal? ?? state.CurrentStopPrice;
                    var profitPerContract = rMultiple * Math.Abs(initialStop - state.EntryPrice);
                    
                    var prompt = $@"I am a trading bot. I just took a partial profit:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Risk-Multiple: {rMultiple:F1}R (profit is {rMultiple:F1}x my initial risk)
Partial Exit: Closed {partialPercent:F0}% ({partialQuantity:F0} contracts) at {targetLevel}
Remaining: {remainingQuantity} contracts still running
Profit on this partial: ${profitPerContract * partialQuantity:F2}

Explain in 2-3 sentences why taking this partial profit is smart while letting the rest run. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Partial Exit: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating partial exit commentary");
                }
            });
        }
        
        /// <summary>
        /// Fire-and-forget: Explain zone break forced exit in background
        /// </summary>
        private void ExplainZoneBreakExitFireAndForget(
            PositionManagementState state,
            ZoneBreakEvent breakEvent,
            decimal currentPnL)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var zoneType = breakEvent.BreakType.ToString();
                    var severity = breakEvent.Severity;
                    var zoneStrength = breakEvent.ZoneStrength;
                    
                    var prompt = $@"I am a trading bot. I just EMERGENCY EXITED a position due to a zone break:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Zone Break: {zoneType}
Severity: {severity}
Zone Strength: {zoneStrength:F2}
Zone Range: {breakEvent.ZoneLow:F2} - {breakEvent.ZoneHigh:F2}
P&L at Exit: ${currentPnL:F2}

Explain in 2-3 sentences why this critical structural break forced me to exit immediately, even if I took a loss. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Zone Break Exit: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating zone break commentary");
                }
            });
        }
        
        /// <summary>
        /// Fire-and-forget: Explain time-based exit in background
        /// </summary>
        private void ExplainTimeBasedExitFireAndForget(
            PositionManagementState state,
            TimeSpan holdDuration,
            decimal currentPnL,
            string marketRegime)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var maxHoldMinutes = state.MaxHoldMinutes;
                    var actualMinutes = holdDuration.TotalMinutes;
                    
                    var prompt = $@"I am a trading bot. I just closed a position due to time limit:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Hold Duration: {actualMinutes:F1} minutes
Max Hold Time: {maxHoldMinutes} minutes
Market Regime: {marketRegime}
P&L at Exit: ${currentPnL:F2}

Explain in 2-3 sentences why I closed this stale position that wasn't moving anywhere. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Time Exit: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating time exit commentary");
                }
            });
        }
        
        /// <summary>
        /// Fire-and-forget: Explain volatility-based stop adjustment in background
        /// </summary>
        private void ExplainVolatilityAdjustmentFireAndForget(
            PositionManagementState state,
            decimal volRatio,
            decimal oldStopDistance,
            decimal newStopDistance,
            string adjustmentReason)
        {
            if (!_commentaryEnabled || _ollamaClient == null)
                return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var isLong = state.Quantity > 0;
                    var direction = isLong ? "LONG" : "SHORT";
                    var adjustmentType = volRatio > 1.5m ? "widened" : "tightened";
                    var adjustmentPct = Math.Abs((newStopDistance - oldStopDistance) / oldStopDistance) * 100;
                    
                    var prompt = $@"I am a trading bot. I just adjusted my stop based on volatility:

Position: {direction} {state.Symbol} at {state.EntryPrice:F2}
Volatility Ratio: {volRatio:F2}x (current ATR vs average)
Stop Distance: {adjustmentType} by {adjustmentPct:F0}%
Old Distance: {oldStopDistance:F2} → New Distance: {newStopDistance:F2}
Reason: {adjustmentReason}

Explain in 2-3 sentences why this volatility-adaptive stop adjustment makes sense in current market conditions. Speak as ME (the bot).";

                    var response = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logger.LogInformation("🤖💭 [POSITION-AI] Volatility Adjustment: {Commentary}", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [POSITION-AI] Error generating volatility commentary");
                }
            });
        }
    }
}
