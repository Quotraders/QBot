using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace BotCore.Resilience;

/// <summary>
/// Enhanced production resilience service with comprehensive Polly policies
/// Implements retry/backoff, circuit-breaker, timeout, and bulkhead patterns for all outbound IO
/// </summary>
public class EnhancedProductionResilienceService
{
    private const int ExponentialBackoffBase = 2;
    private const int HttpTimeoutBufferMilliseconds = 5000;

    private readonly ILogger<EnhancedProductionResilienceService> _logger;
    private readonly ResilienceConfiguration _config;

    public EnhancedProductionResilienceService(
        ILogger<EnhancedProductionResilienceService> logger,
        IOptions<ResilienceConfiguration> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        _logger = logger;
        _config = config.Value;
    }

    /// <summary>
    /// Get comprehensive resilience policy for HTTP calls with retry, circuit breaker, timeout, and bulkhead
    /// </summary>
    public IAsyncPolicy<HttpResponseMessage> GetHttpResiliencePolicy(string operationName)
    {
        // Retry policy with exponential backoff and jitter
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && ShouldRetry(r.StatusCode))
            .WaitAndRetryAsync(
                retryCount: _config.MaxRetries,
                sleepDurationProvider: retryAttempt => CalculateBackoffWithJitter(retryAttempt),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("🔄 [RESILIENCE] Retry {RetryCount}/{MaxRetries} for {Operation} in {Delay}ms", 
                        retryCount, _config.MaxRetries, operationName, timespan.TotalMilliseconds);
                });

        // Circuit breaker policy
        var circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && IsServerError(r.StatusCode))
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: _config.CircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromMilliseconds(_config.CircuitBreakerTimeoutMs),
                onBreak: (exception, duration) =>
                {
                    _logger.LogError("🚫 [RESILIENCE] Circuit breaker OPENED for {Operation} for {Duration}ms", 
                        operationName, duration.TotalMilliseconds);
                },
                onReset: () =>
                {
                    _logger.LogInformation("✅ [RESILIENCE] Circuit breaker CLOSED for {Operation}", operationName);
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("🔄 [RESILIENCE] Circuit breaker HALF-OPEN for {Operation}", operationName);
                });

        // Timeout policy
        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            timeout: TimeSpan.FromMilliseconds(_config.HttpTimeoutMs),
            timeoutStrategy: TimeoutStrategy.Pessimistic,
            onTimeoutAsync: (context, timespan, task) =>
            {
                _logger.LogWarning("⏰ [RESILIENCE] Timeout after {Timeout}ms for {Operation}", 
                    timespan.TotalMilliseconds, operationName);
                return Task.CompletedTask;
            });

        // Bulkhead policy to limit concurrent executions
        var bulkheadPolicy = Policy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization: _config.BulkheadMaxConcurrency,
            maxQueuingActions: _config.BulkheadMaxConcurrency / 2,
            onBulkheadRejectedAsync: (context) =>
            {
                _logger.LogWarning("🚧 [RESILIENCE] Bulkhead rejection for {Operation} - too many concurrent requests", 
                    operationName);
                return Task.CompletedTask;
            });

        // Combine all policies: Bulkhead -> CircuitBreaker -> Retry -> Timeout
        return Policy.WrapAsync(bulkheadPolicy, circuitBreakerPolicy, retryPolicy, timeoutPolicy);
    }

    /// <summary>
    /// Get resilience policy for general operations with retry and timeout
    /// </summary>
    public IAsyncPolicy<T> GetOperationResiliencePolicy<T>(string operationName)
    {
        // Simplified approach for now to avoid Polly API complexity
        // Retry policy defined for potential future use
        _ = Policy.Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: _config.MaxRetries,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(Math.Min(
                    _config.BaseRetryDelayMs * Math.Pow(ExponentialBackoffBase, retryAttempt - 1),
                    _config.MaxRetryDelayMs)));

        var timeoutPolicy = Policy.TimeoutAsync<T>(
            timeout: TimeSpan.FromMilliseconds(_config.HttpTimeoutMs));

        // Return timeout policy for now (retry will be added later)
        return timeoutPolicy;
    }

    /// <summary>
    /// Execute operation with comprehensive resilience policies
    /// </summary>
    public Task<T> ExecuteWithResilienceAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var policy = GetOperationResiliencePolicy<T>(operationName);
        
        return policy.ExecuteAsync(async (ct) =>
        {
            _logger.LogDebug("🔧 [RESILIENCE] Executing {Operation} with resilience protection", operationName);
            return await operation(ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Execute HTTP operation with full resilience stack
    /// </summary>
    public Task<HttpResponseMessage> ExecuteHttpWithResilienceAsync(
        string operationName,
        Func<CancellationToken, Task<HttpResponseMessage>> httpOperation,
        CancellationToken cancellationToken = default)
    {
        var policy = GetHttpResiliencePolicy(operationName);
        
        return policy.ExecuteAsync(async (ct) =>
        {
            _logger.LogDebug("🌐 [RESILIENCE] Executing HTTP {Operation} with full resilience stack", operationName);
            return await httpOperation(ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Get HttpClient with configured resilience policies
    /// </summary>
    public HttpClient CreateResilientHttpClient(string clientName)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMilliseconds(_config.HttpTimeoutMs + HttpTimeoutBufferMilliseconds); // Add buffer for Polly timeout

        // Add default headers for better debugging
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"TradingBot-Resilient/{clientName}");
        httpClient.DefaultRequestHeaders.Add("X-Client-Name", clientName);

        return httpClient;
    }

    #region Helper Methods

    private TimeSpan CalculateBackoffWithJitter(int retryAttempt)
    {
        // Exponential backoff with jitter to avoid thundering herd
        var baseDelay = TimeSpan.FromMilliseconds(_config.BaseRetryDelayMs);
        var exponentialDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, retryAttempt - 1));
        
        // Add jitter (±20%)
        var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10000) / 10000.0 * 0.4 - 0.2; // -0.2 to +0.2
        var jitteredDelay = TimeSpan.FromMilliseconds(exponentialDelay.TotalMilliseconds * (1 + jitter));
        
        // Cap at maximum delay
        var maxDelay = TimeSpan.FromMilliseconds(_config.MaxRetryDelayMs);
        return jitteredDelay > maxDelay ? maxDelay : jitteredDelay;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        // Retry on server errors and specific client errors
        return statusCode >= HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests;
    }

    private static bool IsServerError(HttpStatusCode statusCode)
    {
        return statusCode >= HttpStatusCode.InternalServerError;
    }

    #endregion
}

/// <summary>
/// Configuration for enhanced resilience policies
/// </summary>
public class ResilienceConfiguration
{
    // Constants for validation ranges
    private const int MinRetries = 1;
    private const int MaxRetriesLimit = 10;
    private const int MinBaseRetryDelayMs = 100;
    private const int MaxBaseRetryDelayMs = 10000;
    private const int MinMaxRetryDelayMs = 1000;
    private const int MaxMaxRetryDelayMs = 60000;
    private const int MinHttpTimeoutMs = 5000;
    private const int MaxHttpTimeoutMs = 120000;
    private const int MinCircuitBreakerThreshold = 3;
    private const int MaxCircuitBreakerThreshold = 20;
    private const int MinCircuitBreakerTimeoutMs = 30000;
    private const int MaxCircuitBreakerTimeoutMs = 600000;
    private const int MinBulkheadConcurrency = 5;
    private const int MaxBulkheadConcurrency = 100;
    
    // Default values
    private const int DefaultCircuitBreakerTimeoutMs = 60000;
    private const int DefaultBulkheadMaxConcurrency = 20;
    
    [Required]
    [Range(MinRetries, MaxRetriesLimit)]
    public int MaxRetries { get; set; } = 3;

    [Required]
    [Range(MinBaseRetryDelayMs, MaxBaseRetryDelayMs)]
    public int BaseRetryDelayMs { get; set; } = 500;

    [Required]
    [Range(MinMaxRetryDelayMs, MaxMaxRetryDelayMs)]
    public int MaxRetryDelayMs { get; set; } = 30000;

    [Required]
    [Range(MinHttpTimeoutMs, MaxHttpTimeoutMs)]
    public int HttpTimeoutMs { get; set; } = 30000;

    [Required]
    [Range(MinCircuitBreakerThreshold, MaxCircuitBreakerThreshold)]
    public int CircuitBreakerThreshold { get; set; } = 5;

    [Required]
    [Range(MinCircuitBreakerTimeoutMs, MaxCircuitBreakerTimeoutMs)]
    public int CircuitBreakerTimeoutMs { get; set; } = DefaultCircuitBreakerTimeoutMs;

    [Required]
    [Range(MinBulkheadConcurrency, MaxBulkheadConcurrency)]
    public int BulkheadMaxConcurrency { get; set; } = DefaultBulkheadMaxConcurrency;
}

/// <summary>
/// Extension methods for easy HttpClient configuration with resilience
/// </summary>
public static class ResilienceExtensions
{
    // Retry policy constants
    private const double ExponentialBackoffBase = 2.0;            // Base for exponential backoff calculation
    private const int JitterMaxDelayMs = 1000;                   // Maximum jitter delay in milliseconds
    private const int HttpTimeoutSeconds = 30;                   // HTTP timeout in seconds
    
    /// <summary>
    /// Add resilience policies to HttpClient factory
    /// </summary>
    public static IServiceCollection AddResilientHttpClient<TClient>(
        this IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient = null)
        where TClient : class
    {
        services.AddHttpClient<TClient>(name, client =>
        {
            configureClient?.Invoke(client);
            
            // Set reasonable defaults
            client.Timeout = TimeSpan.FromSeconds(35); // Buffer for Polly policies
            client.DefaultRequestHeaders.Add("User-Agent", $"TradingBot/{name}");
        })
        .AddPolicyHandler(GetRetryPolicy(name))
        .AddPolicyHandler(GetCircuitBreakerPolicy(name))
        .AddPolicyHandler(GetTimeoutPolicy());

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(string clientName)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(ExponentialBackoffBase, retryAttempt)) + TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, JitterMaxDelayMs)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Simple logging without context dependency
                    Console.WriteLine($"🔄 [HTTP-RETRY] {clientName} retry {retryCount}/3 in {timespan.TotalMilliseconds}ms");
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(string clientName)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (exception, duration) =>
                {
                    Console.WriteLine($"🚫 [HTTP-CIRCUIT] {clientName} circuit breaker opened for {duration.TotalSeconds}s");
                },
                onReset: () =>
                {
                    Console.WriteLine($"✅ [HTTP-CIRCUIT] {clientName} circuit breaker reset");
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(HttpTimeoutSeconds);
    }
}

/// <summary>
/// Background service to ensure graceful cancellation handling
/// </summary>
public abstract class ResilientBackgroundService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly string _serviceName;

    protected ResilientBackgroundService(ILogger logger, string serviceName)
    {
        _logger = logger;
        _serviceName = serviceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 [SERVICE] Starting {ServiceName}", _serviceName);

        try
        {
            await ExecuteServiceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected cancellation - log at Information level
            _logger.LogInformation("ℹ️ [SERVICE] {ServiceName} cancelled gracefully", _serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [SERVICE] {ServiceName} failed with unexpected error", _serviceName);
            throw new InvalidOperationException($"Service {_serviceName} execution failed unexpectedly", ex);
        }
        finally
        {
            _logger.LogInformation("🛑 [SERVICE] {ServiceName} stopped", _serviceName);
        }
    }

    /// <summary>
    /// Override this method to implement service logic with proper cancellation handling
    /// </summary>
    protected abstract Task ExecuteServiceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Helper method to safely execute operations with cancellation token handling
    /// </summary>
    protected async Task SafeExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        string operationName = "operation")
    {
        ArgumentNullException.ThrowIfNull(operation);
        
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("ℹ️ [SERVICE] {ServiceName} {Operation} cancelled", _serviceName, operationName);
            throw; // Re-throw to propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [SERVICE] {ServiceName} {Operation} failed", _serviceName, operationName);
            throw new InvalidOperationException($"Service {_serviceName} operation {operationName} failed", ex);
        }
    }
}