#!/bin/bash
# Wrapper script to run the bot with correct Python environment

export PYTHON_EXECUTABLE=/home/codespace/.python/current/bin/python3

cd /workspaces/QBot
./src/UnifiedOrchestrator/bin/Debug/net8.0/UnifiedOrchestrator "$@"
