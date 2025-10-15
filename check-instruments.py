#!/usr/bin/env python3
"""Quick check for ES instruments"""

import asyncio
import os
import sys
from pathlib import Path

# Load .env
env_path = Path(__file__).parent / '.env'
if env_path.exists():
    with open(env_path, 'r', encoding='utf-8', errors='ignore') as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                os.environ[key.strip()] = value.strip()

from project_x_py import ProjectX

async def main():
    client = ProjectX(
        api_key=os.getenv('TOPSTEPX_API_KEY') or os.getenv('PROJECT_X_API_KEY'),
        username=os.getenv('TOPSTEPX_USERNAME') or os.getenv('PROJECT_X_USERNAME'),
        account_name=os.getenv('TOPSTEPX_ACCOUNT_NAME') or os.getenv('PROJECT_X_ACCOUNT_NAME')
    )
    
    await client.authenticate()
    print("✅ Authenticated\n")
    
    print("Searching for ES instruments...")
    instruments = await client.search_instruments(query="ES")
    
    print(f"\nFound {len(instruments)} instruments:")
    for i, inst in enumerate(instruments[:10]):
        print(f"\n{i+1}. {inst}")

asyncio.run(main())
