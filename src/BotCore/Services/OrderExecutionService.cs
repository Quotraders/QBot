using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Production order execution service implementing IOrderService
    /// Integrates with TopstepX adapter for broker communication
    /// Manages order lifecycle and position tracking
    /// </summary>
    public sealed class OrderExecutionService : TradingBot.Abstractions.IOrderService
    {
        private readonly ILogger<OrderExecutionService> _logger;
        private readonly ITopstepXAdapterService _topstepAdapter;
        private readonly OrderExecutionMetrics? _metrics;
        
        // Position tracking: maps position ID (symbol-based) to position data
        private readonly ConcurrentDictionary<string, TradingBot.Abstractions.Position> _positions = new();
        
        // Order tracking: maps order ID to order data
        private readonly ConcurrentDictionary<string, TradingBot.Abstractions.Order> _orders = new();
        
        // Symbol to position ID mapping for lookups
        private readonly ConcurrentDictionary<string, string> _symbolToPositionId = new();
        
        // Configuration snapshot tracking
        private const string DefaultConfigSnapshotId = "default-snapshot";
        
        // PHASE 1: Event Infrastructure for fill notifications
        public event EventHandler<OrderFillEventArgs>? OrderFilled;
        public event EventHandler<OrderPlacedEventArgs>? OrderPlaced;
        public event EventHandler<OrderRejectedEventArgs>? OrderRejected;
        
        public OrderExecutionService(
            ILogger<OrderExecutionService> logger,
            ITopstepXAdapterService topstepAdapter,
            OrderExecutionMetrics? metrics = null)
        {
            _logger = logger;
            _topstepAdapter = topstepAdapter;
            _metrics = metrics;
        }
        
        // ========================================================================
        // HEALTH AND STATUS METHODS
        // ========================================================================
        
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return await _topstepAdapter.IsHealthyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking order service health");
                return false;
            }
        }
        
        public Task<string> GetStatusAsync()
        {
            try
            {
                var isConnected = _topstepAdapter.IsConnected;
                var health = _topstepAdapter.ConnectionHealth;
                var positionCount = _positions.Count;
                var activeOrderCount = _orders.Count(o => o.Value.Status == TradingBot.Abstractions.OrderStatus.Pending || o.Value.Status == TradingBot.Abstractions.OrderStatus.PartiallyFilled);
                
                return Task.FromResult($"Connected: {isConnected}, Health: {health:F1}%, Positions: {positionCount}, Active Orders: {activeOrderCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order service status");
                return Task.FromResult($"Error: {ex.Message}");
            }
        }
        
        // ========================================================================
        // POSITION QUERY METHODS
        // ========================================================================
        
        public Task<List<TradingBot.Abstractions.Position>> GetPositionsAsync()
        {
            return Task.FromResult(_positions.Values.ToList());
        }
        
        public Task<TradingBot.Abstractions.Position?> GetPositionAsync(string positionId)
        {
            _positions.TryGetValue(positionId, out var position);
            return Task.FromResult(position);
        }
        
        // ========================================================================
        // POSITION MANAGEMENT METHODS
        // ========================================================================
        
        /// <summary>
        /// Close entire position via TopstepX API
        /// </summary>
        public async Task<bool> ClosePositionAsync(string positionId)
        {
            try
            {
                if (!_positions.TryGetValue(positionId, out var position))
                {
                    _logger.LogWarning("Position {PositionId} not found for full close", positionId);
                    return false;
                }
                
                _logger.LogInformation("📉 [ORDER-EXEC] Closing full position {PositionId}: {Symbol} {Quantity} contracts",
                    positionId, position.Symbol, position.Quantity);
                
                // Call TopstepX API to close position
                var success = await _topstepAdapter.ClosePositionAsync(position.Symbol, position.Quantity, CancellationToken.None).ConfigureAwait(false);
                
                if (success)
                {
                    // Remove from tracking
                    _positions.TryRemove(positionId, out _);
                    _symbolToPositionId.TryRemove(position.Symbol, out _);
                    
                    _logger.LogInformation("✅ [ORDER-EXEC] Position {PositionId} closed successfully via TopstepX API", positionId);
                    return true;
                }
                
                _logger.LogError("❌ [ORDER-EXEC] Failed to close position {PositionId} via TopstepX API", positionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing position {PositionId}", positionId);
                return false;
            }
        }
        
        /// <summary>
        /// Close partial position (for scaling out) via TopstepX API
        /// </summary>
        public async Task<bool> ClosePositionAsync(string positionId, int quantity, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_positions.TryGetValue(positionId, out var position))
                {
                    _logger.LogWarning("Position {PositionId} not found for partial close", positionId);
                    return false;
                }
                
                if (quantity <= 0 || quantity > position.Quantity)
                {
                    _logger.LogError("Invalid quantity {Qty} for partial close of position {PositionId} (current: {Current})",
                        quantity, positionId, position.Quantity);
                    return false;
                }
                
                _logger.LogInformation("📉 [ORDER-EXEC] Partial close position {PositionId}: {Symbol} closing {Qty} of {Total} contracts",
                    positionId, position.Symbol, quantity, position.Quantity);
                
                // Call TopstepX API to close partial position
                var success = await _topstepAdapter.ClosePositionAsync(position.Symbol, quantity, cancellationToken).ConfigureAwait(false);
                
                if (success)
                {
                    // Update position quantity
                    var remainingQuantity = position.Quantity - quantity;
                    
                    if (remainingQuantity > 0)
                    {
                        position.Quantity = remainingQuantity;
                        _logger.LogInformation("✅ [ORDER-EXEC] Partial close successful for {PositionId} via TopstepX API: {Qty} contracts closed, {Remaining} remaining",
                            positionId, quantity, remainingQuantity);
                    }
                    else
                    {
                        // Position fully closed
                        _positions.TryRemove(positionId, out _);
                        _symbolToPositionId.TryRemove(position.Symbol, out _);
                        _logger.LogInformation("✅ [ORDER-EXEC] Position {PositionId} fully closed via partial close (TopstepX API)", positionId);
                    }
                    
                    return true;
                }
                
                _logger.LogError("❌ [ORDER-EXEC] Failed to execute partial close for position {PositionId} via TopstepX API", positionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in partial close for position {PositionId}", positionId);
                return false;
            }
        }
        
        /// <summary>
        /// Modify stop loss for a position via TopstepX API
        /// </summary>
        public async Task<bool> ModifyStopLossAsync(string positionId, decimal stopPrice)
        {
            try
            {
                if (!_positions.TryGetValue(positionId, out var position))
                {
                    _logger.LogWarning("Position {PositionId} not found for stop loss modification", positionId);
                    return false;
                }
                
                _logger.LogInformation("🛡️ [ORDER-EXEC] Modifying stop loss for {PositionId}: {Symbol} from {Old} to {New}",
                    positionId, position.Symbol, position.StopLoss, stopPrice);
                
                // Call TopstepX API to modify stop loss
                var success = await _topstepAdapter.ModifyStopLossAsync(position.Symbol, stopPrice, CancellationToken.None).ConfigureAwait(false);
                
                if (success)
                {
                    // Update stop loss in position tracking
                    position.StopLoss = stopPrice;
                    
                    _logger.LogInformation("✅ [ORDER-EXEC] Stop loss updated for {PositionId} via TopstepX API", positionId);
                    return true;
                }
                
                _logger.LogError("❌ [ORDER-EXEC] Failed to modify stop loss for {PositionId} via TopstepX API", positionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error modifying stop loss for position {PositionId}", positionId);
                return false;
            }
        }
        
        /// <summary>
        /// Modify take profit for a position via TopstepX API
        /// </summary>
        public async Task<bool> ModifyTakeProfitAsync(string positionId, decimal takeProfitPrice)
        {
            try
            {
                if (!_positions.TryGetValue(positionId, out var position))
                {
                    _logger.LogWarning("Position {PositionId} not found for take profit modification", positionId);
                    return false;
                }
                
                _logger.LogInformation("🎯 [ORDER-EXEC] Modifying take profit for {PositionId}: {Symbol} from {Old} to {New}",
                    positionId, position.Symbol, position.TakeProfit, takeProfitPrice);
                
                // Call TopstepX API to modify take profit
                var success = await _topstepAdapter.ModifyTakeProfitAsync(position.Symbol, takeProfitPrice, CancellationToken.None).ConfigureAwait(false);
                
                if (success)
                {
                    // Update take profit in position tracking
                    position.TakeProfit = takeProfitPrice;
                    
                    _logger.LogInformation("✅ [ORDER-EXEC] Take profit updated for {PositionId} via TopstepX API", positionId);
                    return true;
                }
                
                _logger.LogError("❌ [ORDER-EXEC] Failed to modify take profit for {PositionId} via TopstepX API", positionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error modifying take profit for position {PositionId}", positionId);
                return false;
            }
        }
        
        // ========================================================================
        // ORDER PLACEMENT METHODS
        // ========================================================================
        
        public Task<string> PlaceMarketOrderAsync(string symbol, string side, int quantity, string? tag = null)
        {
            try
            {
                _logger.LogInformation("📈 [ORDER-EXEC] Placing market order: {Symbol} {Side} {Qty} contracts (tag: {Tag})",
                    symbol, side, quantity, tag ?? "none");
                
                // Generate order ID
                var orderId = $"ORD-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{symbol}";
                
                // Create order record
                var order = new TradingBot.Abstractions.Order
                {
                    Id = orderId,
                    Symbol = symbol,
                    Side = side,
                    Quantity = quantity,
                    FilledQuantity = 0,
                    OrderType = "MARKET",
                    Status = TradingBot.Abstractions.OrderStatus.Pending,
                    Tag = tag,
                    ConfigSnapshotId = DefaultConfigSnapshotId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                
                _orders.TryAdd(orderId, order);
                
                // PHASE 1: Record metrics for order placement
                _metrics?.RecordOrderPlaced(symbol);
                
                // PHASE 1: Fire OrderPlaced event
                OrderPlaced?.Invoke(this, new OrderPlacedEventArgs
                {
                    OrderId = orderId,
                    Symbol = symbol,
                    Quantity = quantity,
                    Price = order.Price ?? 0,
                    OrderType = "MARKET",
                    Timestamp = DateTime.UtcNow
                });
                
                // For closing orders, we don't use TopstepX PlaceOrderAsync
                // We just mark them as filled since TopstepX handles the closing
                if (tag?.StartsWith("CLOSE-") == true || tag?.StartsWith("PARTIAL-CLOSE-") == true)
                {
                    order.Status = TradingBot.Abstractions.OrderStatus.Filled;
                    order.FilledQuantity = quantity;
                    order.UpdatedAt = DateTimeOffset.UtcNow;
                    
                    _logger.LogInformation("✅ [ORDER-EXEC] Close order {OrderId} marked as filled", orderId);
                    return Task.FromResult(orderId);
                }
                
                // For entry orders, integrate with TopstepX adapter
                // Note: This would require bracket parameters (stop/target) which aren't available in this method
                // Entry orders should be placed through higher-level services that provide full bracket info
                
                _logger.LogInformation("✅ [ORDER-EXEC] Order {OrderId} created", orderId);
                return Task.FromResult(orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing market order for {Symbol}", symbol);
                
                // PHASE 1: Record rejection in metrics
                _metrics?.RecordOrderRejected(symbol, ex.Message);
                
                return Task.FromResult(string.Empty);
            }
        }
        
        public Task<string> PlaceLimitOrderAsync(string symbol, string side, int quantity, decimal price, string? tag = null)
        {
            try
            {
                _logger.LogInformation("📈 [ORDER-EXEC] Placing limit order: {Symbol} {Side} {Qty} @ {Price} (tag: {Tag})",
                    symbol, side, quantity, price, tag ?? "none");
                
                var orderId = $"ORD-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{symbol}";
                
                var order = new TradingBot.Abstractions.Order
                {
                    Id = orderId,
                    Symbol = symbol,
                    Side = side,
                    Quantity = quantity,
                    FilledQuantity = 0,
                    Price = price,
                    OrderType = "LIMIT",
                    Status = TradingBot.Abstractions.OrderStatus.Pending,
                    Tag = tag,
                    ConfigSnapshotId = DefaultConfigSnapshotId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                
                _orders.TryAdd(orderId, order);
                
                _logger.LogInformation("✅ [ORDER-EXEC] Limit order {OrderId} created", orderId);
                return Task.FromResult(orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing limit order for {Symbol}", symbol);
                return Task.FromResult(string.Empty);
            }
        }
        
        public Task<string> PlaceStopOrderAsync(string symbol, string side, int quantity, decimal stopPrice, string? tag = null)
        {
            try
            {
                _logger.LogInformation("📈 [ORDER-EXEC] Placing stop order: {Symbol} {Side} {Qty} @ {StopPrice} (tag: {Tag})",
                    symbol, side, quantity, stopPrice, tag ?? "none");
                
                var orderId = $"ORD-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{symbol}";
                
                var order = new TradingBot.Abstractions.Order
                {
                    Id = orderId,
                    Symbol = symbol,
                    Side = side,
                    Quantity = quantity,
                    FilledQuantity = 0,
                    StopPrice = stopPrice,
                    OrderType = "STOP",
                    Status = TradingBot.Abstractions.OrderStatus.Pending,
                    Tag = tag,
                    ConfigSnapshotId = DefaultConfigSnapshotId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                
                _orders.TryAdd(orderId, order);
                
                _logger.LogInformation("✅ [ORDER-EXEC] Stop order {OrderId} created", orderId);
                return Task.FromResult(orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing stop order for {Symbol}", symbol);
                return Task.FromResult(string.Empty);
            }
        }
        
        // ========================================================================
        // ORDER MANAGEMENT METHODS
        // ========================================================================
        
        public async Task<bool> CancelOrderAsync(string orderId)
        {
            try
            {
                if (!_orders.TryGetValue(orderId, out var order))
                {
                    _logger.LogWarning("Order {OrderId} not found for cancellation", orderId);
                    return false;
                }
                
                if (order.Status == TradingBot.Abstractions.OrderStatus.Filled || order.Status == TradingBot.Abstractions.OrderStatus.Cancelled)
                {
                    _logger.LogWarning("Cannot cancel order {OrderId} with status {Status}", orderId, order.Status);
                    return false;
                }
                
                _logger.LogInformation("❌ [ORDER-EXEC] Cancelling order {OrderId}", orderId);
                
                // Call TopstepX API to cancel order
                var success = await _topstepAdapter.CancelOrderAsync(orderId, CancellationToken.None).ConfigureAwait(false);
                
                if (success)
                {
                    order.Status = TradingBot.Abstractions.OrderStatus.Cancelled;
                    order.UpdatedAt = DateTimeOffset.UtcNow;
                    
                    _logger.LogInformation("✅ [ORDER-EXEC] Order {OrderId} cancelled via TopstepX API", orderId);
                    return true;
                }
                
                _logger.LogError("❌ [ORDER-EXEC] Failed to cancel order {OrderId} via TopstepX API", orderId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
                return false;
            }
        }
        
        public Task<bool> ModifyOrderAsync(string orderId, int? quantity = null, decimal? price = null)
        {
            try
            {
                if (!_orders.TryGetValue(orderId, out var order))
                {
                    _logger.LogWarning("Order {OrderId} not found for modification", orderId);
                    return Task.FromResult(false);
                }
                
                if (order.Status != TradingBot.Abstractions.OrderStatus.Pending)
                {
                    _logger.LogWarning("Cannot modify order {OrderId} with status {Status}", orderId, order.Status);
                    return Task.FromResult(false);
                }
                
                _logger.LogInformation("✏️ [ORDER-EXEC] Modifying order {OrderId}", orderId);
                
                if (quantity.HasValue)
                {
                    order.Quantity = quantity.Value;
                    _logger.LogInformation("  → Quantity updated to {Qty}", quantity.Value);
                }
                
                if (price.HasValue)
                {
                    order.Price = price.Value;
                    _logger.LogInformation("  → Price updated to {Price}", price.Value);
                }
                
                order.UpdatedAt = DateTimeOffset.UtcNow;
                
                _logger.LogInformation("✅ [ORDER-EXEC] Order {OrderId} modified", orderId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error modifying order {OrderId}", orderId);
                return Task.FromResult(false);
            }
        }
        
        public Task<TradingBot.Abstractions.OrderStatus> GetOrderStatusAsync(string orderId)
        {
            if (_orders.TryGetValue(orderId, out var order))
            {
                return Task.FromResult(order.Status);
            }
            
            _logger.LogWarning("Order {OrderId} not found", orderId);
            return Task.FromResult(TradingBot.Abstractions.OrderStatus.Rejected);
        }
        
        public Task<List<TradingBot.Abstractions.Order>> GetActiveOrdersAsync()
        {
            var activeOrders = _orders.Values
                .Where(o => o.Status == TradingBot.Abstractions.OrderStatus.Pending || o.Status == TradingBot.Abstractions.OrderStatus.PartiallyFilled)
                .ToList();
            
            return Task.FromResult(activeOrders);
        }
        
        // ========================================================================
        // HELPER METHODS
        // ========================================================================
        
        /// <summary>
        /// Register a position in tracking (called when trade is opened)
        /// </summary>
        public void RegisterPosition(string positionId, string symbol, string side, int quantity, decimal entryPrice, decimal? stopLoss = null, decimal? takeProfit = null)
        {
            var position = new TradingBot.Abstractions.Position
            {
                Id = positionId,
                Symbol = symbol,
                Side = side,
                Quantity = quantity,
                AveragePrice = entryPrice,
                UnrealizedPnL = 0,
                RealizedPnL = 0,
                ConfigSnapshotId = DefaultConfigSnapshotId,
                OpenTime = DateTimeOffset.UtcNow,
                StopLoss = stopLoss,
                TakeProfit = takeProfit
            };
            
            _positions.TryAdd(positionId, position);
            _symbolToPositionId[symbol] = positionId;
            
            _logger.LogInformation("📊 [ORDER-EXEC] Position registered: {PositionId} - {Symbol} {Side} {Qty} @ {Price}",
                positionId, symbol, side, quantity, entryPrice);
        }
        
        /// <summary>
        /// Update position P&L (called by position management)
        /// </summary>
        public void UpdatePositionPnL(string positionId, decimal unrealizedPnL, decimal realizedPnL)
        {
            if (_positions.TryGetValue(positionId, out var position))
            {
                position.UnrealizedPnL = unrealizedPnL;
                position.RealizedPnL = realizedPnL;
            }
        }
        
        /// <summary>
        /// PHASE 1: Get execution metrics summary for a symbol
        /// </summary>
        public OrderExecutionMetricsSummary? GetMetricsSummary(string symbol)
        {
            return _metrics?.GetMetricsSummary(symbol);
        }
        
        // ========================================================================
        // PHASE 1: EVENT PUBLISHING METHODS
        // ========================================================================
        
        /// <summary>
        /// Notify subscribers that an order was filled
        /// Called by TopstepXAdapterService when fill events are received
        /// </summary>
        public void OnOrderFillReceived(FillEventData fillData)
        {
            try
            {
                _logger.LogInformation("📥 [FILL-EVENT] Received fill notification: {OrderId} {Symbol} {Qty} @ {Price}",
                    fillData.OrderId, fillData.Symbol, fillData.Quantity, fillData.FillPrice);
                
                var isPartialFill = false;
                
                // Update order tracking if order exists
                if (_orders.TryGetValue(fillData.OrderId, out var order))
                {
                    order.FilledQuantity += fillData.Quantity;
                    isPartialFill = order.FilledQuantity < order.Quantity;
                    order.Status = isPartialFill
                        ? TradingBot.Abstractions.OrderStatus.PartiallyFilled
                        : TradingBot.Abstractions.OrderStatus.Filled;
                    order.UpdatedAt = DateTimeOffset.UtcNow;
                    
                    _logger.LogInformation("✅ [ORDER-UPDATE] Order {OrderId} updated: {FilledQty}/{TotalQty} filled, status: {Status}",
                        fillData.OrderId, order.FilledQuantity, order.Quantity, order.Status);
                    
                    // PHASE 1: Record execution latency
                    _metrics?.RecordExecutionLatency(fillData.OrderId, fillData.Symbol, 
                        order.CreatedAt.DateTime, fillData.Timestamp);
                    
                    // PHASE 1: Record slippage if we have expected price
                    if (order.Price.HasValue && order.Price.Value > 0)
                    {
                        _metrics?.RecordSlippage(fillData.OrderId, fillData.Symbol, 
                            order.Price.Value, fillData.FillPrice, fillData.Quantity);
                    }
                }
                
                // PHASE 1: Record fill in metrics
                _metrics?.RecordOrderFilled(fillData.Symbol, isPartialFill);
                
                // Update position tracking
                UpdatePositionFromFill(fillData);
                
                // Raise event for external subscribers
                OrderFilled?.Invoke(this, new OrderFillEventArgs
                {
                    OrderId = fillData.OrderId,
                    Symbol = fillData.Symbol,
                    Quantity = fillData.Quantity,
                    FillPrice = fillData.FillPrice,
                    Commission = fillData.Commission,
                    Timestamp = fillData.Timestamp,
                    Exchange = fillData.Exchange,
                    LiquidityType = fillData.LiquidityType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing fill event for order {OrderId}", fillData.OrderId);
            }
        }
        
        private void UpdatePositionFromFill(FillEventData fillData)
        {
            // Check if this is an existing position
            if (_symbolToPositionId.TryGetValue(fillData.Symbol, out var positionId) &&
                _positions.TryGetValue(positionId, out var position))
            {
                // Update existing position
                var oldQuantity = position.Quantity;
                var oldAvgPrice = position.AveragePrice;
                
                // Calculate new average price for position additions
                if (fillData.Quantity > 0)
                {
                    var totalCost = (oldQuantity * oldAvgPrice) + (fillData.Quantity * fillData.FillPrice);
                    position.Quantity = oldQuantity + fillData.Quantity;
                    position.AveragePrice = position.Quantity > 0 ? totalCost / position.Quantity : fillData.FillPrice;
                }
                
                _logger.LogDebug("Position {PositionId} updated from fill: {OldQty} -> {NewQty} contracts, avg price: ${AvgPrice:F2}",
                    positionId, oldQuantity, position.Quantity, position.AveragePrice);
            }
        }
    }
    
    // ========================================================================
    // PHASE 1: EVENT DATA CLASSES
    // ========================================================================
    
    /// <summary>
    /// Event args for order fill notifications from TopstepX
    /// </summary>
    public class OrderFillEventArgs : EventArgs
    {
        public string OrderId { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal FillPrice { get; init; }
        public decimal Commission { get; init; }
        public DateTime Timestamp { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string LiquidityType { get; init; } = string.Empty;
    }
    
    /// <summary>
    /// Event args for order placement
    /// </summary>
    public class OrderPlacedEventArgs : EventArgs
    {
        public string OrderId { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal Price { get; init; }
        public string OrderType { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
    }
    
    /// <summary>
    /// Event args for order rejection
    /// </summary>
    public class OrderRejectedEventArgs : EventArgs
    {
        public string OrderId { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
    }
    
    /// <summary>
    /// Fill event data from TopstepX SDK
    /// </summary>
    public class FillEventData
    {
        public string OrderId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal FillPrice { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Commission { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string LiquidityType { get; set; } = string.Empty;
    }
}
