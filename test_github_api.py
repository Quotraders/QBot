#!/usr/bin/env python3
"""Test GitHub API to verify it returns real workflow data"""

import requests
import os
from datetime import datetime

# GitHub token from environment (gh CLI sets this)
GITHUB_TOKEN = os.popen('gh auth token').read().strip()
REPO_OWNER = 'c-trading-bo'
REPO_NAME = 'trading-bot-c-'

headers = {
    'Authorization': f'token {GITHUB_TOKEN}',
    'Accept': 'application/vnd.github.v3+json'
} if GITHUB_TOKEN else {}

print("=" * 60)
print("🔧 Testing GitHub API - Workflow Metrics")
print("=" * 60)
print()

# Test with one workflow
test_workflow = 'data_feature_build.yml'

try:
    url = f'https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/actions/workflows/{test_workflow}/runs'
    params = {'per_page': 5, 'status': 'completed'}
    
    response = requests.get(url, headers=headers, params=params, timeout=10)
    
    if response.status_code == 200:
        data = response.json()
        runs = data.get('workflow_runs', [])
        
        if runs:
            print(f"✅ Successfully fetched workflow runs for: {test_workflow}")
            print(f"   Total runs found: {data.get('total_count', 'N/A')}")
            print()
            print("Recent runs:")
            
            for i, run in enumerate(runs[:5], 1):
                status = run.get('conclusion', 'unknown')
                created = run.get('created_at', 'N/A')
                run_id = run.get('id', 'N/A')
                
                # Calculate runtime
                if run.get('run_started_at') and run.get('updated_at'):
                    try:
                        start = datetime.fromisoformat(run['run_started_at'].replace('Z', '+00:00'))
                        end = datetime.fromisoformat(run['updated_at'].replace('Z', '+00:00'))
                        runtime_seconds = (end - start).total_seconds()
                        runtime_str = f"{runtime_seconds:.0f}s"
                    except:
                        runtime_str = "N/A"
                else:
                    runtime_str = "N/A"
                
                emoji = "✅" if status == "success" else "❌"
                print(f"   {i}. {emoji} Run #{run_id}: {status} - {runtime_str} - {created}")
            
            print()
            print("=" * 60)
            print("✅ SUCCESS: GitHub API is working and returning REAL data!")
            print("   Your workflows can fetch actual run metrics.")
            print("=" * 60)
        else:
            print(f"⚠️ No workflow runs found for {test_workflow}")
            print("   This might be normal if the workflow hasn't run yet.")
    elif response.status_code == 401:
        print("❌ Authentication failed")
        print("   GitHub token may be invalid or missing")
        print(f"   Token present: {bool(GITHUB_TOKEN)}")
    elif response.status_code == 404:
        print(f"❌ Workflow not found: {test_workflow}")
        print("   Check that the workflow file name is correct")
    else:
        print(f"❌ HTTP {response.status_code}")
        print(f"   Response: {response.text[:200]}")
        
except Exception as e:
    print(f"❌ Exception: {e}")
