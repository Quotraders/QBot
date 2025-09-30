using System;
using System.Collections.Generic;
using BotCore.Models;

namespace BotCore.Market
{
    public sealed class BarPyramid
    {
        public readonly BarAggregator M1 = new(TimeSpan.FromMinutes(1));
        public readonly BarAggregator M5 = new(TimeSpan.FromMinutes(5));
        public readonly BarAggregator M30 = new(TimeSpan.FromMinutes(30));

        public BarPyramid()
        {
            M1.OnBarClosed += (cid, b1) =>
            {
                // collapse 1m into 5m / 30m by re-feeding synthetic trades at bar close
                M5.OnTrade(cid, b1.End.AddMilliseconds(-1), b1.Close, (long)Math.Max(1, b1.Volume));
                M30.OnTrade(cid, b1.End.AddMilliseconds(-1), b1.Close, (long)Math.Max(1, b1.Volume));
            };
        }

        /// <summary>
        /// Seed pyramid with historical bars to avoid waiting for live collapse
        /// PRODUCTION REQUIREMENT: Support historical bar propagation for trading readiness
        /// </summary>
        public void SeedFromHistoricalBars(string contractId, IEnumerable<BotCore.Models.Bar> historicalBars)
        {
            var marketBars = new List<Bar>();
            var m30Bars = new List<Bar>();
            
            foreach (var bar in historicalBars)
            {
                // Convert historical bar to Market.Bar format
                var marketBar = new Bar(
                    DateTime.UnixEpoch.AddMilliseconds(bar.Ts),
                    DateTime.UnixEpoch.AddMilliseconds(bar.Ts).AddMinutes(1), // Assume 1-minute bars
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume
                );

                marketBars.Add(marketBar);
                
                // Collect M30 bars for direct seeding (optional enhancement)
                if (ShouldSeedM30Directly(bar))
                {
                    var m30Bar = ConvertToM30Bar(marketBar);
                    m30Bars.Add(m30Bar);
                }
            }

            // Seed M1 with all bars - this will trigger propagation to M5 and M30
            M1.Seed(contractId, marketBars);
            
            // Optionally seed M30 directly for faster warm-up
            if (m30Bars.Count > 0)
            {
                M30.Seed(contractId, m30Bars);
            }
        }

        /// <summary>
        /// Determine if we should seed M30 directly for faster warm-up
        /// </summary>
        private bool ShouldSeedM30Directly(BotCore.Models.Bar bar)
        {
            // Seed every 30th bar as M30, or use bar timestamp to detect actual 30m boundaries
            var barTime = DateTime.UnixEpoch.AddMilliseconds(bar.Ts);
            return barTime.Minute % 30 == 0;
        }

        /// <summary>
        /// Convert 1-minute bar to 30-minute bar representation
        /// </summary>
        private Bar ConvertToM30Bar(Bar minuteBar)
        {
            var m30Start = new DateTime(
                minuteBar.Start.Year,
                minuteBar.Start.Month,
                minuteBar.Start.Day,
                minuteBar.Start.Hour,
                (minuteBar.Start.Minute / 30) * 30,
                0);
            
            return new Bar(
                m30Start,
                m30Start.AddMinutes(30),
                minuteBar.Open,
                minuteBar.High,
                minuteBar.Low,
                minuteBar.Close,
                minuteBar.Volume
            );
        }
    }
}
