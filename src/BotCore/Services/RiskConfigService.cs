using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of risk configuration
    /// Replaces hardcoded risk limits and position sizing parameters
    /// </summary>
    public class RiskConfigService : IRiskConfig
    {
        // Risk Configuration Constants (fail-closed requirement)
        private const decimal DefaultMaxDailyLossUsd = 1000.0m;     // Maximum daily loss limit
        private const decimal DefaultMaxWeeklyLossUsd = 3000.0m;    // Maximum weekly loss limit
        private const double DefaultRiskPerTradePercent = 0.0025;   // 0.25% risk per trade
        private const decimal DefaultFixedRiskPerTradeUsd = 100.0m; // Fixed risk amount per trade
        private const int DefaultMaxConsecutiveLosses = 3;          // Maximum consecutive losses before halt
        private const double DefaultCvarConfidenceLevel = 0.95;     // CVaR confidence level (95%)
        private const double DefaultCvarTargetRMultiple = 0.65;     // CVaR target R-multiple
        
        // Regime-specific multipliers
        private const double DefaultBullRegimeMultiplier = 1.0;     // Bull market multiplier (normal risk)
        private const double DefaultBearRegimeMultiplier = 0.8;     // Bear market multiplier (reduced risk)
        private const double DefaultSidewaysRegimeMultiplier = 0.9; // Sideways market multiplier
        private const double DefaultVolatileRegimeMultiplier = 0.7; // Volatile market multiplier (lower risk)
        private const double DefaultRegimeMultiplier = 0.85;        // Default regime multiplier
        
        // Additional risk parameters
        private const decimal DefaultMaxPositionSize = 5.0m;        // Maximum position size
        private const decimal DefaultDailyLossLimit = 1000.0m;      // Daily loss limit
        private const decimal DefaultPerTradeRisk = 100.0m;         // Per-trade risk amount
        private const decimal DefaultMaxDrawdownPercentage = 0.15m; // Maximum drawdown percentage (15%)
        
        private readonly IConfiguration _config;
        private readonly ILogger<RiskConfigService> _logger;

        public RiskConfigService(IConfiguration config, ILogger<RiskConfigService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public decimal GetMaxDailyLossUsd() => 
            _config.GetValue("Risk:MaxDailyLossUsd", DefaultMaxDailyLossUsd);

        public decimal GetMaxWeeklyLossUsd() => 
            _config.GetValue("Risk:MaxWeeklyLossUsd", DefaultMaxWeeklyLossUsd);

        public double GetRiskPerTradePercent() => 
            _config.GetValue("Risk:RiskPerTradePercent", DefaultRiskPerTradePercent);

        public decimal GetFixedRiskPerTradeUsd() => 
            _config.GetValue("Risk:FixedRiskPerTradeUsd", DefaultFixedRiskPerTradeUsd);

        public int GetMaxOpenPositions() => 
            _config.GetValue("Risk:MaxOpenPositions", 1);

        public int GetMaxConsecutiveLosses() => 
            _config.GetValue("Risk:MaxConsecutiveLosses", DefaultMaxConsecutiveLosses);

        public double GetCvarConfidenceLevel() => 
            _config.GetValue("Risk:CvarConfidenceLevel", DefaultCvarConfidenceLevel);

        public double GetCvarTargetRMultiple() => 
            _config.GetValue("Risk:CvarTargetRMultiple", DefaultCvarTargetRMultiple);

        public double GetRegimeDrawdownMultiplier(string regimeType) => regimeType?.ToLower() switch
        {
            "bull" => _config.GetValue("Risk:RegimeMultipliers:Bull", DefaultBullRegimeMultiplier),
            "bear" => _config.GetValue("Risk:RegimeMultipliers:Bear", DefaultBearRegimeMultiplier),
            "sideways" => _config.GetValue("Risk:RegimeMultipliers:Sideways", DefaultSidewaysRegimeMultiplier),
            "volatile" => _config.GetValue("Risk:RegimeMultipliers:Volatile", DefaultVolatileRegimeMultiplier),
            _ => _config.GetValue("Risk:RegimeMultipliers:Default", DefaultRegimeMultiplier)
        };

        // Additional methods needed by consuming code
        public decimal GetMaxPositionSize() =>
            _config.GetValue("Risk:MaxPositionSize", DefaultMaxPositionSize);

        public decimal GetDailyLossLimit() =>
            _config.GetValue("Risk:DailyLossLimit", DefaultDailyLossLimit);

        public decimal GetPerTradeRisk() =>
            _config.GetValue("Risk:PerTradeRisk", DefaultPerTradeRisk);

        public decimal GetMaxDrawdownPercentage() =>
            _config.GetValue("Risk:MaxDrawdownPercentage", DefaultMaxDrawdownPercentage);
    }
}