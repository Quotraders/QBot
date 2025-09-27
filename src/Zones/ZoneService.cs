using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, SymbolState> _bySym = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _pivotL, _pivotR, _decayHalfLife;
    private readonly int _atrPeriod;
    private readonly double _mergeAtr;

    public ZoneServiceProduction(IConfiguration cfg)
    {
        var section = cfg.GetSection("zone");
        _pivotL = (int) Math.Round(section.GetValue("pivot_left:default", 3.0));
        _pivotR = (int) Math.Round(section.GetValue("pivot_right:default", 3.0));
        _atrPeriod = (int) Math.Round(section.GetValue("atr_period:default", 14.0));
        _mergeAtr = section.GetValue("merge_atr:default", 0.6);
        _decayHalfLife = (int) Math.Round(section.GetValue("decay_halflife_bars:default", 600.0));
    }

    public void OnBar(string symbol, DateTime utc, decimal o, decimal h, decimal l, decimal c, long v)
    {
        var s = _bySym.GetOrAdd(symbol, _ => new SymbolState(_atrPeriod));
        var bar = new Bar(o,h,l,c,v,utc);
        lock (s.Lock)
        {
            s.Atr.Update(h,l,c);
            s.Bars.Push(bar);
            DetectPivotsAndUpdateZones(s, symbol);
            UpdateZoneStatesAndPressure(s, c, utc);
        }
    }

    public void OnTick(string symbol, decimal bid, decimal ask, DateTime utc)
    {
        if (!_bySym.TryGetValue(symbol, out var s)) return;
        lock (s.Lock)
        {
            s.LastPrice = (bid + ask) / 2m;
            s.LastUtc = utc;
        }
    }

    public ZoneSnapshot GetSnapshot(string symbol)
    {
        if (!_bySym.TryGetValue(symbol, out var s))
            return new ZoneSnapshot(null,null,double.PositiveInfinity,double.PositiveInfinity,0,0,DateTime.UtcNow);
        lock (s.Lock)
        {
            var atr = (double) (s.Atr.Value == 0 ? 1m : s.Atr.Value);
            var px = s.LastPrice;
            // nearest demand below
            Zone? demand = s.Zones.Where(z => z.Side == ZoneSide.Demand && z.PriceHigh <= px).OrderByDescending(z => z.PriceHigh).FirstOrDefault();
            Zone? supply = s.Zones.Where(z => z.Side == ZoneSide.Supply && z.PriceLow >= px).OrderBy(z => z.PriceLow).FirstOrDefault();
            double distDemand = demand is null ? double.PositiveInfinity : (double)((px - demand.PriceHigh) / (decimal)atr);
            double distSupply = supply is null ? double.PositiveInfinity : (double)((supply.PriceLow - px) / (decimal)atr);
            var opp = supply ?? demand; // opposing nearest
            double breakoutScore = opp is null ? 0 : EstimateBreakoutScore(s, px, opp, atr);
            double pressure = opp?.Pressure ?? 0.0;
            return new ZoneSnapshot(demand, supply, Math.Max(0,distDemand), Math.Max(0,distSupply), breakoutScore, pressure, s.LastUtc);
        }
    }

    (double distToDemandAtr, double distToSupplyAtr, double breakoutScore, double zonePressure) IZoneFeatureSource.GetFeatures(string symbol)
    {
        var snap = GetSnapshot(symbol);
        return (snap.DistToDemandAtr, snap.DistToSupplyAtr, snap.BreakoutScore, snap.ZonePressure);
    }

    // ===== internals =====

    private sealed class SymbolState
    {
        public readonly object Lock = new();
        public readonly Ring<Bar> Bars = new(3000);
        public readonly AtrWilder Atr;
        public readonly List<Zone> Zones = new();
        public decimal LastPrice;
        public DateTime LastUtc;
        public SymbolState(int atrPeriod){ Atr = new AtrWilder(atrPeriod); }
    }

    private void DetectPivotsAndUpdateZones(SymbolState s, string symbol)
    {
        var n = s.Bars.Count; if (n < _pivotL + _pivotR + 1) return;
        // check pivot at the last completed bar index = n - 1 - _pivotR
        var idx = n - 1 - _pivotR; var center = s.Bars[idx];
        bool isHigh = true, isLow = true;
        for(int i=idx - _pivotL; i<idx + _pivotR + 1; i++)
        {
            var b = s.Bars[i];
            if (i == idx) continue;
            if (b.H >= center.H) isHigh = isHigh && (i<idx ? b.H < center.H : b.H <= center.H);
            if (b.L <= center.L) isLow = isLow && (i<idx ? b.L > center.L : b.L >= center.L);
        }
        var atr = s.Atr.Value; if (atr <= 0) atr = Math.Max(1, center.H - center.L);
        var thickness = atr * (decimal)_mergeAtr; // zone thickness baseline
        if (isHigh)
            AddOrMergeZone(s, new Zone(ZoneSide.Supply, center.H - thickness/2m, center.H + thickness/2m, 0.5, 1, center.Utc, ZoneState.Test));
        if (isLow)
            AddOrMergeZone(s, new Zone(ZoneSide.Demand, center.L - thickness/2m, center.L + thickness/2m, 0.5, 1, center.Utc, ZoneState.Test));
    }

    private void AddOrMergeZone(SymbolState s, Zone z)
    {
        // merge if overlap or within merge distance (ATR * _mergeAtr)
        var mergeDist = z.Thickness; // already proportional to ATR
        for(int i=0;i<s.Zones.Count;i++)
        {
            var o = s.Zones[i]; if (o.Side != z.Side) continue;
            bool overlaps = !(z.PriceHigh < o.PriceLow - mergeDist || z.PriceLow > o.PriceHigh + mergeDist);
            if (overlaps)
            {
                var low = Math.Min(o.PriceLow, z.PriceLow);
                var high = Math.Max(o.PriceHigh, z.PriceHigh);
                var touch = o.TouchCount + z.TouchCount;
                var pressure = Math.Min(1.0, (o.Pressure + z.Pressure) * 0.6 + 0.4);
                s.Zones[i] = new Zone(o.Side, low, high, pressure, touch, DateTime.UtcNow, ZoneState.Test);
                return;
            }
        }
        s.Zones.Add(z);
    }

    private void UpdateZoneStatesAndPressure(SymbolState s, decimal close, DateTime utc)
    {
        if (s.Zones.Count == 0) return;
        var atr = s.Atr.Value; if (atr <= 0) atr = 1;
        var decay = Math.Exp(-Math.Log(2) / Math.Max(1, _decayHalfLife));
        for(int i=0;i<s.Zones.Count;i++)
        {
            var z = s.Zones[i];
            double newPressure = Math.Clamp(z.Pressure * decay + 0.01 * z.TouchCount, 0, 1);
            var state = z.State;
            if (z.Contains(close))
            {
                // inside zone → it's being tested
                state = ZoneState.Test;
                z = z with { TouchCount = z.TouchCount + 1, LastTouchedUtc = utc };
            }
            else if (close > z.PriceHigh + atr*0.2m)
            {
                state = z.Side == ZoneSide.Supply ? ZoneState.Breakout : ZoneState.Retest; // supply broken from below → breakout
            }
            else if (close < z.PriceLow - atr*0.2m)
            {
                state = z.Side == ZoneSide.Demand ? ZoneState.Breakout : ZoneState.Retest;
            }
            s.Zones[i] = z with { Pressure = newPressure, State = state };
        }
        s.LastPrice = close; s.LastUtc = utc;
        // prune invalidated / too wide zones if needed (keep list modest)
        if (s.Zones.Count > 200)
            s.Zones.RemoveAll(z => z.State == ZoneState.Invalidated || z.TouchCount <= 1);
    }

    private static double EstimateBreakoutScore(SymbolState s, decimal px, Zone opp, double atr)
    {
        // lightweight heuristic → replace with small logistic if desired
        // features: momentum over last k bars, volatility contraction ratio, dist/ATR, prior tests
        int k = Math.Min(20, s.Bars.Count-1);
        if (k <= 2) return 0.5;
        var last = s.Bars[s.Bars.Count-1];
        var prev = s.Bars[s.Bars.Count-1-k];
        double mom = (double)((last.C - prev.C) / (decimal)atr);
        // vdc: recent ATR / long-range ATR approximation
        double vdc = 1.0; // if you have long ATR, plug it; else assume 1
        double dist = (double)((opp.Side == ZoneSide.Supply ? opp.PriceLow - px : px - opp.PriceHigh) / (decimal)atr);
        dist = Math.Clamp(dist, 0, 3);
        double tests = Math.Min(5, opp.TouchCount) / 5.0; // more tests → weaker zone
        // logistic-ish clamp
        double score = 1/(1+Math.Exp(-(1.5*mom + 0.8*(1/vdc) + 0.5*tests - 1.0*dist - 0.5)));
        return Math.Clamp(score, 0.0, 1.0);
    }
}