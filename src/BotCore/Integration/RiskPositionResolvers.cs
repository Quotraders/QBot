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
            
            _logger.LogTrace("Risk reject count for {Symbol}: {Count}", symbol, rejectCount);
            return rejectCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve risk reject count for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production risk reject resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

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
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalSize = symbolPositions.Sum(p => p.NetQuantity);
            
            _logger.LogTrace("Position size for {Symbol}: {Size}", symbol, totalSize);
            return Task.FromResult((double?)totalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position size for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production position size resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

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
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalPnL = symbolPositions.Sum(p => p.RealizedPnL);
            
            _logger.LogTrace("Position realized P&L for {Symbol}: {PnL:C}", symbol, totalPnL);
            return Task.FromResult((double?)totalPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve position P&L for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production position P&L resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

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
            var positionTracker = _serviceProvider.GetRequiredService<PositionTrackingSystem>();
            var positions = positionTracker.AllPositions;
            
            var symbolPositions = positions.Values.Where(p => p.Symbol == symbol).ToList();
            var totalUnrealizedPnL = symbolPositions.Sum(p => p.UnrealizedPnL);
            
            _logger.LogTrace("Position unrealized P&L for {Symbol}: {PnL:C}", symbol, totalUnrealizedPnL);
            return Task.FromResult((double?)totalUnrealizedPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve unrealized P&L for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production unrealized P&L resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}