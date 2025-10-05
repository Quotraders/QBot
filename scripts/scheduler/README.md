# Training Scheduler Setup Guide

This directory contains scripts for scheduling automated parameter optimization.

## Overview

The training scheduler runs weekly during market closed windows (Saturday 2:00 AM ET) to optimize trading strategy parameters using historical data.

## Prerequisites

1. **Python 3.8+** installed with Training dependencies
2. **API Credentials** set as environment variables:
   - `TOPSTEP_API_KEY`
   - `TOPSTEP_API_SECRET`
3. **System Requirements**:
   - Not running on VPN
   - Not in remote desktop session
   - DRY_RUN mode enabled (training safety)

## Windows Setup (Task Scheduler)

### 1. Create Scheduled Task

Open PowerShell as Administrator and run:

```powershell
$action = New-ScheduledTaskAction `
    -Execute "PowerShell.exe" `
    -Argument "-ExecutionPolicy Bypass -File C:\path\to\trading-bot-c-\scripts\scheduler\schedule_training.ps1" `
    -WorkingDirectory "C:\path\to\trading-bot-c-"

$trigger = New-ScheduledTaskTrigger `
    -Weekly `
    -DaysOfWeek Saturday `
    -At "2:00AM"

$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 30) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

$principal = New-ScheduledTaskPrincipal `
    -UserId "$env:USERNAME" `
    -LogonType Interactive `
    -RunLevel Highest

Register-ScheduledTask `
    -TaskName "Trading Bot Parameter Optimization" `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Weekly parameter optimization for trading strategies"
```

### 2. Set Environment Variables

In Task Scheduler, edit the task:
1. Go to **Actions** tab → **Edit**
2. Add to **Add arguments**:
   ```
   -ExecutionPolicy Bypass -Command "& {
       $env:TOPSTEP_API_KEY='your_api_key';
       $env:TOPSTEP_API_SECRET='your_api_secret';
       & 'C:\path\to\schedule_training.ps1'
   }"
   ```

### 3. Configure Task Conditions

In Task Scheduler, edit the task:
- **General** tab: ✓ Run with highest privileges
- **Conditions** tab:
  - ✓ Start only if the computer is on AC power
  - ✗ Stop if the computer switches to battery power
  - ✓ Wake the computer to run this task
- **Settings** tab:
  - ✓ Allow task to be run on demand
  - ✓ If the task fails, restart every: **30 minutes**
  - Attempt to restart up to: **3 times**
  - Stop the task if it runs longer than: **4 hours**

## Linux/Mac Setup (Cron)

### 1. Make Script Executable

```bash
chmod +x scripts/scheduler/schedule_training.sh
```

### 2. Set Environment Variables

Create or edit `~/.bash_profile` or `~/.bashrc`:

```bash
export TOPSTEP_API_KEY="your_api_key"
export TOPSTEP_API_SECRET="your_api_secret"
export ARTIFACTS_PATH="/path/to/trading-bot-c-/artifacts"
export LOGS_PATH="/path/to/trading-bot-c-/logs"
```

### 3. Add to Crontab

```bash
crontab -e
```

Add this line (Saturday 2:00 AM Eastern Time):

```cron
# Trading Bot Parameter Optimization (runs every Saturday at 2 AM ET)
0 2 * * 6 /path/to/trading-bot-c-/scripts/scheduler/schedule_training.sh >> /path/to/trading-bot-c-/logs/training_cron.log 2>&1
```

**Note:** Adjust time for your timezone. If server is in UTC and you want 2 AM ET:
- EST (winter): 2 AM ET = 7 AM UTC → `0 7 * * 6`
- EDT (summer): 2 AM ET = 6 AM UTC → `0 6 * * 6`

For automatic DST handling, use:
```bash
# Install tzdata if needed
TZ=America/New_York
0 2 * * 6 TZ=America/New_York /path/to/schedule_training.sh
```

### 4. Verify Cron Job

```bash
# List cron jobs
crontab -l

# Test script manually
./scripts/scheduler/schedule_training.sh
```

## Testing

### Manual Test Run

**Windows:**
```powershell
.\scripts\scheduler\schedule_training.ps1
```

**Linux/Mac:**
```bash
./scripts/scheduler/schedule_training.sh
```

### Verify Outputs

After running, check:

1. **Log file**: `logs/training_YYYYMMDD_HHMMSS.log`
2. **Dashboard**: `artifacts/reports/training_summary_YYYYMMDD.md`
3. **Parameters**: `artifacts/current/parameters/*_parameters.json`
4. **Backup**: `artifacts/previous/parameters/*_parameters.json`

## Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Saturday 2:00 AM ET - Trigger fires                          │
└───────────────────┬─────────────────────────────────────────────┘
                    │
┌───────────────────▼─────────────────────────────────────────────┐
│ 2. Safety Checks                                                 │
│    • Not on VPN                                                  │
│    • Not in RDP/SSH with X forwarding                           │
│    • DRY_RUN mode enabled                                        │
└───────────────────┬─────────────────────────────────────────────┘
                    │
┌───────────────────▼─────────────────────────────────────────────┐
│ 3. Run Python Training Orchestrator                             │
│    • Download 90 days of historical data                        │
│    • Optimize S2, S3, S6, S11 by session                        │
│    • Validate improvements (>10% Sharpe)                         │
│    • Save to artifacts/stage/parameters/                         │
└───────────────────┬─────────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        │                       │
┌───────▼────────┐     ┌────────▼─────────┐
│ 4a. SUCCESS    │     │ 4b. FAILURE      │
│                │     │                  │
│ • Backup       │     │ • Keep current   │
│   current →    │     │   parameters     │
│   previous     │     │ • Log error      │
│ • Promote      │     │ • Alert admin    │
│   stage →      │     │ • Exit 1         │
│   current      │     └──────────────────┘
│ • Generate     │
│   report       │
│ • Exit 0       │
└────────────────┘
```

## Troubleshooting

### Task Doesn't Run

**Windows:**
1. Check Task Scheduler event log: Event Viewer → Task Scheduler
2. Verify task is enabled: `Get-ScheduledTask -TaskName "Trading Bot Parameter Optimization"`
3. Check last run status: Task Scheduler → Task History

**Linux/Mac:**
1. Check cron service: `systemctl status cron` (Linux) or `sudo launchctl list | grep cron` (Mac)
2. Check system log: `grep CRON /var/log/syslog`
3. Verify PATH in cron: Add `PATH=/usr/local/bin:/usr/bin:/bin` to crontab

### Training Fails

1. Check log file: `logs/training_YYYYMMDD_HHMMSS.log`
2. Verify API credentials: `echo $TOPSTEP_API_KEY`
3. Test manually: `cd src/Training && python3 training_orchestrator.py`
4. Check Python dependencies: `pip install -r src/Training/requirements.txt`

### Parameters Not Promoted

1. Verify `artifacts/stage/parameters/` has JSON files
2. Check permissions on `artifacts/current/parameters/`
3. Review log for "Promoting optimized parameters" message
4. Ensure training exited with code 0 (success)

### Safety Checks Fail

1. **VPN Detected**: Disconnect VPN before training
2. **RDP Session**: Run from console session, not RDP
3. **DRY_RUN Disabled**: Set `DRY_RUN=1` or remove the check temporarily
4. **kill.txt Present**: Remove file or move to `kill.txt.bak`

## Monitoring

The scheduler creates these outputs for monitoring:

1. **Training Logs**: `logs/training_*.log` - Detailed execution logs
2. **Dashboard Reports**: `artifacts/reports/training_summary_*.md` - Success/failure summaries
3. **Exit Codes**:
   - `0` = Success (parameters promoted)
   - `1` = Failure (parameters NOT promoted)

Set up monitoring alerts based on exit codes or log patterns.

## Next Steps

After scheduler is running:

1. **Monitor First Run**: Check logs and dashboard after first Saturday run
2. **Review Parameters**: Compare `artifacts/current/` vs `artifacts/previous/`
3. **Enable C# Monitoring**: Register `ParameterPerformanceMonitor` in `Program.cs`
4. **Set Up Alerts**: Configure email/SMS for training failures
5. **Weekly Review**: Check performance reports every Monday

## Advanced Configuration

### Change Schedule

**Windows:**
```powershell
$task = Get-ScheduledTask -TaskName "Trading Bot Parameter Optimization"
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At "3:00AM"
Set-ScheduledTask -TaskName $task.TaskName -Trigger $trigger
```

**Linux/Mac:**
```bash
# Edit crontab
crontab -e

# Change to Sunday 3 AM: 0 3 * * 0 /path/to/schedule_training.sh
```

### Email Alerts

Add to end of script (before exit):

```powershell
# Windows PowerShell
if ($exitCode -ne 0) {
    Send-MailMessage `
        -From "trading-bot@example.com" `
        -To "admin@example.com" `
        -Subject "ALERT: Training Failed" `
        -Body (Get-Content $logFile -Raw) `
        -SmtpServer "smtp.example.com"
}
```

```bash
# Linux
if [ $EXIT_CODE -ne 0 ]; then
    mail -s "ALERT: Training Failed" admin@example.com < "$LOG_FILE"
fi
```

## Security Notes

- Never commit API keys to version control
- Use environment variables or secure key management
- Restrict scheduler script permissions (chmod 700)
- Review logs for sensitive information before sharing
- Rotate API keys periodically

## Support

For issues or questions:
1. Check troubleshooting section above
2. Review training logs in `logs/` directory
3. Test Python training manually: `python3 src/Training/training_orchestrator.py`
4. Verify C# integration: `dotnet run --project src/UnifiedOrchestrator`
