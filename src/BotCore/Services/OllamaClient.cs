using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BotCore.Services;

/// <summary>
/// Client for communicating with Ollama AI service to enable conversational trading bot
/// </summary>
public sealed class OllamaClient : IDisposable
{
    private readonly ILogger<OllamaClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _ollamaBaseUrl;
    private readonly string _modelName;
    private readonly bool _tradeCommentaryEnabled;
    private bool _disposed;
    private bool _serviceUnavailableLogged;

    public OllamaClient(ILogger<OllamaClient> logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        
        _logger = logger;

        // Read configuration with defaults
        _ollamaBaseUrl = configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434";
        _modelName = configuration["OLLAMA_MODEL"] ?? "gemma2:2b";
        _tradeCommentaryEnabled = configuration["OLLAMA_TRADE_COMMENTARY_ENABLED"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

        // Create HTTP client with extended timeout for AI model inference (90 seconds to handle slower models)
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        var commentaryStatus = _tradeCommentaryEnabled ? "ENABLED" : "DISABLED";
        _logger.LogInformation("🤖 [OLLAMA] Initialized with URL: {Url}, Model: {Model}, Trade Commentary: {Status}", 
            _ollamaBaseUrl, _modelName, commentaryStatus);
    }

    /// <summary>
    /// Ask Ollama AI a question and get a response
    /// </summary>
    /// <param name="prompt">The prompt/question to send to the AI</param>
    /// <param name="isTradeCommentary">True if this is trade commentary (BOT-THINKING, BOT-COMMENTARY, BOT-REFLECTION), false for learning commentary</param>
    /// <returns>The AI's response text, or empty string on error or if disabled</returns>
    public async Task<string> AskAsync(string prompt, bool isTradeCommentary = true)
    {
        // Skip trade commentary if disabled (DRY_RUN mode)
        if (isTradeCommentary && !_tradeCommentaryEnabled)
        {
            return string.Empty;
        }
        
        try
        {
            // Create JSON request object
            var requestObject = new
            {
                model = _modelName,
                prompt = prompt,
                stream = false
            };

            var jsonContent = JsonSerializer.Serialize(requestObject);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send POST request to Ollama
            var response = await _httpClient.PostAsync(
                new Uri($"{_ollamaBaseUrl}/api/generate"),
                content
            ).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            // Parse response
            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var responseObject = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

            return responseObject?.Response ?? string.Empty;
        }
        catch (HttpRequestException ex)
        {
            // Only log 404 errors once to avoid log flooding when service is unavailable
            if (!_serviceUnavailableLogged && ex.Message.Contains("404"))
            {
                _logger.LogWarning("⚠️ [OLLAMA] Service unavailable (404 Not Found) - AI features disabled. Configure OLLAMA_BASE_URL if needed.");
                _serviceUnavailableLogged = true;
            }
            else if (!ex.Message.Contains("404"))
            {
                // Log non-404 HTTP errors normally
                _logger.LogError(ex, "❌ [OLLAMA] HTTP error during AI request");
            }
            return string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "❌ [OLLAMA] Request timeout during AI request");
            return string.Empty;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "❌ [OLLAMA] JSON parsing error during AI request");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [OLLAMA] Unexpected error during AI request");
            return string.Empty;
        }
    }

    /// <summary>
    /// Check if Ollama service is connected and available
    /// </summary>
    /// <returns>True if connected, false otherwise</returns>
    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(new Uri($"{_ollamaBaseUrl}/api/tags")).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Response model from Ollama API - used by System.Text.Json for deserialization
    /// </summary>
    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
