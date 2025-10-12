#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Risk-adjusted exposure calculator that accounts for volatility, correlation, and liquidity
    /// </summary>
    public interface ISessionExposureCalculator
    {
        /// <summary>
        /// Calculate risk-adjusted exposure for a session
        /// </summary>
        Task<double> CalculateSessionExposureAsync(List<TradingBot.Abstractions.Position> positions, string session);
        
        /// <summary>
        /// Get volatility multiplier for a session
        /// </summary>
        double GetVolatilityMultiplier(string session);
        
        /// <summary>
        /// Get correlation adjustment for positions in a session
        /// </summary>
        double GetCorrelationAdjustment(List<TradingBot.Abstractions.Position> positions, string session);
        
        /// <summary>
        /// Get liquidity discount for a session
        /// </summary>
        double GetLiquidityDiscount(string session);
    }
}
