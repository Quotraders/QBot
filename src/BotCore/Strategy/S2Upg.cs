using System;
using System.Collections.Generic;
using System.Linq;
using BotCore.Models;

namespace BotCore.Strategy
{
    // Minimal helpers to enhance S2 without overlapping existing upgrades
    internal static class S2Upg
    {
        // S2 Strategy Trading Constants
        private const decimal MinimumTickLevel = 0.25m;
        private const decimal StrongTrendAdjustment = 0.3m;
        private const decimal HighVolatilityAdjustment = 0.2m;
        private const decimal Nasdaq100Adjustment = 0.2m;
        private const decimal LateMoningRelaxation = 0.1m;
        private const decimal HighVolatilityThreshold = 1.5m;
        private const decimal StrongTrendThreshold = 0.25m;
        private const int LateMoningStartMinutes = 680; // 11:20 AM
        private const int LateMoningEndMinutes = 720;   // 12:00 PM
        
        // Volume analysis constants
        private const decimal DefaultVolumeImbalanceRatio = 1.5m; // Default ratio when down volume is zero
        private const int MinimumLookbackBarsForDeceleration = 3; // Minimum bars needed for deceleration analysis
        // Volume imbalance of up vs down bars over lookback window
        public static decimal UpDownImbalance(IList<Bar> bars, int look = 10)
        {
            if (bars is null || bars.Count < 2) return 1m;
            int start = Math.Max(0, bars.Count - look);
            decimal up = 0, dn = 0;
            for (int i = start; i < bars.Count; i++)
            {
                var b = bars[i];
                var vol = Math.Max(0, b.Volume);
                if (b.Close > b.Open) up += vol;
                else if (b.Close < b.Open) dn += vol;
            }
            return dn <= 0 ? DefaultVolumeImbalanceRatio : up / dn; // >1 buyers dominant; <1 sellers dominant
        }

        // Prior day extremes (using local calendar day on Bar.Start). Returns (hi, lo); zeros if not found.
        public static (decimal hi, decimal lo) PriorDayHiLo(IList<Bar> bars, DateTime nowLocal)
        {
            if (bars == null || bars.Count == 0) return (0m, 0m);
            var prevDate = nowLocal.Date.AddDays(-1);
            bool found = false; decimal hi = 0m, lo = 0;
            for (int i = 0; i < bars.Count; i++)
            {
                var d = bars[i].Start.Date;
                if (d != prevDate) continue;
                if (!found) { hi = bars[i].High; lo = bars[i].Low; found = true; }
                else { if (bars[i].High > hi) hi = bars[i].High; if (bars[i].Low < lo) lo = bars[i].Low; }
            }
            return found ? (hi, lo) : (0m, 0m);
        }

        // Ensure there is minimum room vs prior-day extremes (in ticks or ATR fraction)
        public static bool HasRoomVsPriorExtremes(
            IList<Bar> bars,
            DateTime nowLocal,
            decimal price,
            decimal tick,
            decimal atr,
            bool longSide,
            (decimal minAtrFrac, int minTicks) thresh)
        {
            var (pHi, pLo) = PriorDayHiLo(bars, nowLocal);
            if (pHi == 0m && pLo == 0m) return true; // no data → allow
            var req = Math.Max(thresh.minAtrFrac * Math.Max(tick, atr), thresh.minTicks * Math.Max(tick, MinimumTickLevel));
            if (longSide) return (price - pLo) >= req;
            else return (pHi - price) >= req;
        }

        // Z-score deceleration over last few bars: |z| decreasing toward VWAP
        public static bool ZDecelerating(IList<Bar> bars, decimal vwap, decimal sigma, int look = 3)
        {
            if (bars == null || bars.Count < look) return true; // not enough bars → don't block
            decimal AbsZ(int idx)
            {
                var c = bars[idx].Close;
                if (sigma <= 0m) return 0m;
                return Math.Abs((c - vwap) / sigma);
            }
            int n = bars.Count - 1;
            var zNow = AbsZ(n);
            var zPrev = AbsZ(n - 1);
            if (look >= MinimumLookbackBarsForDeceleration)
            {
                var zPrev2 = AbsZ(n - 2);
                return zNow <= zPrev && zPrev <= zPrev2; // monotonic decel
            }
            return zNow <= zPrev; // simple decel
        }

        // Adaptive sigma threshold: lift threshold on strong 5-bar slope / high volz / NQ; small relax late morning
        public static decimal DynamicSigmaThreshold(decimal baseSigma, decimal volz, decimal slope5, DateTime nowLocal, string sym)
        {
            decimal adj = 0;
            var absSlope = Math.Abs(slope5);
            if (absSlope > StrongTrendThreshold) adj += StrongTrendAdjustment;       // strong trend on 1m EMA20 slope proxy
            if (volz > HighVolatilityThreshold) adj += HighVolatilityAdjustment;       // high-vol regime
            if (sym.Contains("NQ", StringComparison.OrdinalIgnoreCase)) adj += Nasdaq100Adjustment; // NQ spikier
            var mins = nowLocal.Hour * 60 + nowLocal.Minute;
            if (mins >= LateMoningStartMinutes && mins <= LateMoningEndMinutes) adj -= LateMoningRelaxation; // ~11:20–12:00 slight relax
            return Math.Max(baseSigma, baseSigma + adj);
        }

        // Require at least 0.5*ATR distance away from nearest opposite pivot (using 1m as proxy)
        public static bool PivotDistanceOK(IList<Bar> bars, decimal price, decimal atr, decimal tickSize, bool longSide)
        {
            var (hi, lo) = LastHtfPivots(bars, 48);
            if (hi == 0m && lo == 0m) return true;
            var dist = longSide ? price - lo : hi - price;
            return dist >= 0.5m * Math.Max(tickSize, atr);
        }

        // Simple swing pivot scan over recent window (1m proxy)
        private static (decimal hi, decimal lo) LastHtfPivots(IList<Bar> b, int lookback)
        {
            if (b == null || b.Count < 5) return (0m, 0m);
            int start = Math.Max(2, b.Count - lookback);
            decimal lastHi = 0m, lastLo = 0m;
            for (int i = start; i < b.Count - 2; i++)
            {
                bool swingHi = b[i].High > b[i - 1].High && b[i].High > b[i - 2].High && b[i].High > b[i + 1].High && b[i].High > b[i + 2].High;
                bool swingLo = b[i].Low < b[i - 1].Low && b[i].Low < b[i - 2].Low && b[i].Low < b[i + 1].Low && b[i].Low < b[i + 2].Low;
                if (swingHi) lastHi = b[i].High;
                if (swingLo) lastLo = b[i].Low;
            }
            return (lastHi, lastLo);
        }

        // Optional size scale from |z|, not wired to sizing here (risk engine controls size). Provided for future use.
        public static decimal SizeScaleFromStretch(decimal absZ)
        {
            decimal s = 0.75m + 0.25m * absZ;
            if (s < 0.5m)
                s = 0.5m;
            if (s > 1.5m)
                s = 1.5m;
            return s;
        }

        // Compute prior-day VWAP (volume-weighted by typical price) and Close
        public static (decimal vwap, decimal close) PriorDayVwapClose(IList<Bar> bars, DateTime nowLocal)
        {
            if (bars == null || bars.Count == 0) return (0m, 0m);
            var prev = nowLocal.Date.AddDays(-1);
            decimal wv = 0m, vol = 0, lastClose = 0; bool seen = false;
            for (int i = 0; i < bars.Count; i++)
            {
                var d = bars[i].Start.Date;
                if (d != prev) continue;
                seen = true;
                var b = bars[i];
                var tp = (b.High + b.Low + b.Close) / 3m;
                var v = Math.Max(0, b.Volume);
                wv += tp * v; vol += v; lastClose = b.Close;
            }
            if (!seen || vol <= 0m) return (0m, 0m);
            return (wv / vol, lastClose);
        }
    }
}
