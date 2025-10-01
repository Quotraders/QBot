# Configuration Directory

## Schema Validation

All configuration files must conform to `schema.json`. Validate configurations using:

```bash
# Validate a config file
python3 -c "import json, jsonschema; 
schema = json.load(open('config/schema.json')); 
config = json.load(open('config/enhanced-trading-bot.json')); 
jsonschema.validate(config, schema); 
print('✅ Configuration valid')"
```

## Expected Defaults & Verification

- **Last Verified:** 2025-01-02
- **Configuration Version:** 1.0
- **Verification Status:** ✅ Schema-validated

### Standard Thresholds
- **Confidence Minimum:** 0.65 (configurable: 0.5-0.9)
- **Feature Drift Threshold:** 0.3 (configurable: 0.1-0.5)
- **Order Book Imbalance Max:** 0.8 (configurable: 0.6-0.9)
- **ES/NQ Tick Size:** 0.25 (MANDATORY - cannot be changed)

### Deprecated Configuration Files

The following files contain legacy configurations and should be migrated or retired:

- `bundles.stage.json` - Contains hardcoded thresholds, should align with orchestrator or be retired
- Strategy files with hardcoded values should use bounds.json references

## Guardrail Requirements

1. **ES/NQ Tick Rounding:** All prices must round to 0.25 increments
2. **Risk Validation:** Risk calculations must be > 0 before execution  
3. **DRY_RUN Default:** Default to simulation mode unless explicitly enabled
4. **Schema Compliance:** All JSON configs must validate against schema.json

## Configuration Update Process

1. Update configuration values
2. Validate against schema: `./scripts/validate-config-schema.sh`
3. Test with dry-run mode
4. Document changes in this file
5. Update verification timestamp