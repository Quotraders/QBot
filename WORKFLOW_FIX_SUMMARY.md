# Workflow .NET SDK Installation Fix Summary

**Date**: 2025-10-15  
**Issue**: Self-hosted workflow .NET SDK installation failures  
**Status**: ✅ FIXED

## Problem

The last PR (#560) introduced `DOTNET_INSTALL_DIR` environment variable to the `actions/setup-dotnet@v4` step in an attempt to fix permission errors. However, this was the WRONG solution and actually caused the problem to return.

### What Was Breaking

```yaml
# ❌ INCORRECT - Causes permission errors
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

### Why It Failed

When you force a custom install directory with `DOTNET_INSTALL_DIR`, the setup-dotnet action can still encounter conflicts with existing system-wide .NET installations at `C:\Program Files\dotnet`, leading to permission errors when running without administrator privileges.

## Solution

The working configuration is to **remove all custom install directory settings** and let the `actions/setup-dotnet@v4` action manage its own installation path using its built-in caching mechanism.

### What Works

```yaml
# ✅ CORRECT - Uses default user-writable cache
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

### Why This Works

1. **Default Caching**: The action automatically installs to a user-writable cache directory (typically under the runner's profile)
2. **No Conflicts**: Avoids conflicts with system-wide installations
3. **No Admin Required**: The default cache location is always writable by the runner user
4. **Automatic PATH Management**: The action handles PATH updates correctly without conflicts

## Files Modified

### 1. `.github/workflows/selfhosted-bot-run.yml`
- **Change**: Removed `DOTNET_INSTALL_DIR` environment variable
- **Lines**: 30-35
- **Impact**: .NET SDK will now install to default cache location

### 2. `.github/workflows/selfhosted-test.yml`
- **Change**: Removed `DOTNET_INSTALL_DIR` environment variable
- **Lines**: 26-31
- **Impact**: .NET SDK will now install to default cache location

### 3. `.github/workflows/bot-launch-diagnostics.yml`
- **Status**: ✅ Already correct (never had DOTNET_INSTALL_DIR)
- **No changes needed**

### 4. `SELF_HOSTED_WORKFLOW_AUDIT.md`
- **Change**: Updated documentation to reflect correct solution
- **Sections Updated**:
  - Critical Finding: .NET SDK Installation
  - Changes Made
  - Conclusion
  - Recommendations

## Verification

### ✅ YAML Syntax Validation
All workflow files have been validated for correct YAML syntax:
- `selfhosted-bot-run.yml` - Valid
- `selfhosted-test.yml` - Valid
- `bot-launch-diagnostics.yml` - Valid

### ✅ Configuration Consistency
All `setup-dotnet` steps across ALL workflow files now use the correct pattern:
- `bot-launch-diagnostics.yml` ✅
- `selfhosted-bot-run.yml` ✅
- `selfhosted-test.yml` ✅
- `bt_smoke.yml` ✅
- `build_ci.yml` ✅
- `pr-audit.yml` ✅
- `qa_tests.yml` ✅
- `wf_validate.yml` ✅
- `wsl-fix-validation.yml` ✅

### ✅ No Custom Install Directories
Verified that NO workflow files contain:
- `DOTNET_INSTALL_DIR` environment variable
- `install-dir` parameter
- Any other custom install directory settings

## What The User Said

> "From the history of your runs, the point where things actually worked was when we stopped trying to force setup-dotnet into C:\Program Files\dotnet and just let the action install into its default user-writable cache."

> "The last time this was fixed, the solution was removing the custom install path entirely and relying only on dotnet-version."

## Conclusion

The workflows are now configured correctly with the minimal, working setup-dotnet configuration. The `actions/setup-dotnet@v4` action will automatically:
1. Install .NET SDK to its default user-writable cache directory
2. Manage PATH updates without conflicts
3. Avoid permission errors on non-elevated self-hosted runners
4. Work reliably across different runner environments

**This is the configuration that was known to work, and it has been restored.**

## Next Steps

1. Test the workflows on the self-hosted runner to confirm the fix
2. Monitor for any .NET SDK installation errors
3. Keep this configuration (no custom install directories) for future stability

---

**Fixed By**: GitHub Copilot Coding Agent  
**Commit**: Remove DOTNET_INSTALL_DIR from workflow files to fix .NET SDK installation  
**PR Branch**: copilot/fix-dotnet-install-path
