# 🎯 Example Walkthrough: Debugging with Copilot

This document shows a **real-world example** of how to use GitHub Copilot to debug your bot when things go wrong.

---

## Scenario: Bot Won't Connect to TopstepX

You try to launch your bot and get an authentication error. Here's how to use Copilot to help you fix it.

---

### Step 1: Launch the Bot and Capture Output

```powershell
# Windows - Launch with diagnostic logging
.\launch-bot-diagnostic.ps1 -RuntimeMinutes 2
```

**Output you see:**
```
[INFO] Checking prerequisites...
[SUCCESS] .NET SDK detected: 9.0.305
[SUCCESS] Python detected: Python 3.12.0
[SUCCESS] .env file found
[SUCCESS] TopstepX API key found in .env
[INFO] Building UnifiedOrchestrator...
[SUCCESS] Build successful
[INFO] Logs will be saved to:
  - Console: logs/bot-diagnostic-20251017-043000.log
  - Errors:  logs/bot-errors-20251017-043000.log

[INFO] Starting UnifiedOrchestrator...

info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepXAdapterService: Validating Python SDK...
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Platform: Native Windows (not WSL)
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Python executable: C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
fail: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepX Authentication failed: HTTP 401 Unauthorized
fail: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Failed to start persistent Python process
```

---

### Step 2: Create GitHub Issue with Context

Go to your repository and create a new issue:

````markdown
## 🐛 Bot Authentication Failing with TopstepX

### Environment
- OS: Windows 11
- .NET Version: 9.0.305
- Python Version: 3.12.0
- TopstepX Account: Practice Account (PRAC-V2-297693-73603697)

### What I Did
1. Ran: `.\launch-bot-diagnostic.ps1 -RuntimeMinutes 2`
2. Bot starts building successfully
3. Fails during TopstepX authentication

### Error Output

<details>
<summary>Full Console Output (click to expand)</summary>

```
[INFO] Starting UnifiedOrchestrator...

info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepXAdapterService: Validating Python SDK...
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Platform: Native Windows (not WSL)
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Python executable: C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
fail: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepX Authentication failed: HTTP 401 Unauthorized
fail: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Failed to start persistent Python process
```

</details>

### My .env Configuration

```bash
TOPSTEPX_API_KEY=J3pePdNU/mvmoRGTygBcNtKbRvL/wSNZ3pFOKCdIy34=
TOPSTEPX_USERNAME=kevinsuero072897@gmail.com
TOPSTEPX_ACCOUNT_ID=297693
TOPSTEPX_ACCOUNT_NAME=PRAC-V2-297693-73603697
```

### Expected Behavior
Bot should authenticate successfully and connect to TopstepX WebSocket

### Actual Behavior
Gets HTTP 401 Unauthorized during authentication

@copilot Can you help me understand what's wrong with my authentication setup?
````

---

### Step 3: Copilot Analyzes the Issue

Copilot responds:

> Based on your error logs and configuration, I see a few potential issues:
>
> 1. **HTTP 401 typically means invalid credentials**. Let me check your setup:
>    - Your API key looks valid (base64 encoded)
>    - Username is provided
>    - Account ID is provided
>
> 2. **Possible causes**:
>    - The API key might be expired or revoked
>    - The account might be inactive
>    - There might be an old JWT token cached that's now invalid
>
> 3. **Try these fixes**:
>
> **Fix 1: Clear cached JWT**
> ```bash
> # Open your .env file and remove this line if it exists:
> # TOPSTEPX_JWT=eyJ...
> 
> # The bot will generate a fresh token on next startup
> ```
>
> **Fix 2: Verify SDK installation**
> ```bash
> pip show project-x-py
> # Should show version >= 3.5.0
> ```
>
> **Fix 3: Test authentication directly**
> ```python
> from project_x_py import TopstepXClient
> 
> client = TopstepXClient(api_key='J3pePdNU/mvmoRGTygBcNtKbRvL/wSNZ3pFOKCdIy34=')
> result = client.authenticate('kevinsuero072897@gmail.com')
> print(f"Auth result: {result}")
> ```
>
> Let me know what you find!

---

### Step 4: You Try the Fixes

You check your .env and find:

```bash
# Your .env file
TOPSTEPX_JWT=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI...
```

Aha! There's an old JWT token. You remove it:

```bash
# Remove or comment out this line:
# TOPSTEPX_JWT=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

### Step 5: Test the Fix

```powershell
.\quick-launch.ps1
```

**New output:**
```
[*] Checking prerequisites...
[✓] .NET SDK found
[✓] .env file found
[✓] UnifiedOrchestrator project found
[*] Building UnifiedOrchestrator...
[✓] Build successful

╔════════════════════════════════════════════╗
║     Starting UnifiedOrchestrator...        ║
║     Press Ctrl+C to stop                   ║
╚════════════════════════════════════════════╝

info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepXAdapterService: Validating Python SDK...
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      Python executable: C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      TopstepX Authentication successful ✅
info: TradingBot.UnifiedOrchestrator.Services.TopstepXAdapterService[0]
      WebSocket connected to rtc.topstepx.com ✅
info: TradingBot.UnifiedOrchestrator.Services.UnifiedOrchestratorService[0]
      UnifiedOrchestrator started ✅
```

**Success!** 🎉

---

### Step 6: Report Back to Copilot

Update your issue:

```markdown
@copilot Perfect! That fixed it! 

The problem was an old cached JWT token in my .env file. After removing:
```
TOPSTEPX_JWT=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

The bot authenticated successfully on the next launch.

**Working output:**
- ✅ TopstepX Authentication successful
- ✅ WebSocket connected to rtc.topstepx.com
- ✅ UnifiedOrchestrator started

Thank you!
```

---

## 🎓 Key Takeaways

### What Made This Successful

1. **Provided complete context**
   - Environment details (OS, .NET, Python versions)
   - Full error messages (not just "it doesn't work")
   - Configuration (sanitized credentials shown)
   - Expected vs actual behavior

2. **Used proper formatting**
   - Code blocks for logs
   - Collapsible sections for long output
   - Clear section headers

3. **Tested suggested fixes**
   - Tried Copilot's recommendations
   - Reported back with results
   - Shared what worked

4. **Iterative approach**
   - Didn't give up after first try
   - Asked follow-up questions when needed
   - Provided more context when requested

---

## 🔧 Another Example: Python SDK Not Found

### The Problem

```
[ERROR] TopstepXAdapterService: Python executable not found
Win32Exception: No such file or directory
```

### Good Question to Ask

```markdown
@copilot I'm getting a Python executable error when launching the bot.

**Error:**
```
[ERROR] TopstepXAdapterService: Python executable not found
Win32Exception: No such file or directory
```

**My .env configuration:**
```
PYTHON_EXECUTABLE=C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe
```

**Verification:**
```powershell
PS> Test-Path "C:\Users\kevin\AppData\Local\Programs\Python\Python312\python.exe"
True

PS> python --version
Python 3.12.0
```

The path exists and Python works from command line. Why can't the bot find it?
```

### Copilot's Response

> The issue is likely that the bot process doesn't have the same PATH environment as your PowerShell session.
>
> **Try this fix:**
>
> 1. **Use forward slashes instead of backslashes:**
>    ```bash
>    PYTHON_EXECUTABLE=C:/Users/kevin/AppData/Local/Programs/Python/Python312/python.exe
>    ```
>
> 2. **Or use just the command name if Python is in PATH:**
>    ```bash
>    PYTHON_EXECUTABLE=python.exe
>    ```
>
> 3. **Or use double backslashes:**
>    ```bash
>    PYTHON_EXECUTABLE=C:\\Users\\kevin\\AppData\\Local\\Programs\\Python\\Python312\\python.exe
>    ```

---

## 📚 More Examples

### Example: Build Fails

**Question:**
```markdown
@copilot Build is failing with this error:

```
error CS0246: The type or namespace name 'ITopstepXAdapter' could not be found
```

I just cloned the repo. What am I missing?
```

**Answer Pattern Copilot Might Give:**
> This looks like a missing NuGet package restore. Try:
> ```bash
> dotnet restore TopstepX.Bot.sln
> dotnet build
> ```

---

### Example: WebSocket Connection Fails

**Question:**
```markdown
@copilot Bot authenticates but WebSocket fails:

```
[ERROR] WebSocket connection to rtc.topstepx.com failed
SSL: CERTIFICATE_VERIFY_FAILED
```

Running on Windows 11. All .env credentials are correct.
```

**Answer Pattern:**
> This is a known SSL certificate validation issue on Windows. Two options:
>
> 1. **Temporary workaround (development only):**
>    Add to .env:
>    ```bash
>    PYTHONHTTPSVERIFY=0
>    ```
>
> 2. **Production fix:**
>    Use REST polling instead:
>    ```bash
>    TOPSTEPX_ADAPTER_MODE=polling
>    ```

---

## ✅ Best Practices Summary

### Do This ✅
- Provide full error messages
- Include configuration (sanitize sensitive data)
- Share environment details
- Use code blocks and formatting
- Test suggested fixes
- Report back with results

### Don't Do This ❌
- Say "it doesn't work" without details
- Paste 10,000 lines of logs without context
- Ask "how do I fix it?" without error messages
- Give up after first suggestion
- Share real API keys/passwords in issues

---

## 🚀 Ready to Try?

1. Launch your bot using one of the methods in [QUICK_START_COPILOT.md](QUICK_START_COPILOT.md)
2. If you encounter issues, create a GitHub issue with full context
3. Tag `@copilot` and ask specific questions
4. Iterate on fixes and report back

**Remember:** The more context you provide, the better Copilot can help! 🤖✨
