# .NET SDK Installation Configuration Audit Report

**Date**: 2025-10-15  
**Issue**: Audit and verify .NET SDK installation configuration in bot-launch-diagnostics and all self-hosted workflows  
**Status**: ✅ VERIFIED CORRECT

## Executive Summary

All GitHub Actions workflow files have been audited and **confirmed to be using the correct .NET SDK installation pattern**. No issues were found. The workflows are properly configured to avoid permission errors on self-hosted runners.

## Audit Scope

### Workflows Audited
1. `.github/workflows/bot-launch-diagnostics.yml` - Bot diagnostics workflow
2. `.github/workflows/selfhosted-bot-run.yml` - Self-hosted bot execution test
3. `.github/workflows/selfhosted-test.yml` - Self-hosted runner connectivity test

### Checks Performed
- ✅ No `DOTNET_INSTALL_DIR` environment variables
- ✅ No `install-dir` parameters in setup-dotnet steps
- ✅ No manual `install-dotnet.ps1` calls
- ✅ All setup-dotnet steps use the correct pattern

## Findings

### ✅ bot-launch-diagnostics.yml
**Status**: CORRECT  
**Configuration**: 
```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

**Analysis**:
- Uses `actions/setup-dotnet@v4` (latest major version)
- Specifies only `dotnet-version: '8.0.x'` parameter
- No custom install directory overrides
- No environment variables that would interfere with default behavior
- Allows the action to install .NET SDK to its default user-writable cache location

### ✅ selfhosted-bot-run.yml  
**Status**: CORRECT  
**Configuration**:
```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

**Analysis**: Same correct pattern as bot-launch-diagnostics.yml

### ✅ selfhosted-test.yml
**Status**: CORRECT  
**Configuration**:
```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

**Analysis**: Same correct pattern as bot-launch-diagnostics.yml

## Why This Configuration Is Correct

### 1. Default User-Writable Cache Directory
When no custom install directory is specified, `actions/setup-dotnet@v4` automatically installs the .NET SDK to a user-writable cache directory, typically:
- Windows: `%USERPROFILE%\.dotnet` or `%LOCALAPPDATA%\Microsoft\dotnet`
- Linux/macOS: `~/.dotnet` or similar user directory

This avoids permission errors that occur when trying to install to `C:\Program Files\dotnet` without administrator privileges.

### 2. No Conflicts with System Installations
By allowing the action to manage its own installation path, we avoid conflicts with any existing system-wide .NET installations.

### 3. Automatic PATH Management
The `setup-dotnet` action automatically updates the `PATH` environment variable to include the installed .NET SDK, ensuring that subsequent steps can find the `dotnet` command.

### 4. Proven to Work
According to the `WORKFLOW_FIX_SUMMARY.md`, this configuration was tested and confirmed to work:
> "From the history of your runs, the point where things actually worked was when we stopped trying to force setup-dotnet into C:\Program Files\dotnet and just let the action install into its default user-writable cache."

## What NOT To Do

### ❌ INCORRECT Pattern #1: Custom DOTNET_INSTALL_DIR
```yaml
# ❌ DO NOT USE THIS
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet  # WRONG!
  with:
    dotnet-version: '8.0.x'
```

**Why it's wrong**: Setting `DOTNET_INSTALL_DIR` can still lead to conflicts with existing .NET installations and permission errors.

### ❌ INCORRECT Pattern #2: Custom install-dir Parameter
```yaml
# ❌ DO NOT USE THIS
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
    install-dir: 'C:\Program Files\dotnet'  # WRONG!
```

**Why it's wrong**: Explicitly setting an install directory to a system location requires admin privileges.

### ❌ INCORRECT Pattern #3: Manual install-dotnet.ps1 to System Directory
```yaml
# ❌ DO NOT USE THIS
- name: "Install .NET SDK"
  shell: pwsh
  run: |
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "install-dotnet.ps1"
    ./install-dotnet.ps1 -Version 8.0  # Defaults to C:\Program Files\dotnet - WRONG!
```

**Why it's wrong**: Without specifying `-InstallDir`, the script defaults to `C:\Program Files\dotnet` which requires admin rights.

### ⚠️ ACCEPTABLE Pattern: Manual install-dotnet.ps1 to User Directory
```yaml
# ⚠️ This is acceptable but NOT RECOMMENDED (prefer setup-dotnet@v4)
- name: "Install .NET SDK"
  shell: pwsh
  run: |
    $InstallDir = "$env:USERPROFILE\.dotnet"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "install-dotnet.ps1"
    ./install-dotnet.ps1 -Version 8.0 -InstallDir $InstallDir
    echo "$InstallDir" | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
```

**Why it's acceptable**: Uses a user-writable directory and updates PATH. However, using `actions/setup-dotnet@v4` is still preferred as it handles caching and versioning better.

## Historical Context

### Previous Issues
According to repository documentation:
- **PR #560**: Incorrectly introduced `DOTNET_INSTALL_DIR` environment variable
- **PR #561**: Fixed the issue by removing `DOTNET_INSTALL_DIR`
- This PR: Confirming the fix is still in place and adding verification tooling

### Root Cause of Repeated Issues
The same problem keeps recurring (this is reportedly the "10th PR") because:
1. Developers trying to "fix" permission errors add custom install directories
2. Custom directories either require admin privileges or cause PATH conflicts
3. The actual solution is counter-intuitive: do LESS (remove customizations), not more

## Verification Tooling

A new verification script has been created to prevent future regressions:

**Script**: `verify-dotnet-installation-config.sh`

**Usage**:
```bash
./verify-dotnet-installation-config.sh
```

**What it checks**:
- ✅ No `DOTNET_INSTALL_DIR` environment variables in workflows
- ✅ No `install-dir` parameters in setup-dotnet steps
- ✅ No manual install-dotnet.ps1 calls (or ensures they use user directories)
- ✅ All self-hosted workflows use the correct simple pattern

**Exit codes**:
- `0`: All checks passed, configuration is correct
- `1`: Problems found, configuration needs fixing

### Recommended Integration

Add this script to:
1. **Pre-commit hooks**: Prevent incorrect configurations from being committed
2. **CI/CD pipeline**: Validate all workflow files on every PR
3. **Documentation**: Reference in contribution guidelines

## Recommendations

### 1. Protect Against Future Regressions
Add the verification script to the CI/CD pipeline to automatically detect and reject any PRs that reintroduce incorrect .NET SDK installation patterns.

### 2. Update Contribution Guidelines
Add explicit guidance in `CONTRIBUTING.md` or similar documentation:
- Always use `actions/setup-dotnet@v4` with only `dotnet-version` parameter
- Never add `DOTNET_INSTALL_DIR` or `install-dir` overrides
- If manual installation is absolutely necessary, use user-writable directories

### 3. Add Workflow Validation
Consider adding a workflow that runs the verification script on all PRs that modify workflow files.

### 4. Document the "Why"
Keep this audit report and `WORKFLOW_FIX_SUMMARY.md` in the repository as reference documentation for future developers who might be tempted to add custom install directories.

## Conclusion

✅ **All self-hosted workflows are correctly configured.**

The workflows use the minimal, working `setup-dotnet@v4` configuration that has been proven to work on self-hosted runners without admin privileges. No changes to the workflow files are needed.

The verification script has been added to help prevent future regressions and ensure that this issue doesn't recur.

### Key Takeaway
**When it comes to .NET SDK installation on self-hosted runners: LESS IS MORE**

The correct approach is to:
1. Use `actions/setup-dotnet@v4`
2. Specify only `dotnet-version`
3. Let the action handle everything else automatically

---

**Audited By**: GitHub Copilot Coding Agent  
**Verification Script**: `verify-dotnet-installation-config.sh`  
**Date**: 2025-10-15
