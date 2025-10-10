using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TopstepX.Bot.Core.Services;

namespace BotCore.Integration;

// Risk and Position Resolvers
public sealed class RiskRejectResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RiskRejectResolver> _logger;
    private readonly string _riskType;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, double, Exception?> LogRiskRejectCount =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(5030, nameof(LogRiskRejectCount)),
            "Risk reject count for {Symbol}: {Count}");
    
    private static readonly Action<ILogger, string, Exception?> LogRiskRejectResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5031, nameof(LogRiskRejectResolutionFailed)),
            "Failed to resolve risk reject count for symbol {Symbol}");
    
    public RiskRejectResolver(IServiceProvider serviceProvider) : this(serviceProvider, "all_types")
    {
    }
    
    public RiskRejectResolver(IServiceProvider serviceProvider, string riskType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _riskType = riskType ?? "all_types";
        _logger = serviceProvider.GetRequiredService<ILogger<RiskRejectResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // PRODUCTION REQUIREMENT: RiskManagementService must exist - fail closed if not available
            var riskManagement = _serviceProvider.GetRequiredService<BotCore.Services.RiskManagementService>();
            var rejectCount = await riskManagement.GetRiskRejectCountAsync(symbol, _riskType, cancellationToken).ConfigureAwait(false);
            
            LogRiskRejectCount(_logger, symbol, rejectCount, null);
            return rejectCount;
        }
        catch (Exception ex)
        {
            LogRiskRejectResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production risk reject resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class PositionSizeResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionSizeResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, int, Exception?> LogPositionSize =
        LoggerMessage.Define<string, int>(
            LogLevel.Trace,
            new EventId(5032, nameof(LogPositionSize)),
            "Position size for {Symbol}: {Size}");
    
    private static readonly Action<ILogger, string, Exception?> LogPositionSizeResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5033, nameof(LogPositionSizeResolutionFailed)),
            "Failed to resolve position size for symbol {Symbol}");
    
    public PositionSizeResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PositionSizeResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalSize = symbolPositions.Sum(p => p.NetQuantity);
            
            LogPositionSize(_logger, symbol, totalSize, null);
            return Task.FromResult((double?)totalSize);
        }
        catch (Exception ex)
        {
            LogPositionSizeResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production position size resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class PositionPnLResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionPnLResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, decimal, Exception?> LogPositionPnL =
        LoggerMessage.Define<string, decimal>(
            LogLevel.Trace,
            new EventId(5034, nameof(LogPositionPnL)),
            "Position realized P&L for {Symbol}: {PnL:C}");
    
    private static readonly Action<ILogger, string, Exception?> LogPositionPnLResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5035, nameof(LogPositionPnLResolutionFailed)),
            "Failed to resolve position P&L for symbol {Symbol}");
    
    public PositionPnLResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PositionPnLResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalPnL = symbolPositions.Sum(p => p.RealizedPnL);
            
            LogPositionPnL(_logger, symbol, totalPnL, null);
            return Task.FromResult((double?)totalPnL);
        }
        catch (Exception ex)
        {
            LogPositionPnLResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production position P&L resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

public sealed class UnrealizedPnLResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnrealizedPnLResolver> _logger;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, string, decimal, Exception?> LogUnrealizedPnL =
        LoggerMessage.Define<string, decimal>(
            LogLevel.Trace,
            new EventId(5036, nameof(LogUnrealizedPnL)),
            "Position unrealized P&L for {Symbol}: {PnL:C}");
    
    private static readonly Action<ILogger, string, Exception?> LogUnrealizedPnLResolutionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5037, nameof(LogUnrealizedPnLResolutionFailed)),
            "Failed to resolve unrealized P&L for symbol {Symbol}");
    
    public UnrealizedPnLResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<UnrealizedPnLResolver>>();
    }
    
    public Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalUnrealizedPnL = symbolPositions.Sum(p => p.UnrealizedPnL);
            
            LogUnrealizedPnL(_logger, symbol, totalUnrealizedPnL, null);
            return Task.FromResult((double?)totalUnrealizedPnL);
        }
        catch (Exception ex)
        {
            LogUnrealizedPnLResolutionFailed(_logger, symbol, ex);
            throw new InvalidOperationException($"Production unrealized P&L resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}