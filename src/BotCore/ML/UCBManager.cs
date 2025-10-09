using System;
using System.Globalization;
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

        // Structured logging delegates
        private static readonly Action<ILogger, string, Exception?> LogUcbManagerInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, nameof(LogUcbManagerInitialized)),
                "🎯 UCB Manager initialized with service URL: {UcbUrl}");

        private static readonly Action<ILogger, string, double, int, double, Exception?> LogUcbRecommendation =
            LoggerMessage.Define<string, double, int, double>(
                LogLevel.Information,
                new EventId(2, nameof(LogUcbRecommendation)),
                "🧠 [UCB] {Strategy} | Confidence: {Confidence:P1} | Size: {Size} | Risk: {Risk:C}");

        private static readonly Action<ILogger, Exception?> LogUcbTimeout =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(3, nameof(LogUcbTimeout)),
                "⏰ [UCB] Timeout calling Python service - check if FastAPI is running");

        private static readonly Action<ILogger, Exception?> LogUcbHttpError =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(4, nameof(LogUcbHttpError)),
                "🔌 [UCB] HTTP error calling Python service");

        private static readonly Action<ILogger, Exception?> LogUcbUnexpectedError =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(5, nameof(LogUcbUnexpectedError)),
                "❌ [UCB] Unexpected error getting recommendation");

        private static readonly Action<ILogger, string, decimal, Exception?> LogPnlUpdated =
            LoggerMessage.Define<string, decimal>(
                LogLevel.Information,
                new EventId(6, nameof(LogPnlUpdated)),
                "💰 [UCB] Updated P&L for {Strategy}: {PnL:C}");

        private static readonly Action<ILogger, Exception?> LogPnlUpdateTimeout =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(7, nameof(LogPnlUpdateTimeout)),
                "⏰ [UCB] Timeout updating P&L - continuing without update");

        private static readonly Action<ILogger, Exception?> LogPnlUpdateHttpError =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(8, nameof(LogPnlUpdateHttpError)),
                "⚠️ [UCB] HTTP error updating P&L - continuing");

        private static readonly Action<ILogger, Exception?> LogPnlUpdateCancelled =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(9, nameof(LogPnlUpdateCancelled)),
                "⚠️ [UCB] Cancelled updating P&L - continuing");

        private static readonly Action<ILogger, Exception?> LogDailyResetCompleted =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(10, nameof(LogDailyResetCompleted)),
                "🌅 [UCB] Daily stats reset completed");

        private static readonly Action<ILogger, Exception?> LogDailyResetTimeout =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(11, nameof(LogDailyResetTimeout)),
                "⏰ [UCB] Timeout resetting daily stats");

        private static readonly Action<ILogger, Exception?> LogDailyResetHttpError =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(12, nameof(LogDailyResetHttpError)),
                "⚠️ [UCB] HTTP error resetting daily stats");

        private static readonly Action<ILogger, Exception?> LogDailyResetCancelled =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(13, nameof(LogDailyResetCancelled)),
                "⚠️ [UCB] Cancelled resetting daily stats");

        public UcbManager(ILogger<UcbManager> logger)
        {
            _logger = logger;
            var ucbUrl = Environment.GetEnvironmentVariable("UCB_SERVICE_URL") ?? DefaultUcbServiceUrl;
            _http = new HttpClient 
            { 
                BaseAddress = new Uri(ucbUrl),
                Timeout = TimeSpan.FromSeconds(5) // Fast failure if Python service stalls
            };
            LogUcbManagerInitialized(_logger, ucbUrl, null);
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
                    instrument = data.PrimaryInstrument?.ToUpper(CultureInfo.InvariantCulture) ?? "ES" // Default to ES
                };

                using var content = new StringContent(
                    JsonConvert.SerializeObject(marketJson, JsonCfg), 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                using var resp = await _http.PostAsync(new Uri("ucb/recommend", UriKind.Relative), content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                var rec = JsonConvert.DeserializeObject<UcbRecommendation>(text, JsonCfg);
                if (rec == null) throw new InvalidOperationException("Null UcbRecommendation");
                
                LogUcbRecommendation(_logger, rec.Strategy ?? "NONE", rec.Confidence ?? 0, rec.PositionSize, rec.RiskAmount ?? 0, null);
                
                return rec;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                LogUcbTimeout(_logger, null);
                throw new TimeoutException("UCB service timeout", ex);
            }
            catch (HttpRequestException ex)
            {
                LogUcbHttpError(_logger, ex);
                throw;
            }
            catch (Exception ex)
            {
                LogUcbUnexpectedError(_logger, ex);
                throw;
            }
        }

        public async Task UpdatePnLAsync(string strategy, decimal pnl, CancellationToken ct = default)
        {
            try
            {
                var body = new { strategy, pnl };
                using var content = new StringContent(
                    JsonConvert.SerializeObject(body, JsonCfg), 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                using var resp = await _http.PostAsync(new Uri("ucb/update_pnl", UriKind.Relative), content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                
                LogPnlUpdated(_logger, strategy, pnl, null);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                LogPnlUpdateTimeout(_logger, null);
            }
            catch (HttpRequestException ex)
            {
                LogPnlUpdateHttpError(_logger, ex);
            }
            catch (TaskCanceledException ex)
            {
                LogPnlUpdateCancelled(_logger, ex);
            }
        }

        public async Task ResetDailyAsync(CancellationToken ct = default)
        {
            try
            {
                using var content = new StringContent("");
                using var resp = await _http.PostAsync(new Uri("ucb/reset_daily", UriKind.Relative), content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                
                LogDailyResetCompleted(_logger, null);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                LogDailyResetTimeout(_logger, null);
            }
            catch (HttpRequestException ex)
            {
                LogDailyResetHttpError(_logger, ex);
            }
            catch (TaskCanceledException ex)
            {
                LogDailyResetCancelled(_logger, ex);
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
