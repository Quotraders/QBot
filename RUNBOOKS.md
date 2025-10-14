# Trading Bot Runbooks

## Intelligence Pipeline Ownership & Review

### Intelligence Data & Scripts
- **Maintainer:** TBD (assign before any intelligence data/scripts land)
- **Review Process:** All intelligence artefacts require sign-off in `AUDIT_LEDGER_UPDATE.md`
- **Escalation:** Intelligence failures escalate to maintainer within 2 hours

### Intelligence Artefact Requirements
Before committing any intelligence data or scripts:
1. **Manifest Creation:** Generate hash, source timestamp, and owner
2. **Documentation:** Update ownership in this file
3. **Sign-off:** Record approval in `AUDIT_LEDGER_UPDATE.md`
4. **Review:** Two-person review for any new intelligence assets

### Intelligence Data Management
- **No bulk commits** of news articles or market snapshots
- **External storage** for large datasets with retrieval instructions
- **Sanitized inputs only** - no third-party content in git history
- **Configuration-driven** processing from `config/` directory

## 🚀 Quick Start Guide

### Verified Working Command
```bash
# Main launcher (verified working with 0 errors, 0 warnings)
dotnet run --project SimpleBot/SimpleBot.csproj
```

### System Status Check
```bash
# Build verification
dotnet build

# Health check via logs when running SimpleBot
# Look for: "✅ TRADING BOT STARTUP COMPLETE - NO ERRORS, NO WARNINGS"
```

## 🔧 Operational Procedures

### Bot Diagnostics & Logging (Self-Hosted Runner)

For self-hosted deployments, use the Bot Launch Diagnostics workflow to capture complete startup information:

**Quick Access:**
1. Go to GitHub Actions → "🤖 Bot Launch Diagnostics - Self-Hosted"
2. Click "Run workflow"
3. Configure runtime (default: 5 minutes)
4. Download artifacts after completion

**What Gets Captured:**
- Complete console output from bot startup
- All error messages and stack traces
- System and environment information
- Structured JSON logs with parsed events
- Runtime metrics and performance data

**Artifact Files:**
- `system-info.json` - System environment details
- `console-output-*.log` - Complete console logs
- `error-output-*.log` - Error stream output
- `structured-log-*.json` - Parsed startup events

**Use Cases:**
- 🐛 Troubleshooting startup failures
- ✅ Validating configuration changes
- 📊 Performance analysis
- 📝 Sharing bot behavior with team

**Safety:** Always runs in DRY_RUN mode to prevent live trading.

📚 **Full Guide:** [.github/workflows/README-bot-diagnostics.md](.github/workflows/README-bot-diagnostics.md)

### Daily Startup Checklist
1. **Environment Check**
   ```bash
   cd /path/to/trading-bot-c-
   git pull
   dotnet restore
   dotnet build
   ```

2. **Health Verification**
   ```bash
   dotnet run --project SimpleBot/SimpleBot.csproj
   # Verify: "SYSTEM STATUS: HEALTHY"
   # Verify: "✅ Strategy ID Generation" working
   # Verify: "✅ Analytics Correlation Test" working
   ```

3. **Component Status**
   - ✅ Strategy System (Trading.Strategies namespace)
   - ✅ Core trading components operational
   - ⚠️ Note: Minimal launcher validates core components only

### Troubleshooting Guide

#### Build Issues
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build

# Check for missing dependencies
dotnet list package --outdated
```

#### Common Problems
1. **Package Version Conflicts**: Update to latest compatible versions
2. **Missing References**: Check project references in .csproj files
3. **Circular Dependencies**: Verify dependency chain doesn't create loops

### System Architecture

#### Current Working Structure
```
SimpleBot/                    # 🎯 Main Entry Point
├── SimpleBot.csproj         # Project configuration
└── Program.cs               # Application launcher

src/
├── Strategies/              # ✅ Core trading strategies
│   ├── StrategyIds.cs      # Strategy ID generation
│   └── Analytics.cs        # Trading analytics
└── Safety/                  # Safety systems
```

#### Key Components Status
- ✅ **Strategy System**: Operational (StrategyIds, Analytics)
- ✅ **Build System**: Working (0 errors, 0 warnings)
- ⚠️ **Full Trading System**: Requires additional setup
- ❌ **Complex Dependencies**: Temporarily disabled for stability

## 🛡️ Safety Protocols

### DRY_RUN Mode (Default)
- System defaults to safe operation
- No live trading without explicit configuration
- All components validated before any trading operations

### Emergency Procedures
```bash
# Emergency stop (if running)
Ctrl+C

# System health check
dotnet run --project SimpleBot/SimpleBot.csproj

# Log analysis
tail -f logs/trading-bot.log  # (when logging to file is configured)
```

## 📊 Monitoring & Maintenance

### Health Indicators
1. **Startup Success**: "✅ TRADING BOT STARTUP COMPLETE - NO ERRORS, NO WARNINGS"
2. **Component Health**: All core components report operational status
3. **Build Status**: Clean build with 0 errors, 0 warnings

### Regular Maintenance
- **Daily**: Verify startup health check
- **Weekly**: Update dependencies if needed
- **Monthly**: Review logs and performance metrics

## 🔄 Deployment

### Current Deployment Status
- ✅ **Development**: Fully operational
- ✅ **Testing**: Core components verified
- ⚠️ **Production**: Requires complete system setup

### Deployment Command
```bash
# Verified working deployment
git clone <repository>
cd trading-bot-c-
dotnet restore
dotnet build
dotnet run --project SimpleBot/SimpleBot.csproj
```

## 📝 Maintenance Log

### Recent Changes
- ✅ **2025-09-08**: Successfully implemented Phase 6-7 cleanup
- ✅ **2025-09-08**: Created working SimpleBot launcher (0 errors, 0 warnings)
- ✅ **2025-09-08**: Resolved circular dependency issues
- ✅ **2025-09-08**: Cleaned up 100+ temporary files from root directory

### Known Issues
- ⚠️ **Abstractions Project**: Has unresolved dependencies (temporarily bypassed)
- ⚠️ **Complex Orchestrators**: Require additional setup for full functionality
- ✅ **Core Components**: Fully operational and verified working

### Next Steps
1. Complete CI/CD pipeline verification
2. Implement comprehensive health monitoring
3. Restore full trading system functionality
4. Complete production deployment testing