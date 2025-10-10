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
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogVolatilityContraction =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6450, nameof(LogVolatilityContraction)),
            "Volatility contraction for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogVolatilityContractionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6451, nameof(LogVolatilityContractionFailed)),
            "Failed to resolve volatility contraction for symbol {Symbol}");
    
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
            // Use key aliasing: "vdc" DSL key maps to "volatility.contraction" published key
            var actualKey = FeatureMapAuthority.ResolveFeatureKey("vdc");
            var value = featureBus.Probe(symbol, actualKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Volatility contraction not available for symbol '{symbol}' - fail closed");
            }
            
            LogVolatilityContraction(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogVolatilityContractionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production volatility contraction resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class MomentumZScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MomentumZScoreResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogMomentumZScore =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6452, nameof(LogMomentumZScore)),
            "Momentum Z-score for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogMomentumZScoreFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6453, nameof(LogMomentumZScoreFailed)),
            "Failed to resolve momentum Z-score for symbol {Symbol}");
    
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
            // Use key aliasing: "mom.zscore" DSL key maps to "momentum.zscore" published key
            var actualKey = FeatureMapAuthority.ResolveFeatureKey("mom.zscore");
            var value = featureBus.Probe(symbol, actualKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Momentum Z-score not available for symbol '{symbol}' - fail closed");
            }
            
            LogMomentumZScore(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogMomentumZScoreFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production momentum Z-score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class PullbackRiskResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PullbackRiskResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogPullbackRisk =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6454, nameof(LogPullbackRisk)),
            "Pullback risk for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogPullbackRiskFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6455, nameof(LogPullbackRiskFailed)),
            "Failed to resolve pullback risk for symbol {Symbol}");
    
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
            
            LogPullbackRisk(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogPullbackRiskFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production pullback risk resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class VolumeMarketResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _volumeType;
    private readonly ILogger<VolumeMarketResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, string, double?, Exception?> LogVolumeMarket =
        LoggerMessage.Define<string, string, double?>(
            LogLevel.Trace,
            new EventId(6456, nameof(LogVolumeMarket)),
            "{VolumeType} for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, string, Exception> LogVolumeMarketFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6457, nameof(LogVolumeMarketFailed)),
            "Failed to resolve {VolumeType} for symbol {Symbol}");
    
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
            
            LogVolumeMarket(_logger, _volumeType, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogVolumeMarketFailed(_logger, _volumeType, symbol, ex);
            throw new InvalidOperationException($"Production volume {_volumeType} resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class InsideBarsResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InsideBarsResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogInsideBars =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6458, nameof(LogInsideBars)),
            "Inside bars for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogInsideBarsFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6459, nameof(LogInsideBarsFailed)),
            "Failed to resolve inside bars for symbol {Symbol}");
    
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
            
            LogInsideBars(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogInsideBarsFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production inside bars resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class VwapDistanceResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VwapDistanceResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogVwapDistance =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6460, nameof(LogVwapDistance)),
            "VWAP distance for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogVwapDistanceFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6461, nameof(LogVwapDistanceFailed)),
            "Failed to resolve VWAP distance for symbol {Symbol}");
    
    public VwapDistanceResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<VwapDistanceResolver>>();
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
            
            LogVwapDistance(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogVwapDistanceFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production VWAP distance resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class BandTouchResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _bandType;
    private readonly ILogger<BandTouchResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, string, double?, Exception?> LogBandTouch =
        LoggerMessage.Define<string, string, double?>(
            LogLevel.Trace,
            new EventId(6462, nameof(LogBandTouch)),
            "{BandType} band touch for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, string, Exception> LogBandTouchFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6463, nameof(LogBandTouchFailed)),
            "Failed to resolve {BandType} band touch for symbol {Symbol}");
    
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
            
            LogBandTouch(_logger, _bandType, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogBandTouchFailed(_logger, _bandType, symbol, ex);
            throw new InvalidOperationException($"Production {_bandType} band touch resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}