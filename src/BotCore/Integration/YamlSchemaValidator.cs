using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BotCore.Integration;

/// <summary>
/// YAML schema validator for strategy/pattern YAML files
/// Validates DSL cards against schema, disables invalid cards, logs violations
/// Integrates with CI pipeline to fail builds on invalid strategy definitions
/// </summary>
public sealed class YamlSchemaValidator
{
    // Validation constraints
    private const int MaxPriority = 100;
    private const int MaxTimeoutMs = 60000;
    private const int MaxLookbackBars = 500;
    private const int MaxConfirmationBars = 10;
    
    private readonly ILogger<YamlSchemaValidator> _logger;
    private readonly IDeserializer _yamlDeserializer;
    
    // Schema definitions for strategy YAML validation
    private readonly Dictionary<string, YamlSchemaDefinition> _schemas = new();
    
    public YamlSchemaValidator(ILogger<YamlSchemaValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
            
        InitializeSchemaDefinitions();
    }
    
    /// <summary>
    /// Initialize schema definitions for all supported YAML types
    /// </summary>
    private void InitializeSchemaDefinitions()
    {
        // Strategy card schema
        _schemas["strategy"] = new YamlSchemaDefinition
        {
            Name = "Strategy Card Schema",
            RequiredFields = new[]
            {
                "name", "enabled", "when", "then"
            },
            OptionalFields = new[]
            {
                "shadow", "description", "priority", "timeout_ms", "metadata"
            },
            FieldValidators = new Dictionary<string, Func<object, bool>>
            {
                ["name"] = value => value is string name && !string.IsNullOrWhiteSpace(name),
                ["enabled"] = value => value is bool,
                ["shadow"] = value => value is bool,
                ["priority"] = value => value is int priority && priority >= 0 && priority <= MaxPriority,
                ["timeout_ms"] = value => value is int timeout && timeout > 0 && timeout <= MaxTimeoutMs,
                ["when"] = ValidateWhenClause,
                ["then"] = ValidateThenClause
            }
        };
        
        // Pattern definition schema
        _schemas["pattern"] = new YamlSchemaDefinition
        {
            Name = "Pattern Definition Schema",
            RequiredFields = new[]
            {
                "name", "type", "conditions"
            },
            OptionalFields = new[]
            {
                "enabled", "reliability", "lookback_bars", "confirmation_bars"
            },
            FieldValidators = new Dictionary<string, Func<object, bool>>
            {
                ["name"] = value => value is string name && !string.IsNullOrWhiteSpace(name),
                ["type"] = value => value is string type && IsValidPatternType(type),
                ["enabled"] = value => value is bool,
                ["reliability"] = value => value is double rel && rel >= 0.0 && rel <= 1.0,
                ["lookback_bars"] = value => value is int bars && bars > 0 && bars <= MaxLookbackBars,
                ["confirmation_bars"] = value => value is int bars && bars >= 0 && bars <= MaxConfirmationBars,
                ["conditions"] = ValidatePatternConditions
            }
        };
        
        _logger.LogInformation("YAML schema definitions initialized with {SchemaCount} schemas", _schemas.Count);
    }
    
    /// <summary>
    /// Validate a YAML file against the appropriate schema
    /// </summary>
    public async Task<YamlValidationResult> ValidateYamlFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            
        var result = new YamlValidationResult
        {
            FilePath = filePath,
            ValidationStarted = DateTime.UtcNow,
            IsValid = false,
            Errors = new List<string>(),
            Warnings = new List<string>()
        };
        
        try
        {
            if (!File.Exists(filePath))
            {
                result.Errors.Add($"File not found: {filePath}");
                return result;
            }
            
            var yamlContent = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(yamlContent))
            {
                result.Errors.Add("YAML file is empty");
                return result;
            }
            
            // Determine schema type from file name/path
            var schemaType = DetermineSchemaType(filePath);
            if (schemaType == null)
            {
                result.Errors.Add($"Cannot determine schema type for file: {filePath}");
                return result;
            }
            
            if (!_schemas.TryGetValue(schemaType, out var schema))
            {
                result.Errors.Add($"No schema definition found for type: {schemaType}");
                return result;
            }
            
            // Parse YAML content
            object? yamlObject;
            try
            {
                yamlObject = _yamlDeserializer.Deserialize(yamlContent);
                if (yamlObject == null)
                {
                    result.Errors.Add("YAML parsing error: Deserializer returned null");
                    return result;
                }
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                result.Errors.Add($"YAML parsing error: {ex.Message}");
                return result;
            }
            catch (InvalidOperationException ex)
            {
                result.Errors.Add($"YAML deserialization error: {ex.Message}");
                return result;
            }
            
            // Validate against schema
            await ValidateAgainstSchemaAsync(yamlObject, schema, result).ConfigureAwait(false);
            
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            
            if (result.IsValid)
            {
                _logger.LogDebug("YAML validation successful: {FilePath}", filePath);
            }
            else
            {
                _logger.LogError("YAML validation failed: {FilePath} - {ErrorCount} errors, {WarningCount} warnings",
                    filePath, result.Errors.Count, result.Warnings.Count);
            }
            
            return result;
        }
        catch (IOException ex)
        {
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            result.Errors.Add($"IO error: {ex.Message}");
            
            _logger.LogError(ex, "IO error validating YAML file: {FilePath}", filePath);
            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            result.Errors.Add($"Access denied: {ex.Message}");
            
            _logger.LogError(ex, "Access denied validating YAML file: {FilePath}", filePath);
            return result;
        }
    }
    
    /// <summary>
    /// Validate all YAML files in a directory
    /// </summary>
    public async Task<YamlDirectoryValidationResult> ValidateDirectoryAsync(string directoryPath, string searchPattern = "*.yaml")
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
            
        var result = new YamlDirectoryValidationResult
        {
            DirectoryPath = directoryPath,
            SearchPattern = searchPattern,
            ValidationStarted = DateTime.UtcNow,
            FileResults = new List<YamlValidationResult>()
        };
        
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                result.DirectoryError = $"Directory not found: {directoryPath}";
                return result;
            }
            
            var yamlFiles = Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories);
            _logger.LogInformation("Found {FileCount} YAML files in {DirectoryPath}", yamlFiles.Length, directoryPath);
            
            foreach (var filePath in yamlFiles)
            {
                var fileResult = await ValidateYamlFileAsync(filePath).ConfigureAwait(false);
                result.FileResults.Add(fileResult);
            }
            
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            result.TotalFiles = result.FileResults.Count;
            result.ValidFiles = result.FileResults.Count(r => r.IsValid);
            result.InvalidFiles = result.TotalFiles - result.ValidFiles;
            result.IsAllValid = result.InvalidFiles == 0;
            
            _logger.LogInformation("Directory validation completed: {ValidFiles}/{TotalFiles} valid files in {DirectoryPath}",
                result.ValidFiles, result.TotalFiles, directoryPath);
                
            return result;
        }
        catch (IOException ex)
        {
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            result.DirectoryError = $"IO error: {ex.Message}";
            
            _logger.LogError(ex, "IO error validating directory: {DirectoryPath}", directoryPath);
            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.ValidationCompleted = DateTime.UtcNow;
            result.ValidationTimeMs = (result.ValidationCompleted - result.ValidationStarted).TotalMilliseconds;
            result.DirectoryError = $"Access denied: {ex.Message}";
            
            _logger.LogError(ex, "Access denied validating directory: {DirectoryPath}", directoryPath);
            return result;
        }
    }
    
    /// <summary>
    /// Determine schema type from file path
    /// </summary>
    private static string? DetermineSchemaType(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        
        if (fileName.Contains("strategy", StringComparison.Ordinal) || fileName.StartsWith("s2") || fileName.StartsWith("s3") || 
            fileName.StartsWith("s6") || fileName.StartsWith("s11"))
        {
            return "strategy";
        }
        
        if (fileName.Contains("pattern", StringComparison.Ordinal))
        {
            return "pattern";
        }
        
        throw new InvalidOperationException($"Unable to determine YAML schema type for file: {fileName}. File must be a strategy (s2, s3, s6, s11) or pattern file.");
    }
    
    /// <summary>
    /// Validate YAML object against schema definition
    /// </summary>
    private static async Task ValidateAgainstSchemaAsync(object yamlObject, YamlSchemaDefinition schema, YamlValidationResult result)
    {
        if (yamlObject is not Dictionary<object, object> yamlDict)
        {
            result.Errors.Add("YAML root must be an object/dictionary");
            return;
        }
        
        var stringDict = yamlDict.ToDictionary(
            kvp => kvp.Key?.ToString() ?? string.Empty,
            kvp => kvp.Value
        );
        
        // Check required fields
        foreach (var requiredField in schema.RequiredFields)
        {
            if (!stringDict.ContainsKey(requiredField))
            {
                result.Errors.Add($"Required field missing: {requiredField}");
            }
        }
        
        // Validate field values
        foreach (var kvp in stringDict)
        {
            var fieldName = kvp.Key;
            var fieldValue = kvp.Value;
            
            // Check if field is known
            if (!schema.RequiredFields.Contains(fieldName) && !schema.OptionalFields.Contains(fieldName))
            {
                result.Warnings.Add($"Unknown field: {fieldName}");
                continue;
            }
            
            // Apply field validator if available
            if (schema.FieldValidators.TryGetValue(fieldName, out var validator))
            {
                try
                {
                    if (!validator(fieldValue))
                    {
                        result.Errors.Add($"Field validation failed: {fieldName} = {fieldValue}");
                    }
                }
                catch (ArgumentException ex)
                {
                    result.Errors.Add($"Field validation argument error for {fieldName}: {ex.Message}");
                }
                catch (FormatException ex)
                {
                    result.Errors.Add($"Field validation format error for {fieldName}: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    result.Errors.Add($"Field validation operation error for {fieldName}: {ex.Message}");
                }
            }
        }
        
        // Set validity based on error count
        result.IsValid = result.Errors.Count == 0;
        
        await Task.CompletedTask.ConfigureAwait(false); // Satisfy async signature
    }
    
    /// <summary>
    /// Validate WHEN clause in strategy definition
    /// </summary>
    private bool ValidateWhenClause(object value)
    {
        if (value is not Dictionary<object, object> whenDict)
            return false;
            
        // Must have at least one condition
        if (whenDict.Count == 0)
            return false;
            
        // Validate condition structure
        foreach (var kvp in whenDict)
        {
            var key = kvp.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return false;
                
            // Check if it's a valid DSL feature key
            if (!IsValidDslFeatureKey(key))
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validate THEN clause in strategy definition
    /// </summary>
    private bool ValidateThenClause(object value)
    {
        if (value is not Dictionary<object, object> thenDict)
            return false;
            
        // Must specify action
        if (!thenDict.ContainsKey("action"))
            return false;
            
        var action = thenDict["action"]?.ToString();
        if (string.IsNullOrWhiteSpace(action))
            return false;
            
        // Validate action type
        var validActions = new[] { "buy", "sell", "hold", "close" };
        if (!validActions.Contains(action.ToLowerInvariant()))
            return false;
            
        return true;
    }
    
    /// <summary>
    /// Validate pattern conditions
    /// </summary>
    private bool ValidatePatternConditions(object value)
    {
        if (value is not List<object> conditions)
            return false;
            
        if (conditions.Count == 0)
            return false;
            
        // Each condition should be a valid condition object
        foreach (var condition in conditions)
        {
            if (condition is not Dictionary<object, object>)
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Check if pattern type is valid
    /// </summary>
    private static bool IsValidPatternType(string type)
    {
        var validTypes = new[]
        {
            "candlestick", "reversal", "continuation", "volume", "momentum", "structural"
        };
        
        return validTypes.Contains(type.ToLowerInvariant());
    }
    
    /// <summary>
    /// Check if DSL feature key is valid (should exist in feature map authority)
    /// </summary>
    private static bool IsValidDslFeatureKey(string key)
    {
        // Production validation of DSL key format - must match feature map authority patterns
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        
        // DSL keys must follow pattern: category.subcategory or category.value
        var parts = key.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }
        
        // Valid categories from feature map authority
        var validCategories = new[] { "zone", "pattern", "vdc", "mom", "atr", "price", "volume", "rsi", "ema", "sma", "market", "position", "risk" };
        return validCategories.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Generate comprehensive validation report
    /// </summary>
    public static string GenerateValidationReport(YamlDirectoryValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        
        var report = new StringBuilder();
        
        report.AppendLine(CultureInfo.InvariantCulture, $"=== YAML SCHEMA VALIDATION REPORT ===");
        report.AppendLine(CultureInfo.InvariantCulture, $"Directory: {result.DirectoryPath}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Search Pattern: {result.SearchPattern}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Validation Time: {result.ValidationTimeMs:F2}ms");
        report.AppendLine(CultureInfo.InvariantCulture, $"Overall Status: {(result.IsAllValid ? "✅ ALL VALID" : "❌ VALIDATION FAILED")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Files: {result.ValidFiles}/{result.TotalFiles} valid");
        report.AppendLine();
        
        if (!string.IsNullOrEmpty(result.DirectoryError))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Directory Error: {result.DirectoryError}");
            report.AppendLine();
        }
        
        // Group results by status
        var validFiles = result.FileResults.Where(r => r.IsValid).ToList();
        var invalidFiles = result.FileResults.Where(r => !r.IsValid).ToList();
        
        if (invalidFiles.Any())
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"❌ INVALID FILES:");
            foreach (var file in invalidFiles)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {Path.GetFileName(file.FilePath)}:");
                foreach (var error in file.Errors)
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"    ERROR: {error}");
                }
                foreach (var warning in file.Warnings)
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"    WARNING: {warning}");
                }
            }
            report.AppendLine();
        }
        
        if (validFiles.Any())
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"✅ VALID FILES:");
            foreach (var file in validFiles)
            {
                var warningCount = file.Warnings.Count;
                var warningText = warningCount > 0 ? $" ({warningCount} warnings)" : "";
                report.AppendLine(CultureInfo.InvariantCulture, $"  {Path.GetFileName(file.FilePath)}{warningText}");
            }
        }
        
        return report.ToString();
    }
}

/// <summary>
/// YAML schema definition
/// </summary>
public sealed class YamlSchemaDefinition
{
    public string Name { get; set; } = string.Empty;
    public string[] RequiredFields { get; set; } = Array.Empty<string>();
    public string[] OptionalFields { get; set; } = Array.Empty<string>();
    public Dictionary<string, Func<object, bool>> FieldValidators { get; set; } = new();
}

/// <summary>
/// YAML validation result for a single file
/// </summary>
public sealed class YamlValidationResult
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime ValidationStarted { get; set; }
    public DateTime ValidationCompleted { get; set; }
    public double ValidationTimeMs { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// YAML validation result for a directory
/// </summary>
public sealed class YamlDirectoryValidationResult
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string SearchPattern { get; set; } = string.Empty;
    public DateTime ValidationStarted { get; set; }
    public DateTime ValidationCompleted { get; set; }
    public double ValidationTimeMs { get; set; }
    public int TotalFiles { get; set; }
    public int ValidFiles { get; set; }
    public int InvalidFiles { get; set; }
    public bool IsAllValid { get; set; }
    public string? DirectoryError { get; set; }
    public List<YamlValidationResult> FileResults { get; set; } = new();
}