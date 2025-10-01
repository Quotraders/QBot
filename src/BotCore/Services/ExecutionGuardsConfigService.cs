using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of execution guards configuration
    /// Replaces hardcoded execution limits with configurable values
    /// </summary>
    public class ExecutionGuardsConfigService : IExecutionGuardsConfig
    {
        private readonly IConfiguration _config;

        // Default execution guard values
        private const double DefaultMaxSpreadTicks = 3.0;
        private const int DefaultMaxLatencyMs = 100;
        private const long DefaultMinVolumeThreshold = 1000L;
        private const double DefaultMaxImbalanceRatio = 0.8;
        private const double DefaultMaxLimitOffsetTicks = 5.0;
        private const int DefaultCircuitBreakerThreshold = 10;
        private const int DefaultTradeAnalysisWindowMinutes = 5;
        private const int DefaultVolumeAnalysisWindowMinutes = 1;
        private const decimal DefaultMicroVolatilityThreshold = 0.002m;
        private const decimal DefaultMaxSpreadBps = 2.0m;

        public ExecutionGuardsConfigService(IConfiguration config, ILogger<ExecutionGuardsConfigService> logger)
        {
            _config = config;
        }

        public double GetMaxSpreadTicks() => 
            _config.GetValue("ExecutionGuards:MaxSpreadTicks", DefaultMaxSpreadTicks);

        public int GetMaxLatencyMs() => 
            _config.GetValue("ExecutionGuards:MaxLatencyMs", DefaultMaxLatencyMs);

        public long GetMinVolumeThreshold() => 
            _config.GetValue("ExecutionGuards:MinVolumeThreshold", DefaultMinVolumeThreshold);

        public double GetMaxImbalanceRatio() => 
            _config.GetValue("ExecutionGuards:MaxImbalanceRatio", DefaultMaxImbalanceRatio);

        public double GetMaxLimitOffsetTicks() => 
            _config.GetValue("ExecutionGuards:MaxLimitOffsetTicks", DefaultMaxLimitOffsetTicks);

        public int GetCircuitBreakerThreshold() => 
            _config.GetValue("ExecutionGuards:CircuitBreakerThreshold", DefaultCircuitBreakerThreshold);

        public int GetTradeAnalysisWindowMinutes() => 
            _config.GetValue("ExecutionGuards:TradeAnalysisWindowMinutes", DefaultTradeAnalysisWindowMinutes);

        public int GetVolumeAnalysisWindowMinutes() => 
            _config.GetValue("ExecutionGuards:VolumeAnalysisWindowMinutes", DefaultVolumeAnalysisWindowMinutes);

        public decimal GetMicroVolatilityThreshold() => 
            _config.GetValue("ExecutionGuards:MicroVolatilityThreshold", DefaultMicroVolatilityThreshold);

        public decimal GetMaxSpreadBps() => 
            _config.GetValue("ExecutionGuards:MaxSpreadBps", DefaultMaxSpreadBps);
    }
}