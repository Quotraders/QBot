# Cloud Model Synchronization Integration

## Overview
This document describes the integration between CloudModelSynchronizationService and UnifiedTradingBrain to enable automatic model hot-swapping from GitHub Actions workflows.

## Architecture

### Components

1. **CloudModelSynchronizationService** (`src/BotCore/Services/CloudModelSynchronizationService.cs`)
   - Background service that polls GitHub Actions API
   - Downloads new model artifacts (ONNX, PKL, JSON)
   - Extracts models to `models/` directory
   - Triggers hot-swap in UnifiedTradingBrain

2. **UnifiedTradingBrain** (`src/BotCore/Brain/UnifiedTradingBrain.cs`)
   - Main AI trading brain
   - Has existing `ReloadModelsAsync()` method with validation
   - Performs atomic model swaps with backup/restore
   - Validates models before deployment

### Flow

```
GitHub Actions Workflow
  └─> Train Models (Neural UCB, CVaR-PPO, HMM, etc.)
      └─> Upload as Artifacts
          └─> CloudModelSynchronizationService (every 15-60 min)
              ├─> Download new artifacts
              ├─> Extract ONNX models
              ├─> Validate checksums (if metadata.json present)
              └─> Call UnifiedTradingBrain.ReloadModelsAsync()
                  ├─> Validate new model
                  ├─> Create backup of current model
                  ├─> Atomic swap
                  └─> Log success/failure
```

## Implementation Details

### Changes Made

#### CloudModelSynchronizationService.cs
```csharp
// Added UnifiedTradingBrain injection
private readonly UnifiedTradingBrain? _tradingBrain;

public CloudModelSynchronizationService(
    ILogger<CloudModelSynchronizationService> logger,
    HttpClient httpClient,
    IMLMemoryManager memoryManager,
    IConfiguration configuration,
    UnifiedTradingBrain? tradingBrain = null,  // NEW
    ProductionResilienceService? resilienceService = null,
    ProductionMonitoringService? monitoringService = null)
{
    _tradingBrain = tradingBrain;
    // ...
}
```

#### DownloadAndUpdateModelAsync Method
```csharp
// After extracting ONNX model, trigger hot-swap
if (extracted && onnxModelPath != null && _tradingBrain != null)
{
    _logger.LogInformation("🔄 [CLOUD-SYNC] Triggering model hot-swap for: {ModelPath}", onnxModelPath);
    var reloadSuccess = await _tradingBrain.ReloadModelsAsync(onnxModelPath, cancellationToken);
    
    if (reloadSuccess)
    {
        _logger.LogInformation("✅ [CLOUD-SYNC] Model hot-swap completed successfully");
    }
    else
    {
        _logger.LogWarning("⚠️ [CLOUD-SYNC] Model hot-swap failed - keeping current model");
    }
}
```

#### Program.cs Service Registration
```csharp
services.AddSingleton<BotCore.Services.CloudModelSynchronizationService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<BotCore.Services.CloudModelSynchronizationService>>();
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient(nameof(BotCore.Services.CloudModelSynchronizationService));
    var memoryManager = provider.GetRequiredService<BotCore.ML.IMLMemoryManager>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var tradingBrain = provider.GetService<BotCore.Brain.UnifiedTradingBrain>();  // NEW
    var resilienceService = provider.GetService<BotCore.Services.ProductionResilienceService>();
    var monitoringService = provider.GetService<BotCore.Services.ProductionMonitoringService>();
    
    return new BotCore.Services.CloudModelSynchronizationService(
        logger, httpClient, memoryManager, configuration, tradingBrain, resilienceService, monitoringService);
});
```

## Configuration

### appsettings.json
```json
{
  "CloudSync": {
    "IntervalMinutes": 15,
    "Enabled": true
  },
  "GitHub": {
    "Owner": "c-trading-bo",
    "Repository": "trading-bot-c-",
    "Token": ""
  }
}
```

### Environment Variables
- `GITHUB_TOKEN`: GitHub Personal Access Token with `actions:read` and `contents:read` permissions
- `GitHub:Owner`: Repository owner (default: "c-trading-bo")
- `GitHub:Repository`: Repository name (default: "trading-bot-c-")
- `CloudSync:IntervalMinutes`: Sync interval in minutes (default: 15)

## Production Safety

### Built-in Safeguards

1. **Model Validation**: UnifiedTradingBrain.ReloadModelsAsync() validates models before deployment
   - Checks model signature compatibility
   - Validates input/output dimensions
   - Ensures model can be loaded by ONNX Runtime

2. **Automatic Backups**: Creates timestamped backups before swapping
   - Location: `models/backup/unified_brain_yyyyMMdd_HHmmss.onnx`
   - Allows instant rollback if issues detected

3. **Atomic Operations**: Model swap is atomic - either succeeds completely or reverts
   - No partial state transitions
   - No trading interruption

4. **Graceful Degradation**: If hot-swap fails, keeps using current model
   - Bot continues trading without disruption
   - Logs failure for investigation

5. **Rate Limiting**: Prevents excessive GitHub API calls
   - Minimum 5-minute interval between syncs
   - Respects GitHub API rate limits

### Monitoring & Logging

All operations are logged with structured logging:
- `🌐 [CLOUD-SYNC]` - General sync operations
- `🔄 [MODEL-RELOAD]` - Model reload operations
- `✅` - Success indicators
- `⚠️` - Warnings
- `❌` - Errors

Example log output:
```
🌐 [CLOUD-SYNC] Starting model synchronization...
🌐 [CLOUD-SYNC] Downloading new model: trained-models-12345 from run 98765
🌐 [CLOUD-SYNC] Model extracted: /home/app/models/rl/cvar_ppo_agent.onnx
🔄 [CLOUD-SYNC] Triggering model hot-swap for: /home/app/models/rl/cvar_ppo_agent.onnx
🔄 [MODEL-RELOAD] Starting model reload: /home/app/models/rl/cvar_ppo_agent.onnx
💾 [MODEL-RELOAD] Backup created: /home/app/models/backup/unified_brain_20250108_143022.onnx
✅ [MODEL-RELOAD] Model reloaded successfully
✅ [CLOUD-SYNC] Model hot-swap completed successfully
```

## Testing

### Manual Testing
1. Trigger a GitHub Actions workflow that produces model artifacts
2. Wait for CloudModelSynchronizationService to poll (default: 15 minutes)
3. Check logs for download and hot-swap messages
4. Verify new model is being used in trading decisions

### Integration Testing
The service can be tested in isolation:
```csharp
// In test environment
var service = provider.GetRequiredService<CloudModelSynchronizationService>();
await service.SynchronizeModelsAsync(CancellationToken.None);
```

### Smoke Test
Run the unified orchestrator with `--smoke` flag:
```bash
dotnet run --project src/UnifiedOrchestrator -- --smoke
```

## Future Enhancements

### Potential Additions (Not Implemented Yet)
1. **Canary Monitoring**: Monitor performance after model swap for degradation
   - Track win rate, drawdown, Sharpe ratio
   - Automatic rollback if metrics degrade >15%
   - Duration: 30-90 minutes

2. **Backup Retention**: Keep last N backups (configurable)
   - Default: 5 backups
   - Automatic cleanup of older versions

3. **Email Alerts**: Notify on successful/failed swaps
   - Uses existing Gmail SMTP integration
   - Template: "New models installed - version {timestamp}"

4. **Metadata Validation**: SHA256 checksum verification
   - Validate downloaded models against metadata.json
   - Reject corrupted downloads

5. **Gate 5 Integration**: Link to existing canary validation system
   - Uses ParameterChangeTracker
   - Monitors trading performance post-swap

## Troubleshooting

### Common Issues

1. **No GitHub token configured**
   ```
   ⚠️ [CLOUD-SYNC] No GitHub token configured - cloud model sync will be disabled
   ```
   **Solution**: Set `GITHUB_TOKEN` environment variable or configure in appsettings.json

2. **Model download fails**
   ```
   ⚠️ [CLOUD-SYNC] Failed to download artifact {ArtifactId}
   ```
   **Solution**: Check GitHub token permissions and network connectivity

3. **Model hot-swap fails**
   ```
   ⚠️ [CLOUD-SYNC] Model hot-swap failed - keeping current model
   ```
   **Solution**: Check model compatibility and ONNX Runtime installation

4. **Rate limiting**
   ```
   🌐 [CLOUD-SYNC] Rate limiting - skipping sync
   ```
   **Solution**: This is normal behavior to prevent excessive API calls

## References

- [CloudModelSynchronizationService.cs](src/BotCore/Services/CloudModelSynchronizationService.cs)
- [UnifiedTradingBrain.cs](src/BotCore/Brain/UnifiedTradingBrain.cs)
- [Program.cs](src/UnifiedOrchestrator/Program.cs)
- [GitHub Actions API Documentation](https://docs.github.com/en/rest/actions)
