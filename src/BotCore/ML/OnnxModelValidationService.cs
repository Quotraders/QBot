using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Collections.ObjectModel;
using System.Text;

namespace BotCore.ML;

/// <summary>
/// Validation result for an individual ONNX model
/// </summary>
public class ValidationResult
{
    public string ModelPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public TimeSpan LoadTime { get; set; }
    public long MemoryUsage { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public bool InferenceValidated { get; set; }
    public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Summary of validation results across all models
/// </summary>
public class ValidationSummary
{
    public int TotalModels { get; set; }
    public int ValidModels { get; set; }
    public int FailedModels { get; set; }
    public double SuccessRate { get; set; }
    public double TotalLoadTimeMs { get; set; }
    public long TotalMemoryUsageMB { get; set; }
    private readonly List<string> _failedModelPaths = new();
    public IReadOnlyList<string> FailedModelPaths => _failedModelPaths;
    public DateTime ValidationDate { get; set; }
    
    internal void AddFailedModelPath(string path) => _failedModelPaths.Add(path);
}

/// <summary>
/// ONNX model startup validation service to ensure all models load properly at application startup
/// Provides comprehensive testing and validation of ML model infrastructure
/// </summary>
public sealed class OnnxModelValidationService
{
    private readonly ILogger<OnnxModelValidationService> _logger;
    private readonly OnnxModelLoader _modelLoader;
    private readonly List<string> _modelPaths = new();
    private readonly Dictionary<string, ValidationResult> _validationResults = new();

    // S109: ONNX model validation thresholds
    private const int KilobytesToMegabytes = 1024;
    private const long GigabytesInBytes = 2L * 1024 * 1024 * 1024; // 2GB limit per model
    private const int MaxLoadTimeSeconds = 30; // Maximum acceptable load time

    public OnnxModelValidationService(ILogger<OnnxModelValidationService> logger, OnnxModelLoader modelLoader)
    {
        _logger = logger;
        _modelLoader = modelLoader;
    }

    /// <summary>
    /// Add model path for validation
    /// </summary>
    public void AddModelPath(string modelPath)
    {
        if (!string.IsNullOrEmpty(modelPath) && !_modelPaths.Contains(modelPath))
        {
            _modelPaths.Add(modelPath);
            _logger.LogDebug("[ONNX-Validation] Added model for validation: {ModelPath}", modelPath);
        }
    }

    /// <summary>
    /// Add multiple model paths for validation
    /// </summary>
    public void AddModelPaths(IEnumerable<string> modelPaths)
    {
        ArgumentNullException.ThrowIfNull(modelPaths);
        
        foreach (var path in modelPaths)
        {
            AddModelPath(path);
        }
    }

    /// <summary>
    /// Discover ONNX models in specified directories
    /// </summary>
    public void DiscoverModels(params string[] directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("[ONNX-Validation] Model directory not found: {Directory}", directory);
                continue;
            }

            var onnxFiles = Directory.GetFiles(directory, "*.onnx", SearchOption.AllDirectories);
            foreach (var file in onnxFiles)
            {
                AddModelPath(file);
            }

            _logger.LogInformation("[ONNX-Validation] Discovered {ModelCount} ONNX models in {Directory}", 
                onnxFiles.Length, directory);
        }
    }

    /// <summary>
    /// Validate all added models
    /// </summary>
    public async Task<bool> ValidateAllModelsAsync()
    {
        _logger.LogInformation("[ONNX-Validation] Starting validation of {ModelCount} models", _modelPaths.Count);

        var allValid = true;
        var validationTasks = new List<Task<ValidationResult>>();

        // Start all validations in parallel
        foreach (var modelPath in _modelPaths)
        {
            validationTasks.Add(ValidateModelAsync(modelPath));
        }

        // Wait for all validations to complete
        var results = await Task.WhenAll(validationTasks).ConfigureAwait(false);

        // Store results and check overall status
        foreach (var result in results)
        {
            _validationResults[result.ModelPath] = result;
            
            if (!result.IsValid)
            {
                allValid = false;
                _logger.LogError("[ONNX-Validation] Model validation FAILED: {ModelPath} - {Error}", 
                    result.ModelPath, result.ErrorMessage);
            }
            else
            {
                _logger.LogInformation("[ONNX-Validation] Model validation SUCCESS: {ModelPath} " +
                    "(Load: {LoadTime}ms, Memory: {MemoryMB}MB, I/O: {InputCount}/{OutputCount})",
                    result.ModelPath, result.LoadTime.TotalMilliseconds, result.MemoryUsage / KilobytesToMegabytes / KilobytesToMegabytes,
                    result.InputCount, result.OutputCount);
            }
        }

        // Summary
        var validCount = results.Count(r => r.IsValid);
        _logger.LogInformation("[ONNX-Validation] Validation completed: {ValidCount}/{TotalCount} models valid", 
            validCount, results.Length);

        if (!allValid)
        {
            _logger.LogWarning("[ONNX-Validation] Some models failed validation - check logs for details");
        }

        return allValid;
    }

    /// <summary>
    /// Validate a single model
    /// </summary>
    private async Task<ValidationResult> ValidateModelAsync(string modelPath)
    {
        var result = new ValidationResult
        {
            ModelPath = modelPath,
            ValidationTime = DateTime.UtcNow
        };

        try
        {
            var startTime = DateTime.UtcNow;
            var memoryBefore = GC.GetTotalMemory(false);

            // Load the model
            var session = await _modelLoader.LoadModelAsync(modelPath, validateInference: true).ConfigureAwait(false);
            
            var loadTime = DateTime.UtcNow - startTime;
            var memoryAfter = GC.GetTotalMemory(false);

            if (session != null)
            {
                result.IsValid = true;
                result.LoadTime = loadTime;
                result.MemoryUsage = Math.Max(0, memoryAfter - memoryBefore);
                result.InputCount = session.InputMetadata.Count;
                result.OutputCount = session.OutputMetadata.Count;
                result.InferenceValidated = true;

                // Additional validation checks
                if (result.InputCount == 0)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "Model has no inputs";
                }
                else if (result.OutputCount == 0)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "Model has no outputs";
                }
                else if (result.LoadTime.TotalSeconds > MaxLoadTimeSeconds)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Model load time too slow: {result.LoadTime.TotalSeconds:F1}s";
                }
                else if (result.MemoryUsage > GigabytesInBytes)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Model memory usage too high: {result.MemoryUsage / KilobytesToMegabytes / KilobytesToMegabytes}MB";
                }
            }
            else
            {
                result.IsValid = false;
                result.ErrorMessage = "Failed to load model";
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[ONNX-Validation] Exception validating model: {ModelPath}", modelPath);
        }

        return result;
    }

    /// <summary>
    /// Get validation results
    /// </summary>
    public IReadOnlyDictionary<string, ValidationResult> GetValidationResults()
    {
        return _validationResults.AsReadOnly();
    }

    /// <summary>
    /// Get summary of validation results
    /// </summary>
    public ValidationSummary GetValidationSummary()
    {
        var validModels = _validationResults.Values.Count(r => r.IsValid);
        var totalModels = _validationResults.Count;
        var totalLoadTime = _validationResults.Values.Sum(r => r.LoadTime.TotalMilliseconds);
        var totalMemory = _validationResults.Values.Sum(r => r.MemoryUsage);
        var failedModels = _validationResults.Values.Where(r => !r.IsValid).ToList();

        var summary = new ValidationSummary
        {
            TotalModels = totalModels,
            ValidModels = validModels,
            FailedModels = totalModels - validModels,
            SuccessRate = totalModels > 0 ? (double)validModels / totalModels : 0,
            TotalLoadTimeMs = totalLoadTime,
            TotalMemoryUsageMB = totalMemory / 1024 / 1024,
            ValidationDate = DateTime.UtcNow
        };
        
        // Populate the readonly collection via Add method
        foreach (var failedModel in failedModels)
        {
            summary.AddFailedModelPath(failedModel.ModelPath);
        }
        
        return summary;
    }

    /// <summary>
    /// Run comprehensive validation and generate report
    /// </summary>
    public async Task<string> GenerateValidationReportAsync()
    {
        var success = await ValidateAllModelsAsync().ConfigureAwait(false);
        var summary = GetValidationSummary();

        var sb = new StringBuilder();
        sb.AppendLine("=== ONNX Model Validation Report ===");
        sb.AppendLine($"Generated: {summary.ValidationDate:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("SUMMARY:");
        sb.AppendLine($"- Total Models: {summary.TotalModels}");
        sb.AppendLine($"- Valid Models: {summary.ValidModels}");
        sb.AppendLine($"- Failed Models: {summary.FailedModels}");
        sb.AppendLine($"- Success Rate: {summary.SuccessRate:P1}");
        sb.AppendLine($"- Total Load Time: {summary.TotalLoadTimeMs:F0}ms");
        sb.AppendLine($"- Total Memory Usage: {summary.TotalMemoryUsageMB}MB");
        sb.AppendLine();
        sb.AppendLine($"STATUS: {(success ? "✅ ALL MODELS VALID" : "❌ SOME MODELS FAILED")}");

        if (summary.FailedModels > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FAILED MODELS:");
            foreach (var failedPath in summary.FailedModelPaths)
            {
                if (_validationResults.TryGetValue(failedPath, out var result))
                {
                    sb.AppendLine($"- {failedPath}: {result.ErrorMessage}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("DETAILED RESULTS:");
        foreach (var result in _validationResults.Values.OrderBy(r => r.ModelPath))
        {
            var status = result.IsValid ? "✅" : "❌";
            sb.AppendLine($"{status} {Path.GetFileName(result.ModelPath)} " +
                         $"({result.LoadTime.TotalMilliseconds:F0}ms, " +
                         $"{result.MemoryUsage / KilobytesToMegabytes / KilobytesToMegabytes}MB, " +
                         $"{result.InputCount}→{result.OutputCount})");
            
            if (!result.IsValid)
            {
                sb.AppendLine($"   Error: {result.ErrorMessage}");
            }
        }

        _logger.LogInformation("[ONNX-Validation] Validation report generated");
        return sb.ToString();
    }
}
