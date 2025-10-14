# 🎯 Bot Diagnostics - Practical Usage Examples

## Quick Start: Running Your First Diagnostic

### Step-by-Step Guide

1. **Navigate to GitHub Actions**
   ```
   Your Repository → Actions Tab → 🤖 Bot Launch Diagnostics - Self-Hosted
   ```

2. **Click "Run workflow"**
   - You'll see a dropdown with options
   - Leave defaults (5 minutes runtime, detailed logs enabled)
   - Click the green "Run workflow" button

3. **Watch it Run**
   - Workflow starts immediately
   - Real-time progress shown in UI
   - Takes ~6-7 minutes total (1 min setup + 5 min runtime)

4. **Download Results**
   - Scroll to bottom of workflow run page
   - Find "Artifacts" section
   - Click "bot-diagnostics-run-{number}" to download ZIP

5. **Analyze Results**
   - Extract ZIP file
   - Open `console-output-*.log` in text editor
   - Review startup sequence

## Example 1: Troubleshooting "Bot Won't Start"

### Scenario
Your bot crashes on startup and you need to know why.

### Steps
1. Run diagnostics workflow (5 min runtime is enough for startup)
2. Download artifacts
3. Open `error-output-*.log` first
4. Look for exception messages

### What You Might See

**Example Error Log:**
```
System.ArgumentNullException: Value cannot be null. (Parameter 'apiKey')
   at TopstepAuthAgent.TokenProvider..ctor(String apiKey, String username)
   at UnifiedOrchestrator.Program.ConfigureUnifiedServices
```

**Interpretation:**
- Missing API key in configuration
- Need to check `.env` file
- Set `TOPSTEPX_API_KEY` environment variable

### Fix
1. Update `.env` file with proper API key
2. Run diagnostics again to verify fix
3. Download artifacts to confirm startup succeeds

## Example 2: Configuration Validation

### Scenario
You updated environment variables and want to verify they're being picked up.

### Steps
1. Make changes to `.env` file
2. Run diagnostics workflow
3. Download artifacts
4. Open `system-info.json`

### What You See

**system-info.json excerpt:**
```json
{
  "timestamp": "2025-10-14T22:30:00Z",
  "runner": {
    "name": "trading-desktop",
    "os": "Windows",
    "hostname": "TRADING-PC"
  },
  "dotnet": {
    "version": "8.0.403"
  }
}
```

**console-output.log excerpt:**
```
📋 Environment Variables:
    ✅ TOPSTEPX_API_KEY: [SET]
    ✅ TOPSTEPX_USERNAME: [SET]
    ✅ TOPSTEPX_ACCOUNT_ID: [SET]
    ✅ DRY_RUN: [SET]
```

### Verification
- All required variables show `[SET]`
- System info shows correct runner
- No error messages about missing config

## Example 3: Performance Baseline

### Scenario
You want to establish baseline performance metrics for bot startup.

### Steps
1. Run diagnostics 3 times back-to-back
2. Download all 3 artifacts
3. Compare startup times

### Analysis

**structured-log-*.json from Run 1:**
```json
{
  "launch_timestamp": "2025-10-14T22:30:00.000Z",
  "end_timestamp": "2025-10-14T22:35:00.450Z",
  "actual_runtime_seconds": 300.45,
  "events": [
    {
      "timestamp": "2025-10-14T22:30:01.234Z",
      "message": "🚀 [STARTUP] Starting unified orchestrator..."
    },
    {
      "timestamp": "2025-10-14T22:30:03.567Z",
      "message": "✅ [STARTUP] Service validation completed"
    }
  ]
}
```

**Key Metrics:**
- Startup time: ~3.3 seconds (from launch to services ready)
- Total runtime: 300.45 seconds
- Events captured: 47 events

### Baseline Established
- Normal startup: < 5 seconds
- If future runs take > 10 seconds, investigate
- Compare event counts across runs

## Example 4: Sharing with Support Team

### Scenario
Bot has intermittent issues and you need to share exact state with teammate.

### Steps
1. Run diagnostics when issue occurs
2. Download artifacts ZIP
3. Share entire ZIP file via Slack/email
4. Teammate can reproduce exact environment

### What Teammate Sees

They extract ZIP and can review:
- **system-info.json**: Exact environment (OS, .NET version, runner)
- **console-output-*.log**: Complete startup sequence
- **structured-log-*.json**: Event timeline with timestamps
- **error-output-*.log**: Any errors that occurred

### Benefits
- No "it works on my machine" issues
- Exact reproduction of environment
- Complete context in single package
- Timestamps for correlation

## Example 5: Before/After Configuration Changes

### Scenario
Testing impact of configuration change on bot behavior.

### Steps
1. **Before**: Run diagnostics with current config
2. Download artifacts, rename to `before-change.zip`
3. **Make changes**: Update configuration files
4. **After**: Run diagnostics again
5. Download artifacts, rename to `after-change.zip`
6. **Compare**: Extract both and diff the logs

### Comparison Commands

```bash
# Extract both
unzip before-change.zip -d before/
unzip after-change.zip -d after/

# Compare console output
diff before/console-output-*.log after/console-output-*.log

# Compare event counts
jq '.events | length' before/structured-log-*.json
jq '.events | length' after/structured-log-*.json

# Find new error messages
grep "ERROR" after/console-output-*.log | \
  grep -v -F -f <(grep "ERROR" before/console-output-*.log)
```

## Example 6: Monitoring Startup Stability

### Scenario
Verify bot startup is consistent across multiple runs.

### Steps
1. Run diagnostics 5 times over 1 hour
2. Download all 5 artifacts
3. Compare startup times and success rates

### Analysis Script

```bash
#!/bin/bash
# analyze-stability.sh

for zip in bot-diagnostics-run-*.zip; do
  unzip -q "$zip" -d "temp-$$/"
  
  # Extract metrics
  runtime=$(jq -r '.actual_runtime_seconds' temp-$$/structured-log-*.json)
  exit_code=$(jq -r '.exit_code' temp-$$/structured-log-*.json)
  events=$(jq '.events | length' temp-$$/structured-log-*.json)
  
  echo "Run: $zip"
  echo "  Runtime: ${runtime}s"
  echo "  Exit Code: $exit_code"
  echo "  Events: $events"
  echo ""
  
  rm -rf "temp-$$/"
done
```

### Expected Output
```
Run: bot-diagnostics-run-42.zip
  Runtime: 300.45s
  Exit Code: 0
  Events: 47

Run: bot-diagnostics-run-43.zip
  Runtime: 300.52s
  Exit Code: 0
  Events: 47

Run: bot-diagnostics-run-44.zip
  Runtime: 300.38s
  Exit Code: 0
  Events: 47
```

### Stability Indicators
- ✅ Consistent runtime (< 1s variance)
- ✅ All exit code 0
- ✅ Same event count
- ✅ No new errors across runs

## Example 7: Long-Running Diagnostic

### Scenario
Need to observe bot behavior over 30 minutes to catch intermittent issue.

### Steps
1. Run workflow with custom parameters:
   - Runtime: 30 minutes
   - Detailed logs: enabled
2. Wait for completion
3. Download larger artifact set

### Configuration
```yaml
runtime_minutes: "30"
capture_detailed_logs: true
```

### Results
- Larger console log file (more data)
- More events captured in structured log
- Able to see steady-state behavior
- Can catch issues that only appear after several minutes

### Use Cases for Long Runs
- Memory leak detection
- Connection stability testing
- Performance degradation over time
- Event pattern analysis

## Example 8: Parsing Events Timeline

### Scenario
Need to understand exact sequence of events during startup.

### Analysis

**Extract event timeline:**
```bash
jq -r '.events[] | "\(.timestamp) [\(.type)] \(.message)"' structured-log-*.json
```

**Output:**
```
2025-10-14T22:30:00.123Z [stdout] 🚀 [STARTUP] Starting unified orchestrator...
2025-10-14T22:30:00.456Z [stdout] ⚙️ [STARTUP] Initializing ML parameter provider...
2025-10-14T22:30:01.789Z [stdout] ✅ [STARTUP] ML parameter provider initialized
2025-10-14T22:30:02.012Z [stdout] ✅ [STARTUP] Service validation completed
```

**Filter for errors only:**
```bash
jq -r '.events[] | select(.message | contains("ERROR")) | 
  "\(.timestamp) \(.message)"' structured-log-*.json
```

**Count events by type:**
```bash
jq '.events | group_by(.type) | 
  map({type: .[0].type, count: length})' structured-log-*.json
```

## Common Patterns

### Finding Specific Log Messages

```bash
# Find all STARTUP messages
grep "STARTUP" console-output-*.log

# Find all error lines
grep -i "error\|exception\|failed" console-output-*.log

# Find specific service initialization
grep "ML parameter provider" console-output-*.log
```

### Checking for Known Issues

```bash
# Check for null reference errors
grep "NullReferenceException" error-output-*.log

# Check for timeout issues
grep -i "timeout" console-output-*.log

# Check for connection failures
grep -i "connection\|connect" console-output-*.log | grep -i "fail\|error"
```

### Extracting Metrics

```bash
# Get exact startup duration
jq -r '.actual_runtime_seconds' structured-log-*.json

# Get event count
jq '.events | length' structured-log-*.json

# Get error event count
jq '[.events[] | select(.type == "stderr")] | length' structured-log-*.json
```

## Best Practices

### When to Run Diagnostics

1. **After Configuration Changes** - Verify changes work
2. **Before Deployment** - Baseline production readiness
3. **During Troubleshooting** - Capture error context
4. **For Documentation** - Show expected behavior
5. **Performance Testing** - Establish benchmarks

### What to Look For

1. **Exit Code** - Should be 0 for clean shutdown
2. **Startup Time** - Should be consistent (< 5s variance)
3. **Event Count** - Should be stable across runs
4. **Error Messages** - Should be none in stderr
5. **Configuration** - All required vars should show [SET]

### Storage and Organization

```
diagnostics/
├── baseline/
│   ├── 2025-10-14-baseline.zip
│   └── analysis-notes.md
├── issues/
│   ├── startup-failure-2025-10-15.zip
│   └── connection-timeout-2025-10-16.zip
└── performance/
    ├── run-001.zip
    ├── run-002.zip
    └── comparison.md
```

## Troubleshooting Tips

### Workflow Doesn't Start
1. Check self-hosted runner is online
2. Verify runner has "self-hosted" tag
3. Confirm workflow file is in `.github/workflows/`

### Artifacts Too Large
1. Reduce runtime_minutes parameter
2. Disable detailed logging if not needed
3. GitHub has 500MB per artifact limit

### Missing Data in Logs
1. Increase runtime_minutes to capture more
2. Enable capture_detailed_logs
3. Check if bot crashed early (see error-output-*.log)

### Can't Download Artifacts
1. Wait for workflow to complete
2. Check you have repository access
3. Verify artifacts didn't expire (30 days)

## Summary

This workflow provides comprehensive diagnostics for bot launches:
- ✅ Easy to trigger via GitHub UI
- ✅ Complete data capture
- ✅ Organized artifact structure
- ✅ Safe (always DRY_RUN mode)
- ✅ Shareable results
- ✅ Historical comparison

Use it regularly to maintain visibility into bot behavior and quickly troubleshoot issues.
