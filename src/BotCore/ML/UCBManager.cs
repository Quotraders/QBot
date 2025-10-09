using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BotCore.ML
{
    /// <summary>
    /// UCB Manager for communicating with Python FastAPI UCB service
    /// Production-ready with proper timeouts and error handling
    /// </summary>
    public class UcbManager : IDisposable
    {
        private const string DefaultUcbServiceUrl = "http://localhost:8001";
        private readonly HttpClient _http;
        private readonly ILogger<UcbManager> _logger;
        private static readonly JsonSerializerSettings JsonCfg = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public UcbManager(ILogger<UcbManager> logger)
        {
            _logger = logger;
            var ucbUrl = Environment.GetEnvironmentVariable("UCB_SERVICE_URL") ?? DefaultUcbServiceUrl;
            _http = new HttpClient 
            { 
                BaseAddress = new Uri(ucbUrl),
                Timeout = TimeSpan.FromSeconds(5) // Fast failure if Python service stalls
            };
            _logger.LogInformation("🎯 UCB Manager initialized with service URL: {UcbUrl}", ucbUrl);
        }

        public async Task<UcbRecommendation> GetRecommendationAsync(MarketData data, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            
            try
            {
                var marketJson = new
                {
                    es_price = data.ESPrice,
                    nq_price = data.NQPrice,
                    es_volume = data.ESVolume,
                    nq_volume = data.NQVolume,
                    es_atr = Math.Clamp(data.ESAtr, 0.25m, 100m), // Sanity bounds
                    nq_atr = Math.Clamp(data.NQAtr, 0.5m, 100m),  // Sanity bounds
                    vix = Math.Clamp(data.VIX, 5m, 100m),           // Sanity bounds
                    tick = Math.Clamp(data.TICK, -3000, 3000),      // Sanity bounds
                    add = Math.Clamp(data.ADD, -2000, 2000),        // Sanity bounds
                    correlation = Math.Clamp(data.Correlation, -1m, 1m), // Correlation bounds
                    rsi_es = Math.Clamp(data.RsiES, 0m, 100m),
                    rsi_nq = Math.Clamp(data.RsiNQ, 0m, 100m),
                    instrument = data.PrimaryInstrument?.ToUpper() ?? "ES" // Default to ES
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(marketJson, JsonCfg), 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                using var resp = await _http.PostAsync("ucb/recommend", content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                var rec = JsonConvert.DeserializeObject<UcbRecommendation>(text, JsonCfg);
                if (rec == null) throw new InvalidOperationException("Null UcbRecommendation");
                
                _logger.LogInformation("🧠 [UCB] {Strategy} | Confidence: {Confidence:P1} | Size: {Size} | Risk: {Risk:C}", 
                    rec.Strategy ?? "NONE", rec.Confidence ?? 0, rec.PositionSize, rec.RiskAmount ?? 0);
                
                return rec;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError("⏰ [UCB] Timeout calling Python service - check if FastAPI is running");
                throw new TimeoutException("UCB service timeout", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "🔌 [UCB] HTTP error calling Python service");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [UCB] Unexpected error getting recommendation");
                throw;
            }
        }

        public async Task UpdatePnLAsync(string strategy, decimal pnl, CancellationToken ct = default)
        {
            try
            {
                var body = new { strategy, pnl };
                var content = new StringContent(
                    JsonConvert.SerializeObject(body, JsonCfg), 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                using var resp = await _http.PostAsync("ucb/update_pnl", content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                
                _logger.LogInformation("💰 [UCB] Updated P&L for {Strategy}: {PnL:C}", strategy, pnl);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning("⏰ [UCB] Timeout updating P&L - continuing without update");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "⚠️ [UCB] HTTP error updating P&L - continuing");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "⚠️ [UCB] Cancelled updating P&L - continuing");
            }
        }

        public async Task ResetDailyAsync(CancellationToken ct = default)
        {
            try
            {
                using var resp = await _http.PostAsync("ucb/reset_daily", new StringContent(""), ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                
                _logger.LogInformation("🌅 [UCB] Daily stats reset completed");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning("⏰ [UCB] Timeout resetting daily stats");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "⚠️ [UCB] HTTP error resetting daily stats");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "⚠️ [UCB] Cancelled resetting daily stats");
            }
        }

        private bool _disposed;

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
                    _http?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    public sealed class UcbRecommendation
    {
        [JsonProperty("trade")] public bool Trade { get; set; }
        [JsonProperty("strategy")] public string? Strategy { get; set; }
        [JsonProperty("confidence")] public double? Confidence { get; set; }
        [JsonProperty("position_size")] public int PositionSize { get; set; }
        [JsonProperty("ucb_score")] public double? UcbScore { get; set; }
        [JsonProperty("risk_amount")] public double? RiskAmount { get; set; }
        [JsonProperty("current_drawdown")] public double? CurrentDrawdown { get; set; }
        [JsonProperty("daily_pnl")] public double? DailyPnL { get; set; }
        [JsonProperty("warning")] public string? Warning { get; set; }
        [JsonProperty("reason")] public string? Reason { get; set; }
    }

    public class MarketData
    {
        public decimal ESPrice { get; set; }
        public decimal NQPrice { get; set; }
        public long ESVolume { get; set; }
        public long NQVolume { get; set; }
        public decimal ESAtr { get; set; }
        public decimal NQAtr { get; set; }
        public decimal VIX { get; set; }
        public int TICK { get; set; }
        public int ADD { get; set; }
        public decimal Correlation { get; set; }
        public decimal RsiES { get; set; }
        public decimal RsiNQ { get; set; }
        public string? PrimaryInstrument { get; set; } = "ES";
    }
}
