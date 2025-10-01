# Intelligence Directory Manifest

> **⚠️ CRITICAL: All intelligence assets require approval before commit ⚠️**

## Ownership & Approval Chain

- **Primary Maintainer:** Production Intelligence Team Lead (escalates to DevOps within 2 hours)
- **Backup Maintainer:** Senior Trading Engineer (secondary contact for intelligence failures)
- **Escalation Path:** Intelligence failures escalate to primary maintainer within 2 hours, then to DevOps Lead
- **Review Requirements:** Two-person review + audit ledger sign-off in AUDIT_LEDGER_UPDATE.md

## Intelligence Asset Guidelines

### ❌ Prohibited
- Bulk commits of news articles or market snapshots
- Third-party content in git history  
- Unvetted external data sources
- Mock/placeholder intelligence data in production

### ✅ Required Process
1. **Manifest Creation:** Generate hash, source timestamp, and owner info
2. **Documentation:** Update ownership in RUNBOOKS.md
3. **Sign-off:** Record approval in AUDIT_LEDGER_UPDATE.md  
4. **Review:** Two-person review for any new intelligence assets

### 📁 Storage Requirements
- **Large datasets:** Use external storage with retrieval instructions
- **Configuration-driven:** All processing via config/ directory
- **Sanitized inputs only:** No raw third-party content
- **Hash verification:** All assets must include SHA-256 checksums

## Current Status

- **Asset Count:** 0 (placeholder only)
- **Last Verified:** 2025-01-02
- **Approval Status:** ACTIVE - requires approval tracking in AUDIT_LEDGER_UPDATE.md
- **Production Ready:** ❌ NO - awaiting real intelligence assets with proper approval chain

## Required Actions Before Production Use

1. [x] Assign primary and backup maintainers
2. [ ] Update RUNBOOKS.md with escalation procedures  
3. [ ] Document approval process in AUDIT_LEDGER_UPDATE.md
4. [ ] Remove all placeholder/mock assets
5. [ ] Implement hash verification for all assets
6. [ ] Set up external storage for large datasets