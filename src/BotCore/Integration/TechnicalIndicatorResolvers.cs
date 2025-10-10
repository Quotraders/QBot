using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Technical Indicator Resolvers
public sealed class AtrResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<AtrResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, int, string, double?, Exception?> LogAtrValue =
        LoggerMessage.Define<int, string, double?>(
            LogLevel.Trace,
            new EventId(6410, nameof(LogAtrValue)),
            "ATR({Period}) for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, int, string, Exception> LogAtrResolutionFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(6411, nameof(LogAtrResolutionFailed)),
            "Failed to resolve ATR({Period}) for symbol {Symbol}");
    
    public AtrResolver(IServiceProvider serviceProvider, int period = 14)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<AtrResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"atr.{_period}";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"ATR({_period}) not available for symbol '{symbol}' - fail closed");
            }
            
            LogAtrValue(_logger, _period, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogAtrResolutionFailed(_logger, _period, symbol, ex);
            throw new InvalidOperationException($"Production ATR({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RealizedVolatilityResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RealizedVolatilityResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double?, Exception?> LogVolatilityValue =
        LoggerMessage.Define<string, double?>(
            LogLevel.Trace,
            new EventId(6412, nameof(LogVolatilityValue)),
            "Realized volatility for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception> LogVolatilityResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6413, nameof(LogVolatilityResolutionFailed)),
            "Failed to resolve realized volatility for symbol {Symbol}");
    
    public RealizedVolatilityResolver(IServiceProvider serviceProvider, int period = 20)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        if (period <= 0) throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<RealizedVolatilityResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            // Fixed: Use the actual key that DecisionFusionCoordinator publishes
            var featureKey = "volatility.realized";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Realized volatility not available for symbol '{symbol}' - fail closed");
            }
            
            LogVolatilityValue(_logger, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogVolatilityResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production realized volatility resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RsiResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<RsiResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, int, string, double?, Exception?> LogRsiValue =
        LoggerMessage.Define<int, string, double?>(
            LogLevel.Trace,
            new EventId(6414, nameof(LogRsiValue)),
            "RSI({Period}) for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, int, string, Exception> LogRsiResolutionFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(6415, nameof(LogRsiResolutionFailed)),
            "Failed to resolve RSI({Period}) for symbol {Symbol}");
    
    public RsiResolver(IServiceProvider serviceProvider, int period = 14)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<RsiResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"rsi.{_period}";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"RSI({_period}) not available for symbol '{symbol}' - fail closed");
            }
            
            LogRsiValue(_logger, _period, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogRsiResolutionFailed(_logger, _period, symbol, ex);
            throw new InvalidOperationException($"Production RSI({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class EmaResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<EmaResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, int, string, double?, Exception?> LogEmaValue =
        LoggerMessage.Define<int, string, double?>(
            LogLevel.Trace,
            new EventId(6416, nameof(LogEmaValue)),
            "EMA({Period}) for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, int, string, Exception> LogEmaResolutionFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(6417, nameof(LogEmaResolutionFailed)),
            "Failed to resolve EMA({Period}) for symbol {Symbol}");
    
    public EmaResolver(IServiceProvider serviceProvider, int period = 21)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<EmaResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"ema.{_period}";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"EMA({_period}) not available for symbol '{symbol}' - fail closed");
            }
            
            LogEmaValue(_logger, _period, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogEmaResolutionFailed(_logger, _period, symbol, ex);
            throw new InvalidOperationException($"Production EMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class SmaResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<SmaResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, int, string, double?, Exception?> LogSmaValue =
        LoggerMessage.Define<int, string, double?>(
            LogLevel.Trace,
            new EventId(6418, nameof(LogSmaValue)),
            "SMA({Period}) for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, int, string, Exception> LogSmaResolutionFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(6419, nameof(LogSmaResolutionFailed)),
            "Failed to resolve SMA({Period}) for symbol {Symbol}");
    
    public SmaResolver(IServiceProvider serviceProvider, int period = 50)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<SmaResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            var featureKey = $"sma.{_period}";
            var value = featureBus.Probe(symbol, featureKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"SMA({_period}) not available for symbol '{symbol}' - fail closed");
            }
            
            LogSmaValue(_logger, _period, symbol, value, null);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            LogSmaResolutionFailed(_logger, _period, symbol, ex);
            throw new InvalidOperationException($"Production SMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}