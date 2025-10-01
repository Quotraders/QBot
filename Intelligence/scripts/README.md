# Intelligence Scripts Directory

## ⚠️ SCRIPT REMOVAL NOTICE

Placeholder Python scripts have been removed per audit requirements from `AUDIT_CATEGORY_GUIDEBOOK.md`.

### Removed Scripts
- **build_features.py** - Empty placeholder script (contained only `print("Building features...")`)
- **train_models.py** - Empty placeholder script (contained only `print("Training models...")`)

### Updated Scripts
- **utils/api_fallback.py** - ✅ Fixed to surface upstream failures instead of returning mock data

### Audit Compliance
- **Cleanup Date:** January 1, 2025
- **Action:** Placeholder script removal per AUDIT_CATEGORY_GUIDEBOOK.md Intelligence section
- **Rationale:** Remove placeholder scripts until real versions exist

### Moving Forward
When intelligence scripts are rebuilt:
1. Load configuration from `config/`
2. Process sanitized inputs only
3. Emit traceable outputs with manifests
4. Require sign-off in `AUDIT_LEDGER_UPDATE.md` before committing
5. Document ownership and review/escalation steps in `RUNBOOKS.md`

### Script Requirements
Real implementations must:
- Load all configuration from external config files
- Handle failures transparently (no mock fallbacks unless explicitly opted-in)
- Generate traceable outputs with manifests (hash, source timestamp, owner)
- Document ownership and maintainer information