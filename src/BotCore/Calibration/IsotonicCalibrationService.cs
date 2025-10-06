using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Calibration
{
    /// <summary>
    /// Isotonic Calibration Service for breakout scores
    /// Calibrates raw model outputs to true probabilities using historical data
    /// Implements fail-closed behavior - missing calibration tables result in HOLD decisions
    /// </summary>
    public sealed class IsotonicCalibrationService
    {
        private readonly ILogger<IsotonicCalibrationService> _logger;
        private readonly CalibrationConfiguration _config;
        private readonly Dictionary<string, CalibrationTable> _calibrationTables = new();
        private readonly object _calibrationLock = new();

        public IsotonicCalibrationService(
            ILogger<IsotonicCalibrationService> logger,
            IOptions<CalibrationConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Validate configuration with fail-closed behavior
            _config.Validate();
        }

        /// <summary>
        /// Apply isotonic calibration to breakout score
        /// Returns calibrated probability or throws exception if calibration table missing (fail-closed)
        /// </summary>
        public async Task<double> CalibrateBreakoutScoreAsync(
            string symbol, 
            string regime, 
            double rawScore, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("[ISOTONIC-CALIBRATION] Symbol cannot be null or empty", nameof(symbol));
            if (string.IsNullOrWhiteSpace(regime))
                throw new ArgumentException("[ISOTONIC-CALIBRATION] Regime cannot be null or empty", nameof(regime));
            if (rawScore < 0.0 || rawScore > 1.0)
                throw new ArgumentException("[ISOTONIC-CALIBRATION] Raw score must be between 0 and 1", nameof(rawScore));

            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                var tableKey = CreateTableKey(symbol, regime);
                
                lock (_calibrationLock)
                {
                    if (!_calibrationTables.TryGetValue(tableKey, out var calibrationTable))
                    {
                        _logger.LogError("[ISOTONIC-CALIBRATION] [AUDIT-VIOLATION] Missing calibration table for {Symbol}_{Regime} - FAIL-CLOSED + TELEMETRY", 
                            symbol, regime);
                        
                        // Fail-closed: throw exception to trigger HOLD decision
                        throw new InvalidOperationException($"[ISOTONIC-CALIBRATION] Missing calibration table for '{tableKey}' - TRIGGERING HOLD");
                    }

                    var calibratedScore = ApplyIsotonicCalibration(rawScore, calibrationTable);
                    
                    _logger.LogTrace("[ISOTONIC-CALIBRATION] {Symbol} {Regime}: Raw={RawScore:F4} -> Calibrated={CalibratedScore:F4}", 
                        symbol, regime, rawScore, calibratedScore);
                    
                    return calibratedScore;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ISOTONIC-CALIBRATION] [AUDIT-VIOLATION] Calibration failed for {Symbol}_{Regime} - FAIL-CLOSED + TELEMETRY", 
                    symbol, regime);
                
                // Fail-closed: re-throw to trigger HOLD decision
                throw new InvalidOperationException($"[ISOTONIC-CALIBRATION] Critical calibration failure for '{symbol}_{regime}': {ex.Message}", ex);
            }
        }

        private static string CreateTableKey(string symbol, string regime)
        {
            return $"breakout_{symbol.ToLowerInvariant()}_{regime.ToLowerInvariant()}";
        }

        private static double ApplyIsotonicCalibration(double rawScore, CalibrationTable table)
        {
            var calibrationPoints = table.CalibrationPoints;
            
            // Find the appropriate calibration range
            for (int i = 0; i < calibrationPoints.Count - 1; i++)
            {
                var current = calibrationPoints[i];
                var next = calibrationPoints[i + 1];

                if (rawScore >= current.PredictedScore && rawScore <= next.PredictedScore)
                {
                    // Linear interpolation between calibration points
                    var fraction = (rawScore - current.PredictedScore) / (next.PredictedScore - current.PredictedScore);
                    return current.CalibratedProbability + fraction * (next.CalibratedProbability - current.CalibratedProbability);
                }
            }

            // Handle edge cases
            if (rawScore <= calibrationPoints[0].PredictedScore)
                return calibrationPoints[0].CalibratedProbability;
            
            return calibrationPoints[^1].CalibratedProbability;
        }
    }

    /// <summary>
    /// Configuration for calibration service - all defaults must be explicit (fail-closed requirement)
    /// </summary>
    public sealed class CalibrationConfiguration
    {
        public string CalibrationTableDirectory { get; set; } = string.Empty;
        public int MinCalibrationDataPoints { get; set; }
        public int CalibrationBinCount { get; set; }
        public double CalibrationUpdateThreshold { get; set; }

        /// <summary>
        /// Validates configuration values with fail-closed behavior
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(CalibrationTableDirectory))
                throw new InvalidOperationException("[ISOTONIC-CALIBRATION] [AUDIT-VIOLATION] CalibrationTableDirectory cannot be empty - FAIL-CLOSED");
            if (MinCalibrationDataPoints <= 0 || CalibrationBinCount <= 0)
                throw new InvalidOperationException("[ISOTONIC-CALIBRATION] [AUDIT-VIOLATION] Data points and bin count must be positive - FAIL-CLOSED");
            if (CalibrationUpdateThreshold <= 0 || CalibrationUpdateThreshold >= 1.0)
                throw new InvalidOperationException("[ISOTONIC-CALIBRATION] [AUDIT-VIOLATION] Update threshold must be between 0 and 1 - FAIL-CLOSED");
        }
    }

    /// <summary>
    /// Calibration table for isotonic regression
    /// </summary>
    public sealed class CalibrationTable
    {
        public string Symbol { get; set; } = string.Empty;
        public string Regime { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int DataPointCount { get; set; }
        public List<CalibrationPoint> CalibrationPoints { get; set; } = new();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Symbol))
                throw new InvalidOperationException("[CALIBRATION-TABLE] Symbol is required");
            if (string.IsNullOrWhiteSpace(Regime))
                throw new InvalidOperationException("[CALIBRATION-TABLE] Regime is required");
            if (CalibrationPoints.Count == 0)
                throw new InvalidOperationException("[CALIBRATION-TABLE] CalibrationPoints cannot be empty");
            if (DataPointCount <= 0)
                throw new InvalidOperationException("[CALIBRATION-TABLE] DataPointCount must be positive");
        }
    }

    /// <summary>
    /// Individual calibration point
    /// </summary>
    public sealed class CalibrationPoint
    {
        public double PredictedScore { get; set; }
        public double CalibratedProbability { get; set; }
    }
}