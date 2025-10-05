using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TradingBot.Abstractions.StrategyParameters;

/// <summary>
/// Session-aware parameters for S2 (VWAP Mean-Reversion) strategy.
/// Supports per-session parameter overrides for Overnight, RTH, and PostRTH sessions.
/// </summary>
public sealed class S2Parameters
{
    // Path to parameter file
    private const string ParameterFilePath = "artifacts/current/parameters/S2_parameters.json";
    
    // Validation ranges
    private const decimal MinStopAtrMult = 1.0m;
    private const decimal MaxStopAtrMult = 4.0m;
    private const decimal MinTargetAtrMult = 2.0m;
    private const decimal MaxTargetAtrMult = 6.0m;
    private const decimal MinAtrDifference = 0.5m;
    
    // JSON serialization options (cached to avoid CA1869)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    
    // Default values matching current S2RuntimeConfig
    public decimal SigmaEnter { get; set; } = 2.0m;
    public decimal AtrEnter { get; set; } = 1.0m;
    public int AtrLen { get; set; } = 14;
    public decimal SigmaForceTrend { get; set; } = 2.8m;
    public decimal MinSlopeTf2 { get; set; } = 0.18m;
    private const decimal DefaultVolZMin = -0.3m;
    public decimal VolZMin { get; set; } = DefaultVolZMin;
    public decimal VolZMax { get; set; } = 2.2m;
    public int ConfirmLookback { get; set; } = 3;
    public int ValidityBars { get; set; } = 3;
    public int CooldownBars { get; set; } = 5;
    public int MaxBarsInTrade { get; set; } = 45;
    public decimal StopAtrMult { get; set; } = 0.75m;
    public decimal TargetAtrMult { get; set; } = 2.0m;
    public decimal TrailAtrMult { get; set; } = 1.0m;
    public int IbEndMinute { get; set; } = 630;
    public decimal EsSigma { get; set; } = 2.0m;
    public decimal NqSigma { get; set; } = 2.6m;
    public decimal OvernightScale { get; set; } = 0.5m;
    public int MinVolume { get; set; } = 3000;
    public int MaxSpreadTicks { get; set; } = 2;
    public int MaxTradesPerSession { get; set; } = 3;
    public string EntryMode { get; set; } = "retest";
    public int RetestOffsetTicks { get; set; } = 1;
    
    /// <summary>
    /// Session-specific parameter overrides. Key is session name (Overnight, RTH, PostRTH).
    /// </summary>
    public Dictionary<string, S2Parameters> SessionOverrides { get; init; } = new();
    
    /// <summary>
    /// Timestamp of last parameter load
    /// </summary>
    private static DateTime _lastLoadTime = DateTime.MinValue;
    
    /// <summary>
    /// Cached parameters
    /// </summary>
    private static S2Parameters? _cachedParameters;
    
    /// <summary>
    /// Load optimal parameters from JSON file with hourly reload check
    /// </summary>
    public static S2Parameters LoadOptimal()
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
                var parameters = JsonSerializer.Deserialize<S2Parameters>(json, JsonOptions);
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
        var defaultParams = new S2Parameters();
        _cachedParameters = defaultParams;
        _lastLoadTime = now;
        return defaultParams;
    }
    
    /// <summary>
    /// Load parameters for specific session with fallback to defaults
    /// </summary>
    public S2Parameters LoadOptimalForSession(string sessionName)
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
        // Validate stop and target relationship
        if (TargetAtrMult <= StopAtrMult + MinAtrDifference)
        {
            return false;
        }
        
        // Validate ranges
        if (StopAtrMult < MinStopAtrMult || StopAtrMult > MaxStopAtrMult)
        {
            return false;
        }
        
        if (TargetAtrMult < MinTargetAtrMult || TargetAtrMult > MaxTargetAtrMult)
        {
            return false;
        }
        
        return true;
    }
}
