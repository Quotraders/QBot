using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BotCore.Utilities;

namespace BotCore;

/// <summary>
/// GitHub-based cloud RL trainer that downloads models from GitHub Releases
/// </summary>
public sealed class CloudRlTrainer : IDisposable
{
    private readonly ILogger _log;
    private readonly HttpClient _http;
    private readonly string _dataDir;
    private readonly string _modelDir;
    private readonly Timer _timer;
    private bool _disposed;

    public CloudRlTrainer(ILogger logger, HttpClient? httpClient = null)
    {
        _log = logger;
        _http = httpClient ?? new HttpClient();
        _dataDir = Path.Combine(AppContext.BaseDirectory, "data", "rl_training");
        _modelDir = Path.Combine(AppContext.BaseDirectory, "models", "rl");

        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_modelDir);

        // Use TimerHelper to reduce duplication
        var pollInterval = TimeSpan.FromMinutes(30);
        _timer = TimerHelper.CreateAsyncTimerWithImmediateStart(CheckForUpdatesCallback, pollInterval);
        LoggingHelper.LogServiceStarted(_log, "CloudRlTrainer", pollInterval, "checking GitHub for model updates");
    }

    private async Task CheckForUpdatesCallback()
    {
        try
        {
            _log.LogInformation("[CloudRlTrainer] Checking GitHub Releases for new models...");
            await CheckGitHubReleasesAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            LoggingHelper.LogError(_log, ex, "CloudRlTrainer", "HTTP error checking for updates");
        }
        catch (TaskCanceledException ex)
        {
            LoggingHelper.LogError(_log, ex, "CloudRlTrainer", "timeout checking for updates");
        }
        catch (InvalidOperationException ex)
        {
            LoggingHelper.LogError(_log, ex, "CloudRlTrainer", "invalid operation checking for updates");
        }
    }

    private async Task CheckGitHubReleasesAsync()
    {
        try
        {
            // Check GitHub API for latest release
            var apiUrl = "https://api.github.com/repos/kevinsuero072897-collab/trading-bot-c-/releases/latest";
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingBot/1.0");

            var response = await _http.GetStringAsync(apiUrl).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<JsonElement>(response);

            if (release.TryGetProperty("tag_name", out var tagElement))
            {
                var latestTag = tagElement.GetString();
                var lastCheckFile = Path.Combine(_modelDir, "last_github_check.txt");

                string? lastCheckedTag = null;
                if (File.Exists(lastCheckFile))
                {
                    lastCheckedTag = await File.ReadAllTextAsync(lastCheckFile).ConfigureAwait(false);
                }

                if (latestTag != lastCheckedTag)
                {
                    _log.LogInformation("[CloudRlTrainer] New models available: {Tag}", latestTag);
                    await DownloadLatestModelsAsync(release).ConfigureAwait(false);
                    await File.WriteAllTextAsync(lastCheckFile, latestTag).ConfigureAwait(false);
                }
                else
                {
                    _log.LogDebug("[CloudRlTrainer] Models up to date: {Tag}", latestTag);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "[CloudRlTrainer] HTTP error checking GitHub releases");
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "[CloudRlTrainer] JSON parsing error checking GitHub releases");
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "[CloudRlTrainer] I/O error checking GitHub releases");
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogWarning(ex, "[CloudRlTrainer] Access denied checking GitHub releases");
        }
    }

    private async Task DownloadLatestModelsAsync(JsonElement release)
    {
        try
        {
            if (!release.TryGetProperty("assets", out var assetsElement)) return;

            foreach (var asset in assetsElement.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement)) continue;
                var assetName = nameElement.GetString();

                if (assetName?.EndsWith(".tar.gz") == true)
                {
                    if (!asset.TryGetProperty("browser_download_url", out var urlElement)) continue;
                    var downloadUrl = urlElement.GetString();

                    _log.LogInformation("[CloudRlTrainer] Downloading models: {AssetName}", assetName);

                    var modelBytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
                    var tempFile = Path.Combine(_modelDir, assetName);
                    await File.WriteAllBytesAsync(tempFile, modelBytes).ConfigureAwait(false);

                    // Extract the tar.gz file (simplified - in production use a proper library)
                    _log.LogInformation("[CloudRlTrainer] Downloaded {Bytes} bytes to {File}", modelBytes.Length, tempFile);
                    break;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "[CloudRlTrainer] HTTP error downloading models");
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "[CloudRlTrainer] Request timeout downloading models");
        }
        catch (IOException ex)
        {
            _log.LogError(ex, "[CloudRlTrainer] I/O error downloading models");
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogError(ex, "[CloudRlTrainer] Access denied downloading models");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Dispose();
        _http?.Dispose();
        _log.LogInformation("[CloudRlTrainer] Disposed");
    }
}
