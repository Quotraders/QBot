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
        private readonly ILogger<ExecutionCostConfigService> _logger;

        public ExecutionCostConfigService(IConfiguration config, ILogger<ExecutionCostConfigService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public decimal GetMaxSlippageUsd() => 
            _config.GetValue("ExecutionCost:MaxSlippageUsd", 25.0m);

        public decimal GetDailyExecutionBudgetUsd() => 
            _config.GetValue("ExecutionCost:DailyBudgetUsd", 500.0m);

        public decimal GetCommissionPerContract() => 
            _config.GetValue("ExecutionCost:CommissionPerContract", 2.50m);

        public double GetMarketImpactMultiplier() => 
            _config.GetValue("ExecutionCost:MarketImpactMultiplier", 0.1);

        public double GetExpectedSlippageTicks(string orderType) => orderType?.ToUpper() switch
        {
            "MARKET" => _config.GetValue("ExecutionCost:MarketOrderSlippageTicks", 1.0),
            "LIMIT" => _config.GetValue("ExecutionCost:LimitOrderSlippageTicks", 0.25),
            "STOP" => _config.GetValue("ExecutionCost:StopOrderSlippageTicks", 2.0),
            _ => _config.GetValue("ExecutionCost:DefaultSlippageTicks", 1.0)
        };

        public decimal GetRoutingCostThresholdUsd() => 
            _config.GetValue("ExecutionCost:RoutingCostThreshold", 5.0m);

        public int GetDefaultTradeSize() => 
            _config.GetValue("ExecutionCost:DefaultTradeSize", 100);

        public decimal GetMaxMarketImpactBps() => 
            _config.GetValue("ExecutionCost:MaxMarketImpactBps", 20.0m);

        public decimal GetOpeningAuctionAdjustment() => 
            _config.GetValue("ExecutionCost:OpeningAuctionAdjustment", 1.2m);

        public decimal GetClosingAuctionAdjustment() => 
            _config.GetValue("ExecutionCost:ClosingAuctionAdjustment", 1.1m);

        public decimal GetAfterHoursAdjustment() => 
            _config.GetValue("ExecutionCost:AfterHoursAdjustment", 1.5m);

        public decimal GetImbalanceAdjustmentMultiplier() => 
            _config.GetValue("ExecutionCost:ImbalanceAdjustmentMultiplier", 1.3m);

        public decimal GetMinSlippageBps() => 
            _config.GetValue("ExecutionCost:MinSlippageBps", 0.5m);

        public decimal GetMaxTimeMultiplier() => 
            _config.GetValue("ExecutionCost:MaxTimeMultiplier", 3.0m);

        public decimal GetTimeMultiplierBaseline() => 
            _config.GetValue("ExecutionCost:TimeMultiplierBaseline", 15.0m); // 15 minutes baseline

        public decimal GetVolatilityBoost() => 
            _config.GetValue("ExecutionCost:VolatilityBoost", 1.2m);

        public decimal GetVolumeThreshold() => 
            _config.GetValue("ExecutionCost:VolumeThreshold", 1000.0m);

        public decimal GetVolumeBoost() => 
            _config.GetValue("ExecutionCost:VolumeBoost", 1.1m);

        public decimal GetMaxFillProbability() => 
            _config.GetValue("ExecutionCost:MaxFillProbability", 0.95m);

        public decimal GetMinFillProbability() => 
            _config.GetValue("ExecutionCost:MinFillProbability", 0.3m);

        public decimal GetDefaultSpreadPenetration() => 
            _config.GetValue("ExecutionCost:DefaultSpreadPenetration", 0.5m);

        public decimal GetMaxSlippageCap() => 
            _config.GetValue("ExecutionCost:MaxSlippageCap", 5.0m);

        public FillProbabilityThresholds GetFillProbabilityThresholds() 
        {
            var thresholds = new FillProbabilityThresholds
            {
                AtMarketProbability = _config.GetValue("ExecutionCost:AtMarketProbability", 0.9m),
                PassiveFallbackProbability = _config.GetValue("ExecutionCost:PassiveFallbackProbability", 0.2m)
            };
            
            // Configure distance thresholds
            thresholds.SetDistanceThresholds(new[]
            {
                new DistanceThreshold { MaxDistance = 0.25, FillProbability = 0.8m },
                new DistanceThreshold { MaxDistance = 0.5, FillProbability = 0.6m },
                new DistanceThreshold { MaxDistance = 1.0, FillProbability = 0.4m },
                new DistanceThreshold { MaxDistance = 2.0, FillProbability = 0.3m }
            });
            
            return thresholds;
        }
    }
}