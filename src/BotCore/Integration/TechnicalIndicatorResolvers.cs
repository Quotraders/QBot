using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Technical Indicator Resolvers
public sealed class ATRResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<ATRResolver> _logger;
    
    public ATRResolver(IServiceProvider serviceProvider, int period = 14)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<ATRResolver>>();
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
            
            _logger.LogTrace("ATR({Period}) for {Symbol}: {Value}", _period, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve ATR({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production ATR({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RealizedVolatilityResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<RealizedVolatilityResolver> _logger;
    
    public RealizedVolatilityResolver(IServiceProvider serviceProvider, int period = 20)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
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
            
            _logger.LogTrace("Realized volatility for {Symbol}: {Value}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve realized volatility for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production realized volatility resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class RSIResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<RSIResolver> _logger;
    
    public RSIResolver(IServiceProvider serviceProvider, int period = 14)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<RSIResolver>>();
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
            
            _logger.LogTrace("RSI({Period}) for {Symbol}: {Value}", _period, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve RSI({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production RSI({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class EMAResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<EMAResolver> _logger;
    
    public EMAResolver(IServiceProvider serviceProvider, int period = 21)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<EMAResolver>>();
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
            
            _logger.LogTrace("EMA({Period}) for {Symbol}: {Value}", _period, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve EMA({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production EMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class SMAResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _period;
    private readonly ILogger<SMAResolver> _logger;
    
    public SMAResolver(IServiceProvider serviceProvider, int period = 50)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _period = period > 0 ? period : throw new ArgumentException("Period must be positive", nameof(period));
        _logger = serviceProvider.GetRequiredService<ILogger<SMAResolver>>();
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
            
            _logger.LogTrace("SMA({Period}) for {Symbol}: {Value}", _period, symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve SMA({Period}) for symbol {Symbol}", _period, symbol);
            throw new InvalidOperationException($"Production SMA({_period}) resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}