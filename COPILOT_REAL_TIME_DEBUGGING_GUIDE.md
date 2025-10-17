# 🤖 GitHub Copilot Real-Time Bot Debugging Guide

## 🎯 Purpose

This guide provides **comprehensive instructions** for enabling GitHub Copilot to help you launch, monitor, and debug your TopstepX trading bot in **real-time**. Since Copilot cannot directly hit APIs from its environment, this document outlines multiple strategies to bridge that gap.

---

## 📋 Table of Contents

1. [Quick Summary of Your Options](#quick-summary-of-your-options)
2. [Option 1: GitHub Actions Self-Hosted Runner (RECOMMENDED)](#option-1-github-actions-self-hosted-runner-recommended)
3. [Option 2: Local Launch with Log Sharing](#option-2-local-launch-with-log-sharing)
4. [Option 3: VS Code with Copilot Chat Integration](#option-3-vs-code-with-copilot-chat-integration)
5. [Option 4: Continuous Diagnostic Mode](#option-4-continuous-diagnostic-mode)
6. [Understanding Your Bot Architecture](#understanding-your-bot-architecture)
7. [Troubleshooting Common Issues](#troubleshooting-common-issues)

---

## 🚀 Quick Summary of Your Options

| Option | Pros | Cons | Best For |
|--------|------|------|----------|
| **GitHub Actions Self-Hosted Runner** | ✅ Fully automated<br>✅ Copilot can see logs<br>✅ Artifact downloads<br>✅ Real TopstepX APIs | ⚠️ Requires self-hosted runner setup | **Production-like testing** |
| **Local Launch + Log Sharing** | ✅ Full control<br>✅ Real-time monitoring<br>✅ Easy to iterate | ⚠️ Manual copy/paste logs<br>⚠️ Requires sharing output | **Quick debugging sessions** |
| **VS Code + Copilot Chat** | ✅ IDE integration<br>✅ Direct file access<br>✅ Code suggestions | ⚠️ Limited to code fixes<br>⚠️ Can't run bot for you | **Code review and fixes** |
| **Continuous Diagnostic Mode** | ✅ Structured logs<br>✅ JSON exports<br>✅ Health monitoring | ⚠️ Requires log review<br>⚠️ Not real-time | **Performance analysis** |

---

## 🏆 Option 1: GitHub Actions Self-Hosted Runner (RECOMMENDED)

### Why This is Best
- **Real APIs**: Bot runs on your machine with actual TopstepX credentials
- **Copilot Can Help**: Logs are uploaded as artifacts that I can analyze
- **No Sandbox Limits**: Your runner has full network access
- **Production-Ready**: Test exactly how it will run in production

### Setup Steps

#### 1. Set Up Self-Hosted Runner (One-Time)

```bash
# On your Windows machine:
# Go to: https://github.com/Quotraders/QBot/settings/actions/runners/new

# Follow the instructions to download and configure the runner
# Make sure to:
# - Select Windows as the OS
# - Run configure.cmd with --labels "self-hosted,Windows,X64,bot-testing"
# - Install as a service (run-service.cmd)
```

#### 2. Verify Runner Configuration

Check that your `.env` file has correct paths:

```bash
# In your QBot/.env file:
PYTHON_EXECUTABLE=C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
TOPSTEPX_API_KEY=J3pePdNU/mvmoRGTygBcNtKbRvL/wSNZ3pFOKCdIy34=
TOPSTEPX_USERNAME=kevinsuero072897@gmail.com
TOPSTEPX_ACCOUNT_ID=297693
```

#### 3. Launch Bot via GitHub Actions

**Already configured!** Use the existing workflow:

```bash
# In GitHub UI:
# 1. Go to Actions tab
# 2. Select "🤖 Bot Launch Diagnostics - Self-Hosted"
# 3. Click "Run workflow"
# 4. Configure:
#    - runtime_minutes: 5 (or longer)
#    - detailed_logging: true
# 5. Click "Run workflow"
```

#### 4. Download and Share Logs

After the workflow completes:

```bash
# 1. Scroll to bottom of workflow run page
# 2. Download "bot-diagnostics-run-{number}.zip"
# 3. Extract the ZIP
# 4. Share relevant logs in the issue/PR:
#    - console-output-*.log (startup sequence)
#    - error-output-*.log (errors only)
#    - structured-log-*.json (parsed events)
```

#### 5. Ask Copilot to Analyze

Once logs are shared, ask:
- "Analyze the bot-diagnostics logs and identify startup failures"
- "Why is the TopstepX connection failing?"
- "What's causing the authentication error in the logs?"
- "Summarize the startup sequence and where it's failing"

### 📊 What Gets Captured

- ✅ Complete console output with timestamps
- ✅ All error messages and stack traces
- ✅ TopstepX API connection attempts
- ✅ Python SDK initialization
- ✅ Market data subscription status
- ✅ System environment information
- ✅ Structured JSON events for analysis

---

## 💻 Option 2: Local Launch with Log Sharing

### When to Use
- Quick debugging sessions
- Testing configuration changes
- Iterating on fixes rapidly

### Steps

#### 1. Launch Bot Locally

```powershell
# In PowerShell (Windows):
cd C:\path\to\QBot
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj

# The bot will:
# - Load configuration from .env
# - Connect to TopstepX APIs
# - Start the UnifiedOrchestrator
# - Display startup logs in console
```

#### 2. Capture Output to File

```powershell
# Option A: Redirect to file
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj 2>&1 | Tee-Object -FilePath bot-output.log

# Option B: Use the existing run-bot script
.\run-bot.sh
```

#### 3. Share Output with Copilot

**In GitHub Issue or PR:**

```markdown
I launched the bot and got this output:

<details>
<summary>Bot Startup Logs (click to expand)</summary>

```
[paste your console output here]
```

</details>

@copilot Can you analyze this and tell me:
1. What's causing the connection failure?
2. What configuration needs to be fixed?
3. How can I resolve the TopstepX authentication error?
```

### 📝 Key Log Sections to Share

When sharing logs, focus on:

1. **Startup sequence** (first 50-100 lines)
2. **Error messages** (look for "error", "exception", "failed")
3. **TopstepX connection** (search for "TopstepX", "authentication", "API")
4. **Python SDK** (search for "Python", "adapter", "SDK")

---

## 🔧 Option 3: VS Code with Copilot Chat Integration

### When to Use
- Code review and suggestions
- Understanding complex code
- Getting implementation guidance
- Real-time code completion

### Setup Steps

#### 1. Ensure VS Code Extensions are Installed

```bash
# Required extensions:
- GitHub Copilot
- GitHub Copilot Chat
- C# Dev Kit
- .NET Runtime
```

#### 2. Open Project in VS Code

```powershell
cd C:\path\to\QBot
code .
```

#### 3. Use Copilot Chat for Debugging

**Open Copilot Chat (Ctrl+Shift+I) and ask:**

```
@workspace How do I launch the UnifiedOrchestrator to connect to TopstepX APIs?
```

```
@workspace Where is the TopstepX authentication configured?
```

```
@workspace What's the correct way to set up the Python SDK adapter?
```

```
#file:Program.cs Explain what happens during bot startup
```

#### 4. Debug Launch Configuration

Create `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch UnifiedOrchestrator",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/UnifiedOrchestrator/bin/Debug/net8.0/UnifiedOrchestrator.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "console": "integratedTerminal",
      "stopAtEntry": false,
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      },
      "envFile": "${workspaceFolder}/.env"
    }
  ]
}
```

#### 5. Set Breakpoints and Debug

1. Open `src/UnifiedOrchestrator/Program.cs`
2. Set breakpoints at key locations:
   - Line ~89: Bootstrap initialization
   - Line ~200: Service registration
   - TopstepXAdapterService constructor
3. Press F5 to start debugging
4. Use Copilot Chat to understand what's happening at each breakpoint

### 💡 Copilot Chat Commands for Debugging

```
/explain - Explain the current code block
/fix - Suggest fixes for the selected code
/tests - Generate tests for the selected code
/doc - Generate documentation
```

**Example workflow:**
1. Hit breakpoint in TopstepXAdapterService
2. Select the authentication code
3. In chat: `/explain` or "Why is this authentication failing?"
4. Copilot will analyze the code and suggest fixes

---

## 📈 Option 4: Continuous Diagnostic Mode

### When to Use
- Long-running performance analysis
- Production monitoring simulation
- Automated health checks

### Implementation

#### 1. Enable Enhanced Logging

Add to your `.env`:

```bash
# Enhanced logging configuration
ASPNETCORE_ENVIRONMENT=Development
Logging__LogLevel__Default=Debug
Logging__LogLevel__Microsoft=Information
Logging__LogLevel__TradingBot=Debug

# Export logs to file
ENABLE_FILE_LOGGING=true
LOG_FILE_PATH=logs/bot-{Date}.log
```

#### 2. Create Diagnostic Launch Script

Create `launch-diagnostic-mode.ps1`:

```powershell
# Diagnostic Mode Launcher
$ErrorActionPreference = "Continue"

# Create logs directory
New-Item -ItemType Directory -Force -Path "logs"

# Launch with full diagnostics
$env:ASPNETCORE_ENVIRONMENT = "Development"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logFile = "logs/bot-diagnostic-$timestamp.log"

Write-Host "Starting bot in diagnostic mode..."
Write-Host "Logs will be written to: $logFile"

dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj 2>&1 | Tee-Object -FilePath $logFile

Write-Host "`nDiagnostic run complete. Check $logFile for details."
```

#### 3. Launch and Monitor

```powershell
.\launch-diagnostic-mode.ps1

# In another terminal, monitor in real-time:
Get-Content logs/bot-diagnostic-*.log -Wait -Tail 50
```

#### 4. Share Diagnostic Report

After running for desired duration:

```bash
# Compress logs
Compress-Archive -Path logs/*.log -DestinationPath bot-diagnostics-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip

# Upload to GitHub issue or attach to PR
```

---

## 🏗️ Understanding Your Bot Architecture

### Component Overview

```
┌─────────────────────────────────────────┐
│      UnifiedOrchestrator (Main)         │
│  - Entry point: Program.cs              │
│  - DI Container & Service Registration  │
└────────────────┬────────────────────────┘
                 │
    ┌────────────┴────────────┬─────────────────────┐
    │                         │                     │
┌───▼────────────────┐ ┌─────▼──────────────┐ ┌───▼─────────────────┐
│ TopstepXAdapter    │ │ UnifiedTradingBrain│ │ MarketDataService   │
│ - Python SDK Bridge│ │ - ML/RL Decision   │ │ - Real-time quotes  │
│ - REST API Client  │ │ - Strategy Engine  │ │ - Historical data   │
└────────────────────┘ └────────────────────┘ └─────────────────────┘
         │
    ┌────▼────────────────────────────┐
    │  TopstepX API (External)        │
    │  - api.topstepx.com             │
    │  - rtc.topstepx.com (WebSocket) │
    └─────────────────────────────────┘
```

### Key Files

| File | Purpose | Why It Matters |
|------|---------|----------------|
| `src/UnifiedOrchestrator/Program.cs` | Main entry point, DI setup | **Where everything starts** |
| `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs` | Manages Python SDK process | **TopstepX connectivity** |
| `src/adapters/topstep_x_adapter.py` | Python bridge to project-x-py SDK | **API communication** |
| `.env` | Configuration & credentials | **Authentication settings** |
| `appsettings.json` | App configuration | **Service settings** |

### Startup Sequence

```
1. Program.cs Bootstrap()
   ↓ Creates required directories
   
2. Load Configuration
   ↓ Read .env file
   ↓ Read appsettings.json
   
3. Build DI Container
   ↓ Register all services
   ↓ TopstepXAdapterService constructor runs
   
4. TopstepXAdapterService Initialization
   ↓ Validate Python executable exists
   ↓ Check project-x-py SDK installed
   ↓ Launch persistent Python process
   
5. Python Adapter Startup
   ↓ Import project-x-py SDK
   ↓ Authenticate with TopstepX
   ↓ Subscribe to market data
   
6. UnifiedOrchestrator Started
   ↓ Begin trading operations
```

---

## 🔍 Troubleshooting Common Issues

### Issue 1: "Failed to start persistent Python process"

**Symptoms:**
```
[ERROR] TopstepXAdapterService: Failed to start persistent Python process
Win32Exception: No such file or directory
```

**Solutions:**

1. **Verify Python path in .env:**
   ```bash
   # Check current setting:
   PYTHON_EXECUTABLE=C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
   
   # Verify it exists:
   Test-Path "C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe"
   ```

2. **Check Python SDK installed:**
   ```bash
   pip show project-x-py
   # Should show version >= 3.5.0
   ```

3. **Try alternative Python paths:**
   ```bash
   # Option 1: System Python
   where python
   
   # Option 2: Python3 explicitly
   where python3
   
   # Update .env with working path
   ```

### Issue 2: "TopstepX authentication failed"

**Symptoms:**
```
[ERROR] Authentication failed with TopstepX API
HTTP 401 Unauthorized
```

**Solutions:**

1. **Verify credentials in .env:**
   ```bash
   TOPSTEPX_API_KEY=J3pePdNU/mvmoRGTygBcNtKbRvL/wSNZ3pFOKCdIy34=
   TOPSTEPX_USERNAME=kevinsuero072897@gmail.com
   TOPSTEPX_ACCOUNT_ID=297693
   ```

2. **Test credentials directly:**
   ```bash
   cd python
   python -c "from project_x_py import TopstepXClient; c=TopstepXClient(api_key='YOUR_KEY'); print(c.authenticate('YOUR_EMAIL'))"
   ```

3. **Check for expired JWT:**
   ```bash
   # Remove old JWT from .env:
   # TOPSTEPX_JWT=...  <-- Delete this line
   
   # Bot will auto-generate new JWT on next startup
   ```

### Issue 3: "WebSocket connection failed (SSL/TLS)"

**Symptoms:**
```
[ERROR] WebSocket connection to rtc.topstepx.com failed
SSL: CERTIFICATE_VERIFY_FAILED
```

**Solutions:**

1. **Temporary workaround (development only):**
   ```bash
   # Add to .env:
   PYTHONHTTPSVERIFY=0
   SSL_CERT_FILE=
   REQUESTS_CA_BUNDLE=
   ```

2. **Proper fix (production):**
   ```bash
   # Use REST polling instead of WebSocket:
   TOPSTEPX_ADAPTER_MODE=polling
   POLLING_INTERVAL_MS=500
   ```

### Issue 4: "Bot builds but crashes immediately"

**Symptoms:**
```
Unhandled exception. System.NullReferenceException
```

**Debugging steps:**

1. **Check .env file exists:**
   ```powershell
   Test-Path .env
   ```

2. **Validate JSON configuration files:**
   ```bash
   # Test each config file:
   Get-Content appsettings.json | ConvertFrom-Json
   Get-Content state/runtime-overrides.json | ConvertFrom-Json
   ```

3. **Run with verbose logging:**
   ```bash
   $env:Logging__LogLevel__Default = "Trace"
   dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
   ```

### Issue 5: "VS Code can't build the project"

**Solutions:**

1. **Install .NET SDK:**
   ```bash
   # Check current version:
   dotnet --version
   # Should be 8.0.x or higher
   
   # Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   ```

2. **Restore packages:**
   ```bash
   dotnet restore TopstepX.Bot.sln
   ```

3. **Clean and rebuild:**
   ```bash
   dotnet clean
   dotnet build
   ```

---

## 🎯 Best Practices for Working with Copilot

### 1. Provide Context

**Instead of:**
```
"The bot isn't working"
```

**Say:**
```
"I'm trying to launch UnifiedOrchestrator to connect to TopstepX APIs. 
I get this error: [paste error]. 

My environment:
- Windows 11
- .NET 9.0.305
- Python 3.12
- TopstepX credentials configured in .env

What should I check?"
```

### 2. Share Relevant Logs

**Always include:**
- First 50 lines (startup sequence)
- Error messages and stack traces
- Last 20 lines (what happened right before failure)

**Format:**
```markdown
<details>
<summary>Bot Startup Logs</summary>

```
[paste logs here]
```

</details>
```

### 3. Ask Specific Questions

**Good questions:**
- "Why is TopstepXAdapterService failing to start the Python process?"
- "What's the correct .env configuration for TopstepX authentication?"
- "How do I fix the SSL certificate error when connecting to rtc.topstepx.com?"

**Avoid:**
- "Fix it"
- "It's broken"
- "Help"

### 4. Iterate Incrementally

After each fix:
1. Test the change
2. Share the new output
3. Ask: "Did this fix the issue or are there new errors?"

---

## 🚀 Quick Start Checklist

Before asking Copilot for help, verify:

- [ ] .NET 8.0+ SDK installed (`dotnet --version`)
- [ ] Python 3.12+ installed (`python --version`)
- [ ] project-x-py SDK installed (`pip show project-x-py`)
- [ ] .env file exists with TopstepX credentials
- [ ] UnifiedOrchestrator builds successfully (`dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj`)
- [ ] Python executable path is correct in .env
- [ ] Network access to api.topstepx.com (check firewall)

---

## 📞 How to Ask for Help

### Template for GitHub Issues/PRs

```markdown
## Issue Description
[Brief description of what's not working]

## Environment
- OS: Windows 11 / Linux / macOS
- .NET Version: [run `dotnet --version`]
- Python Version: [run `python --version`]
- TopstepX Account Type: Practice / Live

## Steps to Reproduce
1. Launch bot: `dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj`
2. [What happens]

## Logs
<details>
<summary>Console Output (click to expand)</summary>

```
[paste complete output here]
```

</details>

## Expected Behavior
[What should happen]

## Actual Behavior
[What actually happens]

@copilot Can you help me understand what's wrong and how to fix it?
```

---

## 📚 Additional Resources

- **[Quick Start Guide](QUICK_START_COPILOT.md)** - TL;DR version to get started fast
- **[Example Walkthrough](COPILOT_DEBUGGING_EXAMPLE.md)** - Real-world debugging scenarios
- **TopstepX API Docs**: https://docs.topstepx.com/
- **project-x-py SDK**: https://github.com/topstepx/project-x-py
- **Bot Architecture**: See `PRODUCTION_ARCHITECTURE.md`
- **Configuration Guide**: See `TOPSTEPX_ADAPTER_SETUP_GUIDE.md`
- **Troubleshooting**: See `RUNBOOKS.md`

---

## ✅ Success Indicators

You'll know the bot is working correctly when you see:

```
✅ Bootstrap: All directories created
✅ Configuration loaded from .env
✅ TopstepXAdapterService: Python SDK validated
✅ TopstepXAdapterService: Persistent process started
✅ TopstepX Authentication successful
✅ WebSocket connected to rtc.topstepx.com
✅ UnifiedOrchestrator started
✅ Market data subscription active
```

---

## 🎉 Conclusion

You have **multiple options** for enabling Copilot to help you debug your bot in real-time:

1. **Best for production testing**: GitHub Actions with self-hosted runner
2. **Best for quick iterations**: Local launch with log sharing
3. **Best for code understanding**: VS Code with Copilot Chat
4. **Best for monitoring**: Continuous diagnostic mode

**Recommended Workflow:**
1. Start with **Option 1** (GitHub Actions) to capture comprehensive diagnostics
2. Use **Option 2** (Local launch) for rapid iteration on fixes
3. Use **Option 3** (VS Code) for understanding and modifying code
4. Use **Option 4** (Diagnostic mode) for long-running performance analysis

**Remember**: The more context and logs you provide, the better Copilot can help you! 🚀
