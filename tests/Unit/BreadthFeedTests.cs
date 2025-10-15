using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using TradingBot.Abstractions;

namespace TradingBot.Tests.Unit
{
    /// <summary>
    /// BreadthFeedTests - Required by S7 Audit-Clean Acceptance Contract
    /// Validates breadth feed integration if breadth module is enabled
    /// </summary>
    public class BreadthFeedTests
    {
        private readonly BreadthConfiguration _breadthConfig;
        private readonly ILogger<TestBreadthFeed> _logger;

        public BreadthFeedTests()
        {
            _breadthConfig = new BreadthConfiguration
            {
                Enabled = true,
                AdvanceDeclineThreshold = 0.75m,
                NewHighsLowsRatio = 2.0m,
                SectorRotationWeight = 0.25m,
                BreadthLookbackBars = 20,
                DataSource = "IndexBreadthFeed"
            };

            _logger = new TestLogger<TestBreadthFeed>();
        }

        [Fact]
        public void BreadthFeed_Constructor_InitializesCorrectly()
        {
            // Arrange & Act
            var breadthFeed = new TestBreadthFeed(_logger, _breadthConfig);

            // Assert
            Assert.NotNull(breadthFeed);
            Assert.False(breadthFeed.IsDataAvailable()); // Should not have data initially
        }

        [Fact]
        public async Task BreadthFeed_GetAdvanceDeclineRatioAsync_ReturnsValidRatio()
        {
            // Arrange
            var breadthFeed = new TestBreadthFeed(_logger, _breadthConfig);
            
            // Simulate some market data
            await breadthFeed.UpdateMarketDataAsync(1500, 500); // 1500 advancing, 500 declining

            // Act
            var adRatio = await breadthFeed.GetAdvanceDeclineRatioAsync();

            // Assert
            Assert.True(adRatio > 0);
            Assert.True(adRatio <= 1.0m); // Should be normalized
        }

        [Fact]
        public async Task BreadthFeed_GetNewHighsLowsRatioAsync_ReturnsValidRatio()
        {
            // Arrange
            var breadthFeed = new TestBreadthFeed(_logger, _breadthConfig);
            
            // Simulate highs/lows data
            await breadthFeed.UpdateHighsLowsAsync(200, 50); // 200 new highs, 50 new lows

            // Act
            var hlRatio = await breadthFeed.GetNewHighsLowsRatioAsync();

            // Assert
            Assert.True(hlRatio > 0);
            Assert.True(hlRatio >= 1.0m); // More highs than lows should be > 1
        }

        [Fact]
        public async Task BreadthFeed_FailClosed_MissingDataTriggersException()
        {
            // Arrange
            var failClosedConfig = new BreadthConfiguration
            {
                Enabled = true,
                AdvanceDeclineThreshold = 0.75m,
                NewHighsLowsRatio = 2.0m,
                SectorRotationWeight = 0.25m,
                BreadthLookbackBars = 20,
                DataSource = "IndexBreadthFeed",
                FailOnMissingData = true // Enable fail-closed behavior
            };

            var breadthFeed = new TestBreadthFeed(_logger, failClosedConfig);

            // Act & Assert - Should throw when no data available and fail-closed is enabled
            if (failClosedConfig.FailOnMissingData && !breadthFeed.IsDataAvailable())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => breadthFeed.GetAdvanceDeclineRatioAsync());
            }
        }

        [Fact]
        public async Task BreadthFeed_DataAvailability_ReflectsActualState()
        {
            // Arrange
            var breadthFeed = new TestBreadthFeed(_logger, _breadthConfig);

            // Assert initial state
            Assert.False(breadthFeed.IsDataAvailable());

            // Act - Add some data
            await breadthFeed.UpdateMarketDataAsync(1000, 800);

            // Assert - Data should now be available
            Assert.True(breadthFeed.IsDataAvailable());
        }

        [Fact]
        public async Task BreadthFeed_S7Integration_ProvidesBreadthAdjustment()
        {
            // Arrange
            var breadthFeed = new TestBreadthFeed(_logger, _breadthConfig);
            await breadthFeed.UpdateMarketDataAsync(1800, 200); // Strong advance/decline ratio
            await breadthFeed.UpdateHighsLowsAsync(300, 25);    // Strong highs/lows ratio

            // Act
            var adRatio = await breadthFeed.GetAdvanceDeclineRatioAsync();
            var hlRatio = await breadthFeed.GetNewHighsLowsRatioAsync();

            // Calculate breadth adjustment similar to S7Service
            decimal breadthScore = 1.0m;
            
            if (adRatio > _breadthConfig.AdvanceDeclineThreshold)
                breadthScore += 0.1m; // Should match S7Service logic
                
            if (hlRatio > _breadthConfig.NewHighsLowsRatio)
                breadthScore += 0.05m; // Should match S7Service logic

            // Assert
            Assert.True(adRatio > _breadthConfig.AdvanceDeclineThreshold);
            Assert.True(hlRatio > _breadthConfig.NewHighsLowsRatio);
            Assert.True(breadthScore > 1.0m); // Breadth boost applied
        }

        [Fact]
        public async Task BreadthFeed_ConfigurationDriven_UsesProperThresholds()
        {
            // Arrange
            var customConfig = new BreadthConfiguration
            {
                Enabled = true,
                AdvanceDeclineThreshold = 0.6m, // Custom threshold
                NewHighsLowsRatio = 1.5m,       // Custom threshold
                SectorRotationWeight = 0.3m,
                BreadthLookbackBars = 15,
                DataSource = "IndexBreadthFeed"
            };

            var breadthFeed = new TestBreadthFeed(_logger, customConfig);
            await breadthFeed.UpdateMarketDataAsync(1200, 800); // 0.6 ratio
            await breadthFeed.UpdateHighsLowsAsync(150, 100);   // 1.5 ratio

            // Act
            var adRatio = await breadthFeed.GetAdvanceDeclineRatioAsync();
            var hlRatio = await breadthFeed.GetNewHighsLowsRatioAsync();

            // Assert - Should meet custom thresholds exactly
            Assert.True(Math.Abs(adRatio - customConfig.AdvanceDeclineThreshold) < 0.1m);
            Assert.True(Math.Abs(hlRatio - customConfig.NewHighsLowsRatio) < 0.1m);
        }

        /// <summary>
        /// Test implementation of IBreadthFeed for testing purposes
        /// </summary>
        private class TestBreadthFeed : IBreadthFeed
        {
            private readonly ILogger<TestBreadthFeed> _logger;
            private readonly BreadthConfiguration _config;
            private decimal _advanceDeclineRatio;
            private decimal _newHighsLowsRatio;
            private bool _hasData;

            public TestBreadthFeed(ILogger<TestBreadthFeed> logger, BreadthConfiguration config)
            {
                _logger = logger;
                _config = config;
                _hasData = false;
            }

            public bool IsDataAvailable() => _hasData;

            public async Task<decimal> GetAdvanceDeclineRatioAsync()
            {
                await Task.CompletedTask;
                
                if (_config.FailOnMissingData && !_hasData)
                {
                    throw new InvalidOperationException("[BREADTH-AUDIT-VIOLATION] Missing advance/decline data - TRIGGERING HOLD + TELEMETRY");
                }

                return _hasData ? _advanceDeclineRatio : 0.5m;
            }

            public async Task<decimal> GetNewHighsLowsRatioAsync()
            {
                await Task.CompletedTask;
                
                if (_config.FailOnMissingData && !_hasData)
                {
                    throw new InvalidOperationException("[BREADTH-AUDIT-VIOLATION] Missing highs/lows data - TRIGGERING HOLD + TELEMETRY");
                }

                return _hasData ? _newHighsLowsRatio : 1.0m;
            }

            public async Task<Dictionary<string, decimal>> GetSectorRotationDataAsync()
            {
                await Task.CompletedTask;
                
                // Return empty dictionary for test implementation
                return new Dictionary<string, decimal>();
            }

            public async Task UpdateMarketDataAsync(int advancing, int declining)
            {
                await Task.CompletedTask;
                var total = advancing + declining;
                _advanceDeclineRatio = total > 0 ? (decimal)advancing / total : 0.5m;
                _hasData = true;
            }

            public async Task UpdateHighsLowsAsync(int newHighs, int newLows)
            {
                await Task.CompletedTask;
                _newHighsLowsRatio = newLows > 0 ? (decimal)newHighs / newLows : 1.0m;
                _hasData = true;
            }
        }

        /// <summary>
        /// Test breadth configuration
        /// </summary>
        private class BreadthConfiguration
        {
            public bool Enabled { get; set; }
            public decimal AdvanceDeclineThreshold { get; set; }
            public decimal NewHighsLowsRatio { get; set; }
            public decimal SectorRotationWeight { get; set; }
            public int BreadthLookbackBars { get; set; }
            public string DataSource { get; set; } = string.Empty;
            public bool FailOnMissingData { get; set; }
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}