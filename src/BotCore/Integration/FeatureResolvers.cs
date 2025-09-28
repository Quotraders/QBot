using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Zone feature resolver - PRODUCTION ONLY - connects to real zone service
/// </summary>
public sealed class ZoneFeatureResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _featureName;
    private readonly ILogger<ZoneFeatureResolver> _logger;
    
    public ZoneFeatureResolver(IServiceProvider serviceProvider, string featureName)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _featureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        _logger = serviceProvider.GetRequiredService<ILogger<ZoneFeatureResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            var value = _featureName switch
            {
                "dist_to_demand_atr" => features.distToDemandAtr,
                "dist_to_supply_atr" => features.distToSupplyAtr,
                "breakout_score" => features.breakoutScore,
                "pressure" => features.zonePressure,
                "test_count" => CalculateZoneTestCount(features),
                "dist_to_opposing_atr" => Math.Max(features.distToDemandAtr, features.distToSupplyAtr),
                _ => throw new InvalidOperationException($"Unknown zone feature: {_featureName}")
            };
            
            _logger.LogTrace("Zone feature {Feature} for {Symbol}: {Value}", _featureName, symbol, value);
            await Task.CompletedTask.ConfigureAwait(false);
            return value;
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
/// Pattern feature resolver - PRODUCTION ONLY - connects to real pattern engine
/// </summary>
public sealed class PatternFeatureResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _featureName;
    private readonly ILogger<PatternFeatureResolver> _logger;
    
    public PatternFeatureResolver(IServiceProvider serviceProvider, string featureName)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _featureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternFeatureResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            var value = _featureName switch
            {
                "bull_score" => patternScores.BullScore,
                "bear_score" => patternScores.BearScore,
                "confidence" => patternScores.OverallConfidence,
                _ => throw new InvalidOperationException($"Unknown pattern feature: {_featureName}")
            };
            
            _logger.LogTrace("Pattern feature {Feature} for {Symbol}: {Value}", _featureName, symbol, value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern feature {Feature} for symbol {Symbol}", _featureName, symbol);
            throw new InvalidOperationException($"Production pattern feature resolution failed for '{_featureName}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Technical indicator resolver - PRODUCTION ONLY - calculates from real bar data
/// </summary>
public sealed class TechnicalIndicatorResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _indicatorType;
    private readonly int _period;
    private readonly ILogger<TechnicalIndicatorResolver> _logger;
    
    public TechnicalIndicatorResolver(IServiceProvider serviceProvider, string indicatorType, int period = 14)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _indicatorType = indicatorType ?? throw new ArgumentNullException(nameof(indicatorType));
        _period = period;
        _logger = serviceProvider.GetRequiredService<ILogger<TechnicalIndicatorResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Connect to real feature bus for technical indicators
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            
            var featureKey = _indicatorType switch
            {
                "atr" => $"atr.{_period}",
                "volatility" => "volatility.realized",
                "momentum" => "momentum.zscore",
                "vdc" => "volatility.contraction",
                "rsi" => $"rsi.{_period}",
                "ema" => $"ema.{_period}",
                "sma" => $"sma.{_period}",
                _ => throw new InvalidOperationException($"Unknown technical indicator: {_indicatorType}")
            };
            
            await Task.CompletedTask.ConfigureAwait(false);
            var result = featureBus.Probe(symbol, featureKey);
            
            if (!result.HasValue)
            {
                throw new InvalidOperationException($"Technical indicator '{_indicatorType}' not available for symbol '{symbol}' - real data required");
            }
            
            _logger.LogTrace("Technical indicator {Indicator} for {Symbol}: {Value}", _indicatorType, symbol, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve technical indicator {Indicator} for symbol {Symbol}", _indicatorType, symbol);
            throw new InvalidOperationException($"Production technical indicator resolution failed for '{_indicatorType}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Market data resolver - PRODUCTION ONLY - gets real market data
/// </summary>
public sealed class MarketDataResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _dataType;
    private readonly ILogger<MarketDataResolver> _logger;
    
    public MarketDataResolver(IServiceProvider serviceProvider, string dataType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketDataResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            
            var featureKey = _dataType switch
            {
                "price" => "price.current",
                "volume" => "volume.current",
                "spread" => "market.spread",
                "liquidity" => "market.liquidity_score",
                _ => throw new InvalidOperationException($"Unknown market data type: {_dataType}")
            };
            
            await Task.CompletedTask.ConfigureAwait(false);
            var result = featureBus.Probe(symbol, featureKey);
            
            if (!result.HasValue)
            {
                throw new InvalidOperationException($"Market data '{_dataType}' not available for symbol '{symbol}' - real data required");
            }
            
            _logger.LogTrace("Market data {DataType} for {Symbol}: {Value}", _dataType, symbol, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve market data {DataType} for symbol {Symbol}", _dataType, symbol);
            throw new InvalidOperationException($"Production market data resolution failed for '{_dataType}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Position data resolver - PRODUCTION ONLY - gets real position information
/// </summary>
public sealed class PositionDataResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _positionMetric;
    private readonly ILogger<PositionDataResolver> _logger;
    
    public PositionDataResolver(IServiceProvider serviceProvider, string positionMetric)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _positionMetric = positionMetric ?? throw new ArgumentNullException(nameof(positionMetric));
        _logger = serviceProvider.GetRequiredService<ILogger<PositionDataResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var positionTracker = _serviceProvider.GetRequiredService<BotCore.Services.PositionTrackingSystem>();
            var positions = await positionTracker.GetCurrentPositionsAsync().ConfigureAwait(false);
            
            var symbolPositions = positions.Where(p => p.Symbol == symbol).ToList();
            
            var value = _positionMetric switch
            {
                "size" => symbolPositions.Sum(p => (double)p.Size),
                "pnl" => symbolPositions.Sum(p => (double)p.UnrealizedPnL),
                "count" => symbolPositions.Count,
                _ => throw new InvalidOperationException($"Unknown position metric: {_positionMetric}")
            };
            
            _logger.LogTrace("Position metric {Metric} for {Symbol}: {Value}", _positionMetric, symbol, value);
            await Task.CompletedTask.ConfigureAwait(false);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position metric {Metric} for symbol {Symbol}", _positionMetric, symbol);
            throw new InvalidOperationException($"Production position data resolution failed for '{_positionMetric}' on '{symbol}': {ex.Message}", ex);
        }
    }
}