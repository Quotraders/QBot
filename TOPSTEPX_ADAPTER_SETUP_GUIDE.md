# TopstepX Python SDK Adapter - Setup & Troubleshooting Guide

## ✅ Current Status: WORKING

The C# trading bot successfully starts and launches the persistent Python process for TopstepX connectivity.

## Platform-Specific Configuration

### Linux / GitHub Actions / Cloud Environments
```bash
# .env file
PYTHON_EXECUTABLE=python3
```

### Windows with WSL (Windows Subsystem for Linux)
```bash
# .env file  
PYTHON_EXECUTABLE=wsl
```

### Windows Native (Not Recommended)
```bash
# .env file
PYTHON_EXECUTABLE=python
```

## Required Dependencies

### Python SDK
```bash
pip install 'project-x-py[all]>=3.5.0'
```

### Environment Variables Required
```bash
TOPSTEPX_API_KEY=<your_api_key>
TOPSTEPX_USERNAME=<your_username>
TOPSTEPX_ACCOUNT_ID=<your_account_id>
```

## Architecture Overview

### Component Flow
```
C# Bot (UnifiedOrchestrator)
    ↓
TopstepXAdapterService (C#)
    ↓
Persistent Python Process (stdin/stdout)
    ↓
project-x-py SDK
    ↓
TopstepX WebSocket API
```

### Key Files
- **C# Service**: `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs`
- **Python Adapter**: `src/adapters/topstep_x_adapter.py`
- **Configuration**: `.env` file in repository root

## Startup Sequence

1. **DI Container Building** ✅
   - All services registered successfully
   - TopstepXAdapterService constructor executes
   - Configuration loaded from `appsettings.json` and `.env`

2. **Python SDK Validation** ✅
   - Checks for Python executable in PATH
   - Validates `project-x-py` package installed
   - Logs platform detection (Native vs WSL)

3. **Persistent Process Launch** ✅
   - Resolves full path to Python executable
   - Starts Python adapter in "stream" mode
   - Establishes stdin/stdout communication

4. **TopstepX Authentication** ⚠️
   - Python adapter attempts API authentication
   - Requires network access to `api.topstepx.com`
   - May fail in sandboxed environments

## Common Issues & Solutions

### Issue 1: "Failed to start persistent Python process"
**Symptoms**: `Win32Exception: No such file or directory`

**Root Cause**: Python executable path not resolved correctly

**Solution**: Code now includes fallback logic:
```csharp
// Tries in order:
1. FindExecutableInPath("python3")
2. /usr/bin/python3
3. /usr/local/bin/python3
4. Falls back to "python3"
```

### Issue 2: "WSL is not available"
**Symptoms**: `PlatformNotSupportedException` on Linux

**Root Cause**: `PYTHON_EXECUTABLE=wsl` set on non-Windows platform

**Solution**: Use `PYTHON_EXECUTABLE=python3` on Linux/macOS

### Issue 3: "project-x-py SDK not found"
**Symptoms**: `ModuleNotFoundError: No module named 'project_x_py'`

**Root Cause**: Python package not installed

**Solution**:
```bash
pip install 'project-x-py[all]'
```

### Issue 4: Network/DNS Errors
**Symptoms**: `No address associated with hostname`

**Root Cause**: Environment lacks network access to TopstepX servers

**Solution**: Ensure environment can reach:
- `api.topstepx.com`
- `rtc.topstepx.com`

This is expected in sandboxed CI/test environments.

## Verification Steps

### 1. Check Bot Starts
```bash
dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj
```

Expected output:
```
✅ [STARTUP] DI container built successfully
🐍 [Native] Resolved Python path: /usr/bin/python3
[PERSISTENT] Starting Python process in stream mode...
```

### 2. Verify Python Process Running
```bash
ps aux | grep topstep_x_adapter.py
```

Should show Python process with "stream" argument.

### 3. Check Logs for Connection Attempts
Look for:
```
[PERSISTENT] Init phase received: {JSON with authentication attempt}
```

### 4. Test Python Adapter Standalone
```bash
cd src/adapters
python3 topstep_x_adapter.py validate_sdk
```

Should output SDK version and validation status.

## Production Deployment Checklist

- [ ] Set correct `PYTHON_EXECUTABLE` for platform
- [ ] Install `project-x-py[all]` Python package
- [ ] Configure TopstepX credentials in environment
- [ ] Verify network access to TopstepX endpoints
- [ ] Test bot startup and check logs
- [ ] Confirm persistent Python process stays running
- [ ] Validate market data flowing (if network available)

## Success Indicators

When everything is working:
```
✅ DI container built successfully
✅ TopstepXAdapter constructor completed
✅ Python SDK validated successfully  
✅ Persistent Python process started
✅ Python adapter attempting authentication
✅ (In production) WebSocket connected
✅ (In production) Market data streaming
```

## For Developers

### Adding Debug Logging
In `TopstepXAdapterService.cs`, logs are prefixed with:
- `[PERSISTENT]` - Persistent process management
- `[SDK-VALIDATION]` - Python SDK validation
- `🐍` - Python-related operations

### Testing Python Adapter Directly
```bash
# Test SDK validation
python3 src/adapters/topstep_x_adapter.py validate_sdk

# Test stream mode (requires credentials)
export TOPSTEPX_API_KEY="your_key"
export TOPSTEPX_USERNAME="your_username"
python3 src/adapters/topstep_x_adapter.py stream
```

### Monitoring Process Communication
The C# service and Python adapter communicate via:
- **C# → Python**: JSON commands sent to stdin
- **Python → C#**: JSON responses from stdout
- **Logging**: Stderr for Python logs (captured by C#)

## Related Documentation
- Main README: `README.md`
- Production Architecture: `PRODUCTION_ARCHITECTURE.md`
- Environment Configuration: `.env.example`
