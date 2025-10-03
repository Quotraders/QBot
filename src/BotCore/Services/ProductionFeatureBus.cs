using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Zones;

namespace BotCore.Services
{
    /// <summary>
    /// Production feature bus for zone telemetry and other feature publishing
    /// </summary>
    public class ProductionFeatureBus : IFeatureBus
    {
        private readonly ILogger<ProductionFeatureBus> _logger;
        private readonly List<FeatureEvent> _recentEvents = new();
        private readonly object _lock = new();
        private const int MaxRecentEvents = 1000;

        public ProductionFeatureBus(ILogger<ProductionFeatureBus> logger)
        {
            _logger = logger;
        }

        public void Publish(string symbol, DateTime utc, string name, decimal value)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                var featureEvent = new FeatureEvent
                {
                    Symbol = symbol,
                    Timestamp = utc,
                    FeatureName = name,
                    Value = value
                };

                lock (_lock)
                {
                    _recentEvents.Add(featureEvent);
                    
                    // Keep only recent events to prevent memory issues
                    if (_recentEvents.Count > MaxRecentEvents)
                    {
                        _recentEvents.RemoveAt(0);
                    }
                }

                // Log feature for observability
                _logger.LogDebug("[FEATURE-BUS] {Symbol} {FeatureName}={Value:F4} @{Timestamp}", 
                    symbol, name, value, utc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FEATURE-BUS] Error publishing feature {FeatureName} for {Symbol}", 
                    name, symbol);
            }
        }

        /// <summary>
        /// Get recent feature events for analysis (optional diagnostic method)
        /// </summary>
        public IReadOnlyList<FeatureEvent> GetRecentEvents()
        {
            lock (_lock)
            {
                return _recentEvents.ToArray();
            }
        }
    }

    /// <summary>
    /// Feature event data structure
    /// </summary>
    public sealed class FeatureEvent
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string FeatureName { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }
}