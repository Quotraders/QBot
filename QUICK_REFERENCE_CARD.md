# 🎯 QBot + Copilot Debugging - Quick Reference Card

**Save this or print it for quick access!**

---

## 🚀 Launch Commands

```powershell
# QUICK LAUNCH (Windows)
.\quick-launch.ps1

# DIAGNOSTIC MODE (Windows, with logs)
.\launch-bot-diagnostic.ps1 -RuntimeMinutes 5

# LINUX/MACOS
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj

# VS CODE (any platform)
Press F5
```

---

## ⚙️ One-Time Setup

```powershell
# 1. Install prerequisites
# - .NET 8.0+ SDK: https://dotnet.microsoft.com/download
# - Python 3.12+: https://www.python.org/downloads/
# - TopstepX SDK: pip install 'project-x-py[all]>=3.5.0'

# 2. Configure .env file
# Copy .env.example to .env and add your credentials

# 3. Setup VS Code (optional)
Copy-Item .vscode-template/* .vscode/  # Windows
cp -r .vscode-template/* .vscode/      # Linux/macOS
```

---

## 🔧 Common Issues & Quick Fixes

| Issue | Quick Fix |
|-------|-----------|
| **Python not found** | Update `PYTHON_EXECUTABLE` in `.env` |
| **Auth failed (401)** | Remove `TOPSTEPX_JWT` from `.env` |
| **Build failed** | `dotnet clean && dotnet restore && dotnet build` |
| **WebSocket SSL error** | Add `PYTHONHTTPSVERIFY=0` to `.env` (dev only) |
| **VS Code can't build** | Install C# Dev Kit extension |

---

## 💬 How to Ask Copilot for Help

### ✅ GOOD Example

```markdown
@copilot Bot authentication failing

**Error:** 
[ERROR] TopstepX Authentication failed: HTTP 401

**Environment:**
- Windows 11
- .NET 9.0.305
- Python 3.12

**Logs:**
[paste first 100 lines]

What should I check?
```

### ❌ BAD Example

```markdown
@copilot it doesn't work
```

---

## 📚 Documentation Quick Links

| Document | Purpose |
|----------|---------|
| **[SOLUTION_SUMMARY_COPILOT_DEBUGGING.md](SOLUTION_SUMMARY_COPILOT_DEBUGGING.md)** | Complete overview |
| **[QUICK_START_COPILOT.md](QUICK_START_COPILOT.md)** | Fast start guide |
| **[COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](COPILOT_REAL_TIME_DEBUGGING_GUIDE.md)** | Full reference |
| **[COPILOT_DEBUGGING_EXAMPLE.md](COPILOT_DEBUGGING_EXAMPLE.md)** | Real examples |

---

## 🎯 3 Ways to Debug

### 1️⃣ GitHub Actions (Automated)
- Go to Actions → "Bot Launch Diagnostics"
- Click "Run workflow"
- Download logs, share with Copilot

### 2️⃣ Local Launch (Quick)
- Run: `.\launch-bot-diagnostic.ps1`
- Share logs with Copilot

### 3️⃣ VS Code (Deep Dive)
- Copy `.vscode-template/*` to `.vscode/`
- Press F5, set breakpoints
- Use Copilot Chat (Ctrl+Shift+I)

---

## ✅ Success Indicators

```
✅ Bootstrap: All directories created
✅ Configuration loaded from .env
✅ TopstepX Authentication successful
✅ WebSocket connected
✅ UnifiedOrchestrator started
✅ Market data streaming
```

---

## 🔍 VS Code Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| **Start debugging** | `F5` |
| **Run task** | `Ctrl+Shift+B` |
| **Open Copilot Chat** | `Ctrl+Shift+I` |
| **Command palette** | `Ctrl+Shift+P` |
| **Toggle terminal** | `` Ctrl+` `` |

---

## 📞 Getting Help

1. Check common issues in [QUICK_START_COPILOT.md](QUICK_START_COPILOT.md)
2. Review examples in [COPILOT_DEBUGGING_EXAMPLE.md](COPILOT_DEBUGGING_EXAMPLE.md)
3. Create GitHub issue with logs
4. Ask: "@copilot [your specific question]"

---

## 🎓 Pro Tips

1. **Always provide context** - error messages, config, environment
2. **Use code blocks** - format logs and code properly
3. **Test suggestions** - try fixes and report back
4. **Iterate** - ask follow-up questions
5. **Be specific** - "Auth failed with 401" > "doesn't work"

---

**Keep this card handy for quick reference! 🚀**

**Last Updated:** 2025-10-17
