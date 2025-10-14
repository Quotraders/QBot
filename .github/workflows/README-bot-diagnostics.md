# 🤖 Bot Launch Diagnostics Workflow

## Overview

The `bot-launch-diagnostics.yml` workflow provides comprehensive diagnostics and logging for the trading bot when launched on a self-hosted runner. This workflow captures all startup information, console output, errors, and system diagnostics into organized artifacts that can be downloaded and analyzed.

## Purpose

This workflow solves the problem of not being able to see bot startup logs and behavior when running on self-hosted infrastructure. It captures:

- ✅ Complete console output from bot startup
- ✅ All error messages and stack traces
- ✅ System and environment information
- ✅ Structured JSON logs with parsed events
- ✅ Runtime metrics and performance data
- ✅ Configuration validation results

## How to Use

### 1. Manual Trigger via GitHub UI

1. Go to **Actions** tab in the GitHub repository
2. Select **"🤖 Bot Launch Diagnostics - Self-Hosted"** workflow
3. Click **"Run workflow"** button
4. Configure options:
   - **Runtime duration**: How long to run the bot (default: 5 minutes)
   - **Detailed logging**: Enable verbose logging (default: true)
5. Click **"Run workflow"** to start

### 2. Monitor Execution

Watch the workflow execution in real-time:
- Each step shows its progress with detailed output
- Console output is streamed to the GitHub Actions UI
- Last 150 lines of bot output are displayed in the UI

### 3. Download Diagnostics

After the workflow completes:

1. Scroll to the **"Artifacts"** section at the bottom of the workflow run
2. Download **"bot-diagnostics-run-{number}"** artifact (ZIP file)
3. Extract the ZIP to review captured diagnostics

## Artifact Contents

The diagnostics artifact contains the following files:

### `system-info.json`
System and environment information captured before bot launch:
```json
{
  "timestamp": "2025-10-14T22:30:00Z",
  "runner": {
    "name": "self-hosted-runner",
    "os": "Windows",
    "arch": "X64",
    "hostname": "TRADING-PC",
    "user": "trader"
  },
  "dotnet": {
    "version": "8.0.403"
  },
  "workflow": {
    "name": "Bot Launch Diagnostics",
    "run_id": "12345",
    "run_number": "42",
    "triggered_by": "user"
  }
}
```

### `console-output-{timestamp}.log`
Complete console output from the bot, including:
- Startup sequence messages
- Service initialization logs
- API connection status
- Configuration validation
- Runtime operations
- All console output until shutdown

### `error-output-{timestamp}.log`
All error stream output:
- Exception messages
- Stack traces
- Critical errors
- Warning messages

### `structured-log-{timestamp}.json`
Parsed and structured log with key events:
```json
{
  "launch_timestamp": "2025-10-14T22:30:00Z",
  "end_timestamp": "2025-10-14T22:35:00Z",
  "runtime_minutes": "5",
  "actual_runtime_seconds": 300.45,
  "dry_run_enabled": true,
  "exit_code": 0,
  "exit_reason": "timeout",
  "events": [
    {
      "timestamp": "2025-10-14T22:30:01.234Z",
      "type": "stdout",
      "message": "🚀 [STARTUP] Starting unified orchestrator..."
    },
    {
      "timestamp": "2025-10-14T22:30:02.567Z",
      "type": "stdout", 
      "message": "✅ [STARTUP] Service validation completed"
    }
  ]
}
```

## Configuration Options

### Runtime Duration
- **Parameter**: `runtime_minutes`
- **Default**: 5 minutes
- **Range**: 1-30 minutes
- **Purpose**: How long the bot should run before graceful shutdown

Longer durations allow you to:
- Observe multiple trading cycles
- Capture steady-state operation
- See connection stability over time

### Detailed Logging
- **Parameter**: `capture_detailed_logs`
- **Default**: true
- **Purpose**: Enable verbose logging for troubleshooting

When enabled, additional diagnostic information is captured.

## Safety Features

The workflow includes multiple safety mechanisms:

### 1. Environment Variable Loading
- **Automatically loads** `.env` file before bot launch
- Ensures all configuration from `.env` is available
- Validates required credentials (TOPSTEPX_API_KEY, USERNAME, ACCOUNT_ID)
- Uses same loading logic as production scripts for consistency
- Environment variables are loaded in both validation and launch steps

### 2. DRY_RUN Mode
- **Always enabled** during diagnostic runs (overrides `.env` settings)
- Prevents real trading operations
- Simulates order execution
- Safe for testing and diagnostics

### 3. Timeout Protection
- Automatic shutdown after configured runtime
- Prevents runaway processes
- Graceful termination
- Resource cleanup

### 3. Error Isolation
- `continue-on-error: true` on bot launch step
- Ensures artifacts are always uploaded
- Captures errors for analysis
- Never loses diagnostic data

## Use Cases

### Troubleshooting Startup Issues
When the bot fails to start or crashes during initialization:
1. Run workflow with default 5-minute runtime
2. Download artifacts
3. Review `console-output-*.log` for error messages
4. Check `structured-log-*.json` for event timeline
5. Analyze `error-output-*.log` for exceptions

### Validating Configuration Changes
After modifying configuration or environment:
1. Run workflow to verify startup succeeds
2. Review system-info.json to confirm environment
3. Check console logs for expected behavior
4. Validate service initialization sequence

### Performance Analysis
To understand bot startup performance:
1. Run workflow multiple times
2. Compare `actual_runtime_seconds` across runs
3. Review event timestamps in structured logs
4. Identify slow initialization steps

### Documentation and Sharing
To share bot behavior with team or support:
1. Run workflow to capture current state
2. Download artifact ZIP
3. Share complete diagnostic package
4. Reviewers can see exact startup sequence

## Reading the Logs

### Console Output Analysis

Look for key startup markers:
```
🚀 [STARTUP] Starting unified orchestrator...
✅ [STARTUP] Service validation completed
⚙️ [STARTUP] Initializing ML parameter provider...
✅ [STARTUP] ML parameter provider initialized
```

Common error patterns:
```
❌ CRITICAL ERROR: ...
⚠️ Warning: ...
Exception: ...
```

### Structured Log Analysis

The structured log provides:
- Exact timestamps for each event
- Event type (stdout/stderr)
- Complete event timeline
- Runtime metrics

Use JSON tools to query:
```bash
# Count events by type
jq '.events | group_by(.type) | map({type: .[0].type, count: length})' structured-log-*.json

# Find error events
jq '.events[] | select(.message | contains("ERROR"))' structured-log-*.json

# Show first 10 events
jq '.events[:10]' structured-log-*.json
```

## Troubleshooting

### Environment Variables Show as [MISSING]
**Problem**: The diagnostic workflow shows `❌ TOPSTEPX_API_KEY: [MISSING]` even though the `.env` file exists and contains the credentials.

**Solution**: As of the latest update, the workflow now automatically loads environment variables from the `.env` file before validation. This issue has been fixed. If you still see missing variables:
- Ensure the `.env` file exists in the repository root
- Verify the environment variables use UPPERCASE names (e.g., `TOPSTEPX_API_KEY=...`)
- Check that lines are not commented out with `#`
- Confirm variables follow the format: `VARIABLE_NAME=value` (no spaces around `=`)

**How it works**: The workflow uses the same `.env` loading logic as `run-bot-wsl.ps1`:
```powershell
Get-Content ".env" | Where-Object { $_ -match '^[A-Z]' -and $_ -notmatch '^#' } | ForEach-Object {
  if ($_ -match '^([A-Z_]+)=(.*)$') {
    $key = $matches[1]
    $value = $matches[2]
    Set-Item -Path "env:$key" -Value $value
  }
}
```

### Workflow Not Appearing
- Ensure you have a self-hosted runner configured
- Check runner is online and accepting jobs
- Verify workflow file is in `.github/workflows/`

### Workflow Fails Immediately
- Check runner connectivity
- Verify .NET SDK is installed on runner
- Ensure repository is accessible from runner

### No Artifacts Uploaded
- Check workflow completed (even with errors)
- Verify `upload-artifact` step executed
- Review step logs for upload errors

### Large Artifact Size
- Reduce runtime_minutes to capture less data
- Disable detailed logging if not needed
- Artifacts auto-expire after 30 days

## Integration with Bot Development

This workflow integrates with the existing bot infrastructure:

- Uses same build process as production
- Respects all safety guardrails (DRY_RUN, kill.txt, etc.)
- Follows production logging patterns
- Compatible with PathConfigService configuration

## Retention and Cleanup

- **Artifact Retention**: 30 days
- **Automatic Cleanup**: GitHub removes after retention period
- **Storage**: Stored in GitHub Actions artifact storage
- **Download Limit**: Can be downloaded multiple times during retention

## Future Enhancements

Potential improvements for this workflow:

- [ ] Add automated log analysis with pattern detection
- [ ] Create summary report with key metrics
- [ ] Support multiple bot configurations in one run
- [ ] Add comparison mode to diff against previous runs
- [ ] Integrate with issue tracking for automatic error reporting
- [ ] Add performance benchmarking
- [ ] Create dashboard visualization of logs

## Support

If you encounter issues with this workflow:

1. Check the workflow logs in GitHub Actions UI
2. Review this README for configuration guidance
3. Download artifacts to analyze locally
4. Check self-hosted runner status
5. Verify .NET environment on runner

## Related Files

- `.github/workflows/bot-launch-diagnostics.yml` - Main workflow definition
- `.github/workflows/selfhosted-bot-run.yml` - Existing bot execution workflow
- `.github/workflows/selfhosted-test.yml` - Runner connectivity test
- `src/UnifiedOrchestrator/Program.cs` - Bot entry point
- `src/BotCore/Services/PathConfigService.cs` - Path configuration
