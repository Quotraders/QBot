
#nullable enable
using BotCore.Models;
namespace BotCore
{
    public static class EmaCrossStrategy
    {
        // EMA calculation constants
        private const decimal EmaMultiplier = 2m;
        private const int MinimumHistoryBuffer = 2;
        
        public static int TrySignal(IReadOnlyList<Bar> bars, int fast = 8, int slow = 21)
        {
            if (bars is null) throw new ArgumentNullException(nameof(bars));
            
            if (bars.Count < Math.Max(fast, slow) + MinimumHistoryBuffer) return 0;
            decimal emaFastPrev = 0, emaSlowPrev = 0;
            var alphaF = EmaMultiplier / (fast + 1);
            var alphaS = EmaMultiplier / (slow + 1);
            decimal emaSlow;
            // initialize with the first close
            decimal emaFast = emaSlow = bars[0].Close;

            for (int i = 1; i < bars.Count; i++)
            {
                emaFastPrev = emaFast;
                emaSlowPrev = emaSlow;
                var c = bars[i].Close;
                emaFast = alphaF * c + (1 - alphaF) * emaFast;
                emaSlow = alphaS * c + (1 - alphaS) * emaSlow;
            }

            var prevCrossUp = emaFastPrev <= emaSlowPrev && emaFast > emaSlow;
            var prevCrossDown = emaFastPrev >= emaSlowPrev && emaFast < emaSlow;

            if (prevCrossUp) return +1;
            if (prevCrossDown) return -1;
            return 0;
        }
    }
}
