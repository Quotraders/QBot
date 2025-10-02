using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BotCore
{
    // EventArgs classes for proper event handling (CA1003 compliance)
    public class OrderEventArgs : EventArgs
    {
        public JsonElement Data { get; }
        public OrderEventArgs(JsonElement data) => Data = data;
    }

    public class TradeEventArgs : EventArgs
    {
        public JsonElement Data { get; }
        public TradeEventArgs(JsonElement data) => Data = data;
    }

    public class PositionEventArgs : EventArgs
    {
        public JsonElement Data { get; }
        public PositionEventArgs(JsonElement data) => Data = data;
    }

    public class AccountEventArgs : EventArgs
    {
        public JsonElement Data { get; }
        public AccountEventArgs(JsonElement data) => Data = data;
    }

    // Lightweight event facade for user stream without pulling external dependencies into BotCore level
    public sealed class UserHubClient
    {
        private static readonly ILogger _logger = 
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<UserHubClient>();

        // LoggerMessage delegates for improved performance (CA1848 compliance)
        private static readonly Action<ILogger, string, Exception> LogArgumentError =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3001), 
                "Argument error in {EventName} handler");

        private static readonly Action<ILogger, string, Exception> LogObjectDisposed =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3002), 
                "Object disposed during {EventName} handler execution");

        private static readonly Action<ILogger, string, Exception> LogInvalidOperation =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3003), 
                "Invalid operation in {EventName} handler");

        private static readonly Action<ILogger, string, Exception> LogNotSupported =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3004), 
                "Not supported in {EventName} handler");

        private static readonly Action<ILogger, string, Exception> LogTimeout =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3005), 
                "Timeout in {EventName} handler");

        private static readonly Action<ILogger, Exception> LogOutOfMemory =
            LoggerMessage.Define(LogLevel.Critical, new EventId(3006), 
                "Critical: Out of memory during event handler execution");
                
        private static readonly Action<ILogger, string, Exception> LogUnexpectedApplicationError =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3007), 
                "Unexpected application error in {EventName} handler - continuing");

        public event EventHandler<OrderEventArgs>? OnOrder;
        public event EventHandler<TradeEventArgs>? OnTrade;
        public event EventHandler<PositionEventArgs>? OnPosition;
        public event EventHandler<AccountEventArgs>? OnAccount;

        // Feed methods can be called by a higher-level client to forward events here
        public void FeedOrder(JsonElement je) => SafeInvokeOrder(OnOrder, je, nameof(OnOrder));
        public void FeedTrade(JsonElement je) => SafeInvokeTrade(OnTrade, je, nameof(OnTrade));
        public void FeedPosition(JsonElement je) => SafeInvokePosition(OnPosition, je, nameof(OnPosition));
        public void FeedAccount(JsonElement je) => SafeInvokeAccount(OnAccount, je, nameof(OnAccount));

        private static void SafeInvokeOrder(EventHandler<OrderEventArgs>? evt, JsonElement arg, string eventName)
        {
            if (evt == null) return;
            
            try 
            { 
                evt.Invoke(null, new OrderEventArgs(arg)); 
            } 
            catch (ArgumentException ex)
            {
                LogArgumentError(_logger, eventName, ex);
            }
            catch (ObjectDisposedException ex)
            {
                LogObjectDisposed(_logger, eventName, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperation(_logger, eventName, ex);
            }
            catch (NotSupportedException ex)
            {
                LogNotSupported(_logger, eventName, ex);
            }
            catch (TimeoutException ex)
            {
                LogTimeout(_logger, eventName, ex);
            }
            catch (OutOfMemoryException ex)
            {
                LogOutOfMemory(_logger, ex);
                throw; // Critical exception, re-throw
            }
            catch (StackOverflowException)
            {
                // Critical system exception - rethrow
                throw;
            }
            catch (Exception ex) when (!(ex is SystemException))
            {
                LogUnexpectedApplicationError(_logger, eventName, ex);
            }
        }

        private static void SafeInvokeTrade(EventHandler<TradeEventArgs>? evt, JsonElement arg, string eventName)
        {
            if (evt == null) return;
            
            try 
            { 
                evt.Invoke(null, new TradeEventArgs(arg)); 
            } 
            catch (ArgumentException ex)
            {
                LogArgumentError(_logger, eventName, ex);
            }
            catch (ObjectDisposedException ex)
            {
                LogObjectDisposed(_logger, eventName, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperation(_logger, eventName, ex);
            }
            catch (NotSupportedException ex)
            {
                LogNotSupported(_logger, eventName, ex);
            }
            catch (TimeoutException ex)
            {
                LogTimeout(_logger, eventName, ex);
            }
            catch (OutOfMemoryException ex)
            {
                LogOutOfMemory(_logger, ex);
                throw; // Critical exception, re-throw
            }
            catch (StackOverflowException)
            {
                // Critical system exception - rethrow
                throw;
            }
            catch (Exception ex) when (!(ex is SystemException))
            {
                LogUnexpectedApplicationError(_logger, eventName, ex);
            }
        }

        private static void SafeInvokePosition(EventHandler<PositionEventArgs>? evt, JsonElement arg, string eventName)
        {
            if (evt == null) return;
            
            try 
            { 
                evt.Invoke(null, new PositionEventArgs(arg)); 
            } 
            catch (ArgumentException ex)
            {
                LogArgumentError(_logger, eventName, ex);
            }
            catch (ObjectDisposedException ex)
            {
                LogObjectDisposed(_logger, eventName, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperation(_logger, eventName, ex);
            }
            catch (NotSupportedException ex)
            {
                LogNotSupported(_logger, eventName, ex);
            }
            catch (TimeoutException ex)
            {
                LogTimeout(_logger, eventName, ex);
            }
            catch (OutOfMemoryException ex)
            {
                LogOutOfMemory(_logger, ex);
                throw; // Critical exception, re-throw
            }
            catch (StackOverflowException)
            {
                // Critical system exception - rethrow
                throw;
            }
            catch (Exception ex) when (!(ex is SystemException))
            {
                LogUnexpectedApplicationError(_logger, eventName, ex);
            }
        }

        private static void SafeInvokeAccount(EventHandler<AccountEventArgs>? evt, JsonElement arg, string eventName)
        {
            if (evt == null) return;
            
            try 
            { 
                evt.Invoke(null, new AccountEventArgs(arg)); 
            } 
            catch (ArgumentException ex)
            {
                LogArgumentError(_logger, eventName, ex);
            }
            catch (ObjectDisposedException ex)
            {
                LogObjectDisposed(_logger, eventName, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperation(_logger, eventName, ex);
            }
            catch (NotSupportedException ex)
            {
                LogNotSupported(_logger, eventName, ex);
            }
            catch (TimeoutException ex)
            {
                LogTimeout(_logger, eventName, ex);
            }
            catch (OutOfMemoryException ex)
            {
                LogOutOfMemory(_logger, ex);
                throw; // Critical exception, re-throw
            }
            catch (StackOverflowException)
            {
                // Critical system exception - rethrow
                throw;
            }
            catch (Exception ex) when (!(ex is SystemException))
            {
                LogUnexpectedApplicationError(_logger, eventName, ex);
            }
        }
    }
}
