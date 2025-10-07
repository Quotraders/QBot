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
        private readonly ConcurrentDictionary<string, PositionManagementState> _activePositions = new();
        
        // Monitoring interval
        private const int MonitoringIntervalSeconds = 5;
        
        // Tick size for ES/MES (0.25) - used for breakeven calculations
        private const decimal EsTickSize = 0.25m;
        private const decimal NqTickSize = 0.25m;
        private const decimal DefaultTickSize = 0.25m;
        
        public UnifiedPositionManagementService(
            ILogger<UnifiedPositionManagementService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
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
                await ModifyStopPriceAsync(state, newStopPrice, "Trailing", cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("📈 [POSITION-MGMT] Trailing stop updated for {PositionId}: {Symbol}, Stop: {Old} → {New} (trail {Ticks} ticks)",
                    state.PositionId, state.Symbol, state.CurrentStopPrice, newStopPrice, state.TrailTicks);
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
    }
}
