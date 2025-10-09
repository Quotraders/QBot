namespace BotCore.Risk
{
    public sealed class RiskConfig
    {
        public decimal RiskPerTrade { get; set; } = 100m; // existing
        // Optional equity % and halts
        public decimal RiskPctOfEquity { get; set; } = 0.0m; // e.g., 0.0025m = 0.25%
        public decimal MaxDailyDrawdown { get; set; } = 1000m;
        public decimal MaxWeeklyDrawdown { get; set; } = 3000m;
        public int MaxConsecutiveLosses { get; set; } = 3;
        public int CooldownMinutesAfterStreak { get; set; } = 30;
        public int MaxOpenPositions { get; set; } = 1;
    }
}
