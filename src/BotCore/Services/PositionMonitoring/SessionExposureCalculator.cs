#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Position = TradingBot.Abstractions.Position;

namespace BotCore.Services.PositionMonitoring
{
    /// <summary>
    /// Risk-adjusted exposure calculator with volatility, correlation, and liquidity adjustments
    /// </summary>
    public class SessionExposureCalculator : ISessionExposureCalculator
    {
        private readonly ILogger<SessionExposureCalculator> _logger;
        private readonly SessionDetectionService _sessionDetection;
        
        // Session-specific volatility multipliers (based on historical ATR)
        private static readonly Dictionary<string, double> VolatilityMultipliers = new()
        {
            { "Asian", 0.6 },        // Lower volatility
            { "European", 0.85 },    // Moderate volatility
            { "USMorning", 1.2 },    // Highest volatility
            { "USAfternoon", 1.0 },  // Normal volatility
            { "Evening", 0.7 }       // Reduced volatility
        };
        
        // Session-specific liquidity scores (0.0 = illiquid, 1.0 = deep)
        private static readonly Dictionary<string, double> LiquidityScores = new()
        {
            { "Asian", 0.6 },        // Thinner market
            { "European", 0.85 },    // Good liquidity
            { "USMorning", 1.0 },    // Deepest market
            { "USAfternoon", 0.9 },  // Good liquidity
            { "Evening", 0.5 }       // Overnight, widest spreads
        };
        
        // ES/NQ correlation by session (historical)
        private static readonly Dictionary<string, double> SessionCorrelations = new()
        {
            { "Asian", 0.85 },
            { "European", 0.88 },
            { "USMorning", 0.92 },   // Highest correlation
            { "USAfternoon", 0.90 },
            { "Evening", 0.80 }
        };
        
        public SessionExposureCalculator(ILogger<SessionExposureCalculator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionDetection = new SessionDetectionService();
        }
        
        /// <summary>
        /// Calculate risk-adjusted exposure for a session
        /// </summary>
        public async Task<double> CalculateSessionExposureAsync(List<TradingBot.Abstractions.Position> positions, string session)
        {
            if (positions == null || positions.Count == 0)
                return 0.0;
                
            // Filter positions to only those from this session
            var sessionPositions = positions.Where(p => 
                _sessionDetection.GetSessionForTimestamp(p.OpenTime.UtcDateTime)
                .Equals(session, StringComparison.OrdinalIgnoreCase))
                .ToList();
                
            if (sessionPositions.Count == 0)
                return 0.0;
            
            // Calculate nominal exposure
            double nominalExposure = sessionPositions
                .Sum(p => Math.Abs(p.Quantity) * (double)p.AveragePrice);
            
            // Apply volatility adjustment
            var volatilityMult = GetVolatilityMultiplier(session);
            double volatilityAdjusted = nominalExposure * volatilityMult;
            
            // Apply correlation adjustment
            var correlationAdj = GetCorrelationAdjustment(sessionPositions, session);
            double correlationAdjusted = volatilityAdjusted * correlationAdj;
            
            // Apply liquidity discount
            var liquidityDisc = GetLiquidityDiscount(session);
            double finalExposure = correlationAdjusted * liquidityDisc;
            
            _logger.LogDebug(
                "[SESSION-CALC] Session {Session}: Nominal={Nominal:F2}, Vol={Vol:F2}, Corr={Corr:F2}, Final={Final:F2}",
                session, nominalExposure, volatilityAdjusted, correlationAdjusted, finalExposure);
            
            await Task.CompletedTask;
            return finalExposure;
        }
        
        /// <summary>
        /// Get volatility multiplier for session
        /// </summary>
        public double GetVolatilityMultiplier(string session)
        {
            return VolatilityMultipliers.TryGetValue(session, out var mult) ? mult : 1.0;
        }
        
        /// <summary>
        /// Get correlation adjustment based on position concentration
        /// </summary>
        public double GetCorrelationAdjustment(List<TradingBot.Abstractions.Position> positions, string session)
        {
            if (positions == null || positions.Count == 0)
                return 1.0;
                
            // Check if we have both ES and NQ positions
            var hasES = positions.Any(p => p.Symbol.Contains("ES", StringComparison.OrdinalIgnoreCase));
            var hasNQ = positions.Any(p => p.Symbol.Contains("NQ", StringComparison.OrdinalIgnoreCase));
            
            if (!hasES || !hasNQ)
                return 1.0; // No correlation adjustment if not both symbols
                
            // Get session correlation
            var correlation = SessionCorrelations.TryGetValue(session, out var corr) ? corr : 0.85;
            
            // High correlation = concentrated risk
            // Correlation 0.9+ = 1.15x multiplier (concentrated)
            // Correlation 0.5-0.9 = 1.05x multiplier (some diversification)
            // Correlation < 0.5 = 0.9x multiplier (hedge benefit)
            if (correlation >= 0.9)
                return 1.15;
            if (correlation >= 0.5)
                return 1.05;
            return 0.9;
        }
        
        /// <summary>
        /// Get liquidity discount for session
        /// </summary>
        public double GetLiquidityDiscount(string session)
        {
            if (!LiquidityScores.TryGetValue(session, out var score))
                score = 0.8; // Default moderate liquidity
                
            // Deep market (high score) = lower slippage risk = 0.95x
            // Thin market (low score) = higher slippage risk = 1.1x
            if (score >= 0.9)
                return 0.95;
            if (score >= 0.7)
                return 1.0;
            return 1.1;
        }
    }
}
