using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using Zones;
using System;

namespace BotCore.Services
{
    /// <summary>
    /// Zone-aware bracket manager that anchors brackets to supply/demand zones
    /// Integrates with existing bracket configuration while adding zone intelligence
    /// </summary>
    public interface IZoneAwareBracketManager
    {
        /// <summary>
        /// Calculate zone-aware bracket levels for a trade
        /// </summary>
        BracketLevels CalculateBracketLevels(string symbol, TradingAction action, decimal entryPrice, decimal atr);

        /// <summary>
        /// Get buffer ticks for zone anchoring
        /// </summary>
        int GetBufferTicks(string symbol);
    }

    public class ZoneAwareBracketManager : IZoneAwareBracketManager
    {
        private readonly ILogger<ZoneAwareBracketManager> _logger;
        private readonly IConfiguration _configuration;
        private readonly IZoneService? _zoneService;
        private readonly IBracketConfig _bracketConfig;

        // Zone anchoring constants
        private const int DefaultBufferTicks = 6;
        private const double FallbackStopAtrMultiple = 1.0;
        private const double FallbackTargetAtrMultiple = 2.0;

        public ZoneAwareBracketManager(
            ILogger<ZoneAwareBracketManager> logger,
            IConfiguration configuration,
            IBracketConfig bracketConfig,
            IZoneService? zoneService = null)
        {
            _logger = logger;
            _configuration = configuration;
            _bracketConfig = bracketConfig;
            _zoneService = zoneService;
        }

        public BracketLevels CalculateBracketLevels(string symbol, TradingAction action, decimal entryPrice, decimal atr)
        {
            try
            {
                // Get zone snapshot if available
                var zoneSnapshot = _zoneService?.GetSnapshot(symbol);
                
                if (zoneSnapshot != null && HasValidZones(zoneSnapshot, action))
                {
                    return CalculateZoneAnchored(symbol, action, entryPrice, atr, zoneSnapshot);
                }
                else
                {
                    return CalculateTraditional(symbol, action, entryPrice, atr);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE-BRACKET] Error calculating bracket levels for {Symbol}, falling back to traditional", symbol);
                return CalculateTraditional(symbol, action, entryPrice, atr);
            }
        }

        public int GetBufferTicks(string symbol)
        {
            var zoneSection = _configuration.GetSection("zone");
            return (int)Math.Round(zoneSection.GetValue("buffer_ticks:default", (double)DefaultBufferTicks));
        }

        private static bool HasValidZones(ZoneSnapshot snapshot, TradingAction action)
        {
            return action switch
            {
                TradingAction.Buy => snapshot.NearestDemand != null && snapshot.NearestSupply != null,
                TradingAction.Sell => snapshot.NearestSupply != null && snapshot.NearestDemand != null,
                _ => false
            };
        }

        private BracketLevels CalculateZoneAnchored(string symbol, TradingAction action, decimal entryPrice, decimal atr, ZoneSnapshot zoneSnapshot)
        {
            var bufferTicks = GetBufferTicks(symbol);
            var tickSize = GetTickSize(symbol);
            var buffer = bufferTicks * tickSize;

            decimal stopLoss, takeProfit;

            if (action == TradingAction.Buy)
            {
                // For long positions: stop near demand zone, target near supply zone
                var demandZone = zoneSnapshot.NearestDemand!;
                var supplyZone = zoneSnapshot.NearestSupply!;

                // Stop loss anchored to demand zone with buffer
                stopLoss = Math.Min(demandZone.PriceLow - buffer, demandZone.Mid - buffer);
                
                // Take profit anchored to supply zone with buffer
                takeProfit = Math.Max(supplyZone.PriceHigh - buffer, supplyZone.Mid - buffer);

                // Ensure minimum reward/risk ratio
                var riskAmount = Math.Abs(entryPrice - stopLoss);
                var rewardAmount = Math.Abs(takeProfit - entryPrice);
                var rewardRiskRatio = rewardAmount / Math.Max(riskAmount, 0.01m);

                if (rewardRiskRatio < (decimal)_bracketConfig.GetMinRewardRiskRatio())
                {
                    // Extend take profit to meet minimum R:R
                    var minReward = riskAmount * (decimal)_bracketConfig.GetMinRewardRiskRatio();
                    takeProfit = entryPrice + minReward;
                }

                _logger.LogDebug("[ZONE-BRACKET] Long {Symbol}: Stop @ {StopLoss:F2} (demand zone), Target @ {TakeProfit:F2} (supply zone)", 
                    symbol, stopLoss, takeProfit);
            }
            else // TradingAction.Sell
            {
                // For short positions: stop near supply zone, target near demand zone  
                var supplyZone = zoneSnapshot.NearestSupply!;
                var demandZone = zoneSnapshot.NearestDemand!;

                // Stop loss anchored to supply zone with buffer
                stopLoss = Math.Max(supplyZone.PriceHigh + buffer, supplyZone.Mid + buffer);
                
                // Take profit anchored to demand zone with buffer
                takeProfit = Math.Min(demandZone.PriceLow + buffer, demandZone.Mid + buffer);

                // Ensure minimum reward/risk ratio
                var riskAmount = Math.Abs(stopLoss - entryPrice);
                var rewardAmount = Math.Abs(entryPrice - takeProfit);
                var rewardRiskRatio = rewardAmount / Math.Max(riskAmount, 0.01m);

                if (rewardRiskRatio < (decimal)_bracketConfig.GetMinRewardRiskRatio())
                {
                    // Extend take profit to meet minimum R:R
                    var minReward = riskAmount * (decimal)_bracketConfig.GetMinRewardRiskRatio();
                    takeProfit = entryPrice - minReward;
                }

                _logger.LogDebug("[ZONE-BRACKET] Short {Symbol}: Stop @ {StopLoss:F2} (supply zone), Target @ {TakeProfit:F2} (demand zone)", 
                    symbol, stopLoss, takeProfit);
            }

            return new BracketLevels
            {
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                IsZoneAnchored = true,
                AnchoringMethod = "ZoneAnchored"
            };
        }

        private BracketLevels CalculateTraditional(string symbol, TradingAction action, decimal entryPrice, decimal atr)
        {
            var stopMultiple = (decimal)_bracketConfig.GetDefaultStopAtrMultiple();
            var targetMultiple = (decimal)_bracketConfig.GetDefaultTargetAtrMultiple();

            decimal stopLoss, takeProfit;

            if (action == TradingAction.Buy)
            {
                stopLoss = entryPrice - (stopMultiple * atr);
                takeProfit = entryPrice + (targetMultiple * atr);
            }
            else
            {
                stopLoss = entryPrice + (stopMultiple * atr);
                takeProfit = entryPrice - (targetMultiple * atr);
            }

            _logger.LogDebug("[ZONE-BRACKET] Traditional {Symbol}: Stop @ {StopLoss:F2}, Target @ {TakeProfit:F2}", 
                symbol, stopLoss, takeProfit);

            return new BracketLevels
            {
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                IsZoneAnchored = false,
                AnchoringMethod = "TraditionalATR"
            };
        }

        private static decimal GetTickSize(string symbol)
        {
            return symbol switch
            {
                "ES" => 0.25m,    // ES tick size
                "NQ" => 0.25m,    // NQ tick size
                "YM" => 1.00m,    // YM tick size
                _ => 0.25m        // Default
            };
        }
    }

    /// <summary>
    /// Bracket levels calculated by zone-aware bracket manager
    /// </summary>
    public sealed class BracketLevels
    {
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public bool IsZoneAnchored { get; set; }
        public string AnchoringMethod { get; set; } = string.Empty;
    }
}