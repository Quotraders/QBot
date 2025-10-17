# VS Code Configuration Templates

This folder contains template VS Code configuration files that enhance your development experience with the QBot project.

## 📁 Files Included

- **launch.json** - Debug launch configurations for UnifiedOrchestrator
- **tasks.json** - Build, run, and test tasks
- **settings.json** - Workspace settings optimized for C# development
- **extensions.json** - Recommended VS Code extensions

## 🚀 How to Use

### Option 1: Copy All Files (Recommended)

```bash
# Windows PowerShell
Copy-Item -Path .vscode-template/* -Destination .vscode/ -Force

# Linux/macOS
cp -r .vscode-template/* .vscode/
```

### Option 2: Copy Individual Files

```bash
# Just the launch configuration for debugging
cp .vscode-template/launch.json .vscode/

# Just the tasks
cp .vscode-template/tasks.json .vscode/
```

## 🎯 What You Get

### Debug Configurations (launch.json)

Press **F5** in VS Code to launch:

- **Launch UnifiedOrchestrator (Debug)** - Standard debug mode
- **Launch UnifiedOrchestrator (Release)** - Production build mode  
- **Launch UnifiedOrchestrator (Verbose Logging)** - With detailed logs
- **Attach to UnifiedOrchestrator** - Attach to running process

### Build Tasks (tasks.json)

Use **Terminal → Run Task** or **Ctrl+Shift+B**:

- **build-orchestrator** - Build UnifiedOrchestrator (default)
- **run-orchestrator** - Run the bot
- **check-python** - Verify Python and SDK installation
- **verify-env** - Check if .env file exists

### Workspace Settings (settings.json)

- Format on save enabled
- GitHub Copilot enabled for all file types
- Optimized search exclusions for better performance
- C# semantic highlighting

### Recommended Extensions (extensions.json)

VS Code will prompt you to install:

- C# Dev Kit
- GitHub Copilot & Copilot Chat
- Python
- EditorConfig

## 🔧 Customization

Feel free to modify these files in your `.vscode/` folder (which is git-ignored) to match your preferences. The templates here serve as a starting point.

## 📚 Related Documentation

- **[QUICK_START_COPILOT.md](../QUICK_START_COPILOT.md)** - Quick start guide
- **[COPILOT_REAL_TIME_DEBUGGING_GUIDE.md](../COPILOT_REAL_TIME_DEBUGGING_GUIDE.md)** - Complete debugging guide

## 💡 Pro Tips

1. **Use F5** to start debugging - breakpoints will work automatically
2. **Use Ctrl+Shift+P** → "Tasks: Run Task" for quick commands
3. **Use Ctrl+Shift+I** to open Copilot Chat for inline help
4. **Set breakpoints** in `Program.cs` and `TopstepXAdapterService.cs` to understand startup flow

## ⚠️ Note

The `.vscode/` folder is git-ignored to prevent conflicts with personal settings. These templates are provided so you can set up your environment consistently.
