namespace BotCore.Brain.Models
{
    /// <summary>
    /// Strategy specialization configuration for the unified trading brain
    /// </summary>
    public class StrategySpecialization
    {
        public string Name { get; set; } = string.Empty;
        public string[] OptimalConditions { get; set; } = Array.Empty<string>();
        public string LearningFocus { get; set; } = string.Empty;
        public string[] TimeWindows { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Performance tracking for individual strategies
    /// </summary>
    public class StrategyPerformance
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal WinRate { get; set; }
        private readonly List<long> _holdTimes = new();
        public IReadOnlyList<long> HoldTimes => _holdTimes;
        
        public void AddHoldTime(long holdTimeMs)
        {
            _holdTimes.Add(holdTimeMs);
        }
        
        public void ReplaceHoldTimes(IEnumerable<long> holdTimes)
        {
            _holdTimes.Clear();
            if (holdTimes != null) _holdTimes.AddRange(holdTimes);
        }
    }

    /// <summary>
    /// Market condition analysis result
    /// </summary>
    public class MarketCondition
    {
        public string ConditionName { get; set; } = string.Empty;
        public decimal SuccessCount { get; set; }
        public decimal TotalCount { get; set; }
        public decimal SuccessRate { get; set; }
    }

    /// <summary>
    /// Performance metrics summary
    /// </summary>
    public class PerformanceMetrics
    {
        public decimal WinRate { get; set; }
        public decimal AveragePnL { get; set; }
        public TimeSpan AverageHoldTime { get; set; }
        public string[] BestConditions { get; set; } = Array.Empty<string>();
        public decimal RecentPerformanceTrend { get; set; }
    }

    /// <summary>
    /// Unified scheduling recommendation from the brain
    /// </summary>
    public class UnifiedSchedulingRecommendation
    {
        public bool IsMarketOpen { get; set; }
        public string LearningIntensity { get; set; } = string.Empty; // INTENSIVE, LIGHT, BACKGROUND
        public int HistoricalLearningIntervalMinutes { get; set; }
        public bool LiveTradingActive { get; set; }
        public string[] RecommendedStrategies { get; set; } = Array.Empty<string>();
        public string Reasoning { get; set; } = string.Empty;
    }
}