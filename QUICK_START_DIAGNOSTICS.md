# 🚀 Quick Start: Bot Diagnostics

## 3-Minute Setup Guide

### Step 1: Navigate to Workflow (30 seconds)
1. Open your browser to GitHub
2. Go to the **Quotraders/QBot** repository
3. Click the **Actions** tab
4. Find **"🤖 Bot Launch Diagnostics - Self-Hosted"**

### Step 2: Run Workflow (30 seconds)
1. Click the workflow name
2. Click **"Run workflow"** button (top right)
3. Keep defaults:
   - Runtime: 5 minutes
   - Detailed logs: enabled
4. Click **"Run workflow"** to confirm

### Step 3: Wait for Completion (5 minutes)
- Workflow executes automatically
- Progress shown in real-time
- Last 150 lines of output displayed in UI

### Step 4: Download Results (1 minute)
1. Scroll to bottom of workflow run page
2. Find **"Artifacts"** section
3. Click **"bot-diagnostics-run-{number}"**
4. ZIP file downloads automatically

### Step 5: Analyze (2 minutes)
1. Extract the ZIP file
2. Open **console-output-*.log** in text editor
3. Review startup sequence
4. Check for errors

## What You Get

```
📦 bot-diagnostics-run-42.zip
├── 📄 system-info.json          ← System environment
├── 📄 console-output-*.log      ← Complete bot output
├── 📄 error-output-*.log        ← Error messages
└── 📄 structured-log-*.json     ← Parsed events
```

## Quick Analysis Commands

### Find Errors
```bash
grep -i "error\|exception" console-output-*.log
```

### Check Startup
```bash
grep "STARTUP" console-output-*.log
```

### View Event Timeline
```bash
jq -r '.events[] | "\(.timestamp) \(.message)"' structured-log-*.json
```

## Common Issues

| Problem | Solution |
|---------|----------|
| Workflow not visible | Verify self-hosted runner is online |
| Build fails | Check .NET SDK installed on runner |
| No artifacts | Wait for workflow to fully complete |
| Large files | Reduce runtime_minutes parameter |

## Next Steps

- 📚 **Full Guide**: [.github/workflows/README-bot-diagnostics.md](.github/workflows/README-bot-diagnostics.md)
- 📖 **Examples**: [docs/bot-diagnostics-examples.md](docs/bot-diagnostics-examples.md)
- 🏗️ **Architecture**: [docs/bot-diagnostics-architecture.md](docs/bot-diagnostics-architecture.md)
- 📝 **Quick Ref**: [docs/bot-diagnostics-quick-reference.md](docs/bot-diagnostics-quick-reference.md)

## Support

Questions? Check the documentation links above or review the workflow run logs for details.

---

**⏱️ Total Time**: ~10 minutes from trigger to analysis
**🔒 Safety**: Always runs in DRY_RUN mode (no live trading)
**📦 Storage**: 30-day retention with unlimited downloads
