using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using global::BotCore.Services;

namespace TradingBot.UnifiedOrchestrator.Services;

/// <summary>
/// PHASE 4 Step 9: Periodic execution metrics reporting and alerting
/// Logs hourly execution quality summaries and alerts on degraded performance
/// </summary>
internal class ExecutionMetricsReportingService : IHostedService, IDisposable
{
    private readonly ILogger<ExecutionMetricsReportingService> _logger;
    private readonly IOrderService _orderService;
    private Timer? _reportingTimer;
    private bool _disposed;
    
    // Symbols to monitor
    private readonly string[] _monitoredSymbols = { "ES", "NQ" };
    
    // Reporting interval: every hour
    private const int ReportingIntervalHours = 1;

    public ExecutionMetricsReportingService(
        ILogger<ExecutionMetricsReportingService> logger,
        IOrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 [METRICS-REPORTING] Starting execution metrics reporting service (interval: {Hours}h)", ReportingIntervalHours);
        
        // Start timer for hourly reporting
        _reportingTimer = new Timer(
            GenerateMetricsReport,
            null,
            TimeSpan.FromHours(ReportingIntervalHours),
            TimeSpan.FromHours(ReportingIntervalHours)
        );
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 [METRICS-REPORTING] Stopping execution metrics reporting service");
        _reportingTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void GenerateMetricsReport(object? state)
    {
        try
        {
            _logger.LogInformation("📊 [METRICS-REPORTING] Generating hourly execution quality report...");
            
            if (_orderService is not OrderExecutionService orderExecService)
            {
                _logger.LogWarning("⚠️ [METRICS-REPORTING] OrderService is not OrderExecutionService type - cannot generate report");
                return;
            }
            
            var hasAlerts = false;
            
            foreach (var symbol in _monitoredSymbols)
            {
                // Get execution quality report
                var report = orderExecService.GetExecutionQualityReport(symbol);
                
                // Check for quality threshold violations
                var qualityOk = orderExecService.CheckExecutionQualityThresholds(symbol, out var alertMessage);
                
                if (!qualityOk)
                {
                    // Alert on degraded execution quality
                    _logger.LogWarning(alertMessage);
                    hasAlerts = true;
                }
                
                // Log the full report
                _logger.LogInformation(report);
            }
            
            if (hasAlerts)
            {
                _logger.LogWarning(
                    "⚠️ [METRICS-REPORTING] EXECUTION QUALITY ALERTS DETECTED - Review metrics above for details");
            }
            else
            {
                _logger.LogInformation(
                    "✅ [METRICS-REPORTING] All monitored symbols have acceptable execution quality");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [METRICS-REPORTING] Error generating metrics report");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _reportingTimer?.Dispose();
            _disposed = true;
        }
    }
}
