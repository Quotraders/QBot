# .NET SDK Installation Configuration Audit Report

**Date**: 2025-10-15  
**Issue**: Restore PR #559 .NET SDK installation configuration  
**Status**: ✅ RESTORED TO WORKING CONFIGURATION

## Executive Summary

All GitHub Actions workflow files have been **restored to the PR #559 working configuration** that uses `DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet` to force .NET SDK installation to a user-writable directory, avoiding permission errors on self-hosted runners.

## Background

### The Problem
Self-hosted GitHub Actions runners were experiencing permission errors when `actions/setup-dotnet@v4` attempted to install or update .NET SDK in system directories like `C:\Program Files\dotnet`, which requires Administrator privileges.

### PR #559 Solution (WORKING)
PR #559 fixed the issue by adding `DOTNET_INSTALL_DIR` environment variable to force installation to the runner's temp directory:

```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

### PR #561 Regression (BROKE IT)
PR #561 incorrectly removed the `DOTNET_INSTALL_DIR` environment variable, claiming it was wrong. This caused the permission errors to return.

### This PR (RESTORATION)
This PR restores the PR #559 working configuration based on user feedback that PR #559 was "the last time the sdk was correctly working."

## Current Configuration

### ✅ bot-launch-diagnostics.yml
**Status**: RESTORED TO PR #559  
**Configuration**: 
```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

### ✅ selfhosted-bot-run.yml  
**Status**: RESTORED TO PR #559  
**Configuration**: Same as above

### ✅ selfhosted-test.yml
**Status**: RESTORED TO PR #559  
**Configuration**: Same as above

## Why This Configuration Works

### 1. Forces User-Writable Directory
By explicitly setting `DOTNET_INSTALL_DIR` to `${{ runner.temp }}/.dotnet`, the installation is forced into the runner's temporary directory, which is always user-writable.

### 2. Bypasses System Installation Conflicts
When .NET SDK is already installed system-wide at `C:\Program Files\dotnet`, the setup-dotnet action may try to update it in place. Setting `DOTNET_INSTALL_DIR` bypasses this entirely.

### 3. No Admin Rights Required
The `${{ runner.temp }}` directory is always writable by the runner user, even without administrator privileges.

### 4. Proven to Work
According to the user: "pr 559 is the last time the sdk was correctly working"

## What NOT To Do

### ❌ Removing DOTNET_INSTALL_DIR (PR #561 mistake)
```yaml
# ❌ WRONG - This was the PR #561 regression
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

**Why it fails**: Without `DOTNET_INSTALL_DIR`, the action may try to install/update .NET in system directories, causing permission errors.

### ❌ Using install-dir Parameter
```yaml
# ❌ WRONG - Don't use install-dir parameter
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
    install-dir: 'C:\Program Files\dotnet'
```

**Why it fails**: The `install-dir` parameter doesn't work the same way as the `DOTNET_INSTALL_DIR` environment variable.

### ❌ Manual Install to System Directory
```yaml
# ❌ WRONG
- run: ./install-dotnet.ps1 -Version 8.0  # Defaults to Program Files
```

## Historical Context

1. **Before PR #559**: Workflows failed with permission errors
2. **PR #559 (WORKING)**: Added `DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet` - fixed the issue
3. **PR #561 (REGRESSION)**: Removed `DOTNET_INSTALL_DIR` - broke it again
4. **This PR (RESTORATION)**: Restored PR #559 configuration based on user feedback

## Verification

The verification script has been updated to validate the PR #559 pattern:

```bash
$ ./verify-dotnet-installation-config.sh
✅ ALL CHECKS PASSED

All workflows use the PR #559 working .NET SDK installation pattern:
  - DOTNET_INSTALL_DIR set to ${{ runner.temp }}/.dotnet
  - No install-dir parameters
  - No manual install-dotnet.ps1 calls to system directories
```

## Key Takeaway

**The PR #559 configuration is the proven working solution:**

```yaml
- name: "🔧 Setup .NET SDK"
  uses: actions/setup-dotnet@v4
  env:
    DOTNET_INSTALL_DIR: ${{ runner.temp }}/.dotnet
  with:
    dotnet-version: '8.0.x'
```

Do not remove the `DOTNET_INSTALL_DIR` environment variable. It is essential for avoiding permission errors on self-hosted runners.

---

**Restored By**: GitHub Copilot Coding Agent  
**Based On**: PR #559 working configuration  
**User Feedback**: "pr 559 is the last time the sdk was correctly working"  
**Date**: 2025-10-15
