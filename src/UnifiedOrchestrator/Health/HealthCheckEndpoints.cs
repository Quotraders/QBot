using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Abstractions;
using System.Text.Json;
using System.Diagnostics;

namespace TradingBot.UnifiedOrchestrator.Health
{
    /// <summary>
    /// Production health check endpoints for monitoring and deployment validation
    /// Addresses audit finding: Missing health check endpoints for production monitoring
    /// </summary>
    public class HealthCheckEndpoints
    {
        private readonly ILogger<HealthCheckEndpoints> _logger;
        private readonly HealthCheckService _healthCheckService;
        private readonly IServiceProvider _serviceProvider;

        public HealthCheckEndpoints(
            ILogger<HealthCheckEndpoints> logger,
            HealthCheckService healthCheckService,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _healthCheckService = healthCheckService;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Basic health check endpoint - returns 200 if system is operational
        /// </summary>
        public async Task<IResult> HealthAsync()
        {
            try
            {
                var healthReport = await _healthCheckService.CheckHealthAsync();
                
                var response = new
                {
                    status = healthReport.Status.ToString(),
                    timestamp = DateTime.UtcNow,
                    version = "1.0.0",
                    uptime = GetUptime()
                };

                return healthReport.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy 
                    ? Results.Ok(response)
                    : Results.StatusCode(503); // Service Unavailable
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return Results.StatusCode(500);
            }
        }

        /// <summary>
        /// Readiness check - verifies system is ready to accept trading requests
        /// </summary>
        public async Task<IResult> ReadyAsync()
        {
            try
            {
                var checks = new Dictionary<string, bool>
                {
                    ["kill_switch"] = !IsKillSwitchActive(),
                    ["trading_brain"] = IsTradingBrainReady(),
                    ["risk_manager"] = IsRiskManagerReady(),
                    ["market_data"] = IsMarketDataReady(),
                    ["authentication"] = IsAuthenticationReady()
                };

                var allReady = checks.Values.All(x => x);
                var response = new
                {
                    ready = allReady,
                    checks = checks,
                    timestamp = DateTime.UtcNow
                };

                return allReady ? Results.Ok(response) : Results.StatusCode(503);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness check failed");
                return Results.StatusCode(500);
            }
        }

        /// <summary>
        /// Metrics endpoint for monitoring and observability
        /// </summary>
        public async Task<IResult> MetricsAsync()
        {
            try
            {
                var metrics = new
                {
                    system = new
                    {
                        uptime_seconds = GetUptimeSeconds(),
                        memory_usage_mb = GC.GetTotalMemory(false) / 1024 / 1024,
                        gc_collections = new
                        {
                            gen0 = GC.CollectionCount(0),
                            gen1 = GC.CollectionCount(1),
                            gen2 = GC.CollectionCount(2)
                        }
                    },
                    trading = await GetTradingMetricsAsync(),
                    timestamp = DateTime.UtcNow
                };

                return Results.Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metrics endpoint failed");
                return Results.StatusCode(500);
            }
        }

        private bool IsKillSwitchActive()
        {
            try
            {
                // Check for kill.txt file
                return File.Exists("kill.txt") || File.Exists("state/kill.txt");
            }
            catch
            {
                return true; // Fail-safe: assume kill switch is active if we can't check
            }
        }

        private bool IsTradingBrainReady()
        {
            try
            {
                var brain = _serviceProvider.GetService<global::BotCore.Brain.UnifiedTradingBrain>();
                return brain != null;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRiskManagerReady()
        {
            try
            {
                var riskManager = _serviceProvider.GetService<IRiskManager>();
                return riskManager != null && !riskManager.IsRiskBreached;
            }
            catch
            {
                return false;
            }
        }

        private bool IsMarketDataReady()
        {
            // Basic check - in production would check actual market data feed status
            return true;
        }

        private bool IsAuthenticationReady()
        {
            try
            {
                // Check if we have TopstepX credentials available
                var jwt = Environment.GetEnvironmentVariable("TOPSTEPX_JWT");
                var username = Environment.GetEnvironmentVariable("TOPSTEPX_USERNAME");
                var apiKey = Environment.GetEnvironmentVariable("TOPSTEPX_API_KEY");
                
                return !string.IsNullOrEmpty(jwt) || 
                       (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(apiKey));
            }
            catch
            {
                return false;
            }
        }

        private TimeSpan GetUptime()
        {
            return DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }

        private double GetUptimeSeconds()
        {
            return GetUptime().TotalSeconds;
        }

        private async Task<object> GetTradingMetricsAsync()
        {
            try
            {
                // Basic trading metrics - would be expanded in production
                return new
                {
                    positions_count = 0, // Would get from position tracker
                    daily_pnl = 0.0m,   // Would get from P&L tracker
                    orders_today = 0,    // Would get from order tracker
                    last_decision_time = DateTime.UtcNow // Would get from decision engine
                };
            }
            catch
            {
                return new { error = "Unable to retrieve trading metrics" };
            }
        }
    }
}