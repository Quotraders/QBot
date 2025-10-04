using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Zone feature resolver - PRODUCTION ONLY - connects to real zone service
/// </summary>
public sealed class ZoneFeatureResolver : IFeatureResolver
{
    // Zone proximity threshold constants (measured in ATR units)
    private const double CloseProximityThreshold = 2.0;     // Close proximity: within 2 ATR
    private const double MediumProximityThreshold = 3.0;    // Medium proximity: within 3 ATR
    private const double FullZoneWeight = 1.0;              // Full weight for very close zones
    private const double MediumZoneWeight = 0.5;            // Medium weight for close zones
    private const double LowZoneWeight = 0.25;              // Low weight for medium distance zones

    private readonly IServiceProvider _serviceProvider;
    private readonly string _featureName;
    private readonly ILogger<ZoneFeatureResolver> _logger;
    
    public ZoneFeatureResolver(IServiceProvider serviceProvider, string featureName)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _featureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        _logger = serviceProvider.GetRequiredService<ILogger<ZoneFeatureResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            var value = _featureName switch
            {
                "dist_to_demand_atr" => (double)features.distToDemandAtr,
                "dist_to_supply_atr" => (double)features.distToSupplyAtr,
                "breakout_score" => (double)features.breakoutScore,
                "pressure" => (double)features.zonePressure,
                "test_count" => CalculateZoneTestCount(((double)features.distToDemandAtr, (double)features.distToSupplyAtr, (double)features.breakoutScore, (double)features.zonePressure)),
                "dist_to_opposing_atr" => (double)Math.Max(features.distToDemandAtr, features.distToSupplyAtr),
                _ => throw new InvalidOperationException($"Unknown zone feature: {_featureName}")
            };
            
            _logger.LogTrace("Zone feature {Feature} for {Symbol}: {Value}", _featureName, symbol, value);
            return Task.FromResult<double?>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve zone feature {Feature} for symbol {Symbol}", _featureName, symbol);
            throw new InvalidOperationException($"Production zone feature resolution failed for '{_featureName}' on '{symbol}': {ex.Message}", ex);
        }
    }
    
    private static double CalculateZoneTestCount((double distToDemandAtr, double distToSupplyAtr, double breakoutScore, double zonePressure) features)
    {
        // Production calculation based on zone pressure and breakout score
        // Higher pressure + higher breakout score = more zone tests
        var baseTestCount = Math.Max(1.0, features.zonePressure * 2.0);
        var breakoutAdjustment = features.breakoutScore * 0.5;
        return Math.Round(baseTestCount + breakoutAdjustment, 1);
    }
}

/// <summary>
/// Zone count resolver - counts active zones based on proximity thresholds
/// </summary>
public sealed class ZoneCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ZoneCountResolver> _logger;
    
    public ZoneCountResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ZoneCountResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            // Count zones based on proximity - closer zones are more significant
            var demandProximity = (double)features.distToDemandAtr;
            var supplyProximity = (double)features.distToSupplyAtr;
            
            var activeZoneCount = 0.0;
            
            // Demand zone contribution (closer = higher weight)
            if (demandProximity <= FullZoneWeight) activeZoneCount += FullZoneWeight;
            else if (demandProximity <= CloseProximityThreshold) activeZoneCount += MediumZoneWeight;
            else if (demandProximity <= MediumProximityThreshold) activeZoneCount += LowZoneWeight;
            
            // Supply zone contribution (closer = higher weight)  
            if (supplyProximity <= FullZoneWeight) activeZoneCount += FullZoneWeight;
            else if (supplyProximity <= CloseProximityThreshold) activeZoneCount += MediumZoneWeight;
            else if (supplyProximity <= MediumProximityThreshold) activeZoneCount += LowZoneWeight;
            
            // Pressure multiplier - higher pressure indicates more significant zones
            activeZoneCount *= Math.Max(FullZoneWeight, (double)features.zonePressure);
            
            _logger.LogTrace("Zone count for {Symbol}: {Count} (demand: {Demand}ATR, supply: {Supply}ATR)", 
                symbol, activeZoneCount, demandProximity, supplyProximity);
            
            return Task.FromResult<double?>(activeZoneCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count zones for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production zone count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Zone tests resolver - calculates zone test frequency based on breakout scores and pressure
/// </summary>
public sealed class ZoneTestsResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ZoneTestsResolver> _logger;
    
    public ZoneTestsResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ZoneTestsResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            // Calculate zone tests based on breakout score and pressure
            var testFrequency = CalculateZoneTestCount(((double)features.distToDemandAtr, (double)features.distToSupplyAtr, (double)features.breakoutScore, (double)features.zonePressure));
            
            _logger.LogTrace("Zone tests for {Symbol}: {Tests} (breakout: {Breakout}, pressure: {Pressure})", 
                symbol, testFrequency, features.breakoutScore, features.zonePressure);
            
            return Task.FromResult<double?>(testFrequency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate zone tests for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production zone tests resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private static double CalculateZoneTestCount((double distToDemandAtr, double distToSupplyAtr, double breakoutScore, double zonePressure) features)
    {
        // Production calculation based on zone pressure and breakout score
        // Higher pressure + higher breakout score = more zone tests
        var baseTestCount = Math.Max(1.0, features.zonePressure * 2.0);
        var breakoutAdjustment = features.breakoutScore * 0.5;
        return Math.Round(baseTestCount + breakoutAdjustment, 1);
    }
}