using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Abstractions;

namespace TradingBot.S7
{
    /// <summary>
    /// Market data to S7 service bridge - feeds bar data to S7 multi-horizon relative strength analysis
    /// Integrates with EnhancedMarketDataFlowService for proper production-grade data flow
    /// </summary>
    public sealed class S7MarketDataBridge : IHostedService, IDisposable
    {
        private readonly ILogger<S7MarketDataBridge> _logger;
        private readonly IServiceProvider _serviceProvider;
        private object? _marketDataService;  // Will resolve to IEnhancedMarketDataFlowService
        private IS7Service? _s7Service;
        private S7Configuration? _config;
        private bool _disposed;

        // LoggerMessage delegates for performance
        private static readonly Action<ILogger, Exception?> _logS7ServiceNotAvailable = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(2001, "S7ServiceNotAvailable"), 
                "[S7-BRIDGE] S7 service not available - S7 data feed disabled");
                
        private static readonly Action<ILogger, Exception?> _logS7ServiceDisabled = 
            LoggerMessage.Define(LogLevel.Information, new EventId(2002, "S7ServiceDisabled"), 
                "[S7-BRIDGE] S7 service disabled in configuration");
                
        private static readonly Action<ILogger, Exception?> _logBridgeStarted = 
            LoggerMessage.Define(LogLevel.Information, new EventId(2003, "BridgeStarted"), 
                "[S7-BRIDGE] S7 market data bridge started successfully");
                
        private static readonly Action<ILogger, Exception?> _logBridgeStopped = 
            LoggerMessage.Define(LogLevel.Information, new EventId(2004, "BridgeStopped"), 
                "[S7-BRIDGE] S7 market data bridge stopped");
                
        private static readonly Action<ILogger, Exception?> _logReflectionError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2005, "ReflectionError"), 
                "[S7-BRIDGE] Reflection error accessing market data service");
                
        private static readonly Action<ILogger, Exception?> _logSubscriptionError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2006, "SubscriptionError"), 
                "[S7-BRIDGE] Error subscribing to market data events");
                
        private static readonly Action<ILogger, Exception?> _logMarketDataServiceNotAvailable = 
            LoggerMessage.Define(LogLevel.Warning, new EventId(2007, "MarketDataServiceNotAvailable"), 
                "[S7-BRIDGE] Market data service not available - S7 bridge disabled");

        public S7MarketDataBridge(
            ILogger<S7MarketDataBridge> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Get services (they might not be available during DI construction)
                _s7Service = _serviceProvider.GetService<IS7Service>();
                _config = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<S7Configuration>>()?.Value;

                if (_s7Service == null)
                {
                    _logS7ServiceNotAvailable(_logger, null);
                    return Task.CompletedTask;
                }

                if (_config == null || !_config.Enabled)
                {
                    _logS7ServiceDisabled(_logger, null);
                    return Task.CompletedTask;
                }

                // Try to get the enhanced market data service
                // Using reflection to avoid direct BotCore dependency
                var marketDataServiceType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "IEnhancedMarketDataFlowService");

                if (marketDataServiceType != null)
                {
                    _marketDataService = _serviceProvider.GetService(marketDataServiceType);
                    if (_marketDataService != null)
                    {
                        // Use reflection to subscribe to the OnMarketDataReceived event
                        var eventInfo = marketDataServiceType.GetEvent("OnMarketDataReceived");
                        if (eventInfo != null)
                        {
                            var handler = new Action<string, object>(OnMarketDataReceived);
                            eventInfo.AddEventHandler(_marketDataService, handler);
                            
                            _logger.LogInformation("[S7-BRIDGE] S7 market data bridge connected to EnhancedMarketDataFlowService");
                            _logger.LogInformation("[S7-BRIDGE] Monitoring symbols: {Symbols}", string.Join(", ", _config.Symbols));
                            return Task.CompletedTask;
                        }
                    }
                }

                    _logMarketDataServiceNotAvailable(_logger, null);
                return Task.CompletedTask;
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logReflectionError(_logger, ex);
                return Task.FromException(ex);
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Failed to invoke reflection method for S7 market data bridge");
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Invalid operation during S7 market data bridge setup");
                return Task.FromException(ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_marketDataService != null)
                {
                    // Use reflection to unsubscribe from the event
                    var marketDataServiceType = _marketDataService.GetType();
                    var eventInfo = marketDataServiceType.GetEvent("OnMarketDataReceived");
                    if (eventInfo != null)
                    {
                        var handler = new Action<string, object>(OnMarketDataReceived);
                        eventInfo.RemoveEventHandler(_marketDataService, handler);
                    }
                }

                _logBridgeStopped(_logger, null);
                return Task.CompletedTask;
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Failed to load types during S7 bridge shutdown");
                return Task.FromException(ex);
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Failed to invoke reflection method during S7 bridge shutdown");
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Invalid operation during S7 bridge shutdown");
                return Task.FromException(ex);
            }
        }

        private async void OnMarketDataReceived(string symbol, object data)
        {
            try
            {
                if (_s7Service == null || _config == null)
                    return;

                // Only process data for configured symbols (ES and NQ)
                if (!_config.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                    return;

                // Extract close price from market data
                decimal? closePrice = null;
                DateTime timestamp = DateTime.UtcNow;

                if (data is MarketData marketData)
                {
                    closePrice = (decimal)marketData.Close;
                    timestamp = marketData.Timestamp;
                }
                else if (data is System.Text.Json.JsonElement jsonElement)
                {
                    // Handle JSON market data format
                    if (jsonElement.TryGetProperty("close", out var closeProp) && closeProp.TryGetDecimal(out var close))
                    {
                        closePrice = close;
                    }
                    else if (jsonElement.TryGetProperty("last", out var lastProp) && lastProp.TryGetDecimal(out var last))
                    {
                        closePrice = last;
                    }
                    else if (jsonElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var price))
                    {
                        closePrice = price;
                    }

                    if (jsonElement.TryGetProperty("timestamp", out var timestampProp))
                    {
                        if (timestampProp.TryGetDateTime(out var dt))
                            timestamp = dt;
                        else if (timestampProp.TryGetInt64(out var unixMs))
                            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).DateTime;
                    }
                }
                else
                {
                    // Try to extract price using reflection as fallback
                    var type = data.GetType();
                    var closeProperty = type.GetProperty("Close") ?? type.GetProperty("Last") ?? type.GetProperty("Price");
                    if (closeProperty != null && closeProperty.PropertyType == typeof(decimal))
                    {
                        closePrice = (decimal?)closeProperty.GetValue(data);
                    }

                    var timestampProperty = type.GetProperty("Timestamp") ?? type.GetProperty("Time");
                    if (timestampProperty != null && timestampProperty.PropertyType == typeof(DateTime))
                    {
                        timestamp = (DateTime)timestampProperty.GetValue(data)!;
                    }
                }

                if (closePrice.HasValue)
                {
                    // Update S7 service with new price data
                    await _s7Service.UpdateAsync(symbol, closePrice.Value, timestamp).ConfigureAwait(false);

                    _logger.LogTrace("[S7-BRIDGE] Updated S7 service: {Symbol} @ {Price} ({Timestamp})", 
                        symbol, closePrice.Value, timestamp);
                }
                else
                {
                    _logger.LogDebug("[S7-BRIDGE] Could not extract close price from market data for {Symbol}: {DataType}", 
                        symbol, data.GetType().Name);
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Reflection error processing market data for {Symbol}", symbol);
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Invocation error processing market data for {Symbol}", symbol);
            }
            catch (InvalidCastException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Type conversion error processing market data for {Symbol}", symbol);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] Invalid operation processing market data for {Symbol}", symbol);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] [S7-AUDIT-VIOLATION] HTTP request error processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Operation canceled processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Timeout processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Invalid argument processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY", symbol);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_marketDataService != null)
                {
                    // Use reflection to unsubscribe from the event
                    var marketDataServiceType = _marketDataService.GetType();
                    var eventInfo = marketDataServiceType.GetEvent("OnMarketDataReceived");
                    if (eventInfo != null)
                    {
                        var handler = new Action<string, object>(OnMarketDataReceived);
                        eventInfo.RemoveEventHandler(_marketDataService, handler);
                    }
                }

                _disposed = true;
            }
        }
    }
}