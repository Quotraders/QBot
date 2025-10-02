using System.Collections.Generic;

namespace BotCore.Config
{
    public static class HighWinRateProfile
    {
        // Strategy attempt caps
        private const int StandardStrategyAttemptCap = 2;
        private const int LimitedStrategyAttemptCap = 1;
        
        // Buffer tick sizes
        private const int EsBufferTicks = 1;
        private const int NqBufferTicks = 2;
        
        // Global filter thresholds
        private const int MaxSpreadTicks = 8;
        private const int MaxSpreadTicksBreakout = 10;
        private const int MinVolumePercentMeanRev = 10;
        private const int MinVolumePercentBreakout = 10;
        private const int StrongVolumePercent = 50;
        private const decimal MinAggImbalanceBreakout = 0.0m;
        private const decimal MaxAggImbalanceMeanRevAbsolute = 999m;
        private const decimal MaxSignalBarAtrMultiplier = 4.0m;
        
        // Concurrency limits
        private const int MaxPositionsTotal = 2;
        
        // Hysteresis parameters
        private const int HysteresisBars = 3;
        private const int MinDwellMinutes = 15;
        
        public static string Profile => "high_win_rate";
        public static Dictionary<string, int> AttemptCaps => new()
        {
            { "S1", 0 }, { "S2", StandardStrategyAttemptCap }, { "S3", StandardStrategyAttemptCap }, { "S4", 0 }, { "S5", 0 }, { "S6", StandardStrategyAttemptCap }, { "S7", LimitedStrategyAttemptCap }, { "S8", 0 }, { "S9", 0 }, { "S10", 0 }, { "S11", StandardStrategyAttemptCap }, { "S12", 0 }, { "S13", 0 }
        };
        public static Dictionary<string, int> Buffers => new() { { "ES_ticks", EsBufferTicks }, { "NQ_ticks", NqBufferTicks } };
        public static Dictionary<string, object> GlobalFilters => new()
        {
            { "spread_ticks_max", MaxSpreadTicks }, { "spread_ticks_max_bo", MaxSpreadTicksBreakout }, { "volume_pct_min_mr", MinVolumePercentMeanRev }, { "volume_pct_min_bo", MinVolumePercentBreakout },
            { "volume_pct_strong", StrongVolumePercent }, { "aggI_min_bo", MinAggImbalanceBreakout }, { "aggI_max_mr_abs", MaxAggImbalanceMeanRevAbsolute }, { "signal_bar_max_atr_mult", MaxSignalBarAtrMultiplier }, { "three_stop_rule", true }
        };
        public static Dictionary<string, object> Concurrency => new() { { "max_positions_total", MaxPositionsTotal }, { "one_fresh_entry_per_symbol", true } };
        public static Dictionary<string, object> Hysteresis => new() { { "bars", HysteresisBars }, { "min_dwell_minutes", MinDwellMinutes } };
        public static Dictionary<string, object> Timeframes => new() { { "signal", "1m" }, { "filter", "5m" }, { "regime", "30m" }, { "daily", true } };
    }
}
