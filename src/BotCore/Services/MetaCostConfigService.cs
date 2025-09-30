using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of meta-cost configuration
    /// Replaces hardcoded cost blending parameters in RL/ML models
    /// </summary>
    public class MetaCostConfigService : IMetaCostConfig
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MetaCostConfigService> _logger;

        // Default meta-cost configuration constants
        private const double DefaultExecutionCostWeight = 0.3;
        private const double DefaultMarketImpactWeight = 0.2;
        private const double DefaultOpportunityCostWeight = 0.25;
        private const double DefaultTimingCostWeight = 0.15;
        private const double DefaultVolatilityRiskWeight = 0.1;
        private const double DefaultCostBlendingTemperature = 1.0;
        private const double DefaultAdaptiveWeightRate = 0.01;

        public MetaCostConfigService(IConfiguration config, ILogger<MetaCostConfigService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public double GetExecutionCostWeight() => 
            _config.GetValue("MetaCost:ExecutionCostWeight", DefaultExecutionCostWeight);

        public double GetMarketImpactWeight() => 
            _config.GetValue("MetaCost:MarketImpactWeight", DefaultMarketImpactWeight);

        public double GetOpportunityCostWeight() => 
            _config.GetValue("MetaCost:OpportunityCostWeight", DefaultOpportunityCostWeight);

        public double GetTimingCostWeight() => 
            _config.GetValue("MetaCost:TimingCostWeight", DefaultTimingCostWeight);

        public double GetVolatilityRiskWeight() => 
            _config.GetValue("MetaCost:VolatilityRiskWeight", DefaultVolatilityRiskWeight);

        public double GetCostBlendingTemperature() => 
            _config.GetValue("MetaCost:CostBlendingTemperature", DefaultCostBlendingTemperature);

        public bool NormalizeCostWeights() => 
            _config.GetValue("MetaCost:NormalizeCostWeights", true);

        public double GetAdaptiveWeightRate() => 
            _config.GetValue("MetaCost:AdaptiveWeightRate", DefaultAdaptiveWeightRate);
    }
}