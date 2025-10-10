using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

// Market State and Regime Resolvers
public sealed class RegimeTypeResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegimeTypeResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, string, double, Exception?> LogRegimeType =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Trace,
            new EventId(6432, nameof(LogRegimeType)),
            "Market regime for {Symbol}: {Regime} ({Value})");
    
    private static readonly Action<ILogger, string, Exception> LogRegimeResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6433, nameof(LogRegimeResolutionFailed)),
            "Failed to resolve market regime for symbol {Symbol}");
    
    public RegimeTypeResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<RegimeTypeResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: RegimeDetectionService must exist - fail closed if not available
            var regimeDetector = _serviceProvider.GetRequiredService<BotCore.Services.RegimeDetectionService>();
            var regimeType = await regimeDetector.GetCurrentRegimeAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Convert regime type to numeric value
            var regimeValue = regimeType.ToUpperInvariant() switch
            {
                "TREND" => 1.0,
                "RANGE" => 0.0,
                "TRANSITION" => 0.5,
                _ => throw new InvalidOperationException($"Unknown regime type: {regimeType}")
            };
            
            LogRegimeType(_logger, symbol, regimeType, regimeValue, null);
            return regimeValue;
        }
        catch (Exception ex)
        {
            LogRegimeResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production market regime resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class MarketSessionResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketSessionResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, string, double, Exception?> LogMarketSession =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Trace,
            new EventId(6434, nameof(LogMarketSession)),
            "Market session for {Symbol}: {Session} ({Value})");
    
    private static readonly Action<ILogger, string, Exception> LogSessionResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6435, nameof(LogSessionResolutionFailed)),
            "Failed to resolve market session for symbol {Symbol}");
    
    public MarketSessionResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketSessionResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var session = await marketTimeService.GetCurrentSessionAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Convert session to numeric value
            var sessionValue = session.ToUpperInvariant() switch
            {
                "OPEN" => 1.0,
                "PREMARKET" => 0.25,
                "POSTMARKET" => 0.75,
                "CLOSED" => 0.0,
                _ => throw new InvalidOperationException($"Unknown market session: {session}")
            };
            
            LogMarketSession(_logger, symbol, session, sessionValue, null);
            return sessionValue;
        }
        catch (Exception ex)
        {
            LogSessionResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production market session resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class MarketOpenMinutesResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketOpenMinutesResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogMinutesSinceOpen =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6436, nameof(LogMinutesSinceOpen)),
            "Minutes since market open for {Symbol}: {Minutes}");
    
    private static readonly Action<ILogger, string, Exception> LogOpenMinutesResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6437, nameof(LogOpenMinutesResolutionFailed)),
            "Failed to resolve minutes since market open for symbol {Symbol}");
    
    public MarketOpenMinutesResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketOpenMinutesResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var minutesSinceOpen = await marketTimeService.GetMinutesSinceOpenAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            LogMinutesSinceOpen(_logger, symbol, minutesSinceOpen, null);
            return minutesSinceOpen;
        }
        catch (Exception ex)
        {
            LogOpenMinutesResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production market open minutes resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class MarketCloseMinutesResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketCloseMinutesResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogMinutesUntilClose =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6438, nameof(LogMinutesUntilClose)),
            "Minutes until market close for {Symbol}: {Minutes}");
    
    private static readonly Action<ILogger, string, Exception> LogCloseMinutesResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6439, nameof(LogCloseMinutesResolutionFailed)),
            "Failed to resolve minutes until market close for symbol {Symbol}");
    
    public MarketCloseMinutesResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<MarketCloseMinutesResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: MarketTimeService must exist - fail closed if not available
            var marketTimeService = _serviceProvider.GetRequiredService<BotCore.Services.MarketTimeService>();
            var minutesUntilClose = await marketTimeService.GetMinutesUntilCloseAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            LogMinutesUntilClose(_logger, symbol, minutesUntilClose, null);
            return minutesUntilClose;
        }
        catch (Exception ex)
        {
            LogCloseMinutesResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production market close minutes resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class SpreadResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpreadResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogSpreadValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6440, nameof(LogSpreadValue)),
            "Bid-ask spread for {Symbol}: {Spread:F4}");
    
    private static readonly Action<ILogger, string, Exception> LogSpreadResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6441, nameof(LogSpreadResolutionFailed)),
            "Failed to resolve bid-ask spread for symbol {Symbol}");
    
    public SpreadResolver(IServiceProvider serviceProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpreadResolver> _logger;
    
    public SpreadResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<SpreadResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            // Use key aliasing: "spread" DSL key maps to "spread.current" published key  
            var actualKey = FeatureMapAuthority.ResolveFeatureKey("spread");
            var value = featureBus.Probe(symbol, actualKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Bid-ask spread not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Bid-ask spread for {Symbol}: {Spread:F4}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve bid-ask spread for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production bid-ask spread resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class LiquidityScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LiquidityScoreResolver> _logger;
    
    public LiquidityScoreResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<LiquidityScoreResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureBus = _serviceProvider.GetRequiredService<BotCore.Fusion.IFeatureBusWithProbe>();
            // Use key aliasing: "liquidity_score" DSL key maps to "liquidity.score" published key
            var actualKey = FeatureMapAuthority.ResolveFeatureKey("liquidity_score");
            var value = featureBus.Probe(symbol, actualKey);
            
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Liquidity score not available for symbol '{symbol}' - fail closed");
            }
            
            _logger.LogTrace("Liquidity score for {Symbol}: {Score:F2}", symbol, value);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve liquidity score for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production liquidity score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}