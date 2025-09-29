using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    
    // Telemetry tracking for missing features
    private readonly HashSet<string> _reportedMissingFeatures = new();
    private readonly object _reportingLock = new();
    
    // Performance counters
    private long _resolverCalls;
    private long _missingFeatureCalls;
    private long _resolverErrors;
    
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
        _logger.LogInformation("Initializing feature resolver manifest...");
        
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
        
        _logger.LogInformation("Feature resolver manifest initialized with {ResolverCount} resolvers", _featureResolvers.Count);
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
        RegisterResolver("vdc", new VolatilityContractionResolver(_serviceProvider));
        RegisterResolver("mom.zscore", new MomentumZScoreResolver(_serviceProvider));
        RegisterResolver("pullback.risk", new PullbackRiskResolver(_serviceProvider));
        RegisterResolver("volume.thrust", new VolumeMarketResolver(_serviceProvider, "thrust"));
        RegisterResolver("inside_bars", new InsideBarsResolver(_serviceProvider));
        RegisterResolver("vwap.distance_atr", new VWAPDistanceResolver(_serviceProvider));
        RegisterResolver("keltner.touch", new BandTouchResolver(_serviceProvider, "keltner"));
        RegisterResolver("bollinger.touch", new BandTouchResolver(_serviceProvider, "bollinger"));
    }
    
    /// <summary>
    /// Register technical indicator feature resolvers
    /// </summary>
    private void RegisterTechnicalIndicatorResolvers()
    {
        RegisterResolver("atr.14", new ATRResolver(_serviceProvider, 14));
        RegisterResolver("atr.20", new ATRResolver(_serviceProvider, 20));
        RegisterResolver("volatility.realized", new RealizedVolatilityResolver(_serviceProvider));
        RegisterResolver("volatility.contraction", new VolatilityContractionResolver(_serviceProvider));
        RegisterResolver("rsi.14", new RSIResolver(_serviceProvider, 14));
        RegisterResolver("ema.21", new EMAResolver(_serviceProvider, 21));
        RegisterResolver("sma.50", new SMAResolver(_serviceProvider, 50));
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
        RegisterResolver("spread.current", new SpreadResolver(_serviceProvider));
        RegisterResolver("liquidity.score", new LiquidityScoreResolver(_serviceProvider));
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
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));
            
        if (_featureResolvers.ContainsKey(featureKey))
        {
            _logger.LogWarning("Overriding existing resolver for feature key: {FeatureKey}", featureKey);
        }
        
        _featureResolvers[featureKey] = resolver;
        _logger.LogTrace("Registered feature resolver: {FeatureKey} → {ResolverType}", featureKey, resolver.GetType().Name);
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
                await EmitMissingFeatureTelemetryAsync(featureKey, cancellationToken);
                
                _logger.LogError("FEATURE MISSING: {FeatureKey} for {Symbol} - HOLDING DECISION", featureKey, symbol);
                return result;
            }
            
            // Resolve feature using registered resolver
            var value = await resolver.ResolveAsync(symbol, cancellationToken);
            
            result.Value = value;
            result.Success = value.HasValue;
            result.ResolverType = resolver.GetType().Name;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            if (result.Success)
            {
                _logger.LogTrace("Feature resolved: {FeatureKey} for {Symbol} = {Value} ({ResolverType})", 
                    featureKey, symbol, value, result.ResolverType);
            }
            else
            {
                _logger.LogWarning("Feature resolution returned null: {FeatureKey} for {Symbol} ({ResolverType})", 
                    featureKey, symbol, result.ResolverType);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _resolverErrors);
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            result.ResolutionTimeMs = (result.CompletedAt - result.RequestedAt).TotalMilliseconds;
            
            _logger.LogError(ex, "Error resolving feature {FeatureKey} for {Symbol}", featureKey, symbol);
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
            if (!_reportedMissingFeatures.Contains(featureKey))
            {
                _reportedMissingFeatures.Add(featureKey);
                shouldEmit = true;
            }
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
                
                _logger.LogError("🚨 FUSION FEATURE MISSING: {FeatureKey} - emitted telemetry", featureKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error emitting fusion.feature_missing telemetry for {FeatureKey}", featureKey);
            }
        }
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
        
        audit.AppendLine("=== FEATURE RESOLVER MANIFEST AUDIT ===");
        audit.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        audit.AppendLine($"Total Resolvers: {report.TotalResolvers}");
        audit.AppendLine($"Resolver Calls: {report.ResolverCalls}");
        audit.AppendLine($"Missing Feature Calls: {report.MissingFeatureCalls}");
        audit.AppendLine($"Resolver Errors: {report.ResolverErrors}");
        audit.AppendLine();
        
        // Group by feature category
        var featureGroups = new Dictionary<string, List<string>>();
        foreach (var feature in report.RegisteredFeatures.Keys)
        {
            var category = feature.Contains('.') ? feature.Split('.')[0] : "unknown";
            if (!featureGroups.ContainsKey(category))
                featureGroups[category] = new List<string>();
            featureGroups[category].Add(feature);
        }
        
        foreach (var group in featureGroups.OrderBy(g => g.Key))
        {
            audit.AppendLine($"[{group.Key.ToUpperInvariant()}] ({group.Value.Count} features)");
            foreach (var feature in group.Value.OrderBy(f => f))
            {
                var resolverType = report.RegisteredFeatures[feature];
                audit.AppendLine($"  {feature} → {resolverType}");
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

// Base resolver implementations would go here - keeping this file focused on the authority
// Individual resolvers would be implemented in separate files for maintainability