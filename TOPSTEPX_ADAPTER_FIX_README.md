# TopstepX Adapter Connection Issue Fix

## Problem Summary

The TopstepX adapter was failing to connect with the error:
```
❌ ERROR: Required TopstepX credentials are missing!
```

### Root Cause

The GitHub Actions workflow was setting environment variables for credentials, but these variables were **empty** (not undefined). When empty environment variables exist, they override values from the `.env` file, causing the adapter to fail with missing credentials.

The workflow had this code:
```yaml
env:
  TOPSTEPX_API_KEY: ${{ secrets.TOPSTEPX_API_KEY }}
  TOPSTEPX_USERNAME: ${{ secrets.TOPSTEPX_USERNAME }}
  TOPSTEPX_ACCOUNT_ID: ${{ secrets.TOPSTEPX_ACCOUNT_ID }}
```

When GitHub secrets are not set, these become empty strings (`""`), not undefined. This causes:
1. `.env` file loads successfully with credentials
2. Environment variables from workflow override with empty values
3. Python adapter receives empty credentials
4. Adapter fails with "credentials missing"

## Solution

### 1. TopstepXAdapterService.cs Changes

Added credential validation and .env reload logic before starting the Python process:

```csharp
// Get environment variables for credentials and retry policy
var apiKey = Environment.GetEnvironmentVariable("TOPSTEPX_API_KEY");
var username = Environment.GetEnvironmentVariable("TOPSTEPX_USERNAME");
var accountId = Environment.GetEnvironmentVariable("TOPSTEPX_ACCOUNT_ID");

// CRITICAL FIX: If credentials are empty, reload from .env file
// This handles the case where the workflow sets empty env vars that override the .env file
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(accountId))
{
    _logger.LogWarning("[ENV-FIX] Credentials are empty, reloading from .env file...");
    
    // Reload .env file to ensure we have credentials from file
    TradingBot.UnifiedOrchestrator.Infrastructure.EnvironmentLoader.LoadEnvironmentFiles();
    
    // Re-read after loading
    apiKey = Environment.GetEnvironmentVariable("TOPSTEPX_API_KEY");
    username = Environment.GetEnvironmentVariable("TOPSTEPX_USERNAME");
    accountId = Environment.GetEnvironmentVariable("TOPSTEPX_ACCOUNT_ID");
    
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(accountId))
    {
        var msg = "TOPSTEPX credentials missing after .env reload. Ensure .env file exists with TOPSTEPX_API_KEY, TOPSTEPX_USERNAME, and TOPSTEPX_ACCOUNT_ID.";
        _logger.LogError("[ENV-FIX] {Message}", msg);
        throw new InvalidOperationException(msg);
    }
    
    _logger.LogInformation("[ENV-FIX] ✅ Successfully loaded credentials from .env file");
}
```

### 2. topstep_x_adapter.py Changes

Enhanced error messages to provide troubleshooting guidance:

```python
if not api_key or not username:
    # Provide detailed error message with troubleshooting steps
    missing_vars = []
    if not api_key:
        missing_vars.append('PROJECT_X_API_KEY/TOPSTEPX_API_KEY')
    if not username:
        missing_vars.append('PROJECT_X_USERNAME/TOPSTEPX_USERNAME')
    
    error_msg = (
        f"Missing required TopstepX credentials: {', '.join(missing_vars)}\n"
        f"\n"
        f"Troubleshooting:\n"
        f"1. Verify .env file exists in repository root with credentials:\n"
        f"   TOPSTEPX_API_KEY=your_api_key\n"
        f"   TOPSTEPX_USERNAME=your_username\n"
        f"   TOPSTEPX_ACCOUNT_ID=your_account_id\n"
        f"\n"
        f"2. Ensure environment variables are not being overridden with empty values\n"
        f"   (check workflow configuration or parent process)\n"
        f"\n"
        f"3. Current environment variables:\n"
        f"   PROJECT_X_API_KEY: {'SET' if os.getenv('PROJECT_X_API_KEY') else 'NOT SET'}\n"
        f"   TOPSTEPX_API_KEY: {'SET' if os.getenv('TOPSTEPX_API_KEY') else 'NOT SET'}\n"
        f"   PROJECT_X_USERNAME: {os.getenv('PROJECT_X_USERNAME', 'NOT SET')}\n"
        f"   TOPSTEPX_USERNAME: {os.getenv('TOPSTEPX_USERNAME', 'NOT SET')}\n"
    )
    
    # Log to stderr for visibility
    print(error_msg, file=sys.stderr)
    raise RuntimeError(error_msg)
```

## How It Works

### Before Fix
1. Workflow starts with empty env vars
2. .env file loads at startup
3. Empty env vars override .env values
4. Python adapter receives empty credentials
5. **FAILURE**

### After Fix
1. Workflow starts with empty env vars
2. .env file loads at startup
3. Empty env vars override .env values (still happens)
4. **C# adapter detects empty credentials**
5. **C# adapter reloads .env file explicitly**
6. .env values now properly set in environment
7. Python adapter receives valid credentials
8. **SUCCESS**

## Testing

### Diagnostic Script

Use the `diagnose-adapter-env.sh` script to check environment configuration:

```bash
./diagnose-adapter-env.sh
```

This will:
- Check for .env file
- Verify required credentials in .env
- Check current environment variables
- Verify Python SDK installation
- Provide recommendations

### Manual Testing

1. **Test with empty env vars** (simulates workflow issue):
```bash
export TOPSTEPX_API_KEY=""
export TOPSTEPX_USERNAME=""
export TOPSTEPX_ACCOUNT_ID=""
dotnet run --project src/UnifiedOrchestrator
```

Expected: Should reload from .env and work correctly

2. **Test with no env vars** (normal case):
```bash
unset TOPSTEPX_API_KEY TOPSTEPX_USERNAME TOPSTEPX_ACCOUNT_ID
dotnet run --project src/UnifiedOrchestrator
```

Expected: Should load from .env and work correctly

3. **Test with missing .env file**:
```bash
mv .env .env.backup
dotnet run --project src/UnifiedOrchestrator
```

Expected: Should fail with clear error message about missing .env file

## Files Modified

1. `src/UnifiedOrchestrator/Services/TopstepXAdapterService.cs` - Added credential validation and .env reload
2. `src/adapters/topstep_x_adapter.py` - Enhanced error messages
3. `diagnose-adapter-env.sh` - New diagnostic tool (added)

## Workflow Remains Unchanged

**IMPORTANT**: The workflow file `.github/workflows/selfhosted-bot-run.yml` was **NOT** modified, as per the constraints. The fix handles the empty environment variables at the application level, allowing the workflow to continue setting them without breaking the bot.

## Prevention

To prevent this issue in the future:

1. **For GitHub Actions**: Keep .env file on self-hosted runner with valid credentials
2. **For Development**: Ensure .env file exists with valid credentials
3. **For CI/CD**: Either:
   - Set GitHub secrets properly, OR
   - Don't set env vars in workflow (let .env handle it), OR
   - Use the fixed code which handles both cases

## Related Issues

- WebSocket connection timeouts (improved with retry logic in adapter)
- Market data streaming (enhanced with better error handling)
- Python SDK initialization (added timeout and retry logic)

## Security Note

The .env file contains sensitive credentials and should:
- Never be committed to version control
- Be listed in .gitignore
- Have restricted file permissions (600 or 400)
- Be backed up securely
