using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TradingBot.Abstractions.StrategyParameters;

/// <summary>
/// Session-aware parameters for S3 (Compression Breakout) strategy.
/// Supports per-session parameter overrides for Overnight, RTH, and PostRTH sessions.
/// </summary>
public sealed class S3Parameters
{
    // Path to parameter file
    private const string ParameterFilePath = "artifacts/current/parameters/S3_parameters.json";
    
    // Validation ranges
    private const decimal MinWidthRank = 0.05m;
    private const decimal MaxWidthRank = 0.4m;
    private const int MinSqueezeBarsValidation = 3;
    private const int MaxSqueezeBarsValidation = 15;
    private const decimal MinStopAtrMult = 0.5m;
    private const decimal MaxStopAtrMult = 3.0m;
    private const decimal MinTargetR = 1.0m;
    private const decimal MaxTargetR = 4.0m;
    
    // JSON serialization options (cached to avoid CA1869)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    
    // Default values matching current S3RuntimeConfig
    public int BbLen { get; set; } = 20;
    public decimal BbMult { get; set; } = 2.0m;
    public int KcEma { get; set; } = 20;
    public int KcAtrLen { get; set; } = 20;
    public decimal KcMult { get; set; } = 1.5m;
    public int AtrLen { get; set; } = 14;
    public int MinSqueezeBars { get; set; } = 6;
    public int PreSqueezeLookback { get; set; } = 60;
    public decimal WidthRankEnter { get; set; } = 0.15m;
    public bool HourlyRankAdapt { get; set; } = true;
    public decimal NrClusterRatio { get; set; } = 0.60m;
    public int NrClusterMinBars { get; set; } = 5;
    public int WidthSlopeDownBars { get; set; } = 8;
    public decimal WidthSlopeTol { get; set; } = 0.0m;
    public decimal ConfirmBreakAtrMult { get; set; } = 0.15m;
    public decimal ContraBufferMult { get; set; } = 1.5m;
    public decimal OvernightBufferAdd { get; set; } = 0.05m;
    public decimal StopAtrMult { get; set; } = 1.1m;
    public decimal TargetR1 { get; set; } = 1.2m;
    public decimal TargetR2 { get; set; } = 2.2m;
    public string EntryMode { get; set; } = "retest";
    public int RetestBars { get; set; } = 5;
    public int RetestBackoffTicks { get; set; } = 1;
    public bool OrGuardEnabled { get; set; } = true;
    public int OrMinutes { get; set; } = 10;
    public string OrAvoidBreakInto { get; set; } = "opposite";
    public bool IbGuardEnabled { get; set; } = true;
    public int IbEndMinute { get; set; } = 630;
    public string IbAvoidBreakInto { get; set; } = "opposite";
    public bool RsEnabled { get; set; } = true;
    public int RsWindowBars { get; set; } = 60;
    public decimal RsThreshold { get; set; } = 0.10m;
    public bool RsDirectionalOnly { get; set; } = true;
    public bool RollEnabled { get; set; } = true;
    public int RollDaysBefore { get; set; } = 2;
    public int RollDaysAfter { get; set; } = 1;
    public decimal RollRankTighten { get; set; } = 0.02m;
    public decimal BreakQMinClosePos { get; set; } = 0.65m;
    public decimal BreakQMaxOppWick { get; set; } = 0.35m;
    public int ValidityBars { get; set; } = 3;
    public int CooldownBars { get; set; } = 5;
    public int MaxBarsInTrade { get; set; } = 45;
    public decimal TrailAtrMult { get; set; } = 1.0m;
    public int MinVolume { get; set; } = 3000;
    public int MaxSpreadTicks { get; set; } = 2;
    public int NewsBlockBeforeMin { get; set; } = 2;
    public int NewsBlockAfterMin { get; set; } = 3;
    public int AttemptCapRTH { get; set; } = 2;
    public int AttemptCapOvernight { get; set; } = 1;
    public bool OnePerSegment { get; set; } = true;
    public int SegmentCooldownMinutes { get; set; } = 15;
    public int EarlyInvalidateBars { get; set; } = 6;
    public decimal ImpulseScoreMin { get; set; } = 1.0m;
    public string TargetsMode { get; set; } = "expansion";
    public decimal ExpansionQuantile { get; set; } = 0.60m;
    public decimal GivebackAfterT1R { get; set; } = 0.20m;
    private const decimal DefaultVolZMin = -0.5m;
    public decimal MinSlopeTf2 { get; set; } = 0.15m;
    public decimal VolZMin { get; set; } = DefaultVolZMin;
    public decimal VolZMax { get; set; } = 2.5m;
    
    /// <summary>
    /// Session-specific parameter overrides. Key is session name (Overnight, RTH, PostRTH).
    /// </summary>
    public Dictionary<string, S3Parameters> SessionOverrides { get; init; } = new();
    
    /// <summary>
    /// Timestamp of last parameter load
    /// </summary>
    private static DateTime _lastLoadTime = DateTime.MinValue;
    
    /// <summary>
    /// Cached parameters
    /// </summary>
    private static S3Parameters? _cachedParameters;
    
    /// <summary>
    /// Load optimal parameters from JSON file with hourly reload check
    /// </summary>
    public static S3Parameters LoadOptimal()
    {
        // Check if we need to reload (hourly)
        var now = DateTime.UtcNow;
        if (_cachedParameters != null && (now - _lastLoadTime).TotalHours < 1.0)
        {
            return _cachedParameters;
        }
        
        try
        {
            if (File.Exists(ParameterFilePath))
            {
                var json = File.ReadAllText(ParameterFilePath);
                var parameters = JsonSerializer.Deserialize<S3Parameters>(json, JsonOptions);
                if (parameters != null && parameters.Validate())
                {
                    _cachedParameters = parameters;
                    _lastLoadTime = now;
                    return parameters;
                }
            }
        }
        catch (JsonException)
        {
            // JSON parsing error - return defaults
        }
        catch (IOException)
        {
            // File access error - return defaults
        }
        
        // Return default parameters if load fails
        var defaultParams = new S3Parameters();
        _cachedParameters = defaultParams;
        _lastLoadTime = now;
        return defaultParams;
    }
    
    /// <summary>
    /// Load parameters for specific session with fallback to defaults
    /// </summary>
    public S3Parameters LoadOptimalForSession(string sessionName)
    {
        if (string.IsNullOrEmpty(sessionName))
        {
            return this;
        }
        
        // Check if we have session-specific overrides
        if (SessionOverrides.TryGetValue(sessionName, out var sessionParams))
        {
            return sessionParams;
        }
        
        // Fall back to base parameters
        return this;
    }
    
    /// <summary>
    /// Validate parameter ranges
    /// </summary>
    public bool Validate()
    {
        return ValidateWidthRank() && ValidateSqueezeBars() && ValidateStopAndTargets();
    }
    
    private bool ValidateWidthRank()
    {
        return WidthRankEnter >= MinWidthRank && WidthRankEnter <= MaxWidthRank;
    }
    
    private bool ValidateSqueezeBars()
    {
        return MinSqueezeBars >= MinSqueezeBarsValidation && MinSqueezeBars <= MaxSqueezeBarsValidation;
    }
    
    private bool ValidateStopAndTargets()
    {
        // Check stop is in range
        if (StopAtrMult < MinStopAtrMult || StopAtrMult > MaxStopAtrMult)
        {
            return false;
        }
        
        // Check targets are in range
        if (TargetR1 < MinTargetR || TargetR1 > MaxTargetR || TargetR2 < MinTargetR || TargetR2 > MaxTargetR)
        {
            return false;
        }
        
        // Target R2 should be greater than R1
        return TargetR2 > TargetR1;
    }
}
