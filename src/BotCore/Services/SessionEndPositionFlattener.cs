using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Abstractions;
using TopstepX.Bot.Core.Services;
using BotCore.Models;

namespace BotCore.Services
{
    /// <summary>
    /// Session-End Position Flattener - Production Trading Bot
    /// 
    /// CRITICAL PRODUCTION SERVICE - Automatically closes all positions before market close:
    /// - Runs every 30 seconds monitoring Eastern Time
    /// - Flattens all positions at 4:55 PM ET (configurable)
    /// - Monday-Thursday: Always flatten (daily maintenance)
    /// - Friday: Configurable (weekend safety vs. weekend holds)
    /// - Saturday-Sunday: Skip (market closed)
    /// 
    /// Works with:
    /// - SessionAwareRuntimeGates for Eastern Time
    /// - PositionTrackingSystem for open positions
    /// - IOrderService for position closes
    /// - UnifiedPositionManagementService for position unregistration
    /// </summary>
    public sealed class SessionEndPositionFlattener : BackgroundService
    {
        private readonly ILogger<SessionEndPositionFlattener> _logger;
        private readonly IConfiguration _configuration;
        private readonly SessionAwareRuntimeGates _sessionGates;
        private readonly PositionTrackingSystem _positionTracker;
        private readonly IServiceProvider _serviceProvider;
        private readonly UnifiedPositionManagementService _positionManagement;
        
        // Monitoring interval - check every 30 seconds
        private const int MonitoringIntervalSeconds = 30;
        
        // Default configuration values
        private const int DefaultFlattenMinutesBefore = 5;  // 5 minutes before 5:00 PM = 4:55 PM
        private const int MarketCloseHour = 17;             // 5:00 PM ET
        private const int MarketCloseMinute = 0;
        
        // Daily reset tracking
        private bool _alreadyFlattenedToday;
        private DateTime _lastResetDate = DateTime.MinValue;
        
        public SessionEndPositionFlattener(
            ILogger<SessionEndPositionFlattener> logger,
            IConfiguration configuration,
            SessionAwareRuntimeGates sessionGates,
            PositionTrackingSystem positionTracker,
            IServiceProvider serviceProvider,
            UnifiedPositionManagementService positionManagement)
        {
            _logger = logger;
            _configuration = configuration;
            _sessionGates = sessionGates;
            _positionTracker = positionTracker;
            _serviceProvider = serviceProvider;
            _positionManagement = positionManagement;
            
            _logger.LogInformation("🔄 [SESSION-FLATTEN] Session-End Position Flattener initialized");
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🎯 [SESSION-FLATTEN] Session-End Position Flattener starting...");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorAndFlattenPositionsAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [SESSION-FLATTEN] Error in position flatten monitoring loop");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(MonitoringIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            
            _logger.LogInformation("🛑 [SESSION-FLATTEN] Session-End Position Flattener stopping");
        }
        
        /// <summary>
        /// Main monitoring loop - checks time and flattens positions if needed
        /// </summary>
        private async Task MonitorAndFlattenPositionsAsync(CancellationToken cancellationToken)
        {
            // Check if feature is enabled
            var autoFlattenEnabled = _configuration.GetValue<bool>("BOT_SESSION_AUTO_FLATTEN", true);
            if (!autoFlattenEnabled)
            {
                return; // Feature disabled, skip monitoring
            }
            
            // Get current Eastern Time from SessionAwareRuntimeGates
            var sessionStatus = await _sessionGates.GetSessionStatusAsync(null, cancellationToken).ConfigureAwait(false);
            var currentEt = sessionStatus.EasternTime;
            
            // Reset daily flag at midnight Eastern Time
            if (currentEt.Date != _lastResetDate)
            {
                _alreadyFlattenedToday = false;
                _lastResetDate = currentEt.Date;
                _logger.LogInformation("🔄 [SESSION-FLATTEN] Daily reset completed for {Date}", currentEt.Date.ToShortDateString());
            }
            
            // Check day of week logic
            var dayOfWeek = currentEt.DayOfWeek;
            
            // Saturday and Sunday - skip all checks (market closed)
            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            {
                return;
            }
            
            // Friday - check configuration
            if (dayOfWeek == DayOfWeek.Friday)
            {
                var flattenFridayEnabled = _configuration.GetValue<bool>("BOT_SESSION_FLATTEN_FRIDAY_ENABLED", true);
                if (!flattenFridayEnabled)
                {
                    return; // Friday flatten disabled, allow weekend holds
                }
            }
            
            // Calculate target flatten time (default 4:55 PM = 5:00 PM - 5 minutes)
            var minutesBefore = _configuration.GetValue<int>("BOT_SESSION_FLATTEN_MINUTES_BEFORE", DefaultFlattenMinutesBefore);
            var targetTime = new DateTime(currentEt.Year, currentEt.Month, currentEt.Day, MarketCloseHour, MarketCloseMinute, 0, DateTimeKind.Unspecified)
                .AddMinutes(-minutesBefore);
            
            // Check if current time is within 1 minute window of target time
            var timeDifference = Math.Abs((currentEt - targetTime).TotalMinutes);
            if (timeDifference > 1.0)
            {
                return; // Not in flatten window
            }
            
            // Check if already flattened today
            if (_alreadyFlattenedToday)
            {
                return; // Already flattened, prevent duplicate runs
            }
            
            // In flatten window and not yet flattened - execute flatten
            _logger.LogWarning("⏰ [SESSION-FLATTEN] Flatten window triggered at {Time} ET (target: {Target})",
                currentEt.ToString("HH:mm:ss", CultureInfo.InvariantCulture), targetTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            
            await FlattenAllPositionsAsync(currentEt).ConfigureAwait(false);
            
            // Set flag to prevent duplicate runs today
            _alreadyFlattenedToday = true;
        }
        
        /// <summary>
        /// Flatten all open positions before market close
        /// </summary>
        private async Task FlattenAllPositionsAsync(DateTime currentEt)
        {
            _logger.LogWarning("🚨 [SESSION-FLATTEN] Session-end flatten triggered at {Time} ET", currentEt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            
            // Get all currently open positions from PositionTrackingSystem
            var positions = _positionTracker.GetAllPositions();
            var openPositions = positions.Values.Where(p => p.NetQuantity != 0).ToList();
            
            // Check if there are any positions to flatten
            if (openPositions.Count == 0)
            {
                _logger.LogInformation("✅ [SESSION-FLATTEN] No positions to flatten");
                return;
            }
            
            _logger.LogWarning("📊 [SESSION-FLATTEN] Found {Count} open position(s) to flatten", openPositions.Count);
            
            var successCount = 0;
            var failCount = 0;
            
            // Get IOrderService from service provider (may be null)
            var orderService = _serviceProvider.GetService<IOrderService>();
            if (orderService == null)
            {
                _logger.LogWarning("⚠️ [SESSION-FLATTEN] IOrderService not available, cannot flatten positions");
                return;
            }
            
            // Loop through each position and close it
            foreach (var position in openPositions)
            {
                try
                {
                    // Generate position ID from symbol for tracking
                    var positionId = position.Symbol;
                    
                    _logger.LogWarning("🔄 [SESSION-FLATTEN] Closing position for symbol {Symbol} (qty: {Qty})",
                        position.Symbol, position.NetQuantity);
                    
                    // Call IOrderService to close the position
                    var success = await orderService.ClosePositionAsync(positionId).ConfigureAwait(false);
                    
                    if (success)
                    {
                        _logger.LogInformation("✅ [SESSION-FLATTEN] Successfully closed position {PositionId}", positionId);
                        
                        // Unregister position from UnifiedPositionManagementService
                        _positionManagement.UnregisterPosition(positionId, ExitReason.SessionEnd);
                        
                        successCount++;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [SESSION-FLATTEN] Failed to close position {PositionId}: Close operation returned false",
                            positionId);
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [SESSION-FLATTEN] Error closing position {Symbol}", position.Symbol);
                    failCount++;
                }
            }
            
            // Log summary
            _logger.LogWarning("📊 [SESSION-FLATTEN] Flattened {Success} of {Total} positions successfully (failed: {Failed})",
                successCount, openPositions.Count, failCount);
        }
    }
}
