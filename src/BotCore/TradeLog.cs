using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace BotCore
{
    public static class TradeLog
    {
        public record ContractSpec(string Sym, int Decimals, decimal TickSize, decimal BigPointValue);
        public static readonly IReadOnlyDictionary<string, ContractSpec> Spec = new Dictionary<string, ContractSpec>
        {
            ["ES"] = new("ES", 2, 0.25m, 50m),
            ["NQ"] = new("NQ", 2, 0.25m, 20m)
        };

        // LoggerMessage delegates for high-performance logging
        private static readonly Action<ILogger, string, Exception?> LogChange_Internal =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(1001, nameof(LogChange)), "{line}");

        private static readonly Action<ILogger, string, string, string, Exception?> SessionLog =
            LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(1002, nameof(Session)), 
                "SESSION mode={mode} acct={acct} syms={syms}");

        private static readonly Action<ILogger, string, string, string, string, string, Exception?> SignalLog =
            LoggerMessage.Define<string, string, string, string, string>(LogLevel.Information, new EventId(1003, nameof(Signal)), 
                "[{sym}] SIGNAL {strat} {side} qty={qty} entry={entry}");

        private static readonly Action<ILogger, string, string, string, string, Exception?> OrderNewLog =
            LoggerMessage.Define<string, string, string, string>(LogLevel.Information, new EventId(1004, nameof(OrderNew)), 
                "[{sym}] ORDER NEW {side} qty={qty} px={px}");

        private static readonly Action<ILogger, string, string, string, string, string, Exception?> FillLog =
            LoggerMessage.Define<string, string, string, string, string>(LogLevel.Information, new EventId(1005, nameof(Fill)), 
                "[{sym}] FILL {side} qty={qty} px={px} pos={pos}");

        private static readonly Action<ILogger, string, string, Exception?> StopNewLog =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1006, nameof(StopNew)), 
                "[{sym}] STOP NEW {stop}");

        private static readonly Action<ILogger, string, string, string, Exception?> StopMoveLog =
            LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(1007, nameof(StopMove)), 
                "[{sym}] STOP MOVE {stop} {reason}");

        private static readonly Action<ILogger, string, string, string, Exception?> StopHitLog =
            LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(1008, nameof(StopHit)), 
                "[{sym}] STOP HIT qty={qty} px={px}");

        private static readonly Action<ILogger, string, string, Exception?> TargetNewLog =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1009, nameof(TargetNew)), 
                "[{sym}] TARGET NEW {t1}");

        private static readonly Action<ILogger, string, string, string, Exception?> ExitLog =
            LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(1010, nameof(Exit)), 
                "[{sym}] EXIT qty={qty} px={px}");

        private static readonly Action<ILogger, string, string, string, Exception?> HeartbeatLog =
            LoggerMessage.Define<string, string, string>(LogLevel.Information, new EventId(1011, nameof(Heartbeat)), 
                "HEARTBEAT dailyPnL={d} maxDailyLoss={m} remaining={r}");

        static decimal RoundToTick(decimal px, decimal tick) =>
            (tick <= 0m) ? px : System.Math.Round(px / tick, 0, System.MidpointRounding.AwayFromZero) * tick;

        static string Fpx(string sym, decimal px)
        {
            if (!Spec.TryGetValue(sym, out var c)) c = new ContractSpec(sym, 2, 0.25m, 1m);
            return RoundToTick(px, c.TickSize).ToString($"F{c.Decimals}", CultureInfo.InvariantCulture);
        }
        static string Fpn(decimal v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        static readonly ConcurrentDictionary<string, string> _last = new();
        static void LogChange(ILogger log, string key, string line, LogLevel lvl = LogLevel.Information)
        {
            if (_last.TryGetValue(key, out var prev) && prev == line) return;
            _last[key] = line;
            LogChange_Internal(log, line, null);
        }

        public static void Session(ILogger log, string mode, string acct, string[] syms) =>
            SessionLog(log, mode, acct, string.Join(",", syms), null);

        public static void Signal(ILogger log, string sym, string strat, string side, int qty, decimal entry, decimal stop, decimal target, string reason, string tag)
            => SignalLog(log, sym, strat, side, qty.ToString(), Fpx(sym, entry), null);

        public static void OrderNew(ILogger log, string sym, string side, int qty, decimal px, string tag)
            => OrderNewLog(log, sym, side, qty.ToString(), Fpx(sym, px), null);

        public static void Fill(ILogger log, string sym, string side, int qty, decimal px, int pos, decimal avg, decimal mark, decimal uPnL, decimal rPnL, string tag)
            => FillLog(log, sym, side, qty.ToString(), Fpx(sym, px), pos.ToString(), null);

        public static void StopNew(ILogger log, string sym, decimal stop, string tag)
            => StopNewLog(log, sym, Fpx(sym, stop), null);

        public static void StopMove(ILogger log, string sym, decimal stop, string reason, string tag)
            => StopMoveLog(log, sym, Fpx(sym, stop), reason, null);

        public static void StopHit(ILogger log, string sym, int qty, decimal px, int pos, decimal rPnL, string tag)
            => StopHitLog(log, sym, qty.ToString(), Fpx(sym, px), null);

        public static void TargetNew(ILogger log, string sym, decimal t1, string tag)
            => TargetNewLog(log, sym, Fpx(sym, t1), null);

        public static void Exit(ILogger log, string sym, int qty, decimal px, int pos, decimal rPnL, string tag)
            => ExitLog(log, sym, qty.ToString(), Fpx(sym, px), null);

        public static void Position(ILogger log, string sym, int pos, decimal avg, decimal mark, decimal uPnL, decimal rPnL)
            => LogChange(log, $"pos/{sym}",
                $"[{sym}] POS {pos:+#;-#;0} @ {Fpx(sym, avg)} mark={Fpx(sym, mark)} uPnL={Fpn(uPnL)} rPnL={Fpn(rPnL)}");

        public static void Skip(ILogger log, string sym, string code, string detail) =>
            LogChange(log, $"skip/{sym}/{code}", $"[{sym}] SKIP {code} {detail}");

        public static void SpreadGate(ILogger log, string sym, bool closed, int ticks, int allowTicks)
            => LogChange(log, $"gate/{sym}", $"[{sym}] GATE spread={(closed ? "CLOSED" : "OPEN")} {ticks}t allow={allowTicks}t");

        public static void Heartbeat(ILogger log, decimal dailyPnL, decimal maxDD, decimal remaining, string exposure)
            => HeartbeatLog(log, Fpn(dailyPnL), Fpn(maxDD), Fpn(remaining), null);
    }
}
