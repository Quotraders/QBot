namespace TradingBot.Abstractions
{
    /// <summary>
    /// Configuration interface for execution costs and budgets
    /// Replaces hardcoded cost budgets and slippage allowances
    /// </summary>
    public interface IExecutionCostConfig
    {
        /// <summary>
        /// Maximum allowed slippage per trade in USD
        /// </summary>
        decimal GetMaxSlippageUsd();

        /// <summary>
        /// Daily execution cost budget in USD
        /// </summary>
        decimal GetDailyExecutionBudgetUsd();

        /// <summary>
        /// Commission per contract/share
        /// </summary>
        decimal GetCommissionPerContract();

        /// <summary>
        /// Market impact cost multiplier (0.0-1.0)
        /// </summary>
        double GetMarketImpactMultiplier();

        /// <summary>
        /// Expected slippage in ticks for different order types
        /// </summary>
        double GetExpectedSlippageTicks(string orderType);

        /// <summary>
        /// Cost threshold for order routing decisions
        /// </summary>
        decimal GetRoutingCostThresholdUsd();

        /// <summary>
        /// Default trade size for calculations when no history available
        /// </summary>
        int GetDefaultTradeSize();

        /// <summary>
        /// Maximum market impact in basis points
        /// </summary>
        decimal GetMaxMarketImpactBps();

        /// <summary>
        /// Execution cost adjustment for opening auction
        /// </summary>
        decimal GetOpeningAuctionAdjustment();

        /// <summary>
        /// Execution cost adjustment for closing auction
        /// </summary>
        decimal GetClosingAuctionAdjustment();

        /// <summary>
        /// Execution cost adjustment for after-hours trading
        /// </summary>
        decimal GetAfterHoursAdjustment();

        /// <summary>
        /// Imbalance adjustment multiplier
        /// </summary>
        decimal GetImbalanceAdjustmentMultiplier();

        /// <summary>
        /// Minimum slippage in basis points
        /// </summary>
        decimal GetMinSlippageBps();

        /// <summary>
        /// Maximum time multiplier for execution cost calculation
        /// </summary>
        decimal GetMaxTimeMultiplier();

        /// <summary>
        /// Time multiplier baseline for execution cost calculation
        /// </summary>
        decimal GetTimeMultiplierBaseline();

        /// <summary>
        /// Volatility boost factor for execution cost calculation
        /// </summary>
        decimal GetVolatilityBoost();

        /// <summary>
        /// Volume threshold for volume-based adjustments
        /// </summary>
        decimal GetVolumeThreshold();

        /// <summary>
        /// Volume boost factor for high-volume scenarios
        /// </summary>
        decimal GetVolumeBoost();

        /// <summary>
        /// Maximum fill probability threshold
        /// </summary>
        decimal GetMaxFillProbability();

        /// <summary>
        /// Minimum fill probability threshold
        /// </summary>
        decimal GetMinFillProbability();

        /// <summary>
        /// Default spread penetration factor
        /// </summary>
        decimal GetDefaultSpreadPenetration();

        /// <summary>
        /// Maximum slippage cap
        /// </summary>
        decimal GetMaxSlippageCap();

        /// <summary>
        /// Fill probability thresholds for different scenarios
        /// </summary>
        FillProbabilityThresholds GetFillProbabilityThresholds();
    }

    /// <summary>
    /// Fill probability configuration data
    /// </summary>
    public class FillProbabilityThresholds
    {
        public decimal AtMarketProbability { get; set; } = 0.9m;
        public decimal PassiveFallbackProbability { get; set; } = 0.2m;
        public List<DistanceThreshold> DistanceThresholds { get; set; } = new();
    }

    /// <summary>
    /// Distance threshold for fill probability calculation
    /// </summary>
    public class DistanceThreshold
    {
        public double MaxDistance { get; set; }
        public decimal FillProbability { get; set; }
    }
}