# Archive Directory

> **⚠️ WARNING: ARCHIVED COMPONENTS - DO NOT USE IN PRODUCTION ⚠️**
> 
> **🚨 QUARANTINE NOTICE: These artifacts are NOT production-ready 🚨**
> 
> All components in this directory are **INACTIVE**, **LEGACY**, and **DEPRECATED**.
> They are preserved for historical reference only and must NOT be used in production systems.

This directory contains files and components that have been moved from the root during repository cleanup and reorganization.

## 🔒 Production Safety Notice

**THESE COMPONENTS ARE QUARANTINED FOR SAFETY REASONS:**
- ❌ **Not security hardened**
- ❌ **Not production tested** 
- ❌ **May contain hardcoded values**
- ❌ **May bypass guardrails**
- ❌ **No active maintenance**

## Structure

### `demos/`
- **`full-automation/`** - Complete demo_full_automation directory moved here for historical reference
- **`DemoRunner/`** - Empty placeholder demo project moved from samples/

## Status

All components in this directory are considered **INACTIVE** and not part of the current operational system. The active system uses:
- **UnifiedOrchestrator** as the main entry point
- **Legacy demos removed** - MinimalDemo deleted per audit requirements (functionality replaced by UnifiedOrchestrator --smoke)
- All production code remains in `src/` directory

## ⚠️ Development Warning

**DO NOT:**
- Use any code from this directory in production
- Copy patterns or configurations from archived components
- Reference these components in active development
- Assume any archived component is secure or validated

**INSTEAD:**
- Use active components in `src/` directory
- Follow current patterns in UnifiedOrchestrator
- Consult current documentation in `docs/`

## Verification Information

- **Last Archive Date:** January 2, 2025
- **Archive Reason:** Repository structure cleanup per security audit requirements
- **Verification Status:** ❌ NOT VERIFIED for production use
- **Security Review:** ❌ NOT REVIEWED under current standards
- **Quarantine Status:** 🔒 ACTIVE - Do not use in production