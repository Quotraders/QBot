# Quick Start Guide - Bot Launch Workflows

## ✅ Workflows Fixed and Ready to Run

All bot launch workflows have been fixed and are ready to use. The bot now handles missing dependencies gracefully and will launch successfully in both local and CI environments.

## Available Workflows

### 1. 🚀 Bot Launch - GitHub Hosted
**File:** `.github/workflows/bot-launch-github-hosted.yml`

**Purpose:** Launch the trading bot on GitHub-hosted runners with full diagnostics

**Features:**
- Configurable runtime (1-60 minutes)
- Log level selection (Debug, Information, Warning, Error)
- DRY_RUN mode for safe testing
- Automatic artifact upload

**To Run:**
1. Go to: Actions → "🚀 Bot Launch - GitHub Hosted"
2. Click "Run workflow"
3. Select runtime, log level, and DRY_RUN mode
4. Click "Run workflow"

---

### 2. 🚀 Bot Execution Test
**File:** `.github/workflows/selfhosted-bot-run.yml`

**Purpose:** Simple bot execution test on GitHub-hosted runners

**Features:**
- Quick startup test
- Configurable timeout
- DRY_RUN mode support

**To Run:**
1. Go to: Actions → "🚀 Bot Execution Test"
2. Click "Run workflow"
3. Select timeout and DRY_RUN mode
4. Click "Run workflow"

---

### 3. 🤖 Bot Launch Diagnostics
**File:** `.github/workflows/bot-launch-diagnostics.yml`

**Purpose:** Launch bot with comprehensive diagnostics capture

**Features:**
- Full diagnostic logging
- System information capture
- Structured log output
- Error stream capture
- Configurable runtime (5-30 minutes)

**To Run:**
1. Go to: Actions → "🤖 Bot Launch Diagnostics"
2. Click "Run workflow"
3. Select runtime and logging options
4. Click "Run workflow"

---

## Prerequisites

### Required Secrets (Set in GitHub Repository Settings)
```
TOPSTEPX_API_KEY - Your TopstepX API key
TOPSTEPX_USERNAME - Your TopstepX username
TOPSTEPX_ACCOUNT_ID - Your TopstepX account ID
TOPSTEPX_ACCOUNT_NAME - Your TopstepX account name (optional)
```

### Local Development
```bash
# Install .NET 8.0 SDK
# https://dotnet.microsoft.com/download/dotnet/8.0

# Clone repository
git clone https://github.com/Quotraders/QBot.git
cd QBot

# Restore packages
dotnet restore TopstepX.Bot.sln

# Build
dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release

# Run
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release
```

---

## Expected Behavior

### ✅ Successful Launch Indicators
```
🚀 UNIFIED TRADING ORCHESTRATOR SYSTEM 🚀
🔧 [STARTUP] Building dependency injection container...
✅ Unified trading system initialized successfully
```

### ⚠️ Expected Warnings (These are normal in CI)
```
⚠️ TopstepX SDK adapter initialization failed (SDK not installed)
⚠️ Connection refused to localhost:8765 (SDK adapter not running)
⚠️ API connectivity issues (network isolation in CI)
```

These warnings are expected and handled gracefully. The bot will still launch and operate in simulation mode.

---

## Troubleshooting

### Bot Won't Start
1. Check that .NET 8.0 SDK is installed
2. Verify all NuGet packages are restored
3. Check build succeeded with no errors
4. Review bot console output for specific errors

### Workflow Fails
1. Verify repository secrets are set correctly
2. Check workflow permissions (Actions → Settings → Workflow permissions)
3. Review workflow run logs for detailed error messages
4. Ensure workflows have `contents: read` and `actions: write` permissions

### Missing Dependencies
The bot now handles missing dependencies gracefully:
- TopstepX SDK: Bot runs in offline mode
- Historical data: Bot waits for live data
- Model files: Uses default configurations

---

## Testing Locally

### Quick Test
```bash
# Run for 10 seconds
timeout 10s dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release

# Should see:
# ✅ Unified trading system initialized successfully
```

### Full Test
```bash
# Run for 5 minutes
timeout 300s dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release
```

---

## Artifacts

After running workflows, download artifacts to review:
- **Console logs** - Complete bot output
- **Error logs** - Error stream capture
- **Structured logs** - JSON formatted events
- **Execution summary** - Configuration and results

**Access:** Actions → Workflow run → Artifacts section (30 day retention)

---

## Safety Features

All workflows include:
- ✅ DRY_RUN mode enabled by default
- ✅ Automatic timeout protection
- ✅ Graceful shutdown handling
- ✅ Comprehensive error logging
- ✅ Artifact capture on failure

---

## Next Steps

1. ✅ Test workflow locally
2. ✅ Run diagnostic workflow on GitHub Actions
3. ✅ Review artifacts and logs
4. ✅ Adjust runtime and log levels as needed
5. ✅ Consider self-hosted runners for production

---

## Support

For issues or questions:
1. Check BOT_LAUNCH_FIX_SUMMARY.md for detailed fix information
2. Review workflow run logs in GitHub Actions
3. Check repository issues and discussions

---

**Last Updated:** 2025-10-17  
**Status:** ✅ All workflows operational
