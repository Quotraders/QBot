using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Zones;
using TradingBot.IntelligenceStack;

namespace BotCore.Integration;

/// <summary>
/// EpochFreeze enforcement system - prevents zone anchor and bracket drift mid-position
/// When a position opens, snapshots zone anchors and bracket settings
/// Enforces that SL/TP stay fixed until position exit
/// Emits telemetry when freeze triggers are detected
/// </summary>
public sealed class EpochFreezeEnforcement
{
    private readonly ILogger<EpochFreezeEnforcement> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Active epoch snapshots keyed by position ID
    private readonly Dictionary<string, EpochSnapshot> _activeEpochs = new();
    private readonly object _epochsLock = new();
    
    // Freeze violation tracking
    private long _freezeViolationCount;
    private long _successfulFreezeEnforcements;
    
    // Configuration
    private readonly EpochFreezeConfig _config;
    
    public EpochFreezeEnforcement(ILogger<EpochFreezeEnforcement> logger, IServiceProvider serviceProvider, EpochFreezeConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _config = config ?? new EpochFreezeConfig();
        
        _logger.LogInformation("EpochFreeze enforcement initialized with tolerance: {Tolerance} ticks", _config.PriceToleranceTicks);
    }
    
    /// <summary>
    /// Capture epoch snapshot when position opens
    /// </summary>
    public async Task<EpochSnapshot> CaptureEpochSnapshotAsync(string positionId, string symbol, EpochSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(positionId))
            throw new ArgumentException("Position ID cannot be null or empty", nameof(positionId));
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        ArgumentNullException.ThrowIfNull(request);
            
        var snapshot = new EpochSnapshot
        {
            PositionId = positionId,
            Symbol = symbol,
            CapturedAt = DateTime.UtcNow,
            EntryPrice = request.EntryPrice,
            PositionSize = request.PositionSize,
            Direction = request.Direction,
            ZoneAnchors = new Dictionary<string, ZoneAnchor>(),
            BracketSettings = new BracketSnapshot(),
            IsFrozen = true
        };
        
        try
        {
            // Capture zone anchors from zone service
            await CaptureZoneAnchorsAsync(snapshot, cancellationToken);
            
            // Capture bracket settings
            CaptureBracketSettings(snapshot, request);
            
            // Store active epoch
            lock (_epochsLock)
            {
                _activeEpochs[positionId] = snapshot;
            }
            
            // Emit telemetry
            await EmitEpochSnapshotTelemetryAsync(snapshot, cancellationToken);
            
            _logger.LogInformation("EpochFreeze snapshot captured for position {PositionId} on {Symbol} - Entry: {EntryPrice}, SL: {StopLoss}, TP: {TakeProfit}",
                positionId, symbol, snapshot.EntryPrice, snapshot.BracketSettings.StopLossPrice, snapshot.BracketSettings.TakeProfitPrice);
                
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing epoch snapshot for position {PositionId}", positionId);
            throw;
        }
    }
    
    /// <summary>
    /// Enforce epoch freeze - validate that zone anchors and brackets haven't drifted
    /// </summary>
    public async Task<EpochFreezeValidationResult> ValidateEpochFreezeAsync(string positionId, EpochValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(positionId))
            throw new ArgumentException("Position ID cannot be null or empty", nameof(positionId));
        ArgumentNullException.ThrowIfNull(request);
            
        var validationResult = new EpochFreezeValidationResult
        {
            PositionId = positionId,
            ValidationTime = DateTime.UtcNow,
            IsValid = false,
            Violations = new List<FreezeViolation>()
        };
        
        try
        {
            // Get active epoch snapshot
            EpochSnapshot? snapshot;
            lock (_epochsLock)
            {
                if (!_activeEpochs.TryGetValue(positionId, out snapshot))
                {
                    validationResult.Violations.Add(new FreezeViolation
                    {
                        ViolationType = "MissingSnapshot",
                        Description = $"No epoch snapshot found for position {positionId}",
                        Severity = ViolationSeverity.Critical
                    });
                    return validationResult;
                }
            }
            
            if (!snapshot.IsFrozen)
            {
                validationResult.IsValid = true;
                validationResult.Note = "Position not frozen - validation skipped";
                return validationResult;
            }
            
            // Validate zone anchors haven't drifted
            await ValidateZoneAnchorFreezeAsync(snapshot, request, validationResult, cancellationToken);
            
            // Validate bracket settings haven't changed
            ValidateBracketFreeze(snapshot, request, validationResult);
            
            // Determine overall validity
            var criticalViolations = validationResult.Violations.Count(v => v.Severity == ViolationSeverity.Critical);
            var warningViolations = validationResult.Violations.Count(v => v.Severity == ViolationSeverity.Warning);
            
            validationResult.IsValid = criticalViolations == 0;
            
            // Update counters
            if (validationResult.IsValid)
            {
                Interlocked.Increment(ref _successfulFreezeEnforcements);
            }
            else
            {
                Interlocked.Increment(ref _freezeViolationCount);
            }
            
            // Emit telemetry for violations
            if (!validationResult.IsValid || warningViolations > 0)
            {
                await EmitFreezeViolationTelemetryAsync(validationResult, cancellationToken);
            }
            
            if (validationResult.IsValid)
            {
                _logger.LogTrace("EpochFreeze validation passed for position {PositionId}", positionId);
            }
            else
            {
                _logger.LogWarning("EpochFreeze violation detected for position {PositionId} - {CriticalCount} critical, {WarningCount} warnings",
                    positionId, criticalViolations, warningViolations);
            }
            
            return validationResult;
        }
        catch (Exception ex)
        {
            validationResult.Violations.Add(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Error during validation: {ex.Message}",
                Severity = ViolationSeverity.Critical
            });
            
            _logger.LogError(ex, "Error validating epoch freeze for position {PositionId}", positionId);
            return validationResult;
        }
    }
    
    /// <summary>
    /// Release epoch freeze when position closes
    /// </summary>
    public async Task ReleaseEpochFreezeAsync(string positionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(positionId))
            throw new ArgumentException("Position ID cannot be null or empty", nameof(positionId));
            
        try
        {
            EpochSnapshot? releasedSnapshot = null;
            
            lock (_epochsLock)
            {
                if (_activeEpochs.TryGetValue(positionId, out releasedSnapshot))
                {
                    _activeEpochs.Remove(positionId);
                }
            }
            
            if (releasedSnapshot != null)
            {
                // Emit telemetry for epoch release
                await EmitEpochReleaseTelemetryAsync(releasedSnapshot, cancellationToken);
                
                _logger.LogInformation("EpochFreeze released for position {PositionId} on {Symbol} - Duration: {Duration}",
                    positionId, releasedSnapshot.Symbol, DateTime.UtcNow - releasedSnapshot.CapturedAt);
            }
            else
            {
                _logger.LogWarning("Attempted to release epoch freeze for unknown position {PositionId}", positionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing epoch freeze for position {PositionId}", positionId);
        }
    }
    
    /// <summary>
    /// Capture zone anchors from zone service
    /// </summary>
    private async Task CaptureZoneAnchorsAsync(EpochSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var zoneService = _serviceProvider.GetService<Zones.IZoneService>();
            if (zoneService == null)
            {
                _logger.LogWarning("Zone service not available for epoch snapshot capture");
                return;
            }
            
            // Get current zone features for the symbol
            var zoneFeatureSource = _serviceProvider.GetService<Zones.IZoneFeatureSource>();
            if (zoneFeatureSource != null)
            {
                var features = zoneFeatureSource.GetFeatures(snapshot.Symbol);
                
                // Create zone anchors based on current zone state
                snapshot.ZoneAnchors["demand"] = new ZoneAnchor
                {
                    ZoneType = "demand",
                    AnchorPrice = snapshot.EntryPrice - features.distToDemandAtr * GetATRValue(snapshot.Symbol),
                    DistanceATR = features.distToDemandAtr,
                    BreakoutScore = features.breakoutScore,
                    Pressure = features.zonePressure,
                    CapturedAt = DateTime.UtcNow
                };
                
                snapshot.ZoneAnchors["supply"] = new ZoneAnchor
                {
                    ZoneType = "supply",
                    AnchorPrice = snapshot.EntryPrice + features.distToSupplyAtr * GetATRValue(snapshot.Symbol),
                    DistanceATR = features.distToSupplyAtr,
                    BreakoutScore = features.breakoutScore,
                    Pressure = features.zonePressure,
                    CapturedAt = DateTime.UtcNow
                };
            }
            
            await Task.CompletedTask; // Satisfy async signature
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing zone anchors for {Symbol}", snapshot.Symbol);
        }
    }
    
    /// <summary>
    /// Capture bracket settings
    /// </summary>
    private void CaptureBracketSettings(EpochSnapshot snapshot, EpochSnapshotRequest request)
    {
        snapshot.BracketSettings = new BracketSnapshot
        {
            StopLossPrice = request.StopLossPrice,
            TakeProfitPrice = request.TakeProfitPrice,
            StopLossATR = request.StopLossATR,
            TakeProfitATR = request.TakeProfitATR,
            RiskRewardRatio = CalculateRiskRewardRatio(request),
            CapturedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Validate zone anchor freeze
    /// </summary>
    private async Task ValidateZoneAnchorFreezeAsync(EpochSnapshot snapshot, EpochValidationRequest request, EpochFreezeValidationResult validationResult, CancellationToken cancellationToken)
    {
        try
        {
            var zoneFeatureSource = _serviceProvider.GetService<Zones.IZoneFeatureSource>();
            if (zoneFeatureSource == null)
            {
                validationResult.Violations.Add(new FreezeViolation
                {
                    ViolationType = "ServiceUnavailable",
                    Description = "Zone feature source not available for validation",
                    Severity = ViolationSeverity.Warning
                });
                return;
            }
            
            var currentFeatures = zoneFeatureSource.GetFeatures(snapshot.Symbol);
            var currentATR = GetATRValue(snapshot.Symbol);
            
            // Validate demand zone anchor
            if (snapshot.ZoneAnchors.TryGetValue("demand", out var demandAnchor))
            {
                var currentDemandPrice = request.CurrentPrice - currentFeatures.distToDemandAtr * currentATR;
                var priceDifference = Math.Abs(currentDemandPrice - demandAnchor.AnchorPrice);
                var toleranceTicks = _config.PriceToleranceTicks * GetTickSize(snapshot.Symbol);
                
                if (priceDifference > toleranceTicks)
                {
                    validationResult.Violations.Add(new FreezeViolation
                    {
                        ViolationType = "ZoneAnchorDrift",
                        Description = $"Demand zone anchor drifted by {priceDifference:F2} (tolerance: {toleranceTicks:F2})",
                        Severity = ViolationSeverity.Critical,
                        ExpectedValue = demandAnchor.AnchorPrice,
                        ActualValue = currentDemandPrice
                    });
                }
            }
            
            // Validate supply zone anchor
            if (snapshot.ZoneAnchors.TryGetValue("supply", out var supplyAnchor))
            {
                var currentSupplyPrice = request.CurrentPrice + currentFeatures.distToSupplyAtr * currentATR;
                var priceDifference = Math.Abs(currentSupplyPrice - supplyAnchor.AnchorPrice);
                var toleranceTicks = _config.PriceToleranceTicks * GetTickSize(snapshot.Symbol);
                
                if (priceDifference > toleranceTicks)
                {
                    validationResult.Violations.Add(new FreezeViolation
                    {
                        ViolationType = "ZoneAnchorDrift",
                        Description = $"Supply zone anchor drifted by {priceDifference:F2} (tolerance: {toleranceTicks:F2})",
                        Severity = ViolationSeverity.Critical,
                        ExpectedValue = supplyAnchor.AnchorPrice,
                        ActualValue = currentSupplyPrice
                    });
                }
            }
            
            await Task.CompletedTask; // Satisfy async signature
        }
        catch (Exception ex)
        {
            validationResult.Violations.Add(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Error validating zone anchors: {ex.Message}",
                Severity = ViolationSeverity.Warning
            });
        }
    }
    
    /// <summary>
    /// Validate bracket freeze
    /// </summary>
    private void ValidateBracketFreeze(EpochSnapshot snapshot, EpochValidationRequest request, EpochFreezeValidationResult validationResult)
    {
        var tolerance = _config.PriceToleranceTicks * GetTickSize(snapshot.Symbol);
        
        // Validate stop loss hasn't moved
        var slDifference = Math.Abs(request.CurrentStopLoss - snapshot.BracketSettings.StopLossPrice);
        if (slDifference > tolerance)
        {
            validationResult.Violations.Add(new FreezeViolation
            {
                ViolationType = "StopLossDrift",
                Description = $"Stop loss drifted by {slDifference:F2} (tolerance: {tolerance:F2})",
                Severity = ViolationSeverity.Critical,
                ExpectedValue = snapshot.BracketSettings.StopLossPrice,
                ActualValue = request.CurrentStopLoss
            });
        }
        
        // Validate take profit hasn't moved
        var tpDifference = Math.Abs(request.CurrentTakeProfit - snapshot.BracketSettings.TakeProfitPrice);
        if (tpDifference > tolerance)
        {
            validationResult.Violations.Add(new FreezeViolation
            {
                ViolationType = "TakeProfitDrift",
                Description = $"Take profit drifted by {tpDifference:F2} (tolerance: {tolerance:F2})",
                Severity = ViolationSeverity.Critical,
                ExpectedValue = snapshot.BracketSettings.TakeProfitPrice,
                ActualValue = request.CurrentTakeProfit
            });
        }
    }
    
    /// <summary>
    /// Get ATR value for symbol
    /// </summary>
    private double GetATRValue(string symbol)
    {
        try
        {
            var featureBusAdapter = _serviceProvider.GetService<BotCore.Fusion.IFeatureBusWithProbe>();
            return featureBusAdapter?.Probe(symbol, "atr.14") ?? 15.0; // Default ATR
        }
        catch
        {
            return 15.0; // Default ATR
        }
    }
    
    /// <summary>
    /// Get tick size for symbol
    /// </summary>
    private static double GetTickSize(string symbol)
    {
        return symbol switch
        {
            "ES" => 0.25,
            "NQ" => 0.25,
            _ => 0.25
        };
    }
    
    /// <summary>
    /// Calculate risk/reward ratio
    /// </summary>
    private static double CalculateRiskRewardRatio(EpochSnapshotRequest request)
    {
        var risk = Math.Abs(request.EntryPrice - request.StopLossPrice);
        var reward = Math.Abs(request.TakeProfitPrice - request.EntryPrice);
        
        return risk > 0 ? reward / risk : 0.0;
    }
    
    /// <summary>
    /// Emit epoch snapshot telemetry
    /// </summary>
    private async Task EmitEpochSnapshotTelemetryAsync(EpochSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (metricsService != null)
            {
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = snapshot.Symbol,
                    ["direction"] = snapshot.Direction.ToString().ToLowerInvariant()
                };
                
                await metricsService.RecordCounterAsync("epoch_freeze.snapshots_captured", 1, tags, cancellationToken);
                await metricsService.RecordGaugeAsync("epoch_freeze.active_epochs", _activeEpochs.Count, tags, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting epoch snapshot telemetry");
        }
    }
    
    /// <summary>
    /// Emit freeze violation telemetry
    /// </summary>
    private async Task EmitFreezeViolationTelemetryAsync(EpochFreezeValidationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (metricsService != null)
            {
                foreach (var violation in result.Violations)
                {
                    var tags = new Dictionary<string, object>
                    {
                        ["position_id"] = result.PositionId,
                        ["violation_type"] = violation.ViolationType,
                        ["severity"] = violation.Severity.ToString().ToLowerInvariant()
                    };
                    
                    await metricsService.RecordCounterAsync("epoch_freeze.violations", 1, tags, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting freeze violation telemetry");
        }
    }
    
    /// <summary>
    /// Emit epoch release telemetry
    /// </summary>
    private async Task EmitEpochReleaseTelemetryAsync(EpochSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _serviceProvider.GetService<RealTradingMetricsService>();
            if (metricsService != null)
            {
                var duration = DateTime.UtcNow - snapshot.CapturedAt;
                var tags = new Dictionary<string, object>
                {
                    ["symbol"] = snapshot.Symbol,
                    ["direction"] = snapshot.Direction.ToString().ToLowerInvariant()
                };
                
                await metricsService.RecordCounterAsync("epoch_freeze.epochs_released", 1, tags, cancellationToken);
                await metricsService.RecordGaugeAsync("epoch_freeze.epoch_duration_minutes", duration.TotalMinutes, tags, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting epoch release telemetry");
        }
    }
    
    /// <summary>
    /// Get enforcement statistics
    /// </summary>
    public EpochFreezeStats GetEnforcementStats()
    {
        lock (_epochsLock)
        {
            return new EpochFreezeStats
            {
                ActiveEpochs = _activeEpochs.Count,
                TotalViolations = _freezeViolationCount,
                SuccessfulEnforcements = _successfulFreezeEnforcements,
                ViolationRate = _successfulFreezeEnforcements > 0 ? (double)_freezeViolationCount / (_freezeViolationCount + _successfulFreezeEnforcements) : 0.0
            };
        }
    }
}

// Supporting data structures and enums...

/// <summary>
/// Epoch freeze configuration
/// </summary>
public sealed class EpochFreezeConfig
{
    public double PriceToleranceTicks { get; set; } = 1.0; // Allow 1 tick tolerance
    public bool EnableZoneAnchorValidation { get; set; } = true;
    public bool EnableBracketValidation { get; set; } = true;
    public TimeSpan MaxEpochDuration { get; set; } = TimeSpan.FromHours(8); // Maximum position duration
}

/// <summary>
/// Epoch snapshot captured at position open
/// </summary>
public sealed class EpochSnapshot
{
    public string PositionId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public double EntryPrice { get; set; }
    public double PositionSize { get; set; }
    public PositionDirection Direction { get; set; }
    public Dictionary<string, ZoneAnchor> ZoneAnchors { get; set; } = new();
    public BracketSnapshot BracketSettings { get; set; } = new();
    public bool IsFrozen { get; set; }
}

/// <summary>
/// Zone anchor snapshot
/// </summary>
public sealed class ZoneAnchor
{
    public string ZoneType { get; set; } = string.Empty; // "demand" or "supply"
    public double AnchorPrice { get; set; }
    public double DistanceATR { get; set; }
    public double BreakoutScore { get; set; }
    public double Pressure { get; set; }
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Bracket settings snapshot
/// </summary>
public sealed class BracketSnapshot
{
    public double StopLossPrice { get; set; }
    public double TakeProfitPrice { get; set; }
    public double StopLossATR { get; set; }
    public double TakeProfitATR { get; set; }
    public double RiskRewardRatio { get; set; }
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Epoch snapshot request
/// </summary>
public sealed class EpochSnapshotRequest
{
    public double EntryPrice { get; set; }
    public double PositionSize { get; set; }
    public PositionDirection Direction { get; set; }
    public double StopLossPrice { get; set; }
    public double TakeProfitPrice { get; set; }
    public double StopLossATR { get; set; }
    public double TakeProfitATR { get; set; }
}

/// <summary>
/// Epoch validation request
/// </summary>
public sealed class EpochValidationRequest
{
    public double CurrentPrice { get; set; }
    public double CurrentStopLoss { get; set; }
    public double CurrentTakeProfit { get; set; }
}

/// <summary>
/// Epoch freeze validation result
/// </summary>
public sealed class EpochFreezeValidationResult
{
    public string PositionId { get; set; } = string.Empty;
    public DateTime ValidationTime { get; set; }
    public bool IsValid { get; set; }
    public List<FreezeViolation> Violations { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>
/// Freeze violation details
/// </summary>
public sealed class FreezeViolation
{
    public string ViolationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
    public double? ExpectedValue { get; set; }
    public double? ActualValue { get; set; }
}

/// <summary>
/// Violation severity levels
/// </summary>
public enum ViolationSeverity
{
    Warning,
    Critical
}

/// <summary>
/// Position direction
/// </summary>
public enum PositionDirection
{
    Long,
    Short
}

/// <summary>
/// Epoch freeze enforcement statistics
/// </summary>
public sealed class EpochFreezeStats
{
    public int ActiveEpochs { get; set; }
    public long TotalViolations { get; set; }
    public long SuccessfulEnforcements { get; set; }
    public double ViolationRate { get; set; }
}