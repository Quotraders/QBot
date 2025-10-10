using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Collections.ObjectModel;
using System.Globalization;
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

    // Structured logging delegates
    private static readonly Action<ILogger, string, Exception?> LogModelAdded =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogModelAdded)),
            "[ONNX-Validation] Added model for validation: {ModelPath}");

    private static readonly Action<ILogger, string, Exception?> LogDirectoryNotFound =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogDirectoryNotFound)),
            "[ONNX-Validation] Model directory not found: {Directory}");

    private static readonly Action<ILogger, int, string, Exception?> LogModelsDiscovered =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(3, nameof(LogModelsDiscovered)),
            "[ONNX-Validation] Discovered {ModelCount} ONNX models in {Directory}");

    private static readonly Action<ILogger, int, Exception?> LogValidationStarting =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(4, nameof(LogValidationStarting)),
            "[ONNX-Validation] Starting validation of {ModelCount} models");

    private static readonly Action<ILogger, string, string, Exception?> LogValidationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(5, nameof(LogValidationFailed)),
            "[ONNX-Validation] Model validation FAILED: {ModelPath} - {Error}");

    private static readonly Action<ILogger, string, double, long, int, int, Exception?> LogValidationSuccess =
        LoggerMessage.Define<string, double, long, int, int>(
            LogLevel.Information,
            new EventId(6, nameof(LogValidationSuccess)),
            "[ONNX-Validation] Model validation SUCCESS: {ModelPath} (Load: {LoadTime}ms, Memory: {MemoryMB}MB, I/O: {InputCount}/{OutputCount})");

    private static readonly Action<ILogger, int, int, Exception?> LogValidationCompleted =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(7, nameof(LogValidationCompleted)),
            "[ONNX-Validation] Validation completed: {ValidCount}/{TotalCount} models valid");

    private static readonly Action<ILogger, Exception?> LogSomeModelsFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(8, nameof(LogSomeModelsFailed)),
            "[ONNX-Validation] Some models failed validation - check logs for details");

    private static readonly Action<ILogger, string, Exception?> LogValidationException =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(9, nameof(LogValidationException)),
            "[ONNX-Validation] Exception validating model: {ModelPath}");

    private static readonly Action<ILogger, Exception?> LogReportGenerated =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(10, nameof(LogReportGenerated)),
            "[ONNX-Validation] Validation report generated");

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
            LogModelAdded(_logger, modelPath, null);
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
                LogDirectoryNotFound(_logger, directory, null);
                continue;
            }

            var onnxFiles = Directory.GetFiles(directory, "*.onnx", SearchOption.AllDirectories);
            foreach (var file in onnxFiles)
            {
                AddModelPath(file);
            }

            LogModelsDiscovered(_logger, onnxFiles.Length, directory, null);
        }
    }

    /// <summary>
    /// Validate all added models
    /// </summary>
    public async Task<bool> ValidateAllModelsAsync()
    {
        LogValidationStarting(_logger, _modelPaths.Count, null);

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
                LogValidationFailed(_logger, result.ModelPath, result.ErrorMessage, null);
            }
            else
            {
                LogValidationSuccess(_logger, result.ModelPath, result.LoadTime.TotalMilliseconds, 
                    result.MemoryUsage / KilobytesToMegabytes / KilobytesToMegabytes,
                    result.InputCount, result.OutputCount, null);
            }
        }

        // Summary
        var validCount = results.Count(r => r.IsValid);
        LogValidationCompleted(_logger, validCount, results.Length, null);

        if (!allValid)
        {
            LogSomeModelsFailed(_logger, null);
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
        catch (OnnxRuntimeException ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"ONNX runtime error: {ex.Message}";
            LogValidationException(_logger, modelPath, ex);
        }
        catch (FileNotFoundException ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Model file not found: {ex.Message}";
            LogValidationException(_logger, modelPath, ex);
        }
        catch (InvalidOperationException ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Invalid operation: {ex.Message}";
            LogValidationException(_logger, modelPath, ex);
        }
        catch (ArgumentException ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Invalid argument: {ex.Message}";
            LogValidationException(_logger, modelPath, ex);
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"=== ONNX Model Validation Report ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Generated: {summary.ValidationDate:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"SUMMARY:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Total Models: {summary.TotalModels}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Valid Models: {summary.ValidModels}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Failed Models: {summary.FailedModels}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Success Rate: {summary.SuccessRate:P1}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Total Load Time: {summary.TotalLoadTimeMs:F0}ms");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Total Memory Usage: {summary.TotalMemoryUsageMB}MB");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"STATUS: {(success ? "✅ ALL MODELS VALID" : "❌ SOME MODELS FAILED")}");

        if (summary.FailedModels > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"FAILED MODELS:");
            foreach (var failedPath in summary.FailedModelPaths)
            {
                if (_validationResults.TryGetValue(failedPath, out var result))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- {failedPath}: {result.ErrorMessage}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"DETAILED RESULTS:");
        foreach (var result in _validationResults.Values.OrderBy(r => r.ModelPath))
        {
            var status = result.IsValid ? "✅" : "❌";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{status} {Path.GetFileName(result.ModelPath)} " +
                         $"({result.LoadTime.TotalMilliseconds:F0}ms, " +
                         $"{result.MemoryUsage / KilobytesToMegabytes / KilobytesToMegabytes}MB, " +
                         $"{result.InputCount}→{result.OutputCount})");
            
            if (!result.IsValid)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"   Error: {result.ErrorMessage}");
            }
        }

        LogReportGenerated(_logger, null);
        return sb.ToString();
    }
}
