using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace TradingBot.BotCore.Services.Helpers
{
    /// <summary>
    /// Centralized strategy constants and configuration to eliminate duplication
    /// Single source of truth for all strategy-related constants and metadata
    /// </summary>
    internal static class StrategyConstants
    {
        /// <summary>
        /// All supported trading strategies
        /// </summary>
        public static readonly string[] AllStrategies = { "S2", "S3", "S6", "S11" };

        /// <summary>
        /// Strategy names with descriptions
        /// </summary>
        public static readonly Dictionary<string, string> StrategyDescriptions = new()
        {
            ["S2"] = "VWAP Mean Reversion - Best overnight and lunch",
            ["S3"] = "Compression Breakout - Best at session opens", 
            ["S6"] = "Opening Drive - ONLY 9:28-10:00",
            ["S11"] = "ADR Exhaustion - Best afternoon"
        };

        /// <summary>
        /// Check if a strategy ID is valid
        /// </summary>
        public static bool IsValidStrategy(string strategyId)
        {
            return AllStrategies.Contains(strategyId);
        }

        /// <summary>
        /// Get strategy description
        /// </summary>
        public static string GetStrategyDescription(string strategyId)
        {
            return StrategyDescriptions.TryGetValue(strategyId, out var description) 
                ? description 
                : "Unknown strategy";
        }
    }

    /// <summary>
    /// Shared helper for strategy metrics calculations
    /// Eliminates duplication of strategy-specific switch statements
    /// </summary>
    internal static class StrategyMetricsHelper
    {
        // Strategy base win rate constants
        private const decimal S2WinRate = 0.58m;   // Mean reversion strategy
        private const decimal S3WinRate = 0.45m;   // Volatility breakout strategy
        private const decimal S6WinRate = 0.52m;   // Momentum strategy
        private const decimal S11WinRate = 0.48m;  // Trend following strategy
        private const decimal DefaultWinRate = 0.50m; // Default balanced win rate
        
        // Strategy risk-reward ratio constants
        private const decimal S2RiskRewardRatio = 1.3m;          // S2 mean reversion modest R:R
        private const decimal S3RiskRewardRatio = 1.8m;          // S3 breakout higher R:R  
        private const decimal S6RiskRewardRatio = 1.5m;          // S6 momentum moderate R:R
        private const decimal S11RiskRewardRatio = 2.2m;         // S11 trend following highest R:R
        private const decimal DefaultRiskRewardRatio = 1.5m;     // Default moderate R:R
        
        // Strategy maximum drawdown expectation constants
        private const decimal S2MaxDrawdownExpectation = 0.15m;   // S2 mean reversion lower drawdown
        private const decimal S3MaxDrawdownExpectation = 0.25m;   // S3 breakout moderate drawdown
        private const decimal S6MaxDrawdownExpectation = 0.20m;   // S6 momentum moderate drawdown
        private const decimal S11MaxDrawdownExpectation = 0.30m;  // S11 trend following higher drawdown
        private const decimal DefaultMaxDrawdownExpectation = 0.20m; // Default moderate drawdown
        
        /// <summary>
        /// Get strategy base win rate
        /// Consolidates all strategy-specific win rate logic
        /// </summary>
        public static decimal GetStrategyBaseWinRate(string strategyId)
        {
            return strategyId switch
            {
                "S2" => S2WinRate,
                "S3" => S3WinRate,
                "S6" => S6WinRate,
                "S11" => S11WinRate,
                _ => DefaultWinRate
            };
        }

        /// <summary>
        /// Get strategy risk/reward ratio
        /// Consolidates risk/reward calculations
        /// </summary>
        public static decimal GetStrategyRiskRewardRatio(string strategyId)
        {
            return strategyId switch
            {
                "S2" => S2RiskRewardRatio,
                "S3" => S3RiskRewardRatio,
                "S6" => S6RiskRewardRatio,
                "S11" => S11RiskRewardRatio,
                _ => DefaultRiskRewardRatio
            };
        }

        /// <summary>
        /// Get strategy maximum drawdown ratio
        /// Consolidates drawdown calculations
        /// </summary>
        public static decimal GetStrategyMaxDrawdownRatio(string strategyId)
        {
            return strategyId switch
            {
                "S2" => S2MaxDrawdownExpectation,
                "S3" => S3MaxDrawdownExpectation,
                "S6" => S6MaxDrawdownExpectation,
                "S11" => S11MaxDrawdownExpectation,
                _ => DefaultMaxDrawdownExpectation
            };
        }

        /// <summary>
        /// Get strategy multiplier for performance calculations with null safety
        /// </summary>
        public static decimal GetStrategyMultiplier(string strategyId, IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            if (parameters == null || !parameters.Any())
            {
                return DefaultStrategyMultiplier;
            }

            return strategyId switch
            {
                "S2" => GetS2StrategyMultiplier(parameters),
                "S3" => GetS3StrategyMultiplier(parameters),
                "S6" => GetS6StrategyMultiplier(parameters),
                "S11" => GetS11StrategyMultiplier(parameters),
                _ => DefaultStrategyMultiplier
            };
        }

        /// <summary>
        /// Get configuration impact on performance with null safety
        /// </summary>
        public static decimal GetConfigurationImpact(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            if (parameters == null || !parameters.Any())
            {
                return 0m;
            }

            decimal impact = 0m;
            
            foreach (var param in parameters)
            {
                if (param?.Key == null) continue;
                
                impact += param.Key switch
                {
                    "sigma_enter" when param.DecimalValue.HasValue => 
                        Math.Max(-0.05m, Math.Min(0.05m, (2.0m - param.DecimalValue.Value) * 0.02m)),
                    "width_rank_threshold" when param.DecimalValue.HasValue => 
                        Math.Max(-0.03m, Math.Min(0.03m, (0.25m - param.DecimalValue.Value) * 0.1m)),
                    _ => 0m
                };
            }
            
            return impact;
        }

        /// <summary>
        /// S2 strategy multiplier calculation with null safety
        /// </summary>
        private static decimal GetS2StrategyMultiplier(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            var sigmaEnter = GetParameterValue(parameters, "sigma_enter", 2.0m);
            var widthRank = GetParameterValue(parameters, "width_rank_threshold", 0.25m);
            return Math.Max(MinimumMultiplierBound, Math.Min(2.0m, (sigmaEnter / 2.0m) * (1.0m - widthRank)));
        }

        /// <summary>
        /// S3 strategy multiplier calculation with null safety
        /// </summary>
        private static decimal GetS3StrategyMultiplier(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            var squeezeThreshold = GetParameterValue(parameters, "squeeze_threshold", 0.8m);
            var breakoutConfidence = GetParameterValue(parameters, "breakout_confidence", 0.7m);
            return Math.Max(0.3m, Math.Min(1.8m, squeezeThreshold * breakoutConfidence));
        }

        /// <summary>
        /// S6 strategy multiplier calculation with null safety
        /// </summary>
        private static decimal GetS6StrategyMultiplier(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            var momentumLookback = GetParameterValue(parameters, "momentum_lookback", 20);
            var momentumThreshold = GetParameterValue(parameters, "momentum_threshold", 0.6m);
            return Math.Max(0.4m, Math.Min(1.6m, (momentumLookback / 20.0m) * momentumThreshold));
        }

        /// <summary>
        /// S11 strategy multiplier calculation with null safety
        /// </summary>
        private static decimal GetS11StrategyMultiplier(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters)
        {
            var trendLength = GetParameterValue(parameters, "trend_length", 30);
            var trendStrength = GetParameterValue(parameters, "trend_strength", 0.8m);
            return Math.Max(0.2m, Math.Min(2.5m, (trendLength / 30.0m) * trendStrength));
        }

        /// <summary>
        /// Helper method to safely extract parameter values from configuration with null safety
        /// </summary>
        private static decimal GetParameterValue(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters, string parameterName, decimal defaultValue)
        {
            if (parameters == null || string.IsNullOrEmpty(parameterName))
            {
                return defaultValue;
            }

            foreach (var param in parameters)
            {
                if (param?.Key == parameterName)
                {
                    return param.DecimalValue ?? param.IntValue ?? defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Helper method to safely extract integer parameter values with null safety
        /// </summary>
        private static int GetParameterValue(IReadOnlyList<TradingBotTuningRunner.ParameterConfig> parameters, string parameterName, int defaultValue)
        {
            if (parameters == null || string.IsNullOrEmpty(parameterName))
            {
                return defaultValue;
            }

            foreach (var param in parameters)
            {
                if (param?.Key == parameterName)
                {
                    return param.IntValue ?? (int)(param.DecimalValue ?? defaultValue);
                }
            }
            return defaultValue;
        }
    }

    /// <summary>
    /// Shared helper for service provider access patterns
    /// Eliminates duplication and improves null safety
    /// </summary>
    internal static class ServiceProviderHelper
    {
        /// <summary>
        /// Safely access a service from the service provider with proper disposal
        /// </summary>
        public static T? GetService<T>(IServiceProvider? serviceProvider) where T : class
        {
            if (serviceProvider == null)
            {
                return null;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetService<T>();
            }
            catch (ObjectDisposedException)
            {
                // Service provider has been disposed
                return null;
            }
            catch (Exception)
            {
                // Any other exception during service resolution
                return null;
            }
        }

        /// <summary>
        /// Safely execute an action with a service from the service provider
        /// </summary>
        public static TResult ExecuteWithService<TService, TResult>(
            IServiceProvider? serviceProvider, 
            Func<TService, TResult> action, 
            TResult fallbackResult) where TService : class
        {
            if (serviceProvider == null || action == null)
            {
                return fallbackResult;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetService<TService>();
                return service != null ? action(service) : fallbackResult;
            }
            catch (ObjectDisposedException)
            {
                return fallbackResult;
            }
            catch (Exception)
            {
                return fallbackResult;
            }
        }
    }
}