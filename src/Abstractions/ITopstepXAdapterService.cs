using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Abstractions
{
    /// <summary>
    /// Event args for status changes
    /// </summary>
    public class StatusChangedEventArgs : EventArgs
    {
        public string Status { get; }
        public StatusChangedEventArgs(string status) => Status = status;
    }

    /// <summary>
    /// Order execution result for TopstepX trades
    /// </summary>
    public record OrderExecutionResult(
        bool Success,
        string? OrderId,
        string? Error,
        string Symbol,
        int Size,
        decimal EntryPrice,
        decimal StopLoss,
        decimal TakeProfit,
        DateTime Timestamp);

    /// <summary>
    /// Historical bar data for backtesting and learning
    /// </summary>
    public class HistoricalBar
    {
        public required string Symbol { get; init; }
        public required DateTime Timestamp { get; init; }
        public required decimal Open { get; init; }
        public required decimal High { get; init; }
        public required decimal Low { get; init; }
        public required decimal Close { get; init; }
        public required long Volume { get; init; }
    }

    /// <summary>
    /// Interface for TopstepX adapter service to avoid circular dependencies
    /// </summary>
    public interface ITopstepXAdapterService
    {
        Task<bool> IsConnectedAsync();
        Task<string> GetAccountStatusAsync();
        event EventHandler<StatusChangedEventArgs>? StatusChanged;
        
        // Additional methods needed by consuming code
        Task<bool> IsHealthyAsync();
        bool IsConnected { get; }
        string ConnectionHealth { get; }
        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task<double> GetHealthScoreAsync(CancellationToken cancellationToken = default);
        
        // Order execution methods for real trading
        Task<OrderExecutionResult> PlaceOrderAsync(string symbol, int size, decimal stopLoss, decimal takeProfit, CancellationToken cancellationToken = default);
        Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken = default);
        Task<bool> ClosePositionAsync(string symbol, int quantity, CancellationToken cancellationToken = default);
        Task<bool> ModifyStopLossAsync(string symbol, decimal stopPrice, CancellationToken cancellationToken = default);
        Task<bool> ModifyTakeProfitAsync(string symbol, decimal takeProfitPrice, CancellationToken cancellationToken = default);
        Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);
        
        // Historical data for backtesting and learning - uses real TopstepX data
        Task<List<HistoricalBar>> GetHistoricalBarsAsync(string symbol, int days = 1, int intervalMinutes = 5, CancellationToken cancellationToken = default);
    }
}