using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Data.SQLite;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Execution
{
    /// <summary>
    /// Execution Verification System - Ensures order execution integrity
    /// Extracted from CriticalSystemComponents.cs for better modularity
    /// </summary>
    public class ExecutionVerificationSystem : IDisposable
    {
        // Execution Verification Constants
        private const int VerificationDelayMs = 100;  // Brief delay during verification processing
        
        private readonly ConcurrentDictionary<string, OrderRecord> _pendingOrders = new();
        private readonly ConcurrentDictionary<string, FillRecord> _confirmedFills = new();
        private readonly SQLiteConnection _database;
        private Timer? _reconciliationTimer;
        private readonly ILogger<ExecutionVerificationSystem> _logger;
        
        public sealed class OrderRecord
        {
            public string OrderId { get; set; } = string.Empty;
            public string ClientOrderId { get; set; } = string.Empty;
            public DateTime SubmittedTime { get; set; }
            public string Symbol { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public decimal Price { get; set; }
            public string Side { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool IsVerified { get; set; }
            public string ExecutionProof { get; set; } = string.Empty;
            
            private readonly List<PartialFill> _partialFills = new();
            public IReadOnlyList<PartialFill> PartialFills => _partialFills;

            public void ReplacePartialFills(IEnumerable<PartialFill> fills)
            {
                _partialFills.Clear();
                if (fills != null) _partialFills.AddRange(fills);
            }
        }
        
        public sealed class FillRecord
        {
            public string FillId { get; set; } = string.Empty;
            public string OrderId { get; set; } = string.Empty;
            public DateTime FillTime { get; set; }
            public decimal FillPrice { get; set; }
            public int FillQuantity { get; set; }
            public decimal Commission { get; set; }
            public string Exchange { get; set; } = string.Empty;
            public string LiquidityType { get; set; } = string.Empty;
        }
        
        public sealed class PartialFill
        {
            public int Quantity { get; set; }
            public decimal Price { get; set; }
            public DateTime Time { get; set; }
        }

        internal sealed class FillEventData
        {
            public string OrderId { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public decimal FillPrice { get; set; }
            public int Quantity { get; set; }
            public string Symbol { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public decimal Commission { get; set; }
            public string Exchange { get; set; } = string.Empty;
            public string LiquidityType { get; set; } = string.Empty;
        }

        internal sealed class OrderStatusData
        {
            public string OrderId { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int FilledQuantity { get; set; }
            public int RemainingQuantity { get; set; }
            public decimal AveragePrice { get; set; }
            public DateTime LastUpdate { get; set; }
        }
        
        public ExecutionVerificationSystem(
            ILogger<ExecutionVerificationSystem> logger,
            ITopstepXClient topstepXClient,
            string databasePath = "execution_verification.db")
        {
            _logger = logger;
            
            // Initialize SQLite database
            _database = new SQLiteConnection($"Data Source={databasePath}");
            _database.Open();
            
            InitializeDatabase();
            StartReconciliationTimer();
            
            _logger.LogInformation("ExecutionVerificationSystem initialized with database: {DatabasePath}", databasePath);
        }

        /// <summary>
        /// Verify order execution with orderId + fill event proof
        /// Production Rule: Order fills require orderId return + fill event confirmation
        /// </summary>
        public async Task<bool> VerifyOrderExecutionAsync(string orderId, string symbol)
        {
            if (string.IsNullOrEmpty(orderId))
            {
                _logger.LogError("Cannot verify execution: orderId is null or empty");
                return false;
            }

            try
            {
                // Check pending orders
                if (!_pendingOrders.TryGetValue(orderId, out var orderRecord))
                {
                    _logger.LogWarning("Order {OrderId} not found in pending orders", orderId);
                    return false;
                }

                // Get order status from TopstepX
                var statusData = await GetOrderStatusFromTopstepXAsync(orderId);
                if (statusData == null)
                {
                    _logger.LogError("Failed to get order status from TopstepX for order {OrderId}", orderId);
                    return false;
                }

                // Check for fill events
                var fillEvents = await GetFillEventsFromTopstepXAsync(orderId);
                if (fillEvents == null || !fillEvents.Any())
                {
                    _logger.LogDebug("No fill events found for order {OrderId}", orderId);
                    return false;
                }

                // Verify fill consistency
                var isVerified = VerifyFillConsistency(orderRecord, statusData, fillEvents);
                
                if (isVerified)
                {
                    // Move to confirmed fills
                    var fillRecord = CreateFillRecord(orderRecord, fillEvents);
                    _confirmedFills.TryAdd(orderId, fillRecord);
                    _pendingOrders.TryRemove(orderId, out _);
                    
                    await PersistFillToDatabaseAsync(fillRecord);
                    
                    _logger.LogInformation("Order {OrderId} execution verified and confirmed", orderId);
                }
                else
                {
                    _logger.LogWarning("Order {OrderId} execution verification failed", orderId);
                }

                return isVerified;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying order execution for {OrderId}", orderId);
                return false;
            }
        }

        /// <summary>
        /// Add order to pending verification queue
        /// </summary>
        public void AddPendingOrder(string orderId, string clientOrderId, string symbol, 
            int quantity, decimal price, string side)
        {
            var orderRecord = new OrderRecord
            {
                OrderId = orderId,
                ClientOrderId = clientOrderId,
                SubmittedTime = DateTime.UtcNow,
                Symbol = symbol,
                Quantity = quantity,
                Price = price,
                Side = side,
                Status = "PENDING_VERIFICATION",
                IsVerified = false
            };

            _pendingOrders.TryAdd(orderId, orderRecord);
            _logger.LogDebug("Added order {OrderId} to pending verification queue", orderId);
        }

        /// <summary>
        /// Get confirmed fill record
        /// </summary>
        public FillRecord? GetConfirmedFill(string orderId)
        {
            _confirmedFills.TryGetValue(orderId, out var fillRecord);
            return fillRecord;
        }

        /// <summary>
        /// Get all pending orders for monitoring
        /// </summary>
        public IReadOnlyDictionary<string, OrderRecord> GetPendingOrders()
        {
            return _pendingOrders.ToArray().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private void InitializeDatabase()
        {
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS ExecutionVerification (
                    OrderId TEXT PRIMARY KEY,
                    ClientOrderId TEXT,
                    Symbol TEXT,
                    Quantity INTEGER,
                    Price DECIMAL,
                    Side TEXT,
                    FillPrice DECIMAL,
                    FillQuantity INTEGER,
                    FillTime TEXT,
                    Commission DECIMAL,
                    Exchange TEXT,
                    LiquidityType TEXT,
                    VerificationTime TEXT,
                    ExecutionProof TEXT
                )";

            using var command = new SQLiteCommand(createTableSql, _database);
            command.ExecuteNonQuery();
        }

        private void StartReconciliationTimer()
        {
            _reconciliationTimer = new Timer(async _ => await ReconcilePendingOrdersAsync(), 
                null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        private async Task ReconcilePendingOrdersAsync()
        {
            try
            {
                var pendingOrders = _pendingOrders.ToArray();
                
                foreach (var (orderId, orderRecord) in pendingOrders)
                {
                    // Check orders older than 1 minute
                    if (DateTime.UtcNow - orderRecord.SubmittedTime > TimeSpan.FromMinutes(1))
                    {
                        await VerifyOrderExecutionAsync(orderId, orderRecord.Symbol);
                        await Task.Delay(VerificationDelayMs); // Throttle API calls
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pending orders reconciliation");
            }
        }

        private async Task<OrderStatusData?> GetOrderStatusFromTopstepXAsync(string orderId)
        {
            try
            {
                // In production, this would call actual TopstepX API
                // For now, return mock data structure
                await Task.Delay(1).ConfigureAwait(false); // Simulate async operation
                return new OrderStatusData
                {
                    OrderId = orderId,
                    Status = "FILLED",
                    FilledQuantity = 1,
                    RemainingQuantity = 0,
                    AveragePrice = 4500.25m,
                    LastUpdate = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get order status from TopstepX for order {OrderId}", orderId);
                return null;
            }
        }

        private async Task<List<FillEventData>?> GetFillEventsFromTopstepXAsync(string orderId)
        {
            try
            {
                // In production, this would call actual TopstepX API
                // For now, return mock fill event
                await Task.Delay(1).ConfigureAwait(false); // Simulate async operation
                return new List<FillEventData>
                {
                    new FillEventData
                    {
                        OrderId = orderId,
                        Price = 4500.25m,
                        FillPrice = 4500.25m,
                        Quantity = 1,
                        Symbol = "ES",
                        Timestamp = DateTime.UtcNow,
                        Commission = 2.50m,
                        Exchange = "CME",
                        LiquidityType = "MAKER"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get fill events from TopstepX for order {OrderId}", orderId);
                return null;
            }
        }

        private bool VerifyFillConsistency(OrderRecord orderRecord, OrderStatusData statusData, 
            List<FillEventData> fillEvents)
        {
            // Verify order quantities match
            var totalFillQuantity = fillEvents.Sum(f => f.Quantity);
            if (totalFillQuantity != statusData.FilledQuantity)
            {
                _logger.LogWarning("Fill quantity mismatch for order {OrderId}: fills={TotalFills}, status={StatusFills}",
                    orderRecord.OrderId, totalFillQuantity, statusData.FilledQuantity);
                return false;
            }

            // Verify all fills have valid data
            foreach (var fill in fillEvents)
            {
                if (fill.FillPrice <= 0 || fill.Quantity <= 0)
                {
                    _logger.LogWarning("Invalid fill data for order {OrderId}: price={Price}, quantity={Quantity}",
                        orderRecord.OrderId, fill.FillPrice, fill.Quantity);
                    return false;
                }
            }

            return true;
        }

        private FillRecord CreateFillRecord(OrderRecord orderRecord, List<FillEventData> fillEvents)
        {
            var primaryFill = fillEvents.First();
            
            return new FillRecord
            {
                FillId = Guid.NewGuid().ToString(),
                OrderId = orderRecord.OrderId,
                FillTime = primaryFill.Timestamp,
                FillPrice = primaryFill.FillPrice,
                FillQuantity = fillEvents.Sum(f => f.Quantity),
                Commission = fillEvents.Sum(f => f.Commission),
                Exchange = primaryFill.Exchange,
                LiquidityType = primaryFill.LiquidityType
            };
        }

        private async Task PersistFillToDatabaseAsync(FillRecord fillRecord)
        {
            const string insertSql = @"
                INSERT OR REPLACE INTO ExecutionVerification 
                (OrderId, FillPrice, FillQuantity, FillTime, Commission, Exchange, LiquidityType, VerificationTime)
                VALUES (@OrderId, @FillPrice, @FillQuantity, @FillTime, @Commission, @Exchange, @LiquidityType, @VerificationTime)";

            using var command = new SQLiteCommand(insertSql, _database);
            command.Parameters.AddWithValue("@OrderId", fillRecord.OrderId);
            command.Parameters.AddWithValue("@FillPrice", fillRecord.FillPrice);
            command.Parameters.AddWithValue("@FillQuantity", fillRecord.FillQuantity);
            command.Parameters.AddWithValue("@FillTime", fillRecord.FillTime.ToString("O"));
            command.Parameters.AddWithValue("@Commission", fillRecord.Commission);
            command.Parameters.AddWithValue("@Exchange", fillRecord.Exchange);
            command.Parameters.AddWithValue("@LiquidityType", fillRecord.LiquidityType);
            command.Parameters.AddWithValue("@VerificationTime", DateTime.UtcNow.ToString("O"));

            await Task.Run(() => command.ExecuteNonQuery());
        }

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _reconciliationTimer?.Dispose();
                    _database?.Dispose();
                    
                    _logger.LogInformation("ExecutionVerificationSystem disposed");
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}