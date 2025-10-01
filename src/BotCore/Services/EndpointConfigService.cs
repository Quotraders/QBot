using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Production implementation of endpoint configuration
    /// Replaces hardcoded API endpoints and connection settings
    /// </summary>
    public class EndpointConfigService : IEndpointConfig
    {
        private readonly IConfiguration _config;

        // Default endpoint configuration constants
        private const int DefaultConnectionTimeoutSeconds = 30;
        private const int DefaultRequestTimeoutSeconds = 60;
        private const int DefaultMaxRetryAttempts = 3;

        public EndpointConfigService(IConfiguration config)
        {
            _config = config;
        }

        public Uri GetTopstepXApiBaseUrl() => 
            new Uri(_config.GetValue("Endpoints:TopstepXApiBaseUrl", "https://api.topstepx.com"));

        public Uri GetTopstepXWebSocketUrl() => 
            new Uri(_config.GetValue("Endpoints:TopstepXWebSocketUrl", "wss://api.topstepx.com/ws"));

        public string GetMLServiceEndpoint() => 
            _config.GetValue("Endpoints:MLServiceEndpoint", "http://localhost:8080");

        public string GetDataFeedEndpoint() => 
            _config.GetValue("Endpoints:DataFeedEndpoint", "https://datafeed.tradingbot.local");

        public string GetRiskServiceEndpoint() => 
            _config.GetValue("Endpoints:RiskServiceEndpoint", "http://localhost:9000");

        public string GetCloudStorageEndpoint() => 
            _config.GetValue("Endpoints:CloudStorageEndpoint", "https://storage.tradingbot.local");

        public int GetConnectionTimeoutSeconds() => 
            _config.GetValue("Endpoints:ConnectionTimeoutSeconds", DefaultConnectionTimeoutSeconds);

        public int GetRequestTimeoutSeconds() => 
            _config.GetValue("Endpoints:RequestTimeoutSeconds", DefaultRequestTimeoutSeconds);

        public int GetMaxRetryAttempts() => 
            _config.GetValue("Endpoints:MaxRetryAttempts", DefaultMaxRetryAttempts);

        public bool UseSecureConnections() => 
            _config.GetValue("Endpoints:UseSecureConnections", true);
    }
}