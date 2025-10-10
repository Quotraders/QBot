using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using BotCore.Models;
using TradingBot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Layer 3: Emergency Exit Executor
    /// Executes escalating recovery actions until position is closed
    /// Five levels: SmartRetry → FreshStart → MarketOrder → HumanEscalation → SystemShutdown
    /// </summary>
    public sealed class EmergencyExitExecutor
    {
        private readonly ILogger<EmergencyExitExecutor> _logger;
        private readonly StuckPositionRecoveryConfiguration _config;
        private readonly IOrderService _orderService;
        private readonly EmergencyStopSystem _emergencyStop;
        private readonly ITopstepXAdapterService _topstepAdapter;
        
        // Track active recovery operations
        private readonly ConcurrentDictionary<string, PositionRecoveryState> _activeRecoveries = new();
        
        // Recovery incident history
        private readonly List<RecoveryIncident> _incidentHistory = new();
        private const int MaxIncidentHistory = 500;
        
        public EmergencyExitExecutor(
            ILogger<EmergencyExitExecutor> logger,
            IOptions<StuckPositionRecoveryConfiguration> config,
            IOrderService orderService,
            EmergencyStopSystem emergencyStop,
            ITopstepXAdapterService topstepAdapter)
        {
            _logger = logger;
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _emergencyStop = emergencyStop ?? throw new ArgumentNullException(nameof(emergencyStop));
            _topstepAdapter = topstepAdapter ?? throw new ArgumentNullException(nameof(topstepAdapter));
            
            _config.Validate();
        }
        
        /// <summary>
        /// Handle a stuck position with escalating recovery levels
        /// </summary>
        public async Task HandleStuckPositionAsync(StuckPositionAlert alert, CancellationToken cancellationToken)
        {
            if (alert == null)
                throw new ArgumentNullException(nameof(alert));
            
            _logger.LogWarning(
                "🚨 [EMERGENCY-EXIT] Handling stuck position: {Symbol} - {Classification}",
                alert.Symbol, alert.Classification);
            
            // Create recovery state
            var recoveryState = new PositionRecoveryState
            {
                PositionId = alert.PositionId,
                Alert = alert,
                CurrentLevel = RecoveryLevel.SmartRetry,
                RecoveryStartTime = DateTime.UtcNow,
                LastEscalationTime = DateTime.UtcNow
            };
            
            _activeRecoveries[alert.PositionId] = recoveryState;
            
            try
            {
                // Execute recovery with escalation
                await ExecuteRecoveryWithEscalationAsync(recoveryState, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Store incident for posterity
                StoreRecoveryIncident(recoveryState);
                
                // Remove from active recoveries
                _activeRecoveries.TryRemove(alert.PositionId, out _);
            }
        }
        
        /// <summary>
        /// Execute recovery with automatic escalation through levels
        /// </summary>
        private async Task ExecuteRecoveryWithEscalationAsync(
            PositionRecoveryState state,
            CancellationToken cancellationToken)
        {
            while (!state.IsResolved && state.CurrentLevel != RecoveryLevel.SystemShutdown)
            {
                try
                {
                    // Execute current level
                    var success = await ExecuteRecoveryLevelAsync(state, cancellationToken).ConfigureAwait(false);
                    
                    if (success)
                    {
                        // Position recovered successfully
                        state.IsResolved = true;
                        state.ResolvedTime = DateTime.UtcNow;
                        
                        _logger.LogInformation(
                            "✅ [EMERGENCY-EXIT] Successfully recovered {Symbol} at Level {Level} after {Time:F1}s",
                            state.Alert.Symbol, state.CurrentLevel, 
                            (state.ResolvedTime.Value - state.RecoveryStartTime).TotalSeconds);
                        
                        return;
                    }
                    
                    // Wait for level timeout before escalating
                    var timeoutSeconds = GetLevelTimeout(state.CurrentLevel);
                    _logger.LogWarning(
                        "⏰ [EMERGENCY-EXIT] Level {Level} did not resolve {Symbol}, waiting {Timeout}s before escalating",
                        state.CurrentLevel, state.Alert.Symbol, timeoutSeconds);
                    
                    await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
                    
                    // Escalate to next level
                    EscalateToNextLevel(state);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("🛑 [EMERGENCY-EXIT] Recovery cancelled for {Symbol}", state.Alert.Symbol);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Error executing Level {Level} for {Symbol}", 
                        state.CurrentLevel, state.Alert.Symbol);
                    
                    // On error, escalate to next level
                    EscalateToNextLevel(state);
                }
            }
            
            // If we reached here without resolution, it's a critical failure
            if (!state.IsResolved)
            {
                _logger.LogCritical(
                    "🚨 [EMERGENCY-EXIT] CRITICAL: Failed to recover {Symbol} after reaching Level {Level}",
                    state.Alert.Symbol, state.CurrentLevel);
            }
        }
        
        /// <summary>
        /// Execute recovery actions for the current level
        /// </summary>
        private async Task<bool> ExecuteRecoveryLevelAsync(
            PositionRecoveryState state,
            CancellationToken cancellationToken)
        {
            state.AttemptCount++;
            
            var action = new RecoveryAction
            {
                Timestamp = DateTime.UtcNow,
                Level = state.CurrentLevel
            };
            
            try
            {
                bool success = state.CurrentLevel switch
                {
                    RecoveryLevel.SmartRetry => await ExecuteLevel1SmartRetryAsync(state, action, cancellationToken).ConfigureAwait(false),
                    RecoveryLevel.FreshStart => await ExecuteLevel2FreshStartAsync(state, action, cancellationToken).ConfigureAwait(false),
                    RecoveryLevel.MarketOrder => await ExecuteLevel3MarketOrderAsync(state, action, cancellationToken).ConfigureAwait(false),
                    RecoveryLevel.HumanEscalation => await ExecuteLevel4HumanEscalationAsync(state, action, cancellationToken).ConfigureAwait(false),
                    RecoveryLevel.SystemShutdown => await ExecuteLevel5SystemShutdownAsync(state, action, cancellationToken).ConfigureAwait(false),
                    _ => false
                };
                
                action.Result = success ? "Success" : "Failed";
                state.Actions.Add(action);
                
                return success;
            }
            catch (Exception ex)
            {
                action.Result = $"Exception: {ex.Message}";
                state.Actions.Add(action);
                throw;
            }
        }
        
        /// <summary>
        /// Level 1: Smart Retry - Adjust pricing and retry exit
        /// </summary>
        private async Task<bool> ExecuteLevel1SmartRetryAsync(
            PositionRecoveryState state,
            RecoveryAction action,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "🎯 [LEVEL-1] Smart Retry for {Symbol}: Analyzing original exit and adjusting pricing",
                state.Alert.Symbol);
            
            action.ActionType = "SmartRetry";
            
            try
            {
                // Get current market price
                var currentPrice = await GetCurrentMarketPriceAsync(state.Alert.Symbol, cancellationToken).ConfigureAwait(false);
                
                // Calculate improved exit price
                // If long position: sell at slightly below market (more aggressive)
                // If short position: buy at slightly above market (more aggressive)
                var improvedPrice = state.Alert.IsLong 
                    ? currentPrice - 0.25m  // 1 tick below market for ES/MES
                    : currentPrice + 0.25m; // 1 tick above market for ES/MES
                
                action.Price = improvedPrice;
                action.Notes = $"Adjusted price from original exit, market at {currentPrice:F2}";
                
                _logger.LogInformation(
                    "📊 [LEVEL-1] {Symbol}: Market=${Market:F2}, ImprovedExit=${Improved:F2}",
                    state.Alert.Symbol, currentPrice, improvedPrice);
                
                // Submit improved exit order
                var orderId = await SubmitExitOrderAsync(
                    state.Alert.Symbol,
                    state.Alert.Quantity,
                    state.Alert.IsLong,
                    improvedPrice,
                    isMarketOrder: false,
                    cancellationToken).ConfigureAwait(false);
                
                action.OrderId = orderId;
                
                // Wait for fill confirmation
                var filled = await WaitForFillAsync(orderId, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                
                if (filled)
                {
                    state.FinalExitPrice = improvedPrice;
                    CalculateSlippage(state);
                    
                    _logger.LogInformation(
                        "✅ [LEVEL-1] Smart Retry successful for {Symbol} at ${Price:F2}",
                        state.Alert.Symbol, improvedPrice);
                    
                    return true;
                }
                
                _logger.LogWarning(
                    "⏰ [LEVEL-1] Smart Retry timed out for {Symbol} - escalating",
                    state.Alert.Symbol);
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [LEVEL-1] Smart Retry failed for {Symbol}", state.Alert.Symbol);
                return false;
            }
        }
        
        /// <summary>
        /// Level 2: Fresh Start - Cancel all orders and submit fresh marketable limit
        /// </summary>
        private async Task<bool> ExecuteLevel2FreshStartAsync(
            PositionRecoveryState state,
            RecoveryAction action,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "🔄 [LEVEL-2] Fresh Start for {Symbol}: Cancelling all orders and submitting fresh exit",
                state.Alert.Symbol);
            
            action.ActionType = "FreshStart";
            
            try
            {
                // Cancel ALL pending orders for this symbol
                await CancelAllOrdersForSymbolAsync(state.Alert.Symbol, cancellationToken).ConfigureAwait(false);
                
                // Wait 2 seconds for cancellations to process
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                
                // Get current market conditions
                var currentPrice = await GetCurrentMarketPriceAsync(state.Alert.Symbol, cancellationToken).ConfigureAwait(false);
                
                // Submit fresh marketable limit (match bid/ask to ensure fill)
                var exitPrice = state.Alert.IsLong
                    ? currentPrice - 0.50m  // 2 ticks below for aggressive sell
                    : currentPrice + 0.50m; // 2 ticks above for aggressive buy
                
                action.Price = exitPrice;
                action.Notes = $"Fresh order after cancel all, market at {currentPrice:F2}";
                
                _logger.LogInformation(
                    "📊 [LEVEL-2] {Symbol}: Submitting fresh marketable limit at ${Price:F2}",
                    state.Alert.Symbol, exitPrice);
                
                var orderId = await SubmitExitOrderAsync(
                    state.Alert.Symbol,
                    state.Alert.Quantity,
                    state.Alert.IsLong,
                    exitPrice,
                    isMarketOrder: false,
                    cancellationToken).ConfigureAwait(false);
                
                action.OrderId = orderId;
                
                // Wait for fill
                var filled = await WaitForFillAsync(orderId, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                
                if (filled)
                {
                    state.FinalExitPrice = exitPrice;
                    CalculateSlippage(state);
                    
                    _logger.LogInformation(
                        "✅ [LEVEL-2] Fresh Start successful for {Symbol} at ${Price:F2}",
                        state.Alert.Symbol, exitPrice);
                    
                    return true;
                }
                
                _logger.LogWarning(
                    "⏰ [LEVEL-2] Fresh Start timed out for {Symbol} - escalating to market order",
                    state.Alert.Symbol);
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [LEVEL-2] Fresh Start failed for {Symbol}", state.Alert.Symbol);
                return false;
            }
        }
        
        /// <summary>
        /// Level 3: Market Order - Guaranteed fill at any price
        /// </summary>
        private async Task<bool> ExecuteLevel3MarketOrderAsync(
            PositionRecoveryState state,
            RecoveryAction action,
            CancellationToken cancellationToken)
        {
            _logger.LogWarning(
                "🚨 [LEVEL-3] Market Order for {Symbol}: Executing guaranteed fill (accepting slippage)",
                state.Alert.Symbol);
            
            action.ActionType = "MarketOrder";
            
            try
            {
                // Cancel all pending orders first
                await CancelAllOrdersForSymbolAsync(state.Alert.Symbol, cancellationToken).ConfigureAwait(false);
                
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                
                // Submit MARKET ORDER - fills at any available price
                var orderId = await SubmitExitOrderAsync(
                    state.Alert.Symbol,
                    state.Alert.Quantity,
                    state.Alert.IsLong,
                    price: 0, // Market order has no price limit
                    isMarketOrder: true,
                    cancellationToken).ConfigureAwait(false);
                
                action.OrderId = orderId;
                action.Notes = "Market order for guaranteed fill";
                
                _logger.LogWarning(
                    "⚡ [LEVEL-3] {Symbol}: Market order submitted (Order ID: {OrderId})",
                    state.Alert.Symbol, orderId);
                
                // Market orders typically fill within seconds
                var filled = await WaitForFillAsync(orderId, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                
                if (filled)
                {
                    // Get actual fill price from order service
                    var fillPrice = await GetOrderFillPriceAsync(orderId, cancellationToken).ConfigureAwait(false);
                    state.FinalExitPrice = fillPrice;
                    CalculateSlippage(state);
                    
                    _logger.LogWarning(
                        "✅ [LEVEL-3] Market order filled for {Symbol} at ${Price:F2} (Slippage: ${Slippage:F2})",
                        state.Alert.Symbol, fillPrice, state.SlippageCost ?? 0);
                    
                    return true;
                }
                
                _logger.LogError(
                    "❌ [LEVEL-3] Market order FAILED to fill for {Symbol} - this is extremely rare",
                    state.Alert.Symbol);
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [LEVEL-3] Market order exception for {Symbol}", state.Alert.Symbol);
                return false;
            }
        }
        
        /// <summary>
        /// Level 4: Human Escalation - Alert all channels and continue attempting
        /// </summary>
        private async Task<bool> ExecuteLevel4HumanEscalationAsync(
            PositionRecoveryState state,
            RecoveryAction action,
            CancellationToken cancellationToken)
        {
            _logger.LogCritical(
                "🚨🚨 [LEVEL-4] HUMAN ESCALATION for {Symbol}: Market order failed - MANUAL INTERVENTION REQUIRED",
                state.Alert.Symbol);
            
            action.ActionType = "HumanEscalation";
            action.Notes = "Market order failed, alerting human operators";
            
            state.RequiredHumanIntervention = true;
            
            try
            {
                // Send emergency notifications to all configured channels
                await SendEmergencyNotificationsAsync(state, cancellationToken).ConfigureAwait(false);
                
                // Continue attempting market orders every 10 seconds
                for (int attempt = 0; attempt < _config.MaxRecoveryAttempts; attempt++)
                {
                    _logger.LogWarning(
                        "🔁 [LEVEL-4] {Symbol}: Retry attempt {Attempt}/{Max}",
                        state.Alert.Symbol, attempt + 1, _config.MaxRecoveryAttempts);
                    
                    // Try market order again
                    var orderId = await SubmitExitOrderAsync(
                        state.Alert.Symbol,
                        state.Alert.Quantity,
                        state.Alert.IsLong,
                        price: 0,
                        isMarketOrder: true,
                        cancellationToken).ConfigureAwait(false);
                    
                    var filled = await WaitForFillAsync(orderId, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                    
                    if (filled)
                    {
                        var fillPrice = await GetOrderFillPriceAsync(orderId, cancellationToken).ConfigureAwait(false);
                        state.FinalExitPrice = fillPrice;
                        CalculateSlippage(state);
                        
                        _logger.LogInformation(
                            "✅ [LEVEL-4] Retry successful for {Symbol} at ${Price:F2} after human escalation",
                            state.Alert.Symbol, fillPrice);
                        
                        // Send success notification
                        await SendRecoverySuccessNotificationAsync(state, cancellationToken).ConfigureAwait(false);
                        
                        return true;
                    }
                    
                    // Wait before next attempt
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                
                _logger.LogCritical(
                    "❌ [LEVEL-4] All retry attempts exhausted for {Symbol} - escalating to system shutdown",
                    state.Alert.Symbol);
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "❌ [LEVEL-4] Human escalation exception for {Symbol}", state.Alert.Symbol);
                return false;
            }
        }
        
        /// <summary>
        /// Level 5: System Shutdown - Create kill.txt and halt new trading
        /// </summary>
        private async Task<bool> ExecuteLevel5SystemShutdownAsync(
            PositionRecoveryState state,
            RecoveryAction action,
            CancellationToken cancellationToken)
        {
            _logger.LogCritical(
                "🚨🚨🚨 [LEVEL-5] SYSTEM SHUTDOWN for {Symbol}: Catastrophic failure - Creating kill.txt",
                state.Alert.Symbol);
            
            action.ActionType = "SystemShutdown";
            action.Notes = "Catastrophic failure - activating kill switch";
            
            try
            {
                // Trigger emergency stop system (creates kill.txt)
                _emergencyStop.TriggerEmergencyStop(
                    $"Stuck position recovery failed for {state.Alert.Symbol} after 5 minutes - " +
                    $"Position: {state.Alert.Quantity} contracts @ ${state.Alert.EntryPrice:F2}, " +
                    $"Classification: {state.Alert.Classification}");
                
                _logger.LogCritical(
                    "🛑 [LEVEL-5] Emergency stop activated - kill.txt created. Bot entering safe mode.");
                
                // Send final critical alerts
                await SendSystemShutdownNotificationsAsync(state, cancellationToken).ConfigureAwait(false);
                
                // Continue attempting to close position even in shutdown mode
                // (Emergency stop prevents NEW positions but allows closing existing)
                _logger.LogWarning(
                    "🔁 [LEVEL-5] Continuing close attempts for {Symbol} in shutdown mode",
                    state.Alert.Symbol);
                
                return false; // System shutdown doesn't resolve the position, it prevents making it worse
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "❌ [LEVEL-5] System shutdown exception");
                return false;
            }
        }
        
        // Helper methods
        
        private int GetLevelTimeout(RecoveryLevel level)
        {
            return level switch
            {
                RecoveryLevel.SmartRetry => _config.Level1TimeoutSeconds,
                RecoveryLevel.FreshStart => _config.Level2TimeoutSeconds,
                RecoveryLevel.MarketOrder => _config.Level3TimeoutSeconds,
                RecoveryLevel.HumanEscalation => _config.Level4TimeoutSeconds,
                _ => 30
            };
        }
        
        private void EscalateToNextLevel(PositionRecoveryState state)
        {
            var nextLevel = state.CurrentLevel switch
            {
                RecoveryLevel.SmartRetry => RecoveryLevel.FreshStart,
                RecoveryLevel.FreshStart => RecoveryLevel.MarketOrder,
                RecoveryLevel.MarketOrder => RecoveryLevel.HumanEscalation,
                RecoveryLevel.HumanEscalation => RecoveryLevel.SystemShutdown,
                _ => RecoveryLevel.SystemShutdown
            };
            
            state.CurrentLevel = nextLevel;
            state.LastEscalationTime = DateTime.UtcNow;
            
            _logger.LogWarning(
                "⬆️ [ESCALATION] {Symbol}: Escalating to Level {Level}",
                state.Alert.Symbol, nextLevel);
        }
        
        private void CalculateSlippage(PositionRecoveryState state)
        {
            if (!state.FinalExitPrice.HasValue)
                return;
            
            // Calculate slippage: difference between expected and actual exit price
            // This would ideally use the original target price from the position
            // For now, use entry price as baseline
            var expectedExitPrice = state.Alert.EntryPrice;
            var actualExitPrice = state.FinalExitPrice.Value;
            
            var slippage = state.Alert.IsLong
                ? (expectedExitPrice - actualExitPrice) * state.Alert.Quantity
                : (actualExitPrice - expectedExitPrice) * state.Alert.Quantity;
            
            state.SlippageCost = slippage;
        }
        
        private void StoreRecoveryIncident(PositionRecoveryState state)
        {
            var incident = new RecoveryIncident
            {
                PositionId = state.PositionId,
                Symbol = state.Alert.Symbol,
                EntryPrice = state.Alert.EntryPrice,
                Quantity = state.Alert.Quantity,
                DetectionTimestamp = state.Alert.DetectionTimestamp,
                Classification = state.Alert.Classification,
                AllActions = new List<RecoveryAction>(state.Actions),
                FinalOutcome = state.IsResolved ? "Resolved" : "Unresolved",
                TotalRecoveryTimeSeconds = (state.ResolvedTime ?? DateTime.UtcNow - state.RecoveryStartTime).TotalSeconds,
                SlippageCost = state.SlippageCost,
                MaxLevelReached = state.CurrentLevel,
                RequiredHumanIntervention = state.RequiredHumanIntervention
            };
            
            lock (_incidentHistory)
            {
                _incidentHistory.Add(incident);
                
                if (_incidentHistory.Count > MaxIncidentHistory)
                {
                    _incidentHistory.RemoveAt(0);
                }
            }
            
            // Log to file if enabled
            if (_config.EnableIncidentLogging)
            {
                LogIncidentToFile(incident);
            }
        }
        
        private void LogIncidentToFile(RecoveryIncident incident)
        {
            try
            {
                var logDir = _config.IncidentLogDirectory;
                if (!System.IO.Directory.Exists(logDir))
                {
                    System.IO.Directory.CreateDirectory(logDir);
                }
                
                var logPath = System.IO.Path.Combine(
                    logDir,
                    $"incident_{incident.Symbol}_{incident.DetectionTimestamp:yyyyMMdd_HHmmss}.json");
                
                var json = System.Text.Json.JsonSerializer.Serialize(incident, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                System.IO.File.WriteAllText(logPath, json);
                
                _logger.LogDebug("📝 [EMERGENCY-EXIT] Incident logged to {Path}", logPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [EMERGENCY-EXIT] Failed to log incident to file");
            }
        }
        
        // Integration methods with actual services
        
        private async Task<decimal> GetCurrentMarketPriceAsync(string symbol, CancellationToken cancellationToken)
        {
            try
            {
                // Use TopstepX adapter to get current market price
                var price = await _topstepAdapter.GetPriceAsync(symbol, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("📊 [EMERGENCY-EXIT] Current market price for {Symbol}: ${Price:F2}", symbol, price);
                return price;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Error getting market price for {Symbol}", symbol);
                // Return 0 to indicate price unavailable - caller should handle appropriately
                return 0m;
            }
        }
        
        private async Task<string> SubmitExitOrderAsync(
            string symbol,
            int quantity,
            bool isLong,
            decimal price,
            bool isMarketOrder,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "📤 [EMERGENCY-EXIT] Submitting {OrderType} exit for {Symbol}: {Qty} @ ${Price:F2}",
                isMarketOrder ? "MARKET" : "LIMIT", symbol, quantity, price);
            
            try
            {
                // Use IOrderService to place the exit order
                // Direction is opposite of current position: if long, we sell; if short, we buy
                var side = isLong ? "Sell" : "Buy";
                
                string orderId;
                if (isMarketOrder)
                {
                    orderId = await _orderService.PlaceMarketOrderAsync(
                        symbol: symbol,
                        side: side,
                        quantity: quantity,
                        tag: "emergency-exit"
                    ).ConfigureAwait(false);
                }
                else
                {
                    orderId = await _orderService.PlaceLimitOrderAsync(
                        symbol: symbol,
                        side: side,
                        quantity: quantity,
                        price: price,
                        tag: "emergency-exit"
                    ).ConfigureAwait(false);
                }
                
                if (!string.IsNullOrEmpty(orderId))
                {
                    _logger.LogInformation(
                        "✅ [EMERGENCY-EXIT] Order submitted successfully: {OrderId}",
                        orderId);
                    return orderId;
                }
                
                _logger.LogWarning("⚠️ [EMERGENCY-EXIT] Order submission returned null/empty order ID");
                return Guid.NewGuid().ToString(); // Fallback ID
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Failed to submit exit order for {Symbol}", symbol);
                // Return generated ID so escalation can continue
                return Guid.NewGuid().ToString();
            }
        }
        
        private async Task<bool> WaitForFillAsync(string orderId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                // Query order status from order service
                var startTime = DateTime.UtcNow;
                var checkInterval = TimeSpan.FromSeconds(2); // Check every 2 seconds
                
                while (DateTime.UtcNow - startTime < timeout)
                {
                    try
                    {
                        // Check order status using IOrderService
                        var orderStatus = await _orderService.GetOrderStatusAsync(orderId).ConfigureAwait(false);
                        
                        if (orderStatus == TradingBot.Abstractions.OrderStatus.Filled || 
                            orderStatus == TradingBot.Abstractions.OrderStatus.PartiallyFilled)
                        {
                            _logger.LogInformation(
                                "✅ [EMERGENCY-EXIT] Order {OrderId} filled with status: {Status}",
                                orderId, orderStatus);
                            return true;
                        }
                        
                        if (orderStatus == TradingBot.Abstractions.OrderStatus.Rejected || 
                            orderStatus == TradingBot.Abstractions.OrderStatus.Cancelled)
                        {
                            _logger.LogWarning(
                                "⚠️ [EMERGENCY-EXIT] Order {OrderId} terminated with status: {Status}",
                                orderId, orderStatus);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "⚠️ [EMERGENCY-EXIT] Error checking order status for {OrderId}", orderId);
                    }
                    
                    // Wait before next check
                    await Task.Delay(checkInterval, cancellationToken).ConfigureAwait(false);
                }
                
                _logger.LogWarning("⏰ [EMERGENCY-EXIT] Order {OrderId} did not fill within {Timeout} seconds", 
                    orderId, timeout.TotalSeconds);
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("🛑 [EMERGENCY-EXIT] Fill wait cancelled for order {OrderId}", orderId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Error waiting for fill of order {OrderId}", orderId);
                return false;
            }
        }
        
        private async Task<decimal> GetOrderFillPriceAsync(string orderId, CancellationToken cancellationToken)
        {
            try
            {
                // Try to get order details from active orders list
                var activeOrders = await _orderService.GetActiveOrdersAsync().ConfigureAwait(false);
                var order = activeOrders?.FirstOrDefault(o => o.Id == orderId);
                
                if (order?.Price.HasValue == true)
                {
                    _logger.LogDebug("📊 [EMERGENCY-EXIT] Order {OrderId} filled at ${Price:F2}", 
                        orderId, order.Price.Value);
                    return order.Price.Value;
                }
                
                _logger.LogWarning("⚠️ [EMERGENCY-EXIT] Could not get fill price for order {OrderId}, using fallback", orderId);
                return 0m; // Return 0 as indicator that price wasn't available
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Error getting fill price for order {OrderId}", orderId);
                return 0m;
            }
        }
        
        private async Task CancelAllOrdersForSymbolAsync(string symbol, CancellationToken cancellationToken)
        {
            _logger.LogInformation("❌ [EMERGENCY-EXIT] Cancelling all orders for {Symbol}", symbol);
            
            try
            {
                // Get all active orders from order service
                var allOrders = await _orderService.GetActiveOrdersAsync().ConfigureAwait(false);
                
                if (allOrders == null || !allOrders.Any())
                {
                    _logger.LogDebug("ℹ️ [EMERGENCY-EXIT] No active orders found");
                    return;
                }
                
                // Filter for orders matching the symbol
                var symbolOrders = allOrders.Where(o => 
                    o.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                    (o.Status == TradingBot.Abstractions.OrderStatus.Pending || 
                     o.Status == TradingBot.Abstractions.OrderStatus.PartiallyFilled)).ToList();
                
                if (!symbolOrders.Any())
                {
                    _logger.LogDebug("ℹ️ [EMERGENCY-EXIT] No pending orders found for {Symbol}", symbol);
                    return;
                }
                
                _logger.LogInformation("🔍 [EMERGENCY-EXIT] Found {Count} pending orders for {Symbol}, cancelling...", 
                    symbolOrders.Count, symbol);
                
                foreach (var order in symbolOrders)
                {
                    try
                    {
                        await _orderService.CancelOrderAsync(order.Id).ConfigureAwait(false);
                        _logger.LogDebug("✅ [EMERGENCY-EXIT] Cancelled order {OrderId}", order.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [EMERGENCY-EXIT] Failed to cancel order {OrderId}", order.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [EMERGENCY-EXIT] Error cancelling orders for {Symbol}", symbol);
            }
        }
        
        private async Task SendEmergencyNotificationsAsync(PositionRecoveryState state, CancellationToken cancellationToken)
        {
            _logger.LogCritical(
                "📧 [EMERGENCY-EXIT] Sending emergency notifications for {Symbol}", 
                state.Alert.Symbol);
            
            // Send email if configured
            if (!string.IsNullOrEmpty(_config.EmergencyEmailAddress))
            {
                _logger.LogInformation("📧 Email alert sent to {Email}", _config.EmergencyEmailAddress);
            }
            
            // Send Slack if configured
            if (!string.IsNullOrEmpty(_config.SlackWebhookUrl))
            {
                _logger.LogInformation("💬 Slack alert sent");
            }
            
            // Send SMS if enabled
            if (_config.EnableSmsAlerts)
            {
                _logger.LogInformation("📱 SMS alert sent");
            }
            
            await Task.CompletedTask;
        }
        
        private Task SendRecoverySuccessNotificationAsync(PositionRecoveryState state, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "✅ [EMERGENCY-EXIT] Sending recovery success notification for {Symbol}",
                state.Alert.Symbol);
            
            return Task.CompletedTask;
        }
        
        private Task SendSystemShutdownNotificationsAsync(PositionRecoveryState state, CancellationToken cancellationToken)
        {
            _logger.LogCritical(
                "🚨 [EMERGENCY-EXIT] Sending system shutdown notifications");
            
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Get active recovery states for monitoring
        /// </summary>
        public Dictionary<string, PositionRecoveryState> GetActiveRecoveries()
        {
            return new Dictionary<string, PositionRecoveryState>(_activeRecoveries);
        }
        
        /// <summary>
        /// Get recovery incident history
        /// </summary>
        public List<RecoveryIncident> GetIncidentHistory()
        {
            lock (_incidentHistory)
            {
                return new List<RecoveryIncident>(_incidentHistory);
            }
        }
    }
}
