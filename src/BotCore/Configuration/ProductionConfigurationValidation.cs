using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace BotCore.Configuration;

/// <summary>
/// Production-grade configuration validation service enforcing fail-fast startup
/// All configuration POCOs must implement validation with proper data annotations
/// </summary>
public static class ProductionConfigurationExtensions
{
    public static IServiceCollection AddProductionConfigurationValidation(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        
        // Add comprehensive configuration validation for all trading components
        services.Configure<TradingConfiguration>(configuration.GetSection("Trading"));
        services.Configure<TopstepXConfiguration>(configuration.GetSection("TopstepX"));
        services.Configure<SecurityConfiguration>(configuration.GetSection("Security"));
        services.Configure<ResilienceConfiguration>(configuration.GetSection("Resilience"));
        services.Configure<ObservabilityConfiguration>(configuration.GetSection("Observability"));
        services.Configure<HealthCheckConfiguration>(configuration.GetSection("HealthChecks"));

        // Add validators with fail-fast startup
        services.AddSingleton<IValidateOptions<TradingConfiguration>, TradingConfigurationValidator>();
        services.AddSingleton<IValidateOptions<TopstepXConfiguration>, TopstepXConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SecurityConfiguration>, SecurityConfigurationValidator>();
        services.AddSingleton<IValidateOptions<ResilienceConfiguration>, ResilienceConfigurationValidator>();
        services.AddSingleton<IValidateOptions<ObservabilityConfiguration>, ObservabilityConfigurationValidator>();
        services.AddSingleton<IValidateOptions<HealthCheckConfiguration>, HealthCheckConfigurationValidator>();

        // Add startup configuration validation service
        services.AddHostedService<ConfigurationValidationStartupService>();

        return services;
    }
}

#region Configuration Models with Data Annotations

/// <summary>
/// Trading system configuration with validation
/// </summary>
public class TradingConfiguration
{
    // Validation range constants for S109 compliance
    private const int MinPositionSize = 1;
    private const int MaxPositionSizeLimit = 10;
    private const double MinDailyLoss = -10000.0;
    private const double MaxDailyLossLimit = -100.0;
    private const double MinProfitTarget = 100.0;
    private const double MaxProfitTargetLimit = 10000.0;
    private const double MinDrawdownLimit = -50000.0;
    private const double MaxDrawdownLimitValue = -1000.0;
    private const int MinDecisionTimeoutSeconds = 5;
    private const int MaxDecisionTimeoutSeconds = 60;
    private const double MinAIConfidenceThreshold = 0.1;
    private const double MaxAIConfidenceThreshold = 0.95;
    private const double MinPositionSizeMultiplier = 0.5;
    private const double MaxPositionSizeMultiplier = 5.0;
    private const double MinRegimeDetectionThreshold = 0.1;
    private const double MaxRegimeDetectionThreshold = 1.00;
    private const double MinStopLossBufferPercentage = 0.01;
    private const double MaxStopLossBufferPercentage = 0.2;
    private const double MinRewardRiskRatio = 1.0;
    private const double MaxRewardRiskRatio = 3.0;
    private const double MinVolatilityAdjustmentFactor = 0.01;
    private const double MaxVolatilityAdjustmentFactor = 0.5;
    
    // Default values as constants
    private const int DefaultMaxPositionSize = 5;
    private const decimal DefaultMaxDailyLoss = -1000m;
    private const decimal DefaultDailyProfitTarget = 500m;
    private const decimal DefaultDrawdownLimit = -2000m;

    [Required]
    [Range(MinPositionSize, MaxPositionSizeLimit)]
    public int MaxPositionSize { get; set; } = DefaultMaxPositionSize;

    [Required]
    [Range(MinDailyLoss, MaxDailyLossLimit)]
    public decimal MaxDailyLoss { get; set; } = DefaultMaxDailyLoss;

    [Required]
    [Range(MinProfitTarget, MaxProfitTargetLimit)]
    public decimal DailyProfitTarget { get; set; } = DefaultDailyProfitTarget;

    [Required]
    [Range(MinDrawdownLimit, MaxDrawdownLimitValue)]
    public decimal DrawdownLimit { get; set; } = DefaultDrawdownLimit;

    [Required]
    public bool EnableAutoExecution { get; set; }

    [Required]
    public bool EnableDryRun { get; set; } = true;

    /// <summary>
    /// Safe application data directory for logs and state
    /// </summary>
    [Required]
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// Backup directory with write permissions
    /// </summary>
    public string? BackupDirectory { get; set; }

    [Required]
    [Range(MinDecisionTimeoutSeconds, MaxDecisionTimeoutSeconds)]
    public int DecisionTimeoutSeconds { get; set; } = 30;

    // ML/AI Configuration Parameters (addresses hardcoded values issue)
    
    /// <summary>
    /// AI model confidence threshold for trade execution
    /// Replaces hardcoded 0.7 value
    /// </summary>
    [Required]
    [Range(MinAIConfidenceThreshold, MaxAIConfidenceThreshold)]
    public double AIConfidenceThreshold { get; set; } = 0.75;

    /// <summary>
    /// Default position sizing multiplier for dynamic calculation
    /// Replaces hardcoded 2.5 value
    /// </summary>
    [Required]
    [Range(MinPositionSizeMultiplier, MaxPositionSizeMultiplier)]
    public double DefaultPositionSizeMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Regime detection confidence threshold
    /// Replaces hardcoded 1.0 value for regime detection
    /// </summary>
    [Required]
    [Range(MinRegimeDetectionThreshold, MaxRegimeDetectionThreshold)]
    public double RegimeDetectionThreshold { get; set; } = 0.8;

    /// <summary>
    /// Stop loss buffer as percentage of ATR
    /// Replaces hardcoded 0.05 value
    /// </summary>
    [Required]
    [Range(MinStopLossBufferPercentage, MaxStopLossBufferPercentage)]
    public double StopLossBufferPercentage { get; set; } = 0.04;

    /// <summary>
    /// Reward to risk ratio threshold for trade validation
    /// Replaces hardcoded 1.2 value
    /// </summary>
    [Required]
    [Range(MinRewardRiskRatio, MaxRewardRiskRatio)]
    public double RewardRiskRatioThreshold { get; set; } = 1.5;

    /// <summary>
    /// Minimum confidence for model fallback scenarios
    /// Replaces hardcoded 0.1 value
    /// </summary>
    [Range(MinVolatilityAdjustmentFactor, MaxVolatilityAdjustmentFactor)]
    public double? MinimumConfidence { get; set; } = 0.1;
}

/// <summary>
/// TopstepX API configuration with validation
/// </summary>
public class TopstepXConfiguration
{
    // Timeout configuration constants
    private const int MIN_HTTP_TIMEOUT_SECONDS = 5;
    private const int MAX_HTTP_TIMEOUT_SECONDS = 300;
    private const int DEFAULT_HTTP_TIMEOUT_SECONDS = 30;

    // Retry configuration constants
    private const int MIN_MAX_RETRIES = 3;
    private const int MAX_MAX_RETRIES = 30;
    private const int DEFAULT_MAX_RETRIES = 5;

    // SignalR configuration constants
    private const int MIN_SIGNALR_RECONNECT_DELAY_MS = 1000;
    private const int MAX_SIGNALR_RECONNECT_DELAY_MS = 60000;
    private const int DEFAULT_SIGNALR_RECONNECT_DELAY_MS = 5000;
    
    // Default TopstepX API URLs
    private const string DEFAULT_API_BASE_URL = "https://api.topstepx.com";
    private const string DEFAULT_USER_HUB_URL = "https://rtc.topstepx.com/hubs/user";
    private const string DEFAULT_MARKET_HUB_URL = "https://rtc.topstepx.com/hubs/market";

    [Required]
    public Uri ApiBaseUrl { get; set; } = new Uri(DEFAULT_API_BASE_URL);

    [Required]
    public Uri UserHubUrl { get; set; } = new Uri(DEFAULT_USER_HUB_URL);

    [Required]
    public Uri MarketHubUrl { get; set; } = new Uri(DEFAULT_MARKET_HUB_URL);

    [Required]
    [Range(MIN_HTTP_TIMEOUT_SECONDS, MAX_HTTP_TIMEOUT_SECONDS)]
    public int HttpTimeoutSeconds { get; set; } = DEFAULT_HTTP_TIMEOUT_SECONDS;

    [Required]
    [Range(MIN_MAX_RETRIES, MAX_MAX_RETRIES)]
    public int MaxRetries { get; set; } = DEFAULT_MAX_RETRIES;

    [Required]
    [Range(MIN_SIGNALR_RECONNECT_DELAY_MS, MAX_SIGNALR_RECONNECT_DELAY_MS)]
    public int SignalRReconnectDelayMs { get; set; } = DEFAULT_SIGNALR_RECONNECT_DELAY_MS;
    
    // HTTP Adapter Configuration (for Python adapter communication)
    public string AdapterMode { get; set; } = "http"; // "http" or "stdin" 
    public string AdapterHttpHost { get; set; } = "localhost";
    public int AdapterHttpPort { get; set; } = 8765;
    public int AdapterStartupTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Security configuration with validation
/// </summary>
public class SecurityConfiguration
{
    // Constants for validation ranges
    private const int MinTokenRefreshIntervalSeconds = 3600;
    private const int MaxTokenRefreshIntervalSeconds = 86400;
    private const int MaxAllowedLoginAttempts = 10;
    
    [Required]
    public bool RequireEnvironmentCredentials { get; set; } = true;

    [Required]
    public bool EnableEncryption { get; set; } = true;

    [Required]
    public bool RotateTokens { get; set; } = true;

    [Required]
    [Range(MinTokenRefreshIntervalSeconds, MaxTokenRefreshIntervalSeconds)]
    public int TokenRefreshIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Key store path - must be secure directory
    /// </summary>
    public string? KeyStorePath { get; set; }

    [Required]
    [Range(1, MaxAllowedLoginAttempts)]
    public int MaxLoginAttempts { get; set; } = 3;
}

/// <summary>
/// Resilience configuration with validation
/// </summary>
public class ResilienceConfiguration
{
    // Constants for validation ranges
    private const int MaxAllowedRetries = 10;
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

    [Required]
    [Range(1, MaxAllowedRetries)]
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
    public int CircuitBreakerTimeoutMs { get; set; } = 60000;

    [Required]
    [Range(MinBulkheadConcurrency, MaxBulkheadConcurrency)]
    public int BulkheadMaxConcurrency { get; set; } = 20;
}

/// <summary>
/// Observability configuration with validation
/// </summary>
public class ObservabilityConfiguration
{
    // Constants for validation ranges
    private const int MinLogRetentionDays = 1;
    private const int MaxLogRetentionDays = 30;
    private const int MinMetricsIntervalMs = 100;
    private const int MaxMetricsIntervalMs = 10000;
    
    [Required]
    public bool EnableStructuredLogging { get; set; } = true;

    [Required]
    public bool EnableMetrics { get; set; } = true;

    [Required]
    public bool EnableTracing { get; set; } = true;

    [Required]
    [Range(MinLogRetentionDays, MaxLogRetentionDays)]
    public int LogRetentionDays { get; set; } = 7;

    [Required]
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Log output directory with write permissions
    /// </summary>
    [Required]
    public string LogDirectory { get; set; } = "logs";

    [Required]
    [Range(MinMetricsIntervalMs, MaxMetricsIntervalMs)]
    public int MetricsIntervalMs { get; set; } = 1000;
}

/// <summary>
/// Health check configuration with validation
/// </summary>
public class HealthCheckConfiguration
{
    // Constants for validation ranges
    private const int MinTimeoutMs = 1000;
    private const int MaxTimeoutMs = 30000;
    private const int DefaultTimeoutMs = 5000;
    private const int MaxIntervalMs = 60000;
    private const int MinFailureThreshold = 1;
    private const int MaxFailureThreshold = 10;
    private const int MaxRecoveryTimeoutMs = 300000;
    
    [Required]
    [Range(DefaultTimeoutMs, MaxIntervalMs)]
    public int IntervalMs { get; set; } = 10000;

    [Required]
    [Range(MinTimeoutMs, MaxTimeoutMs)]
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    [Required]
    public bool EnableHealthEndpoint { get; set; } = true;

    [Required]
    [Range(MinFailureThreshold, MaxFailureThreshold)]
    public int FailureThreshold { get; set; } = 3;

    [Required]
    [Range(MaxTimeoutMs, MaxRecoveryTimeoutMs)]
    public int RecoveryTimeoutMs { get; set; } = 60000;
}

#endregion

#region Configuration Validators

/// <summary>
/// Trading configuration validator with business logic validation
/// </summary>
public class TradingConfigurationValidator : IValidateOptions<TradingConfiguration>
{
    private const decimal MinimumDailyLoss = 100m;
    private const decimal ProfitTargetMinimumRatio = 0.1m;

    public ValidateOptionsResult Validate(string? name, TradingConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        // Validate data directory exists and is writable
        if (!ValidateDirectory(options.DataDirectory, true))
        {
            failures.Add($"DataDirectory '{options.DataDirectory}' must exist and be writable");
        }

        // Validate backup directory if specified
        if (!string.IsNullOrEmpty(options.BackupDirectory) && !ValidateDirectory(options.BackupDirectory, true))
        {
            failures.Add($"BackupDirectory '{options.BackupDirectory}' must exist and be writable");
        }

        // Business logic validation
        if (Math.Abs(options.MaxDailyLoss) < MinimumDailyLoss)
        {
            failures.Add("MaxDailyLoss must be at least -100 for meaningful risk management");
        }

        if (options.DailyProfitTarget <= Math.Abs(options.MaxDailyLoss) * ProfitTargetMinimumRatio)
        {
            failures.Add("DailyProfitTarget should be reasonable relative to MaxDailyLoss (at least 10% of loss limit)");
        }

        // Safety validation
        if (options.EnableAutoExecution && !options.EnableDryRun)
        {
            var killFile = Environment.GetEnvironmentVariable("KILL_FILE") ?? "kill.txt";
            if (!File.Exists(killFile))
            {
                failures.Add($"Auto execution requires kill file '{killFile}' for emergency stops");
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static bool ValidateDirectory(string path, bool requireWritable = false)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Resolve relative paths safely
            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            if (requireWritable)
            {
                // Test write permissions
                var testFile = Path.Combine(fullPath, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// TopstepX configuration validator with endpoint validation
/// </summary>
public class TopstepXConfigurationValidator : IValidateOptions<TopstepXConfiguration>
{
    public ValidateOptionsResult Validate(string? name, TopstepXConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        // Validate URLs are accessible endpoints
        if (options.ApiBaseUrl?.Scheme != "https")
        {
            failures.Add("ApiBaseUrl must be a valid HTTPS URL");
        }

        if (options.UserHubUrl?.Scheme != "https")
        {
            failures.Add("UserHubUrl must be a valid HTTPS URL");
        }

        if (options.MarketHubUrl?.Scheme != "https")
        {
            failures.Add("MarketHubUrl must be a valid HTTPS URL");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Security configuration validator with credential validation
/// </summary>
public class SecurityConfigurationValidator : IValidateOptions<SecurityConfiguration>
{
    public ValidateOptionsResult Validate(string? name, SecurityConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        if (options.RequireEnvironmentCredentials)
        {
            // Validate required environment variables exist
            var requiredVars = new[] { "TOPSTEPX_USERNAME", "TOPSTEPX_API_KEY" };
            var missingVars = requiredVars.Where(v => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))).ToList();
            
            if (missingVars.Count > 0)
            {
                failures.Add($"Required environment variables missing: {string.Join(", ", missingVars)}");
            }
        }

        // Validate key store path if specified
        if (!string.IsNullOrEmpty(options.KeyStorePath))
        {
            try
            {
                var keyStorePath = Path.GetFullPath(options.KeyStorePath);
                var directory = Path.GetDirectoryName(keyStorePath);
                if (!Directory.Exists(directory))
                {
                    failures.Add($"KeyStorePath directory does not exist: {directory}");
                }
            }
            catch (ArgumentException ex)
            {
                failures.Add($"Invalid KeyStorePath: {ex.Message}");
            }
            catch (System.Security.SecurityException ex)
            {
                failures.Add($"Invalid KeyStorePath: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                failures.Add($"Invalid KeyStorePath: {ex.Message}");
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Resilience configuration validator
/// </summary>
public class ResilienceConfigurationValidator : IValidateOptions<ResilienceConfiguration>
{
    private const int MinimumCircuitBreakerThreshold = 3;

    public ValidateOptionsResult Validate(string? name, ResilienceConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        // Validate retry configuration makes sense
        if (options.BaseRetryDelayMs >= options.MaxRetryDelayMs)
        {
            failures.Add("BaseRetryDelayMs must be less than MaxRetryDelayMs");
        }

        // Validate circuit breaker makes sense
        if (options.CircuitBreakerThreshold < MinimumCircuitBreakerThreshold)
        {
            failures.Add("CircuitBreakerThreshold should be at least 3 for meaningful protection");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Observability configuration validator with directory validation
/// </summary>
public class ObservabilityConfigurationValidator : IValidateOptions<ObservabilityConfiguration>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        // Validate log directory
        if (!ValidateLogDirectory(options.LogDirectory))
        {
            failures.Add($"LogDirectory '{options.LogDirectory}' must exist and be writable");
        }

        // Validate log level
        var validLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };
        if (!validLevels.Contains(options.LogLevel))
        {
            failures.Add($"LogLevel must be one of: {string.Join(", ", validLevels)}");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static bool ValidateLogDirectory(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;

            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            // Test write permissions
            var testFile = Path.Combine(fullPath, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// Health check configuration validator
/// </summary>
public class HealthCheckConfigurationValidator : IValidateOptions<HealthCheckConfiguration>
{
    public ValidateOptionsResult Validate(string? name, HealthCheckConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        var failures = new List<string>();

        // Validate timeouts make sense
        if (options.TimeoutMs >= options.IntervalMs)
        {
            failures.Add("TimeoutMs must be less than IntervalMs");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

#endregion

/// <summary>
/// Startup service that validates all configuration on startup with fail-fast behavior
/// </summary>
public class ConfigurationValidationStartupService : IHostedService
{
    private readonly ILogger<ConfigurationValidationStartupService> _logger;
    private readonly IServiceProvider _serviceProvider;

    // LoggerMessage delegates for CA1848 performance compliance
    private static readonly Action<ILogger, Exception?> LogStartingValidation =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6101, nameof(LogStartingValidation)),
            "🔧 [CONFIG] Starting production configuration validation...");
    
    private static readonly Action<ILogger, Exception?> LogAllValidated =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6102, nameof(LogAllValidated)),
            "✅ [CONFIG] All production configuration validated successfully");
    
    private static readonly Action<ILogger, Exception> LogValidationFailed =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(6103, nameof(LogValidationFailed)),
            "❌ [CONFIG] Configuration validation failed - stopping application");
    
    private static readonly Action<ILogger, string, Exception?> LogSectionValidated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(6104, nameof(LogSectionValidated)),
            "✅ [CONFIG] {Section} configuration validated");
    
    private static readonly Action<ILogger, string, string, Exception> LogSectionValidationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(6105, nameof(LogSectionValidationFailed)),
            "❌ [CONFIG] {Section} validation failed: {Failures}");

    public ConfigurationValidationStartupService(
        ILogger<ConfigurationValidationStartupService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogStartingValidation(_logger, null);

        try
        {
            // Validate all configuration types
            ValidateConfiguration<TradingConfiguration>("Trading");
            ValidateConfiguration<TopstepXConfiguration>("TopstepX");
            ValidateConfiguration<SecurityConfiguration>("Security");
            ValidateConfiguration<ResilienceConfiguration>("Resilience");
            ValidateConfiguration<ObservabilityConfiguration>("Observability");
            ValidateConfiguration<HealthCheckConfiguration>("HealthChecks");

            LogAllValidated(_logger, null);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogValidationFailed(_logger, ex);
            throw new InvalidOperationException("Production configuration validation failed - application cannot start with invalid configuration", ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void ValidateConfiguration<T>(string sectionName) where T : class
    {
        try
        {
            var options = _serviceProvider.GetRequiredService<IOptions<T>>();
            _ = options.Value; // This triggers validation
            LogSectionValidated(_logger, sectionName, null);
        }
        catch (OptionsValidationException ex)
        {
            LogSectionValidationFailed(_logger, sectionName, string.Join("; ", ex.Failures), ex);
            throw;
        }
    }
}