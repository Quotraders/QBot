# Audit Category Guidebook

This guidebook translates the repository-wide audit ledger into actionable playbooks. Each section maps to a major directory or subsystem so an agent can remediate findings without hunting through prior conversations. The purpose of every audit is to expose anything wrong, risky, or incomplete inside that folder so we can move it to production-ready status and launch the bot with confidence. Audits are discovery passes—not hard blocks—so log every gap you find and then decide how to prioritize the fixes. Follow the checklists in order and validate every guardrail before marking a task complete.

## How to Use This Guide

1. **Pick a category** from the table of contents that matches the files you are touching.
2. **Review the context box** (role and key risks) to understand why the guardrails exist.
3. **Work through the remediation checklist** in order. Treat every item as mandatory unless the acceptance criteria explicitly allow skipping it—the checklist exists to surface the full backlog of fixes needed before the directory is truly production-ready.
4. **Run the verification steps** before committing. The analyzer baseline must stay clean and all production guardrails must remain intact.
5. **Update `AUDIT_TABLE_CHANGES.md`** once the remediation and validation steps pass.

> **Quick note for the deletion/fix audit:** During this documentation pass we’re only flagging what to delete or repair. Any references to DRY_RUN or other production guardrails indicate future hardening work—ignore them unless you’re explicitly preparing a production deployment.
>
> **Baseline commands** (run whenever you finish a checklist for production readiness):
>
> ```bash
> ./dev-helper.sh build
> ./dev-helper.sh analyzer-check
> ./dev-helper.sh test
> ./dev-helper.sh riskcheck
> ```
>
> Always execute these from a non-networked environment with DRY_RUN enabled.

---

## Top-Level Directories

### `.githooks/`
- **Role:** Local Git hook scripts that block unsafe commits before they reach CI.
- **Key Risks:** Missing analyzer checks, narrow file scanning, or skipped guardrail verification allows production-unsafe code to land.
- **Remediation Checklist:**
  1. Swap `grep -E` for `grep -P` (or replace every `\s` token with `[[:space:]]`) so whitespace-sensitive patterns actually match.
  2. Replace the global `find ./src/**/*.cs` scan with a `git diff --cached --name-only` loop that inspects only staged files (and gracefully handles file deletions or renames).
  3. Chain `./dev-helper.sh analyzer-check` (with `--no-restore`) after the existing build invocation in `pre-commit` so local commits respect analyzer baselines.
  4. Extend pattern scanning to cover `tests/`, `scripts/`, and any directory touching production guardrails. If scope remains limited to `src/`, document the rationale and add compensating controls.
  5. Add a guardrail assertion that fails the commit when `kill.txt` is present or DRY_RUN is disabled in configuration.
  6. Confirm the hook path is registered for both Unix (`.git/hooks/`) and Windows (Git Bash) contributors; document setup in `README.md`.
- **Verification:**
  - Stage a file with a magic number containing whitespace and confirm the updated regex engine flags it.
  - Stage a file in an unrelated directory and ensure the hook ignores it unless it’s part of the staged diff.
  - Touch a file containing a known magic number (e.g., `0.25`) in `tests/` and confirm the hook blocks the commit.
  - Toggle DRY_RUN off temporarily in a local `.env` and ensure the hook stops the commit with a guardrail warning.

### `.github/`
- **Role:** CI/CD workflows and guardrail automation.
- **Key Risks:** Missing analyzer enforcement or ability to merge without passing guardrails.
- **Remediation Checklist:**
  1. Rip out all simulated JSON in `build_ci.yml`, `qa_tests.yml`, `promote_manifest.yml`, and friends—call `./dev-helper.sh build`, `./dev-helper.sh analyzer-check`, `./dev-helper.sh test`, and `./dev-helper.sh riskcheck` directly (use `--no-restore` if runtime is a concern).
  2. Convert every “log but succeed” block into a real failure: check exit codes from guardrail scripts, and `exit 1` when tests or validations fail so GitHub marks the job red.
  3. Move workflow scratch data (manifests, audit logs, session files) into `$RUNNER_TEMP` or `mktemp` directories; never write persistent artifacts into the repo checkout.
  4. Teach `.github/scripts/session_deduplicator.py` to respect runner temp paths and clean up after itself—no more `Intelligence/data/mechanic` writes inside the repository.
  5. Prune or automate `workflow_status.json` and `system_status.txt`; either generate them from real workflow events or drop them to avoid stale “all green” dashboards.
  6. Add kill-switch/DRY_RUN gating steps that abort pipelines if `kill.txt` exists or DRY_RUN isn’t enforced, then wire analyzer/guardrail workflows into branch protection once real guardrails run.
  7. Document emergency rollback procedures in `CURRENT_ACTIVE_SYSTEM_VERIFICATION.md` and link the authoritative workflow list in `RUNBOOKS.md` so operators know how to respond when CI fails for real.
- **Verification:**
  - Trigger intentional analyzer/risk/test failures and confirm the relevant workflows exit non-zero and block merges.
  - Validate required checks in repository settings include analyzer, guardrail, and risk jobs tied to the updated workflows.
  - Run workflows twice in quick succession and confirm session deduplication writes only to temp storage (no repo diffs).
  - Inspect workflow artifacts; manifests and reports should exist only in uploaded artifacts, not in the repository tree.

### `archive/`
- **Role:** Historical artifacts and legacy snapshots.
- **Key Risks:** Confusion with live code or resurrection of unsafe logic.
- **Remediation Checklist:**
  1. Catalogue each sub-folder with creation date, purpose, and retention policy.
  2. Quarantine or delete items that contain obsolete credentials or unsafe patterns.
  3. Add `README.md` markers clarifying "Do Not Use In Production" for retained archives.
- **Verification:**
  - Confirm `archive/` is excluded from build scripts and code generation.
  - Spot check files for secrets using `git secrets --scan archive/`.

### `artifacts/`
- **Role:** Generated build outputs.
- **Key Risks:** Accidental commits of stale binaries.
- **Remediation Checklist:**
  - Add/update `.gitignore` entries to prevent new artifacts from being tracked.
  - Document artifact naming conventions in `DEPLOYMENT_PIPELINE_USAGE.md`.
- **Verification:**
  - Run `git status` after builds to ensure `artifacts/` remains clean.

### `bin/` & `obj/`
- **Role:** Generated build outputs that must never be committed.
- **Key Risks:** Accidentally tracked binaries pollute diffs, hide stale dependencies, and mask source changes during audits.
- **Remediation Checklist:**
  1. Delete any existing `bin/` or `obj/` directories from the repository and rely on `dotnet build` to regenerate them locally.
  2. Keep the `.gitignore` entries for `bin/` and `obj/` and extend guardrail scripts to fail when `git ls-files bin obj` returns results.
  3. Add a cleanup step (`dotnet clean` or folder purge) to local/CI build scripts so transient artefacts never reach commits.
  4. Document in `RUNBOOKS.md` that developers must clear these folders before opening PRs and avoid `git add -A` on the repo root.
- **Verification:**
  - `git status --short | grep -E 'bin|obj'` returns empty after clean builds, and CI guardrails fail when artefacts slip through.

### `config/`
- **Role:** Environment and application configuration.
- **Key Risks:** DRY_RUN disabled, secrets in plain text, or defaults that bypass guardrails.
- **Remediation Checklist:**
  1. Audit every config file for DRY_RUN defaults and ensure production requires explicit opt-in.
  2. Move secrets to environment variables or secret stores; replace inline values with references.
  3. Validate `AllowLiveTrading` toggles are false by default.
  4. Add schema validation tests (e.g., using `System.Text.Json` schema) to catch misconfigurations.
  5. Delete legacy configuration blocks (`BookAwareSimulator` in `enhanced-trading-bot.json`, etc.) that no longer map to active systems; archive them separately if operators still need references.
  6. Create required runtime directories (`state/gates/`, `state/explain/`, `data/training/execution/` when simulators are enabled) with `.gitkeep` placeholders so first-run jobs do not fail with `DirectoryNotFoundException`.
- **Verification:**
  - Run `./dev-helper.sh riskcheck` and ensure risk validation passes with configs loaded.
  - Launch counterfactual replay/explainability jobs in DRY_RUN; they should write into the new directories without manual setup.

### `data/`
- **Role:** Static datasets and reference tables.
- **Key Risks:** Stale market mappings or licensed data without provenance.
- **Remediation Checklist:**
  1. Delete outdated readiness docs (`docs/readiness/PRODUCTION_GUARDRAILS_COMPLETE.md`, `docs/readiness/PRODUCTION_ENFORCEMENT_GUIDE.md`) that claim full production coverage—remove them rather than rewriting.
  2. Remove or quarantine the “final audit” reports in `docs/audits/` (e.g., `ML_RL_CLOUD_FINAL_AUDIT_REPORT.md`) unless they are restated as historical-only with bold warning headers.
  3. Update active guides to align with the current audit ledger, including replacing placeholders like `$(date)` in `docs/README.md` and clarifying that guardrails remain in development.
  4. Add explicit remediation steps referencing this guidebook for any remaining gaps.
  5. Stamp each document retained in `docs/` with a “Last Verified” date and responsible owner.
  4. Re-run failed analytics (e.g., `regime/comprehensive_analysis.json`) and block deployment when success counts are zero—capture error telemetry instead of committing empty error blobs.
  - Spot-check random runbooks to ensure procedures match current scripts and that no document claims production readiness unless backed by current code/tests.
  6. Document schema and test harness usage for `validation/multi_symbol_test_data.parquet`, ensuring automation loads it via configuration rather than hardcoded paths.
  7. Create required directories (`data/training/execution/`, mirrors for `state/gates/` and `state/explain/`) with `.gitkeep` so runtime jobs succeed on first run.
  8. Add checksum or signature validation for any datasets that remain so tampering is detectable during CI.
- **Verification:**
  - Run the dataset verification script to confirm manifests, checksums, and required directories exist, then execute `./dev-helper.sh riskcheck` to ensure guardrail validations still pass with the trimmed dataset set.

### `docs/`
- **Role:** Operational runbooks and guardrail references.
- **Key Risks:** Drift between documentation and code.
- **Remediation Checklist:**
  1. Align guardrail documentation with the latest `AUDIT_LEDGER_UPDATE.md` findings.
  2. Add explicit remediation steps referencing this guidebook for each outstanding gap.
  3. Stamp each document with a "Last Verified" date.
- **Verification:**
  - Spot-check random runbooks to ensure procedures match current scripts.

### `Intelligence/`
- **Role:** Shared intelligence assets (market news ingestion, feature generation, model packaging).
- **Key Risks:** Bulk news dumps committed to the repo, placeholder scripts that never run, and mock fallbacks that hide real API failures.
- **Remediation Checklist:**
  1. **Delete/Archive:** Remove everything under `Intelligence/data/**` (`dashboard/`, `integrated/`, `market/`, `news/`, `raw/`, `regime/`, `training/`). If any sample needs to live on, move it to external storage and leave a README with retrieval notes.
  2. **Scrub History:** Use `git filter-repo` (or equivalent) to purge third-party article text and market snapshots from prior commits; record the cleanup hash in `AUDIT_LEDGER_UPDATE.md`.
  3. **Rebuild Pipelines:** Remove the placeholder Python scripts (`build_features.py`, `train_models.py`) until real versions exist. When rebuilt, the scripts should load configuration from `config/`, process sanitized inputs, and emit traceable outputs.
  4. **Fix Fallbacks:** Rewrite `scripts/utils/api_fallback.py` so it surfaces upstream failures (HTTP codes, exception details) instead of returning mock payloads. Make the fallback opt-in and document how production code should react when the API is down.
  5. **Track Artefacts:** Introduce manifests for any regenerated intelligence artefacts (hash, source timestamp, owner) and require sign-off in `AUDIT_LEDGER_UPDATE.md` before committing them.
  6. **Document Ownership:** Name a maintainer for the intelligence pipeline and spell out review/escalation steps in `RUNBOOKS.md` before new data or scripts land.
- **Verification:**
  - Confirm `git status` (and a tree listing of new commits) shows no files under `Intelligence/data/`.
  - Spot-check the filtered history to ensure third-party content is gone.
  - Run the rebuilt intelligence scripts against sample inputs and verify they generate usable artefacts without relying on mock fallbacks.

### `legacy-projects/`
- **Role:** Frozen `.NET` projects that pre-date `UnifiedOrchestrator`; kept only for historical reference.
- **Key Risks:** Developers or CI reintroducing legacy binaries, diverging package versions, or running code paths that bypass today’s guardrails.
- **Remediation Checklist:**
  1. Delete the entire `legacy-projects/` directory from active branches. Do not move it elsewhere inside this repository; any archival copy must live outside the repo under separate governance.
  2. Update references (`PROJECT_STRUCTURE.md`, automation scripts, ignore lists) to reflect that the directory is gone.
  3. Add/confirm guardrail checks that fail if future PRs reintroduce projects named `TradingBot` or `TradingBot.Orchestrators` anywhere in the source tree.
  4. Capture the rationale in the PR description so auditors understand the deletion is intentional and production-safe, noting where external archives (if any) reside.
- **Verification:**
  - Run `git status` to confirm `legacy-projects/` is removed and no binaries remain.
  - Execute `git grep -n "legacy-projects" *.sln Directory.Build.props` and confirm no lingering references.
  - Review CI/build discovery scripts to ensure they no longer detect the legacy projects.

### `manifests/`
- **Role:** Source-controlled manifest contracts that advertise which model tranches the orchestrator should promote. `src/UnifiedOrchestrator/Program.cs` bootstraps `manifests/manifest.json`, CI (`.github/workflows/promote_manifest.yml`) mutates it during promotions, and `validate-full-automation.sh` exercises a `test_manifest.json` fixture.
- **Key Risks:** Placeholder manifest entries point to non-existent artefacts, mixed casing drifts between production/test manifests, and automation currently writes throwaway manifests directly into the repo tree.
- **Remediation Checklist:**
  1. Replace the placeholder URLs, hashes, and tranche identifiers in `manifests/manifest.json` with the real production manifest generated by the model registry. Every entry must carry a validated SHA-256 checksum, file size, and tranche metadata.
  2. Update CI (`promote_manifest.yml`) to validate the manifest it proposes against staged artefacts before writing to git. Promotions should fail when the checksum mismatch, file is missing, or the manifest regresses in version/created-at timestamp.
  3. Refactor `validate-full-automation.sh` so the SHA validation harness writes its `test_manifest.json` into a temp directory (e.g., `$VALIDATION_DIR/manifests/`) instead of polluting the tracked `manifests/` folder. Keep the fixture schema identical to production and document that the repo copy is authoritative.
  4. Enforce schema parity between production and test manifests: align property casing (`Version` vs `version`) and require `RegimeArtifacts` blocks (or document why test fixtures can omit them). Add a JSON schema check to guard against silent casing drift.
  5. Document rollback procedure alongside the manifest (e.g., `manifests/ROLLBACK.md`) so operators can revert to a prior version with matching checksums.
- **Verification:**
  - Run the manifest validation job in CI and confirm it fails on checksum mismatch, schema divergence, or non-monotonic versioning.
  - Execute `validate-full-automation.sh` and verify the script now writes manifests under its temp workspace, leaving the repo clean.
  - Run orchestration smoke test (`UnifiedOrchestrator --smoke`) with the refreshed manifest to ensure bootstrap still succeeds.

### `MinimalDemo/`
- **Role:** Legacy console demo that pre-dates `UnifiedOrchestrator --smoke`.
- **Key Risks:** Prints “production trading bot demonstration” messaging, hardcodes guardrail success banners, and is still invoked by deployment/verification scripts even though it doesn’t exercise real guardrails.
- **Remediation Checklist:**
  1. Delete the entire `MinimalDemo/` project from the repository. UnifiedOrchestrator’s `--smoke` command now replaces its functionality.
  2. Update all scripts and docs that reference the project (`scripts/operations/verify-production-ready.sh`, `production-demo.sh`, `deploy-production.sh`, `PROJECT_STRUCTURE.md`, etc.) to call `dotnet run --project src/UnifiedOrchestrator/UnifiedOrchestrator.csproj --smoke` or remove the demo step entirely.
  3. Remove any analyzer suppressions (`NoWarn` entries) or guardrail exceptions that existed solely for the demo.
  4. If historical training material is needed, archive the demo outside this repository and add a pointer in `RUNBOOKS.md` noting where to find it.
- **Verification:**
  - Run the updated operations scripts; they should succeed without referencing `MinimalDemo/` and should fail if `UnifiedOrchestrator --smoke` reports guardrail issues.

### `ml/` & `ml_online/`
- **Role:** Legacy 24/7 training prototypes that pre-date the UnifiedOrchestrator guardrail overhaul.
- **Key Risks:** Synthetic data generation, auto-promotion without checksum/sign-off, no DRY_RUN or kill-switch enforcement, and blind model hot-reload scripts that mutate production paths.
- **Remediation Checklist:**
  1. Delete both `ml/` and `ml_online/` directories from the active repository. They are historical prototypes and must not run in production environments.
  2. Update any scripts or documentation that reference these folders (training guides, deployment helpers, `PROJECT_STRUCTURE.md`, etc.) to clarify they are removed and replaced by UnifiedOrchestrator pipelines.
  3. If archival reference is needed, relocate the code to an external archive with a clear “prototype only—unsafe” banner; do not keep runnable copies in-tree.
  4. When rebuilding ML pipelines, require manifest-backed promotions, SHA-256 verification, DRY_RUN/kill-switch gating, and telemetry coverage before reintroducing automation.
- **Verification:**
  - Confirm repository tree no longer contains `ml/` or `ml_online/`.
  - Run scripts that previously referenced the folders; they should either skip ML steps or rely on the new orchestrator workflows.

### `models/`
- **Role:** Legacy dump of model binaries that should now live in an external registry.
- **Key Risks:** Unsourced `.onnx`/`.pth` binaries, synthetic win-rate JSON, empty registries, and orchestrator scripts pointing at artefacts that no longer exist.
- **Remediation Checklist:**
  1. Delete the entire `models/` directory from the repository; relocate any artefacts that need to be preserved into the approved external model registry with signed checksums.
  2. Update all manifests, scripts, and docs (`manifests/manifest.json`, `PROJECT_STRUCTURE.md`, orchestration helpers) to reference the registry output instead of in-repo paths.
  3. Replace placeholder telemetry exports (`time_optimization/*.json`, RL metrics) with real guardrail reports generated during model promotion, or drop them until validated outputs exist.
  4. Add a CI/analyzer guardrail that fails if future commits introduce tracked binaries under `models/` (or any top-level directory) without accompanying registry metadata.
- **Verification:**
  - `git status` shows no `models/` directory, and `git grep "models/"` in manifests/scripts resolves only to external registry paths.
  - Run the orchestrator smoke test (`UnifiedOrchestrator --smoke`) using the updated manifest; it must bootstrap models exclusively from the registry location.

### `python/`
- **Role:** Active ML/RL integration surface. Hosts the FastAPI decision service, neural UCB tooling, and the SDK bridge that `UnifiedOrchestrator` invokes when Python integration is enabled.
- **Key Risks:** Drift between the Python stack and C# guardrails, missing dependency pinning for CI runners, and latent scripts (e.g., `start_decision_service.sh`, `train_neural_ucb.py`) that assume manual execution rather than orchestrator ownership.
- **Current Findings:**
  - `Program.cs` wires `PythonIntegrationOptions` directly to `python/decision_service/simple_decision_service.py` and `python/ucb/neural_ucb_topstep.py`; `appsettings.json` ships with the feature enabled by default.
  - `DecisionServiceRouter` performs live health checks against the FastAPI endpoint and falls back to the C# brain only when the Python service fails.
  - CI helpers (`scripts/ml-rl-audit-ci.sh`) and ops docs (`IMPLEMENTATION_SUMMARY.md`, ML/RL audit reports) still reference these entry points. Removing the directory would break the configured ML/UCB path unless the orchestrator is refactored first.
- **Remediation Checklist:**
  1. Keep the directory in-tree while Python integration remains wired into production options. Document the required runtime (FastAPI + neural UCB) in operator runbooks.
  2. Add a maintainer task to track alignment between the Python requirements files and the orchestrator expectations (ensure pinned versions exist and match deployment images).
  3. When (and if) the organization migrates fully to the C# brain, refactor `PythonIntegrationOptions`, `DecisionServiceRouter`, and related scripts before scheduling removal of this directory.
- **Verification:**
  - Smoke test the orchestrator with `PythonIntegration.enabled=true`; confirm the router reaches the FastAPI service and the fallback logic still works when the service is offline.
  - `git grep "python/decision_service"` and `git grep "python/ucb"` should only surface intentional integration points called out in this guide.

### `scripts/`
- **Role:** Historical automation bundle (production demo generators, launch scripts, bulletproof workflow experiments).
- **Key Risks:** Many scripts fabricate artifacts, disable `TreatWarningsAsErrors`, or bypass guardrails (e.g., `operations/launch-production.sh`, `production-demo.sh`, `ml-rl-audit-ci.sh`). None are referenced by the current orchestrator build or CI.
- **Remediation Checklist:**
  1. Delete the entire `scripts/` tree. Archive externally only if the fake demo outputs need historical reference.
  2. Update `PROJECT_STRUCTURE.md`, `RUNBOOKS.md`, and CI docs to reflect that scripted launch/validation flows were replaced by `./dev-helper.sh` and orchestrator commands.
  3. Block future additions by adding guardrail checks that fail when new tracked files appear under `scripts/` without explicit approval.
- **Verification:**
  - `git status` confirms `scripts/` is removed; baseline commands (`./dev-helper.sh build`, `analyzer-check`, `test`, `riskcheck`) still pass via supported entry points.

### `state/`
- **Role:** Runtime snapshots.
- **Key Risks:** Sensitive data persisted locally.
- **Remediation Checklist:**
  - Encrypt state files at rest or purge after use.
  - Update `.gitignore` to prevent accidental commits.
- **Verification:**
  - Run `git ls-files state/` and ensure no tracked files remain.

### `strategies/`
- **Role:** Non-`src/` strategy definitions.
- **Key Risks:** Hardcoded parameters diverging from adaptive bundles.
- **Remediation Checklist:**
  - Replace literals with references to `ParameterBundle` configurations.
  - Document expected strategy/bundle mappings.
- **Verification:**
  - Run strategy backtests ensuring they load bundles dynamically.

### `tests/`
- **Role:** Regression and guardrail coverage.
- **Key Risks:** Missing tests for safety-critical paths.
- **Remediation Checklist:**
  1. Ensure guardrail scenarios (kill switch, DRY_RUN enforcement, order evidence) are covered by integration tests.
  2. Add analyzer regression tests mirroring real-world failures.
- **Verification:**
  - `./dev-helper.sh test` must pass with guardrail coverage metrics recorded.

### `tools/`
- **Role:** Developer utilities.
- **Key Risks:** Tools enabling unsafe changes (e.g., analyzer suppression).
- **Remediation Checklist:**
  - Audit for scripts that modify `Directory.Build.props` or analyzers; remove or lock down.
  - Require authentication for tools that touch live systems.
- **Verification:**
  - Attempt to run tooling that would suppress analyzers; it should fail.

### `wwwroot/`
- **Role:** Static web assets.
- **Key Risks:** Sensitive data or outdated assets served publicly.
- **Remediation Checklist:**
  - Scan for secrets using `npm audit` and custom grep.
  - Ensure caching headers enforce versioning.
- **Verification:**
  - Run static analyzer (e.g., `npm run lint` if applicable) on assets.

---

## Source Modules (`src/`)

### `src/Abstractions/`
- **Role:** Shared contracts and configuration surfaces.
- **Key Risks:** Interfaces lacking guardrail properties.
- **Remediation Checklist:**
  - Review abstractions to ensure they expose DRY_RUN, risk, and telemetry requirements.
  - Add XML documentation summarizing guardrail expectations.
- **Verification:**
  - Build consumers to confirm new properties flow through DI registrations.

### `src/adapters/`
- **Role:** External data adapters.
- **Key Risks:** Fail-open integration when upstream data is missing.
- **Remediation Checklist:**
  - Implement fail-closed defaults and telemetry on adapter failures.
  - Centralize retry policies with bounded timeouts.
- **Verification:**
  - Simulate network failure; adapter should surface guardrail alert and halt trading decisions.

### `src/Backtest/`
- **Role:** Historical backtesting pipelines.
- **Key Risks:** Divergence between backtest and DRY_RUN behavior.
- **Remediation Checklist:**
  - Align configuration loading with production services.
  - Ensure kill-switch logic is mirrored in backtests.
- **Verification:**
  - Run `run_historical_backtest.py` and confirm guardrail telemetry matches production expectations.

### `src/BotCore/`
- **Role:** Core trading services, features, and guardrails.
- **Key Risks:** Hardcoded business logic or missing guardrail integration.
- **Remediation Checklist:**
  1. **`Services/ModelRotationService.cs`**
     - Replace time-of-day heuristics with `RegimeDetectionService` inputs.
     - Inject `RegimeDetectionService` via DI and update callers.
     - Add unit tests covering regime transitions.
  2. **`Services/ProductionBreadthFeedService.cs`**
     - Integrate production breadth provider; remove configuration-only heuristics.
     - Propagate breadth signals into S7 adjustments with validation.
  3. **`Services/ProductionGuardrailOrchestrator.cs`**
     - Enforce `AllowLiveTrading` gate and consult `ProductionOrderEvidenceService` before orders progress.
     - Emit structured telemetry for every guardrail decision.
  4. **`Services/ProductionKillSwitchService.cs`**
     - Make kill-file path configurable.
     - Broadcast DRY_RUN activation to sibling processes (e.g., event bus or file marker).
  5. **`Services/EmergencyStopSystem.cs`**
     - Coordinate with kill switch to create `kill.txt`.
     - Align namespaces/DI registration with guardrail orchestrator.
     - Emit telemetry on activation.
  6. **`Services/ProductionResilienceService.cs`**
     - Replace mutable collections with thread-safe alternatives.
     - Validate `ResilienceConfig` bounds during startup.
     - Use `HttpRequestException.StatusCode` when classifying retries.
  7. **`Features/FeaturePublisher.cs`**
     - Externalize publish cadence; reject zero/negative intervals.
     - Log telemetry around publish latency.
  8. **`Features/OfiProxyResolver.cs`**
     - Bind `LookbackBars` to configuration with validation.
     - Centralize safe-zero/min datapoint logic.
  9. **`Features/BarDispatcherHook.cs`**
     - Fail closed for non-standard bar sources unless explicitly configured.
     - Drive publisher enablement via configuration.
- **Verification:**
  - Run targeted unit tests (create if missing) plus baseline commands.
  - Verify telemetry dashboards show new guardrail events.

### `src/Cloud/`
- **Role:** Cloud deployment agents.
- **Key Risks:** Remote operations bypassing guardrails.
- **Remediation Checklist:**
  - Audit deployment scripts to ensure DRY_RUN required unless authorized.
  - Add retries with exponential backoff and telemetry.
- **Verification:**
  - Execute dry-run deployment; ensure guardrails trigger when not authorized.

### `src/Infrastructure/`
- **Role:** Infrastructure helpers (storage, messaging).
- **Key Risks:** Fail-open IO operations.
- **Remediation Checklist:**
  - Add circuit breakers with safe defaults.
  - Ensure unauthorized access throws guardrail exceptions.
- **Verification:**
  - Simulate IO failure; confirm system halts trading and logs alert.

### `src/IntelligenceAgent/`
- **Role:** Intelligence orchestration.
- **Key Risks:** Adaptive learning bypassing guardrails.
- **Remediation Checklist:**
  - Validate telemetry for intelligence decisions includes guardrail status.
  - Confirm outputs respect risk thresholds > 0.
- **Verification:**
  - Run intelligence workflows with mock data; ensure guardrail flags propagate.

### `src/IntelligenceStack/`
- **Role:** Supporting intelligence stack services.
- **Key Risks:** Inconsistent configuration or logging gaps.
- **Remediation Checklist:**
  - Ensure every component honors `ProductionGuardrailConfig`.
  - Add structured logging to feed monitoring dashboards.
- **Verification:**
  - Execute stack startup in DRY_RUN; guardrail logs must appear in aggregation.

### `src/ML/`
- **Role:** Machine learning pipelines.
- **Key Risks:** Hardcoded hyperparameters influencing live trades.
- **Remediation Checklist:**
  - Load hyperparameters from configuration with validation.
  - Validate models before deployment with guardrail simulations.
- **Verification:**
  - Run ML training script; ensure guardrail validation step fails unsafe models.

### `src/Monitoring/`
- **Role:** Monitoring and alerting logic.
- **Key Risks:** Missed guardrail breaches due to poor monitoring.
- **Remediation Checklist:**
  - Add alerts for kill-switch activation, analyzer failures, and DRY_RUN toggles.
  - Ensure metrics push to central observability stack.
- **Verification:**
  - Trigger synthetic guardrail events; alerts must fire within SLA.

### `src/OrchestratorAgent/`
- **Role:** Trade orchestration.
- **Key Risks:** Parameter drift.
- **Remediation Checklist:**
  - Confirm recent configuration-driven changes (`InstitutionalParameterOptimizer`) remain bounded.
  - Audit other orchestrator modules for lingering literals.
- **Verification:**
  - Run orchestrator integration tests in DRY_RUN mode.

### `src/RLAgent/`
- **Role:** Reinforcement learning agent.
- **Key Risks:** 340+ analyzer violations blocking clean builds.
- **Remediation Checklist:**
  1. Prioritize CA/S-prefix warnings affecting correctness or safety.
  2. Batch fixes while keeping public APIs stable.
  3. Add analyzer suppression documentation for any intentional deviations (with approval).
- **Verification:**
  - After each batch, run `dotnet build -warnaserror` on `src/RLAgent` solution.

### `src/S7/`
- **Role:** Strategy 7 implementations.
- **Key Risks:** Hardcoded thresholds and fail-open market-data handling.
- **Remediation Checklist:**
  1. **`S7Service.cs`** – Move thresholds (e.g., `DefaultMinZScoreThreshold`) into `S7Configuration`; add lower-bound validation.
  2. **`S7FeaturePublisher.cs`** – Introduce dedicated publish-interval config with telemetry-backed validation.
  3. **`S7MarketDataBridge.cs`** – Fail closed when `IEnhancedMarketDataFlowService` is unavailable; emit telemetry when reflection fallback kicks in.
- **Verification:**
  - Run S7 integration tests with missing market data; system should halt trading and log alerts.

### `src/Safety/`
- **Role:** Safety utilities.
- **Key Risks:** Drift from orchestrator guardrails.
- **Remediation Checklist:**
  - Align helper utilities with updated kill switch and guardrail orchestrator APIs.
  - Ensure utility defaults force DRY_RUN.
- **Verification:**
  - Execute safety unit tests; confirm compliance with orchestrator changes.

### `src/Strategies/`
- **Role:** Strategy logic wrappers.
- **Key Risks:** Hardcoded parameters.
- **Remediation Checklist:**
  - Ensure strategies use adaptive bundles introduced in `ParameterBundle`.
  - Remove residual literals and map to configuration.
- **Verification:**
  - Run strategy regression suite using adaptive bundles.

### `src/StrategyAgent/`
- **Role:** Strategy agent orchestration.
- **Key Risks:** Unaudited integration points.
- **Remediation Checklist:**
  - Audit DI registrations for guardrail awareness.
  - Confirm agent logs guardrail decisions.
- **Verification:**
  - Run agent in DRY_RUN; guardrail events must surface in logs.

### `src/SupervisorAgent/`
- **Role:** Supervisory control plane.
- **Key Risks:** Missing oversight of guardrail states.
- **Remediation Checklist:**
  - Ensure supervisor polls kill-switch, DRY_RUN, and analyzer status.
  - Add escalation logic for prolonged guardrail violations.
- **Verification:**
  - Simulate guardrail breach; supervisor should escalate according to runbook.

### `src/Tests/`
- **Role:** Test harnesses.
- **Key Risks:** Insufficient coverage of guardrail logic.
- **Remediation Checklist:**
  - Expand tests to cover new guardrail behaviors (kill switch, DRY_RUN toggles, order evidence).
  - Add regression tests mirroring known incident postmortems.
- **Verification:**
  - Ensure coverage reports include guardrail namespaces.

### `src/TopstepAuthAgent/`
- **Role:** Authentication agent.
- **Key Risks:** Token handling bypassing guardrails.
- **Remediation Checklist:**
  - Validate tokens without logging secrets.
  - Ensure auth failures trigger guardrail halts.
- **Verification:**
  - Simulate invalid token; confirm system fails closed.

### `src/TopstepX.Bot/`
- **Role:** Bot entry point.
- **Key Risks:** Divergence from BotCore guardrails.
- **Remediation Checklist:**
  - Synchronize kill-switch and emergency stop logic with BotCore updates.
  - Confirm startup validates DRY_RUN state before trading.
- **Verification:**
  - Launch bot with `kill.txt` present; startup must abort.

### `src/UnifiedOrchestrator/`
- **Role:** Unified orchestrator brain.
- **Key Risks:** Hardcoded confidences.
- **Remediation Checklist:**
  - Validate recent `TradingBrainAdapter` changes remain configuration-driven.
  - Audit other orchestrator brains for literals.
- **Verification:**
  - Run orchestrator regression suite ensuring thresholds load from config.

### `src/UpdaterAgent/`
- **Role:** Update management agent.
- **Key Risks:** Unsafely applying updates in live mode.
- **Remediation Checklist:**
  - Require DRY_RUN mode for update downloads.
  - Add checksum validation prior to applying updates.
- **Verification:**
  - Perform update workflow in DRY_RUN; must fail if checksum mismatches.

### `src/Zones/`
- **Role:** Zone logic and risk envelopes.
- **Key Risks:** Incorrect guardrail boundaries.
- **Remediation Checklist:**
  - Validate zone calculations respect adaptive configurations.
  - Add telemetry for zone breaches.
- **Verification:**
  - Run zone simulation; ensure breaches trigger guardrail responses.

---

## Keeping the Guide Current

- After closing a checklist item, update both this guidebook and `AUDIT_TABLE_CHANGES.md`.
- Log new findings in `AUDIT_LEDGER_UPDATE.md` before remediating so audit history stays intact.
- Re-run full baseline commands weekly to ensure no drift between documentation and enforcement.
