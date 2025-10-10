using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BotCore.Features;

namespace BotCore.Integration;

/// <summary>
/// Feature map authority - central manifest mapping every DSL key to concrete resolvers
/// Unknown keys trigger hold decisions and emit fusion.feature_missing telemetry
/// NO silent failures - all feature requests must be explicitly mapped
/// </summary>
public sealed class FeatureMapAuthority
{
    private readonly ILogger<FeatureMapAuthority> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Master feature resolver manifest - ALL DSL keys must be mapped here
    private readonly Dictionary<string, IFeatureResolver> _featureResolvers = new();
    
    // Feature key aliasing - maps DSL shorthand keys to actual published feature keys
    private static readonly Dictionary<string, string> FeatureKeyAliases = new()
    {
        // DSL shorthand -> Published key
        ["vdc"] = "volatility.contraction",
        ["mom.zscore"] = "momentum.zscore",
        ["momentum.z_score"] = "momentum.zscore", // Alternative mapping
        ["momentum.acceleration"] = "momentum.zscore", // Acceleration approximated via Z-score
        ["pullback.risk"] = "pullback.risk",
        ["liquidity_score"] = "liquidity.score",
        ["spread"] = "spread.current",
        
        // Liquidity absorption feature aliases for DSL usage
        ["liquidity.absorb_bull"] = "liquidity.absorb_bull",
        ["liquidity.absorb_bear"] = "liquidity.absorb_bear", 
        ["liquidity.vpr"] = "liquidity.vpr",
        
        // Order Flow Imbalance proxy alias
        ["ofi.proxy"] = "ofi.proxy"
    };
    
    // Telemetry tracking for missing features
    private readonly HashSet<string> _reportedMissingFeatures = new();
    private readonly object _reportingLock = new();
    
    // Performance counters
    private long _resolverCalls;
    private long _missingFeatureCalls;
    private long _resolverErrors;
    
    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, Exception?> LogInitializingManifest =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6520, nameof(LogInitializingManifest)),
            "Initializing feature resolver manifest...");
    
    private static readonly Action<ILogger, int, Exception?> LogManifestInitialized =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(6521, nameof(LogManifestInitialized)),
            "Feature resolver manifest initialized with {ResolverCount} resolvers");
    
    private static readonly Action<ILogger, string, Exception?> LogOverridingResolver =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6522, nameof(LogOverridingResolver)),
            "Overriding existing resolver for feature key: {FeatureKey}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogResolverRegistered =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(6523, nameof(LogResolverRegistered)),
            "Registered feature resolver: {FeatureKey} → {ResolverType}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogFeatureMissing =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6524, nameof(LogFeatureMissing)),
            "FEATURE MISSING: {FeatureKey} for {Symbol} - HOLDING DECISION");
    
    private static readonly Action<ILogger, string, string, double?, string, Exception?> LogFeatureResolved =
        LoggerMessage.Define<string, string, double?, string>(
            LogLevel.Trace,
            new EventId(6525, nameof(LogFeatureResolved)),
            "Feature resolved: {FeatureKey} for {Symbol} = {Value} ({ResolverType})");
    
    private static readonly Action<ILogger, string, string, string, Exception?> LogFeatureResolvedNull =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(6526, nameof(LogFeatureResolvedNull)),
            "Feature resolution returned null: {FeatureKey} for {Symbol} ({ResolverType})");
    
    private static readonly Action<ILogger, string, string, Exception> LogInvalidOperationResolvingFeature =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6527, nameof(LogInvalidOperationResolvingFeature)),
            "Invalid operation resolving feature {FeatureKey} for {Symbol}");
    
    private static readonly Action<ILogger, string, string, Exception> LogFeatureKeyNotFound =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6528, nameof(LogFeatureKeyNotFound)),
            "Feature key not found: {FeatureKey} for {Symbol}");
    
    private static readonly Action<ILogger, string, string, Exception> LogTimeoutResolvingFeature =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6529, nameof(LogTimeoutResolvingFeature)),
            "Timeout resolving feature {FeatureKey} for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogFusionFeatureMissing =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6530, nameof(LogFusionFeatureMissing)),
            "🚨 FUSION FEATURE MISSING: {FeatureKey} - emitted telemetry");
    
    private static readonly Action<ILogger, string, Exception> LogReflectionErrorEmittingTelemetry =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6531, nameof(LogReflectionErrorEmittingTelemetry)),
            "Reflection error emitting fusion.feature_missing telemetry for {FeatureKey}");
    
    private static readonly Action<ILogger, string, Exception> LogInvalidOperationEmittingTelemetry =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6532, nameof(LogInvalidOperationEmittingTelemetry)),
            "Invalid operation emitting fusion.feature_missing telemetry for {FeatureKey}");
    
    private static readonly Action<ILogger, string, string, double, Exception?> LogMtfFeatureValue =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Trace,
            new EventId(6533, nameof(LogMtfFeatureValue)),
            "MTF feature {FeatureKey} for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogMtfFeatureNoValue =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(6534, nameof(LogMtfFeatureNoValue)),
            "MTF feature {FeatureKey} for {Symbol}: no value available");
    
    private static readonly Action<ILogger, string, string, Exception> LogMtfFeatureFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6535, nameof(LogMtfFeatureFailed)),
            "Failed to resolve MTF feature {FeatureKey} for symbol {Symbol}");
    
    private static readonly Action<ILogger, string, string, double, Exception?> LogLiquidityFeatureValue =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Trace,
            new EventId(6536, nameof(LogLiquidityFeatureValue)),
            "Liquidity feature {FeatureKey} for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, string, Exception?> LogLiquidityFeatureNoValue =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(6537, nameof(LogLiquidityFeatureNoValue)),
            "Liquidity feature {FeatureKey} for {Symbol}: no value available");
    
    private static readonly Action<ILogger, string, string, Exception> LogLiquidityFeatureFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6538, nameof(LogLiquidityFeatureFailed)),
            "Failed to resolve liquidity feature {FeatureKey} for symbol {Symbol}");
    
    private static readonly Action<ILogger, string, double, Exception?> LogOfiProxyValue =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(6539, nameof(LogOfiProxyValue)),
            "OFI proxy for {Symbol}: {Value}");
    
    private static readonly Action<ILogger, string, Exception?> LogOfiProxyNoValue =
        LoggerMessage.Define<string>(
            LogLevel.Trace,
            new EventId(6540, nameof(LogOfiProxyNoValue)),
            "OFI proxy for {Symbol}: no value available");
    
    private static readonly Action<ILogger, string, Exception> LogOfiProxyFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6541, nameof(LogOfiProxyFailed)),
            "Failed to resolve OFI proxy for symbol {Symbol}");
    
    public FeatureMapAuthority(ILogger<FeatureMapAuthority> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        InitializeFeatureResolverManifest();
    }
    
    /// <summary>
    /// Initialize the complete feature resolver manifest
    /// Every DSL key used anywhere in the system MUST be mapped here
    /// </summary>
    private void InitializeFeatureResolverManifest()
    {
        LogInitializingManifest(_logger, null);
        
        // Zone-based features
        RegisterZoneFeatureResolvers();
        
        // Pattern-based features
        RegisterPatternFeatureResolvers();
        
        // Market microstructure features
        RegisterMarketMicrostructureResolvers();
        
        // Technical indicator features
        RegisterTechnicalIndicatorResolvers();
        
        // Risk and position features
        RegisterRiskPositionResolvers();
        
        // Regime and market state features
        RegisterRegimeMarketStateResolvers();
        
        // Performance and execution features
        RegisterPerformanceExecutionResolvers();
        
        LogManifestInitialized(_logger, _featureResolvers.Count, null);
    }
    
    /// <summary>
    /// Register zone-based feature resolvers
    /// </summary>
    private void RegisterZoneFeatureResolvers()
    {
        RegisterResolver("zone.dist_to_demand_atr", new ZoneFeatureResolver(_serviceProvider, "dist_to_demand_atr"));
        RegisterResolver("zone.dist_to_supply_atr", new ZoneFeatureResolver(_serviceProvider, "dist_to_supply_atr"));
        RegisterResolver("zone.breakout_score", new ZoneFeatureResolver(_serviceProvider, "breakout_score"));
        RegisterResolver("zone.pressure", new ZoneFeatureResolver(_serviceProvider, "pressure"));
        RegisterResolver("zone.test_count", new ZoneFeatureResolver(_serviceProvider, "test_count"));
        RegisterResolver("zone.dist_to_opposing_atr", new ZoneFeatureResolver(_serviceProvider, "dist_to_opposing_atr"));
        RegisterResolver("zone.proximity_atr_demand", new ZoneFeatureResolver(_serviceProvider, "proximity_atr_demand"));
        RegisterResolver("zone.proximity_atr_supply", new ZoneFeatureResolver(_serviceProvider, "proximity_atr_supply"));
        RegisterResolver("zones.count", new ZoneCountResolver(_serviceProvider));
        RegisterResolver("zones.tests", new ZoneTestsResolver(_serviceProvider));
    }
    
    /// <summary>
    /// Register pattern-based feature resolvers
    /// </summary>
    private void RegisterPatternFeatureResolvers()
    {
        RegisterResolver("pattern.bull_score", new PatternScoreResolver(_serviceProvider, true));
        RegisterResolver("pattern.bear_score", new PatternScoreResolver(_serviceProvider, false));
        RegisterResolver("pattern.signal_doji", new PatternSignalResolver(_serviceProvider, "Doji"));
        RegisterResolver("pattern.signal_hammer", new PatternSignalResolver(_serviceProvider, "Hammer"));
        RegisterResolver("pattern.signal_confirmed", new PatternConfirmationResolver(_serviceProvider));
        RegisterResolver("pattern.reliability_doji", new PatternReliabilityResolver(_serviceProvider, "Doji"));
        RegisterResolver("pattern.reliability_hammer", new PatternReliabilityResolver(_serviceProvider, "Hammer"));
    }
    
    /// <summary>
    /// Register market microstructure feature resolvers
    /// </summary>
    private void RegisterMarketMicrostructureResolvers()
    {
        // Fixed: Register with DSL shorthand keys that StrategyKnowledgeGraphNew actually uses
        RegisterResolver("vdc", new VolatilityContractionResolver(_serviceProvider));
        RegisterResolver("mom.zscore", new MomentumZScoreResolver(_serviceProvider));
        RegisterResolver("momentum.z_score", new MomentumZScoreResolver(_serviceProvider)); // Alternative key
        RegisterResolver("momentum.acceleration", new MomentumZScoreResolver(_serviceProvider)); // Acceleration via Z-score
        RegisterResolver("pullback.risk", new PullbackRiskResolver(_serviceProvider));
        RegisterResolver("volume.thrust", new VolumeMarketResolver(_serviceProvider, "thrust"));
        RegisterResolver("inside_bars", new InsideBarsResolver(_serviceProvider));
        RegisterResolver("vwap.distance_atr", new VwapDistanceResolver(_serviceProvider));
        RegisterResolver("keltner.touch", new BandTouchResolver(_serviceProvider, "keltner"));
        RegisterResolver("bollinger.touch", new BandTouchResolver(_serviceProvider, "bollinger"));
        
        // Liquidity absorption features
        RegisterResolver("liquidity.absorb_bull", new LiquidityAbsorptionFeatureResolver(_serviceProvider, "liquidity.absorb_bull"));
        RegisterResolver("liquidity.absorb_bear", new LiquidityAbsorptionFeatureResolver(_serviceProvider, "liquidity.absorb_bear"));
        RegisterResolver("liquidity.vpr", new LiquidityAbsorptionFeatureResolver(_serviceProvider, "liquidity.vpr"));
        
        // Order flow imbalance proxy
        RegisterResolver("ofi.proxy", new OfiProxyFeatureResolver(_serviceProvider));
        
        // Multi-timeframe structure features using adapter pattern
        RegisterResolver("mtf.align", new MtfFeatureResolver(_serviceProvider, "mtf.align"));
        RegisterResolver("mtf.bias", new MtfFeatureResolver(_serviceProvider, "mtf.bias"));
    }
    
    /// <summary>
    /// Register technical indicator feature resolvers
    /// </summary>
    private void RegisterTechnicalIndicatorResolvers()
    {
        RegisterResolver("atr.14", new AtrResolver(_serviceProvider, 14));
        RegisterResolver("atr.20", new AtrResolver(_serviceProvider, 20));
        RegisterResolver("volatility.realized", new RealizedVolatilityResolver(_serviceProvider));
        RegisterResolver("volatility.contraction", new VolatilityContractionResolver(_serviceProvider));
        RegisterResolver("rsi.14", new RsiResolver(_serviceProvider, 14));
        RegisterResolver("ema.21", new EmaResolver(_serviceProvider, 21));
        RegisterResolver("sma.50", new SmaResolver(_serviceProvider, 50));
    }
    
    /// <summary>
    /// Register risk and position feature resolvers
    /// </summary>
    private void RegisterRiskPositionResolvers()
    {
        RegisterResolver("risk.rejected_entries_overexposed", new RiskRejectResolver(_serviceProvider, "overexposed"));
        RegisterResolver("risk.rejected_entries_correlation", new RiskRejectResolver(_serviceProvider, "correlation"));
        RegisterResolver("risk.rejected_entries_volatility", new RiskRejectResolver(_serviceProvider, "volatility"));
        RegisterResolver("position.size", new PositionSizeResolver(_serviceProvider));
        RegisterResolver("position.pnl", new PositionPnLResolver(_serviceProvider));
        RegisterResolver("position.unrealized", new UnrealizedPnLResolver(_serviceProvider));
    }
    
    /// <summary>
    /// Register regime and market state feature resolvers
    /// </summary>
    private void RegisterRegimeMarketStateResolvers()
    {
        RegisterResolver("regime.type", new RegimeTypeResolver(_serviceProvider));
        RegisterResolver("market.session", new MarketSessionResolver(_serviceProvider));
        RegisterResolver("market.open_minutes", new MarketOpenMinutesResolver(_serviceProvider));
        RegisterResolver("market.close_minutes", new MarketCloseMinutesResolver(_serviceProvider));
        
        // Register both aliased keys for spread and liquidity
        var spreadResolver = new SpreadResolver(_serviceProvider);
        var liquidityResolver = new LiquidityScoreResolver(_serviceProvider);
        RegisterResolver("spread.current", spreadResolver);
        RegisterResolver("spread", spreadResolver); // DSL shorthand
        RegisterResolver("liquidity.score", liquidityResolver);
        RegisterResolver("liquidity_score", liquidityResolver); // DSL shorthand
    }
    
    /// <summary>
    /// Register performance and execution feature resolvers
    /// </summary>
    private void RegisterPerformanceExecutionResolvers()
    {
        RegisterResolver("execution.slippage", new ExecutionSlippageResolver(_serviceProvider));
        RegisterResolver("execution.fill_rate", new ExecutionFillRateResolver(_serviceProvider));
        RegisterResolver("latency.decision_ms", new DecisionLatencyResolver(_serviceProvider));
        RegisterResolver("latency.order_ms", new OrderLatencyResolver(_serviceProvider));
        RegisterResolver("bars.recent", new RecentBarCountResolver(_serviceProvider));
        RegisterResolver("bars.processed", new ProcessedBarCountResolver(_serviceProvider));
    }
    
    /// <summary>
    /// Register a feature resolver in the manifest
    /// </summary>
    private void RegisterResolver(string featureKey, IFeatureResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
            throw new ArgumentException("Feature key cannot be null or empty", nameof(featureKey));
        ArgumentNullException.ThrowIfNull(resolver);
            
        if (_featureResolvers.ContainsKey(featureKey))
        {
            LogOverridingResolver(_logger, featureKey, null);
        }
        
        _featureResolvers[featureKey] = resolver;
        LogResolverRegistered(_logger, featureKey, resolver.GetType().Name, null);
    }
    
    /// <summary>
    /// Resolve a feature value using the manifest - THE ONLY WAY to get feature values
    /// Unknown keys trigger hold decisions and emit telemetry
    /// </summary>
    public async Task<FeatureResolutionResult> ResolveFeatureAsync(string symbol, string featureKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        if (string.IsNullOrWhiteSpace(featureKey))
            throw new ArgumentException("Feature key cannot be null or empty", nameof(featureKey));
            
        Interlocked.Increment(ref _resolverCalls);
        
        var result = new FeatureResolutionResult
        {
            Symbol = symbol,
            FeatureKey = featureKey,
            RequestedAt = DateTime.UtcNow,
            Success = false
        };
        
        try
        {
            // Check if resolver exists in manifest
            if (!_featureResolvers.TryGetValue(featureKey, out var resolver))
            {
                // Unknown feature key - trigger hold decision and emit telemetry
                Interlocked.Increment(ref _missingFeatureCalls);
                result.Error = $"Feature key '{featureKey}' not found in resolver manifest";
                result.ShouldHoldDecision = true;
                
                // Emit fusion.feature_missing telemetry (once per session)
                await EmitMissingFeatureTelemetryAsync(featureKey, cancellationToken).ConfigureAwait(false);
                
                LogFeatureMissing(_logger, featureKey, symbol, null);
                return result;
            }
            
            // Resolve feature using registered resolver
            var value = await resolver.ResolveAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            result.Value = value;
            result.Success = value.HasValue;
            result.ResolverType = resolver.GetType().Name;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            if (result.Success)
            {
                LogFeatureResolved(_logger, featureKey, symbol, value, result.ResolverType, null);
            }
            else
            {
                LogFeatureResolvedNull(_logger, featureKey, symbol, result.ResolverType, null);
            }
            
            return result;
        }
        catch (InvalidOperationException ex)
        {
            Interlocked.Increment(ref _resolverErrors);
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            LogInvalidOperationResolvingFeature(_logger, featureKey, symbol, ex);
            return result;
        }
        catch (KeyNotFoundException ex)
        {
            Interlocked.Increment(ref _resolverErrors);
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            LogFeatureKeyNotFound(_logger, featureKey, symbol, ex);
            return result;
        }
        catch (TimeoutException ex)
        {
            Interlocked.Increment(ref _resolverErrors);
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            LogTimeoutResolvingFeature(_logger, featureKey, symbol, ex);
            return result;
        }
    }
    
    /// <summary>
    /// Emit fusion.feature_missing telemetry once per session per missing key
    /// </summary>
    private async Task EmitMissingFeatureTelemetryAsync(string featureKey, CancellationToken cancellationToken)
    {
        bool shouldEmit = false;
        
        lock (_reportingLock)
        {
            shouldEmit = _reportedMissingFeatures.Add(featureKey);
        }
        
        if (shouldEmit)
        {
            try
            {
                var metricsService = _serviceProvider.GetService(typeof(TradingBot.IntelligenceStack.RealTradingMetricsService));
                if (metricsService != null)
                {
                    var tags = new Dictionary<string, string> { ["key"] = featureKey };
                    // Use reflection to call RecordCounterAsync since we can't strongly type it
                    var method = metricsService.GetType().GetMethod("RecordCounterAsync");
                    if (method != null)
                    {
                        var task = method.Invoke(metricsService, new object[] { "fusion.feature_missing", 1, tags, cancellationToken });
                        if (task is Task taskResult)
                        {
                            await taskResult.ConfigureAwait(false);
                        }
                    }
                }
                
                LogFusionFeatureMissing(_logger, featureKey, null);
            }
            catch (TargetInvocationException ex)
            {
                LogReflectionErrorEmittingTelemetry(_logger, featureKey, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogInvalidOperationEmittingTelemetry(_logger, featureKey, ex);
            }
        }
    }
    
    /// <summary>
    /// Resolve feature key aliases - maps DSL shorthand to actual published keys
    /// </summary>
    public static string ResolveFeatureKey(string dslKey)
    {
        return FeatureKeyAliases.TryGetValue(dslKey, out var actualKey) ? actualKey : dslKey;
    }
    
    /// <summary>
    /// Get comprehensive feature manifest report
    /// </summary>
    public FeatureManifestReport GetManifestReport()
    {
        var report = new FeatureManifestReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalResolvers = _featureResolvers.Count,
            ResolverCalls = _resolverCalls,
            MissingFeatureCalls = _missingFeatureCalls,
            ResolverErrors = _resolverErrors
        };
        
        foreach (var kvp in _featureResolvers)
        {
            report.RegisteredFeatures[kvp.Key] = kvp.Value.GetType().Name;
        }
        
        return report;
    }
    
    /// <summary>
    /// Generate comprehensive feature manifest audit log
    /// </summary>
    public string GenerateManifestAudit()
    {
        var report = GetManifestReport();
        var audit = new StringBuilder();
        
        audit.AppendLine(CultureInfo.InvariantCulture, $"=== FEATURE RESOLVER MANIFEST AUDIT ===");
        audit.AppendLine(CultureInfo.InvariantCulture, $"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        audit.AppendLine(CultureInfo.InvariantCulture, $"Total Resolvers: {report.TotalResolvers}");
        audit.AppendLine(CultureInfo.InvariantCulture, $"Resolver Calls: {report.ResolverCalls}");
        audit.AppendLine(CultureInfo.InvariantCulture, $"Missing Feature Calls: {report.MissingFeatureCalls}");
        audit.AppendLine(CultureInfo.InvariantCulture, $"Resolver Errors: {report.ResolverErrors}");
        audit.AppendLine();
        
        // Group by feature category
        var featureGroups = new Dictionary<string, List<string>>();
        foreach (var feature in report.RegisteredFeatures.Keys)
        {
            var category = feature.Contains('.', StringComparison.Ordinal) ? feature.Split('.')[0] : "unknown";
            if (!featureGroups.ContainsKey(category))
                featureGroups[category] = new List<string>();
            featureGroups[category].Add(feature);
        }
        
        foreach (var group in featureGroups.OrderBy(g => g.Key))
        {
            audit.AppendLine(CultureInfo.InvariantCulture, $"[{group.Key.ToUpperInvariant()}] ({group.Value.Count} features)");
            foreach (var feature in group.Value.OrderBy(f => f))
            {
                var resolverType = report.RegisteredFeatures[feature];
                audit.AppendLine(CultureInfo.InvariantCulture, $"  {feature} → {resolverType}");
            }
            audit.AppendLine();
        }
        
        return audit.ToString();
    }
}

/// <summary>
/// Feature resolution result
/// </summary>
public sealed class FeatureResolutionResult
{
    public string Symbol { get; set; } = string.Empty;
    public string FeatureKey { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public double ResolutionTimeMs { get; set; }
    public bool Success { get; set; }
    public double? Value { get; set; }
    public string? Error { get; set; }
    public string? ResolverType { get; set; }
    public bool ShouldHoldDecision { get; set; }
}

/// <summary>
/// Feature manifest report
/// </summary>
public sealed class FeatureManifestReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalResolvers { get; set; }
    public long ResolverCalls { get; set; }
    public long MissingFeatureCalls { get; set; }
    public long ResolverErrors { get; set; }
    public Dictionary<string, string> RegisteredFeatures { get; } = new();
}

/// <summary>
/// Base interface for all feature resolvers
/// </summary>
public interface IFeatureResolver
{
    Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default);
}

/// <summary>
/// MTF Feature Resolver adapter - bridges MtfStructureResolver to Integration interface
/// </summary>
public sealed class MtfFeatureResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _featureKey;
    private readonly ILogger<MtfFeatureResolver> _logger;
    
    public MtfFeatureResolver(IServiceProvider serviceProvider, string featureKey)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _featureKey = featureKey ?? throw new ArgumentNullException(nameof(featureKey));
        _logger = serviceProvider.GetRequiredService<ILogger<MtfFeatureResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var mtfResolver = _serviceProvider.GetRequiredService<BotCore.Features.MtfStructureResolver>();
            var value = await mtfResolver.TryGetAsync(symbol, _featureKey, cancellationToken).ConfigureAwait(false);
            
            if (value.HasValue)
            {
                LogMtfFeatureValue(_logger, _featureKey, symbol, value.Value, null);
            }
            else
            {
                LogMtfFeatureNoValue(_logger, _featureKey, symbol, null);
            }
            
            return value;
        }
        catch (Exception ex)
        {
            LogMtfFeatureFailed(_logger, _featureKey, symbol, ex);
            throw new InvalidOperationException($"Production MTF feature resolution failed for '{symbol}.{_featureKey}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Liquidity Absorption Feature Resolver adapter - bridges LiquidityAbsorptionResolver to Integration interface
/// </summary>
public sealed class LiquidityAbsorptionFeatureResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _featureKey;
    private readonly ILogger<LiquidityAbsorptionFeatureResolver> _logger;
    
    public LiquidityAbsorptionFeatureResolver(IServiceProvider serviceProvider, string featureKey)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _featureKey = featureKey ?? throw new ArgumentNullException(nameof(featureKey));
        _logger = serviceProvider.GetRequiredService<ILogger<LiquidityAbsorptionFeatureResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var liquidityResolver = _serviceProvider.GetRequiredService<BotCore.Features.LiquidityAbsorptionResolver>();
            var value = await liquidityResolver.TryGetAsync(symbol, _featureKey, cancellationToken).ConfigureAwait(false);
            
            if (value.HasValue)
            {
                _logger.LogTrace("Liquidity feature {FeatureKey} for {Symbol}: {Value}", _featureKey, symbol, value.Value);
            }
            else
            {
                _logger.LogTrace("Liquidity feature {FeatureKey} for {Symbol}: no value available", _featureKey, symbol);
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve liquidity feature {FeatureKey} for symbol {Symbol}", _featureKey, symbol);
            throw new InvalidOperationException($"Production liquidity feature resolution failed for '{symbol}.{_featureKey}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Order Flow Imbalance Proxy Feature Resolver adapter - bridges OfiProxyResolver to Integration interface
/// </summary>
public sealed class OfiProxyFeatureResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OfiProxyFeatureResolver> _logger;
    
    public OfiProxyFeatureResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<OfiProxyFeatureResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var ofiResolver = _serviceProvider.GetRequiredService<BotCore.Features.OfiProxyResolver>();
            var value = await ofiResolver.TryGetAsync(symbol, "ofi.proxy", cancellationToken).ConfigureAwait(false);
            
            if (value.HasValue)
            {
                _logger.LogTrace("OFI proxy for {Symbol}: {Value}", symbol, value.Value);
            }
            else
            {
                _logger.LogTrace("OFI proxy for {Symbol}: no value available", symbol);
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve OFI proxy for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production OFI proxy resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

// Base resolver implementations would go here - keeping this file focused on the authority
// Individual resolvers would be implemented in separate files for maintainability