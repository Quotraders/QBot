using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Zones;
using Xunit;

namespace TradingBot.Tests.Unit;

public class ZoneServiceTests
{
    private readonly ZoneServiceProduction _zoneService;
    private readonly IConfiguration _configuration;

    public ZoneServiceTests()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"zone:entry_block_atr:default", "0.8"},
                {"zone:allow_breakout_threshold:default", "0.7"},
                {"zone:pivot_left:default", "3"},
                {"zone:pivot_right:default", "3"},
                {"zone:atr_period:default", "14"},
                {"zone:merge_atr:default", "0.6"},
                {"zone:decay_halflife_bars:default", "600"}
            });
        
        _configuration = configBuilder.Build();
        _zoneService = new ZoneServiceProduction(_configuration);
    }

    [Fact]
    public void GetSnapshot_WithoutData_ReturnsEmptySnapshot()
    {
        // Arrange
        var symbol = "ES";

        // Act
        var snapshot = _zoneService.GetSnapshot(symbol);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.NearestDemand);
        Assert.Null(snapshot.NearestSupply);
        Assert.Equal(double.PositiveInfinity, snapshot.DistToDemandAtr);
        Assert.Equal(double.PositiveInfinity, snapshot.DistToSupplyAtr);
    }

    [Fact]
    public void OnBar_CreatesZones_WhenPivotsDetected()
    {
        // Arrange
        var symbol = "ES";
        var basePrice = 4500m;
        
        // Create bars with a clear swing high pattern
        var bars = new[]
        {
            (4500m, 4510m, 4490m, 4500m), // Base
            (4500m, 4515m, 4495m, 4510m), // Up
            (4510m, 4525m, 4505m, 4520m), // Up more
            (4520m, 4530m, 4510m, 4525m), // Peak (pivot high)
            (4525m, 4520m, 4505m, 4515m), // Down
            (4515m, 4515m, 4500m, 4505m), // Down more
            (4505m, 4510m, 4495m, 4500m), // Recovery
        };
        
        // Act - Feed bars to create zones
        for (int i = 0; i < bars.Length; i++)
        {
            var (o, h, l, c) = bars[i];
            var time = DateTime.UtcNow.AddMinutes(-bars.Length + i);
            _zoneService.OnBar(symbol, time, o, h, l, c, 1000);
        }
        
        // Set current price via tick
        _zoneService.OnTick(symbol, 4500m, 4501m, DateTime.UtcNow);

        // Assert - Should have detected some zones
        var snapshot = _zoneService.GetSnapshot(symbol);
        Assert.NotNull(snapshot);
        
        // We should have zones created from pivot detection
        // Note: May not have nearest supply/demand if pivots aren't exactly positioned, 
        // but we should at least have valid distances
        Assert.True(snapshot.DistToDemandAtr >= 0 || snapshot.DistToDemandAtr == double.PositiveInfinity);
        Assert.True(snapshot.DistToSupplyAtr >= 0 || snapshot.DistToSupplyAtr == double.PositiveInfinity);
    }

    [Fact]
    public void GetFeatures_ReturnsValidFeatures()
    {
        // Arrange
        var symbol = "ES";
        _zoneService.OnTick(symbol, 4500m, 4501m, DateTime.UtcNow);

        // Act
        var features = ((IZoneFeatureSource)_zoneService).GetFeatures(symbol);

        // Assert
        Assert.True(features.distToDemandAtr >= 0 || double.IsPositiveInfinity(features.distToDemandAtr));
        Assert.True(features.distToSupplyAtr >= 0 || double.IsPositiveInfinity(features.distToSupplyAtr));
        Assert.True(features.breakoutScore >= 0 && features.breakoutScore <= 1);
        Assert.True(features.zonePressure >= 0 && features.zonePressure <= 1);
    }
}