using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Zones;

namespace Zones;

internal sealed class AtrWilder
{
    private readonly int _n;
    private decimal? _atr;
    private decimal? _prevClose;
    public AtrWilder(int period) { _n = Math.Max(1, period); }
    public decimal Update(decimal h, decimal l, decimal c)
    {
        if (_prevClose is null) { _prevClose = c; _atr = h - l; return _atr.Value; }
        var tr = Math.Max(h - l, Math.Max(Math.Abs(h - _prevClose.Value), Math.Abs(l - _prevClose.Value)));
        if (_atr is null) _atr = tr; else _atr = (_atr.Value * (_n - 1) + tr) / _n;
        _prevClose = c; return _atr.Value;
    }
    public decimal Value => _atr ?? 0m;
}

internal sealed class Ring<T>
{
    private readonly T[] _buf; private int _i; private int _count;
    public Ring(int cap){ _buf = new T[cap]; }
    public void Push(T x){ _buf[_i] = x; _i = (_i + 1) % _buf.Length; _count = Math.Min(_count + 1, _buf.Length); }
    public int Count => _count; public int Capacity => _buf.Length;
    public T this[int idx] => _buf[(_i - _count + idx + _buf.Length) % _buf.Length];
    public IEnumerable<T> Items(){ for(int k=0;k<_count;k++) yield return this[k]; }
}

public sealed class ZoneServiceProduction : IZoneService, IZoneFeatureSource
{
    internal sealed record Bar(decimal O, decimal H, decimal L, decimal C, long V, DateTime Utc);

    // Constants to avoid magic numbers  
    private const int DefaultPivotSize = 3;
    private const int DefaultAtrPeriod = 14;
    private const decimal DefaultMergeAtr = 0.6m;
    private const int DefaultDecayHalfLife = 600;
    private const decimal HalfDivisor = 2.0m;
    private const decimal BreakoutThresholdAtr = 0.2m;
    private const int MaxZonesPerSymbol = 200;
    private const int MinHistoryBars = 2;
    private const decimal InitialZonePressure = 0.5m;
    private const int RingBufferSize = 3000;
    private const int MidpointDivisor = 2;
    private const decimal MinTouchDecay = 0.01m;
    private const decimal ZoneMergingPressureFactor = 0.6m;
    private const decimal ZoneMergingPressureOffset = 0.4m;
    private const int MinTouchThreshold = 1;

    private readonly ConcurrentDictionary<string, SymbolState> _bySym = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _pivotL, _pivotR, _decayHalfLife;
    private readonly int _atrPeriod;
    private readonly decimal _mergeAtr;

    public ZoneServiceProduction([NotNull] IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        
        var section = cfg.GetSection("zone");
        _pivotL = (int) Math.Round(section.GetValue("pivot_left:default", (double)DefaultPivotSize));
        _pivotR = (int) Math.Round(section.GetValue("pivot_right:default", (double)DefaultPivotSize));
        _atrPeriod = (int) Math.Round(section.GetValue("atr_period:default", (double)DefaultAtrPeriod));
        _mergeAtr = section.GetValue("merge_atr:default", DefaultMergeAtr);
        _decayHalfLife = (int) Math.Round(section.GetValue("decay_halflife_bars:default", (double)DefaultDecayHalfLife));
    }

    public void OnBar(string symbol, DateTime utc, decimal o, decimal h, decimal l, decimal c, long v)
    {
        var s = _bySym.GetOrAdd(symbol, _ => new SymbolState(_atrPeriod));
        var bar = new Bar(o,h,l,c,v,utc);
        lock (s.Lock)
        {
            s.Atr.Update(h,l,c);
            s.Bars.Push(bar);
            DetectPivotsAndUpdateZones(s);
            UpdateZoneStatesAndPressure(s, c, utc);
        }
    }

    public void OnTick(string symbol, decimal bid, decimal ask, DateTime utc)
    {
        if (!_bySym.TryGetValue(symbol, out var s)) return;
        lock (s.Lock)
        {
            s.LastPrice = (bid + ask) / MidpointDivisor;
            s.LastUtc = utc;
        }
    }

    public ZoneSnapshot GetSnapshot(string symbol)
    {
        if (!_bySym.TryGetValue(symbol, out var s))
            return CreateEmptySnapshot();
            
        lock (s.Lock)
        {
            return CreateZoneSnapshot(s);
        }
    }

    private static ZoneSnapshot CreateEmptySnapshot()
    {
        return new ZoneSnapshot(null, null, decimal.MaxValue, decimal.MaxValue, 0m, 0m, DateTime.UtcNow);
    }

    private static ZoneSnapshot CreateZoneSnapshot(SymbolState s)
    {
        var atr = GetSnapshotAtr(s);
        var px = s.LastPrice;
        var (demand, supply) = FindNearestZones(s, px);
        var distances = CalculateZoneDistances(demand, supply, px, atr);
        var opposingZone = supply ?? demand;
        var breakoutScore = opposingZone is null ? 0m : EstimateBreakoutScore(s, px, opposingZone, atr);
        var pressure = opposingZone?.Pressure ?? 0.0m;
        
        return new ZoneSnapshot(demand, supply, distances.demandDist, distances.supplyDist, breakoutScore, pressure, s.LastUtc);
    }

    private static decimal GetSnapshotAtr(SymbolState s)
    {
        return s.Atr.Value == 0 ? 1m : s.Atr.Value;
    }

    private static (Zone? demand, Zone? supply) FindNearestZones(SymbolState s, decimal px)
    {
        var demand = s.Zones.Where(z => z.Side == ZoneSide.Demand && z.PriceHigh <= px).OrderByDescending(z => z.PriceHigh).FirstOrDefault();
        var supply = s.Zones.Where(z => z.Side == ZoneSide.Supply && z.PriceLow >= px).OrderBy(z => z.PriceLow).FirstOrDefault();
        return (demand, supply);
    }

    private static (decimal demandDist, decimal supplyDist) CalculateZoneDistances(Zone? demand, Zone? supply, decimal px, decimal atr)
    {
        decimal distDemand = demand is null ? decimal.MaxValue : (px - demand.PriceHigh) / atr;
        decimal distSupply = supply is null ? decimal.MaxValue : (supply.PriceLow - px) / atr;
        return (Math.Max(0m, distDemand), Math.Max(0m, distSupply));
    }

    (decimal distToDemandAtr, decimal distToSupplyAtr, decimal breakoutScore, decimal zonePressure) IZoneFeatureSource.GetFeatures(string symbol)
    {
        var snap = GetSnapshot(symbol);
        return (snap.DistToDemandAtr, snap.DistToSupplyAtr, snap.BreakoutScore, snap.ZonePressure);
    }

    // ===== internals =====

    private sealed class SymbolState
    {
        public readonly object Lock = new();
        public readonly Ring<Bar> Bars = new(RingBufferSize);
        public readonly AtrWilder Atr;
        public readonly List<Zone> Zones = new();
        public decimal LastPrice;
        public DateTime LastUtc;
        public SymbolState(int atrPeriod){ Atr = new AtrWilder(atrPeriod); }
    }

    private void DetectPivotsAndUpdateZones(SymbolState s)
    {
        var n = s.Bars.Count; 
        if (n < _pivotL + _pivotR + 1) return;
        
        ProcessPivotDetection(s, n);
    }

    private void ProcessPivotDetection(SymbolState s, int barCount)
    {
        var idx = GetPivotIndex(barCount);
        var center = s.Bars[idx];
        var (isHigh, isLow) = AnalyzePivotPattern(s, idx, center);
        
        if (isHigh || isLow)
        {
            CreateZonesFromPivot(s, center, isHigh, isLow);
        }
    }

    private int GetPivotIndex(int barCount)
    {
        return barCount - 1 - _pivotR;
    }

    private (bool isHigh, bool isLow) AnalyzePivotPattern(SymbolState s, int centerIdx, Bar center)
    {
        bool isHigh = true, isLow = true;
        
        for(int i = centerIdx - _pivotL; i < centerIdx + _pivotR + 1; i++)
        {
            if (i == centerIdx) continue;
            
            var b = s.Bars[i];
            if (b.H >= center.H) 
                isHigh = isHigh && (i < centerIdx ? b.H < center.H : b.H <= center.H);
            if (b.L <= center.L) 
                isLow = isLow && (i < centerIdx ? b.L > center.L : b.L >= center.L);
        }
        
        return (isHigh, isLow);
    }

    private void CreateZonesFromPivot(SymbolState s, Bar center, bool isHigh, bool isLow)
    {
        var atr = GetEffectiveAtr(s, center);
        var thickness = atr * _mergeAtr;
        
        if (isHigh)
        {
            var supplyZone = CreateSupplyZone(center, thickness);
            AddOrMergeZone(s, supplyZone);
        }
        
        if (isLow)
        {
            var demandZone = CreateDemandZone(center, thickness);
            AddOrMergeZone(s, demandZone);
        }
    }

    private static decimal GetEffectiveAtr(SymbolState s, Bar center)
    {
        var atr = s.Atr.Value;
        return atr <= 0 ? Math.Max(1, center.H - center.L) : atr;
    }

    private static Zone CreateSupplyZone(Bar center, decimal thickness)
    {
        return new Zone(
            ZoneSide.Supply, 
            center.H - thickness / MidpointDivisor, 
            center.H + thickness / MidpointDivisor, 
            InitialZonePressure, 
            1, 
            center.Utc, 
            ZoneState.Test);
    }

    private static Zone CreateDemandZone(Bar center, decimal thickness)
    {
        return new Zone(
            ZoneSide.Demand, 
            center.L - thickness / MidpointDivisor, 
            center.L + thickness / MidpointDivisor, 
            InitialZonePressure, 
            1, 
            center.Utc, 
            ZoneState.Test);
    }

    private static void AddOrMergeZone(SymbolState s, Zone newZone)
    {
        var existingZoneIndex = FindMergableZone(s, newZone);
        
        if (existingZoneIndex >= 0)
        {
            MergeZones(s, existingZoneIndex, newZone);
        }
        else
        {
            s.Zones.Add(newZone);
        }
    }

    private static int FindMergableZone(SymbolState s, Zone newZone)
    {
        var mergeDist = newZone.Thickness;
        
        for(int i = 0; i < s.Zones.Count; i++)
        {
            var existing = s.Zones[i];
            if (existing.Side != newZone.Side) continue;
            
            if (ShouldMergeZones(existing, newZone, mergeDist))
            {
                return i;
            }
        }
        
        return -1;
    }

    private static bool ShouldMergeZones(Zone existing, Zone newZone, decimal mergeDist)
    {
        return !(newZone.PriceHigh < existing.PriceLow - mergeDist || 
                 newZone.PriceLow > existing.PriceHigh + mergeDist);
    }

    private static void MergeZones(SymbolState s, int existingIndex, Zone newZone)
    {
        var existing = s.Zones[existingIndex];
        var mergedZone = CreateMergedZone(existing, newZone);
        s.Zones[existingIndex] = mergedZone;
    }

    private static Zone CreateMergedZone(Zone existing, Zone newZone)
    {
        var low = Math.Min(existing.PriceLow, newZone.PriceLow);
        var high = Math.Max(existing.PriceHigh, newZone.PriceHigh);
        var touchCount = existing.TouchCount + newZone.TouchCount;
        var pressure = Math.Min(1.0m, (existing.Pressure + newZone.Pressure) * ZoneMergingPressureFactor + ZoneMergingPressureOffset);
        
        return new Zone(existing.Side, low, high, pressure, touchCount, DateTime.UtcNow, ZoneState.Test);
    }

    private void UpdateZoneStatesAndPressure(SymbolState s, decimal close, DateTime utc)
    {
        if (s.Zones.Count == 0) return;
        
        var atr = GetValidAtr(s);
        var decay = CalculateDecayFactor();
        
        UpdateAllZones(s, close, utc, atr, decay);
        PruneInvalidZones(s);
        
        s.LastPrice = close; 
        s.LastUtc = utc;
    }

    private static decimal GetValidAtr(SymbolState s)
    {
        var atr = s.Atr.Value;
        return atr <= 0 ? 1 : atr;
    }

    private decimal CalculateDecayFactor()
    {
        // Use double for Math.Exp/Log, then convert back to decimal
        var exponent = -Math.Log((double)HalfDivisor) / Math.Max(1, _decayHalfLife);
        return (decimal)Math.Exp(exponent);
    }

    private static void UpdateAllZones(SymbolState s, decimal close, DateTime utc, decimal atr, decimal decay)
    {
        for(int i = 0; i < s.Zones.Count; i++)
        {
            var zone = s.Zones[i];
            var updatedZone = UpdateSingleZone(zone, close, utc, atr, decay);
            s.Zones[i] = updatedZone;
        }
    }

    private static Zone UpdateSingleZone(Zone zone, decimal close, DateTime utc, decimal atr, decimal decay)
    {
        var newPressure = CalculateNewPressure(zone, decay);
        var newState = DetermineZoneState(zone, close, atr);
        var touchUpdate = zone.Contains(close) ? (zone.TouchCount + 1, utc) : (zone.TouchCount, zone.LastTouchedUtc);
        
        return zone with 
        { 
            Pressure = newPressure, 
            State = newState,
            TouchCount = touchUpdate.Item1,
            LastTouchedUtc = touchUpdate.Item2
        };
    }

    private static decimal CalculateNewPressure(Zone zone, decimal decay)
    {
        return Math.Clamp(zone.Pressure * decay + MinTouchDecay * zone.TouchCount, 0m, 1m);
    }

    private static ZoneState DetermineZoneState(Zone zone, decimal close, decimal atr)
    {
        if (zone.Contains(close))
        {
            return ZoneState.Test;
        }
        
        var breakoutThreshold = atr * BreakoutThresholdAtr;
        
        if (close > zone.PriceHigh + breakoutThreshold)
        {
            return zone.Side == ZoneSide.Supply ? ZoneState.Breakout : ZoneState.Retest;
        }
        
        if (close < zone.PriceLow - breakoutThreshold)
        {
            return zone.Side == ZoneSide.Demand ? ZoneState.Breakout : ZoneState.Retest;
        }
        
        return zone.State;
    }

    private static void PruneInvalidZones(SymbolState s)
    {
        if (s.Zones.Count > MaxZonesPerSymbol)
        {
            s.Zones.RemoveAll(z => z.State == ZoneState.Invalidated || z.TouchCount <= MinTouchThreshold);
        }
    }

    private static decimal EstimateBreakoutScore(SymbolState s, decimal px, Zone opp, decimal atr)
    {
        const int lookbackBars = 20;
        const decimal defaultVdc = 1.0m;
        const int baselineAtrPeriod = 50;
        const decimal maxDistanceAtr = 3.0m;
        const decimal maxTestsForScore = 5.0m;
        const decimal momentumWeight = 1.5m;
        const decimal vdcWeight = 0.8m;
        const decimal testsWeight = 0.5m;
        const decimal distanceWeight = 1.0m;
        const decimal logisticBias = 0.5m;
        const decimal minVolatilityRatio = 0.5m;
        const decimal maxVolatilityRatio = 2.0m;
        
        int k = Math.Min(lookbackBars, s.Bars.Count - 1);
        if (k <= MinHistoryBars) return InitialZonePressure;
        
        var momentum = CalculateMomentum(s, k, atr);
        var volatilityRatio = CalculateVolatilityRatio(s, atr, baselineAtrPeriod, minVolatilityRatio, maxVolatilityRatio);
        var distance = CalculateNormalizedDistance(px, opp, atr, maxDistanceAtr);
        var testsFactor = CalculateTestsFactor(opp, maxTestsForScore);
        
        return CalculateLogisticScore(momentum, volatilityRatio, testsFactor, distance, 
            momentumWeight, vdcWeight, testsWeight, distanceWeight, logisticBias);
    }

    private static decimal CalculateMomentum(SymbolState s, int lookback, decimal atr)
    {
        var last = s.Bars[s.Bars.Count - 1];
        var prev = s.Bars[s.Bars.Count - 1 - lookback];
        return (last.C - prev.C) / atr;
    }

    private static decimal CalculateVolatilityRatio(SymbolState s, decimal currentAtr, int baselinePeriod, decimal minRatio, decimal maxRatio)
    {
        // VDC = Volatility Dynamic Component
        // Measures how current volatility compares to baseline
        // Higher volatility = zones more fragile, price moves faster
        // Lower volatility = zones more stable, harder to break
        
        if (currentAtr <= 0) return 1.0m;
        
        // Calculate baseline volatility using longer-period ATR
        int availableBars = Math.Min(baselinePeriod, s.Bars.Count);
        if (availableBars < 2) return 1.0m;
        
        decimal sumTrueRange = 0m;
        for (int i = 1; i < availableBars; i++)
        {
            var current = s.Bars[s.Bars.Count - 1 - i];
            var previous = s.Bars[s.Bars.Count - i];
            var tr = Math.Max(
                current.H - current.L,
                Math.Max(
                    Math.Abs(current.H - previous.C),
                    Math.Abs(current.L - previous.C)
                )
            );
            sumTrueRange += tr;
        }
        
        var baselineAtr = sumTrueRange / (availableBars - 1);
        
        if (baselineAtr <= 0) return 1.0m;
        
        // Calculate volatility ratio
        var volatilityRatio = currentAtr / baselineAtr;
        
        // Clamp ratio to reasonable bounds
        return Math.Clamp(volatilityRatio, minRatio, maxRatio);
    }

    private static decimal CalculateNormalizedDistance(decimal price, Zone zone, decimal atr, decimal maxDistance)
    {
        decimal distance = (zone.Side == ZoneSide.Supply ? zone.PriceLow - price : price - zone.PriceHigh) / atr;
        return Math.Clamp(distance, 0m, maxDistance);
    }

    private static decimal CalculateTestsFactor(Zone zone, decimal maxTests)
    {
        return Math.Min(maxTests, zone.TouchCount) / maxTests;
    }

    private static decimal CalculateLogisticScore(decimal momentum, decimal vdc, decimal tests, decimal distance,
        decimal momWeight, decimal vdcWeight, decimal testsWeight, decimal distWeight, decimal bias)
    {
        const decimal MinScore = 0.0m;
        const decimal MaxScore = 1.0m;
        
        // Use double for Math.Exp, then convert back to decimal
        var exponent = (double)(-(momWeight * momentum + vdcWeight * (1m / vdc) + testsWeight * tests - distWeight * distance - bias));
        var score = (decimal)(1.0 / (1.0 + Math.Exp(exponent)));
        return Math.Clamp(score, MinScore, MaxScore);
    }
}