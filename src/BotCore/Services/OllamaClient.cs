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
    private bool _disposed;

    public OllamaClient(ILogger<OllamaClient> logger, IConfiguration configuration)
    {
        _logger = logger;

        // Read configuration with defaults
        _ollamaBaseUrl = configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434";
        _modelName = configuration["OLLAMA_MODEL"] ?? "gemma2:2b";

        // Create HTTP client with timeout
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _logger.LogInformation("🤖 [OLLAMA] Initialized with URL: {Url}, Model: {Model}", 
            _ollamaBaseUrl, _modelName);
    }

    /// <summary>
    /// Ask Ollama AI a question and get a response
    /// </summary>
    /// <param name="prompt">The prompt/question to send to the AI</param>
    /// <returns>The AI's response text, or empty string on error</returns>
    public async Task<string> AskAsync(string prompt)
    {
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
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send POST request to Ollama
            var response = await _httpClient.PostAsync(
                $"{_ollamaBaseUrl}/api/generate",
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
            _logger.LogError(ex, "❌ [OLLAMA] HTTP error during AI request");
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
            var response = await _httpClient.GetAsync($"{_ollamaBaseUrl}/api/tags").ConfigureAwait(false);
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
    /// Response model from Ollama API
    /// </summary>
    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
