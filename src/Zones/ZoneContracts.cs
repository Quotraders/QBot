namespace Zones;

public enum ZoneSide { Demand, Supply }
public enum ZoneState { Test, Hold, Breakout, Retest, Invalidated }

public sealed record Zone(
    ZoneSide Side,
    decimal PriceLow,
    decimal PriceHigh,
    double Pressure,     // 0..1 (touches, confluence, recency)
    int TouchCount,
    DateTime LastTouchedUtc,
    ZoneState State)
{
    private const int MidpointDivisor = 2;
    
    public decimal Mid => (PriceLow + PriceHigh) / MidpointDivisor;
    public decimal Thickness => PriceHigh - PriceLow;
    public bool Contains(decimal price) => price >= PriceLow && price <= PriceHigh;
}

public sealed record ZoneSnapshot(
    Zone? NearestDemand,
    Zone? NearestSupply,
    double DistToDemandAtr,   // >=0
    double DistToSupplyAtr,   // >=0
    double BreakoutScore,     // 0..1 about NEAREST OPPOSING zone
    double ZonePressure,      // 0..1 of NEAREST OPPOSING zone
    DateTime Utc);

public interface IZoneService
{
    ZoneSnapshot GetSnapshot(string symbol);
    void OnBar(string symbol, DateTime utc, decimal o, decimal h, decimal l, decimal c, long v);
    void OnTick(string symbol, decimal bid, decimal ask, DateTime utc);
}

public interface IZoneFeatureSource
{
    // Provides zone features to the brain/feature-bus each bar
    (double distToDemandAtr, double distToSupplyAtr, double breakoutScore, double zonePressure) GetFeatures(string symbol);
}