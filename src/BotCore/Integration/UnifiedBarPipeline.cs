using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BotCore.Models;
using BotCore.Services;
using System.Threading;

namespace BotCore.Integration;

/// <summary>
/// Unified bar pipeline - single orchestrator for all bar processing
/// Ensures consistent data flow: MarketStructureCore → ZoneService → PatternEngine → DslEngine → FeatureBus
/// NO alternate paths - both live feed and backtest harness must use this single pipeline
/// </summary>
public sealed class UnifiedBarPipeline
{
    private readonly ILogger<UnifiedBarPipeline> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // Pipeline components - injected via service provider to ensure consistent DI graph
    private readonly Lazy<BotCore.Services.MarketStructureCore?> _marketStructureCore;
    private readonly Lazy<Zones.IZoneService?> _zoneService;
    private readonly Lazy<BotCore.Patterns.PatternEngine?> _patternEngine;
    private readonly Lazy<BotCore.StrategyDsl.IStrategyKnowledgeGraph?> _dslEngine;
    private readonly Lazy<Zones.IFeatureBus?> _featureBus;
    private readonly Lazy<BotCore.Services.RealTradingMetricsService?> _metricsService;
    
    // Telemetry counters
    private long _barsProcessed = 0;
    private long _pipelineErrors = 0;
    private DateTime _lastBarProcessed = DateTime.MinValue;
    
    public UnifiedBarPipeline(ILogger<UnifiedBarPipeline> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Lazy initialization to ensure services are resolved at runtime, not construction
        _marketStructureCore = new Lazy<BotCore.Services.MarketStructureCore?>(() => 
            _serviceProvider.GetService<BotCore.Services.MarketStructureCore>());
        _zoneService = new Lazy<Zones.IZoneService?>(() => 
            _serviceProvider.GetService<Zones.IZoneService>());
        _patternEngine = new Lazy<BotCore.Patterns.PatternEngine?>(() => 
            _serviceProvider.GetService<BotCore.Patterns.PatternEngine>());
        _dslEngine = new Lazy<BotCore.StrategyDsl.IStrategyKnowledgeGraph?>(() => 
            _serviceProvider.GetService<BotCore.StrategyDsl.IStrategyKnowledgeGraph>());
        _featureBus = new Lazy<Zones.IFeatureBus?>(() => 
            _serviceProvider.GetService<Zones.IFeatureBus>());
        _metricsService = new Lazy<BotCore.Services.RealTradingMetricsService?>(() => 
            _serviceProvider.GetService<BotCore.Services.RealTradingMetricsService>());
    }
    
    /// <summary>
    /// Process a bar through the unified pipeline - THE ONLY PATH for bar processing
    /// Flow: MarketStructureCore.Update → ZoneService.OnBar → PatternEngine.OnBar → DslEngine.Evaluate → FeatureBus.Publish
    /// </summary>
    public async Task<UnifiedBarProcessingResult> ProcessAsync(string symbol, Bar bar, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        if (bar == null)
            throw new ArgumentNullException(nameof(bar));
            
        var processingResult = new UnifiedBarProcessingResult
        {
            Symbol = symbol,
            Bar = bar,
            ProcessingStarted = DateTime.UtcNow,
            Success = false,
            PipelineSteps = new List<PipelineStepResult>()
        };
        
        try
        {
            _logger.LogDebug("Starting unified bar processing for {Symbol} at {Timestamp}", symbol, bar.Start);
            
            // Step 1: MarketStructureCore.Update
            var marketStructureResult = await ProcessMarketStructureUpdateAsync(symbol, bar, cancellationToken);
            processingResult.PipelineSteps.Add(marketStructureResult);
            
            if (!marketStructureResult.Success)
            {
                _logger.LogWarning("MarketStructureCore update failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 2: ZoneService.OnBar
            var zoneServiceResult = await ProcessZoneServiceOnBarAsync(symbol, bar, cancellationToken);
            processingResult.PipelineSteps.Add(zoneServiceResult);
            
            if (!zoneServiceResult.Success)
            {
                _logger.LogWarning("ZoneService OnBar failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 3: PatternEngine.OnBar
            var patternEngineResult = await ProcessPatternEngineOnBarAsync(symbol, bar, cancellationToken);
            processingResult.PipelineSteps.Add(patternEngineResult);
            
            if (!patternEngineResult.Success)
            {
                _logger.LogWarning("PatternEngine OnBar failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 4: DslEngine.Evaluate
            var dslEngineResult = await ProcessDslEngineEvaluateAsync(symbol, bar, cancellationToken);
            processingResult.PipelineSteps.Add(dslEngineResult);
            
            if (!dslEngineResult.Success)
            {
                _logger.LogWarning("DslEngine Evaluate failed for {Symbol} - stopping pipeline", symbol);
                return processingResult;
            }
            
            // Step 5: FeatureBus.Publish (pattern signals injection)
            var featureBusResult = await ProcessFeatureBusPublishAsync(symbol, bar, patternEngineResult, cancellationToken);
            processingResult.PipelineSteps.Add(featureBusResult);
            
            // Final success determination
            processingResult.Success = featureBusResult.Success;
            processingResult.ProcessingCompleted = DateTime.UtcNow;
            processingResult.ProcessingTimeMs = (processingResult.ProcessingCompleted - processingResult.ProcessingStarted).TotalMilliseconds;
            
            // Update telemetry
            Interlocked.Increment(ref _barsProcessed);
            _lastBarProcessed = DateTime.UtcNow;
            
            // Emit telemetry
            await EmitPipelineTelemetryAsync(processingResult, cancellationToken);
            
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
        catch (Exception ex)
        {
            Interlocked.Increment(ref _pipelineErrors);
            processingResult.ProcessingCompleted = DateTime.UtcNow;
            processingResult.ProcessingTimeMs = (processingResult.ProcessingCompleted - processingResult.ProcessingStarted).TotalMilliseconds;
            processingResult.Error = ex.Message;
            
            _logger.LogError(ex, "Critical error in unified bar pipeline for {Symbol}", symbol);
            return processingResult;
        }
    }
    
    /// <summary>
    /// Step 1: Process MarketStructureCore update
    /// </summary>
    private async Task<PipelineStepResult> ProcessMarketStructureUpdateAsync(string symbol, Bar bar, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "MarketStructureCore.Update",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var marketStructureCore = _marketStructureCore.Value;
            if (marketStructureCore != null)
            {
                await marketStructureCore.UpdateAsync(symbol, bar, cancellationToken);
                stepResult.Success = true;
                _logger.LogTrace("MarketStructureCore.Update completed for {Symbol}", symbol);
            }
            else
            {
                stepResult.Success = false;
                stepResult.Error = "MarketStructureCore service not available";
                _logger.LogWarning("MarketStructureCore service not registered in DI container");
            }
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Error in MarketStructureCore.Update for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 2: Process ZoneService OnBar
    /// </summary>
    private async Task<PipelineStepResult> ProcessZoneServiceOnBarAsync(string symbol, Bar bar, CancellationToken cancellationToken)
    {
        var stepResult = new PipelineStepResult 
        { 
            StepName = "ZoneService.OnBar",
            StartTime = DateTime.UtcNow 
        };
        
        try
        {
            var zoneService = _zoneService.Value;
            if (zoneService != null)
            {
                await zoneService.OnBarAsync(symbol, bar, cancellationToken);
                stepResult.Success = true;
                _logger.LogTrace("ZoneService.OnBar completed for {Symbol}", symbol);
            }
            else
            {
                stepResult.Success = false;
                stepResult.Error = "ZoneService not available";
                _logger.LogWarning("ZoneService not registered in DI container");
            }
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Error in ZoneService.OnBar for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 3: Process PatternEngine OnBar
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
            var patternEngine = _patternEngine.Value;
            if (patternEngine != null)
            {
                // Get current pattern scores for this bar
                var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken);
                stepResult.Success = true;
                stepResult.Data = new { BullScore = patternScores.BullScore, BearScore = patternScores.BearScore };
                _logger.LogTrace("PatternEngine.OnBar completed for {Symbol} - Bull: {BullScore}, Bear: {BearScore}", 
                    symbol, patternScores.BullScore, patternScores.BearScore);
            }
            else
            {
                stepResult.Success = false;
                stepResult.Error = "PatternEngine not available";
                _logger.LogWarning("PatternEngine not registered in DI container");
            }
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Error in PatternEngine.OnBar for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 4: Process DslEngine Evaluate
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
            var dslEngine = _dslEngine.Value;
            if (dslEngine != null)
            {
                var recommendations = await dslEngine.EvaluateAsync(symbol, bar.Start, cancellationToken);
                stepResult.Success = true;
                stepResult.Data = new { RecommendationCount = recommendations.Count };
                _logger.LogTrace("DslEngine.Evaluate completed for {Symbol} - {RecommendationCount} recommendations", 
                    symbol, recommendations.Count);
            }
            else
            {
                stepResult.Success = false;
                stepResult.Error = "DslEngine not available";
                _logger.LogWarning("DslEngine not registered in DI container");
            }
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Error in DslEngine.Evaluate for {Symbol}", symbol);
        }
        finally
        {
            stepResult.EndTime = DateTime.UtcNow;
            stepResult.DurationMs = (stepResult.EndTime - stepResult.StartTime).TotalMilliseconds;
        }
        
        return stepResult;
    }
    
    /// <summary>
    /// Step 5: Process FeatureBus Publish (pattern signals injection)
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
            var featureBus = _featureBus.Value;
            if (featureBus != null && patternEngineResult.Success && patternEngineResult.Data != null)
            {
                // Publish pattern signals to feature bus
                var patternData = patternEngineResult.Data as dynamic;
                if (patternData != null)
                {
                    featureBus.Publish(symbol, bar.Start, "pattern.bull_score", patternData.BullScore);
                    featureBus.Publish(symbol, bar.Start, "pattern.bear_score", patternData.BearScore);
                }
                
                // Publish bar completion signal
                featureBus.Publish(symbol, bar.Start, "bar.processed", 1.0);
                
                stepResult.Success = true;
                _logger.LogTrace("FeatureBus.Publish completed for {Symbol}", symbol);
            }
            else
            {
                stepResult.Success = false;
                stepResult.Error = "FeatureBus not available or pattern data missing";
                _logger.LogWarning("FeatureBus not registered or pattern engine data unavailable");
            }
            
            await Task.CompletedTask; // Satisfy async signature
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            stepResult.Error = ex.Message;
            _logger.LogError(ex, "Error in FeatureBus.Publish for {Symbol}", symbol);
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
    private async Task EmitPipelineTelemetryAsync(UnifiedBarProcessingResult result, CancellationToken cancellationToken)
    {
        try
        {
            var metricsService = _metricsService.Value;
            if (metricsService != null)
            {
                var tags = new Dictionary<string, string>
                {
                    ["symbol"] = result.Symbol,
                    ["success"] = result.Success.ToString().ToLowerInvariant()
                };
                
                // Emit pipeline metrics
                await metricsService.RecordGaugeAsync("unified_pipeline.processing_time_ms", result.ProcessingTimeMs, tags, cancellationToken);
                await metricsService.RecordCounterAsync("unified_pipeline.bars_processed", 1, tags, cancellationToken);
                
                // Emit step-level metrics
                foreach (var step in result.PipelineSteps)
                {
                    var stepTags = new Dictionary<string, string>(tags)
                    {
                        ["step"] = step.StepName,
                        ["step_success"] = step.Success.ToString().ToLowerInvariant()
                    };
                    
                    await metricsService.RecordGaugeAsync("unified_pipeline.step_duration_ms", step.DurationMs, stepTags, cancellationToken);
                }
                
                // Emit cumulative metrics
                await metricsService.RecordGaugeAsync("unified_pipeline.total_bars_processed", _barsProcessed, cancellationToken);
                await metricsService.RecordGaugeAsync("unified_pipeline.total_errors", _pipelineErrors, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitting pipeline telemetry for {Symbol}", result.Symbol);
        }
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
            IsHealthy = _pipelineErrors == 0 || (_barsProcessed > 0 && (double)_pipelineErrors / _barsProcessed < 0.01) // < 1% error rate
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
    public DateTime ProcessingCompleted { get; set; }
    public double ProcessingTimeMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<PipelineStepResult> PipelineSteps { get; set; } = new();
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