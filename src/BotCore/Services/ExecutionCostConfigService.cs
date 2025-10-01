using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of execution cost configuration
    /// Replaces hardcoded cost budgets and slippage parameters
    /// </summary>
    public class ExecutionCostConfigService : IExecutionCostConfig
    {
        private readonly IConfiguration _config;

        // Default execution cost constants
        private const decimal DefaultMaxSlippageUsd = 25.0m;
        private const decimal DefaultDailyBudgetUsd = 500.0m;
        private const decimal DefaultCommissionPerContract = 2.50m;
        private const double DefaultMarketImpactMultiplier = 0.1;
        private const double DefaultMarketOrderSlippageTicks = 1.0;
        private const double DefaultLimitOrderSlippageTicks = 0.25;
        private const double DefaultStopOrderSlippageTicks = 2.0;
        private const decimal DefaultRoutingCostThreshold = 5.0m;
        private const int DefaultTradeSize = 100;
        private const decimal DefaultMaxMarketImpactBps = 20.0m;
        private const decimal DefaultOpeningAuctionAdjustment = 1.2m;
        private const decimal DefaultClosingAuctionAdjustment = 1.1m;
        private const decimal DefaultAfterHoursAdjustment = 1.5m;
        private const decimal DefaultImbalanceAdjustmentMultiplier = 1.3m;
        private const decimal DefaultMinSlippageBps = 0.5m;
        private const decimal DefaultMaxTimeMultiplier = 3.0m;
        private const decimal DefaultTimeMultiplierBaseline = 15.0m; // 15 minutes baseline
        private const decimal DefaultVolatilityBoost = 1.2m;
        private const decimal DefaultVolumeThreshold = 1000.0m;
        private const decimal DefaultVolumeBoost = 1.1m;
        private const decimal DefaultMaxFillProbability = 0.95m;
        private const decimal DefaultMinFillProbability = 0.3m;
        private const decimal DefaultSpreadPenetration = 0.5m;
        private const decimal DefaultMaxSlippageCap = 5.0m;
        private const decimal DefaultAtMarketProbability = 0.9m;
        private const decimal DefaultPassiveFallbackProbability = 0.2m;
        
        // Fill probability threshold constants
        private const double DefaultMaxDistance1 = 0.25;
        private const decimal DefaultFillProbability1 = 0.8m;
        private const double DefaultMaxDistance2 = 0.5;
        private const decimal DefaultFillProbability2 = 0.6m;
        private const double DefaultMaxDistance3 = 1.0;
        private const decimal DefaultFillProbability3 = 0.4m;
        private const double DefaultMaxDistance4 = 2.0;
        private const decimal DefaultFillProbability4 = 0.3m;

        public ExecutionCostConfigService(IConfiguration config, ILogger<ExecutionCostConfigService> logger)
        {
            _config = config;
        }

        public decimal GetMaxSlippageUsd() => 
            _config.GetValue("ExecutionCost:MaxSlippageUsd", DefaultMaxSlippageUsd);

        public decimal GetDailyExecutionBudgetUsd() => 
            _config.GetValue("ExecutionCost:DailyBudgetUsd", DefaultDailyBudgetUsd);

        public decimal GetCommissionPerContract() => 
            _config.GetValue("ExecutionCost:CommissionPerContract", DefaultCommissionPerContract);

        public double GetMarketImpactMultiplier() => 
            _config.GetValue("ExecutionCost:MarketImpactMultiplier", DefaultMarketImpactMultiplier);

        public double GetExpectedSlippageTicks(string orderType) => orderType?.ToUpper() switch
        {
            "MARKET" => _config.GetValue("ExecutionCost:MarketOrderSlippageTicks", DefaultMarketOrderSlippageTicks),
            "LIMIT" => _config.GetValue("ExecutionCost:LimitOrderSlippageTicks", DefaultLimitOrderSlippageTicks),
            "STOP" => _config.GetValue("ExecutionCost:StopOrderSlippageTicks", DefaultStopOrderSlippageTicks),
            _ => _config.GetValue("ExecutionCost:DefaultSlippageTicks", DefaultMarketOrderSlippageTicks)
        };

        public decimal GetRoutingCostThresholdUsd() => 
            _config.GetValue("ExecutionCost:RoutingCostThreshold", DefaultRoutingCostThreshold);

        public int GetDefaultTradeSize() => 
            _config.GetValue("ExecutionCost:DefaultTradeSize", DefaultTradeSize);

        public decimal GetMaxMarketImpactBps() => 
            _config.GetValue("ExecutionCost:MaxMarketImpactBps", DefaultMaxMarketImpactBps);

        public decimal GetOpeningAuctionAdjustment() => 
            _config.GetValue("ExecutionCost:OpeningAuctionAdjustment", DefaultOpeningAuctionAdjustment);

        public decimal GetClosingAuctionAdjustment() => 
            _config.GetValue("ExecutionCost:ClosingAuctionAdjustment", DefaultClosingAuctionAdjustment);

        public decimal GetAfterHoursAdjustment() => 
            _config.GetValue("ExecutionCost:AfterHoursAdjustment", DefaultAfterHoursAdjustment);

        public decimal GetImbalanceAdjustmentMultiplier() => 
            _config.GetValue("ExecutionCost:ImbalanceAdjustmentMultiplier", DefaultImbalanceAdjustmentMultiplier);

        public decimal GetMinSlippageBps() => 
            _config.GetValue("ExecutionCost:MinSlippageBps", DefaultMinSlippageBps);

        public decimal GetMaxTimeMultiplier() => 
            _config.GetValue("ExecutionCost:MaxTimeMultiplier", DefaultMaxTimeMultiplier);

        public decimal GetTimeMultiplierBaseline() => 
            _config.GetValue("ExecutionCost:TimeMultiplierBaseline", DefaultTimeMultiplierBaseline);

        public decimal GetVolatilityBoost() => 
            _config.GetValue("ExecutionCost:VolatilityBoost", DefaultVolatilityBoost);

        public decimal GetVolumeThreshold() => 
            _config.GetValue("ExecutionCost:VolumeThreshold", DefaultVolumeThreshold);

        public decimal GetVolumeBoost() => 
            _config.GetValue("ExecutionCost:VolumeBoost", DefaultVolumeBoost);

        public decimal GetMaxFillProbability() => 
            _config.GetValue("ExecutionCost:MaxFillProbability", DefaultMaxFillProbability);

        public decimal GetMinFillProbability() => 
            _config.GetValue("ExecutionCost:MinFillProbability", DefaultMinFillProbability);

        public decimal GetDefaultSpreadPenetration() => 
            _config.GetValue("ExecutionCost:DefaultSpreadPenetration", DefaultSpreadPenetration);

        public decimal GetMaxSlippageCap() => 
            _config.GetValue("ExecutionCost:MaxSlippageCap", DefaultMaxSlippageCap);

        public FillProbabilityThresholds GetFillProbabilityThresholds() 
        {
            var thresholds = new FillProbabilityThresholds
            {
                AtMarketProbability = _config.GetValue("ExecutionCost:AtMarketProbability", DefaultAtMarketProbability),
                PassiveFallbackProbability = _config.GetValue("ExecutionCost:PassiveFallbackProbability", DefaultPassiveFallbackProbability)
            };
            
            // Configure distance thresholds
            thresholds.SetDistanceThresholds(new[]
            {
                new DistanceThreshold { MaxDistance = DefaultMaxDistance1, FillProbability = DefaultFillProbability1 },
                new DistanceThreshold { MaxDistance = DefaultMaxDistance2, FillProbability = DefaultFillProbability2 },
                new DistanceThreshold { MaxDistance = DefaultMaxDistance3, FillProbability = DefaultFillProbability3 },
                new DistanceThreshold { MaxDistance = DefaultMaxDistance4, FillProbability = DefaultFillProbability4 }
            });
            
            return thresholds;
        }
    }
}