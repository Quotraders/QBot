using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Services;
using BotCore.Execution;
using Zones;
using TradingBot.Abstractions;

namespace BotCore.Services
{
    /// <summary>
    /// Market data to zone service bridge - feeds bar data to zone detection engine
    /// </summary>
    public class ZoneMarketDataBridge : IHostedService, IDisposable
    {
        private readonly ILogger<ZoneMarketDataBridge> _logger;
        private readonly IServiceProvider _serviceProvider;
        private IEnhancedMarketDataFlowService? _marketDataService;
        private IZoneService? _zoneService;
        private bool _disposed;

        public ZoneMarketDataBridge(
            ILogger<ZoneMarketDataBridge> logger,
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
                _marketDataService = _serviceProvider.GetService<IEnhancedMarketDataFlowService>();
                _zoneService = _serviceProvider.GetService<IZoneService>();

                if (_marketDataService == null)
                {
                    _logger.LogWarning("[ZONE-BRIDGE] Enhanced market data service not available - zone data feed disabled");
                    return Task.CompletedTask;
                }

                if (_zoneService == null)
                {
                    _logger.LogWarning("[ZONE-BRIDGE] Zone service not available - zone data feed disabled");
                    return Task.CompletedTask;
                }

                // Subscribe to market data events
                _marketDataService.OnMarketDataReceived += OnMarketDataReceived;
                
                _logger.LogInformation("🔗 [ZONE-BRIDGE] Market data to zone service bridge activated");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-BRIDGE] Failed to start zone market data bridge");
                return Task.CompletedTask;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_marketDataService != null)
            {
                _marketDataService.OnMarketDataReceived -= OnMarketDataReceived;
            }

            _logger.LogInformation("[ZONE-BRIDGE] Market data to zone service bridge stopped");
            return Task.CompletedTask;
        }

        private void OnMarketDataReceived(string symbol, object data)
        {
            if (_zoneService == null || string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            try
            {
                // Handle different types of market data
                switch (data)
                {
                    case MarketData marketData:
                        ProcessMarketData(marketData);
                        break;
                    case BotCore.Execution.Quote quote:
                        ProcessQuoteData(symbol, quote);
                        break;
                    default:
                        _logger.LogTrace("[ZONE-BRIDGE] Ignoring unsupported data type: {DataType}", data.GetType().Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-BRIDGE] Error processing market data for {Symbol}", symbol);
            }
        }

        private void ProcessMarketData(MarketData marketData)
        {
            if (_zoneService == null) return;

            // If this is OHLCV bar data, feed to zone service
            if (marketData.Open != 0 && marketData.High != 0 && 
                marketData.Low != 0 && marketData.Close != 0)
            {
                _zoneService.OnBar(
                    marketData.Symbol, 
                    marketData.Timestamp, 
                    (decimal)marketData.Open, 
                    (decimal)marketData.High, 
                    (decimal)marketData.Low, 
                    (decimal)marketData.Close, 
                    (long)(marketData.Volume));

                _logger.LogTrace("[ZONE-BRIDGE] Fed bar data to zone service: {Symbol} OHLCV", marketData.Symbol);
            }
            // If this is tick data with bid/ask, feed as tick
            else if (marketData.Bid != 0 && marketData.Ask != 0)
            {
                _zoneService.OnTick(
                    marketData.Symbol,
                    (decimal)marketData.Bid,
                    (decimal)marketData.Ask,
                    marketData.Timestamp);

                _logger.LogTrace("[ZONE-BRIDGE] Fed tick data to zone service: {Symbol} bid/ask", marketData.Symbol);
            }
        }

        private void ProcessQuoteData(string symbol, BotCore.Execution.Quote quote)
        {
            if (_zoneService == null) return;

            _zoneService.OnTick(symbol, quote.BidPrice, quote.AskPrice, quote.Timestamp);
            _logger.LogTrace("[ZONE-BRIDGE] Fed quote data to zone service: {Symbol}", symbol);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_marketDataService != null)
                {
                    _marketDataService.OnMarketDataReceived -= OnMarketDataReceived;
                }
                _disposed = true;
            }
        }
    }
}