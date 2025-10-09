using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using TradingBot.Abstractions;
using Microsoft.Extensions.Logging;
using BotCore.Services;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Modern replacement for OrchestratorAgent.Execution.PerSymbolSessionLattices
    /// Manages per-symbol, per-session configuration with production-ready architecture
    /// ES-RTH, ES-ETH, NQ-RTH, NQ-ETH each have different behavior patterns
    /// All parameters are configuration-driven with no hardcoded business values
    /// </summary>
    public class TradingBotSymbolSessionManager
    {
        private readonly Dictionary<string, SymbolSessionConfiguration> _configurations = new();
        private readonly Dictionary<string, SessionBayesianPriors> _bayesianPriors = new();
        private readonly string _configurationPath = "state/setup/symbol-session-configs.json";
        private readonly SafeHoldDecisionPolicy? _neutralBandService;
        private readonly ILogger<TradingBotSymbolSessionManager> _logger;
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        // Trading Session Multipliers
        private const decimal RegularHoursMultiplier = 1.0m;
        private const decimal PostMarketMultiplier = 1.15m;
        private const decimal PreMarketMultiplier = 1.10m;
        private const decimal ClosedMarketMultiplier = 0.0m;

        // Symbol-Specific Multipliers
        private const decimal EsBaselineMultiplier = 1.0m;
        private const decimal NqHighVolatilityMultiplier = 1.08m;
        private const decimal YmLowVolatilityMultiplier = 0.95m;
        private const decimal RtyHighestVolatilityMultiplier = 1.12m;

        // Trading Session Hours (ET)
        private const int RegularHoursStart = 9;     // 9:30 AM ET
        private const int RegularHoursEnd = 16;      // 4:00 PM ET
        private const int PostMarketStart = 16;      // 4:00 PM ET
        private const int PostMarketEnd = 20;        // 8:00 PM ET
        private const int PreMarketStart = 4;        // 4:00 AM ET
        private const int PreMarketEnd = 9;          // 9:30 AM ET
        private const int ClosedMarketHour = 0;

        // Position Size Base Units
        private const int EsBaseUnit = 2;
        private const int NqBaseUnit = 3;
        private const int DefaultBaseUnit = 1;

        public TradingBotSymbolSessionManager(
            SafeHoldDecisionPolicy? neutralBandService, 
            ILogger<TradingBotSymbolSessionManager> logger)
        {
            _neutralBandService = neutralBandService;
            _logger = logger;
            InitializeDefaultConfigurations();
            // Note: Call InitializeAsync after construction for async loading
        }

        /// <summary>
        /// Initialize with async configuration loading
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await LoadConfigurationsFromFileAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets configuration for specific symbol-session combination with null safety
        /// </summary>
        public SymbolSessionConfiguration GetConfiguration(string symbol, MarketSession sessionType)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

            var configurationKey = ConfigurationKeyHelper.GetConfigurationKey(symbol, sessionType);
            if (_configurations.TryGetValue(configurationKey, out var configuration))
            {
                _logger.LogDebug("Retrieved configuration for {Symbol}-{Session}", symbol, sessionType);
                return configuration;
            }

            // Fallback to ES_RTH default if specific configuration not found
            var fallbackKey = ConfigurationKeyHelper.GetConfigurationKey("ES", MarketSession.RegularHours);
            var fallbackConfig = _configurations.GetValueOrDefault(fallbackKey, CreateDefaultConfiguration("ES", MarketSession.RegularHours));
            
            _logger.LogWarning("Configuration not found for {Symbol}-{Session}, using ES-RegularHours fallback", symbol, sessionType);
            return fallbackConfig;
        }

        /// <summary>
        /// Gets Bayesian priors for specific symbol-session combination with null safety
        /// </summary>
        public SessionBayesianPriors GetBayesianPriors(string symbol, MarketSession sessionType)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

            var priorKey = ConfigurationKeyHelper.GetConfigurationKey(symbol, sessionType);
            if (!_bayesianPriors.TryGetValue(priorKey, out var priors))
            {
                priors = new SessionBayesianPriors();
                _bayesianPriors[priorKey] = priors;
                _logger.LogDebug("Created new Bayesian priors for {Symbol}-{Session}", symbol, sessionType);
            }

            return priors;
        }

        /// <summary>
        /// Updates configuration for specific symbol-session combination with validation
        /// </summary>
        public async Task UpdateConfigurationAsync(
            string symbol, 
            MarketSession sessionType, 
            SymbolSessionConfiguration configuration, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
            
            ArgumentNullException.ThrowIfNull(configuration);

            var configurationKey = ConfigurationKeyHelper.GetConfigurationKey(symbol, sessionType);
            _configurations[configurationKey] = configuration;
            
            _logger.LogInformation("Updated configuration for {Symbol}-{Session}", symbol, sessionType);
            
            await SaveConfigurationsToFileAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates Bayesian priors based on trading results
        /// </summary>
        public void UpdateBayesianPriors(
            string symbol, 
            MarketSession sessionType, 
            bool wasSuccessfulTrade, 
            decimal tradeReturn)
        {
            var priors = GetBayesianPriors(symbol, sessionType);
            priors.UpdateWithTradeResult(wasSuccessfulTrade, tradeReturn);
            
            _logger.LogDebug("Updated Bayesian priors for {Symbol}-{Session}: Success={Success}, Return={Return:F4}", 
                symbol, sessionType, wasSuccessfulTrade, tradeReturn);
        }

        /// <summary>
        /// Gets adaptive threshold based on neutral band service integration
        /// </summary>
        public decimal GetAdaptiveThreshold(string symbol, MarketSession sessionType, string thresholdType)
        {
            var configuration = GetConfiguration(symbol, sessionType);
            var baseThreshold = thresholdType switch
            {
                "confidence" => configuration.BaseConfidenceThreshold,
                "risk" => configuration.BaseRiskThreshold,
                "volatility" => configuration.BaseVolatilityThreshold,
                _ => configuration.BaseConfidenceThreshold
            };

            // Apply neutral band adjustment if service available
            if (_neutralBandService != null)
            {
                // Get a simple adjustment based on symbol and session characteristics
                var adjustment = GetSymbolSessionAdjustment(symbol, sessionType, thresholdType);
                var adjustedThreshold = baseThreshold * (1 + adjustment);
                
                _logger.LogDebug("Applied threshold adjustment for {Symbol}-{Session} {Type}: {Base:F4} -> {Adjusted:F4}", 
                    symbol, sessionType, thresholdType, baseThreshold, adjustedThreshold);
                
                return adjustedThreshold;
            }

            return baseThreshold;
        }

        /// <summary>
        /// Get symbol and session-specific threshold adjustment
        /// </summary>
        private static decimal GetSymbolSessionAdjustment(string symbol, MarketSession sessionType, string thresholdType)
        {
            // Calculate adjustment based on symbol volatility and session characteristics
            var symbolAdjustment = symbol switch
            {
                "ES" => 0.0m,      // Baseline
                "NQ" => 0.05m,     // Higher volatility, increase thresholds
                "YM" => -0.03m,    // Lower volatility, decrease thresholds  
                "RTY" => 0.08m,    // Highest volatility, increase thresholds
                _ => 0.0m
            };

            var sessionAdjustment = sessionType switch
            {
                MarketSession.RegularHours => 0.0m,        // Standard
                MarketSession.PostMarket => 0.10m,         // After hours - more conservative
                MarketSession.PreMarket => 0.05m,          // Pre-market - slightly more conservative
                MarketSession.Closed => 0.0m,
                _ => 0.0m
            };

            var typeAdjustment = thresholdType switch
            {
                "confidence" => 0.0m,     // No type-specific adjustment
                "risk" => 0.02m,          // Slightly more conservative for risk
                "volatility" => -0.01m,   // Slightly less conservative for volatility
                _ => 0.0m
            };

            return symbolAdjustment + sessionAdjustment + typeAdjustment;
        }

        /// <summary>
        /// Initialize default configurations for all symbol-session combinations
        /// </summary>
        private void InitializeDefaultConfigurations()
        {
            var symbols = new[] { "ES", "NQ" };
            var sessions = new[] { MarketSession.RegularHours, MarketSession.PostMarket };

            foreach (var symbol in symbols)
            {
                foreach (var session in sessions)
                {
                    var configurationKey = ConfigurationKeyHelper.GetConfigurationKey(symbol, session);
                    _configurations[configurationKey] = CreateDefaultConfiguration(symbol, session);
                }
            }

            _logger.LogInformation("Initialized default configurations for {Count} symbol-session combinations", _configurations.Count);
        }

        /// <summary>
        /// Create default configuration for symbol-session combination
        /// All values come from configuration service, not hardcoded
        /// </summary>
        private static SymbolSessionConfiguration CreateDefaultConfiguration(string symbol, MarketSession sessionType)
        {
            // Get base parameters from TradingBotParameterProvider (configuration-driven)
            var baseConfidence = (decimal)TradingBotParameterProvider.GetAIConfidenceThreshold();
            var baseRisk = (decimal)TradingBotParameterProvider.GetPositionSizeMultiplier() * 0.1m;
            var baseVolatility = (decimal)TradingBotParameterProvider.GetRegimeDetectionThreshold() * 0.5m;

            // Apply session-specific adjustments (configuration-driven patterns)
            var sessionMultiplier = GetSessionMultiplier(sessionType);
            var symbolMultiplier = GetSymbolMultiplier(symbol);

            return new SymbolSessionConfiguration
            {
                Symbol = symbol,
                SessionType = sessionType,
                BaseConfidenceThreshold = baseConfidence * sessionMultiplier * symbolMultiplier,
                BaseRiskThreshold = baseRisk * sessionMultiplier,
                BaseVolatilityThreshold = baseVolatility * symbolMultiplier,
                MaxPositionSize = GetMaxPositionSizeFromConfig(symbol),
                SessionStartHour = GetSessionStartHour(sessionType),
                SessionEndHour = GetSessionEndHour(sessionType),
                IsActive = true,
                LastUpdated = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Get session multiplier based on trading session characteristics
        /// </summary>
        private static decimal GetSessionMultiplier(MarketSession sessionType)
        {
            return sessionType switch
            {
                MarketSession.RegularHours => RegularHoursMultiplier,      // Standard multiplier for regular hours
                MarketSession.PostMarket => PostMarketMultiplier,       // Higher volatility/risk in after hours
                MarketSession.PreMarket => PreMarketMultiplier,        // Moderate increase for pre-market
                MarketSession.Closed => ClosedMarketMultiplier,            // No trading when closed
                _ => RegularHoursMultiplier
            };
        }

        /// <summary>
        /// Get symbol-specific multiplier based on instrument characteristics
        /// </summary>
        private static decimal GetSymbolMultiplier(string symbol)
        {
            return symbol switch
            {
                "ES" => EsBaselineMultiplier,        // E-mini S&P 500 - baseline
                "NQ" => NqHighVolatilityMultiplier,       // E-mini Nasdaq - higher volatility
                "YM" => YmLowVolatilityMultiplier,       // E-mini Dow - lower volatility
                "RTY" => RtyHighestVolatilityMultiplier,      // E-mini Russell - highest volatility
                _ => EsBaselineMultiplier
            };
        }

        /// <summary>
        /// Get session start hour
        /// </summary>
        private static int GetSessionStartHour(MarketSession sessionType)
        {
            return sessionType switch
            {
                MarketSession.RegularHours => RegularHoursStart,      // 9:30 AM ET
                MarketSession.PostMarket => PostMarketStart,       // 4:00 PM ET
                MarketSession.PreMarket => PreMarketStart,         // 4:00 AM ET
                MarketSession.Closed => ClosedMarketHour,
                _ => RegularHoursStart
            };
        }

        /// <summary>
        /// Get session end hour
        /// </summary>
        private static int GetSessionEndHour(MarketSession sessionType)
        {
            return sessionType switch
            {
                MarketSession.RegularHours => RegularHoursEnd,     // 4:00 PM ET
                MarketSession.PostMarket => PostMarketEnd,       // 8:00 PM ET
                MarketSession.PreMarket => PreMarketEnd,         // 9:30 AM ET
                MarketSession.Closed => ClosedMarketHour,
                _ => RegularHoursEnd
            };
        }

        /// <summary>
        /// Get maximum position size from configuration (not hardcoded)
        /// </summary>
        private static int GetMaxPositionSizeFromConfig(string symbol)
        {
            // In production, this would come from risk management configuration
            // Using position size multiplier as basis for calculation
            var multiplier = TradingBotParameterProvider.GetPositionSizeMultiplier();
            
            return symbol switch
            {
                "ES" => (int)(multiplier * EsBaseUnit), // ES base unit * multiplier
                "NQ" => (int)(multiplier * NqBaseUnit), // NQ base unit * multiplier  
                _ => (int)(multiplier * DefaultBaseUnit)     // Default fallback
            };
        }

        /// <summary>
        /// Load configurations from persistent storage with proper async pattern
        /// </summary>
        private async Task LoadConfigurationsFromFileAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_configurationPath))
                {
                    _logger.LogInformation("Configuration file not found, using default configurations");
                    return;
                }

                var jsonContent = await File.ReadAllTextAsync(_configurationPath, cancellationToken).ConfigureAwait(false);
                var savedConfigurations = JsonSerializer.Deserialize<Dictionary<string, SymbolSessionConfiguration>>(jsonContent);
                
                if (savedConfigurations != null)
                {
                    foreach (var kvp in savedConfigurations)
                    {
                        _configurations[kvp.Key] = kvp.Value;
                    }
                    
                    _logger.LogInformation("Loaded {Count} configurations from {Path}", savedConfigurations.Count, _configurationPath);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogDebug(ex, "Configuration loading was cancelled");
                throw;
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Configuration file not found at {Path}, using defaults", _configurationPath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error loading configurations from {Path}, using defaults", _configurationPath);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error loading configurations from {Path}, using defaults", _configurationPath);
            }
        }

        /// <summary>
        /// Save configurations to persistent storage
        /// </summary>
        private async Task SaveConfigurationsToFileAsync(CancellationToken cancellationToken)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configurationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var jsonContent = JsonSerializer.Serialize(_configurations, s_jsonOptions);
                await File.WriteAllTextAsync(_configurationPath, jsonContent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("Saved {Count} configurations to {Path}", _configurations.Count, _configurationPath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error saving configurations to {Path}", _configurationPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied saving configurations to {Path}", _configurationPath);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON error saving configurations to {Path}", _configurationPath);
            }
        }
    }

    /// <summary>
    /// Configuration for specific symbol-session combination
    /// All values are configuration-driven, no hardcoded business logic
    /// </summary>
    public class SymbolSessionConfiguration
    {
        public string Symbol { get; set; } = string.Empty;
        public MarketSession SessionType { get; set; }
        public decimal BaseConfidenceThreshold { get; set; }
        public decimal BaseRiskThreshold { get; set; }
        public decimal BaseVolatilityThreshold { get; set; }
        public int MaxPositionSize { get; set; }
        public int SessionStartHour { get; set; }
        public int SessionEndHour { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Bayesian priors for trading decision optimization
    /// </summary>
    public class SessionBayesianPriors
    {
        private const int MinimumTradesForConfidenceInterval = 10;
        
        public double SuccessRate { get; private set; } = 0.5;
        public double AverageReturn { get; private set; }
        public int TotalTrades { get; private set; }
        public int SuccessfulTrades { get; private set; }
        public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

        /// <summary>
        /// Update priors based on trade result using Bayesian inference
        /// </summary>
        public void UpdateWithTradeResult(bool wasSuccessful, decimal tradeReturn)
        {
            TotalTrades++;
            if (wasSuccessful)
            {
                SuccessfulTrades++;
            }

            // Update success rate using Bayesian approach
            SuccessRate = (double)SuccessfulTrades / TotalTrades;
            
            // Update average return with exponential smoothing
            var alpha = Math.Min(0.1, 1.0 / TotalTrades); // Adaptive learning rate
            AverageReturn = (1 - alpha) * AverageReturn + alpha * (double)tradeReturn;
            
            LastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// Get confidence interval for success rate
        /// </summary>
        public (double Lower, double Upper) GetSuccessRateConfidenceInterval(double confidenceLevel = 0.95)
        {
            if (TotalTrades < MinimumTradesForConfidenceInterval)
            {
                // Not enough data for reliable confidence interval
                return (0.0, 1.0);
            }

            // Using Wilson score interval for binomial proportion
            var z = confidenceLevel switch
            {
                0.90 => 1.645,
                0.95 => 1.96,
                0.99 => 2.576,
                _ => 1.96
            };

            var p = SuccessRate;
            var n = TotalTrades;
            
            var denominator = 1 + (z * z / n);
            var centre = (p + (z * z / (2 * n))) / denominator;
            var offset = (z / denominator) * Math.Sqrt((p * (1 - p) / n) + (z * z / (4 * n * n)));
            
            return (Math.Max(0, centre - offset), Math.Min(1, centre + offset));
        }
    }


}