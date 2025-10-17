# Testing the selfhosted-bot-run Workflow

## How to Manually Trigger the Workflow

The `selfhosted-bot-run.yml` workflow is configured to run via manual dispatch. To trigger it:

1. Navigate to the **Actions** tab in the GitHub repository
2. Select the **🚀 Bot Execution Test** workflow from the left sidebar
3. Click the **Run workflow** button
4. Select the branch you want to test (e.g., `copilot/fix-trading-bot-errors`)
5. Configure the parameters:
   - **timeout_minutes**: Keep default `5` or change to desired timeout
   - **dry_run**: Set to `true` for paper trading mode
6. Click **Run workflow**

## What to Monitor

After triggering the workflow, monitor the following in the workflow logs:

### Pre-flight Checks (Expected)
```
🐍 Checking Python environment...
✅ Python: Python X.X.X
✅ project-x-py SDK is installed
```

### Bot Startup (Expected)
```
🚀 Initializing TopstepX Python SDK adapter in PERSISTENT mode...
✅ TopstepX adapter initialized successfully in PERSISTENT mode
```

### Known Issues to Check For

1. **Python SDK Connection**: Look for "No connection could be made because the target machine actively refused it (localhost:8765)"
   - **Fixed**: Environment variables now properly passed to Python subprocess
   
2. **WebSocket Authentication**: Look for "WebSocket error" in logs
   - **Status**: May still occur if TopstepX credentials are invalid/expired
   - **Non-blocking**: Bot will run in degraded mode without real-time data

3. **Missing Config Files**: Look for health check warnings about missing files
   - **Status**: Non-critical warnings, bot runs without pre-trained models
   - Files: Parameter Bundle, Champion RL Model, Strategy Selection Model, Emergency Stop File

### Expected Behavior

- ✅ Bot should start and initialize for ~3 minutes
- ✅ Python SDK adapter should launch successfully (if Python/SDK available)
- ✅ Workflow will show progress updates every 60 seconds
- ✅ After 7 minutes, workflow will timeout gracefully (expected)
- ✅ Last 150 lines of logs will be displayed for analysis

## Verifying the Fix

Compare new run logs with previous run logs. The fix should show:

1. **Before Fix**:
   ```
   [15:41:53.485] [ERR] ERROR Services.HistoricalDataBridgeService: [HISTORICAL-BRIDGE] SDK adapter bars failed for ES: No connection could be made because the target machine actively refused it. (localhost:8765)
   ```

2. **After Fix** (with Python/SDK available):
   ```
   [PERSISTENT] ✅ Python adapter initialized successfully
   [ADAPTER] Adapter already initialized
   ✅ TopstepX adapter initialized successfully in PERSISTENT mode
   ```

3. **After Fix** (without Python/SDK):
   ```
   ⚠️ WARNING: project-x-py SDK not found - bot will run without live data
   To install: pip install 'project-x-py[all]'
   ```

## Troubleshooting

If the bot still fails to connect:

1. **Check Python Installation**:
   ```powershell
   python --version
   python -c "import project_x_py; print('SDK OK')"
   ```

2. **Check Environment Variables**:
   - Ensure `TOPSTEPX_API_KEY` secret is set
   - Ensure `TOPSTEPX_USERNAME` secret is set
   - Ensure `TOPSTEPX_ACCOUNT_ID` secret is set (if applicable)

3. **Check Network Connectivity**:
   - Verify self-hosted runner can reach TopstepX API endpoints
   - Check firewall rules if WebSocket connections fail

4. **Check Credentials**:
   - Verify TopstepX API credentials are valid and not expired
   - Check if account has proper permissions for real-time data
