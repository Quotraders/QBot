using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of controller options configuration
    /// Replaces hardcoded decisioning parameters and UCB settings
    /// </summary>
    public class ControllerOptionsService : IControllerOptionsService
    {
        // Controller Configuration Constants
        private const double DefaultBullConfidenceLower = 0.6;        // Bull regime confidence lower bound
        private const double DefaultBullConfidenceUpper = 0.9;        // Bull regime confidence upper bound
        private const double DefaultBearConfidenceLower = 0.65;       // Bear regime confidence lower bound
        private const double DefaultBearConfidenceUpper = 0.85;       // Bear regime confidence upper bound
        private const double DefaultSidewaysConfidenceLower = 0.70;   // Sideways regime confidence lower bound
        private const double DefaultSidewaysConfidenceUpper = 0.8;    // Sideways regime confidence upper bound
        private const double DefaultConfidenceLower = 0.65;           // Default confidence lower bound
        private const double DefaultConfidenceUpper = 0.85;           // Default confidence upper bound
        private const int DefaultMaxTradesPerHour = 5;                // Maximum trades per hour limit
        private const int DefaultMaxTradesPerDay = 20;                // Maximum trades per day limit
        private const int DefaultSafeHoldExtrasMinutes = 15;          // Default safe hold extra minutes
        
        // UCB and strategy selection constants
        private const double DefaultUcbExplorationParameter = 1.4;
        private const double DefaultUcbConfidenceWidth = 0.1;
        private const int DefaultUcbMinSamples = 10;
        private const double DefaultStrategySelectionTemperature = 0.5;
        private const int DefaultConfidenceCalibrationLookbackHours = 24;
        private const double DefaultVixMaxValue = 100.0;
        private const double DefaultVolumeRatioMaxValue = 10.0;
        private const double DefaultRsiMaxValue = 100.0;
        private const double DefaultMomentumScaleFactor = 0.05;
        
        // Market impact and volatility constants  
        private const double DefaultVolatilityMaxValue = 5.0;           // Maximum volatility threshold
        private const double DefaultVixNeutralLevel = 0.3;              // VIX neutral impact level
        private const double DefaultVixImpactFactor = 0.3;              // VIX impact scaling factor
        private const double DefaultVolumeImpactFactor = 0.2;           // Volume impact scaling factor
        private const double DefaultMomentumImpactFactor = 0.25;        // Momentum impact scaling factor
        private const double DefaultNoiseAmplitude = 0.05;              // Market noise amplitude
        
        private readonly IConfiguration _config;

        public ControllerOptionsService(IConfiguration config)
        {
            _config = config;
        }

        public (double Lower, double Upper) GetConfidenceBands(string regimeType) => regimeType?.ToUpperInvariant() switch
        {
            "BULL" => (
                _config.GetValue("Controller:ConfidenceBands:Bull:Lower", DefaultBullConfidenceLower),
                _config.GetValue("Controller:ConfidenceBands:Bull:Upper", DefaultBullConfidenceUpper)
            ),
            "BEAR" => (
                _config.GetValue("Controller:ConfidenceBands:Bear:Lower", DefaultBearConfidenceLower),
                _config.GetValue("Controller:ConfidenceBands:Bear:Upper", DefaultBearConfidenceUpper)
            ),
            "sideways" => (
                _config.GetValue("Controller:ConfidenceBands:Sideways:Lower", DefaultSidewaysConfidenceLower),
                _config.GetValue("Controller:ConfidenceBands:Sideways:Upper", DefaultSidewaysConfidenceUpper)
            ),
            _ => (
                _config.GetValue("Controller:ConfidenceBands:Default:Lower", DefaultConfidenceLower),
                _config.GetValue("Controller:ConfidenceBands:Default:Upper", DefaultConfidenceUpper)
            )
        };

        public (int PerHour, int PerDay) GetTradePacingLimits() => (
            _config.GetValue("Controller:TradePacing:MaxPerHour", DefaultMaxTradesPerHour),
            _config.GetValue("Controller:TradePacing:MaxPerDay", DefaultMaxTradesPerDay)
        );

        public int GetSafeHoldExtrasMinutes() => 
            _config.GetValue("Controller:SafeHoldExtrasMinutes", DefaultSafeHoldExtrasMinutes);

        public double GetUcbExplorationParameter() => 
            _config.GetValue("Controller:UCB:ExplorationParameter", DefaultUcbExplorationParameter);

        public double GetUcbConfidenceWidth() => 
            _config.GetValue("Controller:UCB:ConfidenceWidth", DefaultUcbConfidenceWidth);

        public int GetUcbMinSamples() => 
            _config.GetValue("Controller:UCB:MinSamples", DefaultUcbMinSamples);

        public double GetStrategySelectionTemperature() => 
            _config.GetValue("Controller:StrategySelectionTemperature", DefaultStrategySelectionTemperature);

        public bool EnableDynamicConfidenceAdjustment() => 
            _config.GetValue("Controller:EnableDynamicConfidenceAdjustment", true);

        public int GetConfidenceCalibrationLookbackHours() => 
            _config.GetValue("Controller:ConfidenceCalibrationLookbackHours", DefaultConfidenceCalibrationLookbackHours);

        public double GetVixMaxValue() => 
            _config.GetValue("Controller:VixMaxValue", DefaultVixMaxValue);

        public double GetVolumeRatioMaxValue() => 
            _config.GetValue("Controller:VolumeRatioMaxValue", DefaultVolumeRatioMaxValue);

        public double GetRsiMaxValue() => 
            _config.GetValue("Controller:RsiMaxValue", DefaultRsiMaxValue);

        public double GetMomentumScaleFactor() => 
            _config.GetValue("Controller:MomentumScaleFactor", DefaultMomentumScaleFactor);

        public double GetVolatilityMaxValue() => 
            _config.GetValue("Controller:VolatilityMaxValue", DefaultVolatilityMaxValue);

        public double GetVixNeutralLevel() => 
            _config.GetValue("Controller:VixNeutralLevel", DefaultVixNeutralLevel);

        public double GetVixImpactFactor() => 
            _config.GetValue("Controller:VixImpactFactor", DefaultVixImpactFactor);

        public double GetVolumeImpactFactor() => 
            _config.GetValue("Controller:VolumeImpactFactor", DefaultVolumeImpactFactor);

        public double GetMomentumImpactFactor() => 
            _config.GetValue("Controller:MomentumImpactFactor", DefaultMomentumImpactFactor);

        public double GetNoiseAmplitude() => 
            _config.GetValue("Controller:NoiseAmplitude", DefaultNoiseAmplitude);
    }
}