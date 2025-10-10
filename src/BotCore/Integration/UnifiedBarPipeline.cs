using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BotCore.Models;
using BotCore.Services;
using System.Threading;
using System.Collections.Generic;
using Zones;

namespace BotCore.Integration;

/// <summary>
/// Unified bar pipeline - single orchestrator for all bar processing
/// Ensures consistent data flow: ZoneService → PatternEngine → DslEngine → FeatureBus
/// NO alternate paths - both live feed and backtest harness must use this single pipeline
/// </summary>
public sealed class UnifiedBarPipeline
{
    // Pipeline health constants
    private const double HealthyErrorRateThreshold = 0.01; // 1% error rate threshold
    
    private readonly ILogger<UnifiedBarPipeline> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Pipeline components - injected via service provider to ensure consistent DI graph
    private readonly Lazy<IZoneService?> _zoneService;
    private readonly Lazy<BotCore.Patterns.PatternEngine?> _patternEngine;
    private readonly Lazy<BotCore.Strategy.IStrategyKnowledgeGraph?> _dslEngine;
    private readonly Lazy<Zones.IFeatureBus?> _featureBus;
    
    // Telemetry counters
    private long _barsProcessed;
    private long _pipelineErrors;
    private DateTime _lastBarProcessed = DateTime.MinValue;
    
    public UnifiedBarPipeline(ILogger<UnifiedBarPipeline> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Lazy initialization to ensure services are resolved at runtime, not construction
        _zoneService = new Lazy<IZoneService?>(() => 
            _serviceProvider.GetService<IZoneService>());
        _patternEngine = new Lazy<BotCore.Patterns.PatternEngine?>(() => 
            _serviceProvider.GetService<BotCore.Patterns.PatternEngine>());
        _dslEngine = new Lazy<BotCore.Strategy.IStrategyKnowledgeGraph?>(() => 
            _serviceProvider.GetService<BotCore.Strategy.IStrategyKnowledgeGraph>());
        _featureBus = new Lazy<Zones.IFeatureBus?>(() => 
            _serviceProvider.GetService<Zones.IFeatureBus>());
    }
    
    /// <summary>
    /// Process a bar through the unified pipeline - THE ONLY PATH for bar processing
    /// Flow: ZoneService.OnBar → PatternEngine.OnBar → DslEngine.Evaluate → FeatureBus.Publish
    /// </summary>
    public async Task<UnifiedBarProcessingResult> ProcessAsync(string symbol, Bar bar, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        ArgumentNullException.ThrowIfNull(bar);
            
        var processingResult = new UnifiedBarProcessingResult
        {
            Symbol = symbol,
            Bar = bar,
            ProcessingStarted = DateTime.UtcNow,
            Success = false
        };
        
        try
        {
            _logger.LogDebug("Starting unified bar processing for {Symbol} at {Timestamp}", symbol, bar.Start);
            
            // Step 1: ZoneService.OnBar
            var zoneServiceResult = await ProcessZoneServiceOnBarAsync(symbol, bar, cancellationToken).ConfigureAwait(false);
            processingResult.AddPipelineStep(zoneServiceResult);
            
            if (!zoneServiceResult.Success)
            {
                _logger.LogWarning("ZoneService OnBar failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 2: PatternEngine.OnBar
            var patternEngineResult = await ProcessPatternEngineOnBarAsync(symbol, bar, cancellationToken).ConfigureAwait(false);
            processingResult.AddPipelineStep(patternEngineResult);
            
            if (!patternEngineResult.Success)
            {
                _logger.LogWarning("PatternEngine OnBar failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 3: DslEngine.Evaluate
            var dslEngineResult = await ProcessDslEngineEvaluateAsync(symbol, bar, cancellationToken).ConfigureAwait(false);
            processingResult.AddPipelineStep(dslEngineResult);
            
            if (!dslEngineResult.Success)
            {
                _logger.LogWarning("DslEngine Evaluate failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 4: FeatureBus.Publish (pattern signals injection)
            var featureBusResult = await ProcessFeatureBusPublishAsync(symbol, bar, patternEngineResult, cancellationToken).ConfigureAwait(false);
            processingResult.AddPipelineStep(featureBusResult);
            
            // Final success determination
            processingResult.Success = featureBusResult.Success;
            processingResult.ProcessingCompleted = DateTime.UtcNow;
            processingResult.ProcessingTimeMs = (processingResult.ProcessingCompleted - processingResult.ProcessingStarted).TotalMilliseconds;
            
            // Update telemetry
            Interlocked.Increment(ref _barsProcessed);
            _lastBarProcessed = DateTime.UtcNow;
            
            // Emit telemetry
            await EmitPipelineTelemetryAsync(processingResult, cancellationToken).ConfigureAwait(false);
            
            if (processingResult.Success)
            {
                _logger.LogDebug("Unified bar processing completed successfully for {Symbol} in {ProcessingTime}ms", 
                    symbol, processingResult.ProcessingTimeMs);
            }
            else
            {
                Interlocked.Increment(ref _pipelineErrors);
                _logger.LogError("Unified bar processing failed for {Symbol} after {ProcessingTime}ms", 
                    symbol, processingResult.ProcessingTimeMs);
            }
            
            return processingResult;
        }
        catch (InvalidOperationException ex)
        {
            Interlocked.Increment(ref _pipelineErrors);
            processingResult.ProcessingCompleted = DateTime.UtcNow;
            processingResult.ProcessingTimeMs = (processingResult.ProcessingCompleted - processingResult.ProcessingStarted).TotalMilliseconds;
            processingResult.Error = ex.Message;
            
            _logger.LogError(ex, "Invalid operation in unified bar pipeline for {Symbol}", symbol);
            return processingResult;
        }
        catch (ArgumentException ex)
        {
            Interlocked.Increment(ref _pipelineErrors);
            processingResult.ProcessingCompleted = DateTime.UtcNow;
            processingResult.ProcessingTimeMs = (processingResult.ProcessingCompleted - processingResult.ProcessingStarted).TotalMilliseconds;
            processingResult.Error = ex.Message;
            
            _logger.LogError(ex, "Invalid argument in unified bar pipeline for {Symbol}", symbol);
            return processingResult;
        }
    }
    
    /// <summary>
    /// Step 1: Process ZoneService OnBar
    /// </summary>
    private Task<PipelineStepResult> ProcessZoneServiceOnBarAsync(string symbol, Bar bar, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "ZoneService.OnBar",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var zoneService = _zoneService.Value ?? throw new InvalidOperationException("ZoneService must be registered in DI container for production operation");
            zoneService.OnBar(symbol, bar.Start, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
            stepResult.Success = true;
            _logger.LogTrace("ZoneService.OnBar completed for {Symbol}", symbol);
        }
        catch (InvalidOperationException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid operation in ZoneService.OnBar for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid argument in ZoneService.OnBar for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return Task.FromResult(stepResult);
    }
    
    /// <summary>
    /// Step 2: Process PatternEngine OnBar
    /// </summary>
    private async Task<PipelineStepResult> ProcessPatternEngineOnBarAsync(string symbol, Bar bar, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "PatternEngine.OnBar",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var patternEngine = _patternEngine.Value ?? throw new InvalidOperationException("PatternEngine must be registered in DI container for production operation");
            // Get current pattern scores for this bar
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            stepResult.Success = true;
            stepResult.Data = new { BullScore = patternScores.BullScore, BearScore = patternScores.BearScore };
            _logger.LogTrace("PatternEngine.OnBar completed for {Symbol} - Bull: {BullScore}, Bear: {BearScore}", 
                symbol, patternScores.BullScore, patternScores.BearScore);
        }
        catch (InvalidOperationException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid operation in PatternEngine.OnBar for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid argument in PatternEngine.OnBar for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 3: Process DslEngine Evaluate
    /// </summary>
    private async Task<PipelineStepResult> ProcessDslEngineEvaluateAsync(string symbol, Bar bar, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "DslEngine.Evaluate",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var dslEngine = _dslEngine.Value ?? throw new InvalidOperationException("DslEngine must be registered in DI container for production operation");
            var recommendations = await dslEngine.EvaluateAsync(symbol, bar.Start, cancellationToken).ConfigureAwait(false);
            stepResult.Success = true;
            stepResult.Data = new { RecommendationCount = recommendations.Count };
            _logger.LogTrace("DslEngine.Evaluate completed for {Symbol} - {RecommendationCount} recommendations", 
                symbol, recommendations.Count);
        }
        catch (InvalidOperationException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid operation in DslEngine.Evaluate for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid argument in DslEngine.Evaluate for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 4: Process FeatureBus Publish (pattern signals injection)
    /// </summary>
    private async Task<PipelineStepResult> ProcessFeatureBusPublishAsync(string symbol, Bar bar, PipelineStepResult patternEngineResult, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "FeatureBus.Publish",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var featureBus = _featureBus.Value ?? throw new InvalidOperationException("FeatureBus must be registered in DI container for production operation");
            
            if (!patternEngineResult.Success)
            {
                throw new InvalidOperationException("Pattern engine result must be successful for feature bus publishing");
            }
            
            if (patternEngineResult.Data == null)
            {
                throw new InvalidOperationException("Pattern engine data must be available for feature bus publishing");
            }
            
            // Publish pattern signals to feature bus (Data is already validated as non-null above)
            var patternData = patternEngineResult.Data as dynamic;
            
            featureBus.Publish(symbol, bar.Start, "pattern.bull_score", patternData.BullScore);
            featureBus.Publish(symbol, bar.Start, "pattern.bear_score", patternData.BearScore);
            
            // Publish bar completion signal
            featureBus.Publish(symbol, bar.Start, "bar.processed", 1.0);
            
            stepResult.Success = true;
            _logger.LogTrace("FeatureBus.Publish completed for {Symbol}", symbol);
            
            await Task.CompletedTask.ConfigureAwait(false); // Satisfy async signature
        }
        catch (InvalidOperationException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid operation in FeatureBus.Publish for {Symbol}", symbol);
        }
        catch (ArgumentException ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Invalid argument in FeatureBus.Publish for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Emit comprehensive pipeline telemetry
    /// </summary>
    private Task EmitPipelineTelemetryAsync(UnifiedBarProcessingResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Note: Using structured logging instead of metrics service for observability
            // RealTradingMetricsService integration would be done here in full production setup
            
            // Emit pipeline metrics via logging for observability
            _logger.LogInformation("Unified pipeline metrics: Symbol={Symbol}, ProcessingTime={ProcessingTimeMs}ms, Success={Success}, BarsProcessed={BarsProcessed}", 
                result.Symbol, result.ProcessingTimeMs, result.Success, _barsProcessed);
            
            // Emit step-level metrics
            foreach (var step in result.PipelineSteps)
            {
                _logger.LogTrace("Pipeline step metrics: Step={StepName}, Duration={DurationMs}ms, Success={Success}", 
                    step.StepName, step.DurationMs, step.Success);
            }
            
            // Log cumulative metrics
            _logger.LogDebug("Pipeline cumulative metrics: TotalBarsProcessed={BarsProcessed}, TotalErrors={Errors}", 
                _barsProcessed, _pipelineErrors);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation emitting pipeline telemetry for {Symbol}", result.Symbol);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument emitting pipeline telemetry for {Symbol}", result.Symbol);
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get pipeline health and statistics
    /// </summary>
    public UnifiedPipelineHealth GetPipelineHealth()
    {
        return new UnifiedPipelineHealth
        {
            BarsProcessed = _barsProcessed,
            PipelineErrors = _pipelineErrors,
            LastBarProcessed = _lastBarProcessed,
            ErrorRate = _barsProcessed > 0 ? (double)_pipelineErrors / _barsProcessed : 0.0,
            IsHealthy = _pipelineErrors == 0 || (_barsProcessed > 0 && (double)_pipelineErrors / _barsProcessed < HealthyErrorRateThreshold) // < 1% error rate
        };
    }
}

/// <summary>
/// Unified bar processing result
/// </summary>
public sealed class UnifiedBarProcessingResult
{
    public string Symbol { get; set; } = string.Empty;
    public Bar Bar { get; set; } = null!;
    public DateTime ProcessingStarted { get; set; }
    private readonly System.Collections.Generic.List<PipelineStepResult> _pipelineSteps = new();
    
    public DateTime ProcessingCompleted { get; set; }
    public double ProcessingTimeMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public System.Collections.Generic.IReadOnlyList<PipelineStepResult> PipelineSteps => _pipelineSteps;
    
    public void AddPipelineStep(PipelineStepResult step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _pipelineSteps.Add(step);
    }
}

/// <summary>
/// Individual pipeline step result
/// </summary>
public sealed class PipelineStepResult
{
    public string StepName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}

/// <summary>
/// Unified pipeline health information
/// </summary>
public sealed class UnifiedPipelineHealth
{
    public long BarsProcessed { get; set; }
    public long PipelineErrors { get; set; }
    public DateTime LastBarProcessed { get; set; }
    public double ErrorRate { get; set; }
    public bool IsHealthy { get; set; }
}