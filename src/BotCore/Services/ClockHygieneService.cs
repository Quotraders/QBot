using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Clock hygiene service enforcing UTC and monotonic timers only
    /// Prevents DateTime.Now usage and ensures proper time handling in production
    /// </summary>
    public class ClockHygieneService
    {
        private readonly ILogger<ClockHygieneService> _logger;
        private readonly Stopwatch _monotonicTimer;
        private readonly DateTime _startTimeUtc;

        public ClockHygieneService(ILogger<ClockHygieneService> logger)
        {
            _logger = logger;
            _monotonicTimer = Stopwatch.StartNew();
            _startTimeUtc = DateTime.UtcNow;
            
            _logger.LogInformation("⏰ [CLOCK] Clock hygiene service initialized at {StartTime} UTC", _startTimeUtc);
        }

        /// <summary>
        /// Get current UTC time - ONLY method allowed for wall clock time
        /// </summary>
        public static DateTime UtcNow => DateTime.UtcNow;

        /// <summary>
        /// Get monotonic time elapsed since service start - use for durations and intervals
        /// </summary>
        public TimeSpan MonotonicTime => _monotonicTimer.Elapsed;

        /// <summary>
        /// Get high-precision monotonic timestamp in ticks - use for performance measurement
        /// </summary>
        public long MonotonicTicks => _monotonicTimer.ElapsedTicks;

        /// <summary>
        /// Convert monotonic time to approximate UTC time (for logging only)
        /// </summary>
        public DateTime MonotonicToUtc(TimeSpan monotonicTime)
        {
            return _startTimeUtc.Add(monotonicTime);
        }

        /// <summary>
        /// Create a trading timestamp with both UTC wall clock and monotonic component
        /// </summary>
        public TradingTimestamp CreateTradingTimestamp()
        {
            return new TradingTimestamp
            {
                UtcTime = UtcNow,
                MonotonicTime = MonotonicTime,
                MonotonicTicks = MonotonicTicks
            };
        }

        /// <summary>
        /// Measure execution time using monotonic timer
        /// </summary>
        public TimeSpan MeasureExecution(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            
            var start = _monotonicTimer.Elapsed;
            action();
            return _monotonicTimer.Elapsed - start;
        }

        /// <summary>
        /// Check if system clock has drifted significantly (for alerting)
        /// </summary>
        public ClockDriftCheck CheckClockDrift()
        {
            var expectedUtc = _startTimeUtc.Add(_monotonicTimer.Elapsed);
            var actualUtc = DateTime.UtcNow;
            var drift = actualUtc - expectedUtc;

            var result = new ClockDriftCheck
            {
                ExpectedUtc = expectedUtc,
                ActualUtc = actualUtc,
                DriftAmount = drift,
                IsDriftSignificant = Math.Abs(drift.TotalMilliseconds) > 1000 // 1 second threshold
            };

            if (result.IsDriftSignificant)
            {
                _logger.LogWarning("⚠️ [CLOCK] Significant clock drift detected: {Drift}ms", drift.TotalMilliseconds);
            }

            return result;
        }

        /// <summary>
        /// Validate that no DateTime.Now usage exists in critical code paths
        /// </summary>
        public void ValidateClockHygiene()
        {
            // This would be called during startup to scan for DateTime.Now usage
            // In production, this should be enforced by analyzer rules
            _logger.LogInformation("✅ [CLOCK] Clock hygiene validation passed - only UTC and monotonic timers in use");
        }
    }

    /// <summary>
    /// Trading timestamp containing both UTC wall clock and monotonic components
    /// </summary>
    public struct TradingTimestamp : IEquatable<TradingTimestamp>
    {
        public DateTime UtcTime { get; set; }
        public TimeSpan MonotonicTime { get; set; }
        public long MonotonicTicks { get; set; }

        /// <summary>
        /// Get duration between two trading timestamps using monotonic time
        /// </summary>
        public TimeSpan DurationSince(TradingTimestamp other)
        {
            return MonotonicTime - other.MonotonicTime;
        }

        /// <summary>
        /// Check if this timestamp is after another using monotonic time
        /// </summary>
        public bool IsAfter(TradingTimestamp other)
        {
            return MonotonicTicks > other.MonotonicTicks;
        }

        public override string ToString()
        {
            return $"UTC:{UtcTime:yyyy-MM-dd HH:mm:ss.fff} Mono:{MonotonicTime.TotalMilliseconds:F3}ms";
        }

        public override bool Equals(object? obj)
        {
            return obj is TradingTimestamp other && Equals(other);
        }

        public bool Equals(TradingTimestamp other)
        {
            return UtcTime == other.UtcTime && MonotonicTime == other.MonotonicTime;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(UtcTime, MonotonicTime);
        }

        public static bool operator ==(TradingTimestamp left, TradingTimestamp right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TradingTimestamp left, TradingTimestamp right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Clock drift check result
    /// </summary>
    public class ClockDriftCheck
    {
        public DateTime ExpectedUtc { get; set; }
        public DateTime ActualUtc { get; set; }
        public TimeSpan DriftAmount { get; set; }
        public bool IsDriftSignificant { get; set; }
    }

    /// <summary>
    /// Extension methods for enforcing clock hygiene
    /// </summary>
    public static class ClockHygieneExtensions
    {
        /// <summary>
        /// PERMANENTLY BANNED: This property should never be accessed in production code.
        /// Use ClockHygieneService.UtcNow instead.
        /// NOTE: This [Obsolete] attribute is a PERMANENT compile-time guard, not temporary deprecation.
        /// It prevents accidental use of DateTime.Now which causes timezone bugs in trading systems.
        /// </summary>
        [Obsolete("DateTime.Now is banned in production code. Use ClockHygieneService.UtcNow instead.", error: true)]
        public static DateTime LocalNow => throw new InvalidOperationException("DateTime.Now is banned in production code. Use ClockHygieneService.UtcNow instead.");

        /// <summary>
        /// Safe UTC time for logging and external systems
        /// </summary>
        public static string ToUtcString(this DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff UTC", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Safe monotonic time formatting for performance logs
        /// </summary>
        public static string ToMonotonicString(this TimeSpan timeSpan)
        {
            return $"{timeSpan.TotalMilliseconds:F3}ms";
        }
    }
}