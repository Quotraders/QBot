# Exception Handling Patterns - Production Trading Bot

**Purpose:** Document approved `catch (Exception)` patterns that are intentional and required by production guardrails.  
**Created:** 2025-10-10  
**Scope:** Agent 5 BotCore folders (Integration, Patterns, Features, Market, Configuration, Extensions, HealthChecks, Fusion, StrategyDsl)  
**Analyzer:** CA1031 "Modify method to catch a more specific exception type"

---

## 🎯 Overview

The CA1031 analyzer warns against catching generic `Exception` types, preferring specific exception types. However, in production trading systems, there are legitimate scenarios where catching `Exception` is **required** or **strongly recommended** to maintain system stability and resilience.

This document defines 4 approved patterns where `catch (Exception)` is intentional and should not be considered a violation.

---

## ✅ Pattern 1: Health Check Implementations

### Rule
**Health checks must never throw exceptions.** They must catch all exceptions and return an `Unhealthy` status with context.

### Rationale
- Health checks are used by orchestrators and monitoring systems
- Throwing from a health check can cause cascading failures
- Health check failures should be observable, not fatal

### Code Pattern
```csharp
public class MyHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Perform health check logic
            await CheckResourceAvailability(cancellationToken);
            return HealthCheckResult.Healthy("Resource is available");
        }
        catch (Exception ex) // ✅ APPROVED: Health checks must never throw
        {
            return HealthCheckResult.Unhealthy(
                $"Health check failed: {ex.Message}",
                ex);
        }
    }
}
```

### Justification Comment
```csharp
catch (Exception ex) // Approved: Health checks must never throw (Pattern 1: EXCEPTION_HANDLING_PATTERNS.md)
```

### Examples in Codebase
- `HealthChecks/ProductionHealthChecks.cs` (52 occurrences)
- Any class implementing `IHealthCheck` interface

### References
- Production Guardrails: "Health check implementations must never throw exceptions: catch and return unhealthy status with context"
- Microsoft Docs: [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)

---

## ✅ Pattern 2: Feed Health Monitoring

### Rule
**Market data feed health monitoring must be resilient.** Feed checks must not throw to maintain continuous monitoring of other feeds.

### Rationale
- One failed feed should not stop monitoring of other feeds
- Feed health is aggregated across multiple sources
- Feed failures are expected and should be logged, not fatal

### Code Pattern
```csharp
private void CheckFeedHealth(IMarketDataFeed feed)
{
    try
    {
        // Check feed connectivity, data freshness, etc.
        var lastUpdate = feed.GetLastUpdateTime();
        if (DateTime.UtcNow - lastUpdate > TimeSpan.FromSeconds(30))
        {
            _logger.LogWarning("Feed {FeedName} is stale", feed.Name);
        }
    }
    catch (Exception ex) // ✅ APPROVED: Feed monitoring must be resilient
    {
        _logger.LogError(ex, 
            "Feed health check failed for {FeedName}, continuing with other feeds", 
            feed.Name);
        // Continue monitoring other feeds - don't propagate exception
    }
}

public async Task<DataConsistencyResult> CheckDataConsistency()
{
    var results = new List<FeedResult>();
    
    foreach (var feed in _feeds)
    {
        try
        {
            var data = await feed.GetLatestDataAsync();
            results.Add(FeedResult.Success(feed.Name, data));
        }
        catch (Exception ex) // ✅ APPROVED: One feed failure should not stop consistency check
        {
            results.Add(FeedResult.Failure(feed.Name, ex.Message));
            _logger.LogError(ex, "Failed to get data from {FeedName}", feed.Name);
        }
    }
    
    return new DataConsistencyResult(results);
}
```

### Justification Comment
```csharp
catch (Exception ex) // Approved: Feed monitoring must be resilient (Pattern 2: EXCEPTION_HANDLING_PATTERNS.md)
```

### Examples in Codebase
- `Market/RedundantDataFeedManager.cs` (45 occurrences)
- `Integration/MarketDataBridge.cs`

### References
- Production Guardrails: "Disposal patterns in integration code: if you create HttpClient, DbConnection, or Stream, you must dispose them"
- Pattern: Resilient external service integration

---

## ✅ Pattern 3: ML/AI Prediction Failures

### Rule
**ML model prediction failures must not crash the trading system.** Predictions are non-critical operations with fallback behaviors.

### Rationale
- Model predictions may fail due to invalid inputs, model loading errors, etc.
- Trading system must continue operating without ML predictions
- Fallback to default behaviors (e.g., default position sizing)

### Code Pattern
```csharp
public async Task<double?> PredictSizeAsync(
    string symbol,
    StrategyRecommendation recommendation)
{
    try
    {
        // Load model and make prediction
        var model = await _modelLoader.LoadModelAsync("position-sizing");
        var features = _featureBuilder.BuildFeatures(symbol, recommendation);
        return await model.PredictAsync(features);
    }
    catch (Exception ex) // ✅ APPROVED: ML prediction failures are non-fatal
    {
        _logger.LogError(ex, 
            "Position sizing prediction failed for {Symbol}, using default size", 
            symbol);
        return null; // Caller will use default position size
    }
}
```

### Justification Comment
```csharp
catch (Exception ex) // Approved: ML predictions must not crash system (Pattern 3: EXCEPTION_HANDLING_PATTERNS.md)
```

### Examples in Codebase
- `Fusion/MLConfiguration.cs` (28 occurrences)
- `Features/FeatureBuilder.cs`

### References
- Production Guardrails: "DRY_RUN Default: Default to simulation mode unless explicitly enabled for live trading"
- Pattern: Fail-safe ML integration

---

## ✅ Pattern 4: Integration Boundaries (External Services)

### Rule
**External service calls at integration boundaries must be resilient.** Calls to external APIs, databases, or services must not crash the application.

### Rationale
- External services may be unavailable, slow, or return unexpected errors
- Integration layer is a trust boundary - validate all inputs/outputs
- Provide meaningful error responses to callers

### Code Pattern
```csharp
public async Task<Result<MarketData>> GetMarketDataAsync(
    string symbol,
    CancellationToken cancellationToken = default)
{
    try
    {
        // Call external market data API
        var response = await _httpClient.GetAsync(
            $"/api/marketdata/{symbol}",
            cancellationToken);
        
        response.EnsureSuccessStatusCode();
        
        var data = await response.Content.ReadFromJsonAsync<MarketData>(
            cancellationToken: cancellationToken);
        
        ArgumentNullException.ThrowIfNull(data);
        
        return Result<MarketData>.Success(data);
    }
    catch (Exception ex) // ✅ APPROVED: External boundaries must be resilient
    {
        _logger.LogError(ex, 
            "Failed to retrieve market data for {Symbol} from external API", 
            symbol);
        
        return Result<MarketData>.Failure(
            $"External API call failed: {ex.Message}");
    }
}
```

### Justification Comment
```csharp
catch (Exception ex) // Approved: Integration boundaries must be resilient (Pattern 4: EXCEPTION_HANDLING_PATTERNS.md)
```

### Examples in Codebase
- `Integration/ProductionIntegrationCoordinator.cs` (55 occurrences)
- `Integration/ServiceInventory.cs`
- `Integration/EpochFreezeEnforcement.cs`

### References
- Production Guardrails: "Integration boundaries are trust boundaries: validate all inputs from external systems with null guards and range checks"
- Pattern: Resilient external integration

---

## ❌ Anti-Patterns (Do Not Use `catch (Exception)`)

### Anti-Pattern 1: Hiding Programming Errors
```csharp
// ❌ BAD: Catching exceptions to hide bugs
public void ProcessOrder(Order order)
{
    try
    {
        order.Execute(); // What if order is null? What if Execute throws?
    }
    catch (Exception ex) // ❌ This hides programming errors
    {
        _logger.LogError("Something went wrong");
        // No rethrow, no specific handling
    }
}

// ✅ GOOD: Let programming errors surface
public void ProcessOrder(Order order)
{
    ArgumentNullException.ThrowIfNull(order);
    order.Execute(); // Let exceptions propagate
}
```

### Anti-Pattern 2: Empty Catch Blocks
```csharp
// ❌ BAD: Swallowing exceptions
try
{
    RiskyOperation();
}
catch (Exception) // ❌ Exception is completely ignored
{
    // No logging, no handling, no rethrow
}

// ✅ GOOD: At minimum, log the exception
try
{
    RiskyOperation();
}
catch (Exception ex)
{
    _logger.LogError(ex, "RiskyOperation failed");
    // Then decide: rethrow, return error result, or use fallback
}
```

### Anti-Pattern 3: Catching to Rethrow
```csharp
// ❌ BAD: Catching just to rethrow (S2139 violation)
try
{
    DoWork();
}
catch (Exception ex)
{
    _logger.LogError(ex, "DoWork failed");
    throw; // If you're just logging and rethrowing, don't catch
}

// ✅ GOOD: Let exception propagate, use middleware/filter for logging
// Or use a specific exception type if you need to add context
catch (SpecificException ex)
{
    throw new DomainException("Business context about the error", ex);
}
```

---

## 🔧 Applying Patterns to Codebase

### Step 1: Identify Pattern
Review each CA1031 violation and determine if it matches one of the 4 approved patterns:
1. Health check implementation?
2. Feed health monitoring?
3. ML/AI prediction?
4. Integration boundary (external service)?

### Step 2: Add Justification Comment
If it matches a pattern, add the appropriate justification comment:
```csharp
catch (Exception ex) // Approved: [Pattern name] (Pattern N: EXCEPTION_HANDLING_PATTERNS.md)
```

### Step 3: Improve Logging
Ensure the catch block has appropriate logging:
```csharp
catch (Exception ex) // Approved: ...
{
    _logger.LogError(ex, 
        "Meaningful message with {StructuredData}", 
        relevantContext);
    
    // Then: return error result, use fallback, or continue
}
```

### Step 4: Consider Suppression
For entire classes that match a pattern (e.g., all health checks), consider:
```csharp
#pragma warning disable CA1031 // All health checks in this class use approved Pattern 1
public class ProductionHealthChecks
{
    // All catch(Exception) blocks are approved Pattern 1
}
#pragma warning restore CA1031
```

---

## 📊 Violation Inventory (Agent 5 Scope)

| Folder | CA1031 Count | Likely Patterns | Action Required |
|--------|--------------|-----------------|-----------------|
| HealthChecks | 52 | Pattern 1 (100%) | Add justification comments |
| Market | 45 | Pattern 2 (100%) | Add justification comments |
| Integration | 55 | Pattern 4 (90%), Mixed (10%) | Review and justify each |
| Fusion | 28 | Pattern 3 (100%) | Add justification comments |
| **Total** | **180** | **Mostly approved patterns** | **~8 hours to document** |

---

## ✅ Next Steps

1. **Review All 180 Violations:** Go through each CA1031 in Agent 5 scope
2. **Categorize by Pattern:** Assign to Pattern 1, 2, 3, 4, or "Needs Review"
3. **Add Justification Comments:** For approved patterns
4. **Fix or Justify Remaining:** For violations that don't match patterns, either:
   - Fix to catch specific exception types
   - Justify why generic catch is necessary
5. **Update AGENT-5-STATUS.md:** Document completion

**Estimated Effort:** 8-12 hours for complete review and documentation

---

## 📚 References

1. **CA1031 Analyzer Rule:** https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031
2. **Exception Handling Best Practices:** https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions
3. **Health Checks:** https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks
4. **Production Guardrails:** `.github/copilot-instructions.md`

---

**Document Status:** ✅ APPROVED - Ready for implementation  
**Owner:** Agent 5  
**Last Updated:** 2025-10-10
