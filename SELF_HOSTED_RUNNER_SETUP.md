# Self-Hosted Runner Configuration

## Changes Made

All bot launch workflows have been updated to use **self-hosted runners** instead of GitHub-hosted runners.

### Updated Workflows

1. **bot-launch-github-hosted.yml** (renamed to "🚀 Bot Launch - Self-Hosted Runner")
   - Changed: `runs-on: ubuntu-latest` → `runs-on: self-hosted`
   
2. **bot-launch-diagnostics.yml**
   - Changed: `runs-on: ubuntu-latest` → `runs-on: self-hosted`
   
3. **selfhosted-bot-run.yml**
   - Changed: `runs-on: ubuntu-latest` → `runs-on: self-hosted`

## Benefits of Self-Hosted Runners

✅ **Full Network Access**
- Can resolve and connect to api.topstepx.com
- No DNS resolution issues
- Complete TopstepX API connectivity

✅ **Python SDK Available**
- project-x-py SDK can be pre-installed on runner
- No need to install dependencies in each workflow run
- Faster workflow execution

✅ **Persistent Environment**
- Can maintain Python packages between runs
- Reuse model files and cached data
- Consistent runtime environment

## Prerequisites for Self-Hosted Runner

### 1. Runner Setup
Set up a self-hosted runner following GitHub's documentation:
https://docs.github.com/en/actions/hosting-your-own-runners

### 2. Required Software
Install these on your self-hosted runner:
```bash
# .NET SDK 8.0
# Install from: https://dotnet.microsoft.com/download/dotnet/8.0

# Python 3.12+
sudo apt update
sudo apt install python3 python3-pip

# TopstepX SDK
pip3 install 'project-x-py[all]>=3.5.0'
```

### 3. Environment Variables
Configure on the runner or in repository secrets:
```bash
TOPSTEPX_API_KEY=your_api_key
TOPSTEPX_USERNAME=your_username
TOPSTEPX_ACCOUNT_ID=your_account_id
PYTHON_EXECUTABLE=python3
```

### 4. Network Configuration
Ensure the runner has:
- DNS resolution for api.topstepx.com
- HTTPS access to port 443
- WebSocket connectivity to rtc.topstepx.com

## Important Limitations

⚠️ **GitHub Copilot Agent Cannot:**
- Trigger workflow runs (you must manually trigger via GitHub Actions UI)
- Access workflow run logs directly
- Monitor runner output in real-time

✅ **GitHub Copilot Agent Can:**
- Modify workflow YAML files
- Analyze logs when you provide them
- Fix code issues based on errors you share

## How to Use

### Step 1: Set Up Self-Hosted Runner
1. Go to repository Settings → Actions → Runners
2. Click "New self-hosted runner"
3. Follow setup instructions for your OS
4. Install required software (see Prerequisites above)

### Step 2: Trigger Workflow
1. Go to Actions tab in GitHub
2. Select one of the bot launch workflows
3. Click "Run workflow"
4. Choose your parameters (runtime, log level, etc.)
5. Click "Run workflow" button

### Step 3: Share Logs for Analysis
If errors occur:
1. Open the failed workflow run
2. Copy relevant error messages or logs
3. Paste them in a comment mentioning @copilot
4. Copilot will analyze and provide fixes

## Expected Behavior

With self-hosted runner and network access:

```
✅ Python SDK validated successfully
✅ SDK adapter process starts in stream mode
✅ TopstepX authentication succeeds
✅ WebSocket connection established
✅ Live market data streaming
✅ Historical data retrieval working
✅ Bot operates in full production mode
```

## Testing the Setup

Run this command on your self-hosted runner to verify setup:

```bash
# Check .NET
dotnet --version  # Should show 8.0.x

# Check Python
python3 --version  # Should show 3.12+

# Check SDK
python3 -c "import project_x_py; print('SDK OK')"

# Check network
ping -c 3 api.topstepx.com

# Build bot
cd /path/to/repo
dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release

# Test run
export PYTHON_EXECUTABLE=python3
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj -c Release
```

## Workflow Comparison

### Before (GitHub-Hosted)
```
❌ DNS resolution fails
❌ Cannot connect to api.topstepx.com
⚠️  SDK must be installed each run
⚠️  Limited to isolated network
```

### After (Self-Hosted)
```
✅ Full network connectivity
✅ TopstepX API accessible
✅ SDK pre-installed
✅ Production-ready environment
```

## Troubleshooting

### Runner Not Appearing
- Check runner service is running: `./run.sh` or `./run.cmd`
- Verify runner is online in Settings → Actions → Runners

### Authentication Fails
- Verify TOPSTEPX_API_KEY is set correctly
- Check credentials in .env file or runner environment
- Test API key: `curl -H "Authorization: Bearer $TOPSTEPX_API_KEY" https://api.topstepx.com/api/Auth/loginKey`

### SDK Not Found
- Install: `pip3 install 'project-x-py[all]>=3.5.0'`
- Verify: `python3 -c "import project_x_py; print('OK')"`
- Set PYTHON_EXECUTABLE: `export PYTHON_EXECUTABLE=python3`

## Next Steps

1. ✅ Set up self-hosted runner
2. ✅ Install prerequisites (.NET, Python, SDK)
3. ✅ Configure environment variables
4. ✅ Manually trigger a workflow
5. ✅ Monitor logs and share any errors with @copilot

---

**Status:** All workflows configured for self-hosted runners ✅

**Last Updated:** 2025-10-17
