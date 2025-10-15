# Trading Bot - Quick Troubleshooting Guide

## 🚀 Quick Start

```bash
# Easiest way to launch the bot
./run-bot.sh --check-sdk
```

## ❌ Common Errors and Solutions

### 1. "ModuleNotFoundError: No module named 'project_x_py'"

**What it means:** The TopstepX Python SDK is not installed.

**Solution:**
```bash
pip install 'project-x-py[all]'
```

**Impact:** Without the SDK, the bot runs in degraded mode:
- ❌ No live WebSocket connections to TopstepX
- ❌ No real-time market data
- ❌ No historical data loading
- ✅ ML/RL models still work offline
- ✅ Backtesting still works with local data

---

### 2. "SDK bridge script not found"

**What it means:** Cannot find `python/sdk_bridge.py`

**Solution:**
Ensure you're running the bot from the project root:
```bash
cd /path/to/QBot
./run-bot.sh
```

**Check:** The script should exist at `python/sdk_bridge.py`

---

### 3. "NO real historical data available"

**What it means:** Cannot load historical market data for ES/NQ contracts.

**Solutions (in order):**

1. **Install TopstepX SDK:**
   ```bash
   pip install 'project-x-py[all]'
   ```

2. **Set environment variables:**
   ```bash
   export TOPSTEPX_API_KEY="your_key_here"
   export TOPSTEPX_USERNAME="your_email@example.com"
   ```
   Or add to `.env` file:
   ```
   TOPSTEPX_API_KEY=your_key_here
   TOPSTEPX_USERNAME=your_email@example.com
   ```

3. **Verify network connectivity:**
   ```bash
   curl -I https://api.topstepx.com
   ```

4. **Check SDK bridge exists:**
   ```bash
   ls -la python/sdk_bridge.py
   ```

---

### 4. "TopstepX SDK adapter initialization failed"

**What it means:** The Python SDK validation failed.

**Solutions:**

1. **Check Python version:**
   ```bash
   python3 --version  # Should be 3.8+
   ```

2. **Reinstall SDK:**
   ```bash
   pip uninstall project-x-py
   pip install 'project-x-py[all]'
   ```

3. **Verify installation:**
   ```bash
   python3 -c "import project_x_py; print('SDK OK')"
   ```

---

## 🔍 Checking System Status

### Verify SDK Installation
```bash
./run-bot.sh --check-sdk
```

### Check Environment Variables
```bash
# Should show [SET] or actual values
echo "API_KEY: ${TOPSTEPX_API_KEY:+[SET]}"
echo "USERNAME: $TOPSTEPX_USERNAME"
```

### Test Python SDK Directly
```bash
python3 -c "import project_x_py" && echo "SDK installed" || echo "SDK missing"
```

### View Bot Logs with Filtering
```bash
# Run bot and filter for errors
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj 2>&1 | grep -E "ERROR|WARN|SDK|HISTORICAL"
```

---

## 📊 Understanding Log Messages

### ✅ Success Messages
- `✅ TopstepX SDK validated successfully` - SDK is working
- `✅ [HISTORICAL-BRIDGE] Seeded X historical bars` - Historical data loaded
- `✅ TopstepX adapter initialized successfully` - WebSocket connections ready

### ⚠️ Warning Messages (Not Critical)
- `⚠️ [SDK-VALIDATION] TopstepX SDK not available` - Running in degraded mode
- `⚠️ [HISTORICAL-BRIDGE] Historical data unavailable` - No real data, using fallbacks
- `⚠️ [AUTONOMOUS-ENGINE] No historical data available` - Fresh start with zero trades

### ❌ Error Messages (Need Attention)
- `❌ [SDK-VALIDATION] project-x-py SDK not found` - Install SDK required
- `❌ [HISTORICAL-BRIDGE] NO real historical data available` - Fix SDK/credentials
- `RuntimeError: project-x-py SDK is required` - Critical: install SDK

---

## 🛠️ Development Helpers

### Build and Run Quickly
```bash
# Use dev-helper script
./dev-helper.sh build
./dev-helper.sh run
```

### Check for Build Errors
```bash
dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
```

### Run with Detailed Logs
```bash
./run-bot.sh --with-logs
# Logs saved to: logs/bot-run-YYYYMMDD-HHMMSS.log
```

---

## 📚 Additional Resources

- **RUNBOOKS.md** - Operational procedures and daily checklists
- **TOPSTEPX_ADAPTER_SETUP_GUIDE.md** - Detailed SDK setup instructions
- **README.md** - Full system documentation

---

## 🆘 Still Having Issues?

If you're still experiencing problems:

1. **Check all prerequisites:**
   - [ ] Python 3.8+ installed
   - [ ] .NET 8.0 SDK installed
   - [ ] TopstepX SDK installed
   - [ ] Environment variables set
   - [ ] Running from project root

2. **Collect diagnostic info:**
   ```bash
   # System info
   python3 --version
   dotnet --version
   
   # SDK status
   python3 -c "import project_x_py" 2>&1
   
   # Environment
   env | grep TOPSTEPX
   ```

3. **Review the full error log:**
   ```bash
   ./run-bot.sh --with-logs
   # Then check: logs/bot-run-*.log
   ```

4. **Try degraded mode:**
   - The bot should still run without the SDK
   - ML/RL models work offline
   - Useful for testing non-live features
