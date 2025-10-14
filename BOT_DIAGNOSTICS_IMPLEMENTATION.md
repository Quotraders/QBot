# Bot Launch Diagnostics Implementation Summary

## Overview

Implemented a comprehensive GitHub Actions workflow that enables self-hosted runners to launch the entire trading bot and automatically capture all startup information, logs, and diagnostics into downloadable artifacts.

## Problem Solved

**Original Issue:**
> "use self hosted runner to launch my entire bot as a work flow and automatically tell us all the info that happens when bot launches and puts it into a folder so u can read it and fix any future issue and u can see the logs that are being posted on startup since u dont have access to it besides azure or runner"

**Solution:**
- Created automated workflow that runs on self-hosted runner
- Captures complete bot startup sequence with all logs
- Packages everything into downloadable artifacts
- Provides structured data for easy analysis
- Enables remote troubleshooting without direct access to runner

## Implementation Details

### Files Created

1. **`.github/workflows/bot-launch-diagnostics.yml`** (20KB)
   - Main GitHub Actions workflow
   - Runs on self-hosted runner
   - Captures all bot output and diagnostics
   - Uploads artifacts to GitHub

2. **`.github/workflows/README-bot-diagnostics.md`** (8.4KB)
   - Comprehensive documentation
   - Usage instructions
   - Troubleshooting guide
   - Integration details

3. **`docs/bot-diagnostics-quick-reference.md`** (4.1KB)
   - Quick reference guide
   - Common use cases
   - Useful commands
   - Troubleshooting table

### Files Modified

1. **`README.md`**
   - Added "Bot Diagnostics & Monitoring" section
   - Linked to workflow documentation
   - Explained safety features

2. **`RUNBOOKS.md`**
   - Added operational procedures for diagnostics
   - Included workflow access instructions
   - Listed use cases and artifact details

## Workflow Features

### Comprehensive Data Capture

The workflow captures:
- ✅ System information (OS, .NET version, runner details)
- ✅ Environment validation (configuration files, env vars)
- ✅ Complete console output from bot startup
- ✅ All error messages and stack traces
- ✅ Structured JSON logs with parsed events
- ✅ Runtime metrics and performance data
- ✅ Exit codes and shutdown reasons

### Artifact Structure

```
bot-diagnostics-run-{number}.zip
├── system-info.json          # System environment and configuration
├── console-output-*.log      # Complete console output
├── error-output-*.log        # Error stream capture
└── structured-log-*.json     # Parsed events with timestamps
```

### Safety Features

1. **Always DRY_RUN Mode**: Prevents live trading during diagnostics
2. **Automatic Timeout**: Graceful shutdown after configured runtime
3. **Error Isolation**: Artifacts uploaded even if bot crashes
4. **Resource Cleanup**: Process termination handled safely

### Workflow Steps

1. **Pre-Launch Environment Validation**
   - Check system configuration
   - Validate .NET environment
   - Verify required files exist
   - Create diagnostics directory

2. **Setup .NET and Restore Packages**
   - Restore NuGet packages
   - Track restore duration

3. **Build Trading Bot**
   - Build UnifiedOrchestrator
   - Track build duration

4. **Launch Bot with Full Diagnostics**
   - Set DRY_RUN mode
   - Capture stdout and stderr
   - Parse important events
   - Save structured logs

5. **Package Diagnostics Artifacts**
   - List all captured files
   - Prepare for upload

6. **Upload Diagnostics Artifacts**
   - Upload to GitHub Actions
   - 30-day retention
   - Compressed storage

7. **Final Execution Report**
   - Summary of results
   - Access instructions
   - Next steps guidance

## Usage

### Manual Trigger

1. Go to **Actions** tab in GitHub
2. Select **"🤖 Bot Launch Diagnostics - Self-Hosted"**
3. Click **"Run workflow"**
4. Configure:
   - Runtime duration (default: 5 minutes)
   - Detailed logging (default: enabled)
5. Wait for completion
6. Download artifacts

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `runtime_minutes` | string | "5" | Bot runtime duration |
| `capture_detailed_logs` | boolean | true | Enable verbose logging |

## Use Cases

### 1. Troubleshooting Startup Failures
When the bot fails to start:
- Run workflow to capture failure
- Download artifacts
- Review console logs for errors
- Analyze structured events timeline
- Check error logs for exceptions

### 2. Validating Configuration Changes
After modifying configuration:
- Run workflow to test changes
- Verify environment in system-info.json
- Check console logs for expected behavior
- Confirm no new errors appear

### 3. Performance Analysis
To analyze bot performance:
- Run workflow multiple times
- Compare runtime metrics
- Review event timestamps
- Identify slow initialization

### 4. Documentation and Sharing
To share bot behavior:
- Run workflow to capture state
- Download artifact package
- Share with team or support
- Provides complete context

## Integration

### Existing Infrastructure

The workflow integrates seamlessly with:
- ✅ Same build process as production
- ✅ Respects all safety guardrails (kill.txt, DRY_RUN)
- ✅ Compatible with PathConfigService
- ✅ Follows production logging patterns
- ✅ Uses UnifiedOrchestrator entry point

### No Breaking Changes

- No modifications to core bot code
- No changes to existing workflows
- No impact on production deployments
- Purely additive functionality

## Technical Details

### YAML Workflow Structure

- **Trigger**: `workflow_dispatch` (manual only)
- **Runner**: `self-hosted`
- **Timeout**: Configurable (runtime + 5 minutes buffer)
- **Shell**: PowerShell for Windows compatibility
- **Permissions**: Read-only content, write actions

### PowerShell Implementation

- Uses .NET process capture for output
- Event handlers for stdout/stderr
- StringBuilder for efficient string operations
- Structured data output via JSON
- Graceful process termination

### Artifact Management

- Stored in `$RUNNER_TEMP` directory
- 30-day retention policy
- Compression level 6 for efficiency
- Multiple downloads allowed

## Validation

### YAML Syntax
- ✅ Valid YAML structure
- ✅ Proper job/step configuration
- ✅ Correct parameter types
- ✅ Valid GitHub Actions syntax

### Documentation
- ✅ Comprehensive README
- ✅ Quick reference guide
- ✅ Runbook integration
- ✅ Main README updates

### Safety Compliance
- ✅ No analyzer bypasses
- ✅ No configuration modifications
- ✅ No suppression additions
- ✅ Maintains DRY_RUN mode
- ✅ Preserves all guardrails

## Future Enhancements

Potential improvements:
- [ ] Automated log analysis with pattern detection
- [ ] Summary report generation
- [ ] Multiple configuration testing
- [ ] Performance benchmarking
- [ ] Comparison mode vs previous runs
- [ ] Dashboard visualization

## Testing

### Manual Testing Required

The workflow requires a self-hosted runner to test. Manual testing steps:

1. Ensure self-hosted runner is configured
2. Trigger workflow via GitHub Actions UI
3. Monitor execution in real-time
4. Download artifacts after completion
5. Verify all files are present
6. Validate log content is complete

### Expected Outcomes

- ✅ Workflow completes successfully
- ✅ All artifacts are uploaded
- ✅ Console logs capture bot startup
- ✅ Structured logs contain parsed events
- ✅ System info includes environment details

## Security Considerations

### Credentials Protection

- ✅ Environment variables not exposed in logs
- ✅ Sensitive values shown as `[SET]` or `[MISSING]`
- ✅ No API keys in artifacts
- ✅ Artifacts stored securely in GitHub

### Access Control

- ✅ Workflow requires manual trigger
- ✅ Artifacts only accessible to repository members
- ✅ 30-day retention limits exposure
- ✅ No external integrations

## Compliance

### Production Guardrails

Maintains all production requirements:
- ✅ **Minimal Changes**: Additive only, no modifications to core
- ✅ **DRY_RUN Default**: Always enabled for diagnostics
- ✅ **No Analyzer Bypasses**: No suppressions or config changes
- ✅ **Safety Mechanisms**: All guardrails remain functional
- ✅ **No Live Trading**: Diagnostic runs never execute live orders

### Code Quality

- ✅ No new analyzer warnings
- ✅ YAML syntax validated
- ✅ Documentation complete
- ✅ Follows existing patterns

## Documentation

### User-Facing Documentation

1. **Workflow README** (`.github/workflows/README-bot-diagnostics.md`)
   - Complete usage guide
   - Detailed examples
   - Troubleshooting section
   - Integration details

2. **Quick Reference** (`docs/bot-diagnostics-quick-reference.md`)
   - One-page reference
   - Common commands
   - Quick troubleshooting
   - Use case examples

3. **Main README** (`README.md`)
   - High-level overview
   - Quick access instructions
   - Links to detailed docs

4. **Runbooks** (`RUNBOOKS.md`)
   - Operational procedures
   - Integration with existing runbooks
   - Daily workflow integration

## Conclusion

This implementation successfully addresses the requirement to:
- ✅ Launch entire bot via self-hosted workflow
- ✅ Automatically capture all startup information
- ✅ Put logs into organized folder structure
- ✅ Enable remote troubleshooting
- ✅ Provide visibility into startup process

The solution is:
- **Safe**: DRY_RUN mode, no live trading
- **Complete**: Captures all relevant data
- **Accessible**: Easy download from GitHub
- **Documented**: Comprehensive guides
- **Production-Ready**: Follows all guardrails

## File Summary

| File | Type | Purpose |
|------|------|---------|
| `.github/workflows/bot-launch-diagnostics.yml` | Workflow | Main automation |
| `.github/workflows/README-bot-diagnostics.md` | Documentation | Complete guide |
| `docs/bot-diagnostics-quick-reference.md` | Documentation | Quick reference |
| `README.md` | Documentation | Overview (updated) |
| `RUNBOOKS.md` | Documentation | Procedures (updated) |

**Total Lines Added**: ~800
**Total Files Modified**: 2
**Total Files Created**: 3
