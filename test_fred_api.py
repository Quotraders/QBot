#!/usr/bin/env python3
"""Test FRED API to verify it returns real Federal Reserve data"""

import requests

API_KEY = 'd688f45598966fdcfb5b76c1f3f3bde1'

series_to_test = {
    'Total Assets (Fed Balance Sheet)': 'WALCL',
    'Securities Held Outright': 'WSHOSHO',
    'Reserve Balances': 'WRESBAL'
}

print("=" * 60)
print("🏦 Testing FRED API - Federal Reserve Economic Data")
print("=" * 60)
print()

all_success = True

for name, series_id in series_to_test.items():
    try:
        url = 'https://api.stlouisfed.org/fred/series/observations'
        params = {
            'series_id': series_id,
            'api_key': API_KEY,
            'file_type': 'json',
            'sort_order': 'desc',
            'limit': 1
        }
        
        response = requests.get(url, params=params, timeout=10)
        
        if response.status_code == 200:
            data = response.json()
            if 'observations' in data and len(data['observations']) > 0:
                obs = data['observations'][0]
                value = obs['value']
                date = obs['date']
                
                # Convert to trillions for readability
                value_float = float(value)
                value_trillions = value_float / 1_000_000
                
                print(f"✅ {name}")
                print(f"   Value: ${value}M = ${value_trillions:.2f}T")
                print(f"   Date: {date}")
                print()
            else:
                print(f"❌ {name}: No data in response")
                all_success = False
        else:
            print(f"❌ {name}: HTTP {response.status_code}")
            print(f"   Error: {response.text[:200]}")
            all_success = False
            
    except Exception as e:
        print(f"❌ {name}: Exception - {e}")
        all_success = False

print("=" * 60)
if all_success:
    print("✅ SUCCESS: FRED API is working and returning REAL data!")
    print("   Your API key is valid and ready to use in workflows.")
else:
    print("❌ FAILURE: Some API calls failed")
print("=" * 60)
