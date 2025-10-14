# 🚀 Bot Diagnostics Quick Reference

## Launch Diagnostics Workflow

**GitHub Actions → 🤖 Bot Launch Diagnostics - Self-Hosted**

### Quick Start
1. Navigate to **Actions** tab
2. Select workflow: "🤖 Bot Launch Diagnostics - Self-Hosted"
3. Click **"Run workflow"**
4. Wait for completion (default: 5 minutes)
5. Download artifacts from workflow run

### Configuration Options

| Parameter | Default | Description |
|-----------|---------|-------------|
| `runtime_minutes` | 5 | How long to run bot |
| `capture_detailed_logs` | true | Enable verbose logging |

### Artifacts Downloaded

```
bot-diagnostics-run-{number}.zip
├── system-info.json          # System environment
├── console-output-*.log      # Complete console logs
├── error-output-*.log        # Error stream
└── structured-log-*.json     # Parsed events with timestamps
```

### Common Use Cases

#### 🐛 Troubleshooting Startup Failures
1. Run workflow
2. Download artifacts
3. Check `console-output-*.log` for errors
4. Review `error-output-*.log` for exceptions
5. Analyze `structured-log-*.json` for event timeline

#### ✅ Validating Configuration
1. Make configuration changes
2. Run workflow to test
3. Review `system-info.json` for environment
4. Check console logs for expected behavior

#### 📊 Performance Analysis
1. Run workflow multiple times
2. Compare runtime metrics in structured logs
3. Identify slow initialization steps
4. Review event timestamps

### Safety Features

✅ **DRY_RUN Mode Always Enabled** - No live trading
✅ **Automatic Timeout** - Graceful shutdown after runtime limit
✅ **Error Isolation** - Artifacts always uploaded even on errors
✅ **Resource Cleanup** - Process termination handled safely

### Reading the Logs

#### Key Startup Markers (console-output-*.log)
```
🚀 [STARTUP] Starting unified orchestrator...
✅ [STARTUP] Service validation completed
⚙️ [STARTUP] Initializing ML parameter provider...
✅ [STARTUP] ML parameter provider initialized
```

#### Error Patterns to Look For
```
❌ CRITICAL ERROR: ...
⚠️ Warning: ...
Exception: ...
NullReferenceException
InvalidOperationException
```

#### Structured Log Analysis (structured-log-*.json)
```json
{
  "launch_timestamp": "2025-10-14T22:30:00Z",
  "end_timestamp": "2025-10-14T22:35:00Z",
  "actual_runtime_seconds": 300.45,
  "exit_code": 0,
  "exit_reason": "timeout",
  "events": [...]
}
```

### Useful Commands

#### Extract Errors from Console Log
```bash
grep -E "(ERROR|Exception|❌)" console-output-*.log
```

#### Count Events by Type
```bash
jq '.events | group_by(.type) | map({type: .[0].type, count: length})' structured-log-*.json
```

#### Find Specific Event
```bash
jq '.events[] | select(.message | contains("STARTUP"))' structured-log-*.json
```

#### Show Event Timeline
```bash
jq '.events[] | "\(.timestamp) - \(.message)"' structured-log-*.json
```

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Workflow not appearing | Verify self-hosted runner is online |
| Workflow fails immediately | Check .NET SDK installed on runner |
| No artifacts uploaded | Verify workflow completed (check logs) |
| Large artifact size | Reduce runtime_minutes parameter |

### Integration Points

- Uses same build process as production
- Respects all safety guardrails (kill.txt, DRY_RUN)
- Compatible with PathConfigService configuration
- Follows production logging patterns

### Retention

- **Artifact Retention:** 30 days
- **Storage:** GitHub Actions artifact storage
- **Downloads:** Unlimited during retention period

### Support

1. Check workflow logs in GitHub Actions UI
2. Review [README-bot-diagnostics.md](.github/workflows/README-bot-diagnostics.md)
3. Download artifacts for local analysis
4. Verify self-hosted runner status

### Related Files

- `.github/workflows/bot-launch-diagnostics.yml` - Workflow definition
- `.github/workflows/README-bot-diagnostics.md` - Full documentation
- `src/UnifiedOrchestrator/Program.cs` - Bot entry point
- `RUNBOOKS.md` - Operational procedures

---

**📚 Full Documentation:** [.github/workflows/README-bot-diagnostics.md](.github/workflows/README-bot-diagnostics.md)
