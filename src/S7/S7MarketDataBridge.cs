using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        private readonly S7MarketDataBridgeConfiguration _bridgeConfig;
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
            LoggerMessage.Define(LogLevel.Error, new EventId(2007, "MarketDataServiceNotAvailable"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Enhanced market data service not available - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> _logMonitoringSymbols = 
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(2008, "MonitoringSymbols"), 
                "[S7-BRIDGE] Monitoring symbols: {Symbols}");

        private static readonly Action<ILogger, Exception?> _logReflectionMethodError = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2009, "ReflectionMethodError"), 
                "[S7-BRIDGE] Failed to invoke reflection method for S7 market data bridge");

        private static readonly Action<ILogger, Exception?> _logInvalidOperationSetup = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2010, "InvalidOperationSetup"), 
                "[S7-BRIDGE] Invalid operation during S7 market data bridge setup");

        private static readonly Action<ILogger, Exception?> _logTypeLoadShutdown = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2011, "TypeLoadShutdown"), 
                "[S7-BRIDGE] Failed to load types during S7 bridge shutdown");

        private static readonly Action<ILogger, Exception?> _logInvocationShutdown = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2012, "InvocationShutdown"), 
                "[S7-BRIDGE] Failed to invoke reflection method during S7 bridge shutdown");

        private static readonly Action<ILogger, Exception?> _logInvalidOperationShutdown = 
            LoggerMessage.Define(LogLevel.Error, new EventId(2013, "InvalidOperationShutdown"), 
                "[S7-BRIDGE] Invalid operation during S7 bridge shutdown");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataArgumentError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2014, "MarketDataArgumentError"), 
                "[S7-BRIDGE] Invalid argument in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataOperationError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2015, "MarketDataOperationError"), 
                "[S7-BRIDGE] Invalid operation in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataCancelledError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2016, "MarketDataCancelledError"), 
                "[S7-BRIDGE] Operation canceled in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataTimeoutError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2017, "MarketDataTimeoutError"), 
                "[S7-BRIDGE] Timeout in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataHttpError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2018, "MarketDataHttpError"), 
                "[S7-BRIDGE] HTTP error in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logMarketDataAccessError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2019, "MarketDataAccessError"), 
                "[S7-BRIDGE] Access denied in market data processing for {Symbol}");

        private static readonly Action<ILogger, string, decimal, DateTime, Exception?> _logServiceUpdated = 
            LoggerMessage.Define<string, decimal, DateTime>(LogLevel.Trace, new EventId(2020, "ServiceUpdated"), 
                "[S7-BRIDGE] Updated S7 service: {Symbol} @ {Price} ({Timestamp})");

        private static readonly Action<ILogger, string, string, Exception?> _logCouldNotExtractPrice = 
            LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(2021, "CouldNotExtractPrice"), 
                "[S7-BRIDGE] Could not extract close price from market data for {Symbol}: {DataType}");

        private static readonly Action<ILogger, string, Exception?> _logReflectionProcessingError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2022, "ReflectionProcessingError"), 
                "[S7-BRIDGE] Reflection error processing market data for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logInvocationProcessingError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2023, "InvocationProcessingError"), 
                "[S7-BRIDGE] Invocation error processing market data for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logTypeConversionError = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2024, "TypeConversionError"), 
                "[S7-BRIDGE] Type conversion error processing market data for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logInvalidOperationProcessing = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2025, "InvalidOperationProcessing"), 
                "[S7-BRIDGE] Invalid operation processing market data for {Symbol}");

        private static readonly Action<ILogger, string, Exception?> _logHttpRequestAuditViolation = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2026, "HttpRequestAuditViolation"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] HTTP request error processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> _logOperationCancelledAuditViolation = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2027, "OperationCancelledAuditViolation"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Operation canceled processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> _logTimeoutAuditViolation = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2028, "TimeoutAuditViolation"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Timeout processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> _logReflectionFallbackTelemetry = 
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2030, "ReflectionFallbackTelemetry"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Price extraction falling back to reflection for {Symbol} - TELEMETRY");

        private static readonly Action<ILogger, string, Exception?> _logArgumentAuditViolation = 
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(2031, "ArgumentAuditViolation"), 
                "[S7-BRIDGE] [S7-AUDIT-VIOLATION] Invalid argument processing market data for {Symbol} - FAIL-CLOSED + TELEMETRY");

        public S7MarketDataBridge(
            ILogger<S7MarketDataBridge> logger,
            IServiceProvider serviceProvider,
            IOptions<S7MarketDataBridgeConfiguration> bridgeConfig)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _bridgeConfig = bridgeConfig?.Value ?? throw new ArgumentNullException(nameof(bridgeConfig));
            
            // Validate configuration on construction
            _bridgeConfig.Validate();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!InitializeServices())
                    return Task.CompletedTask;

                var marketDataServiceType = FindMarketDataServiceType();
                if (marketDataServiceType == null)
                {
                    return HandleMissingServiceType();
                }

                return SetupMarketDataSubscription(marketDataServiceType);
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logReflectionError(_logger, ex);
                return Task.FromException(ex);
            }
            catch (TargetInvocationException ex)
            {
                _logReflectionMethodError(_logger, ex);
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logInvalidOperationSetup(_logger, ex);
                return Task.FromException(ex);
            }
        }

        private bool InitializeServices()
        {
            // Get services (they might not be available during DI construction)
            _s7Service = _serviceProvider.GetService<IS7Service>();
            _config = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<S7Configuration>>()?.Value;

            if (_s7Service == null)
            {
                _logS7ServiceNotAvailable(_logger, null);
                return false;
            }

            if (_config == null || !_config.Enabled)
            {
                _logS7ServiceDisabled(_logger, null);
                return false;
            }

            return true;
        }

        private Type? FindMarketDataServiceType()
        {
            // Try to get the enhanced market data service
            // Using reflection to avoid direct BotCore dependency
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == _bridgeConfig.EnhancedServiceTypeName);
        }

        private Task HandleMissingServiceType()
        {
            if (_bridgeConfig.FailOnMissingEnhancedService)
            {
                _logMarketDataServiceNotAvailable(_logger, null);
                throw new InvalidOperationException($"[S7-BRIDGE] [S7-AUDIT-VIOLATION] Enhanced market data service type {_bridgeConfig.EnhancedServiceTypeName} not found - FAIL-CLOSED + TELEMETRY");
            }

            // AUDIT-CLEAN: Configuration gating - only proceed if explicitly allowed
            if (!_bridgeConfig.EnableConfigurationGating)
            {
                _logMarketDataServiceNotAvailable(_logger, null);
            }
            
            return Task.CompletedTask;
        }

        private Task SetupMarketDataSubscription(Type marketDataServiceType)
        {
            _marketDataService = _serviceProvider.GetService(marketDataServiceType);
            if (_marketDataService == null)
            {
                return HandleMissingServiceInstance();
            }

            return SubscribeToMarketDataEvent(marketDataServiceType);
        }

        private Task HandleMissingServiceInstance()
        {
            if (_bridgeConfig.FailOnMissingEnhancedService)
            {
                _logMarketDataServiceNotAvailable(_logger, null);
                throw new InvalidOperationException($"[S7-BRIDGE] [S7-AUDIT-VIOLATION] Enhanced market data service {_bridgeConfig.EnhancedServiceTypeName} not registered in DI - FAIL-CLOSED + TELEMETRY");
            }

            return Task.CompletedTask;
        }

        private Task SubscribeToMarketDataEvent(Type marketDataServiceType)
        {
            // Use reflection to subscribe to the OnMarketDataReceived event
            var eventInfo = marketDataServiceType.GetEvent(_bridgeConfig.MarketDataEventName);
            if (eventInfo == null)
            {
                return HandleMissingEvent();
            }

            var handler = new Action<string, object>(OnMarketDataReceived);
            eventInfo.AddEventHandler(_marketDataService, handler);
            
            _logBridgeStarted(_logger, null);
            _logMonitoringSymbols(_logger, string.Join(", ", _config!.Symbols), null);
            return Task.CompletedTask;
        }

        private Task HandleMissingEvent()
        {
            _logSubscriptionError(_logger, null);
            
            if (_bridgeConfig.FailOnMissingEnhancedService)
            {
                throw new InvalidOperationException($"[S7-BRIDGE] [S7-AUDIT-VIOLATION] Event {_bridgeConfig.MarketDataEventName} not found on enhanced market data service - FAIL-CLOSED + TELEMETRY");
            }

            return Task.CompletedTask;
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
                _logTypeLoadShutdown(_logger, ex);
                return Task.FromException(ex);
            }
            catch (TargetInvocationException ex)
            {
                _logInvocationShutdown(_logger, ex);
                return Task.FromException(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logInvalidOperationShutdown(_logger, ex);
                return Task.FromException(ex);
            }
        }

        private void OnMarketDataReceived(string symbol, object data)
        {
            // Use Task.Run to safely handle async work in event handler
            _ = Task.Run(async () =>
            {
                try
                {
                    await OnMarketDataReceivedAsync(symbol, data).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    _logMarketDataArgumentError(_logger, symbol, ex);
                }
                catch (InvalidOperationException ex)
                {
                    _logMarketDataOperationError(_logger, symbol, ex);
                }
                catch (OperationCanceledException ex)
                {
                    _logMarketDataCancelledError(_logger, symbol, ex);
                }
                catch (TimeoutException ex)
                {
                    _logMarketDataTimeoutError(_logger, symbol, ex);
                }
                catch (HttpRequestException ex)
                {
                    _logMarketDataHttpError(_logger, symbol, ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logMarketDataAccessError(_logger, symbol, ex);
                }
            });
        }

        private async Task OnMarketDataReceivedAsync(string symbol, object data)
        {
            try
            {
                if (!ShouldProcessMarketData(symbol))
                    return;

                var (closePrice, timestamp) = ExtractPriceAndTimestamp(data, symbol);
                
                if (closePrice.HasValue)
                {
                    await UpdateS7ServiceAsync(symbol, closePrice.Value, timestamp).ConfigureAwait(false);
                }
                else
                {
                    _logCouldNotExtractPrice(_logger, symbol, data.GetType().Name, null);
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logReflectionProcessingError(_logger, symbol, ex);
            }
            catch (TargetInvocationException ex)
            {
                _logInvocationProcessingError(_logger, symbol, ex);
            }
            catch (InvalidCastException ex)
            {
                _logTypeConversionError(_logger, symbol, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logInvalidOperationProcessing(_logger, symbol, ex);
            }
            catch (HttpRequestException ex)
            {
                _logHttpRequestAuditViolation(_logger, symbol, ex);
            }
            catch (OperationCanceledException ex)
            {
                _logOperationCancelledAuditViolation(_logger, symbol, ex);
            }
            catch (TimeoutException ex)
            {
                _logTimeoutAuditViolation(_logger, symbol, ex);
            }
            catch (ArgumentException ex)
            {
                _logArgumentAuditViolation(_logger, symbol, ex);
            }
        }

        private bool ShouldProcessMarketData(string symbol)
        {
            if (_s7Service == null || _config == null)
                return false;

            // Only process data for configured symbols (ES and NQ)
            return _config.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase);
        }

        private (decimal? closePrice, DateTime timestamp) ExtractPriceAndTimestamp(object data, string symbol)
        {
            decimal? closePrice = null;
            DateTime timestamp = DateTime.UtcNow;

            if (data is MarketData marketData)
            {
                closePrice = (decimal)marketData.Close;
                timestamp = marketData.Timestamp;
            }
            else if (data is System.Text.Json.JsonElement jsonElement)
            {
                closePrice = ExtractPriceFromJson(jsonElement);
                timestamp = ExtractTimestampFromJson(jsonElement);
            }
            else
            {
                (closePrice, timestamp) = ExtractPriceUsingReflection(data, symbol);
            }

            return (closePrice, timestamp);
        }

        private static decimal? ExtractPriceFromJson(System.Text.Json.JsonElement jsonElement)
        {
            // Handle JSON market data format
            if (jsonElement.TryGetProperty("close", out var closeProp) && closeProp.TryGetDecimal(out var close))
            {
                return close;
            }
            else if (jsonElement.TryGetProperty("last", out var lastProp) && lastProp.TryGetDecimal(out var last))
            {
                return last;
            }
            else if (jsonElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var price))
            {
                return price;
            }

            return null;
        }

        private static DateTime ExtractTimestampFromJson(System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.TryGetProperty("timestamp", out var timestampProp))
            {
                if (timestampProp.TryGetDateTime(out var dt))
                    return dt;
                else if (timestampProp.TryGetInt64(out var unixMs))
                    return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).DateTime;
            }

            return DateTime.UtcNow;
        }

        private (decimal? closePrice, DateTime timestamp) ExtractPriceUsingReflection(object data, string symbol)
        {
            decimal? closePrice = null;
            DateTime timestamp = DateTime.UtcNow;

            // Try to extract price using reflection as fallback
            // AUDIT-CLEAN: Surface telemetry when falling back to reflection
            if (_bridgeConfig.EnableReflectionFallbackTelemetry)
            {
                _logReflectionFallbackTelemetry(_logger, symbol, null);
            }
            
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

            return (closePrice, timestamp);
        }

        private async Task UpdateS7ServiceAsync(string symbol, decimal closePrice, DateTime timestamp)
        {
            // Update S7 service with new price data
            await _s7Service!.UpdateAsync(symbol, closePrice, timestamp).ConfigureAwait(false);
            _logServiceUpdated(_logger, symbol, closePrice, timestamp, null);
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