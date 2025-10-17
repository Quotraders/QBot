# Quick Start: GitHub-Hosted Bot Launch

## 🚀 Launch the Bot in 3 Steps

### Step 1: Configure Secrets
1. Go to your GitHub repository
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Add these secrets:
   - `TOPSTEPX_API_KEY` = Your TopstepX API key
   - `TOPSTEPX_USERNAME` = Your TopstepX email/username
   - `TOPSTEPX_ACCOUNT_ID` = Your TopstepX account ID
   - `TOPSTEPX_ACCOUNT_NAME` = Your account name (optional)

### Step 2: Run the Workflow
1. Go to **Actions** tab
2. Click **"Bot Launch - GitHub Hosted"**
3. Click **"Run workflow"** button
4. Configure:
   - **Runtime**: 5 minutes (for testing)
   - **Log Level**: Information
   - **DRY_RUN**: ✅ Enabled (recommended for first run)
5. Click **"Run workflow"**

### Step 3: View Results
1. Click on the running workflow
2. Click on the job name to see real-time logs
3. Wait for completion
4. Download artifacts for detailed logs

## ⚠️ Important Notes

- **DRY_RUN Mode**: Enabled by default. Uses real market data but simulates trades.
- **First Run**: Always test with 5 minutes runtime first
- **Logs**: Automatically uploaded as artifacts (30-day retention)
- **Debugging**: Check console logs in artifacts for any issues

## 📊 What to Expect

### Successful Run Shows:
```
✅ .NET SDK Version: 8.0.x
✅ TopstepX credentials: Validated
✅ Build completed successfully
✅ Process started (PID: xxxx)
⏱️ Bot is running...
✅ Bot stopped
✅ Status: SUCCESS
```

### Common Issues:
- **"Required TopstepX credentials are missing"** → Check GitHub secrets
- **"Build failed"** → Check build logs, may need to restore packages
- **"Process exited immediately"** → Download artifacts, check console logs

## 🔗 Resources

- **Full Documentation**: `.github/workflows/README-GITHUB-HOSTED-WORKFLOWS.md`
- **Troubleshooting**: See documentation for common issues
- **TopstepX Setup**: `TOPSTEPX_ADAPTER_SETUP_GUIDE.md`
- **Main README**: `README.md`

## 💡 Next Steps

After successful first run:
1. Review downloaded logs
2. Verify TopstepX connection in logs
3. Increase runtime if needed (10-30 minutes)
4. Enable Debug logging for more details
5. Consider longer runs for actual trading (with DRY_RUN still enabled)

## 🛡️ Safety

- ✅ DRY_RUN mode is ON by default
- ✅ LIVE_ORDERS is set to 0
- ✅ No real money at risk with default settings
- ⚠️ Only disable DRY_RUN when ready for live trading

---

**Need help?** See full documentation or create an issue with your workflow logs.
