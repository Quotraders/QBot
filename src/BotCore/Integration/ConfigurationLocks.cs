using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace BotCore.Integration;

/// <summary>
/// Configuration locks service to enforce required production settings
/// Ensures critical safety settings are active across all environments
/// </summary>
public sealed class ConfigurationLocks
{
    private readonly ILogger<ConfigurationLocks> _logger;
    private readonly IConfiguration _configuration;
    
    // Required configuration locks for production safety
    private readonly Dictionary<string, string> _requiredSettings = new()
    {
        ["ALLOW_TOPSTEP_LIVE"] = "0",
        ["LIVE_ORDERS"] = "0", 
        ["FUSION_HOLD_ON_DISAGREE"] = "1",
        ["ZONES_FAIL_CLOSED"] = "1",
        ["PATTERNS_FAIL_CLOSED"] = "1",
        ["PRODUCTION_RULE_ENFORCEMENT"] = "1",
        ["TREAT_WARNINGS_AS_ERRORS"] = "true"
    };
    
    public ConfigurationLocks(ILogger<ConfigurationLocks> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
    
    /// <summary>
    /// Validate and enforce required configuration locks
    /// </summary>
    public ConfigurationLockReport ValidateConfigurationLocks()
    {
        var report = new ConfigurationLockReport
        {
            ValidatedAt = DateTime.UtcNow,
            Settings = new Dictionary<string, ConfigurationSettingStatus>(),
            IsCompliant = true
        };
        
        _logger.LogInformation("Validating production configuration locks...");
        
        foreach (var requiredSetting in _requiredSettings)
        {
            var key = requiredSetting.Key;
            var expectedValue = requiredSetting.Value;
            var actualValue = _configuration[key] ?? GetEnvironmentVariable(key);
            
            var status = new ConfigurationSettingStatus
            {
                Key = key,
                ExpectedValue = expectedValue,
                ActualValue = actualValue ?? "NOT_SET",
                IsCompliant = string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase)
            };
            
            report.Settings[key] = status;
            
            if (!status.IsCompliant)
            {
                report.IsCompliant = false;
                _logger.LogError("Configuration lock violation: {Key} expected {Expected} but got {Actual}",
                    key, expectedValue, actualValue ?? "NOT_SET");
            }
            else
            {
                _logger.LogDebug("Configuration lock verified: {Key} = {Value}", key, actualValue);
            }
        }
        
        // Additional safety checks
        ValidateAdditionalSafetySettings(report);
        
        if (report.IsCompliant)
        {
            _logger.LogInformation("✅ All configuration locks validated successfully");
        }
        else
        {
            _logger.LogError("❌ Configuration lock validation failed - system may not be production ready");
        }
        
        return report;
    }
    
    /// <summary>
    /// Get environment variable value
    /// </summary>
    private static string? GetEnvironmentVariable(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }
    
    /// <summary>
    /// Validate additional safety settings
    /// </summary>
    private void ValidateAdditionalSafetySettings(ConfigurationLockReport report)
    {
        // Validate session gates are enabled
        ValidateSetting(report, "SESSION_GATES_ENABLED", "1", "Session gates must be enabled for production safety");
        
        // Validate egress guards are active
        ValidateSetting(report, "EGRESS_GUARD_ENABLED", "1", "Egress guards must be active to prevent unauthorized API calls");
        
        // Validate arming token requirement
        ValidateSetting(report, "REQUIRE_ARMING_TOKEN", "1", "Arming token must be required before enabling live flags");
        
        // Validate paper mode is active for safety
        ValidateSetting(report, "PAPER_MODE", "1", "Paper mode should be active for initial deployment");
        
        // Validate critical system components are enabled
        ValidateSetting(report, "CRITICAL_SYSTEM_ENABLE", "1", "Critical system components must be enabled");
        ValidateSetting(report, "EXECUTION_VERIFICATION_ENABLE", "1", "Execution verification must be enabled");
        ValidateSetting(report, "DISASTER_RECOVERY_ENABLE", "1", "Disaster recovery must be enabled");
        ValidateSetting(report, "CORRELATION_PROTECTION_ENABLE", "1", "Correlation protection must be enabled");
    }
    
    /// <summary>
    /// Validate individual setting with warning/error logging
    /// </summary>
    private void ValidateSetting(ConfigurationLockReport report, string key, string expectedValue, string description)
    {
        var actualValue = _configuration[key] ?? GetEnvironmentVariable(key);
        var isCompliant = string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        
        var status = new ConfigurationSettingStatus
        {
            Key = key,
            ExpectedValue = expectedValue,
            ActualValue = actualValue ?? "NOT_SET",
            IsCompliant = isCompliant,
            Description = description
        };
        
        report.Settings[key] = status;
        
        if (!isCompliant)
        {
            report.IsCompliant = false;
            _logger.LogWarning("Safety setting non-compliant: {Key} expected {Expected} but got {Actual} - {Description}",
                key, expectedValue, actualValue ?? "NOT_SET", description);
        }
    }
    
    /// <summary>
    /// Generate comprehensive configuration audit log
    /// </summary>
    public string GenerateConfigurationAudit()
    {
        var report = ValidateConfigurationLocks();
        var audit = new StringBuilder();
        
        audit.AppendLine("=== PRODUCTION CONFIGURATION LOCKS AUDIT ===");
        audit.AppendLine($"Validated: {report.ValidatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        audit.AppendLine($"Overall Compliance: {(report.IsCompliant ? "✅ COMPLIANT" : "❌ NON-COMPLIANT")}");
        audit.AppendLine($"Total Settings Checked: {report.Settings.Count}");
        audit.AppendLine();
        
        // Group by compliance status
        var compliantSettings = report.Settings.Values.Where(s => s.IsCompliant).ToList();
        var nonCompliantSettings = report.Settings.Values.Where(s => !s.IsCompliant).ToList();
        
        if (nonCompliantSettings.Any())
        {
            audit.AppendLine("❌ NON-COMPLIANT SETTINGS:");
            foreach (var setting in nonCompliantSettings)
            {
                audit.AppendLine($"  {setting.Key}:");
                audit.AppendLine($"    Expected: {setting.ExpectedValue}");
                audit.AppendLine($"    Actual: {setting.ActualValue}");
                if (!string.IsNullOrEmpty(setting.Description))
                {
                    audit.AppendLine($"    Description: {setting.Description}");
                }
            }
            audit.AppendLine();
        }
        
        audit.AppendLine("✅ COMPLIANT SETTINGS:");
        foreach (var setting in compliantSettings)
        {
            audit.AppendLine($"  {setting.Key}: {setting.ActualValue}");
        }
        
        return audit.ToString();
    }
    
    /// <summary>
    /// Force update configuration settings to required values (development/test only)
    /// </summary>
    public void EnforceConfigurationLocks()
    {
        _logger.LogWarning("Enforcing configuration locks - this should only be used in development/test environments");
        
        foreach (var requiredSetting in _requiredSettings)
        {
            var key = requiredSetting.Key;
            var value = requiredSetting.Value;
            
            // Set environment variable to enforce the lock
            Environment.SetEnvironmentVariable(key, value);
            _logger.LogInformation("Enforced configuration lock: {Key} = {Value}", key, value);
        }
    }
}

/// <summary>
/// Configuration lock validation report
/// </summary>
public sealed class ConfigurationLockReport
{
    public DateTime ValidatedAt { get; set; }
    public Dictionary<string, ConfigurationSettingStatus> Settings { get; init; } = new();
    public bool IsCompliant { get; set; }
}

/// <summary>
/// Individual configuration setting status
/// </summary>
public sealed class ConfigurationSettingStatus
{
    public string Key { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public bool IsCompliant { get; set; }
    public string Description { get; set; } = string.Empty;
}