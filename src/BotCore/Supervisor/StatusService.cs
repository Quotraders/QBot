#nullable enable
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Linq;

// NOTE: Not used by Orchestrator; OrchestratorAgent injects SupervisorAgent.StatusService.
// This BotCore.Supervisor.StatusService is for other agents or legacy paths.
namespace BotCore.Supervisor
{
    public sealed class StatusService(ILogger<StatusService> log)
    {
        private readonly ILogger<StatusService> _log = log;
        private readonly ConcurrentDictionary<string, object> _vals = new();
        private DateTimeOffset _lastBeat = DateTimeOffset.MinValue;
        private DateTimeOffset _lastEmit = DateTimeOffset.MinValue;
        private string _lastSig = string.Empty;

        public long AccountId { get; set; }
        public Dictionary<string, string> Contracts { get; set; } = [];

        public void Set(string key, object value) => _vals[key] = value;
        public T? Get<T>(string key) => _vals.TryGetValue(key, out var v) ? (T?)v : default;

        private static bool Concise() => (Environment.GetEnvironmentVariable("APP_CONCISE_CONSOLE") ?? "true").Trim().ToLowerInvariant() is "1" or "true" or "yes";
        private static bool ShowStatusTick() => (Environment.GetEnvironmentVariable("APP_SHOW_STATUS_TICK") ?? "false").Trim().ToLowerInvariant() is "1" or "true" or "yes";
        private static bool QuietJson() => (Environment.GetEnvironmentVariable("QUIET_JSON_STATUS") ?? "false").Trim().ToLowerInvariant() is "1" or "true" or "yes";

        public void Heartbeat()
        {
            var now = DateTimeOffset.UtcNow;
            if (QuietJson()) return;
            // In concise mode, status tick is opt-in
            if (Concise() && !ShowStatusTick()) return;
            if (!Concise())
            {
                if (now - _lastBeat < TimeSpan.FromSeconds(5)) return; // throttle
            }
            _lastBeat = now;

            var snapshot = new
            {
                whenUtc = now,
                accountId = AccountId,
                contracts = Contracts,
                userHub = Get<string>("user.state"),
                marketHub = Get<string>("market.state"),
                lastTrade = Get<DateTimeOffset?>("last.trade"),
                lastQuote = Get<DateTimeOffset?>("last.quote"),
                strategies = Get<object?>("strategies"),
                openOrders = Get<object?>("orders.open"),
                risk = Get<object?>("risk.state")
            };
            var json = JsonSerializer.Serialize(snapshot);

            // Stable signature excludes whenUtc so we don't emit just because time advanced
            var sigObj = new
            {
                accountId = AccountId,
                contracts = Contracts,
                userHub = Get<string>("user.state"),
                marketHub = Get<string>("market.state"),
                lastTrade = Get<DateTimeOffset?>("last.trade"),
                lastQuote = Get<DateTimeOffset?>("last.quote"),
                strategies = Get<object?>("strategies"),
                openOrders = Get<object?>("orders.open"),
                risk = Get<object?>("risk.state")
            };
            var sig = JsonSerializer.Serialize(sigObj);

            if (Concise())
            {
                // Emit only when state signature changed or every 60s
                if (sig != _lastSig || (now - _lastEmit) >= TimeSpan.FromSeconds(60))
                {
                    _lastSig = sig;
                    _lastJson = json;
                    _lastEmit = now;
                    _log.LogInformation("BOT STATUS => {Json}", json);
                }
                return;
            }

            _log.LogInformation("BOT STATUS => {Json}", json);
        }
    }
}
