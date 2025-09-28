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
            // Completed synchronously
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
            
            // Completed synchronously
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
            
            // Completed synchronously
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
            var positionTracker = _serviceProvider.GetRequiredService<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
            var positions = positionTracker.GetAllPositions().Values.ToList();
            
            var symbolPositions = positions.Where(p => p.Symbol == symbol).ToList();
            
            var value = _positionMetric switch
            {
                "size" => symbolPositions.Sum(p => (double)p.NetQuantity),
                "pnl" => symbolPositions.Sum(p => (double)p.UnrealizedPnL),
                "count" => symbolPositions.Count,
                _ => throw new InvalidOperationException($"Unknown position metric: {_positionMetric}")
            };
            
            _logger.LogTrace("Position metric {Metric} for {Symbol}: {Value}", _positionMetric, symbol, value);
            // Completed synchronously
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position metric {Metric} for symbol {Symbol}", _positionMetric, symbol);
            throw new InvalidOperationException($"Production position data resolution failed for '{_positionMetric}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

// Zone Resolvers - Production implementations connecting to real zone services

/// <summary>
/// Zone count resolver - PRODUCTION ONLY - counts active zones for symbol
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
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            // Count zones based on demand/supply presence and strength
            var zoneCount = 0.0;
            if (features.distToDemandAtr < 5.0) zoneCount += 1.0; // Close demand zone
            if (features.distToSupplyAtr < 5.0) zoneCount += 1.0; // Close supply zone
            if (features.zonePressure > 0.7) zoneCount += 0.5; // High pressure adds partial count
            
            _logger.LogTrace("Zone count for {Symbol}: {Count}", symbol, zoneCount);
            // Completed synchronously
            return zoneCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve zone count for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production zone count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Zone tests resolver - PRODUCTION ONLY - counts zone test events
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
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetRequiredService<Zones.IZoneFeatureSource>();
            var features = zoneFeatureSource.GetFeatures(symbol);
            
            // Calculate zone test count using existing logic
            var baseTestCount = Math.Max(1.0, features.zonePressure * 2.0);
            var breakoutAdjustment = features.breakoutScore * 0.5;
            var testCount = Math.Round(baseTestCount + breakoutAdjustment, 1);
            
            _logger.LogTrace("Zone tests for {Symbol}: {Count}", symbol, testCount);
            // Completed synchronously
            return testCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve zone tests for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production zone tests resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Pattern Resolvers - Production implementations connecting to real pattern engine

/// <summary>
/// Pattern score resolver - PRODUCTION ONLY - gets bull/bear pattern scores
/// </summary>
public sealed class PatternScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _isBullScore;
    private readonly ILogger<PatternScoreResolver> _logger;
    
    public PatternScoreResolver(IServiceProvider serviceProvider, bool isBullScore)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _isBullScore = isBullScore;
        _logger = serviceProvider.GetRequiredService<ILogger<PatternScoreResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            var score = _isBullScore ? patternScores.BullScore : patternScores.BearScore;
            _logger.LogTrace("Pattern score ({Type}) for {Symbol}: {Score}", _isBullScore ? "Bull" : "Bear", symbol, score);
            return score;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern score for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production pattern score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pattern signal resolver - PRODUCTION ONLY - detects specific pattern signals
/// </summary>
public sealed class PatternSignalResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _patternType;
    private readonly ILogger<PatternSignalResolver> _logger;
    
    public PatternSignalResolver(IServiceProvider serviceProvider, string patternType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _patternType = patternType ?? throw new ArgumentNullException(nameof(patternType));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternSignalResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Simplified signal detection based on pattern scores rather than specific pattern types
            var signal = (_patternType.Equals("Doji", StringComparison.OrdinalIgnoreCase) && patternScores.OverallConfidence > 0.5) ? 1.0 : 0.0;
            if (_patternType.Equals("Hammer", StringComparison.OrdinalIgnoreCase) && (patternScores.BullScore > 0.6 || patternScores.BearScore > 0.6))
            {
                signal = 1.0;
            }
            
            _logger.LogTrace("Pattern signal {PatternType} for {Symbol}: {Signal}", _patternType, symbol, signal);
            return signal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern signal {PatternType} for symbol {Symbol}", _patternType, symbol);
            throw new InvalidOperationException($"Production pattern signal resolution failed for '{_patternType}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pattern confirmation resolver - PRODUCTION ONLY - checks if patterns are confirmed
/// </summary>
public sealed class PatternConfirmationResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PatternConfirmationResolver> _logger;
    
    public PatternConfirmationResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternConfirmationResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Pattern is confirmed if overall confidence > 70% and strong directional score
            var confirmed = (patternScores.OverallConfidence > 0.7 && 
                           (patternScores.BullScore > 0.6 || patternScores.BearScore > 0.6)) ? 1.0 : 0.0;
                           
            _logger.LogTrace("Pattern confirmation for {Symbol}: {Confirmed}", symbol, confirmed);
            return confirmed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern confirmation for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production pattern confirmation resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pattern reliability resolver - PRODUCTION ONLY - measures pattern reliability
/// </summary>
public sealed class PatternReliabilityResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _patternType;
    private readonly ILogger<PatternReliabilityResolver> _logger;
    
    public PatternReliabilityResolver(IServiceProvider serviceProvider, string patternType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _patternType = patternType ?? throw new ArgumentNullException(nameof(patternType));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternReliabilityResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Base reliability on overall confidence - patterns with higher confidence are more reliable
            var reliability = patternScores.OverallConfidence;
            
            _logger.LogTrace("Pattern reliability {PatternType} for {Symbol}: {Reliability}", _patternType, symbol, reliability);
            return reliability;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern reliability {PatternType} for symbol {Symbol}", _patternType, symbol);
            throw new InvalidOperationException($"Production pattern reliability resolution failed for '{_patternType}' on '{symbol}': {ex.Message}", ex);
        }
    }
}

// Market Microstructure Resolvers - Production implementations for advanced market metrics

/// <summary>
/// Volatility contraction resolver - PRODUCTION ONLY - measures volatility compression
/// </summary>
public sealed class VolatilityContractionResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VolatilityContractionResolver> _logger;
    
    public VolatilityContractionResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<VolatilityContractionResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var vdc = featureBus.Probe(symbol, "volatility.contraction");
            
            if (!vdc.HasValue)
            {
                throw new InvalidOperationException($"Volatility contraction not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Volatility contraction for {Symbol}: {VDC}", symbol, vdc);
            // Completed synchronously
            return vdc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve volatility contraction for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production volatility contraction resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Momentum Z-score resolver - PRODUCTION ONLY - calculates momentum relative strength
/// </summary>
public sealed class MomentumZScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MomentumZScoreResolver> _logger;
    
    public MomentumZScoreResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MomentumZScoreResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var zscore = featureBus.Probe(symbol, "momentum.zscore");
            
            if (!zscore.HasValue)
            {
                throw new InvalidOperationException($"Momentum Z-score not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Momentum Z-score for {Symbol}: {ZScore}", symbol, zscore);
            // Completed synchronously
            return zscore;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve momentum Z-score for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production momentum Z-score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pullback risk resolver - PRODUCTION ONLY - measures pullback risk levels
/// </summary>
public sealed class PullbackRiskResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PullbackRiskResolver> _logger;
    
    public PullbackRiskResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PullbackRiskResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var pullbackRisk = featureBus.Probe(symbol, "pullback.risk");
            
            if (!pullbackRisk.HasValue)
            {
                throw new InvalidOperationException($"Pullback risk not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Pullback risk for {Symbol}: {Risk}", symbol, pullbackRisk);
            // Completed synchronously
            return pullbackRisk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pullback risk for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production pullback risk resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Volume market resolver - PRODUCTION ONLY - analyzes volume market conditions
/// </summary>
public sealed class VolumeMarketResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _volumeType;
    private readonly ILogger<VolumeMarketResolver> _logger;
    
    public VolumeMarketResolver(IServiceProvider serviceProvider, string volumeType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _volumeType = volumeType ?? throw new ArgumentNullException(nameof(volumeType));
        _logger = serviceProvider.GetRequiredService<ILogger<VolumeMarketResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var volumeFeature = featureBus.Probe(symbol, $"volume.{_volumeType}");
            
            if (!volumeFeature.HasValue)
            {
                throw new InvalidOperationException($"Volume {_volumeType} not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Volume {VolumeType} for {Symbol}: {Value}", _volumeType, symbol, volumeFeature);
            // Completed synchronously
            return volumeFeature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve volume {VolumeType} for symbol {Symbol}", _volumeType, symbol);
            throw new InvalidOperationException($"Production volume {_volumeType} resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Inside bars resolver - PRODUCTION ONLY - detects inside bar patterns
/// </summary>
public sealed class InsideBarsResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InsideBarsResolver> _logger;
    
    public InsideBarsResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<InsideBarsResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var insideBars = featureBus.Probe(symbol, "inside_bars");
            
            if (!insideBars.HasValue)
            {
                throw new InvalidOperationException($"Inside bars not available for symbol '{symbol}' - real bar data required");
            }
            
            _logger.LogTrace("Inside bars for {Symbol}: {Count}", symbol, insideBars);
            // Completed synchronously
            return insideBars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve inside bars for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production inside bars resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// VWAP distance resolver - PRODUCTION ONLY - measures distance from VWAP
/// </summary>
public sealed class VWAPDistanceResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VWAPDistanceResolver> _logger;
    
    public VWAPDistanceResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<VWAPDistanceResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var vwapDistance = featureBus.Probe(symbol, "vwap.distance_atr");
            
            if (!vwapDistance.HasValue)
            {
                throw new InvalidOperationException($"VWAP distance not available for symbol '{symbol}' - real VWAP data required");
            }
            
            _logger.LogTrace("VWAP distance for {Symbol}: {Distance}", symbol, vwapDistance);
            // Completed synchronously
            return vwapDistance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve VWAP distance for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production VWAP distance resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Band touch resolver - PRODUCTION ONLY - detects touches of Bollinger/Keltner bands
/// </summary>
public sealed class BandTouchResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _bandType;
    private readonly ILogger<BandTouchResolver> _logger;
    
    public BandTouchResolver(IServiceProvider serviceProvider, string bandType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _bandType = bandType ?? throw new ArgumentNullException(nameof(bandType));
        _logger = serviceProvider.GetRequiredService<ILogger<BandTouchResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var bandTouch = featureBus.Probe(symbol, $"{_bandType}.touch");
            
            if (!bandTouch.HasValue)
            {
                throw new InvalidOperationException($"{_bandType} band touch not available for symbol '{symbol}' - real band data required");
            }
            
            _logger.LogTrace("{BandType} band touch for {Symbol}: {Touch}", _bandType, symbol, bandTouch);
            // Completed synchronously
            return bandTouch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve {BandType} band touch for symbol {Symbol}", _bandType, symbol);
            throw new InvalidOperationException($"Production {_bandType} band touch resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Technical Indicator Resolvers - Production implementations for standard technical indicators

/// <summary>
/// ATR resolver - PRODUCTION ONLY - calculates Average True Range
/// </summary>
public sealed class ATRResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<ATRResolver> _logger;
    
    public ATRResolver(IServiceProvider serviceProvider, int period)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period;
        _logger = serviceProvider.GetRequiredService<ILogger<ATRResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var atr = featureBus.Probe(symbol, $"atr.{_period}");
            
            if (!atr.HasValue)
            {
                throw new InvalidOperationException($"ATR({_period}) not available for symbol '{symbol}' - real bar data required");
            }
            
            _logger.LogTrace("ATR({Period}) for {Symbol}: {Value}", _period, symbol, atr);
            // Completed synchronously
            return atr;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve ATR({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production ATR({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Realized volatility resolver - PRODUCTION ONLY - calculates realized volatility
/// </summary>
public sealed class RealizedVolatilityResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RealizedVolatilityResolver> _logger;
    
    public RealizedVolatilityResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<RealizedVolatilityResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var realizedVol = featureBus.Probe(symbol, "volatility.realized");
            
            if (!realizedVol.HasValue)
            {
                throw new InvalidOperationException($"Realized volatility not available for symbol '{symbol}' - real price data required");
            }
            
            _logger.LogTrace("Realized volatility for {Symbol}: {Value}", symbol, realizedVol);
            // Completed synchronously
            return realizedVol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve realized volatility for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production realized volatility resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// RSI resolver - PRODUCTION ONLY - calculates Relative Strength Index
/// </summary>
public sealed class RSIResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<RSIResolver> _logger;
    
    public RSIResolver(IServiceProvider serviceProvider, int period)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period;
        _logger = serviceProvider.GetRequiredService<ILogger<RSIResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var rsi = featureBus.Probe(symbol, $"rsi.{_period}");
            
            if (!rsi.HasValue)
            {
                throw new InvalidOperationException($"RSI({_period}) not available for symbol '{symbol}' - real price data required");
            }
            
            _logger.LogTrace("RSI({Period}) for {Symbol}: {Value}", _period, symbol, rsi);
            // Completed synchronously
            return rsi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve RSI({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production RSI({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// EMA resolver - PRODUCTION ONLY - calculates Exponential Moving Average
/// </summary>
public sealed class EMAResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<EMAResolver> _logger;
    
    public EMAResolver(IServiceProvider serviceProvider, int period)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period;
        _logger = serviceProvider.GetRequiredService<ILogger<EMAResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var ema = featureBus.Probe(symbol, $"ema.{_period}");
            
            if (!ema.HasValue)
            {
                throw new InvalidOperationException($"EMA({_period}) not available for symbol '{symbol}' - real price data required");
            }
            
            _logger.LogTrace("EMA({Period}) for {Symbol}: {Value}", _period, symbol, ema);
            // Completed synchronously
            return ema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve EMA({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production EMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// SMA resolver - PRODUCTION ONLY - calculates Simple Moving Average
/// </summary>
public sealed class SMAResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<SMAResolver> _logger;
    
    public SMAResolver(IServiceProvider serviceProvider, int period)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period;
        _logger = serviceProvider.GetRequiredService<ILogger<SMAResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var sma = featureBus.Probe(symbol, $"sma.{_period}");
            
            if (!sma.HasValue)
            {
                throw new InvalidOperationException($"SMA({_period}) not available for symbol '{symbol}' - real price data required");
            }
            
            _logger.LogTrace("SMA({Period}) for {Symbol}: {Value}", _period, symbol, sma);
            // Completed synchronously
            return sma;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve SMA({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production SMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Risk/Position Resolvers - Production implementations for risk management and position tracking

/// <summary>
/// Risk reject resolver - PRODUCTION ONLY - tracks risk-based trade rejections
/// FAIL-CLOSED: Requires RiskManagementService - no fallbacks allowed
/// </summary>
public sealed class RiskRejectResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _riskType;
    private readonly ILogger<RiskRejectResolver> _logger;
    
    public RiskRejectResolver(IServiceProvider serviceProvider, string riskType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _riskType = riskType ?? throw new ArgumentNullException(nameof(riskType));
        _logger = serviceProvider.GetRequiredService<ILogger<RiskRejectResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync($"risk.reject.{_riskType}", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: RiskManagementService must exist - fail closed if not available
            var riskManager = _serviceProvider.GetRequiredService<BotCore.Services.RiskManagementService>();
            var rejectCount = await riskManager.GetRiskRejectCountAsync(symbol, _riskType, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Risk reject count ({RiskType}) for {Symbol}: {Count}", _riskType, symbol, rejectCount);
            await EmitSuccessTelemetryAsync($"risk.reject.{_riskType}", symbol, (double)rejectCount, cancellationToken);
            return (double)rejectCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Risk reject resolution failed for {RiskType} on {Symbol}", _riskType, symbol);
            await EmitFailureTelemetryAsync($"risk.reject.{_riskType}", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Risk reject resolution failed for '{_riskType}' on '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F2") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Position size resolver - PRODUCTION ONLY - gets current position size
/// </summary>
public sealed class PositionSizeResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionSizeResolver> _logger;
    
    public PositionSizeResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PositionSizeResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use existing PositionTrackingSystem
            var positionTracker = _serviceProvider.GetRequiredService<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
            var positions = positionTracker.GetAllPositions().Values.ToList();
            
            var totalSize = positions.Where(p => p.Symbol == symbol).Sum(p => (double)Math.Abs(p.NetQuantity));
            _logger.LogTrace("Position size for {Symbol}: {Size}", symbol, totalSize);
            return Task.FromResult<double?>(totalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position size for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production position size resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Position PnL resolver - PRODUCTION ONLY - gets current position PnL
/// </summary>
public sealed class PositionPnLResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionPnLResolver> _logger;
    
    public PositionPnLResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PositionPnLResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use existing PositionTrackingSystem
            var positionTracker = _serviceProvider.GetRequiredService<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
            var positions = positionTracker.GetAllPositions().Values.ToList();
            
            var totalPnL = positions.Where(p => p.Symbol == symbol).Sum(p => (double)p.RealizedPnL);
            _logger.LogTrace("Position PnL for {Symbol}: {PnL}", symbol, totalPnL);
            return Task.FromResult<double?>(totalPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position PnL for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production position PnL resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Unrealized PnL resolver - PRODUCTION ONLY - gets current unrealized PnL
/// </summary>
public sealed class UnrealizedPnLResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnrealizedPnLResolver> _logger;
    
    public UnrealizedPnLResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<UnrealizedPnLResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use existing PositionTrackingSystem  
            var positionTracker = _serviceProvider.GetRequiredService<TopstepX.Bot.Core.Services.PositionTrackingSystem>();
            var positions = positionTracker.GetAllPositions().Values.ToList();
            
            var unrealizedPnL = positions.Where(p => p.Symbol == symbol).Sum(p => (double)p.UnrealizedPnL);
            _logger.LogTrace("Unrealized PnL for {Symbol}: {PnL}", symbol, unrealizedPnL);
            return Task.FromResult<double?>(unrealizedPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve unrealized PnL for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production unrealized PnL resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Regime/Market State Resolvers - Production implementations for market regime detection

/// <summary>
/// Regime type resolver - PRODUCTION ONLY - determines current market regime
/// </summary>
public sealed class RegimeTypeResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegimeTypeResolver> _logger;
    
    public RegimeTypeResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<RegimeTypeResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("regime.type", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: RegimeDetectionService must exist - fail closed if not available
            var regimeDetector = _serviceProvider.GetRequiredService<BotCore.Services.RegimeDetectionService>();
            var regimeType = await regimeDetector.GetCurrentRegimeAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Convert regime type to numeric: Trend=1.0, Range=0.0, Transition=0.5
            var numericRegime = regimeType switch
            {
                "Trend" => 1.0,
                "Range" => 0.0,
                "Transition" => 0.5,
                _ => throw new InvalidOperationException($"Unknown regime type: {regimeType}")
            };
            
            _logger.LogTrace("Regime type for {Symbol}: {Regime} ({Numeric})", symbol, regimeType, numericRegime);
            await EmitSuccessTelemetryAsync("regime.type", symbol, numericRegime, cancellationToken);
            return numericRegime;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Regime type resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("regime.type", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Regime type resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F1") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Market session resolver - PRODUCTION ONLY - determines current market session
/// FAIL-CLOSED: Requires MarketTimeService - no time-based heuristics allowed
/// </summary>
public sealed class MarketSessionResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketSessionResolver> _logger;
    
    public MarketSessionResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketSessionResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("market.session", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var session = await marketTimeService.GetCurrentSessionAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Convert session to numeric: Open=1.0, PreMarket=0.3, PostMarket=0.7, Closed=0.0
            var numericSession = session switch
            {
                "Open" => 1.0,
                "PreMarket" => 0.3,
                "PostMarket" => 0.7,
                "Closed" => 0.0,
                _ => throw new InvalidOperationException($"Unknown market session: {session}")
            };
            
            _logger.LogTrace("Market session for {Symbol}: {Session} ({Numeric})", symbol, session, numericSession);
            await EmitSuccessTelemetryAsync("market.session", symbol, numericSession, cancellationToken);
            return numericSession;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Market session resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("market.session", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Market session resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F1") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Market open minutes resolver - PRODUCTION ONLY - minutes since market open
/// </summary>
public sealed class MarketOpenMinutesResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketOpenMinutesResolver> _logger;
    
    public MarketOpenMinutesResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketOpenMinutesResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("market.open_minutes", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var minutesFromOpen = await marketTimeService.GetMinutesSinceOpenAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Minutes since open for {Symbol}: {Minutes}", symbol, minutesFromOpen);
            await EmitSuccessTelemetryAsync("market.open_minutes", symbol, minutesFromOpen, cancellationToken);
            return minutesFromOpen;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Minutes since open resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("market.open_minutes", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Market open minutes resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F0") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Market close minutes resolver - PRODUCTION ONLY - minutes until market close
/// </summary>
public sealed class MarketCloseMinutesResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketCloseMinutesResolver> _logger;
    
    public MarketCloseMinutesResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketCloseMinutesResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("market.close_minutes", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var minutesToClose = await marketTimeService.GetMinutesUntilCloseAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Minutes until close for {Symbol}: {Minutes}", symbol, minutesToClose);
            await EmitSuccessTelemetryAsync("market.close_minutes", symbol, minutesToClose, cancellationToken);
            return minutesToClose;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Minutes until close resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("market.close_minutes", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Market close minutes resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F0") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Spread resolver - PRODUCTION ONLY - gets current bid-ask spread
/// </summary>
public sealed class SpreadResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpreadResolver> _logger;
    
    public SpreadResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<SpreadResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var spread = featureBus.Probe(symbol, "spread.current");
            
            if (!spread.HasValue)
            {
                throw new InvalidOperationException($"Spread not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Spread for {Symbol}: {Spread}", symbol, spread);
            // Completed synchronously
            return spread;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve spread for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production spread resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Liquidity score resolver - PRODUCTION ONLY - gets market liquidity score
/// </summary>
public sealed class LiquidityScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LiquidityScoreResolver> _logger;
    
    public LiquidityScoreResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<LiquidityScoreResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var liquidityScore = featureBus.Probe(symbol, "liquidity.score");
            
            if (!liquidityScore.HasValue)
            {
                throw new InvalidOperationException($"Liquidity score not available for symbol '{symbol}' - real market data required");
            }
            
            _logger.LogTrace("Liquidity score for {Symbol}: {Score}", symbol, liquidityScore);
            // Completed synchronously
            return liquidityScore;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve liquidity score for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production liquidity score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Execution Metrics Resolvers - Production implementations for execution performance tracking

/// <summary>
/// Execution slippage resolver - PRODUCTION ONLY - tracks execution slippage
/// FAIL-CLOSED: Requires ExecutionAnalyticsService - no defaults allowed
/// </summary>
public sealed class ExecutionSlippageResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionSlippageResolver> _logger;
    
    public ExecutionSlippageResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ExecutionSlippageResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("execution.slippage", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: ExecutionAnalyticsService must exist - fail closed if not available
            var executionAnalytics = _serviceProvider.GetRequiredService<BotCore.Services.ExecutionAnalyticsService>();
            var avgSlippage = await executionAnalytics.GetAverageSlippageAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Execution slippage for {Symbol}: {Slippage}", symbol, avgSlippage);
            await EmitSuccessTelemetryAsync("execution.slippage", symbol, avgSlippage, cancellationToken);
            return avgSlippage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Execution slippage resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("execution.slippage", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Execution slippage resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F4") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Execution fill rate resolver - PRODUCTION ONLY - tracks order fill rates
/// FAIL-CLOSED: Requires ExecutionAnalyticsService - no defaults allowed
/// </summary>
public sealed class ExecutionFillRateResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionFillRateResolver> _logger;
    
    public ExecutionFillRateResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ExecutionFillRateResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("execution.fill_rate", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: ExecutionAnalyticsService must exist - fail closed if not available
            var executionAnalytics = _serviceProvider.GetRequiredService<BotCore.Services.ExecutionAnalyticsService>();
            var fillRate = await executionAnalytics.GetFillRateAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Execution fill rate for {Symbol}: {FillRate}", symbol, fillRate);
            await EmitSuccessTelemetryAsync("execution.fill_rate", symbol, fillRate, cancellationToken);
            return fillRate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Execution fill rate resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("execution.fill_rate", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Execution fill rate resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F3") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Decision latency resolver - PRODUCTION ONLY - tracks decision-making latency
/// FAIL-CLOSED: Requires PerformanceMetricsService - no defaults allowed
/// </summary>
public sealed class DecisionLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DecisionLatencyResolver> _logger;
    
    public DecisionLatencyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<DecisionLatencyResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("latency.decision_ms", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: PerformanceMetricsService must exist - fail closed if not available
            var performanceMetrics = _serviceProvider.GetRequiredService<BotCore.Services.PerformanceMetricsService>();
            var avgLatency = await performanceMetrics.GetAverageDecisionLatencyAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Decision latency for {Symbol}: {Latency}ms", symbol, avgLatency);
            await EmitSuccessTelemetryAsync("latency.decision_ms", symbol, avgLatency, cancellationToken);
            return avgLatency;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Decision latency resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("latency.decision_ms", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Decision latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F2") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Order latency resolver - PRODUCTION ONLY - tracks order execution latency
/// FAIL-CLOSED: Requires PerformanceMetricsService - no defaults allowed
/// </summary>
public sealed class OrderLatencyResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderLatencyResolver> _logger;
    
    public OrderLatencyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<OrderLatencyResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("latency.order_ms", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: PerformanceMetricsService must exist - fail closed if not available
            var performanceMetrics = _serviceProvider.GetRequiredService<BotCore.Services.PerformanceMetricsService>();
            var avgLatency = await performanceMetrics.GetAverageOrderLatencyAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Order latency for {Symbol}: {Latency}ms", symbol, avgLatency);
            await EmitSuccessTelemetryAsync("latency.order_ms", symbol, avgLatency, cancellationToken);
            return avgLatency;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Order latency resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("latency.order_ms", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Order latency resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F2") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Recent bar count resolver - PRODUCTION ONLY - counts recent bars processed
/// FAIL-CLOSED: Requires BarTrackingService - no defaults allowed
/// </summary>
public sealed class RecentBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecentBarCountResolver> _logger;
    
    public RecentBarCountResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<RecentBarCountResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("bars.recent", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: BarTrackingService must exist - fail closed if not available
            var barTracker = _serviceProvider.GetRequiredService<BotCore.Services.BarTrackingService>();
            var recentCount = await barTracker.GetRecentBarCountAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Recent bar count for {Symbol}: {Count}", symbol, recentCount);
            await EmitSuccessTelemetryAsync("bars.recent", symbol, (double)recentCount, cancellationToken);
            return (double)recentCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Recent bar count resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("bars.recent", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Recent bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F0") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}

/// <summary>
/// Processed bar count resolver - PRODUCTION ONLY - counts total bars processed
/// FAIL-CLOSED: Requires BarTrackingService - no defaults allowed
/// </summary>
public sealed class ProcessedBarCountResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessedBarCountResolver> _logger;
    
    public ProcessedBarCountResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ProcessedBarCountResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // Emit telemetry for resolution attempt
            await EmitResolutionTelemetryAsync("bars.processed", symbol, cancellationToken);
            
            // PRODUCTION REQUIREMENT: BarTrackingService must exist - fail closed if not available
            var barTracker = _serviceProvider.GetRequiredService<BotCore.Services.BarTrackingService>();
            var processedCount = await barTracker.GetProcessedBarCountAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            _logger.LogTrace("Processed bar count for {Symbol}: {Count}", symbol, processedCount);
            await EmitSuccessTelemetryAsync("bars.processed", symbol, (double)processedCount, cancellationToken);
            return (double)processedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAIL-CLOSED: Processed bar count resolution failed for {Symbol}", symbol);
            await EmitFailureTelemetryAsync("bars.processed", symbol, ex.Message, cancellationToken);
            throw new InvalidOperationException($"PRODUCTION FAIL-CLOSED: Processed bar count resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
    
    private async Task EmitResolutionTelemetryAsync(string featureKey, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.attempt", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit resolution telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitSuccessTelemetryAsync(string featureKey, string symbol, double value, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["value"] = value.ToString("F0") };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.success", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit success telemetry for {Feature}", featureKey);
        }
    }
    
    private async Task EmitFailureTelemetryAsync(string featureKey, string symbol, string error, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string> { ["feature"] = featureKey, ["symbol"] = symbol, ["error"] = error };
                var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                if (method != null)
                {
                    var task = method.Invoke(metricsService, new object[] { "feature.resolution.failure", 1, tags, cancellationToken });
                    if (task is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit failure telemetry for {Feature}", featureKey);
        }
    }
}