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
    // Market data defaults
    private const double DefaultAtrValue = 15.0;
    private const double StandardFuturesTickSize = 0.25;
    
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
        
        LogEpochFreezeInitialized(_logger, _config.PriceToleranceTicks, null);
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
            await CaptureZoneAnchorsAsync(snapshot, cancellationToken).ConfigureAwait(false);
            
            // Capture bracket settings
            CaptureBracketSettings(snapshot, request);
            
            // Store active epoch
            lock (_epochsLock)
            {
                _activeEpochs[positionId] = snapshot;
            }
            
            // Emit telemetry
            await EmitEpochSnapshotTelemetryAsync(snapshot, cancellationToken).ConfigureAwait(false);
            
            LogSnapshotCaptured(_logger, positionId, symbol, snapshot.EntryPrice, snapshot.BracketSettings.StopLossPrice, snapshot.BracketSettings.TakeProfitPrice, null);
                
            return snapshot;
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentCapturingSnapshot(_logger, positionId, ex);
            throw new InvalidOperationException($"Failed to capture epoch snapshot for position {positionId} due to invalid argument", ex);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationCapturingSnapshot(_logger, positionId, ex);
            throw new InvalidOperationException($"Failed to capture epoch snapshot for position {positionId}", ex);
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
            IsValid = false
        };
        
        try
        {
            // Get active epoch snapshot
            EpochSnapshot? snapshot;
            lock (_epochsLock)
            {
                if (!_activeEpochs.TryGetValue(positionId, out snapshot))
                {
                    validationResult.AddViolation(new FreezeViolation
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
            await ValidateZoneAnchorFreezeAsync(snapshot, request, validationResult, cancellationToken).ConfigureAwait(false);
            
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
                await EmitFreezeViolationTelemetryAsync(validationResult, cancellationToken).ConfigureAwait(false);
            }
            
            if (validationResult.IsValid)
            {
                LogValidationPassed(_logger, positionId, null);
            }
            else
            {
                LogViolationDetected(_logger, positionId, criticalViolations, warningViolations, null);
            }
            
            return validationResult;
        }
        catch (ArgumentException ex)
        {
            validationResult.AddViolation(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Invalid argument during validation: {ex.Message}",
                Severity = ViolationSeverity.Critical
            });
            
            LogInvalidArgumentValidating(_logger, positionId, ex);
            return validationResult;
        }
        catch (InvalidOperationException ex)
        {
            validationResult.AddViolation(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Invalid operation during validation: {ex.Message}",
                Severity = ViolationSeverity.Critical
            });
            
            LogInvalidOperationValidating(_logger, positionId, ex);
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
                await EmitEpochReleaseTelemetryAsync(releasedSnapshot, cancellationToken).ConfigureAwait(false);
                
                LogFreezeReleased(_logger, positionId, releasedSnapshot.Symbol, DateTime.UtcNow - releasedSnapshot.CapturedAt, null);
            }
            else
            {
                LogReleaseUnknownPosition(_logger, positionId, null);
            }
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentReleasing(_logger, positionId, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationReleasing(_logger, positionId, ex);
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
                LogZoneServiceNotAvailable(_logger, null);
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
                    AnchorPrice = snapshot.EntryPrice - (double)features.distToDemandAtr * GetATRValue(snapshot.Symbol),
                    DistanceATR = (double)features.distToDemandAtr,
                    BreakoutScore = (double)features.breakoutScore,
                    Pressure = (double)features.zonePressure,
                    CapturedAt = DateTime.UtcNow
                };
                
                snapshot.ZoneAnchors["supply"] = new ZoneAnchor
                {
                    ZoneType = "supply",
                    AnchorPrice = snapshot.EntryPrice + (double)features.distToSupplyAtr * GetATRValue(snapshot.Symbol),
                    DistanceATR = (double)features.distToSupplyAtr,
                    BreakoutScore = (double)features.breakoutScore,
                    Pressure = (double)features.zonePressure,
                    CapturedAt = DateTime.UtcNow
                };
            }
            
            await Task.CompletedTask.ConfigureAwait(false); // Satisfy async signature
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationCapturingZoneAnchors(_logger, snapshot.Symbol, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentCapturingZoneAnchors(_logger, snapshot.Symbol, ex);
        }
    }
    
    /// <summary>
    /// Capture bracket settings
    /// </summary>
    private static void CaptureBracketSettings(EpochSnapshot snapshot, EpochSnapshotRequest request)
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
                validationResult.AddViolation(new FreezeViolation
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
                var currentDemandPrice = request.CurrentPrice - (double)currentFeatures.distToDemandAtr * currentATR;
                var priceDifference = Math.Abs(currentDemandPrice - demandAnchor.AnchorPrice);
                var toleranceTicks = _config.PriceToleranceTicks * GetTickSize(snapshot.Symbol);
                
                if (priceDifference > toleranceTicks)
                {
                    validationResult.AddViolation(new FreezeViolation
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
                var currentSupplyPrice = request.CurrentPrice + (double)currentFeatures.distToSupplyAtr * currentATR;
                var priceDifference = Math.Abs(currentSupplyPrice - supplyAnchor.AnchorPrice);
                var toleranceTicks = _config.PriceToleranceTicks * GetTickSize(snapshot.Symbol);
                
                if (priceDifference > toleranceTicks)
                {
                    validationResult.AddViolation(new FreezeViolation
                    {
                        ViolationType = "ZoneAnchorDrift",
                        Description = $"Supply zone anchor drifted by {priceDifference:F2} (tolerance: {toleranceTicks:F2})",
                        Severity = ViolationSeverity.Critical,
                        ExpectedValue = supplyAnchor.AnchorPrice,
                        ActualValue = currentSupplyPrice
                    });
                }
            }
            
            await Task.CompletedTask.ConfigureAwait(false); // Satisfy async signature
        }
        catch (InvalidOperationException ex)
        {
            validationResult.AddViolation(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Invalid operation validating zone anchors: {ex.Message}",
                Severity = ViolationSeverity.Warning
            });
        }
        catch (ArgumentException ex)
        {
            validationResult.AddViolation(new FreezeViolation
            {
                ViolationType = "ValidationError",
                Description = $"Invalid argument validating zone anchors: {ex.Message}",
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
            validationResult.AddViolation(new FreezeViolation
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
            validationResult.AddViolation(new FreezeViolation
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
            return featureBusAdapter?.Probe(symbol, "atr.14") ?? DefaultAtrValue; // Default ATR
        }
        catch (InvalidOperationException)
        {
            return DefaultAtrValue; // Default ATR on service error
        }
        catch (ArgumentException)
        {
            return DefaultAtrValue; // Default ATR on invalid argument
        }
    }
    
    /// <summary>
    /// Get tick size for symbol (all supported symbols use standard futures tick size)
    /// </summary>
    private static double GetTickSize(string symbol)
    {
        return StandardFuturesTickSize;
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
                    ["direction"] = snapshot.Direction.ToString().ToUpperInvariant()
                };
                
                await metricsService.RecordCounterAsync("epoch_freeze.snapshots_captured", 1, tags, cancellationToken).ConfigureAwait(false);
                await metricsService.RecordGaugeAsync("epoch_freeze.active_epochs", _activeEpochs.Count, tags, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationEmittingSnapshotTelemetry(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentEmittingSnapshotTelemetry(_logger, ex);
        }
        catch (OperationCanceledException ex)
        {
            LogCancelledEmittingSnapshotTelemetry(_logger, ex);
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
                        ["severity"] = violation.Severity.ToString().ToUpperInvariant()
                    };
                    
                    await metricsService.RecordCounterAsync("epoch_freeze.violations", 1, tags, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationEmittingViolationTelemetry(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentEmittingViolationTelemetry(_logger, ex);
        }
        catch (OperationCanceledException ex)
        {
            LogCancelledEmittingViolationTelemetry(_logger, ex);
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
                    ["direction"] = snapshot.Direction.ToString().ToUpperInvariant()
                };
                
                await metricsService.RecordCounterAsync("epoch_freeze.epochs_released", 1, tags, cancellationToken).ConfigureAwait(false);
                await metricsService.RecordGaugeAsync("epoch_freeze.epoch_duration_minutes", duration.TotalMinutes, tags, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperationEmittingReleaseTelemetry(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogInvalidArgumentEmittingReleaseTelemetry(_logger, ex);
        }
        catch (OperationCanceledException ex)
        {
            LogCancelledEmittingReleaseTelemetry(_logger, ex);
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
    
    // LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, double, Exception?> LogEpochFreezeInitialized =
        LoggerMessage.Define<double>(LogLevel.Information, new EventId(6001, nameof(LogEpochFreezeInitialized)),
            "EpochFreeze enforcement initialized with tolerance: {Tolerance} ticks");
    
    private static readonly Action<ILogger, string, string, double, double, double, Exception?> LogSnapshotCaptured =
        LoggerMessage.Define<string, string, double, double, double>(LogLevel.Information, new EventId(6002, nameof(LogSnapshotCaptured)),
            "EpochFreeze snapshot captured for position {PositionId} on {Symbol} - Entry: {EntryPrice}, SL: {StopLoss}, TP: {TakeProfit}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidArgumentCapturingSnapshot =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6003, nameof(LogInvalidArgumentCapturingSnapshot)),
            "Invalid argument capturing epoch snapshot for position {PositionId}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidOperationCapturingSnapshot =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6004, nameof(LogInvalidOperationCapturingSnapshot)),
            "Invalid operation capturing epoch snapshot for position {PositionId}");
    
    private static readonly Action<ILogger, string, Exception?> LogValidationPassed =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(6005, nameof(LogValidationPassed)),
            "EpochFreeze validation passed for position {PositionId}");
    
    private static readonly Action<ILogger, string, int, int, Exception?> LogViolationDetected =
        LoggerMessage.Define<string, int, int>(LogLevel.Warning, new EventId(6006, nameof(LogViolationDetected)),
            "EpochFreeze violation detected for position {PositionId} - {CriticalCount} critical, {WarningCount} warnings");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidArgumentValidating =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6007, nameof(LogInvalidArgumentValidating)),
            "Invalid argument validating epoch freeze for position {PositionId}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidOperationValidating =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6008, nameof(LogInvalidOperationValidating)),
            "Invalid operation validating epoch freeze for position {PositionId}");
    
    private static readonly Action<ILogger, string, string, TimeSpan, Exception?> LogFreezeReleased =
        LoggerMessage.Define<string, string, TimeSpan>(LogLevel.Information, new EventId(6009, nameof(LogFreezeReleased)),
            "EpochFreeze released for position {PositionId} on {Symbol} - Duration: {Duration}");
    
    private static readonly Action<ILogger, string, Exception?> LogReleaseUnknownPosition =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(6010, nameof(LogReleaseUnknownPosition)),
            "Attempted to release epoch freeze for unknown position {PositionId}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidArgumentReleasing =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6011, nameof(LogInvalidArgumentReleasing)),
            "Invalid argument releasing epoch freeze for position {PositionId}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidOperationReleasing =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6012, nameof(LogInvalidOperationReleasing)),
            "Invalid operation releasing epoch freeze for position {PositionId}");
    
    private static readonly Action<ILogger, Exception?> LogZoneServiceNotAvailable =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6013, nameof(LogZoneServiceNotAvailable)),
            "Zone service not available for epoch snapshot capture");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidOperationCapturingZoneAnchors =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6014, nameof(LogInvalidOperationCapturingZoneAnchors)),
            "Invalid operation capturing zone anchors for {Symbol}");
    
    private static readonly Action<ILogger, string, Exception?> LogInvalidArgumentCapturingZoneAnchors =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6015, nameof(LogInvalidArgumentCapturingZoneAnchors)),
            "Invalid argument capturing zone anchors for {Symbol}");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationEmittingSnapshotTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6016, nameof(LogInvalidOperationEmittingSnapshotTelemetry)),
            "Invalid operation emitting epoch snapshot telemetry");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentEmittingSnapshotTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6017, nameof(LogInvalidArgumentEmittingSnapshotTelemetry)),
            "Invalid argument emitting epoch snapshot telemetry");
    
    private static readonly Action<ILogger, Exception?> LogCancelledEmittingSnapshotTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6018, nameof(LogCancelledEmittingSnapshotTelemetry)),
            "Operation cancelled emitting epoch snapshot telemetry");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationEmittingViolationTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6019, nameof(LogInvalidOperationEmittingViolationTelemetry)),
            "Invalid operation emitting freeze violation telemetry");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentEmittingViolationTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6020, nameof(LogInvalidArgumentEmittingViolationTelemetry)),
            "Invalid argument emitting freeze violation telemetry");
    
    private static readonly Action<ILogger, Exception?> LogCancelledEmittingViolationTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6021, nameof(LogCancelledEmittingViolationTelemetry)),
            "Operation cancelled emitting freeze violation telemetry");
    
    private static readonly Action<ILogger, Exception?> LogInvalidOperationEmittingReleaseTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6022, nameof(LogInvalidOperationEmittingReleaseTelemetry)),
            "Invalid operation emitting epoch release telemetry");
    
    private static readonly Action<ILogger, Exception?> LogInvalidArgumentEmittingReleaseTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6023, nameof(LogInvalidArgumentEmittingReleaseTelemetry)),
            "Invalid argument emitting epoch release telemetry");
    
    private static readonly Action<ILogger, Exception?> LogCancelledEmittingReleaseTelemetry =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6024, nameof(LogCancelledEmittingReleaseTelemetry)),
            "Operation cancelled emitting epoch release telemetry");
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
    public Dictionary<string, ZoneAnchor> ZoneAnchors { get; init; } = new();
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
    private readonly System.Collections.Generic.List<FreezeViolation> _violations = new();
    
    public string PositionId { get; set; } = string.Empty;
    public DateTime ValidationTime { get; set; }
    public bool IsValid { get; set; }
    public System.Collections.Generic.IReadOnlyList<FreezeViolation> Violations => _violations;
    public string? Note { get; set; }
    
    public void AddViolation(FreezeViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        _violations.Add(violation);
    }
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
    Buy,
    Sell
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