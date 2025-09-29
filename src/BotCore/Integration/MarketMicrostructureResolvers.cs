using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Market Microstructure Resolvers
public sealed class VolatilityContractionResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VolatilityContractionResolver> _logger;
    
    public VolatilityContractionResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<VolatilityContractionResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            // Note: This resolver is registered for "vdc" key and probes "volatility.contraction" 
            // The FeatureBusMapper should handle the mapping from "vdc" to "volatility.contraction"
            var value = featureBus.Probe(symbol, "volatility.contraction");
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Volatility contraction not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Volatility contraction for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve volatility contraction for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production volatility contraction resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class MomentumZScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MomentumZScoreResolver> _logger;
    
    public MomentumZScoreResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MomentumZScoreResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            // Fixed: Use the actual key that DecisionFusionCoordinator publishes
            var value = featureBus.Probe(symbol, "momentum.zscore");
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Momentum Z-score not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Momentum Z-score for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve momentum Z-score for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production momentum Z-score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class PullbackRiskResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PullbackRiskResolver> _logger;
    
    public PullbackRiskResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PullbackRiskResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var value = featureBus.Probe(symbol, "pullback.risk");
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Pullback risk not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Pullback risk for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pullback risk for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production pullback risk resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

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
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"volume.{_volumeType}";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Volume {_volumeType} not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Volume {Type} for {Symbol}: {Value}", _volumeType, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve volume {Type} for symbol {Symbol}", _volumeType, symbol);
            throw new InvalidOperationException($"Production volume {_volumeType} resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class InsideBarsResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InsideBarsResolver> _logger;
    
    public InsideBarsResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<InsideBarsResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var value = featureBus.Probe(symbol, "inside_bars");
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Inside bars data not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Inside bars for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve inside bars for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production inside bars resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class VWAPDistanceResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VWAPDistanceResolver> _logger;
    
    public VWAPDistanceResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<VWAPDistanceResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var value = featureBus.Probe(symbol, "vwap.distance_atr");
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"VWAP distance not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("VWAP distance (ATR) for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve VWAP distance for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production VWAP distance resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

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
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"{_bandType}.touch";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"{_bandType} band touch not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("{BandType} band touch for {Symbol}: {Value}", _bandType, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve {BandType} band touch for symbol {Symbol}", _bandType, symbol);
            throw new InvalidOperationException($"Production {_bandType} band touch resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}