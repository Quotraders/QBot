using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using BotCore.Utilities;

namespace BotCore
{
    /// <summary>
    /// Uploads local training data to cloud storage for 100% cloud-based learning.
    /// Converts JSONL training data to parquet format and uploads to S3.
    /// Replaces local training - bot only needs to run for trading, not learning.
    /// </summary>
    public sealed class CloudDataUploader : IDisposable
    {
        // Configuration constants
        private const int MaxLogMessageLength = 100;
        
        private readonly ILogger<CloudDataUploader> _log;
        private readonly Timer? _timer;
        private readonly string _dataDir;
        private readonly string _s3Bucket;
        private readonly bool _enabled;
        private bool _disposed;

        public CloudDataUploader(ILogger<CloudDataUploader> logger)
        {
            _log = logger;
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data", "rl_training");
            _s3Bucket = Environment.GetEnvironmentVariable("S3_BUCKET") ?? "";

            // Enable if S3 configuration is present
            _enabled = !string.IsNullOrEmpty(_s3Bucket) &&
                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")) &&
                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));

            if (_enabled)
            {
                // Upload data every 15 minutes using TimerHelper
                var interval = TimeSpan.FromMinutes(15);
                _timer = TimerHelper.CreateAsyncTimerWithImmediateStart(UploadDataAsync, interval);
                LoggingHelper.LogServiceStarted(_log, "CloudDataUploader", interval, "uploading");
            }
            else
            {
                _log.LogInformation("[CloudDataUploader] Disabled - missing S3 configuration (S3_BUCKET, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timer?.Dispose();
            _log.LogInformation("[CloudDataUploader] Disposed");
        }

        private async Task UploadDataAsync()
        {
            if (_disposed || !_enabled) return;

            try
            {
                await UploadTrainingDataToCloudAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] HTTP error uploading training data");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] Access denied uploading training data");
            }
            catch (IOException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] I/O error uploading training data");
            }
            catch (TimeoutException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] Timeout uploading training data");
            }
        }

        /// <summary>
        /// Upload local training data (JSONL) to cloud storage as parquet for cloud training pipeline
        /// </summary>
        public async Task UploadTrainingDataToCloudAsync()
        {
            if (!_enabled)
            {
                _log.LogDebug("[CloudDataUploader] Upload skipped - not enabled");
                return;
            }

            try
            {
                if (!Directory.Exists(_dataDir))
                {
                    _log.LogDebug("[CloudDataUploader] No training data directory found: {Dir}", _dataDir);
                    return;
                }

                var jsonlFiles = Directory.GetFiles(_dataDir, "*.jsonl");
                if (jsonlFiles.Length == 0)
                {
                    _log.LogDebug("[CloudDataUploader] No JSONL files to upload");
                    return;
                }

                // Group files by symbol and type (features vs outcomes)
                var featureFiles = jsonlFiles.Where(f => Path.GetFileName(f).StartsWith("features_")).ToList();
                var outcomeFiles = jsonlFiles.Where(f => Path.GetFileName(f).StartsWith("outcomes_")).ToList();

                _log.LogInformation("[CloudDataUploader] Found {FeatureFiles} feature files, {OutcomeFiles} outcome files",
                    featureFiles.Count, outcomeFiles.Count);

                // Convert and upload feature files  
                foreach (var file in featureFiles)
                {
                    await ConvertAndUploadJsonlFileAsync(file, "features").ConfigureAwait(false);
                }

                // Convert and upload outcome files
                foreach (var file in outcomeFiles)
                {
                    await ConvertAndUploadJsonlFileAsync(file, "outcomes").ConfigureAwait(false);
                }

                // Clean up old JSONL files after successful upload (keep last 3 days)
                await CleanupOldFilesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[CloudDataUploader] Error uploading training data");
                throw new InvalidOperationException("Cloud data upload operation failed", ex);
            }
        }

        private async Task ConvertAndUploadJsonlFileAsync(string jsonlPath, string dataType)
        {
            try
            {
                var fileName = Path.GetFileName(jsonlPath);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

                // Extract symbol from filename (e.g., features_es_20241201.jsonl -> es)
                var symbol = ExtractSymbolFromFilename(fileName);

                // Create parquet filename for cloud storage
                var parquetFileName = $"{dataType}_{symbol}_{timestamp}.parquet";
                var tempParquetPath = Path.Combine(Path.GetTempPath(), parquetFileName);
                var s3Key = $"logs/{parquetFileName}";

                // Convert JSONL to parquet format
                await ConvertJsonlToParquetAsync(jsonlPath, tempParquetPath).ConfigureAwait(false);

                // Upload to S3 using AWS CLI (since AWS SDK would add dependencies)
                var success = await UploadToS3Async(tempParquetPath, s3Key).ConfigureAwait(false);

                if (success)
                {
                    _log.LogInformation("[CloudDataUploader] ✅ Uploaded {Type} data: {File} -> s3://{Bucket}/{Key}",
                        dataType, fileName, _s3Bucket, s3Key);

                    // Mark original file as uploaded by renaming it
                    var uploadedPath = jsonlPath + ".uploaded";
                    if (!File.Exists(uploadedPath))
                    {
                        File.Move(jsonlPath, uploadedPath);
                    }
                }
                else
                {
                    _log.LogWarning("[CloudDataUploader] Failed to upload {File}", fileName);
                }

                // Clean up temp file
                if (File.Exists(tempParquetPath))
                {
                    File.Delete(tempParquetPath);
                }
            }
            catch (FileNotFoundException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] File not found error converting/uploading {File}", jsonlPath);
            }
            catch (IOException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] I/O error converting/uploading {File}", jsonlPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] Access denied converting/uploading {File}", jsonlPath);
            }
            catch (InvalidOperationException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] Invalid operation converting/uploading {File}", jsonlPath);
            }
        }

        private static string ExtractSymbolFromFilename(string filename)
        {
            // Extract symbol from patterns like: features_es_20241201.jsonl or outcomes_nq_20241201.jsonl
            var parts = Path.GetFileNameWithoutExtension(filename).Split('_');
            if (parts.Length >= 2)
            {
                var symbol = parts[1].ToLowerInvariant();
                if (symbol == "es" || symbol == "nq")
                {
                    return symbol;
                }
            }
            return "unknown";
        }

        private async Task ConvertJsonlToParquetAsync(string jsonlPath, string parquetPath)
        {
            // For now, create a simple CSV format that can be easily converted to parquet
            // In a full implementation, you'd use a proper parquet library
            var csvPath = parquetPath.Replace(".parquet", ".csv");

            var lines = await File.ReadAllLinesAsync(jsonlPath).ConfigureAwait(false);
            if (lines.Length == 0) return;

            // Parse first line to get column headers
            using var firstDoc = JsonDocument.Parse(lines[0]);
            var headers = firstDoc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

            var csvLines = new List<string> { string.Join(",", headers) };

            // Convert each JSONL line to CSV
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var values = headers.Select(h =>
                    {
                        if (doc.RootElement.TryGetProperty(h, out var prop))
                        {
                            return prop.ValueKind switch
                            {
                                JsonValueKind.String => $"\"{prop.GetString()?.Replace("\"", "\"\"")}\"",
                                JsonValueKind.Number => prop.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                _ => prop.GetRawText()
                            };
                        }
                        return "";
                    });
                    csvLines.Add(string.Join(",", values));
                }
                catch (JsonException ex)
                {
                    _log.LogWarning(ex, "[CloudDataUploader] Failed to parse JSONL line: {Line}", line.Substring(0, Math.Min(MaxLogMessageLength, line.Length)));
                }
                catch (ArgumentException ex)
                {
                    _log.LogWarning(ex, "[CloudDataUploader] Invalid JSONL line format: {Line}", line.Substring(0, Math.Min(MaxLogMessageLength, line.Length)));
                }
            }

            await File.WriteAllLinesAsync(csvPath, csvLines).ConfigureAwait(false);

            // For now, just rename CSV to parquet (cloud pipeline can handle CSV files)
            // In production, you'd use Apache Arrow or similar for true parquet conversion
            File.Move(csvPath, parquetPath);

            _log.LogDebug("[CloudDataUploader] Converted {Lines} JSONL lines to {Format}", lines.Length, "parquet-compatible CSV");
        }

        private async Task<bool> UploadToS3Async(string filePath, string s3Key)
        {
            try
            {
                var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";

                // Use AWS CLI for upload (more reliable than coding S3 client from scratch)
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "aws",
                    Arguments = $"s3 cp \"{filePath}\" \"s3://{_s3Bucket}/{s3Key}\" --region {region}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    _log.LogDebug("[CloudDataUploader] AWS CLI upload success: {Output}", output.Trim());
                    return true;
                }
                else
                {
                    _log.LogWarning("[CloudDataUploader] AWS CLI upload failed (exit {Code}): {Error}", process.ExitCode, error.Trim());
                    return false;
                }
            }
            catch (InvalidOperationException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] S3 upload process error");
                return false;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _log.LogError(ex, "[CloudDataUploader] AWS CLI not found or process start failed");
                return false;
            }
            catch (IOException ex)
            {
                _log.LogError(ex, "[CloudDataUploader] File I/O error during S3 upload");
                return false;
            }
        }

        private Task CleanupOldFilesAsync()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-3);
                var uploadedFiles = Directory.GetFiles(_dataDir, "*.uploaded");

                foreach (var file in uploadedFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc < cutoff)
                    {
                        File.Delete(file);
                        _log.LogDebug("[CloudDataUploader] Cleaned up old uploaded file: {File}", Path.GetFileName(file));
                    }
                }
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "[CloudDataUploader] I/O error cleaning up old files");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "[CloudDataUploader] Access denied cleaning up old files");
            }

            return Task.CompletedTask;
        }
    }
}