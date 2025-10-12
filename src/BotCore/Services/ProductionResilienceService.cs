using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace BotCore.Services;

/// <summary>
/// Production-grade resilience service implementing retry logic, circuit breakers, and graceful degradation
/// Essential for reliable financial trading operations
/// </summary>
public class ProductionResilienceService
{
    private readonly ILogger<ProductionResilienceService> _logger;
    private readonly ResilienceConfiguration _config;
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers = new();

    public ProductionResilienceService(ILogger<ProductionResilienceService> logger, IOptions<ResilienceConfiguration> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        _logger = logger;
        _config = config.Value;
        
        // Note: Configuration validation is handled by ResilienceConfigurationValidator in DI
    }

    /// <summary>
    /// Execute operation with retry logic and circuit breaker protection
    /// </summary>
    public async Task<T> ExecuteWithResilienceAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        
        var circuitBreaker = GetOrCreateCircuitBreaker(operationName);
        
        // Check circuit breaker state
        if (circuitBreaker.State == CircuitState.Open)
        {
            if (DateTime.UtcNow - circuitBreaker.LastFailure < TimeSpan.FromMilliseconds(_config.CircuitBreakerTimeoutMs))
            {
                _logger.LogWarning("🚫 [RESILIENCE] Circuit breaker OPEN for {Operation}, falling back to default", operationName);
                throw new CircuitBreakerOpenException($"Circuit breaker is open for {operationName}");
            }
            else
            {
                // Try half-open state
                circuitBreaker.State = CircuitState.HalfOpen;
                _logger.LogInformation("🔄 [RESILIENCE] Circuit breaker HALF-OPEN for {Operation}, attempting recovery", operationName);
            }
        }

        var lastException = (Exception?)null;
        
        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var result = await operation(cancellationToken).ConfigureAwait(false);
                
                // Success - reset circuit breaker
                if (circuitBreaker.State == CircuitState.HalfOpen)
                {
                    circuitBreaker.State = CircuitState.Closed;
                    circuitBreaker.FailureCount = 0;
                    _logger.LogInformation("✅ [RESILIENCE] Circuit breaker CLOSED for {Operation} - recovery successful", operationName);
                }
                
                if (attempt > 1)
                {
                    _logger.LogInformation("✅ [RESILIENCE] Operation {Operation} succeeded on attempt {Attempt}", operationName, attempt);
                }
                
                return result;
            }
            catch (Exception ex) when (IsRetriableException(ex))
            {
                lastException = ex;
                circuitBreaker.FailureCount++;
                
                _logger.LogWarning(ex, "⚠️ [RESILIENCE] Operation {Operation} failed on attempt {Attempt}/{MaxAttempts}: {Error}", 
                    operationName, attempt, _config.MaxRetries, ex.Message);
                
                // Check if we should open the circuit breaker
                if (circuitBreaker.FailureCount >= _config.CircuitBreakerThreshold)
                {
                    circuitBreaker.State = CircuitState.Open;
                    circuitBreaker.LastFailure = DateTime.UtcNow;
                    _logger.LogError("🚫 [RESILIENCE] Circuit breaker OPENED for {Operation} after {Failures} failures", 
                        operationName, circuitBreaker.FailureCount);
                        
                    // Emit structured telemetry for circuit breaker open (always enabled for production compliance)
                    _logger.LogError("🚫 [RESILIENCE][CIRCUIT-OPEN] Operation: {Operation}, Failures: {Failures}, Timestamp: {Timestamp}", 
                        operationName, circuitBreaker.FailureCount, DateTime.UtcNow);
                }
                
                if (attempt < _config.MaxRetries)
                {
                    var delay = CalculateExponentialBackoff(attempt);
                    _logger.LogDebug("⏳ [RESILIENCE] Waiting {Delay}ms before retry {Attempt}", delay.TotalMilliseconds, attempt + 1);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Non-retriable exception
                throw new InvalidOperationException($"❌ [RESILIENCE] Non-retriable error in {operationName}", ex);
            }
        }
        
        // All retries exhausted
        _logger.LogError(lastException, "❌ [RESILIENCE] Operation {Operation} failed after {Attempts} attempts", operationName, _config.MaxRetries);
        throw lastException ?? new InvalidOperationException($"Operation {operationName} failed after {_config.MaxRetries} attempts");
    }

    /// <summary>
    /// Execute HTTP operation with proper timeout and retry handling
    /// </summary>
    public Task<T> ExecuteHttpOperationAsync<T>(
        string operationName,
        Func<HttpClient, CancellationToken, Task<T>> httpOperation,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithResilienceAsync(operationName, async (ct) =>
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(_config.HttpTimeoutMs));
            
            return await httpOperation(httpClient, timeoutCts.Token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Fallback mechanism for critical trading operations
    /// </summary>
    public T ExecuteWithFallback<T>(
        string operationName,
        Func<T> primaryOperation,
        Func<T> fallbackOperation,
        bool logFallback = true)
    {
        ArgumentNullException.ThrowIfNull(primaryOperation);
        ArgumentNullException.ThrowIfNull(fallbackOperation);
        
        try
        {
            return primaryOperation();
        }
        catch (Exception ex)
        {
            if (logFallback)
            {
                _logger.LogWarning(ex, "⚠️ [RESILIENCE] Primary operation {Operation} failed, using fallback: {Error}", 
                    operationName, ex.Message);
            }
            
            try
            {
                var result = fallbackOperation();
                _logger.LogInformation("🔄 [RESILIENCE] Fallback operation {Operation} succeeded", operationName);
                return result;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "❌ [RESILIENCE] Both primary and fallback operations failed for {Operation}", operationName);
                throw new InvalidOperationException($"Both primary and fallback operations failed for {operationName}", ex);
            }
        }
    }

    private CircuitBreakerState GetOrCreateCircuitBreaker(string operationName)
    {
        return _circuitBreakers.GetOrAdd(operationName, _ => new CircuitBreakerState());
    }

    private static bool IsRetriableException(Exception ex)
    {
        return ex switch
        {
            HttpRequestException httpEx => IsRetriableHttpError(httpEx),
            TaskCanceledException => true, // Timeout
            SocketException => true,
            TimeoutException => true,
            JsonException => false, // Data corruption, don't retry
            ArgumentException => false, // Bad input, don't retry
            UnauthorizedAccessException => false, // Auth issues, don't retry
            _ => false
        };
    }

    private static bool IsRetriableHttpError(HttpRequestException httpEx)
    {
        // Use HttpRequestException.Data or try to extract status code
        if (httpEx.Data.Contains("StatusCode") && httpEx.Data["StatusCode"] is HttpStatusCode statusCode)
        {
            return statusCode >= HttpStatusCode.InternalServerError || 
                   statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.TooManyRequests;
        }
        
        // Fallback to message parsing if status code not available
        return GetHttpStatusCodeFromMessage(httpEx);
    }

    private static bool GetHttpStatusCodeFromMessage(HttpRequestException httpEx)
    {
        // Extract status code from HTTP exception message if available
        var message = httpEx.Message;
        return message.Contains("500", StringComparison.Ordinal) || message.Contains("502", StringComparison.Ordinal) || message.Contains("503", StringComparison.Ordinal) || 
               message.Contains("504", StringComparison.Ordinal) || message.Contains("408", StringComparison.Ordinal) || message.Contains("429", StringComparison.Ordinal);
    }

    private TimeSpan CalculateExponentialBackoff(int attempt)
    {
        var baseDelay = TimeSpan.FromMilliseconds(_config.BaseRetryDelayMs);
        var maxDelay = TimeSpan.FromMilliseconds(_config.MaxRetryDelayMs);
        var exponentialDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitteredDelay = TimeSpan.FromMilliseconds(exponentialDelay.TotalMilliseconds * (0.8 + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10000) / 10000.0 * 0.4));
        
        return jitteredDelay > maxDelay ? maxDelay : jitteredDelay;
    }
}

#region Configuration and Data Models

// Note: ResilienceConfiguration is now defined in ProductionGuardrailConfiguration.cs

public class CircuitBreakerState
{
    public CircuitState State { get; set; } = CircuitState.Closed;
    public int FailureCount { get; set; }
    public DateTime LastFailure { get; set; }
}

public enum CircuitState
{
    Closed,   // Normal operation
    Open,     // Failing, don't try
    HalfOpen  // Testing if service is back
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }

    public CircuitBreakerOpenException()
    {
    }

    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

#endregion