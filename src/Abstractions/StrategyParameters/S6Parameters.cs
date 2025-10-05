using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TradingBot.Abstractions.StrategyParameters;

/// <summary>
/// Session-aware parameters for S6 (Opening Drive / Reversal) strategy.
/// Supports per-session parameter overrides for Overnight, RTH, and PostRTH sessions.
/// Extracted from S6Config in S6_MaxPerf_FullStack.cs
/// </summary>
public sealed class S6Parameters
{
    // Path to parameter file
    private const string ParameterFilePath = "artifacts/current/parameters/S6_parameters.json";
    
    // Validation ranges
    private const double MinADXValidation = 10.0;
    private const double MaxADXValidation = 40.0;
    private const double MinRVOLValidation = 0.5;
    private const double MaxRVOLValidation = 5.0;
    private const double MinStopAtrMult = 0.5;
    private const double MaxStopAtrMult = 3.0;
    private const double MinTargetAdrFrac = 0.05;
    private const double MaxTargetAdrFrac = 0.5;
    
    // JSON serialization options (cached to avoid CA1869)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    
    // Default values matching current S6Config
    public TimeSpan WindowStart { get; set; } = TimeSpan.Parse("09:28", CultureInfo.InvariantCulture);
    public TimeSpan RTHOpen { get; set; } = TimeSpan.Parse("09:30", CultureInfo.InvariantCulture);
    public TimeSpan WindowEnd { get; set; } = TimeSpan.Parse("10:00", CultureInfo.InvariantCulture);
    public TimeSpan ONWindowStart { get; set; } = TimeSpan.Parse("18:00", CultureInfo.InvariantCulture);
    public TimeSpan ONWindowEnd { get; set; } = TimeSpan.Parse("09:28", CultureInfo.InvariantCulture);
    
    // Risk parameters
    public int BaseQty { get; set; } = 1;
    public int MaxSpreadTicks { get; set; } = 2;
    public int StopTicksMin { get; set; } = 8;
    public double StopAtrMult { get; set; } = 1.5;
    public double TargetAdrFrac { get; set; } = 0.18;
    public int MaxHoldMinutes { get; set; } = 45;
    public double FlipMinR { get; set; } = 1.25;
    
    // Filters
    public double MinADX { get; set; } = 18.0;
    public double MinRVOL { get; set; } = 1.2;
    public int DivMaxBp { get; set; } = 12;
    public double MinDomImbalance { get; set; } = 0.18;
    
    // Retests
    public bool RetestEnable { get; set; } = true;
    public int RetestGraceTicks { get; set; } = 2;
    public int RetestConfirmBars1m { get; set; } = 2;
    
    // History
    public int AdrLookbackDays { get; set; } = 14;
    public int RvolLookbackDays { get; set; } = 20;
    
    // Failed breakout
    public int FailBreakPenetrationTicksES { get; set; } = 3;
    public int FailBreakPenetrationTicksNQ { get; set; } = 4;
    
    /// <summary>
    /// Session-specific parameter overrides. Key is session name (Overnight, RTH, PostRTH).
    /// </summary>
    public Dictionary<string, S6Parameters> SessionOverrides { get; init; } = new();
    
    /// <summary>
    /// Timestamp of last parameter load
    /// </summary>
    private static DateTime _lastLoadTime = DateTime.MinValue;
    
    /// <summary>
    /// Cached parameters
    /// </summary>
    private static S6Parameters? _cachedParameters;
    
    /// <summary>
    /// Load optimal parameters from JSON file with hourly reload check
    /// </summary>
    public static S6Parameters LoadOptimal()
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
                var parameters = JsonSerializer.Deserialize<S6Parameters>(json, JsonOptions);
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
        var defaultParams = new S6Parameters();
        _cachedParameters = defaultParams;
        _lastLoadTime = now;
        return defaultParams;
    }
    
    /// <summary>
    /// Load parameters for specific session with fallback to defaults
    /// </summary>
    public S6Parameters LoadOptimalForSession(string sessionName)
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
        // Validate ADX range
        if (MinADX < MinADXValidation || MinADX > MaxADXValidation)
        {
            return false;
        }
        
        // Validate RVOL range
        if (MinRVOL < MinRVOLValidation || MinRVOL > MaxRVOLValidation)
        {
            return false;
        }
        
        // Validate stop ATR multiplier
        if (StopAtrMult < MinStopAtrMult || StopAtrMult > MaxStopAtrMult)
        {
            return false;
        }
        
        // Validate target ADR fraction
        if (TargetAdrFrac < MinTargetAdrFrac || TargetAdrFrac > MaxTargetAdrFrac)
        {
            return false;
        }
        
        return true;
    }
}
