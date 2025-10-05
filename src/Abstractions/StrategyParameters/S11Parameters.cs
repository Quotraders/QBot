using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TradingBot.Abstractions.StrategyParameters;

/// <summary>
/// Session-aware parameters for S11 (ADR / IB Exhaustion Fade) strategy.
/// Supports per-session parameter overrides for Overnight, RTH, and PostRTH sessions.
/// Extracted from S11Config in S11_MaxPerf_FullStack.cs
/// </summary>
public sealed class S11Parameters
{
    // Path to parameter file
    private const string ParameterFilePath = "artifacts/current/parameters/S11_parameters.json";
    
    // Validation ranges
    private const double MinMaxADXValidation = 15.0;
    private const double MaxMaxADXValidation = 30.0;
    private const double MinRVOLValidation = 0.5;
    private const double MaxRVOLValidation = 5.0;
    private const double MinStopAtrMult = 0.5;
    private const double MaxStopAtrMult = 3.0;
    private const double MinTargetAdrFrac = 0.05;
    private const double MaxTargetAdrFrac = 0.3;
    
    // Default values matching current S11Config
    public TimeSpan WindowStart { get; set; } = TimeSpan.Parse("13:30", CultureInfo.InvariantCulture);
    public TimeSpan WindowEnd { get; set; } = TimeSpan.Parse("15:30", CultureInfo.InvariantCulture);
    public TimeSpan IBStart { get; set; } = TimeSpan.Parse("09:30", CultureInfo.InvariantCulture);
    public TimeSpan IBEnd { get; set; } = TimeSpan.Parse("10:30", CultureInfo.InvariantCulture);
    
    // Risk parameters
    public int BaseQty { get; set; } = 1;
    public double MultiplierAfternoon { get; set; } = 0.8;
    public int MaxSpreadTicks { get; set; } = 2;
    public int StopTicksMin { get; set; } = 8;
    public double StopAtrMult { get; set; } = 1.2;
    public double TargetAdrFrac { get; set; } = 0.12;
    public int MaxHoldMinutes { get; set; } = 90;
    
    // Filters
    public double MaxADX { get; set; } = 20.0;
    public double MinRVOL { get; set; } = 1.5;
    public double MinRSI { get; set; } = 25.0;
    public double MaxRSI { get; set; } = 75.0;
    public double MinDomImbalance { get; set; } = 0.25;
    
    // Exhaustion detection
    public double ExhaustionVolMult { get; set; } = 2.0;
    public int ExhaustionBars { get; set; } = 3;
    public double RejectionWickRatio { get; set; } = 0.6;
    
    // ADR tracking
    public int AdrLookbackDays { get; set; } = 20;
    public double AdrExhaustionThreshold { get; set; } = 0.75;
    
    // News avoidance
    public bool EnableNewsGate { get; set; }
    
    /// <summary>
    /// Session-specific parameter overrides. Key is session name (Overnight, RTH, PostRTH).
    /// </summary>
    public Dictionary<string, S11Parameters> SessionOverrides { get; set; } = new();
    
    /// <summary>
    /// Timestamp of last parameter load
    /// </summary>
    private static DateTime _lastLoadTime = DateTime.MinValue;
    
    /// <summary>
    /// Cached parameters
    /// </summary>
    private static S11Parameters? _cachedParameters;
    
    /// <summary>
    /// Load optimal parameters from JSON file with hourly reload check
    /// </summary>
    public static S11Parameters LoadOptimal()
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
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                
                var parameters = JsonSerializer.Deserialize<S11Parameters>(json, options);
                if (parameters != null && parameters.Validate())
                {
                    _cachedParameters = parameters;
                    _lastLoadTime = now;
                    return parameters;
                }
            }
        }
        catch (Exception)
        {
            // Fail silently, return defaults
        }
        
        // Return default parameters if load fails
        var defaultParams = new S11Parameters();
        _cachedParameters = defaultParams;
        _lastLoadTime = now;
        return defaultParams;
    }
    
    /// <summary>
    /// Load parameters for specific session with fallback to defaults
    /// </summary>
    public S11Parameters LoadOptimalForSession(string sessionName)
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
        if (MaxADX < MinMaxADXValidation || MaxADX > MaxMaxADXValidation)
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
        
        // Validate RSI range
        if (MinRSI >= MaxRSI)
        {
            return false;
        }
        
        return true;
    }
}
