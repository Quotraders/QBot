using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using global::BotCore.Helpers;
using global::BotCore.Configuration;
using global::BotCore.Services;
using TradingBot.Abstractions;

namespace TradingBot.UnifiedOrchestrator.Services;

internal record HealthScoreResult(
    int HealthScore,
    string Status,
    Dictionary<string, object> InstrumentHealth,
    Dictionary<string, object> SuiteStats,
    DateTime LastCheck,
    bool Initialized);

internal record PortfolioStatusResult(
    Dictionary<string, object> Portfolio,
    Dictionary<string, PositionInfo> Positions,
    DateTime Timestamp);

internal record PositionInfo(
    int Size,
    decimal AveragePrice,
    decimal UnrealizedPnL,
    decimal RealizedPnL);

internal class TopstepXAdapterService : TradingBot.Abstractions.ITopstepXAdapterService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<TopstepXAdapterService> _logger;
    private readonly TopstepXConfiguration _config;
    private readonly string[] _instruments;
    private Process? _pythonProcess;
    private bool _isInitialized;
    private double _connectionHealth;
    private readonly object _processLock = new();
    private bool _disposed;
    
    // PHASE 2: Fill event subscription infrastructure
    private readonly CancellationTokenSource _fillEventCts = new();
    private Task? _fillEventListenerTask;
    private readonly ConcurrentQueue<FillEventData> _fillEventQueue = new();

    // PHASE 2: Fill event callback for OrderExecutionService
    public event EventHandler<FillEventData>? FillEventReceived;

    public TopstepXAdapterService(
        ILogger<TopstepXAdapterService> logger,
        IOptions<TopstepXConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _instruments = new[] { "MNQ", "ES" }; // Support MNQ and ES as specified
        _isInitialized = false;
        _connectionHealth = 0.0;
    }

    public bool IsConnected => _isInitialized && _connectionHealth >= 80.0;
    
    // Interface expects string, provide both for compatibility
    public string ConnectionHealth => _connectionHealth.ToString("F1");
    public double ConnectionHealthScore => _connectionHealth;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("TopstepX adapter already initialized");
            return;
        }

        try
        {
            _logger.LogInformation("🚀 Initializing TopstepX Python SDK adapter...");

            // Validate Python SDK is available
            await ValidatePythonSDKAsync(cancellationToken).ConfigureAwait(false);

            // Initialize adapter through Python process
            var result = await ExecutePythonCommandAsync("initialize", cancellationToken).ConfigureAwait(false);
            
            if (result.Success)
            {
                _isInitialized = true;
                _connectionHealth = 100.0;
                
                // PHASE 2: Start fill event listener
                StartFillEventListener();
                
                _logger.LogInformation("✅ TopstepX adapter initialized successfully");
            }
            else
            {
                var error = $"Failed to initialize TopstepX adapter: {result.Error}";
                _logger.LogError("❌ {Error}", error);
                throw new InvalidOperationException(error);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Exception during TopstepX adapter initialization");
            throw new InvalidOperationException("Failed to initialize TopstepX adapter", ex);
        }
    }

    public async Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        if (!Array.Exists(_instruments, i => i == symbol))
        {
            throw new ArgumentException($"Symbol {symbol} not supported. Supported: {string.Join(", ", _instruments)}");
        }

        try
        {
            var command = new { action = "get_price", symbol };
            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                if (result.Data.TryGetProperty("price", out var priceElement))
                {
                    var price = priceElement.GetDecimal();
                    _logger.LogDebug("[PRICE] {Symbol}: ${Price:F2}", symbol, price);
                    return price;
                }
            }
            
            throw new InvalidOperationException($"Failed to get price for {symbol}: {result.Error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting price for {Symbol}", symbol);
            throw;
        }
    }

    public async Task<OrderExecutionResult> PlaceOrderAsync(
        string symbol, 
        int size, 
        decimal stopLoss, 
        decimal takeProfit, 
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Validate and round prices to valid tick increments before placing order
            try
            {
                stopLoss = PriceHelper.RoundToTick(stopLoss, symbol);
                takeProfit = PriceHelper.RoundToTick(takeProfit, symbol);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError("Price validation failed: {Error}", ex.Message);
                return new OrderExecutionResult(
                    false,
                    null,
                    ex.Message,
                    symbol,
                    size,
                    0m,
                    stopLoss,
                    takeProfit,
                    DateTime.UtcNow);
            }
            
            var currentPrice = await GetPriceAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation(
                "[ORDER] Placing bracket order: {Symbol} size={Size} entry=${EntryPrice:F2} stop=${StopLoss:F2} target=${TakeProfit:F2}",
                symbol, size, currentPrice, stopLoss, takeProfit);

            var command = new
            {
                action = "place_order",
                symbol,
                size,
                stop_loss = stopLoss,
                take_profit = takeProfit,
                max_risk_percent = 0.01 // 1% risk as specified
            };

            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var success = result.Data.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                var orderId = result.Data.TryGetProperty("order_id", out var orderIdElement) ? orderIdElement.GetString() : null;
                var error = result.Data.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                var timestamp = result.Data.TryGetProperty("timestamp", out var tsElement) ? 
                    DateTime.Parse(tsElement.GetString()!) : DateTime.UtcNow;

                var orderResult = new OrderExecutionResult(
                    success,
                    orderId,
                    error,
                    symbol,
                    size,
                    currentPrice,
                    stopLoss,
                    takeProfit,
                    timestamp);

                if (success)
                {
                    _logger.LogInformation("✅ Order placed successfully: {OrderId}", orderId);
                }
                else
                {
                    _logger.LogError("❌ Order placement failed: {Error}", error);
                }

                return orderResult;
            }
            
            throw new InvalidOperationException($"Invalid response from Python adapter: {result.Error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order for {Symbol}", symbol);
            return new OrderExecutionResult(
                false,
                null,
                ex.Message,
                symbol,
                size,
                0m,
                stopLoss,
                takeProfit,
                DateTime.UtcNow);
        }
    }

    public async Task<HealthScoreResult> GetHealthScoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var command = new { action = "get_health_score" };
            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var healthScore = result.Data.TryGetProperty("health_score", out var scoreElement) ? scoreElement.GetInt32() : 0;
                var status = result.Data.TryGetProperty("status", out var statusElement) ? statusElement.GetString()! : "unknown";
                var lastCheck = result.Data.TryGetProperty("last_check", out var checkElement) ? 
                    DateTime.Parse(checkElement.GetString()!) : DateTime.UtcNow;
                var initialized = result.Data.TryGetProperty("initialized", out var initElement) && initElement.GetBoolean();

                // Extract instrument health
                var instrumentHealth = new Dictionary<string, object>();
                if (result.Data.TryGetProperty("instruments", out var instrumentsElement))
                {
                    foreach (var property in instrumentsElement.EnumerateObject())
                    {
                        instrumentHealth[property.Name] = property.Value.GetDouble();
                    }
                }

                // Extract suite stats
                var suiteStats = new Dictionary<string, object>();
                if (result.Data.TryGetProperty("suite_stats", out var statsElement))
                {
                    foreach (var property in statsElement.EnumerateObject())
                    {
                        suiteStats[property.Name] = property.Value.ToString()!;
                    }
                }

                // Update internal health tracking
                _connectionHealth = healthScore;

                var healthResult = new HealthScoreResult(
                    healthScore,
                    status,
                    instrumentHealth,
                    suiteStats,
                    lastCheck,
                    initialized);

                if (healthScore >= 80)
                {
                    _logger.LogDebug("System healthy: {HealthScore}%", healthScore);
                }
                else
                {
                    _logger.LogWarning("System health degraded: {HealthScore}% - Status: {Status}", healthScore, status);
                }

                return healthResult;
            }
            
            throw new InvalidOperationException($"Failed to get health score: {result.Error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting health score");
            return new HealthScoreResult(0, "error", new(), new(), DateTime.UtcNow, false);
        }
    }

    public async Task<PortfolioStatusResult> GetPortfolioStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized");
        }

        try
        {
            var command = new { action = "get_portfolio_status" };
            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var portfolio = new Dictionary<string, object>();
                var positions = new Dictionary<string, PositionInfo>();
                var timestamp = DateTime.UtcNow;

                if (result.Data.TryGetProperty("portfolio", out var portfolioElement))
                {
                    foreach (var property in portfolioElement.EnumerateObject())
                    {
                        portfolio[property.Name] = property.Value.ToString()!;
                    }
                }

                if (result.Data.TryGetProperty("positions", out var positionsElement))
                {
                    foreach (var property in positionsElement.EnumerateObject())
                    {
                        var posData = property.Value;
                        if (!posData.TryGetProperty("error", out _)) // Skip positions with errors
                        {
                            var size = posData.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt32() : 0;
                            var avgPrice = posData.TryGetProperty("average_price", out var priceElement) ? priceElement.GetDecimal() : 0m;
                            var unrealizedPnl = posData.TryGetProperty("unrealized_pnl", out var unrealizedElement) ? unrealizedElement.GetDecimal() : 0m;
                            var realizedPnl = posData.TryGetProperty("realized_pnl", out var realizedElement) ? realizedElement.GetDecimal() : 0m;

                            positions[property.Name] = new PositionInfo(size, avgPrice, unrealizedPnl, realizedPnl);
                        }
                    }
                }

                if (result.Data.TryGetProperty("timestamp", out var tsElement))
                {
                    timestamp = DateTime.Parse(tsElement.GetString()!);
                }

                return new PortfolioStatusResult(portfolio, positions, timestamp);
            }
            
            throw new InvalidOperationException($"Failed to get portfolio status: {result.Error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting portfolio status");
            throw;
        }
    }

    /// <summary>
    /// Close a position (full or partial) via TopstepX API
    /// </summary>
    public async Task<bool> ClosePositionAsync(string symbol, int quantity, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        try
        {
            _logger.LogInformation("[CLOSE] Closing position: {Symbol} quantity={Qty}", symbol, quantity);

            var command = new
            {
                action = "close_position",
                symbol,
                quantity
            };

            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var success = result.Data.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("✅ Position closed successfully: {Symbol} {Qty} contracts", symbol, quantity);
                }
                else
                {
                    var error = result.Data.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Unknown error";
                    _logger.LogError("❌ Position close failed: {Error}", error);
                }
                
                return success;
            }
            
            _logger.LogError("❌ Invalid response from Python adapter for close position");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing position {Symbol}", symbol);
            return false;
        }
    }

    /// <summary>
    /// Modify stop loss for a position via TopstepX API
    /// </summary>
    public async Task<bool> ModifyStopLossAsync(string symbol, decimal stopPrice, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Round to valid tick increment
            stopPrice = PriceHelper.RoundToTick(stopPrice, symbol);
            
            _logger.LogInformation("[MODIFY-STOP] Modifying stop loss: {Symbol} stop=${StopPrice:F2}", symbol, stopPrice);

            var command = new
            {
                action = "modify_stop_loss",
                symbol,
                stop_price = stopPrice
            };

            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var success = result.Data.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("✅ Stop loss modified successfully: {Symbol} stop=${StopPrice:F2}", symbol, stopPrice);
                }
                else
                {
                    var error = result.Data.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Unknown error";
                    _logger.LogError("❌ Stop loss modification failed: {Error}", error);
                }
                
                return success;
            }
            
            _logger.LogError("❌ Invalid response from Python adapter for modify stop loss");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying stop loss for {Symbol}", symbol);
            return false;
        }
    }

    /// <summary>
    /// Modify take profit for a position via TopstepX API
    /// </summary>
    public async Task<bool> ModifyTakeProfitAsync(string symbol, decimal takeProfitPrice, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Round to valid tick increment
            takeProfitPrice = PriceHelper.RoundToTick(takeProfitPrice, symbol);
            
            _logger.LogInformation("[MODIFY-TARGET] Modifying take profit: {Symbol} target=${TargetPrice:F2}", symbol, takeProfitPrice);

            var command = new
            {
                action = "modify_take_profit",
                symbol,
                take_profit_price = takeProfitPrice
            };

            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var success = result.Data.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("✅ Take profit modified successfully: {Symbol} target=${TargetPrice:F2}", symbol, takeProfitPrice);
                }
                else
                {
                    var error = result.Data.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Unknown error";
                    _logger.LogError("❌ Take profit modification failed: {Error}", error);
                }
                
                return success;
            }
            
            _logger.LogError("❌ Invalid response from Python adapter for modify take profit");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying take profit for {Symbol}", symbol);
            return false;
        }
    }

    /// <summary>
    /// Cancel an order via TopstepX API
    /// </summary>
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Adapter not initialized. Call InitializeAsync first.");
        }

        try
        {
            _logger.LogInformation("[CANCEL] Cancelling order: {OrderId}", orderId);

            var command = new
            {
                action = "cancel_order",
                order_id = orderId
            };

            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                var success = result.Data.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("✅ Order cancelled successfully: {OrderId}", orderId);
                }
                else
                {
                    var error = result.Data.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Unknown error";
                    _logger.LogError("❌ Order cancellation failed: {Error}", error);
                }
                
                return success;
            }
            
            _logger.LogError("❌ Invalid response from Python adapter for cancel order");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            _logger.LogDebug("Adapter already disconnected");
            return;
        }

        try
        {
            _logger.LogInformation("Disconnecting TopstepX adapter...");
            
            var command = new { action = "disconnect" };
            await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            _isInitialized = false;
            _connectionHealth = 0.0;
            
            lock (_processLock)
            {
                _pythonProcess?.Kill();
                _pythonProcess?.Dispose();
                _pythonProcess = null;
            }
            
            _logger.LogInformation("✅ TopstepX adapter disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
            throw;
        }
    }

    // Implement interface methods from TradingBot.Abstractions.ITopstepXAdapterService
    public async Task<bool> IsConnectedAsync()
    {
        return await Task.FromResult(IsConnected).ConfigureAwait(false);
    }

    public async Task<string> GetAccountStatusAsync()
    {
        try
        {
            var portfolioStatus = await GetPortfolioStatusAsync().ConfigureAwait(false);
            return $"Connected: {IsConnected}, Health: {_connectionHealth:F1}%, Positions: {portfolioStatus.Positions.Count}";
        }
        catch
        {
            return $"Connected: {IsConnected}, Health: {_connectionHealth:F1}%";
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var healthScore = await GetHealthScoreAsync().ConfigureAwait(false);
            return healthScore.HealthScore >= 80;
        }
        catch
        {
            return false;
        }
    }

    async Task<double> TradingBot.Abstractions.ITopstepXAdapterService.GetHealthScoreAsync(CancellationToken cancellationToken)
    {
        var result = await GetHealthScoreAsync(cancellationToken).ConfigureAwait(false);
        return result.HealthScore;
    }

    string TradingBot.Abstractions.ITopstepXAdapterService.ConnectionHealth => $"{_connectionHealth:F1}%";

    public event EventHandler<TradingBot.Abstractions.StatusChangedEventArgs>? StatusChanged;

    private async Task ValidatePythonSDKAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if project-x-py is installed
            var result = await ExecutePythonCommandAsync("validate_sdk", cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "project-x-py SDK not found. Install with: pip install 'project-x-py[all]'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Python SDK validation failed");
            throw new InvalidOperationException("Failed to validate Python SDK installation", ex);
        }
    }

    private async Task<(bool Success, JsonElement? Data, string? Error)> ExecutePythonCommandAsync(
        string command, 
        CancellationToken cancellationToken)
    {
        try
        {
            var adapterPath = Path.Combine(AppContext.BaseDirectory, "src", "adapters", "topstep_x_adapter.py");
            if (!File.Exists(adapterPath))
            {
                // Try relative path from current directory
                adapterPath = Path.Combine("src", "adapters", "topstep_x_adapter.py");
                if (!File.Exists(adapterPath))
                {
                    throw new FileNotFoundException($"TopstepX adapter not found at {adapterPath}");
                }
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{adapterPath}\" \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set environment variables for credentials if available
            var apiKey = Environment.GetEnvironmentVariable("PROJECT_X_API_KEY");
            var username = Environment.GetEnvironmentVariable("PROJECT_X_USERNAME");
            
            if (!string.IsNullOrEmpty(apiKey))
                processInfo.Environment["PROJECT_X_API_KEY"] = apiKey;
            if (!string.IsNullOrEmpty(username))
                processInfo.Environment["PROJECT_X_USERNAME"] = username;

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Python process");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(output);
                    return (true, data, null);
                }
                catch (JsonException)
                {
                    // Output might not be JSON for simple commands
                    return (true, null, null);
                }
            }
            else
            {
                var errorMsg = !string.IsNullOrEmpty(error) ? error : $"Process exited with code {process.ExitCode}";
                _logger.LogError("Python command failed: {Error}", errorMsg);
                return (false, null, errorMsg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Python command: {Command}", command);
            return (false, null, ex.Message);
        }
    }

    // ========================================================================
    // PHASE 2: FILL EVENT LISTENER INFRASTRUCTURE
    // ========================================================================
    
    /// <summary>
    /// Start background task to listen for fill events from TopstepX SDK
    /// </summary>
    private void StartFillEventListener()
    {
        _fillEventListenerTask = Task.Run(async () =>
        {
            _logger.LogInformation("🎧 [FILL-LISTENER] Starting fill event listener...");
            
            while (!_fillEventCts.Token.IsCancellationRequested)
            {
                try
                {
                    // PHASE 2: Poll for fill events from Python SDK
                    // In production, this would use WebSocket or streaming API
                    await PollForFillEventsAsync(_fillEventCts.Token).ConfigureAwait(false);
                    
                    // Poll every 2 seconds to avoid excessive API calls
                    await Task.Delay(TimeSpan.FromSeconds(2), _fillEventCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in fill event listener");
                    
                    // Reconnection logic: wait 5 seconds before retrying
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), _fillEventCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            
            _logger.LogInformation("🛑 [FILL-LISTENER] Fill event listener stopped");
        }, _fillEventCts.Token);
    }
    
    /// <summary>
    /// Poll Python SDK for new fill events
    /// </summary>
    private async Task PollForFillEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var command = new { action = "get_fill_events" };
            var result = await ExecutePythonCommandAsync(JsonSerializer.Serialize(command), cancellationToken).ConfigureAwait(false);
            
            if (result.Success && result.Data != null)
            {
                // Check if we got fill events
                if (result.Data.TryGetProperty("fills", out var fillsElement) && fillsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fillElement in fillsElement.EnumerateArray())
                    {
                        var fillEvent = ParseFillEvent(fillElement);
                        if (fillEvent != null)
                        {
                            // Add to queue and notify subscribers
                            _fillEventQueue.Enqueue(fillEvent);
                            
                            _logger.LogInformation("📥 [FILL-LISTENER] Received fill: {OrderId} {Symbol} {Qty} @ {Price}",
                                fillEvent.OrderId, fillEvent.Symbol, fillEvent.Quantity, fillEvent.FillPrice);
                            
                            // Notify subscribers
                            FillEventReceived?.Invoke(this, fillEvent);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling for fill events");
            throw;
        }
    }
    
    /// <summary>
    /// Parse fill event from JSON
    /// </summary>
    private FillEventData? ParseFillEvent(JsonElement fillElement)
    {
        try
        {
            var orderId = fillElement.TryGetProperty("order_id", out var orderIdElement) ? orderIdElement.GetString() : null;
            var symbol = fillElement.TryGetProperty("symbol", out var symbolElement) ? symbolElement.GetString() : null;
            var quantity = fillElement.TryGetProperty("quantity", out var qtyElement) ? qtyElement.GetInt32() : 0;
            var price = fillElement.TryGetProperty("price", out var priceElement) ? priceElement.GetDecimal() : 0m;
            var fillPrice = fillElement.TryGetProperty("fill_price", out var fillPriceElement) ? fillPriceElement.GetDecimal() : price;
            var commission = fillElement.TryGetProperty("commission", out var commElement) ? commElement.GetDecimal() : 0m;
            var exchange = fillElement.TryGetProperty("exchange", out var exchElement) ? exchElement.GetString() : "CME";
            var liquidityType = fillElement.TryGetProperty("liquidity_type", out var liqElement) ? liqElement.GetString() : "TAKER";
            var timestampStr = fillElement.TryGetProperty("timestamp", out var tsElement) ? tsElement.GetString() : null;
            
            if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(symbol))
            {
                _logger.LogWarning("Received fill event with missing order_id or symbol");
                return null;
            }
            
            var timestamp = !string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, out var parsedTs)
                ? parsedTs
                : DateTime.UtcNow;
            
            return new FillEventData
            {
                OrderId = orderId,
                Symbol = symbol,
                Quantity = quantity,
                Price = price,
                FillPrice = fillPrice,
                Commission = commission,
                Exchange = exchange ?? "CME",
                LiquidityType = liquidityType ?? "TAKER",
                Timestamp = timestamp
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing fill event");
            return null;
        }
    }
    
    /// <summary>
    /// Subscribe to fill events with a callback
    /// </summary>
    public void SubscribeToFillEvents(Action<FillEventData> callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }
        
        FillEventReceived += (sender, fillData) => callback(fillData);
        
        _logger.LogInformation("✅ [FILL-LISTENER] Callback subscribed to fill events");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            try
            {
                // PHASE 2: Stop fill event listener
                _fillEventCts.Cancel();
                if (_fillEventListenerTask != null)
                {
                    await _fillEventListenerTask.ConfigureAwait(false);
                }
                _fillEventCts.Dispose();
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await DisconnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Disconnect timed out during async disposal after 5 seconds");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during async disposal");
            }

            lock (_processLock)
            {
                _pythonProcess?.Dispose();
                _pythonProcess = null;
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        // Synchronous dispose calls async dispose with a bounded helper
        // This is the recommended pattern when IAsyncDisposable is implemented
        if (!_disposed)
        {
            try
            {
                // Use GetAwaiter().GetResult() in a controlled manner with timeout
                var disposeTask = DisposeAsync().AsTask();
                if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogWarning("Async dispose timed out in synchronous Dispose() after 5 seconds");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calling async dispose from synchronous Dispose()");
            }

            _disposed = true;
        }
    }
}