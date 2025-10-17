# 🚀 Quick Start: Launch Your Bot with Copilot Help

This is your **TL;DR** guide to getting your bot running and enabling GitHub Copilot to help you debug it in real-time.

---

## ⚡ Super Fast Start (2 Minutes)

### Windows (PowerShell)
```powershell
# Clone and navigate (if not already there)
cd C:\path\to\QBot

# Quick launch
.\quick-launch.ps1
```

### Linux/macOS (Bash)
```bash
# Navigate to your repo
cd /path/to/QBot

# Build and run
dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
```

### VS Code (Any Platform)
1. Open folder in VS Code: `code .`
2. Press **F5** to launch with debugger
3. Or use **Terminal → Run Task → run-orchestrator**

---

## 📋 Prerequisites Checklist

Before you start, make sure you have:

- [ ] **.NET 8.0+ SDK** installed → `dotnet --version`
- [ ] **Python 3.12+** installed → `python --version`  
- [ ] **project-x-py SDK** installed → `pip install 'project-x-py[all]>=3.5.0'`
- [ ] **.env file** configured with your TopstepX credentials
- [ ] **VS Code** (optional, but recommended for best experience)

---

## 🎯 Your 3 Best Options

### 1️⃣ Self-Hosted Runner (BEST for real-time Copilot help)

**Why**: Copilot can see logs, analyze issues, and suggest fixes automatically.

**Steps**:
1. Go to [Actions tab](https://github.com/Quotraders/QBot/actions)
2. Select **"🤖 Bot Launch Diagnostics - Self-Hosted"**
3. Click **"Run workflow"**
4. Wait for completion, download logs
5. Ask Copilot to analyze: *"@copilot analyze bot-diagnostics logs"*

**More info**: See [COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](COPILOT_REAL_TIME_DEBUGGING_GUIDE.md#option-1-github-actions-self-hosted-runner-recommended)

---

### 2️⃣ Local Launch with Log Sharing (FASTEST for iteration)

**Why**: Full control, real-time monitoring, immediate feedback.

**Quick command**:
```powershell
# Windows - Diagnostic mode with log capture
.\launch-bot-diagnostic.ps1 -RuntimeMinutes 5

# Or simple mode - just run it
.\quick-launch.ps1
```

**Share logs with Copilot**:
```markdown
@copilot I launched the bot and got this output:

[paste your console output here]

What's wrong and how do I fix it?
```

**More info**: See [COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](COPILOT_REAL_TIME_DEBUGGING_GUIDE.md#option-2-local-launch-with-log-sharing)

---

### 3️⃣ VS Code Debugging (BEST for code fixes)

**Why**: IDE integration, breakpoints, inline Copilot suggestions.

**Steps**:
1. Open QBot folder in VS Code
2. Set breakpoints in code (e.g., `Program.cs`, `TopstepXAdapterService.cs`)
3. Press **F5** or select **"Launch UnifiedOrchestrator (Debug)"**
4. Use Copilot Chat: **Ctrl+Shift+I** to ask questions

**Copilot Chat examples**:
```
@workspace How do I fix the TopstepX authentication error?
```
```
#file:TopstepXAdapterService.cs Explain what this service does
```
```
/explain [select code] → Get explanation of selected code
```

**More info**: See [COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](COPILOT_REAL_TIME_DEBUGGING_GUIDE.md#option-3-vs-code-with-copilot-chat-integration)

---

## 🛠️ Files We Created for You

| File | Purpose | Use It For |
|------|---------|------------|
| **quick-launch.ps1** | Simple bot launcher | Quick testing without logs |
| **launch-bot-diagnostic.ps1** | Diagnostic mode with logs | Detailed debugging sessions |
| **.vscode/launch.json** | VS Code debug configs | F5 debugging in VS Code |
| **.vscode/tasks.json** | Build/run tasks | Quick commands in VS Code |
| **COPILOT_REAL_TIME_DEBUGGING_GUIDE.md** | Complete guide | Understanding all options |

---

## 🔧 Common Issues (Quick Fixes)

### "Python not found"
```powershell
# Check your .env file
PYTHON_EXECUTABLE=C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe

# Verify it exists
Test-Path "C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe"
```

### "TopstepX authentication failed"
```powershell
# Verify credentials in .env
TOPSTEPX_API_KEY=your_api_key_here
TOPSTEPX_USERNAME=your_email@example.com
TOPSTEPX_ACCOUNT_ID=your_account_id
```

### "Build failed"
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
```

### "VS Code can't find .NET"
```bash
# Install .NET 8.0 SDK from:
https://dotnet.microsoft.com/download/dotnet/8.0
```

---

## 🎓 How to Ask Copilot for Help

### ✅ Good Questions

```markdown
@copilot I'm getting this error when launching the bot:

[ERROR] TopstepXAdapterService: Failed to start persistent Python process

Here's my .env configuration:
PYTHON_EXECUTABLE=C:\Users\kevin\...\python.exe

And the full console output:
[paste output here]

What should I check?
```

### ❌ Avoid

- "Fix it" (too vague)
- "It doesn't work" (no context)
- "Help" (no details)

### 💡 Pro Tip

The more context you provide (error messages, configuration, logs), the better Copilot can help!

---

## 📚 Next Steps

1. **Try launching locally**: `.\quick-launch.ps1`
2. **If issues occur**: Share logs with Copilot
3. **For deep debugging**: Use VS Code with F5
4. **For automated testing**: Set up self-hosted runner

---

## 🆘 Need More Help?

- **Comprehensive guide**: [COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](COPILOT_REAL_TIME_DEBUGGING_GUIDE.md)
- **Bot architecture**: See the guide's "Understanding Your Bot Architecture" section
- **Troubleshooting**: See the guide's "Troubleshooting Common Issues" section
- **Ask Copilot**: Create a GitHub issue with logs and tag `@copilot`

---

## ✅ Success Looks Like

When your bot is working correctly, you'll see:

```
✅ Bootstrap: All directories created
✅ Configuration loaded from .env
✅ TopstepXAdapterService: Python SDK validated
✅ TopstepXAdapterService: Persistent process started
✅ TopstepX Authentication successful
✅ WebSocket connected to rtc.topstepx.com
✅ UnifiedOrchestrator started
```

---

**You're all set!** Pick an option above and get started. Remember, Copilot is here to help you debug in real-time! 🚀
