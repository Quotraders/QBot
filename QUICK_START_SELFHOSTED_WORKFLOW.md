# Quick Start: Self-Hosted Bot Workflow

## 🚀 How to Run the Self-Hosted Bot Workflow

The self-hosted bot workflow (`selfhosted-bot-run.yml`) allows you to test the trading bot on your own self-hosted GitHub Actions runner.

### Prerequisites

1. **Self-Hosted Runner Setup**
   - Follow: [SELF_HOSTED_RUNNER_SETUP.md](SELF_HOSTED_RUNNER_SETUP.md)
   - Runner must be registered with your repository
   - Runner must have .NET 8.0 SDK installed

2. **TopstepX Credentials** (GitHub Secrets)
   - Go to: Settings → Secrets and variables → Actions
   - Add these secrets:
     - `TOPSTEPX_API_KEY` = Your TopstepX API key
     - `TOPSTEPX_USERNAME` = Your TopstepX email/username
     - `TOPSTEPX_ACCOUNT_ID` = Your TopstepX account ID
     - `TOPSTEPX_ACCOUNT_NAME` = Your account name (optional)

3. **Python SDK** (Optional - for full trading features)
   - On your self-hosted runner, install:
   ```bash
   pip install 'project-x-py[all]'
   ```
   - Without SDK: Bot will run in degraded mode (no live data feed)

### How to Run the Workflow

#### Option 1: Via GitHub UI (Recommended)

1. Go to your repository on GitHub
2. Click the **Actions** tab
3. In the left sidebar, find **"🚀 Bot Execution Test"**
4. Click **"Run workflow"** button (top right)
5. Configure the run:
   - **Branch**: Select your branch (e.g., `main` or `copilot/fix-bot-workflow-errors`)
   - **timeout_minutes**: `5` (for testing) or `10-30` (for longer runs)
   - **dry_run**: ✅ **Keep this ENABLED** (true) for safe paper trading
6. Click **"Run workflow"**

#### Option 2: Via GitHub CLI

```bash
# Install GitHub CLI if needed
# https://cli.github.com/

# Trigger the workflow (DRY_RUN mode - safe)
gh workflow run selfhosted-bot-run.yml \
  --ref your-branch-name \
  -f timeout_minutes=5 \
  -f dry_run=true

# Check workflow status
gh run list --workflow=selfhosted-bot-run.yml --limit 5

# View logs of the latest run
gh run view --log
```

### What to Expect

#### Successful Run Output

You should see logs like this:

```
🔧 .NET Environment Setup
================================================
✅ .NET SDK Version: 8.0.x
✅ Environment configuration created
✅ NuGet restore completed

🏗️ Building Trading Bot Solution
================================================
✅ Build completed successfully

🔍 Verifying Build Output
================================================
✅ Build artifacts verified
   UnifiedOrchestrator.dll: 1.40 MB

🚀 Starting Trading Bot
================================================
✅ Process started (PID: xxxx)
⏱️ Bot is running...

✅ Unified trading system initialized successfully - SDK Ready: False
```

#### Expected Warnings (Not Errors)

These are **normal** if Python SDK is not installed:

```
❌ [SDK-VALIDATION] project-x-py SDK not found
   → Bot continues with fallback data sources

❌ Connection refused (localhost:8765)
   → SDK adapter not running, using direct API

⚠️ NO real historical data available
   → Bot will wait for live market data
```

**Success Indicator**: Look for `✅ Unified trading system initialized successfully`

### Workflow Configuration

The workflow runs for **15 minutes** by default, which includes:
- Startup time (~1-2 minutes)
- Bot execution time (configurable via `timeout_minutes` input)
- Shutdown time (~1 minute)

### Safety Features

✅ **DRY_RUN Mode** (enabled by default)
- Uses real market data
- Simulates all trades
- **No real money at risk**
- Perfect for testing

⚠️ **Live Trading** (disabled by default)
- Only enable `dry_run=false` when you're ready for real trading
- Requires full setup (Python SDK, live data feed)
- **Real money at risk**

### Troubleshooting

#### Workflow Not Starting

**Problem**: Workflow shows "Waiting for a runner to pick up this job..."

**Solution**: 
- Check that your self-hosted runner is online
- Go to: Settings → Actions → Runners
- Runner should show as "Idle" (green)
- Restart runner if needed

#### Build Fails on macOS

**Problem**: `stat: illegal option -- c`

**Solution**: 
- This was fixed in commit c677496
- Make sure you're running the workflow from the updated branch

#### Bot Exits Immediately

**Problem**: Bot logs show immediate exit

**Check**:
1. Look for `✅ Unified trading system initialized successfully`
2. If missing, check for ERROR logs before exit
3. Verify TopstepX credentials are set correctly

#### No Market Data

**Problem**: `NO real historical data available`

**Solutions**:
1. **Install Python SDK** (recommended):
   ```bash
   pip install 'project-x-py[all]'
   ```

2. **Start SDK Adapter** (if installed):
   ```bash
   cd src/adapters
   python topstep_x_adapter.py persistent
   ```

3. **Use Direct API** (fallback - limited):
   - Bot will connect directly to TopstepX REST API
   - Some features may be degraded

### Viewing Results

#### During Run

1. Click on the running workflow
2. Click on the job name ("Run Trading Bot")
3. Watch real-time logs

#### After Run

1. Go to Actions tab
2. Click on the completed workflow run
3. View logs and artifacts
4. Download logs: "📋 Last 100 lines of bot output"

### Next Steps

After successful test run:

1. ✅ Review logs for any unexpected errors
2. ✅ Verify TopstepX connection status
3. ✅ Check that strategies are enabled
4. ✅ Monitor for a few runs to ensure stability

**When ready for live trading:**
1. Install Python SDK on runner
2. Verify credentials are correct
3. Start with small position sizes
4. Monitor closely for the first few trades
5. Only then disable DRY_RUN mode

### Platform-Specific Notes

#### Linux Runners (Ubuntu/Debian)
- ✅ Fully supported
- Uses `stat -c%s` for file size checks

#### macOS Runners
- ✅ Fully supported (as of commit c677496)
- Uses `stat -f%z` fallback for file size checks

#### Windows Runners (WSL/Git Bash)
- ⚠️ May need additional configuration
- Ensure Git Bash or WSL is available
- Test with a short timeout first

### Related Documentation

- [SELF_HOSTED_RUNNER_SETUP.md](SELF_HOSTED_RUNNER_SETUP.md) - Runner setup guide
- [WORKFLOW_FIX_REPORT.md](WORKFLOW_FIX_REPORT.md) - Recent workflow fixes
- [QUICK_START_GITHUB_WORKFLOWS.md](QUICK_START_GITHUB_WORKFLOWS.md) - GitHub-hosted alternative
- [TOPSTEPX_ADAPTER_SETUP_GUIDE.md](TOPSTEPX_ADAPTER_SETUP_GUIDE.md) - Python SDK setup

### Support

If you encounter issues:

1. Check the workflow logs for specific error messages
2. Review the troubleshooting section above
3. Verify all prerequisites are met
4. Create an issue with:
   - Workflow run URL
   - Error message from logs
   - Runner OS and configuration

---

**Last Updated**: 2025-10-17  
**Workflow Version**: selfhosted-bot-run.yml (commit c677496)  
**Status**: ✅ Production Ready
