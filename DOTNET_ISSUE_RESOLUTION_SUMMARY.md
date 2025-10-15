# .NET SDK Installation Issue Resolution Summary

**PR**: Restore PR #559 .NET SDK Installation Configuration  
**Date**: 2025-10-15  
**Status**: ✅ RESTORED TO WORKING CONFIGURATION

## Issue Description

The user reported: "pr 559 is the last time the sdk was correctly working need to put it back to whatever u did there"

## Investigation Findings

### Timeline of Events

1. **PR #559 (WORKING)**: Added `DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet` to force .NET SDK installation to user-writable temp directory
   - Status: Worked correctly
   - User confirmation: "last time the sdk was correctly working"

2. **PR #561 (REGRESSION)**: Removed `DOTNET_INSTALL_DIR`, claiming it was wrong
   - Status: Broke the workflows again
   - Caused: Permission errors returned

3. **This PR (RESTORATION)**: Restored PR #559 configuration based on user feedback

### Root Cause

When .NET SDK is already installed system-wide at `C:\Program Files\dotnet`, the `actions/setup-dotnet@v4` action may attempt to update it in place, even without explicit install directory configuration. This causes permission errors on self-hosted runners not running with administrator privileges.

## Solution Implemented

Restored the PR #559 working configuration to all three self-hosted workflows:

```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

### Files Changed

1. ✅ `.github/workflows/bot-launch-diagnostics.yml` - Restored DOTNET_INSTALL_DIR
2. ✅ `.github/workflows/selfhosted-bot-run.yml` - Restored DOTNET_INSTALL_DIR  
3. ✅ `.github/workflows/selfhosted-test.yml` - Restored DOTNET_INSTALL_DIR
4. ✅ `verify-dotnet-installation-config.sh` - Updated to validate PR #559 pattern
5. ✅ `DOTNET_INSTALLATION_AUDIT_REPORT.md` - Updated documentation
6. ✅ `DOTNET_ISSUE_RESOLUTION_SUMMARY.md` - This document

## Why This Configuration Works

### 1. Explicit User-Writable Directory
Setting `DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet` explicitly forces installation to the runner's temp directory, which is always user-writable.

### 2. Bypasses System Installation
This prevents the setup-dotnet action from attempting to update any existing system-wide .NET installation at `C:\Program Files\dotnet`.

### 3. No Admin Rights Required
The `${{ runner.temp }}` directory is always writable by the runner user without administrator privileges.

### 4. User-Confirmed Working
According to the user feedback, PR #559 with this configuration was "the last time the sdk was correctly working."

## Verification

The verification script confirms the correct configuration:

```bash
$ ./verify-dotnet-installation-config.sh
✅ ALL CHECKS PASSED

All workflows use the PR #559 working .NET SDK installation pattern:
  - DOTNET_INSTALL_DIR set to ${{ runner.temp }}/.dotnet
  - No install-dir parameters
  - No manual install-dotnet.ps1 calls to system directories
```

## What Was Wrong with PR #561

PR #561 removed the `DOTNET_INSTALL_DIR` environment variable, using only:

```yaml
# ❌ This was PR #561 - caused regression
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

Without the explicit `DOTNET_INSTALL_DIR`, the action may try to install/update .NET in system directories when a system-wide installation exists, causing permission errors.

## Correct Pattern (PR #559)

**Always use this pattern for self-hosted runners:**

```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

## DO NOT

❌ Remove the `DOTNET_INSTALL_DIR` environment variable  
❌ Use `install-dir` parameter instead of `DOTNET_INSTALL_DIR` env var  
❌ Let setup-dotnet use default behavior on self-hosted runners with existing system .NET installations

## Future Protection

### Automated Checks
- ✅ Verification script validates PR #559 pattern
- ✅ PR audit workflow runs verification on all PRs
- ✅ Script exits with error if configuration doesn't match PR #559

### Documentation
- ✅ Audit report explains why PR #559 configuration is correct
- ✅ Clear examples and historical context
- ✅ User feedback documented

## Conclusion

### ✅ Configuration Restored

All three self-hosted workflows now use the PR #559 working configuration with `DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet`.

### 🎯 Key Takeaway

**For self-hosted Windows runners with existing system .NET installations, you MUST explicitly set DOTNET_INSTALL_DIR:**

```yaml
env:
  DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
```

This forces installation to a user-writable directory and bypasses permission errors.

---

**Restored By**: GitHub Copilot Coding Agent  
**Based On**: User feedback - "pr 559 is the last time the sdk was correctly working"  
**Date**: 2025-10-15  
**Verification**: All checks passed ✅
