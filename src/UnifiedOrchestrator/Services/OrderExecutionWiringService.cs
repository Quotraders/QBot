using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using global::BotCore.Services;

namespace TradingBot.UnifiedOrchestrator.Services;

/// <summary>
/// Wiring service to connect TopstepXAdapterService fill events to OrderExecutionService
/// This service establishes the event subscription when the application starts
/// </summary>
internal class OrderExecutionWiringService : IHostedService
{
    private readonly ILogger<OrderExecutionWiringService> _logger;
    private readonly ITopstepXAdapterService _topstepAdapter;
    private readonly IOrderService _orderService;

    public OrderExecutionWiringService(
        ILogger<OrderExecutionWiringService> logger,
        ITopstepXAdapterService topstepAdapter,
        IOrderService orderService)
    {
        _logger = logger;
        _topstepAdapter = topstepAdapter;
        _orderService = orderService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔌 [WIRING] Connecting fill event subscription...");

            // Wire up the fill event subscription
            // When TopstepXAdapterService receives a fill event, it will notify OrderExecutionService
            if (_topstepAdapter is TopstepXAdapterService adapter && 
                _orderService is OrderExecutionService orderExecService)
            {
                adapter.SubscribeToFillEvents(fillData =>
                {
                    try
                    {
                        orderExecService.OnOrderFillReceived(fillData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing fill event in OrderExecutionService");
                    }
                });

                _logger.LogInformation("✅ [WIRING] Fill event subscription established: TopstepXAdapter → OrderExecutionService");
                _logger.LogInformation("📡 [WIRING] Fill events from TopstepX will now automatically update orders and positions");
            }
            else
            {
                _logger.LogWarning("⚠️ [WIRING] Could not establish fill event subscription - service types do not match expected concrete types");
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WIRING] Failed to establish fill event subscription");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔌 [WIRING] OrderExecutionWiringService stopping...");
        return Task.CompletedTask;
    }
}
