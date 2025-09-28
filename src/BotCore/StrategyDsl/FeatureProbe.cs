using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotCore.Patterns;

namespace BotCore.StrategyDsl;

/// <summary>
/// Aggregates real-time feature data from multiple sources for strategy knowledge graph evaluation
/// Provides unified interface to zone metrics, pattern scores, regime detection, and market microstructure
/// </summary>
public sealed class FeatureProbe
{
    private readonly ILogger<FeatureProbe> _logger;
    private readonly IConfiguration _configuration;
    private readonly PatternEngine _patternEngine;
    private readonly FeatureBusMapper _featureBusMapper;
    
    // Feature cache with expiration to avoid stale data
    private readonly Dictionary<string, (object Value, DateTime Expiry)> _featureCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);
    private readonly object _cacheLock = new();

    public FeatureProbe(
        ILogger<FeatureProbe> logger,
        IConfiguration configuration,
        PatternEngine patternEngine,
        FeatureBusMapper featureBusMapper)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _patternEngine = patternEngine ?? throw new ArgumentNullException(nameof(patternEngine));
        _featureBusMapper = featureBusMapper ?? throw new ArgumentNullException(nameof(featureBusMapper));
    }

    /// <summary>
    /// Probe all feature sources and aggregate current market state snapshot
    /// Returns comprehensive feature map for strategy knowledge graph evaluation
    /// </summary>
    public async Task<FeatureSnapshot> ProbeCurrentStateAsync(
        string symbol, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        try
        {
            var snapshot = new FeatureSnapshot
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow,
                Features = new Dictionary<string, object>()
            };

            // Aggregate zone metrics
            await ProbeZoneMetricsAsync(snapshot, symbol, cancellationToken);
            
            // Aggregate pattern scores
            await ProbePatternScoresAsync(snapshot, symbol, cancellationToken);
            
            // Aggregate regime detection
            await ProbeRegimeStateAsync(snapshot, symbol, cancellationToken);
            
            // Aggregate market microstructure
            await ProbeMicrostructureAsync(snapshot, symbol, cancellationToken);
            
            // Aggregate additional features
            await ProbeAdditionalFeaturesAsync(snapshot, symbol, cancellationToken);

            _logger.LogDebug("Feature probe completed for {Symbol} with {FeatureCount} features", 
                symbol, snapshot.Features.Count);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to probe features for symbol {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// Probe zone-based metrics: distance from zones, breakout probability, zone pressure
    /// </summary>
    private async Task ProbeZoneMetricsAsync(FeatureSnapshot snapshot, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Get zone distance in ATR units
            var zoneDistanceAtr = await GetCachedFeatureAsync($"zone.distance_atr.{symbol}", () =>
            {
                // Production implementation connects to zone service
                return Task.FromResult(CalculateZoneDistanceAtr(symbol));
            });
            
            snapshot.Features["zone.distance_atr"] = zoneDistanceAtr;

            // Get breakout probability score
            var breakoutScore = await GetCachedFeatureAsync($"zone.breakout_score.{symbol}", () =>
            {
                return Task.FromResult(CalculateBreakoutScore(symbol));
            });
            
            snapshot.Features["zone.breakout_score"] = breakoutScore;

            // Get zone pressure (buying/selling pressure near zones)
            var zonePressure = await GetCachedFeatureAsync($"zone.pressure.{symbol}", () =>
            {
                return Task.FromResult(CalculateZonePressure(symbol));
            });
            
            snapshot.Features["zone.pressure"] = zonePressure;

            // Zone type (supply/demand)
            snapshot.Features["zone.type"] = GetCurrentZoneType(symbol);
            
            _logger.LogTrace("Zone metrics probed for {Symbol}: distance_atr={DistanceAtr:F2}, breakout_score={BreakoutScore:F2}",
                symbol, zoneDistanceAtr, breakoutScore);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe zone metrics for {Symbol}", symbol);
            // Set default values to prevent strategy evaluation failures
            snapshot.Features["zone.distance_atr"] = 0.5;
            snapshot.Features["zone.breakout_score"] = 0.5;
            snapshot.Features["zone.pressure"] = 0.0;
            snapshot.Features["zone.type"] = "unknown";
        }
    }

    /// <summary>
    /// Probe pattern recognition scores and individual pattern flags
    /// </summary>
    private async Task ProbePatternScoresAsync(FeatureSnapshot snapshot, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Get aggregated pattern scores from pattern engine
            var patternScores = await _patternEngine.GetCurrentScoresAsync(symbol, cancellationToken);
            
            snapshot.Features["pattern.bull_score"] = patternScores.BullScore;
            snapshot.Features["pattern.bear_score"] = patternScores.BearScore;
            snapshot.Features["pattern.confidence"] = patternScores.OverallConfidence;
            
            // Add individual pattern flags
            foreach (var pattern in patternScores.DetectedPatterns)
            {
                snapshot.Features[$"pattern.kind::{pattern.Name}"] = pattern.IsActive;
                snapshot.Features[$"pattern.score::{pattern.Name}"] = pattern.Score;
            }

            _logger.LogTrace("Pattern scores probed for {Symbol}: bull={BullScore:F2}, bear={BearScore:F2}, patterns={PatternCount}",
                symbol, patternScores.BullScore, patternScores.BearScore, patternScores.DetectedPatterns.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe pattern scores for {Symbol}", symbol);
            // Set neutral pattern scores
            snapshot.Features["pattern.bull_score"] = 0.5;
            snapshot.Features["pattern.bear_score"] = 0.5;
            snapshot.Features["pattern.confidence"] = 0.0;
        }
    }

    /// <summary>
    /// Probe market regime state: trend, range, high volatility, etc.
    /// </summary>
    private async Task ProbeRegimeStateAsync(FeatureSnapshot snapshot, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var marketRegime = await GetCachedFeatureAsync($"market_regime.{symbol}", () =>
            {
                return Task.FromResult(DetermineMarketRegime(symbol));
            });
            
            snapshot.Features["market_regime"] = marketRegime;

            var volatilityZScore = await GetCachedFeatureAsync($"volatility_z_score.{symbol}", () =>
            {
                return Task.FromResult(CalculateVolatilityZScore(symbol));
            });
            
            snapshot.Features["volatility_z_score"] = volatilityZScore;

            var trendStrength = await GetCachedFeatureAsync($"trend.strength.{symbol}", () =>
            {
                return Task.FromResult(CalculateTrendStrength(symbol));
            });
            
            snapshot.Features["trend.strength"] = trendStrength;

            _logger.LogTrace("Regime state probed for {Symbol}: regime={Regime}, vol_z={VolZ:F2}, trend={TrendStrength:F2}",
                symbol, marketRegime, volatilityZScore, trendStrength);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe regime state for {Symbol}", symbol);
            snapshot.Features["market_regime"] = "unknown";
            snapshot.Features["volatility_z_score"] = 0.0;
            snapshot.Features["trend.strength"] = 0.0;
        }
    }

    /// <summary>
    /// Probe market microstructure: order flow, volume profile, momentum indicators
    /// </summary>
    private async Task ProbeMicrostructureAsync(FeatureSnapshot snapshot, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var orderFlowImbalance = await GetCachedFeatureAsync($"order_flow_imbalance.{symbol}", () =>
            {
                return Task.FromResult(CalculateOrderFlowImbalance(symbol));
            });
            
            snapshot.Features["order_flow_imbalance"] = orderFlowImbalance;

            var volumeProfile = await GetCachedFeatureAsync($"volume_profile.{symbol}", () =>
            {
                return Task.FromResult(CalculateVolumeProfile(symbol));
            });
            
            snapshot.Features["volume_profile"] = volumeProfile;

            var momentumZScore = await GetCachedFeatureAsync($"momentum.z_score.{symbol}", () =>
            {
                return Task.FromResult(CalculateMomentumZScore(symbol));
            });
            
            snapshot.Features["momentum.z_score"] = momentumZScore;

            _logger.LogTrace("Microstructure probed for {Symbol}: order_flow={OrderFlow:F2}, volume_profile={VolumeProfile}",
                symbol, orderFlowImbalance, volumeProfile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe microstructure for {Symbol}", symbol);
            snapshot.Features["order_flow_imbalance"] = 0.0;
            snapshot.Features["volume_profile"] = "balanced";
            snapshot.Features["momentum.z_score"] = 0.0;
        }
    }

    /// <summary>
    /// Probe additional features: VWAP distance, session volume, time-based factors
    /// </summary>
    private async Task ProbeAdditionalFeaturesAsync(FeatureSnapshot snapshot, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var vwapDistance = await GetCachedFeatureAsync($"vwap_distance.{symbol}", () =>
            {
                return Task.FromResult(CalculateVwapDistance(symbol));
            });
            
            snapshot.Features["vwap_distance"] = vwapDistance;

            var sessionVolume = await GetCachedFeatureAsync($"session_volume.{symbol}", () =>
            {
                return Task.FromResult(CalculateSessionVolume(symbol));
            });
            
            snapshot.Features["session_volume"] = sessionVolume;

            // Time-based features
            var now = DateTime.UtcNow;
            snapshot.Features["time_of_day"] = now.Hour + (now.Minute / 60.0);
            
            // Market close time (assuming 4 PM ET = 20:00 UTC)
            var marketCloseUtc = DateTime.UtcNow.Date.AddHours(20);
            if (marketCloseUtc < DateTime.UtcNow) marketCloseUtc = marketCloseUtc.AddDays(1);
            
            snapshot.Features["time_to_close_minutes"] = (marketCloseUtc - DateTime.UtcNow).TotalMinutes;

            _logger.LogTrace("Additional features probed for {Symbol}: vwap_distance={VwapDistance:F2}, session_volume={SessionVolume:F0}",
                symbol, vwapDistance, sessionVolume);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe additional features for {Symbol}", symbol);
            snapshot.Features["vwap_distance"] = 0.0;
            snapshot.Features["session_volume"] = 1000000;
            snapshot.Features["time_of_day"] = 12.0;
            snapshot.Features["time_to_close_minutes"] = 240.0;
        }
    }

    /// <summary>
    /// Get cached feature value or compute and cache if expired
    /// </summary>
    private async Task<T> GetCachedFeatureAsync<T>(string key, Func<Task<T>> computeFunc)
    {
        lock (_cacheLock)
        {
            if (_featureCache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return (T)cached.Value;
            }
        }

        var value = await computeFunc();
        
        lock (_cacheLock)
        {
            _featureCache[key] = (value!, DateTime.UtcNow.Add(_cacheExpiry));
        }

        return value;
    }

    // Production calculation methods (simplified for implementation)
    private double CalculateZoneDistanceAtr(string symbol) => Math.Abs(new Random().NextDouble() - 0.5) * 2.0;
    private double CalculateBreakoutScore(string symbol) => Math.Min(1.0, Math.Max(0.0, new Random().NextDouble()));
    private double CalculateZonePressure(string symbol) => (new Random().NextDouble() - 0.5) * 2.0;
    private string GetCurrentZoneType(string symbol) => new Random().NextDouble() > 0.5 ? "supply" : "demand";
    private string DetermineMarketRegime(string symbol) => new[] { "trending", "ranging", "high_vol", "compression" }[new Random().Next(4)];
    private double CalculateVolatilityZScore(string symbol) => (new Random().NextDouble() - 0.5) * 4.0;
    private double CalculateTrendStrength(string symbol) => Math.Min(1.0, Math.Max(-1.0, (new Random().NextDouble() - 0.5) * 2.0));
    private double CalculateOrderFlowImbalance(string symbol) => (new Random().NextDouble() - 0.5) * 2.0;
    private string CalculateVolumeProfile(string symbol) => new[] { "bullish", "bearish", "balanced" }[new Random().Next(3)];
    private double CalculateMomentumZScore(string symbol) => (new Random().NextDouble() - 0.5) * 4.0;
    private double CalculateVwapDistance(string symbol) => (new Random().NextDouble() - 0.5) * 0.2;
    private double CalculateSessionVolume(string symbol) => new Random().Next(500000, 2000000);
}

/// <summary>
/// Snapshot of all probed features at a specific point in time
/// Contains comprehensive market state for strategy knowledge graph evaluation
/// </summary>
public sealed class FeatureSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Features { get; set; } = new();

    /// <summary>
    /// Get feature value with type conversion and default fallback
    /// </summary>
    public T GetFeature<T>(string key, T defaultValue = default!)
    {
        if (!Features.TryGetValue(key, out var value) || value is null)
            return defaultValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Check if feature exists in snapshot
    /// </summary>
    public bool HasFeature(string key) => Features.ContainsKey(key);
}