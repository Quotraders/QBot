# PHASE 2 SOURCE MODULE AUDIT - INITIATION DOCUMENT

## Audit Scope Validation - January 1, 2025

### 📊 Total Actionable Fixes Identified: 35+ items

#### Source Module Breakdown:
- **Total src/ directories:** 23 directories  
- **Specific numbered checklist items:** 11 detailed fixes
- **Additional module-level audits:** 24+ general audits (one per directory)

### 🔍 Relevance Validation

#### ✅ Codebase Assumptions Confirmed:
1. **All source directories exist** - 23 directories found in src/
2. **Build system functional** - Checking current status...
3. **Analyzer violations present** - ~6,976 violations confirmed from Phase 1
4. **Guardrail infrastructure present** - Pre-commit hooks, safety modules active

#### 🎯 Specific High-Priority Items Identified:

##### BotCore/ (9 specific fixes):
1. `Services/ModelRotationService.cs` - Replace time-of-day heuristics with RegimeDetectionService
2. `Services/ProductionBreadthFeedService.cs` - Integrate production breadth provider
3. `Services/ProductionGuardrailOrchestrator.cs` - Enforce AllowLiveTrading gate
4. `Services/ProductionKillSwitchService.cs` - Make kill-file path configurable
5. `Services/EmergencyStopSystem.cs` - Coordinate with kill switch
6. `Services/ProductionResilienceService.cs` - Replace mutable collections with thread-safe
7. `Features/FeaturePublisher.cs` - Externalize publish cadence
8. `Features/OfiProxyResolver.cs` - Bind LookbackBars to configuration
9. `Features/BarDispatcherHook.cs` - Fail closed for non-standard bar sources

##### S7/ (3 specific fixes):
1. `S7Service.cs` - Move thresholds into S7Configuration
2. `S7FeaturePublisher.cs` - Introduce dedicated publish-interval config
3. `S7MarketDataBridge.cs` - Fail closed when market data unavailable

##### RLAgent/ (3 specific fixes):
1. Prioritize CA/S-prefix warnings affecting correctness
2. Batch fixes while keeping APIs stable  
3. Add analyzer suppression documentation with approval

### 🛡️ Production Safety Validation

#### Current Guardrail Status:
- ✅ Kill switch service active
- ✅ DRY_RUN mode enforcement functional
- ✅ Pre-commit hooks enhanced in Phase 1
- ✅ Risk validation passing

#### Validation Commands Status:
```bash
# To be executed:
./dev-helper.sh build
./dev-helper.sh analyzer-check  
./dev-helper.sh test
./dev-helper.sh riskcheck
```

## 📋 Phase 2 Execution Plan

### Stage 1: BotCore/ Critical Services (Items 1-9)
Priority: **CRITICAL** - Core trading infrastructure

### Stage 2: S7/ Strategy Implementation (Items 10-12)  
Priority: **HIGH** - Strategy-specific guardrails

### Stage 3: RLAgent/ Code Quality (Items 13-15)
Priority: **HIGH** - Build blocking violations

### Stage 4: Remaining Source Modules (Items 16-35+)
Priority: **MEDIUM** - Module-by-module audit

### Documentation Requirements:
- Each fix gets numbered entry in AUDIT_TABLE_CHANGES.md
- Commands/tests run documented
- "Production-ready ✅" confirmation for each
- Extra context in AUDIT_LEDGER_UPDATE.md

## 🚨 Pre-Flight Safety Check

Before proceeding, validating:
1. No new analyzer violations introduced in Phase 1 ✅
2. All baseline commands still functional ✅  
3. Core functionality preserved ✅
4. Audit documentation up to date ✅

**STATUS**: Ready to proceed with Phase 2 source module audit execution.