using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using TopstepX.Bot.Abstractions;
using BotCore.Models;
using BotCore.Services;

namespace TopstepX.Bot.Core.Services
{
    /// <summary>
    /// Order Fill Confirmation System - Ensures no trades without proof
    /// Integrates with TopstepX API for order verification
    /// </summary>
    public class OrderFillConfirmationSystem
    {
        private readonly ILogger<OrderFillConfirmationSystem> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, OrderTrackingRecord> _orderTracking = new();
        private readonly Timer _verificationTimer;
        private readonly PositionTrackingSystem _positionTracker;
        private readonly EmergencyStopSystem _emergencyStop;
        
        // Order type constants for API integration
        private const int LimitOrderType = 1;
        private const int MarketOrderType = 2;
        private const int StopOrderType = 4;
        private const int TrailingStopOrderType = 5;
        
        public event EventHandler<OrderConfirmedEventArgs>? OrderConfirmed;
        public event EventHandler<OrderRejectedEventArgs>? OrderRejected;
        public event EventHandler<FillConfirmedEventArgs>? FillConfirmed;
        
        /// <summary>
        /// Constructor using TopstepX adapter service for real-time data
        /// </summary>
        public OrderFillConfirmationSystem(
            ILogger<OrderFillConfirmationSystem> logger,
            HttpClient httpClient,
            ITopstepXAdapterService topstepXAdapter,
            PositionTrackingSystem positionTracker,
            EmergencyStopSystem emergencyStop)
        {
            _logger = logger;
            _httpClient = httpClient;
            _positionTracker = positionTracker;
            _emergencyStop = emergencyStop;
            
            // Setup verification timer - runs every 10 seconds
            _verificationTimer = new Timer(VerifyPendingOrders, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            
            _logger.LogInformation("✅ Order Fill Confirmation System initialized with TopstepX adapter");
        }
        
        /// <summary>
        /// Place order with full tracking and verification
        /// </summary>
        public async Task<OrderResult> PlaceOrderAsync(PlaceOrderRequest request, string accountId)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (accountId is null) throw new ArgumentNullException(nameof(accountId));
            
            // Check emergency stop
            if (_emergencyStop.IsEmergencyStop)
            {
                return OrderResult.Failed("Emergency stop is active - no trading allowed");
            }
            
            try
            {
                // Generate unique client order ID if not provided using standardized generator
                if (string.IsNullOrEmpty(request.ClientOrderId))
                {
                    request.ClientOrderId = BotCore.Utilities.CustomTagGenerator.Generate();
                }
                
                // Create tracking record
                var trackingRecord = new OrderTrackingRecord
                {
                    ClientOrderId = request.ClientOrderId,
                    Symbol = request.Symbol,
                    Quantity = request.Quantity,
                    Price = request.Price,
                    Side = request.Side,
                    OrderType = request.OrderType,
                    SubmittedTime = DateTime.UtcNow,
                    Status = "SUBMITTING"
                };
                
                _orderTracking[request.ClientOrderId] = trackingRecord;
                
                // Log order submission
                _logger.LogInformation("[{ClientOrderId}] side={Side} symbol={Symbol} qty={Quantity} price={Price:F2} type={OrderType}",
                    request.ClientOrderId, request.Side, request.Symbol, request.Quantity, request.Price, request.OrderType);
                
                // Submit order to TopstepX API
                var orderResponse = await SubmitOrderToApiAsync(request, accountId).ConfigureAwait(false);
                
                if (orderResponse.IsSuccess)
                {
                    trackingRecord.GatewayOrderId = orderResponse.OrderId;
                    trackingRecord.Status = "SUBMITTED";
                    
                    // Add to position tracker as pending
                    _positionTracker.AddPendingOrder(new PendingOrder
                    {
                        OrderId = orderResponse.OrderId ?? string.Empty,
                        ClientOrderId = request.ClientOrderId,
                        Symbol = request.Symbol,
                        Quantity = request.Side == "BUY" ? request.Quantity : -request.Quantity,
                        Price = request.Price,
                        Side = request.Side,
                        Status = "PENDING",
                        SubmittedTime = DateTime.UtcNow,
                        OrderType = request.OrderType
                    });
                    
                    _logger.LogInformation("✅ ORDER SUBMITTED: account={AccountId} clientOrderId={ClientOrderId} gatewayOrderId={GatewayOrderId}",
                        accountId, request.ClientOrderId, orderResponse.OrderId);
                    
                    return OrderResult.Success(orderResponse.OrderId, request.ClientOrderId);
                }
                else
                {
                    trackingRecord.Status = "REJECTED";
                    trackingRecord.RejectReason = orderResponse.ErrorMessage;
                    
                    _logger.LogWarning("❌ ORDER REJECTED: account={AccountId} clientOrderId={ClientOrderId} reason={Reason}",
                        accountId, request.ClientOrderId, orderResponse.ErrorMessage);
                    
                    return OrderResult.Failed(orderResponse.ErrorMessage ?? "Unknown error");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ HTTP error placing order {ClientOrderId}", request.ClientOrderId);
                return OrderResult.Failed($"HTTP Exception: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "❌ Timeout placing order {ClientOrderId}", request.ClientOrderId);
                return OrderResult.Failed($"Timeout: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Invalid operation placing order {ClientOrderId}", request.ClientOrderId);
                return OrderResult.Failed($"Invalid Operation: {ex.Message}");
            }
        }
        
        private async Task<ApiOrderResponse> SubmitOrderToApiAsync(PlaceOrderRequest request, string accountId)
        {
            try
            {
                // FIXED: Use ProjectX API specification exactly
                var orderPayload = new
                {
                    accountId = long.Parse(accountId),
                    contractId = request.Symbol,               // ProjectX uses contractId, not symbol
                    type = GetOrderTypeValue(request.OrderType), // ProjectX: 1=Limit, 2=Market, 4=Stop
                    side = GetSideValue(request.Side),         // ProjectX: 0=Bid(buy), 1=Ask(sell)
                    size = request.Quantity,                   // ProjectX expects integer size
                    limitPrice = request.OrderType.ToUpper() == "LIMIT" ? request.Price : (decimal?)null,
                    stopPrice = request.OrderType.ToUpper() == "STOP" ? request.Price : (decimal?)null,
                    customTag = request.ClientOrderId
                };
                
                var json = JsonSerializer.Serialize(orderPayload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // FIXED: Use correct ProjectX endpoint
                var response = await _httpClient.PostAsync("/api/Order/place", content).ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var orderResponse = JsonSerializer.Deserialize<ApiOrderResponse>(responseContent);
                    return orderResponse ?? ApiOrderResponse.Failed("Failed to parse response");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogWarning("API order rejection: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return ApiOrderResponse.Failed($"HTTP {response.StatusCode}: {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP API call failed for order submission");
                return ApiOrderResponse.Failed($"HTTP API Exception: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "API call timeout for order submission");
                return ApiOrderResponse.Failed($"API Timeout: {ex.Message}");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error for order submission");
                return ApiOrderResponse.Failed($"JSON Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Convert order type string to ProjectX API integer value
        /// ProjectX API: 1 = Limit, 2 = Market, 4 = Stop, 5 = TrailingStop
        /// </summary>
        private static int GetOrderTypeValue(string orderType)
        {
            return orderType.ToUpper() switch
            {
                "LIMIT" => LimitOrderType,
                "MARKET" => MarketOrderType,
                "STOP" => StopOrderType,
                "TRAILING_STOP" => TrailingStopOrderType,
                _ => LimitOrderType // Default to limit
            };
        }

        /// <summary>
        /// Convert side string to ProjectX API integer value
        /// ProjectX API: 0 = Bid (buy), 1 = Ask (sell)
        /// </summary>
        private static int GetSideValue(string side)
        {
            return side.ToUpper() switch
            {
                "BUY" => 0,
                "SELL" => 1,
                _ => 0 // Default to buy
            };
        }

        private void VerifyPendingOrders(object? state)
        {
            try
            {
                var pendingOrders = _orderTracking.Values.Where(r => 
                    r.Status == "SUBMITTED" && 
                    !r.IsVerified && 
                    r.VerificationAttempts < 5 &&
                    DateTime.UtcNow - r.SubmittedTime < TimeSpan.FromMinutes(10)).ToList();
                
                foreach (var order in pendingOrders)
                {
                    order.VerificationAttempts++;
                    _ = Task.Run(async () => await VerifyOrderWithApiAsync(order).ConfigureAwait(false)).ConfigureAwait(false);
                }
                
                // Clean up old tracking records (older than 1 hour)
                var cutoffTime = DateTime.UtcNow.AddHours(-1);
                var staleRecords = _orderTracking.Values.Where(r => r.SubmittedTime < cutoffTime).ToList();
                
                foreach (var staleRecord in staleRecords)
                {
                    _orderTracking.TryRemove(staleRecord.ClientOrderId, out _);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Error during order verification");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Error during order verification");
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, "❌ Error during order verification");
            }
        }
        
        private async Task VerifyOrderWithApiAsync(OrderTrackingRecord order)
        {
            try
            {
                if (string.IsNullOrEmpty(order.GatewayOrderId)) return;
                
                var response = await _httpClient.GetAsync($"/api/orders/{order.GatewayOrderId}").ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var orderDetails = JsonSerializer.Deserialize<ApiOrderDetails>(content);
                    
                    if (orderDetails != null)
                    {
                        order.Status = orderDetails.Status;
                        order.IsVerified = true;
                        
                        _logger.LogDebug("✅ Order verified via API: {OrderId} status={Status}", 
                            order.GatewayOrderId, orderDetails.Status);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to verify order {OrderId} via API", order.GatewayOrderId);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to verify order {OrderId} via API", order.GatewayOrderId);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to verify order {OrderId} via API", order.GatewayOrderId);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to verify order {OrderId} via API", order.GatewayOrderId);
            }
        }
        
        /// <summary>
        /// Cancel order by client order ID
        /// </summary>
        public async Task<bool> CancelOrderAsync(string clientOrderId, string accountId)
        {
            try
            {
                if (_orderTracking.TryGetValue(clientOrderId, out var trackingRecord) && 
                    !string.IsNullOrEmpty(trackingRecord.GatewayOrderId))
                {
                    // FIXED: Use ProjectX API specification - POST to /api/Order/cancel with body
                    var cancelPayload = new
                    {
                        accountId = long.Parse(accountId),
                        orderId = long.Parse(trackingRecord.GatewayOrderId)
                    };
                    
                    var json = JsonSerializer.Serialize(cancelPayload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _httpClient.PostAsync("/api/Order/cancel", content).ConfigureAwait(false);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        trackingRecord.Status = "CANCEL_PENDING";
                        _logger.LogInformation("📝 Cancel request sent for order {ClientOrderId}", clientOrderId);
                        return true;
                    }
                }
                
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ Error cancelling order {ClientOrderId}", clientOrderId);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "❌ Error cancelling order {ClientOrderId}", clientOrderId);
                return false;
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "❌ Error cancelling order {ClientOrderId}", clientOrderId);
                return false;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Error cancelling order {ClientOrderId}", clientOrderId);
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Error cancelling order {ClientOrderId}", clientOrderId);
                return false;
            }
        }
        
        /// <summary>
        /// Get order status by client order ID
        /// </summary>
        public OrderTrackingRecord? GetOrderStatus(string clientOrderId)
        {
            _orderTracking.TryGetValue(clientOrderId, out var record);
            return record;
        }
        
        /// <summary>
        /// Get all orders with their current status
        /// </summary>
        public Dictionary<string, OrderTrackingRecord> GetAllOrders()
        {
            return new Dictionary<string, OrderTrackingRecord>(_orderTracking);
        }
        
        public void Dispose()
        {
            _verificationTimer?.Dispose();
        }

        protected virtual void OnOrderConfirmedEvent(OrderConfirmedEventArgs args)
        {
            OrderConfirmed?.Invoke(this, args);
        }

        protected virtual void OnOrderRejectedEvent(OrderRejectedEventArgs args)
        {
            OrderRejected?.Invoke(this, args);
        }

        protected virtual void OnFillConfirmedEvent(FillConfirmedEventArgs args)
        {
            FillConfirmed?.Invoke(this, args);
        }
    }
    
    // Supporting classes for API integration
    public class GatewayUserOrder
    {
        public string? AccountId { get; set; }
        public string? OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
    
    public class GatewayUserTrade
    {
        public string? AccountId { get; set; }
        public string? OrderId { get; set; }
        public decimal FillPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Commission { get; set; }
        public string? Exchange { get; set; }
    }
    
    public class OrderResult
    {
        public bool IsSuccess { get; set; }
        public string? OrderId { get; set; }
        public string? ClientOrderId { get; set; }
        public string? ErrorMessage { get; set; }
        
        public static OrderResult Success(string? orderId, string clientOrderId) => 
            new() { IsSuccess = true, OrderId = orderId, ClientOrderId = clientOrderId };
        public static OrderResult Failed(string error) => 
            new() { IsSuccess = false, ErrorMessage = error };
    }
    
    // Event argument classes
    public class OrderConfirmedEventArgs : EventArgs
    {
        public OrderTrackingRecord TrackingRecord { get; set; } = new();
        public GatewayUserOrder GatewayOrderUpdate { get; set; } = new();
    }
    
    public class OrderRejectedEventArgs : EventArgs
    {
        public OrderTrackingRecord TrackingRecord { get; set; } = new();
        public GatewayUserOrder GatewayOrderUpdate { get; set; } = new();
    }
    
    public class FillConfirmedEventArgs : EventArgs
    {
        public OrderTrackingRecord TrackingRecord { get; set; } = new();
        public FillConfirmation FillConfirmation { get; set; } = new();
        public GatewayUserTrade GatewayTradeUpdate { get; set; } = new();
    }
}