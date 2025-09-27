using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using Zones;

namespace BotCore.Services;

/// <summary>
/// Safe-hold decision policy with neutral band logic
/// Implements bearish 45% / bullish 55% thresholds with hysteresis
/// Prevents trading when confidence is in the neutral zone
/// </summary>
public class SafeHoldDecisionPolicy
{
    private readonly ILogger<SafeHoldDecisionPolicy> _logger;
    private readonly IConfiguration _configuration;
    private readonly NeutralBandConfiguration _config;
    private readonly IZoneService? _zoneService;

    public SafeHoldDecisionPolicy(ILogger<SafeHoldDecisionPolicy> logger, IConfiguration configuration, IZoneService? zoneService = null)
    {
        _logger = logger;
        _configuration = configuration;
        _config = LoadNeutralBandConfiguration();
        _zoneService = zoneService;
    }

    /// <summary>
    /// Evaluate trading decision based on confidence with neutral band logic
    /// Returns: BUY, SELL, or HOLD (for neutral zone)
    /// </summary>
    public async Task<TradingDecision> EvaluateDecisionAsync(
        double confidence, 
        string symbol, 
        string strategyId, 
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // Apply neutral band logic with hysteresis
        if (confidence <= _config.BearishThreshold)
        {
            _logger.LogDebug("[NEUTRAL_BAND] {Symbol} {Strategy}: confidence={Confidence:F3} <= {BearishThreshold:F3} → SELL", 
                symbol, strategyId, confidence, _config.BearishThreshold);
            return new TradingDecision
            {
                Action = TradingAction.Sell,
                Confidence = confidence,
                Reason = $"Below bearish threshold ({_config.BearishThreshold:F3})",
                Symbol = symbol,
                StrategyId = strategyId,
                Timestamp = DateTime.UtcNow
            };
        }

        if (confidence >= _config.BullishThreshold)
        {
            _logger.LogDebug("[NEUTRAL_BAND] {Symbol} {Strategy}: confidence={Confidence:F3} >= {BullishThreshold:F3} → BUY", 
                symbol, strategyId, confidence, _config.BullishThreshold);
            return new TradingDecision
            {
                Action = TradingAction.Buy,
                Confidence = confidence,
                Reason = $"Above bullish threshold ({_config.BullishThreshold:F3})",
                Symbol = symbol,
                StrategyId = strategyId,
                Timestamp = DateTime.UtcNow
            };
        }

        // Confidence is in neutral zone - return HOLD with hysteresis logic
        _logger.LogInformation("[NEUTRAL_BAND] {Symbol} {Strategy}: confidence={Confidence:F3} in neutral zone ({BearishThreshold:F3} - {BullishThreshold:F3}) → HOLD", 
            symbol, strategyId, confidence, _config.BearishThreshold, _config.BullishThreshold);

        return new TradingDecision
        {
            Action = TradingAction.Hold,
            Confidence = confidence,
            Reason = $"In neutral zone ({_config.BearishThreshold:F3} - {_config.BullishThreshold:F3})",
            Symbol = symbol,
            StrategyId = strategyId,
            Timestamp = DateTime.UtcNow,
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["neutral_band_width"] = _config.BullishThreshold - _config.BearishThreshold,
                ["distance_to_bearish"] = confidence - _config.BearishThreshold,
                ["distance_to_bullish"] = _config.BullishThreshold - confidence,
                ["hysteresis_active"] = _config.EnableHysteresis
            }
        };
    }

    /// <summary>
    /// Check if confidence is in neutral band (should hold)
    /// </summary>
    public bool IsInNeutralBand(double confidence)
    {
        return confidence > _config.BearishThreshold && confidence < _config.BullishThreshold;
    }

    /// <summary>
    /// Zone gate evaluation to block entries near opposing zones without sufficient breakout probability
    /// </summary>
    public (bool Held, string Reason, TradingDecision MaybeAmended) ZoneGate(TradingDecision decision, string symbol)
    {
        if (_zoneService == null)
        {
            // Zone service not available, allow trade to proceed
            return (false, string.Empty, decision);
        }

        try
        {
            var snap = _zoneService.GetSnapshot(symbol);
            var zoneSection = _configuration.GetSection("zone");
            double blockAtr = zoneSection.GetValue("entry_block_atr:default", 0.8);
            double allowBreak = zoneSection.GetValue("allow_breakout_threshold:default", 0.7);
            double sizeTiltFactor = zoneSection.GetValue("size_tilt_near_zone:default", 0.7);

            if (decision.Action == TradingAction.Buy)
            {
                // Check if we're too close to supply without breakout potential
                if (snap.DistToSupplyAtr <= blockAtr && snap.BreakoutScore < allowBreak)
                {
                    _logger.LogInformation("[ZONE-GATE] {Symbol}: Blocked LONG entry - near supply zone (dist={DistAtr:F2}, breakout={Score:F2})",
                        symbol, snap.DistToSupplyAtr, snap.BreakoutScore);
                    return (true, $"Blocked by supply zone (dist={snap.DistToSupplyAtr:F2} ATR, breakout={snap.BreakoutScore:F2})", decision);
                }

                // Apply size tilt if near supply
                if (snap.DistToSupplyAtr < 1.0)
                {
                    var proximity = snap.DistToSupplyAtr;
                    var tiltFactor = Math.Max(0.25, 1.0 - (1.0 - 0.25) * (1.0 - proximity));
                    var adjustedConfidence = decision.Confidence * tiltFactor * sizeTiltFactor;
                    
                    var amended = new TradingDecision
                    {
                        Action = decision.Action,
                        Confidence = adjustedConfidence,
                        Reason = decision.Reason + $" (zone-tilted: {tiltFactor:F2})",
                        Symbol = decision.Symbol,
                        StrategyId = decision.StrategyId,
                        Timestamp = decision.Timestamp,
                        Metadata = decision.Metadata
                    };
                    
                    if (amended.Metadata != null)
                    {
                        amended.Metadata["zone_tilt_applied"] = true;
                        amended.Metadata["zone_tilt_factor"] = tiltFactor;
                        amended.Metadata["original_confidence"] = decision.Confidence;
                    }
                    
                    return (false, string.Empty, amended);
                }
            }
            else if (decision.Action == TradingAction.Sell)
            {
                // Check if we're too close to demand without breakout potential
                if (snap.DistToDemandAtr <= blockAtr && snap.BreakoutScore < allowBreak)
                {
                    _logger.LogInformation("[ZONE-GATE] {Symbol}: Blocked SHORT entry - near demand zone (dist={DistAtr:F2}, breakout={Score:F2})",
                        symbol, snap.DistToDemandAtr, snap.BreakoutScore);
                    return (true, $"Blocked by demand zone (dist={snap.DistToDemandAtr:F2} ATR, breakout={snap.BreakoutScore:F2})", decision);
                }

                // Apply size tilt if near demand
                if (snap.DistToDemandAtr < 1.0)
                {
                    var proximity = snap.DistToDemandAtr;
                    var tiltFactor = Math.Max(0.25, 1.0 - (1.0 - 0.25) * (1.0 - proximity));
                    var adjustedConfidence = decision.Confidence * tiltFactor * sizeTiltFactor;
                    
                    var amended = new TradingDecision
                    {
                        Action = decision.Action,
                        Confidence = adjustedConfidence,
                        Reason = decision.Reason + $" (zone-tilted: {tiltFactor:F2})",
                        Symbol = decision.Symbol,
                        StrategyId = decision.StrategyId,
                        Timestamp = decision.Timestamp,
                        Metadata = decision.Metadata
                    };
                    
                    if (amended.Metadata != null)
                    {
                        amended.Metadata["zone_tilt_applied"] = true;
                        amended.Metadata["zone_tilt_factor"] = tiltFactor;
                        amended.Metadata["original_confidence"] = decision.Confidence;
                    }
                    
                    return (false, string.Empty, amended);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE-GATE] Error evaluating zone gate for {Symbol}", symbol);
            // On error, allow trade to proceed without zone gate
        }

        return (false, string.Empty, decision);
    }

    /// <summary>
    /// Get neutral band statistics
    /// </summary>
    public NeutralBandStats GetNeutralBandStats()
    {
        return new NeutralBandStats
        {
            BearishThreshold = _config.BearishThreshold,
            BullishThreshold = _config.BullishThreshold,
            NeutralBandWidth = _config.BullishThreshold - _config.BearishThreshold,
            EnableHysteresis = _config.EnableHysteresis,
            HysteresisBuffer = _config.HysteresisBuffer
        };
    }

    /// <summary>
    /// Load neutral band configuration from appsettings
    /// </summary>
    private NeutralBandConfiguration LoadNeutralBandConfiguration()
    {
        var section = _configuration.GetSection("NeutralBand");
        
        return new NeutralBandConfiguration
        {
            BearishThreshold = section.GetValue<double>("BearishThreshold", 0.45), // 45%
            BullishThreshold = section.GetValue<double>("BullishThreshold", 0.55), // 55%
            EnableHysteresis = section.GetValue<bool>("EnableHysteresis", true),
            HysteresisBuffer = section.GetValue<double>("HysteresisBuffer", 0.02) // 2% buffer
        };
    }
}

/// <summary>
/// Neutral band configuration
/// </summary>
public class NeutralBandConfiguration
{
    public double BearishThreshold { get; set; } = 0.45; // 45%
    public double BullishThreshold { get; set; } = 0.55; // 55%
    public bool EnableHysteresis { get; set; } = true;
    public double HysteresisBuffer { get; set; } = 0.02; // 2%
}

/// <summary>
/// Trading decision result
/// </summary>
public class TradingDecision
{
    public TradingAction Action { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string StrategyId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public System.Collections.Generic.Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Neutral band statistics
/// </summary>
public class NeutralBandStats
{
    public double BearishThreshold { get; set; }
    public double BullishThreshold { get; set; }
    public double NeutralBandWidth { get; set; }
    public bool EnableHysteresis { get; set; }
    public double HysteresisBuffer { get; set; }
}