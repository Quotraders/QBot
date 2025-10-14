# Bot Diagnostics Workflow Architecture

## Workflow Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    GitHub Actions Trigger                        │
│                  (Manual: workflow_dispatch)                     │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Self-Hosted Runner Picks Up Job                │
│            (Windows/Linux with .NET SDK installed)               │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         STEP 1: Pre-Launch Environment Validation                │
│  • Check system info (OS, .NET, runner details)                  │
│  • Validate .env file exists                                     │
│  • Check required environment variables                          │
│  • Create diagnostics directory in $RUNNER_TEMP                  │
│  • Save system-info.json                                         │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         STEP 2: Setup .NET and Restore Packages                  │
│  • Run: dotnet restore TopstepX.Bot.sln                          │
│  • Track restore duration                                        │
│  • Verify successful completion                                  │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              STEP 3: Build Trading Bot                           │
│  • Run: dotnet build UnifiedOrchestrator.csproj -c Release       │
│  • Track build duration                                          │
│  • Verify successful compilation                                 │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│       STEP 4: Launch Bot with Full Diagnostics Capture           │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  • Set DRY_RUN=true (safety mode)                         │  │
│  │  • Create log files:                                      │  │
│  │    - console-output-{timestamp}.log                       │  │
│  │    - error-output-{timestamp}.log                         │  │
│  │    - structured-log-{timestamp}.json                      │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Launch Process:                                          │  │
│  │  dotnet run --project UnifiedOrchestrator --no-build      │  │
│  │                                                           │  │
│  │  Capture:                                                 │  │
│  │  ├─ stdout → console-output.log + structured events      │  │
│  │  ├─ stderr → error-output.log + structured events        │  │
│  │  └─ Parse key events (🚀 ✅ ❌ [STARTUP] etc.)          │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Bot runs for configured duration (default: 5 minutes)    │  │
│  │  ↓                                                        │  │
│  │  Graceful shutdown after timeout                         │  │
│  │  ↓                                                        │  │
│  │  Save all outputs to log files                           │  │
│  └───────────────────────────────────────────────────────────┘  │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         STEP 5: Package Diagnostics Artifacts                    │
│  • List all captured files                                       │
│  • Display file sizes                                            │
│  • Prepare for upload                                            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         STEP 6: Upload Diagnostics to GitHub                     │
│  • Upload entire diagnostics directory as ZIP                    │
│  • Artifact name: bot-diagnostics-run-{run_number}               │
│  • Retention: 30 days                                            │
│  • Compression: Level 6                                          │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         STEP 7: Generate Final Execution Report                  │
│  • Display summary statistics                                    │
│  • Show artifact location                                        │
│  • Provide next steps guidance                                   │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
                   ┌─────────────────┐
                   │  Workflow Done   │
                   └─────────────────┘
```

## Data Flow

```
┌──────────────────┐
│   Trading Bot    │
│  (UnifiedOrch.)  │
└────────┬─────────┘
         │
         ├─── stdout ────────────┐
         │                       │
         └─── stderr ────────┐   │
                             │   │
                    ┌────────▼───▼────────┐
                    │  Event Handlers     │
                    │  (PowerShell)       │
                    └────────┬────────────┘
                             │
                ┌────────────┼────────────┐
                │            │            │
         ┌──────▼─────┐ ┌───▼───────┐ ┌──▼──────────┐
         │  Console   │ │   Error   │ │ Structured  │
         │  Output    │ │  Output   │ │    Log      │
         │   .log     │ │   .log    │ │   .json     │
         └──────┬─────┘ └───┬───────┘ └──┬──────────┘
                │           │            │
                └───────────┼────────────┘
                            │
                   ┌────────▼─────────┐
                   │   Diagnostics    │
                   │    Directory     │
                   └────────┬─────────┘
                            │
                   ┌────────▼─────────┐
                   │  GitHub Actions  │
                   │    Artifacts     │
                   └────────┬─────────┘
                            │
                   ┌────────▼─────────┐
                   │    Download      │
                   │   (Users)        │
                   └──────────────────┘
```

## Artifact Structure

```
bot-diagnostics-run-{number}.zip
│
├── system-info.json
│   └── {
│         "timestamp": "2025-10-14T22:30:00Z",
│         "runner": {...},
│         "dotnet": {...},
│         "workflow": {...}
│       }
│
├── console-output-2025-10-14_22-30-00.log
│   ├── 🚀 [STARTUP] Starting unified orchestrator...
│   ├── ⚙️ [STARTUP] Initializing ML parameter provider...
│   ├── ✅ [STARTUP] ML parameter provider initialized
│   ├── ✅ [STARTUP] Service validation completed
│   └── ... (complete console output)
│
├── error-output-2025-10-14_22-30-00.log
│   └── (any stderr output, exceptions, etc.)
│
└── structured-log-2025-10-14_22-30-00.json
    └── {
          "launch_timestamp": "2025-10-14T22:30:00Z",
          "end_timestamp": "2025-10-14T22:35:00Z",
          "actual_runtime_seconds": 300.45,
          "exit_code": 0,
          "exit_reason": "timeout",
          "events": [
            {
              "timestamp": "2025-10-14T22:30:01.234Z",
              "type": "stdout",
              "message": "🚀 [STARTUP] Starting..."
            },
            ...
          ]
        }
```

## Component Interaction

```
┌─────────────────────────────────────────────────────────────┐
│                      GitHub Actions                          │
│                                                              │
│  ┌────────────────────────────────────────────────────┐     │
│  │         Workflow: bot-launch-diagnostics.yml       │     │
│  │                                                    │     │
│  │  Inputs:                                           │     │
│  │  • runtime_minutes (default: 5)                    │     │
│  │  • capture_detailed_logs (default: true)           │     │
│  └────────────────────────────────────────────────────┘     │
│                          │                                   │
└──────────────────────────┼───────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   Self-Hosted Runner                         │
│                                                              │
│  ┌────────────────────────────────────────────────────┐     │
│  │  PowerShell Environment                            │     │
│  │  • .NET SDK 8.0+                                   │     │
│  │  • Process capture & event handling                │     │
│  │  • File I/O for logs                               │     │
│  │  • JSON serialization                              │     │
│  └────────────────────────────────────────────────────┘     │
│                          │                                   │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────┐     │
│  │  Trading Bot (UnifiedOrchestrator)                 │     │
│  │  • Runs in DRY_RUN mode                            │     │
│  │  • Full service initialization                     │     │
│  │  • Normal startup sequence                         │     │
│  │  • Logging to stdout/stderr                        │     │
│  └────────────────────────────────────────────────────┘     │
│                          │                                   │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────┐     │
│  │  Diagnostics Directory ($RUNNER_TEMP)              │     │
│  │  • Temporary storage on runner                     │     │
│  │  • Files created during workflow                   │     │
│  │  • Cleaned up after upload                         │     │
│  └────────────────────────────────────────────────────┘     │
│                          │                                   │
└──────────────────────────┼───────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                  GitHub Artifact Storage                     │
│                                                              │
│  • Compressed ZIP file                                       │
│  • 30-day retention                                          │
│  • Download from workflow run page                           │
│  • Accessible to repository members                          │
└─────────────────────────────────────────────────────────────┘
```

## Safety Mechanisms

```
┌────────────────────────────────────────────────────────────┐
│                    Safety Layers                            │
│                                                             │
│  Layer 1: DRY_RUN Mode                                      │
│  ┌───────────────────────────────────────────────────┐     │
│  │ • Environment variable: DRY_RUN=true              │     │
│  │ • Prevents real order execution                   │     │
│  │ • Simulates all trading operations                │     │
│  └───────────────────────────────────────────────────┘     │
│                        ▼                                    │
│  Layer 2: Timeout Protection                                │
│  ┌───────────────────────────────────────────────────┐     │
│  │ • Workflow timeout: runtime + 5 minutes           │     │
│  │ • Process timeout: runtime in minutes             │     │
│  │ • Graceful shutdown on timeout                    │     │
│  └───────────────────────────────────────────────────┘     │
│                        ▼                                    │
│  Layer 3: Error Isolation                                   │
│  ┌───────────────────────────────────────────────────┐     │
│  │ • continue-on-error: true                         │     │
│  │ • Artifacts uploaded even on failure              │     │
│  │ • Error details captured in logs                  │     │
│  └───────────────────────────────────────────────────┘     │
│                        ▼                                    │
│  Layer 4: Resource Cleanup                                  │
│  ┌───────────────────────────────────────────────────┐     │
│  │ • Process termination (Kill if needed)            │     │
│  │ • Temp directory cleanup                          │     │
│  │ • Event handler deregistration                    │     │
│  └───────────────────────────────────────────────────┘     │
└────────────────────────────────────────────────────────────┘
```

## Usage Scenarios

```
Scenario 1: Troubleshooting
  User → Triggers workflow → Bot crashes → Logs captured
    → Downloads artifacts → Reviews error logs → Fixes issue

Scenario 2: Configuration Validation
  User → Changes .env → Triggers workflow → Bot starts
    → Reviews system-info.json → Confirms config → Deploys

Scenario 3: Performance Baseline
  User → Runs 3x → Downloads all → Compares metrics
    → Establishes baseline → Monitors future runs

Scenario 4: Team Collaboration
  User → Encounters issue → Runs workflow → Downloads ZIP
    → Shares with teammate → Complete context provided

Scenario 5: Before/After Testing
  User → Before: Run + Download → Make changes
    → After: Run + Download → Compare logs → Validate
```

## File Relationships

```
Repository Files:
┌─────────────────────────────────────────────────────────┐
│ .github/workflows/                                       │
│ ├── bot-launch-diagnostics.yml ────┐ (Main workflow)   │
│ └── README-bot-diagnostics.md ─────┤ (Documentation)   │
│                                     │                   │
│ docs/                               │                   │
│ ├── bot-diagnostics-quick-reference.md ─┤             │
│ └── bot-diagnostics-examples.md ────────┤             │
│                                          │             │
│ README.md ──────────────────────────────┤ (All link   │
│ RUNBOOKS.md ────────────────────────────┤  to main    │
│ BOT_DIAGNOSTICS_IMPLEMENTATION.md ──────┘  workflow)  │
└─────────────────────────────────────────────────────────┘
```

## System Requirements

```
Self-Hosted Runner:
├── Operating System: Windows or Linux
├── .NET SDK: 8.0 or higher
├── PowerShell: 5.1+ (Windows) or 7+ (Linux)
├── Disk Space: ~500MB for artifacts
├── Memory: 2GB+ recommended
└── Network: Access to GitHub and TopstepX APIs
```

## Timeline Example

```
Minute 0: ████ Workflow triggered
Minute 0-1: ████████ Environment validation + Setup
Minute 1-2: ████████████ NuGet restore
Minute 2-3: ████████████████ Build bot
Minute 3-8: ████████████████████████████████████ Bot running
Minute 8-9: ████████████████████████████████████████ Package & Upload
Minute 9: ████████████████████████████████████████████ Complete

Total: ~9 minutes for 5-minute runtime
```
