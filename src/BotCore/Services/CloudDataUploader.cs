using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BotCore.Services;

/// <summary>
/// Service for uploading trading data to cloud storage
/// </summary>
public interface ICloudDataUploader
{
    Task<bool> UploadTradeDataAsync(object tradeData);
    Task<bool> UploadMarketDataAsync(object marketData);
}

/// <summary>
/// Implementation of cloud data uploader service
/// </summary>
public class CloudDataUploader : ICloudDataUploader
{
    private static readonly JsonSerializerOptions s_jsonOptionsCamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ILogger<CloudDataUploader> _logger;

    public CloudDataUploader(ILogger<CloudDataUploader> logger)
    {
        _logger = logger;
    }

    public async Task<bool> UploadTradeDataAsync(object tradeData)
    {
        try
        {
            _logger.LogInformation("Uploading trade data to cloud storage");
            
            // Serialize trade data to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(tradeData, s_jsonOptionsCamelCase);
            
            // Upload to cloud storage (Azure Blob, AWS S3, etc.)
            using var httpClient = new HttpClient();
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var cloudEndpoint = Environment.GetEnvironmentVariable("CLOUD_UPLOAD_ENDPOINT") ?? "https://api.cloudprovider.com/upload/trades";
            var response = await httpClient.PostAsync(cloudEndpoint, content).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("✅ Trade data uploaded successfully to cloud storage");
                return true;
            }
            else
            {
                _logger.LogWarning("❌ Cloud upload failed with status: {StatusCode}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload trade data");
            return false;
        }
    }

    public async Task<bool> UploadMarketDataAsync(object marketData)
    {
        try
        {
            _logger.LogInformation("Uploading market data to cloud storage");
            
            // Serialize market data to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(marketData, s_jsonOptionsCamelCase);
            
            // Compress data for efficient upload
            var compressedData = CompressData(json);
            
            // Upload to cloud storage
            using var httpClient = new HttpClient();
            var content = new ByteArrayContent(compressedData);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/gzip");
            
            var cloudEndpoint = Environment.GetEnvironmentVariable("CLOUD_UPLOAD_ENDPOINT") ?? "https://api.cloudprovider.com/upload/market";
            var response = await httpClient.PostAsync(cloudEndpoint, content).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("✅ Market data uploaded successfully to cloud storage");
                return true;
            }
            else
            {
                _logger.LogWarning("❌ Market data cloud upload failed with status: {StatusCode}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload market data");
            return false;
        }
    }
    
    private static byte[] CompressData(string data)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
