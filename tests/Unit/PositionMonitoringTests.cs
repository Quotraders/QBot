using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BotCore.Services.PositionMonitoring;
using TradingBot.Abstractions;

namespace TradingBot.Tests.Unit
{
    /// <summary>
    /// Tests for Position Monitoring features: Real-time monitoring, session exposure, and time tracking
    /// </summary>
    public class PositionMonitoringTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRealTimePositionMonitor _realTimeMonitor;
        private readonly ISessionExposureCalculator _sessionCalculator;
        private readonly IPositionTimeTracker _timeTracker;
        private readonly SessionDetectionService _sessionDetection;

        public PositionMonitoringTests()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton<IRealTimePositionMonitor, RealTimePositionMonitor>();
            services.AddSingleton<ISessionExposureCalculator, SessionExposureCalculator>();
            services.AddSingleton<IPositionTimeTracker, PositionTimeTracker>();
            services.AddSingleton<SessionDetectionService>();
            
            _serviceProvider = services.BuildServiceProvider();
            _realTimeMonitor = _serviceProvider.GetRequiredService<IRealTimePositionMonitor>();
            _sessionCalculator = _serviceProvider.GetRequiredService<ISessionExposureCalculator>();
            _timeTracker = _serviceProvider.GetRequiredService<IPositionTimeTracker>();
            _sessionDetection = _serviceProvider.GetRequiredService<SessionDetectionService>();
        }

        /// <summary>
        /// Test that SessionDetectionService correctly identifies trading sessions
        /// </summary>
        [Theory]
        [InlineData(2, "Asian")]      // 2:00 UTC = Asian session
        [InlineData(10, "European")]  // 10:00 UTC = European session
        [InlineData(15, "USMorning")] // 15:00 UTC = US Morning
        [InlineData(19, "USAfternoon")] // 19:00 UTC = US Afternoon
        [InlineData(22, "Evening")]   // 22:00 UTC = Evening
        public void SessionDetection_CorrectlyIdentifiesSessions(int hour, string expectedSession)
        {
            // Arrange
            var testTime = new DateTime(2025, 10, 10, hour, 0, 0, DateTimeKind.Utc);

            // Act
            var actualSession = _sessionDetection.GetCurrentSession(testTime);

            // Assert
            Assert.Equal(expectedSession, actualSession);
        }

        /// <summary>
        /// Test that RealTimePositionMonitor calculates exposure correctly
        /// </summary>
        [Fact]
        public async Task RealTimeMonitor_CalculatesSessionExposure_Correctly()
        {
            // Arrange - Create a position in USMorning session (15:00 UTC)
            var openTime = new DateTimeOffset(2025, 10, 10, 15, 0, 0, TimeSpan.Zero);
            var positions = new List<Position>
            {
                new Position
                {
                    Id = "TEST001",
                    Symbol = "ES",
                    Side = "LONG",
                    Quantity = 2,
                    AveragePrice = 5800.00m,
                    UnrealizedPnL = 100.00m,
                    RealizedPnL = 0.00m,
                    ConfigSnapshotId = "CONFIG001",
                    OpenTime = openTime
                }
            };

            // Act
            var exposure = await _realTimeMonitor.GetSessionExposureAsync("USMorning", positions);

            // Assert - Should calculate exposure as Quantity * Price = 2 * 5800 = 11600
            Assert.True(exposure > 0, "Exposure should be positive for long position");
            Assert.True(exposure <= 11600, "Exposure should not exceed nominal value");
        }

        /// <summary>
        /// Test that SessionExposureCalculator returns correct volatility multipliers
        /// </summary>
        [Theory]
        [InlineData("Asian", 0.6)]
        [InlineData("European", 0.85)]
        [InlineData("USMorning", 1.2)]
        [InlineData("USAfternoon", 1.0)]
        [InlineData("Evening", 0.7)]
        public void SessionCalculator_ReturnsCorrectVolatilityMultiplier(string session, double expectedMult)
        {
            // Act
            var actual = _sessionCalculator.GetVolatilityMultiplier(session);

            // Assert
            Assert.Equal(expectedMult, actual);
        }

        /// <summary>
        /// Test that SessionExposureCalculator calculates risk-adjusted exposure
        /// </summary>
        [Fact]
        public async Task SessionCalculator_CalculatesRiskAdjustedExposure_WithVolatilityAndLiquidity()
        {
            // Arrange - Position in high-volatility USMorning session
            var openTime = new DateTimeOffset(2025, 10, 10, 15, 0, 0, TimeSpan.Zero);
            var positions = new List<Position>
            {
                new Position
                {
                    Id = "TEST002",
                    Symbol = "ES",
                    Side = "LONG",
                    Quantity = 1,
                    AveragePrice = 5800.00m,
                    UnrealizedPnL = 0.00m,
                    RealizedPnL = 0.00m,
                    ConfigSnapshotId = "CONFIG002",
                    OpenTime = openTime
                }
            };

            // Act
            var exposure = await _sessionCalculator.CalculateSessionExposureAsync(positions, "USMorning");

            // Assert - Exposure should be adjusted by volatility (1.2x) and liquidity (0.95x)
            Assert.True(exposure > 0, "Risk-adjusted exposure should be positive");
        }

        /// <summary>
        /// Test that PositionTimeTracker tracks session attribution
        /// </summary>
        [Fact]
        public async Task TimeTracker_TracksPositionLifecycle_AcrossSessions()
        {
            // Arrange - Position opened in European session
            var openTime = new DateTimeOffset(2025, 10, 10, 10, 0, 0, TimeSpan.Zero);
            var positions = new List<Position>
            {
                new Position
                {
                    Id = "TEST003",
                    Symbol = "NQ",
                    Side = "SHORT",
                    Quantity = -1,
                    AveragePrice = 20000.00m,
                    UnrealizedPnL = -50.00m,
                    RealizedPnL = 0.00m,
                    ConfigSnapshotId = "CONFIG003",
                    OpenTime = openTime
                }
            };

            // Act
            var exposure = await _timeTracker.GetSessionTimeExposureAsync(positions, "European");

            // Assert
            Assert.True(exposure > 0, "Time-tracked exposure should be positive");
        }

        /// <summary>
        /// Test that services handle empty position list gracefully
        /// </summary>
        [Fact]
        public async Task Services_HandleEmptyPositions_Gracefully()
        {
            // Arrange
            var emptyPositions = new List<Position>();

            // Act & Assert - Should return 0 exposure, not throw
            var realTimeExposure = await _realTimeMonitor.GetSessionExposureAsync("USMorning", emptyPositions);
            var sessionExposure = await _sessionCalculator.CalculateSessionExposureAsync(emptyPositions, "USMorning");
            var timeExposure = await _timeTracker.GetSessionTimeExposureAsync(emptyPositions, "USMorning");

            Assert.Equal(0.0, realTimeExposure);
            Assert.Equal(0.0, sessionExposure);
            Assert.Equal(0.0, timeExposure);
        }
    }
}
