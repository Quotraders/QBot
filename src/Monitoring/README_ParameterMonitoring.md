# Parameter Performance Monitoring & Rollback System

## Overview

The Parameter Performance Monitor is a background service that continuously tracks live trading performance and automatically rolls back to previous parameters if performance degrades significantly.

## How It Works

```
┌──────────────────────────────────────────────────────────────────┐
│ Every Hour During CME Futures Market Hours                       │
│ (Sun 6 PM - Fri 5 PM ET, excluding daily 5-6 PM maintenance)    │
└────────────────────┬─────────────────────────────────────────────┘
                     │
┌────────────────────▼─────────────────────────────────────────────┐
│ 1. Load Baseline Sharpe from Parameter JSON                      │
│    - Read artifacts/current/parameters/S2_parameters.json         │
│    - Extract "optimized_sharpe" value                            │
└────────────────────┬─────────────────────────────────────────────┘
                     │
┌────────────────────▼─────────────────────────────────────────────┐
│ 2. Calculate Current Rolling Sharpe (Last 3 Days)               │
│    - Get all trades from last 3 trading days                    │
│    - Calculate: Sharpe = (mean return / std return) * √252      │
└────────────────────┬─────────────────────────────────────────────┘
                     │
┌────────────────────▼─────────────────────────────────────────────┐
│ 3. Compare Performance                                           │
│    - Degradation % = (Baseline - Current) / Baseline            │
└────────────────────┬─────────────────────────────────────────────┘
                     │
        ┌────────────┴────────────┐
        │                         │
┌───────▼────────┐     ┌──────────▼──────────┐
│ Degradation    │     │ Performance OK      │
│ > 20%          │     │ or < 20%           │
└───────┬────────┘     └──────────┬──────────┘
        │                         │
┌───────▼────────┐     ┌──────────▼──────────┐
│ Increment      │     │ Reset Degradation   │
│ Consecutive    │     │ Counter to 0        │
│ Day Counter    │     └─────────────────────┘
└───────┬────────┘
        │
        │ Day 1, 2... Keep monitoring
        │
┌───────▼────────────────────────────────┐
│ Day 3: Counter >= 3                    │
│ TRIGGER AUTOMATIC ROLLBACK             │
└───────┬────────────────────────────────┘
        │
┌───────▼────────────────────────────────┐
│ 4. Perform Rollback                    │
│    • Backup current → rollback/        │
│    • Copy previous → current           │
│    • Set env var ROLLBACK_ACTIVE=true  │
│    • Log event to rollback/events/     │
│    • Reset counter                     │
└────────────────────────────────────────┘
```

## Configuration

### Thresholds (Configurable in ParameterPerformanceMonitor.cs)

```csharp
private const double DegradationThreshold = 0.20;      // 20% drop triggers rollback
private const int ConsecutiveDaysThreshold = 3;        // Must see degradation for 3 days
private const int RollingDays = 3;                     // Calculate metrics over last 3 days
private const int CheckIntervalMinutes = 60;           // Run every hour
```

### Adjusting Thresholds

Edit `src/Monitoring/ParameterPerformanceMonitor.cs`:

```csharp
// More aggressive rollback (trigger sooner)
private const double DegradationThreshold = 0.15;  // 15% drop
private const int ConsecutiveDaysThreshold = 2;    // 2 consecutive days

// More conservative rollback (give parameters more time)
private const double DegradationThreshold = 0.25;  // 25% drop
private const int ConsecutiveDaysThreshold = 5;    // 5 consecutive days
```

## Integration with Program.cs

Add the service to your DI container in `src/UnifiedOrchestrator/Program.cs`:

```csharp
// Register Parameter Performance Monitor (Phase 6 - Monitoring & Rollback)
services.AddHostedService<TradingBot.Monitoring.ParameterPerformanceMonitor>();
```

This should be added after other hosted services, typically near line 650-660.

## Trade Tracking

The monitoring system needs trade data to calculate performance. Your trading engine should call `TrackTrade()` after each trade completes:

```csharp
// In your trading execution code
public class TradingEngine
{
    private readonly ParameterPerformanceMonitor _monitor;
    
    public async Task ExecuteTradeAsync(string strategy, Trade trade)
    {
        // ... execute trade ...
        
        // Track trade for monitoring
        var returnPct = (trade.ExitPrice - trade.EntryPrice) / trade.EntryPrice;
        _monitor.TrackTrade(strategy, returnPct, DateTime.UtcNow);
    }
}
```

## Directory Structure

```
artifacts/
├── current/
│   └── parameters/         # Active parameters loaded by strategies
│       ├── S2_parameters.json
│       ├── S3_parameters.json
│       ├── S6_parameters.json
│       └── S11_parameters.json
│
├── previous/
│   └── parameters/         # Backup of last good parameters (for rollback)
│       ├── S2_parameters.json
│       ├── S3_parameters.json
│       ├── S6_parameters.json
│       └── S11_parameters.json
│
├── stage/
│   └── parameters/         # Newly optimized parameters (pending promotion)
│       └── S2_parameters.json
│
└── rollback/
    ├── parameters/         # Failed parameters (archived with timestamp)
    │   └── S2_parameters_failed_20250115_143022.json
    └── events/            # Rollback event logs
        └── rollback_S2_20250115_143022.json
```

## Rollback Event Log Format

When a rollback occurs, an event is logged to `artifacts/rollback/events/`:

```json
{
  "timestamp": "2025-01-15T14:30:22.123Z",
  "strategy": "S2",
  "baseline_sharpe": 1.63,
  "current_sharpe": 1.12,
  "degradation_pct": 0.3128,
  "consecutive_days": 3,
  "action": "AUTOMATIC_ROLLBACK"
}
```

## Environment Variables

After rollback, these environment variables are set:

```bash
PARAMETER_ROLLBACK_ACTIVE=true
PARAMETER_ROLLBACK_S2=2025-01-15T14:30:22.123Z
```

Check these in your code if needed:

```csharp
if (Environment.GetEnvironmentVariable("PARAMETER_ROLLBACK_ACTIVE") == "true")
{
    // Rollback occurred - consider additional safety measures
    var s2RollbackTime = Environment.GetEnvironmentVariable("PARAMETER_ROLLBACK_S2");
    _logger.LogWarning("S2 parameters were rolled back at {Time}", s2RollbackTime);
}
```

## Monitoring Logs

The service logs to the standard logging system with prefix `[PARAM-MONITOR]`:

```
[2025-01-15 14:25:00] [INFO] [PARAM-MONITOR] Checking S2 performance
[2025-01-15 14:25:00] [INFO] [PARAM-MONITOR] S2 - Baseline Sharpe: 1.630, Current Sharpe: 1.450
[2025-01-15 14:25:00] [WARN] [PARAM-MONITOR] S2 degraded by 11.0% (day 1/3)
```

When degradation reaches threshold:

```
[2025-01-15 14:30:22] [ERROR] [PARAM-MONITOR] ROLLBACK TRIGGERED for S2 after 3 consecutive days
[2025-01-15 14:30:22] [CRITICAL] [PARAM-MONITOR] ========================================
AUTOMATIC ROLLBACK TRIGGERED
========================================
Strategy: S2
Baseline Sharpe: 1.630
Current Sharpe: 1.120
Degradation: 31.3%
========================================
[2025-01-15 14:30:22] [INFO] [PARAM-MONITOR] Backed up failed parameters
[2025-01-15 14:30:22] [INFO] [PARAM-MONITOR] Rolled back S2 parameters from previous version
[2025-01-15 14:30:22] [CRITICAL] [PARAM-MONITOR] ROLLBACK COMPLETE for S2
```

## Testing

### Manual Test

1. **Simulate Trade Data:**
```csharp
var monitor = serviceProvider.GetRequiredService<ParameterPerformanceMonitor>();

// Simulate 10 losing trades (degradation)
for (int i = 0; i < 10; i++)
{
    monitor.TrackTrade("S2", -0.02, DateTime.UtcNow.AddHours(-i));
}
```

2. **Wait for Check Interval:**
   - Monitor runs every hour during CME futures market hours (Sun 6 PM - Fri 5 PM ET, excluding daily 5-6 PM maintenance)
   - Or manually trigger by restarting service

3. **Verify Logs:**
   - Check for degradation warnings
   - After 3 consecutive checks, verify rollback triggered

### Integration Test

```bash
# Build and run
dotnet run --project src/UnifiedOrchestrator

# Check logs
tail -f logs/unified_orchestrator.log | grep PARAM-MONITOR

# Verify rollback files created
ls -la artifacts/rollback/parameters/
ls -la artifacts/rollback/events/
```

## Alerting

### Email Alerts (Optional Enhancement)

Add email notification on rollback:

```csharp
private async Task TriggerRollbackAsync(...)
{
    // ... existing rollback code ...
    
    // Send email alert
    await SendEmailAlertAsync(
        "ALERT: Automatic Parameter Rollback",
        $"Strategy {strategy} rolled back due to {degradation:P1} performance drop"
    );
}
```

### Slack/Teams Webhooks

```csharp
private async Task SendSlackAlert(string strategy, double degradation)
{
    var webhookUrl = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
    var payload = new
    {
        text = $"🚨 ROLLBACK: {strategy} parameters rolled back ({degradation:P1} degradation)"
    };
    
    using var client = new HttpClient();
    var content = new StringContent(JsonSerializer.Serialize(payload));
    await client.PostAsync(webhookUrl, content);
}
```

## Troubleshooting

### Monitor Not Running

**Check registration:**
```bash
# Verify service is registered
grep "AddHostedService.*ParameterPerformanceMonitor" src/UnifiedOrchestrator/Program.cs
```

**Check logs:**
```bash
# Look for initialization message
grep "PARAM-MONITOR.*Starting" logs/*.log
```

### No Trade Data

**Issue:** Monitor cannot calculate Sharpe without trade data.

**Solution:** Ensure trading engine calls `TrackTrade()`:
```csharp
_monitor.TrackTrade(strategy, returnPct, DateTime.UtcNow);
```

### Rollback Not Triggering

**Check conditions:**
1. Degradation > 20% for 3 consecutive days
2. Market is open during checks (hourly 9:30 AM - 4:00 PM ET)
3. Sufficient trade data (minimum 2 trades in rolling window)
4. Previous parameters exist in `artifacts/previous/parameters/`

**Debug logs:**
```bash
# Enable debug logging in appsettings.json
{
  "Logging": {
    "LogLevel": {
      "TradingBot.Monitoring.ParameterPerformanceMonitor": "Debug"
    }
  }
}
```

### False Rollbacks

**Issue:** Parameters roll back too aggressively.

**Solution:** Adjust thresholds:
```csharp
// More conservative
private const double DegradationThreshold = 0.30;  // 30% drop required
private const int ConsecutiveDaysThreshold = 5;    // 5 days required
```

## Best Practices

1. **Always Keep Previous Parameters:** Never delete `artifacts/previous/parameters/`
2. **Review Rollback Events:** Check `artifacts/rollback/events/` weekly
3. **Monitor Logs:** Set up alerts for `[CRITICAL]` level messages
4. **Test After Deployment:** Verify service starts and tracks trades
5. **Adjust Thresholds:** Tune based on strategy volatility
6. **Archive Failed Parameters:** Keep `artifacts/rollback/parameters/` for analysis

## Recovery After Rollback

1. **Investigate Root Cause:**
   - Review failed parameters: `artifacts/rollback/parameters/`
   - Check market conditions during degradation
   - Analyze trades during failure period

2. **Re-optimize If Needed:**
   - Run training manually: `python src/Training/training_orchestrator.py`
   - Review optimization reports
   - Test new parameters in simulation first

3. **Manual Promotion:**
   ```bash
   # When confident new parameters are good
   cp artifacts/stage/parameters/*.json artifacts/current/parameters/
   
   # Clear rollback flag
   unset PARAMETER_ROLLBACK_ACTIVE
   ```

4. **Reset Monitoring:**
   - Service automatically resets after rollback
   - New baseline from rolled-back parameters
   - Fresh 3-day evaluation period

## Future Enhancements

- [ ] Email/SMS alerts on rollback
- [ ] Dashboard UI for monitoring status
- [ ] A/B testing framework (run both parameter sets)
- [ ] Gradual rollback (percentage-based allocation)
- [ ] Machine learning for anomaly detection
- [ ] Integration with strategy selection system
- [ ] Historical rollback analysis reports

## Support

For issues:
1. Check logs with `grep PARAM-MONITOR logs/*.log`
2. Verify directory structure: `tree artifacts/`
3. Test trade tracking manually
4. Review rollback events: `cat artifacts/rollback/events/*.json`
