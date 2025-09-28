using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BotCore.Services;
using Zones;
using Xunit;

namespace TradingBot.Tests.Unit;

/// <summary>
/// Behavioral tests for hybrid zone provider system
/// Tests the three scenarios specified: fresh legacy, stale legacy, and disagreement
/// </summary>
public class HybridZoneProviderBehaviorTests
{
    private readonly IConfiguration _configuration;

    public HybridZoneProviderBehaviorTests()
    {
        var configData = new Dictionary<string, string>
        {
            ["ZONES_MODE"] = "hybrid",
            ["ZONES_TTL_SEC"] = "300",
            ["ZONES_AGREE_TICKS"] = "2.0",
            ["ZONES_FAIL_CLOSED"] = "true"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    [Fact]
    public async Task When_Legacy_Is_Fresh_Hybrid_Returns_Legacy()
    {
        // Arrange
        var symbol = "ES";
        var currentPrice = 4500m;
        
        var legacyZoneData = CreateFreshLegacyZoneData(symbol, currentPrice);
        var legacyZoneService = new MockLegacyZoneService(legacyZoneData, isStale: false);
        var modernZoneService = new MockModernZoneService(symbol, currentPrice);
        
        var legacyProvider = new LegacyZoneProvider(legacyZoneService, NullLogger<LegacyZoneProvider>.Instance);
        var modernProvider = new ModernZoneProvider(modernZoneService, NullLogger<ModernZoneProvider>.Instance);
        var hybridProvider = new HybridZoneProvider(legacyProvider, modernProvider, _configuration, NullLogger<HybridZoneProvider>.Instance);

        // Act
        var result = await hybridProvider.GetZoneSnapshotAsync(symbol);

        // Assert
        Assert.True(result.IsSuccess, "Hybrid provider should return success when legacy is fresh");
        Assert.Equal(ZoneSource.Hybrid, result.Source);
        Assert.NotNull(result.Snapshot);
        Assert.False(result.IsStale);
        
        // Verify that legacy data is used (nearest supply should be from legacy)
        Assert.NotNull(result.Snapshot.NearestSupply);
        Assert.Equal(4520m, result.Snapshot.NearestSupply.Mid); // Legacy supply zone mid price
    }

    [Fact]
    public async Task When_Legacy_Is_Stale_Hybrid_Returns_Modern()
    {
        // Arrange
        var symbol = "ES";
        var currentPrice = 4500m;
        
        var legacyZoneData = CreateStaleLegacyZoneData(symbol, currentPrice);
        var legacyZoneService = new MockLegacyZoneService(legacyZoneData, isStale: true);
        var modernZoneService = new MockModernZoneService(symbol, currentPrice);
        
        var legacyProvider = new LegacyZoneProvider(legacyZoneService, NullLogger<LegacyZoneProvider>.Instance);
        var modernProvider = new ModernZoneProvider(modernZoneService, NullLogger<ModernZoneProvider>.Instance);
        var hybridProvider = new HybridZoneProvider(legacyProvider, modernProvider, _configuration, NullLogger<HybridZoneProvider>.Instance);

        // Act
        var result = await hybridProvider.GetZoneSnapshotAsync(symbol);

        // Assert
        Assert.True(result.IsSuccess, "Hybrid provider should return success when modern is available");
        Assert.Equal(ZoneSource.Hybrid, result.Source);
        Assert.NotNull(result.Snapshot);
        Assert.False(result.IsStale);
        
        // Verify that modern data is used (should have modern zone characteristics)
        Assert.NotNull(result.Snapshot.NearestSupply);
        // Modern service creates zones at currentPrice + 25 for supply
        Assert.True(result.Snapshot.NearestSupply.Mid > currentPrice + 20m);
    }

    [Fact]
    public async Task When_Both_Disagree_Beyond_AgreeThreshold_And_FailClosed_Policy_Holds_Entry()
    {
        // Arrange
        var symbol = "ES";
        var currentPrice = 4500m;
        
        // Create legacy data with supply zone at 4520
        var legacyZoneData = CreateFreshLegacyZoneData(symbol, currentPrice);
        var legacyZoneService = new MockLegacyZoneService(legacyZoneData, isStale: false);
        
        // Create modern data with supply zone at 4530 (10 points = 40 ticks difference, > 2 tick threshold)
        var modernZoneService = new MockModernZoneService(symbol, currentPrice, supplyOffset: 30m);
        
        var legacyProvider = new LegacyZoneProvider(legacyZoneService, NullLogger<LegacyZoneProvider>.Instance);
        var modernProvider = new ModernZoneProvider(modernZoneService, NullLogger<ModernZoneProvider>.Instance);
        var hybridProvider = new HybridZoneProvider(legacyProvider, modernProvider, _configuration, NullLogger<HybridZoneProvider>.Instance);

        // Act
        var result = await hybridProvider.GetZoneSnapshotAsync(symbol);

        // Assert
        Assert.False(result.IsSuccess, "Hybrid provider should fail when disagreement is detected and fail_closed=true");
        Assert.Equal(ZoneSource.Disagree, result.Source);
        Assert.Null(result.Snapshot);
        Assert.False(result.IsStale);
        Assert.Contains("disagreement", result.ErrorReason);
    }

    [Fact]
    public async Task SafeHoldDecisionPolicy_Blocks_Entry_On_Zone_Disagreement()
    {
        // Arrange
        var symbol = "ES";
        var currentPrice = 4500m;
        
        // Create disagreeing zone providers
        var legacyZoneData = CreateFreshLegacyZoneData(symbol, currentPrice);
        var legacyZoneService = new MockLegacyZoneService(legacyZoneData, isStale: false);
        var modernZoneService = new MockModernZoneService(symbol, currentPrice, supplyOffset: 30m); // Large disagreement
        
        var legacyProvider = new LegacyZoneProvider(legacyZoneService, NullLogger<LegacyZoneProvider>.Instance);
        var modernProvider = new ModernZoneProvider(modernZoneService, NullLogger<ModernZoneProvider>.Instance);
        var hybridProvider = new HybridZoneProvider(legacyProvider, modernProvider, _configuration, NullLogger<HybridZoneProvider>.Instance);
        
        var telemetryService = new ZoneTelemetryService(NullLogger<ZoneTelemetryService>.Instance);
        var safeHoldPolicy = new SafeHoldDecisionPolicy(
            NullLogger<SafeHoldDecisionPolicy>.Instance, 
            _configuration, 
            hybridProvider, 
            telemetryService);

        var decision = new TradingDecision
        {
            Action = TradingAction.Buy,
            Symbol = symbol,
            Confidence = 0.8
        };

        // Act
        var (held, reason, _) = safeHoldPolicy.ZoneGate(decision, symbol);

        // Assert
        Assert.True(held, "SafeHoldDecisionPolicy should hold entry when zones disagree");
        Assert.Equal("Blocked by zone disagreement", reason);
        
        // Verify telemetry was emitted
        var metrics = telemetryService.GetRecentMetrics();
        Assert.NotEmpty(metrics);
        
        // Check for risk.rejected_entries metric
        var hasRejectedEntryMetric = false;
        foreach (var metric in metrics.Values)
        {
            if (metric is { } metricObj)
            {
                var dynamicMetric = metricObj as dynamic;
                if (dynamicMetric?.Name == "risk.rejected_entries")
                {
                    hasRejectedEntryMetric = true;
                    break;
                }
            }
        }
        Assert.True(hasRejectedEntryMetric, "Should emit risk.rejected_entries metric for zone disagreement");
    }

    // Helper methods and mock classes
    private static ZoneData CreateFreshLegacyZoneData(string symbol, decimal currentPrice)
    {
        return new ZoneData
        {
            Symbol = symbol,
            CurrentPrice = currentPrice,
            SupplyZones = new List<Zone>
            {
                new()
                {
                    Price = currentPrice + 20m, // Supply zone at 4520
                    Strength = 0.8m,
                    Thickness = 5m,
                    TouchCount = 2,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                    LastTested = DateTime.UtcNow.AddMinutes(-5),
                    Status = "Active"
                }
            },
            DemandZones = new List<Zone>
            {
                new()
                {
                    Price = currentPrice - 20m, // Demand zone at 4480
                    Strength = 0.7m,
                    Thickness = 5m,
                    TouchCount = 1,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-45),
                    LastTested = DateTime.UtcNow.AddMinutes(-10),
                    Status = "Active"
                }
            }
        };
    }

    private static ZoneData CreateStaleLegacyZoneData(string symbol, decimal currentPrice)
    {
        return new ZoneData
        {
            Symbol = symbol,
            CurrentPrice = currentPrice,
            SupplyZones = new List<Zone>(), // No zones = stale
            DemandZones = new List<Zone>()
        };
    }

    // Mock implementations
    private sealed class MockLegacyZoneService : IZoneService
    {
        private readonly ZoneData? _zoneData;
        private readonly bool _isStale;

        public MockLegacyZoneService(ZoneData? zoneData, bool isStale)
        {
            _zoneData = zoneData;
            _isStale = isStale;
        }

        public Task<ZoneData?> GetLatestZonesAsync(string symbol = "ES") => Task.FromResult(_isStale ? null : _zoneData);
        public Task<decimal> GetNearestSupportAsync(string symbol, decimal currentPrice) => Task.FromResult(currentPrice - 20m);
        public Task<decimal> GetNearestResistanceAsync(string symbol, decimal currentPrice) => Task.FromResult(currentPrice + 20m);
        public Task<decimal> GetOptimalStopLevelAsync(string symbol, decimal entryPrice, bool isLong) => Task.FromResult(entryPrice - 10m);
        public Task<decimal> GetOptimalTargetLevelAsync(string symbol, decimal entryPrice, bool isLong) => Task.FromResult(entryPrice + 20m);
        public Task<decimal> GetZoneBasedPositionSizeAsync(string symbol, decimal baseSize, decimal entryPrice, bool isLong) => Task.FromResult(baseSize);
        public Task<bool> IsNearZoneAsync(string symbol, decimal price, decimal tolerance = 0.001m) => Task.FromResult(false);
        public Task<Zone> GetNearestZoneAsync(decimal price, string zoneType) => Task.FromResult(new Zone());
        public Task<string> GetZoneContextAsync(decimal price) => Task.FromResult("test");
        public Task<decimal> GetZoneAdjustedStopLossAsync(decimal entryPrice, string direction) => Task.FromResult(entryPrice - 10m);
        public Task<decimal> GetZoneAdjustedTargetAsync(decimal entryPrice, string direction) => Task.FromResult(entryPrice + 20m);
        public Task RecordZoneInteraction(decimal price, string outcome) => Task.CompletedTask;
    }

    private sealed class MockModernZoneService : Zones.IZoneService
    {
        private readonly string _symbol;
        private readonly decimal _currentPrice;
        private readonly decimal _supplyOffset;

        public MockModernZoneService(string symbol, decimal currentPrice, decimal supplyOffset = 25m)
        {
            _symbol = symbol;
            _currentPrice = currentPrice;
            _supplyOffset = supplyOffset;
        }

        public ZoneSnapshot GetSnapshot(string symbol)
        {
            var supplyZone = new Zone(
                ZoneSide.Supply,
                _currentPrice + _supplyOffset - 2.5m,
                _currentPrice + _supplyOffset + 2.5m,
                0.8,
                1,
                DateTime.UtcNow,
                ZoneState.Hold
            );

            var demandZone = new Zone(
                ZoneSide.Demand,
                _currentPrice - 25m - 2.5m,
                _currentPrice - 25m + 2.5m,
                0.7,
                1,
                DateTime.UtcNow,
                ZoneState.Hold
            );

            return new ZoneSnapshot(
                demandZone,
                supplyZone,
                25.0 / 1.0, // Assume 1.0 ATR
                _supplyOffset / 1.0,
                0.5,
                0.8,
                DateTime.UtcNow
            );
        }

        public void OnBar(string symbol, DateTime utc, decimal o, decimal h, decimal l, decimal c, long v) { }
        public void OnTick(string symbol, decimal bid, decimal ask, DateTime utc) { }
    }
}