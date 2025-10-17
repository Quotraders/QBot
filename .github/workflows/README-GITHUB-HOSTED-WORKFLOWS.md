# GitHub-Hosted Bot Launch Workflows

## Overview

All bot launch workflows have been upgraded to run on **GitHub-hosted runners** (ubuntu-latest) instead of self-hosted runners. This allows you to:

- ✅ Launch the bot from anywhere without requiring a self-hosted runner
- ✅ Connect to TopstepX API with full credentials
- ✅ View comprehensive logs directly in GitHub Actions
- ✅ Debug issues without needing local infrastructure
- ✅ Download artifacts for detailed analysis

## Available Workflows

### 1. 🚀 Bot Launch - GitHub Hosted
**File**: `.github/workflows/bot-launch-github-hosted.yml`

**Purpose**: Primary workflow for launching the bot with configurable runtime and logging levels.

**Features**:
- Configurable runtime (1, 5, 10, 15, 30, 60 minutes)
- Adjustable log levels (Debug, Information, Warning, Error)
- DRY_RUN mode toggle (paper trading vs live)
- Full TopstepX API connectivity
- Comprehensive console logging
- Artifact upload for debugging

**How to Use**:
1. Go to Actions → Bot Launch - GitHub Hosted
2. Click "Run workflow"
3. Select runtime duration
4. Choose log level
5. Enable/disable DRY_RUN mode
6. Click "Run workflow"
7. View logs in real-time
8. Download artifacts after completion

### 2. 🤖 Bot Launch Diagnostics
**File**: `.github/workflows/bot-launch-diagnostics.yml`

**Purpose**: Advanced diagnostic run with detailed logging and system information capture.

**Features**:
- Pre-launch environment validation
- System information capture (JSON)
- Structured event logging
- Detailed console and error logs
- 30-day artifact retention
- Comprehensive execution reports

**How to Use**:
1. Go to Actions → Bot Launch Diagnostics
2. Click "Run workflow"
3. Select runtime duration (5-30 minutes)
4. Enable detailed verbose logs if needed
5. Enable/disable DRY_RUN mode
6. View comprehensive diagnostics in artifacts

### 3. 🚀 Bot Execution Test
**File**: `.github/workflows/selfhosted-bot-run.yml`

**Purpose**: Simple bot execution test (formerly self-hosted, now GitHub-hosted).

**Features**:
- Quick 5-minute test run
- Basic log capture
- Error detection
- Simple success/failure reporting

**How to Use**:
1. Go to Actions → Bot Execution Test
2. Click "Run workflow"
3. Set timeout minutes (default: 5)
4. Enable/disable DRY_RUN mode
5. View execution summary

## Required Secrets

All workflows require the following GitHub secrets to be configured in your repository settings:

### TopstepX Credentials
- `TOPSTEPX_API_KEY` - Your TopstepX API key
- `TOPSTEPX_USERNAME` - Your TopstepX username/email
- `TOPSTEPX_ACCOUNT_ID` - Your TopstepX account ID
- `TOPSTEPX_ACCOUNT_NAME` - Your TopstepX account name (optional)

### How to Set Secrets
1. Go to your repository on GitHub
2. Navigate to Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Add each secret with its value
5. Save

## Workflow Configuration

### Environment Variables
All workflows automatically configure:
- TopstepX API endpoints
- DRY_RUN mode (paper trading)
- Enhanced learning features
- Model registry and promotion
- Logging levels

### .NET Environment
- .NET SDK 8.0.x is automatically installed
- All NuGet packages are restored
- Solution is built in Release mode
- UnifiedOrchestrator is the entry point

### Artifacts
Each workflow uploads artifacts containing:
- **Console logs**: Complete bot output
- **Error logs**: Stderr output
- **System info**: Environment details (JSON)
- **Structured logs**: Parsed events and metrics (JSON)
- **Execution summary**: Final report

Artifacts are retained for 30 days and can be downloaded from the Actions tab.

## Viewing Logs

### Real-Time Logs
1. Go to Actions → Select your workflow run
2. Click on the job name (e.g., "Launch Trading Bot")
3. Expand the "🚀 Launch Trading Bot" step
4. View streaming logs in real-time

### Downloaded Artifacts
1. Go to Actions → Select your workflow run
2. Scroll to the bottom "Artifacts" section
3. Click on the artifact name (e.g., "bot-logs-run-123")
4. Extract the ZIP file
5. Open log files with any text editor

## Debugging Issues

### Common Issues

**Issue**: "Required TopstepX credentials are missing"
- **Solution**: Ensure all required secrets are set in repository settings

**Issue**: "Build failed"
- **Solution**: Check the build logs for compilation errors. May indicate missing dependencies.

**Issue**: "Process exited immediately"
- **Solution**: Download artifacts and check console/error logs for startup failures

**Issue**: "Bot execution timeout"
- **Solution**: This is expected! The bot runs continuously, so timeout means it ran for the configured duration.

### Log Analysis
Check the console logs for these patterns:
- `✅` - Success indicators
- `❌` - Error indicators
- `⚠️` - Warning indicators
- `[STARTUP]` - Startup events
- `[CRITICAL]` - Critical errors
- `STDERR:` - Error stream output

## Safety Features

### DRY_RUN Mode
- Enabled by default in all workflows
- Uses real market data but simulates trades
- No real money at risk
- Can be disabled via workflow input (use with caution!)

### LIVE_ORDERS
- Always set to `0` in workflow configurations
- Prevents accidental live trading
- Manual override required for live trading

### Timeout Protection
- All workflows have maximum runtime limits
- Prevents runaway processes
- Graceful shutdown with SIGTERM
- Force kill with SIGKILL if needed

## Migration from Self-Hosted

If you were previously using self-hosted runners:

### What Changed
- ✅ Runs on GitHub-hosted ubuntu-latest runners
- ✅ No PowerShell - all bash scripts
- ✅ Credentials from GitHub secrets (not .env file)
- ✅ Simplified environment setup
- ✅ Consistent Linux environment

### What Stayed the Same
- ✅ Same bot functionality
- ✅ Same TopstepX connectivity
- ✅ Same safety features (DRY_RUN, kill switches)
- ✅ Same logging capabilities

### Advantages of GitHub-Hosted
1. **No infrastructure required**: No need to maintain self-hosted runners
2. **Consistent environment**: Same Linux environment every time
3. **Better security**: Credentials stored in GitHub secrets
4. **Easier debugging**: Logs and artifacts automatically available
5. **Cost effective**: Included in GitHub Actions free tier

## Troubleshooting

### Workflow Not Starting
- Check that all required secrets are set
- Verify you have write permissions to Actions
- Ensure workflow is enabled in repository settings

### Bot Fails to Connect to TopstepX
- Verify TopstepX credentials are correct
- Check API key is not expired
- Ensure account ID matches your TopstepX account
- Review error logs for specific connection errors

### Build Failures
- Check for .NET SDK version compatibility
- Review NuGet package restore logs
- Verify all project references are valid
- Check for code compilation errors

### Runtime Errors
- Download and review console logs
- Check error logs for exceptions
- Review structured logs for event sequence
- Look for TopstepX API errors

## Support

For issues or questions:
1. Review the logs and artifacts first
2. Check the troubleshooting section above
3. Search GitHub Issues for similar problems
4. Create a new issue with:
   - Workflow run link
   - Downloaded artifacts (sanitize sensitive data!)
   - Description of the problem
   - Steps to reproduce

## Best Practices

1. **Always test with DRY_RUN first**
2. **Start with short runtime (5 minutes) for testing**
3. **Use Debug log level when investigating issues**
4. **Download artifacts for detailed analysis**
5. **Monitor TopstepX account activity**
6. **Review logs after each run**
7. **Keep secrets secure and rotate regularly**

## Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [TopstepX API Documentation](https://docs.topstepx.com/)
- [Repository README](../../README.md)
- [Quick Start Guide](../../QUICK_START_COPILOT.md)
