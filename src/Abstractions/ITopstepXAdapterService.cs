using System;
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
        
        // Order execution method for real trading
        Task<OrderExecutionResult> PlaceOrderAsync(string symbol, int size, decimal stopLoss, decimal takeProfit, CancellationToken cancellationToken = default);
        Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken = default);
    }
}