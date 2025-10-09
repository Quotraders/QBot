using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using TradingBot.Abstractions;
using BotCore.Utilities;

namespace BotCore.ML;

/// <summary>
/// Async, batched ONNX inference service with GPU/quantized path detection
/// Implements requirement: Async, batched ONNX inference with GPU/quantized path detection
/// </summary>
public class BatchedOnnxInferenceService : IDisposable
{
    // Batch processing constants
    private const int BatchQueueOverflowMultiplier = 2; // Allow up to 2x batch size in queue
    
    private readonly ILogger<BatchedOnnxInferenceService> _logger;
    private readonly OnnxModelLoader _modelLoader;
    private readonly BatchConfig _batchConfig;
    private readonly Timer _batchProcessor;
    private readonly Channel<InferenceRequest> _requestQueue;
    private bool _disposed;

    // GPU and quantization detection
    private bool _gpuAvailable;
    private bool _quantizedModelsSupported;

    // Structured logging delegates
    private static readonly Action<ILogger, bool, bool, int, Exception?> LogServiceInitialized =
        LoggerMessage.Define<bool, bool, int>(
            LogLevel.Information,
            new EventId(1, nameof(LogServiceInitialized)),
            "Batched ONNX inference service initialized - GPU: {GpuAvailable}, Quantization: {QuantizationSupported}, BatchSize: {BatchSize}");

    private static readonly Action<ILogger, string, Exception?> LogHardwareDetection =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(LogHardwareDetection)),
            "Hardware detection - Available providers: {Providers}");

    private static readonly Action<ILogger, Exception?> LogHardwareDetectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(LogHardwareDetectionFailed)),
            "Failed to detect hardware capabilities");

    private static readonly Action<ILogger, Exception?> LogBatchProcessingError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4, nameof(LogBatchProcessingError)),
            "Error processing batches");

    private static readonly Action<ILogger, Exception?> LogModelBatchError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5, nameof(LogModelBatchError)),
            "Error processing model batch");

    private static readonly Action<ILogger, string, Exception?> LogRequestTimeout =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6, nameof(LogRequestTimeout)),
            "Request timeout for model: {ModelKey}");

    private static readonly Action<ILogger, int, Exception?> LogBatchProcessed =
        LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(7, nameof(LogBatchProcessed)),
            "Processed batch with {RequestCount} requests");

    private static readonly Action<ILogger, Exception?> LogBatchProcessingException =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(8, nameof(LogBatchProcessingException)),
            "Exception processing batch");

    private static readonly Action<ILogger, Exception?> LogChannelReadWarning =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(9, nameof(LogChannelReadWarning)),
            "Channel read was cancelled or completed");

    public BatchedOnnxInferenceService(
        ILogger<BatchedOnnxInferenceService> logger,
        OnnxModelLoader modelLoader,
        BatchConfig batchConfig)
    {
        _logger = logger;
        _modelLoader = modelLoader;
        _batchConfig = batchConfig;

        // Create unbounded channel for inference requests
        var options = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        };
        _requestQueue = Channel.CreateUnbounded<InferenceRequest>(options);

        // Initialize hardware detection
        InitializeHardwareDetection();

        // Start batch processor using TimerHelper to reduce duplication
        var processingInterval = TimeSpan.FromMilliseconds(100);
        _batchProcessor = TimerHelper.CreateAsyncTimerWithImmediateStart(ProcessBatchesAsync, processingInterval);

        LogServiceInitialized(_logger, _gpuAvailable, _quantizedModelsSupported, _batchConfig.ModelInferenceBatchSize, null);
    }

    private void InitializeHardwareDetection()
    {
        try
        {
            // Detect GPU availability
            var providers = OrtEnv.Instance().GetAvailableProviders();
            _gpuAvailable = providers.Contains("CUDAExecutionProvider") || providers.Contains("DmlExecutionProvider");

            // Check for quantization support (INT8/FP16)
            _quantizedModelsSupported = true; // ONNX Runtime generally supports quantization

            LogHardwareDetection(_logger, string.Join(", ", providers), null);
        }
        catch (OnnxRuntimeException ex)
        {
            LogHardwareDetectionFailed(_logger, ex);
            _gpuAvailable = false;
            _quantizedModelsSupported = false;
        }
        catch (InvalidOperationException ex)
        {
            LogHardwareDetectionFailed(_logger, ex);
            _gpuAvailable = false;
            _quantizedModelsSupported = false;
        }
    }

    /// <summary>
    /// Submit inference request for batched processing
    /// </summary>
    public async Task<double[]> InferAsync(string modelPath, float[] features, CancellationToken cancellationToken = default)
    {
        var request = new InferenceRequest
        {
            Id = Guid.NewGuid().ToString(),
            ModelPath = modelPath,
            Features = features,
            Timestamp = DateTime.UtcNow,
            CompletionSource = new TaskCompletionSource<double[]>(),
            CancellationToken = cancellationToken
        };

        // Add to queue for batched processing
        await _requestQueue.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        // Wait for result with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // Configurable timeout

        try
        {
            return await request.CompletionSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogChannelReadWarning(_logger, null);
            throw new TimeoutException($"Inference request timed out for model: {modelPath}");
        }
    }

    /// <summary>
    /// Process batched inference requests
    /// </summary>
    private async Task ProcessBatchesAsync()
    {
        try
        {
            // Collect pending requests
            var requests = new List<InferenceRequest>();
            while (_requestQueue.Reader.TryRead(out var request) && requests.Count < _batchConfig.ModelInferenceBatchSize * BatchQueueOverflowMultiplier)
            {
                requests.Add(request);
            }

            if (requests.Count == 0) return;

            // Group by model path for efficient batching
            var modelGroups = requests.GroupBy(r => r.ModelPath).ToList();

            foreach (var group in modelGroups)
            {
                await ProcessModelBatchAsync(group.Key, group.ToList()).ConfigureAwait(false);
            }
        }
        catch (OnnxRuntimeException ex)
        {
            LogBatchProcessingError(_logger, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogBatchProcessingError(_logger, ex);
        }
        catch (ArgumentException ex)
        {
            LogBatchProcessingError(_logger, ex);
        }
    }

    /// <summary>
    /// Process batch of requests for a specific model
    /// </summary>
    private async Task ProcessModelBatchAsync(string modelPath, List<InferenceRequest> requests)
    {
        try
        {
            // Load model if not already loaded
            var session = await _modelLoader.LoadModelAsync(modelPath).ConfigureAwait(false);
            if (session == null)
            {
                LogRequestTimeout(_logger, modelPath, null);
                FailRequests(requests, new InvalidOperationException($"Failed to load model: {modelPath}"));
                return;
            }

            // Determine optimal batch size
            var batchSize = Math.Min(requests.Count, _batchConfig.ModelInferenceBatchSize);
            
            // Process in batches
            for (int i = 0; i < requests.Count; i += batchSize)
            {
                var batchRequests = requests.Skip(i).Take(batchSize).ToList();
                await ProcessSingleBatchAsync(session, batchRequests).ConfigureAwait(false);
            }
        }
        catch (OnnxRuntimeException ex)
        {
            LogModelBatchError(_logger, ex);
            FailRequests(requests, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogModelBatchError(_logger, ex);
            FailRequests(requests, ex);
        }
        catch (ArgumentException ex)
        {
            LogModelBatchError(_logger, ex);
            FailRequests(requests, ex);
        }
    }

    /// <summary>
    /// Process a single batch of inference requests
    /// </summary>
    private async Task ProcessSingleBatchAsync(InferenceSession session, List<InferenceRequest> batchRequests)
    {
        try
        {
            // Prepare batch input
            var batchSize = batchRequests.Count;
            var featureSize = batchRequests[0].Features.Length;
            var batchInput = new float[batchSize, featureSize];

            for (int i = 0; i < batchSize; i++)
            {
                for (int j = 0; j < featureSize; j++)
                {
                    batchInput[i, j] = batchRequests[i].Features[j];
                }
            }

            // Create input tensor
            var inputTensor = new DenseTensor<float>(batchInput.Cast<float>().ToArray(), new[] { batchSize, featureSize });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.ToList()[0], inputTensor)
            };

            // Run inference
            var outputs = await Task.Run(() => session.Run(inputs)).ConfigureAwait(false);
            var outputsList = outputs.ToList();
            var outputTensor = outputsList[0].AsTensor<float>();

            // Extract results and complete requests
            for (int i = 0; i < batchSize; i++)
            {
                try
                {
                    var result = new double[outputTensor.Dimensions[1]];
                    for (int j = 0; j < result.Length; j++)
                    {
                        result[j] = outputTensor[i, j];
                    }

                    batchRequests[i].CompletionSource.SetResult(result);
                }
                catch (IndexOutOfRangeException ex)
                {
                    batchRequests[i].CompletionSource.SetException(ex);
                }
                catch (ArgumentException ex)
                {
                    batchRequests[i].CompletionSource.SetException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    batchRequests[i].CompletionSource.SetException(ex);
                }
            }

            LogBatchProcessed(_logger, batchSize, null);

            // Dispose outputs
            foreach (var output in outputs)
            {
                output.Dispose();
            }
        }
        catch (OnnxRuntimeException ex)
        {
            LogBatchProcessingException(_logger, ex);
            FailRequests(batchRequests, ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            LogBatchProcessingException(_logger, ex);
            FailRequests(batchRequests, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogBatchProcessingException(_logger, ex);
            FailRequests(batchRequests, ex);
        }
        catch (ArgumentException ex)
        {
            LogBatchProcessingException(_logger, ex);
            FailRequests(batchRequests, ex);
        }
    }

    private static void FailRequests(List<InferenceRequest> requests, Exception exception)
    {
        foreach (var request in requests)
        {
            try
            {
                request.CompletionSource.SetException(exception);
            }
            catch (InvalidOperationException)
            {
                // Ignore if already completed
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _batchProcessor?.Dispose();
                _requestQueue.Writer.Complete();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Individual inference request
/// </summary>
internal sealed class InferenceRequest
{
    public string Id { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public float[] Features { get; set; } = Array.Empty<float>();
    public DateTime Timestamp { get; set; }
    public TaskCompletionSource<double[]> CompletionSource { get; set; } = new();
    public CancellationToken CancellationToken { get; set; }
}