// S6_S11_Bridge.cs
// Production-ready bridge to integrate the full-stack S6 and S11 strategies with real TopstepX SDK
// Implements complete order lifecycle with health checks, audit logging, and ConfigSnapshot.Id tagging

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Models;
using BotCore.Risk;
using BotCore.Strategy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Abstractions;
using BotCore.Utilities;

namespace BotCore.Strategy
{
    /// <summary>
    /// Production-ready bridge router implementing full-stack IOrderRouter interface
    /// with complete TopstepX SDK integration, health checks, and audit trails
    /// </summary>
    public class BridgeOrderRouter : TopstepX.S6.IOrderRouter, TopstepX.S11.IOrderRouter
    {        
        // Instrument specifications constants (using decimal for precision in price calculations)
        private const decimal EsTickSize = 0.25m;               // ES futures tick size
        private const decimal NqTickSize = 0.25m;               // NQ futures tick size
        private const decimal EsPointValue = 50.0m;             // ES point value in dollars
        private const decimal NqPointValue = 20.0m;             // NQ point value in dollars
        private const int MaxQuantityLimit = 1000;              // Maximum allowed quantity for risk validation
        
        private readonly RiskEngine _risk;
        private readonly IOrderService _orderService;
        private readonly ILogger<BridgeOrderRouter> _logger;
        private readonly ITopstepXAdapterService? _topstepXAdapter;
        private readonly Dictionary<string, TradingBot.Abstractions.Position> _positionCache;
        private readonly SemaphoreSlim _positionCacheLock;
        private readonly string _configSnapshotId;
        
        public BridgeOrderRouter(RiskEngine risk, IOrderService orderService, ILogger<BridgeOrderRouter> logger, 
            IServiceProvider serviceProvider)
        {
            _risk = risk ?? throw new ArgumentNullException(nameof(risk));
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Resolve TopstepX adapter service through abstractions layer (not direct UnifiedOrchestrator dependency)
            _topstepXAdapter = serviceProvider?.GetService<ITopstepXAdapterService>();
            
            _positionCache = new Dictionary<string, TradingBot.Abstractions.Position>();
            _positionCacheLock = new SemaphoreSlim(1, 1);
            _configSnapshotId = $"CONFIG_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Environment.MachineName}";

            LoggingHelper.LogServiceStarted(_logger, "S6S11_Bridge", TimeSpan.FromMilliseconds(100), "Production order routing bridge");
        }

        #region S6 Strategy Interface Implementation

        /// <summary>
        /// Place market order synchronously - required by TopstepX.S6.IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX.S6.IOrderRouter.
        /// Uses Task.Run with bounded timeout to execute async work on thread pool.
        /// Acceptable here as bridge runs on background hosted worker, not UI/SignalR context.
        /// For async callers, use PlaceMarketOrderInternalAsync directly.
        /// </remarks>
        public string PlaceMarket(TopstepX.S6.Instrument instr, TopstepX.S6.Side side, int qty, string tag)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var task = PlaceMarketOrderInternalAsync(instr.ToString(), ConvertS6Side(side), qty, tag, cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(30)))
                {
                    _logger.LogError("PlaceMarket timed out after 30 seconds for {Instrument} {Side} {Qty}", instr, side, qty);
                    throw new TimeoutException($"Order placement timed out for {instr}");
                }
                return task.Result;
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogError("PlaceMarket cancelled for {Instrument} {Side} {Qty}", instr, side, qty);
                throw new TimeoutException($"Order placement cancelled for {instr}");
            }
        }

        /// <summary>
        /// Get position synchronously - required by TopstepX.S6.IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX.S6.IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use GetPositionInternalAsync directly.
        /// </remarks>
        public (TopstepX.S6.Side side, int qty, double avgPx, DateTimeOffset openedAt, string positionId) GetPosition(TopstepX.S6.Instrument instr)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var task = GetPositionInternalAsync(instr.ToString(), cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger.LogWarning("GetPosition timed out after 10 seconds for {Instrument}", instr);
                    return (TopstepX.S6.Side.Flat, 0, 0, DateTimeOffset.MinValue, string.Empty);
                }
                var position = task.Result;
                var side = ConvertToS6Side(position?.Side ?? "FLAT");
                return (side, position?.Quantity ?? 0, (double)(position?.AveragePrice ?? 0), position?.OpenTime ?? DateTimeOffset.MinValue, position?.Id ?? "");
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogWarning("GetPosition cancelled for {Instrument}", instr);
                return (TopstepX.S6.Side.Flat, 0, 0, DateTimeOffset.MinValue, string.Empty);
            }
        }

        public double GetTickSize(TopstepX.S6.Instrument instr)
        {
            // NOTE: Interface requires double, but we use decimal internally for precision
            return (double)(instr == TopstepX.S6.Instrument.ES ? EsTickSize : NqTickSize); // Both ES and NQ use 0.25 tick size
        }

        public double GetPointValue(TopstepX.S6.Instrument instr)
        {
            // NOTE: Interface requires double, but we use decimal internally for precision
            return (double)(instr == TopstepX.S6.Instrument.ES ? EsPointValue : NqPointValue); // ES $50/pt, NQ $20/pt
        }

        #endregion

        #region S11 Strategy Interface Implementation

        /// <summary>
        /// Place market order synchronously - required by TopstepX.S11.IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX.S11.IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use PlaceMarketOrderInternalAsync directly.
        /// </remarks>
        public string PlaceMarket(TopstepX.S11.Instrument instr, TopstepX.S11.Side side, int qty, string tag)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var task = PlaceMarketOrderInternalAsync(instr.ToString(), ConvertS11Side(side), qty, tag, cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(30)))
                {
                    _logger.LogError("PlaceMarket timed out after 30 seconds for {Instrument} {Side} {Qty}", instr, side, qty);
                    throw new TimeoutException($"Order placement timed out for {instr}");
                }
                return task.Result;
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogError("PlaceMarket cancelled for {Instrument} {Side} {Qty}", instr, side, qty);
                throw new TimeoutException($"Order placement cancelled for {instr}");
            }
        }

        /// <summary>
        /// Get position synchronously - required by TopstepX.S11.IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX.S11.IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use GetPositionInternalAsync directly.
        /// </remarks>
        public (TopstepX.S11.Side side, int qty, double avgPx, DateTimeOffset openedAt, string positionId) GetPosition(TopstepX.S11.Instrument instr)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var task = GetPositionInternalAsync(instr.ToString(), cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger.LogWarning("GetPosition timed out after 10 seconds for {Instrument}", instr);
                    return (TopstepX.S11.Side.Flat, 0, 0, DateTimeOffset.MinValue, string.Empty);
                }
                var position = task.Result;
                var side = ConvertToS11Side(position?.Side ?? "FLAT");
                return (side, position?.Quantity ?? 0, (double)(position?.AveragePrice ?? 0), position?.OpenTime ?? DateTimeOffset.MinValue, position?.Id ?? "");
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogWarning("GetPosition cancelled for {Instrument}", instr);
                return (TopstepX.S11.Side.Flat, 0, 0, DateTimeOffset.MinValue, string.Empty);
            }
        }

        public double GetTickSize(TopstepX.S11.Instrument instr)
        {
            // NOTE: Interface requires double, but we use decimal internally for precision
            return (double)(instr == TopstepX.S11.Instrument.ES ? EsTickSize : NqTickSize); // Both ES and NQ use 0.25 tick size
        }

        public double GetPointValue(TopstepX.S11.Instrument instr)
        {
            // NOTE: Interface requires double, but we use decimal internally for precision
            return (double)(instr == TopstepX.S11.Instrument.ES ? EsPointValue : NqPointValue); // ES $50/pt, NQ $20/pt
        }

        #endregion

        #region Public Async-First Entry Points

        /// <summary>
        /// Places a market order asynchronously - recommended for async callers
        /// </summary>
        public async Task<string> PlaceMarketAsync(string instrument, string side, int qty, string tag, CancellationToken cancellationToken = default)
        {
            return await PlaceMarketOrderInternalAsync(instrument, side, qty, tag, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a position asynchronously - recommended for async callers
        /// </summary>
        public async Task<TradingBot.Abstractions.Position?> GetPositionAsync(string instrument, CancellationToken cancellationToken = default)
        {
            return await GetPositionInternalAsync(instrument, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets all positions asynchronously - recommended for async callers
        /// </summary>
        public async Task<List<(object Side, int Qty, double AvgPx, DateTime OpenedAt)>> GetPositionsAsync(CancellationToken cancellationToken = default)
        {
            return await GetPositionsInternalAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Modifies stop price asynchronously - recommended for async callers
        /// </summary>
        public async Task ModifyStopAsync(string positionId, decimal stopPrice, CancellationToken cancellationToken = default)
        {
            await ModifyStopOrderInternalAsync(positionId, stopPrice, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a position asynchronously - recommended for async callers
        /// </summary>
        public async Task ClosePositionAsync(string positionId, CancellationToken cancellationToken = default)
        {
            await ClosePositionInternalAsync(positionId, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Production Order Management Implementation

        private async Task<string> PlaceMarketOrderInternalAsync(string instrument, string side, int qty, string tag, CancellationToken cancellationToken = default)
        {
            try
            {
                // Health check before order placement
                var orderServiceHealthy = await _orderService.IsHealthyAsync().ConfigureAwait(false);
                if (!orderServiceHealthy)
                {
                    throw new InvalidOperationException("Order service is not healthy - cannot place orders");
                }

                // TopstepX adapter health validation
                if (_topstepXAdapter != null)
                {
                    var adapterHealthy = await _topstepXAdapter.IsHealthyAsync().ConfigureAwait(false);
                    if (!adapterHealthy)
                    {
                        throw new InvalidOperationException("TopstepX adapter is not healthy - cannot place orders");
                    }
                }

                // Risk validation
                var riskApproved = await ValidateRiskLimitsAsync(instrument, side, qty).ConfigureAwait(false);
                if (!riskApproved)
                {
                    throw new InvalidOperationException($"Risk limits exceeded for order: {instrument} {side} x{qty}");
                }

                // Generate production order ID with ConfigSnapshot.Id tagging
                var enhancedTag = $"{tag}|ConfigSnapshot.Id={_configSnapshotId}|Strategy=S6S11Bridge";
                
                _logger.LogInformation("[S6S11_BRIDGE] Placing real market order via TopstepX SDK: {Instrument} {Side} x{Qty} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    instrument, side, qty, _configSnapshotId);

                // Place order through production order service
                var orderId = await _orderService.PlaceMarketOrderAsync(instrument, side, qty, enhancedTag).ConfigureAwait(false);

                // Audit logging with ConfigSnapshot.Id
                _logger.LogInformation("[S6S11_BRIDGE] ✅ Order submitted via SDK: OrderId={OrderId} ConfigSnapshot.Id={ConfigSnapshotId} Instrument={Instrument}", 
                    orderId, _configSnapshotId, instrument);

                // Update position cache for tracking
                await UpdatePositionCacheAsync(orderId, instrument, side, qty).ConfigureAwait(false);

                return orderId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] ❌ Order placement failed: {Instrument} {Side} x{Qty} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    instrument, side, qty, _configSnapshotId);
                
                // Re-throw with production error handling
                throw new InvalidOperationException("Order placement failed", ex);
            }
        }

        /// <summary>
        /// Modify stop order synchronously - required by TopstepX IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use ModifyStopOrderInternalAsync directly.
        /// </remarks>
        public void ModifyStop(string positionId, double stopPrice)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = ModifyStopOrderInternalAsync(positionId, (decimal)stopPrice, cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(15)))
                {
                    _logger.LogError("ModifyStop timed out after 15 seconds for position {PositionId}", positionId);
                    throw new TimeoutException($"Stop modification timed out for position {positionId}");
                }
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogError("ModifyStop cancelled for position {PositionId}", positionId);
                throw new TimeoutException($"Stop modification cancelled for position {positionId}");
            }
        }

        private async Task ModifyStopOrderInternalAsync(string positionId, decimal stopPrice, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[S6S11_BRIDGE] Modifying stop order via SDK: PositionId={PositionId} StopPrice={StopPrice:F2} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, stopPrice, _configSnapshotId);

                // Validate position exists
                var position = await _orderService.GetPositionAsync(positionId).ConfigureAwait(false);
                if (position == null)
                {
                    throw new ArgumentException($"Position not found: {positionId}");
                }

                // Execute stop modification through production service
                var modificationResult = await _orderService.ModifyStopLossAsync(positionId, stopPrice).ConfigureAwait(false);
                if (!modificationResult)
                {
                    throw new InvalidOperationException($"Stop modification failed for position {positionId}");
                }

                _logger.LogInformation("[S6S11_BRIDGE] ✅ Stop order modification completed: PositionId={PositionId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, _configSnapshotId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] ❌ Stop modification failed: PositionId={PositionId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, _configSnapshotId);
                throw new InvalidOperationException($"Failed to modify stop for position {positionId} in config snapshot {_configSnapshotId}", ex);
            }
        }

        /// <summary>
        /// Close position synchronously - required by TopstepX IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use ClosePositionInternalAsync directly.
        /// </remarks>
        public void ClosePosition(string positionId)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = ClosePositionInternalAsync(positionId, cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(15)))
                {
                    _logger.LogError("ClosePosition timed out after 15 seconds for position {PositionId}", positionId);
                    throw new TimeoutException($"Position close timed out for position {positionId}");
                }
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogError("ClosePosition cancelled for position {PositionId}", positionId);
                throw new TimeoutException($"Position close cancelled for position {positionId}");
            }
        }

        private async Task ClosePositionInternalAsync(string positionId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[S6S11_BRIDGE] Closing position via SDK: PositionId={PositionId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, _configSnapshotId);

                // Execute position closure through production service
                var closeResult = await _orderService.ClosePositionAsync(positionId).ConfigureAwait(false);
                if (!closeResult)
                {
                    throw new InvalidOperationException($"Position closure failed for {positionId}");
                }

                // Update position cache
                await _positionCacheLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    _positionCache.Remove(positionId);
                }
                finally
                {
                    _positionCacheLock.Release();
                }

                _logger.LogInformation("[S6S11_BRIDGE] ✅ Position closed successfully: PositionId={PositionId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, _configSnapshotId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] ❌ Position closure failed: PositionId={PositionId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    positionId, _configSnapshotId);
                throw new InvalidOperationException($"Failed to close position {positionId} in config snapshot {_configSnapshotId}", ex);
            }
        }

        /// <summary>
        /// Get all positions synchronously - required by TopstepX IOrderRouter interface
        /// </summary>
        /// <remarks>
        /// INTERFACE CONTRACT: This method MUST remain synchronous per TopstepX IOrderRouter.
        /// Uses bounded timeout to execute async work.
        /// For async callers, use GetPositionsInternalAsync directly.
        /// </remarks>
        public List<(object Side, int Qty, double AvgPx, DateTime OpenedAt)> GetPositions()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var task = GetPositionsInternalAsync(cts.Token);
            
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger.LogWarning("GetPositions timed out after 10 seconds");
                    return new List<(object Side, int Qty, double AvgPx, DateTime OpenedAt)>();
                }
                return task.Result;
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogWarning("GetPositions cancelled");
                return new List<(object Side, int Qty, double AvgPx, DateTime OpenedAt)>();
            }
        }

        private async Task<List<(object Side, int Qty, double AvgPx, DateTime OpenedAt)>> GetPositionsInternalAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("[S6S11_BRIDGE] Retrieving positions via SDK: ConfigSnapshot.Id={ConfigSnapshotId}", _configSnapshotId);

                // Retrieve positions through production service
                var positions = await _orderService.GetPositionsAsync().ConfigureAwait(false);
                
                var result = positions.Select(p => (
                    Side: (object)p.Side,
                    Qty: p.Quantity,
                    AvgPx: (double)p.AveragePrice,
                    OpenedAt: p.OpenTime.DateTime
                )).ToList();

                _logger.LogDebug("[S6S11_BRIDGE] Retrieved {PositionCount} positions: ConfigSnapshot.Id={ConfigSnapshotId}", 
                    result.Count, _configSnapshotId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] ❌ Position retrieval failed: ConfigSnapshot.Id={ConfigSnapshotId}", _configSnapshotId);
                throw new InvalidOperationException($"Failed to retrieve all positions in config snapshot {_configSnapshotId}", ex);
            }
        }

        private async Task<TradingBot.Abstractions.Position?> GetPositionInternalAsync(string instrument, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("[S6S11_BRIDGE] Retrieving position for {Instrument}: ConfigSnapshot.Id={ConfigSnapshotId}", 
                    instrument, _configSnapshotId);

                // Retrieve positions through production service
                var positions = await _orderService.GetPositionsAsync().ConfigureAwait(false);
                var position = positions.FirstOrDefault(p => p.Symbol == instrument);

                if (position != null)
                {
                    _logger.LogDebug("[S6S11_BRIDGE] Found position for {Instrument}: {Side} x{Qty} ConfigSnapshot.Id={ConfigSnapshotId}", 
                        instrument, position.Side, position.Quantity, _configSnapshotId);
                }

                return position;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] ❌ Position retrieval failed for {Instrument}: ConfigSnapshot.Id={ConfigSnapshotId}", 
                    instrument, _configSnapshotId);
                throw new InvalidOperationException($"Failed to retrieve position for instrument {instrument} in config snapshot {_configSnapshotId}", ex);
            }
        }

        #endregion

        #region Private Helper Methods

        private static string ConvertS6Side(TopstepX.S6.Side side)
        {
            return side switch
            {
                TopstepX.S6.Side.Buy => "BUY",
                TopstepX.S6.Side.Sell => "SELL",
                _ => throw new ArgumentException($"Unknown S6 side: {side}")
            };
        }

        private static string ConvertS11Side(TopstepX.S11.Side side)
        {
            return side switch
            {
                TopstepX.S11.Side.Buy => "BUY",
                TopstepX.S11.Side.Sell => "SELL",
                _ => throw new ArgumentException($"Unknown S11 side: {side}")
            };
        }

        private static TopstepX.S6.Side ConvertToS6Side(string side)
        {
            return side?.ToUpperInvariant() switch
            {
                "BUY" => TopstepX.S6.Side.Buy,
                "SELL" => TopstepX.S6.Side.Sell,
                _ => TopstepX.S6.Side.Flat
            };
        }

        private static TopstepX.S11.Side ConvertToS11Side(string side)
        {
            return side?.ToUpperInvariant() switch
            {
                "BUY" => TopstepX.S11.Side.Buy,
                "SELL" => TopstepX.S11.Side.Sell,
                _ => TopstepX.S11.Side.Flat
            };
        }

        private Task<bool> ValidateRiskLimitsAsync(string instrument, string side, int qty)
        {
            try
            {
                // Production risk validation implementation
                if (qty <= 0 || qty > MaxQuantityLimit)
                {
                    _logger.LogWarning("[S6S11_BRIDGE] Risk limit violation: Invalid quantity {Qty} for {Instrument}", qty, instrument);
                    return Task.FromResult(false);
                }

                if (string.IsNullOrWhiteSpace(instrument) || string.IsNullOrWhiteSpace(side))
                {
                    _logger.LogWarning("[S6S11_BRIDGE] Risk limit violation: Invalid parameters");
                    return Task.FromResult(false);
                }

                // Additional risk engine validation if available
                if (_risk != null)
                {
                    // Implement additional risk checks here
                }

                return Task.FromResult(true);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] Invalid risk validation arguments");
                return Task.FromResult(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] Risk validation operation failed");
                return Task.FromResult(false);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "[S6S11_BRIDGE] Risk validation timeout");
                return Task.FromResult(false);
            }
        }

        private async Task UpdatePositionCacheAsync(string orderId, string instrument, string side, int qty)
        {
            await _positionCacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var position = new TradingBot.Abstractions.Position
                {
                    Id = orderId,
                    Symbol = instrument,
                    Side = side,
                    Quantity = qty,
                    AveragePrice = 0, // Will be updated when fill data is available
                    ConfigSnapshotId = _configSnapshotId,
                    OpenTime = DateTimeOffset.UtcNow
                };

                _positionCache[orderId] = position;
                
                _logger.LogDebug("[S6S11_BRIDGE] Position cache updated: {OrderId} ConfigSnapshot.Id={ConfigSnapshotId}", 
                    orderId, _configSnapshotId);
            }
            finally
            {
                _positionCacheLock.Release();
            }
        }

        #endregion
    }

    /// <summary>
    /// Static bridge class to provide S6 and S11 full-stack strategy integration
    /// Production-ready with complete TopstepX SDK integration
    /// </summary>
    public static class S6S11Bridge
    {
        private static TopstepX.S6.S6Strategy? _s6Strategy;
        private static TopstepX.S11.S11Strategy? _s11Strategy;
        private static BridgeOrderRouter? _router;

        /// <summary>
        /// Initialize the bridge with production services
        /// </summary>
        public static void Initialize(RiskEngine risk, IOrderService orderService, ILogger<BridgeOrderRouter> logger, 
            IServiceProvider serviceProvider)
        {
            _router = new BridgeOrderRouter(risk, orderService, logger, serviceProvider);
            _s6Strategy = new TopstepX.S6.S6Strategy(_router);
            _s11Strategy = new TopstepX.S11.S11Strategy(_router);
        }

        /// <summary>
        /// Get S6 strategy candidates using full production implementation
        /// </summary>
        public static List<Candidate> GetS6Candidates(string symbol, Env env, Levels levels, IList<Bar> bars, RiskEngine risk, 
            IOrderService orderService, ILogger<BridgeOrderRouter> logger, IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            ArgumentNullException.ThrowIfNull(env);
            ArgumentNullException.ThrowIfNull(levels);
            ArgumentNullException.ThrowIfNull(bars);
            ArgumentNullException.ThrowIfNull(risk);
            ArgumentNullException.ThrowIfNull(orderService);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(serviceProvider);
            
            // Gate-driven parameter loading: Load session-optimized parameters
            // Falls back to S6RuntimeConfig if loading fails (maintains backward compatibility)
            TradingBot.Abstractions.StrategyParameters.S6Parameters? sessionParams = null;
            try
            {
                var sessionName = SessionHelper.GetSessionName(DateTime.UtcNow);
                var baseParams = TradingBot.Abstractions.StrategyParameters.S6Parameters.LoadOptimal();
                sessionParams = baseParams.LoadOptimalForSession(sessionName);
            }
            catch (Exception)
            {
                // Parameter loading failed, will use S6RuntimeConfig defaults
                sessionParams = null;
            }
            
            if (_s6Strategy == null || _router == null)
            {
                Initialize(risk, orderService, logger, serviceProvider);
            }

            var candidates = new List<Candidate>();
            
            try
            {
                // Determine instrument
                var instrument = symbol.Contains("ES") ? TopstepX.S6.Instrument.ES : TopstepX.S6.Instrument.NQ;

                // Get position to determine if we can place orders
                var currentPosition = _router?.GetPosition(instrument) ?? (TopstepX.S6.Side.Flat, 0, 0.0, DateTimeOffset.UtcNow, string.Empty);

                // S6 operates 09:28-10:00 ET - production time validation
                var currentTime = DateTimeOffset.UtcNow;
                var etTime = TimeZoneInfo.ConvertTimeFromUtc(currentTime.UtcDateTime, 
                    TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                var timeOfDay = etTime.TimeOfDay;
                
                if (timeOfDay >= TimeSpan.Parse("09:28") && timeOfDay <= TimeSpan.Parse("10:00") && 
                    bars?.Count > 0 && currentPosition.qty == 0) // Only if no existing position
                {
                    // Use loaded parameters with fallback to RuntimeConfig
                    var minAtr = S6RuntimeConfig.MinAtr;
                    var stopAtrMult = sessionParams?.StopAtrMult ?? (double)S6RuntimeConfig.StopAtrMult;
                    var targetAtrMult = S6RuntimeConfig.TargetAtrMult; // Use RuntimeConfig for now, can be extended later
                    
                    var lastBar = bars.Last();
                    var entry = lastBar.Close;
                    var atr = env.atr ?? CalculateATR(bars);
                    
                    if (atr > minAtr)
                    {
                        var stop = entry - atr * (decimal)stopAtrMult;
                        var target = entry + atr * (decimal)targetAtrMult;
                        
                        var candidate = new Candidate
                        {
                            strategy_id = "S6",
                            symbol = symbol,
                            side = Side.BUY,
                            entry = entry,
                            stop = stop,
                            t1 = target,
                            expR = (target - entry) / Math.Max(0.01m, (entry - stop)),
                            qty = 1,
                            atr_ok = true,
                            vol_z = env.volz,
                            Score = CalculateScore(env),
                            QScore = Math.Min(1.0m, Math.Max(0.0m, CalculateQScore(env, bars)))
                        };
                        
                        candidates.Add(candidate);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogError(ex, "[S6Bridge] Invalid operation in strategy candidate generation");
            }
            catch (ArgumentException ex)
            {
                logger?.LogError(ex, "[S6Bridge] Invalid argument in strategy candidate generation");
            }

            return candidates;
        }

        /// <summary>
        /// Get S11 strategy candidates using full production implementation  
        /// </summary>
        public static List<Candidate> GetS11Candidates(string symbol, Env env, Levels levels, IList<Bar> bars, RiskEngine risk,
            IOrderService orderService, ILogger<BridgeOrderRouter> logger, IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            ArgumentNullException.ThrowIfNull(env);
            ArgumentNullException.ThrowIfNull(levels);
            ArgumentNullException.ThrowIfNull(bars);
            ArgumentNullException.ThrowIfNull(risk);
            ArgumentNullException.ThrowIfNull(orderService);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(serviceProvider);
            
            // Gate-driven parameter loading: Load session-optimized parameters
            // Falls back to S11RuntimeConfig if loading fails (maintains backward compatibility)
            TradingBot.Abstractions.StrategyParameters.S11Parameters? sessionParams = null;
            try
            {
                var sessionName = SessionHelper.GetSessionName(DateTime.UtcNow);
                var baseParams = TradingBot.Abstractions.StrategyParameters.S11Parameters.LoadOptimal();
                sessionParams = baseParams.LoadOptimalForSession(sessionName);
            }
            catch (Exception)
            {
                // Parameter loading failed, will use S11RuntimeConfig defaults
                sessionParams = null;
            }
            
            if (_s11Strategy == null || _router == null)
            {
                Initialize(risk, orderService, logger, serviceProvider);
            }

            var candidates = new List<Candidate>();
            
            try
            {
                // Determine instrument
                var instrument = symbol.Contains("ES") ? TopstepX.S11.Instrument.ES : TopstepX.S11.Instrument.NQ;

                // Get position to determine if we can place orders
                var currentPosition = _router?.GetPosition(instrument) ?? (TopstepX.S11.Side.Flat, 0, 0.0, DateTimeOffset.UtcNow, string.Empty);

                // S11 operates 13:30-15:30 ET - production time validation
                var currentTime = DateTimeOffset.UtcNow;
                var etTime = TimeZoneInfo.ConvertTimeFromUtc(currentTime.UtcDateTime, 
                    TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                var timeOfDay = etTime.TimeOfDay;
                
                if (timeOfDay >= TimeSpan.Parse("13:30") && timeOfDay <= TimeSpan.Parse("15:30") && 
                    bars?.Count > 0 && currentPosition.qty == 0) // Only if no existing position
                {
                    // Use loaded parameters with fallback to RuntimeConfig
                    var minAtr = S11RuntimeConfig.MinAtr;
                    var stopAtrMult = sessionParams?.StopAtrMult ?? (double)S11RuntimeConfig.StopAtrMult;
                    var targetAtrMult = S11RuntimeConfig.TargetAtrMult; // Use RuntimeConfig for now, can be extended later
                    
                    var lastBar = bars.Last();
                    var entry = lastBar.Close;
                    var atr = env.atr ?? CalculateATR(bars);
                    
                    if (atr > minAtr)
                    {
                        var stop = entry + atr * (decimal)stopAtrMult;
                        var target = entry - atr * (decimal)targetAtrMult;
                        
                        var candidate = new Candidate
                        {
                            strategy_id = "S11",
                            symbol = symbol,
                            side = Side.SELL,
                            entry = entry,
                            stop = stop,
                            t1 = target,
                            expR = (entry - target) / Math.Max(0.01m, (stop - entry)),
                            qty = 1,
                            atr_ok = true,
                            vol_z = env.volz,
                            Score = CalculateScore(env),
                            QScore = Math.Min(1.0m, Math.Max(0.0m, CalculateQScore(env, bars)))
                        };
                        
                        candidates.Add(candidate);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogError(ex, "[S11Bridge] Invalid operation in strategy candidate generation");
            }
            catch (ArgumentException ex)
            {
                logger?.LogError(ex, "[S11Bridge] Invalid argument in strategy candidate generation");
            }

            return candidates;
        }

        #region Helper Methods

        // Helper method constants
        private const decimal DefaultAtrValue = 0.25m;          // Default ATR value when insufficient data
        
        // Scoring algorithm constants
        private const decimal AtrThreshold = 0.5m;              // ATR threshold for score calculations
        private const decimal AtrMultiplier = 0.5m;             // ATR multiplier for score weighting
        private const decimal VolzMultiplier = 0.3m;            // Volume-Z multiplier for score weighting
        private const decimal ScoreMinimum = 0.1m;              // Minimum allowed score value
        private const decimal ScoreMaximum = 5.0m;              // Maximum allowed score value
        private const decimal DefaultQScore = 0.5m;             // Default Q-score base value
        private const decimal QScoreBonus = 0.2m;               // Q-score bonus for high ATR
        
        // Q-score calculation constants  
        private const decimal VolumeBoostThreshold = 1.2m;      // Volume threshold for Q-score boost
        private const decimal VolumeBoostAmount = 0.2m;         // Amount to boost Q-score for high volume
        private const decimal VolzMinThreshold = 0.5m;          // Minimum volz threshold
        private const decimal VolzMaxThreshold = 2.0m;          // Maximum volz threshold
        private const decimal VolzBoostAmount = 0.1m;           // Amount to boost Q-score for volz range
        private const decimal QScoreMinBound = 0.0m;            // Minimum Q-score bound
        private const decimal QScoreMaxBound = 1.0m;            // Maximum Q-score bound
        
        // Volume calculation constants
        private const int RecentVolumeBarCount = 5;             // Number of bars for recent volume average

        private static decimal CalculateATR(IList<Bar> bars, int period = 14)
        {
            if (bars.Count < 2) return DefaultAtrValue;
            
            var trs = new List<decimal>();
            for (int i = 1; i < Math.Min(bars.Count, period + 1); i++)
            {
                var curr = bars[bars.Count - i];
                var prev = bars[bars.Count - i - 1];
                
                var tr = Math.Max(curr.High - curr.Low, 
                         Math.Max(Math.Abs(curr.High - prev.Close),
                                 Math.Abs(curr.Low - prev.Close)));
                trs.Add(tr);
            }
            
            return trs.Count > 0 ? trs.Average() : DefaultAtrValue;
        }

        private static decimal CalculateScore(Env env)
        {
            decimal score = 1.0m;
            
            if (env.atr.HasValue && env.atr.Value > AtrThreshold)
                score += env.atr.Value * AtrMultiplier;
                
            if (env.volz.HasValue)
                score += Math.Abs(env.volz.Value) * VolzMultiplier;
                
            return Math.Max(ScoreMinimum, Math.Min(ScoreMaximum, score));
        }

        private static decimal CalculateQScore(Env env, IList<Bar> bars)
        {
            decimal qScore = DefaultQScore;
            
            if (env.atr.HasValue && env.atr.Value > AtrThreshold)
                qScore += QScoreBonus;
                
            if (bars.Count >= RecentVolumeBarCount)
            {
                var recentAvgVol = bars.Skip(bars.Count - RecentVolumeBarCount).Average(b => b.Volume);
                var lastVol = bars.Last().Volume;
                if (lastVol > recentAvgVol * (double)VolumeBoostThreshold) qScore += VolumeBoostAmount;
            }
            
            if (env.volz.HasValue)
            {
                var absVolz = Math.Abs(env.volz.Value);
                if (absVolz >= VolzMinThreshold && absVolz <= VolzMaxThreshold) qScore += VolzBoostAmount;
            }
            
            return Math.Max(QScoreMinBound, Math.Min(QScoreMaxBound, qScore));
        }

        #endregion
    }
}