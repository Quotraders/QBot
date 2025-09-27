using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zones;

namespace Zones;

public interface IFeatureBus 
{ 
    void Publish(string symbol, DateTime utc, string name, double value); 
}

public sealed class ZoneFeaturePublisher : BackgroundService
{
    private readonly IZoneFeatureSource _zones; 
    private readonly IFeatureBus? _bus; 
    private readonly ILogger<ZoneFeaturePublisher> _log; 
    private readonly TimeSpan _tf; 
    private readonly int _emitEvery;
    
    public ZoneFeaturePublisher(IZoneFeatureSource zones, IFeatureBus? bus, ILogger<ZoneFeaturePublisher> log, IConfiguration cfg)
    { 
        _zones = zones; 
        _bus = bus; 
        _log = log; 
        _tf = TimeSpan.FromMinutes(cfg.GetValue("Zone:BarTimeframeMinutes", 5)); 
        _emitEvery = cfg.GetValue("Zone:EmitFeatureEveryBars", 1); 
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_bus == null)
        {
            _log.LogInformation("[ZONE-FEATURES] No feature bus configured, zone features not published");
            return;
        }

        int k = 0; 
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_tf, stoppingToken).ConfigureAwait(false); 
                k++;
                if (k % _emitEvery != 0) continue;
                
                foreach (var symbol in _tracked)
                {
                    var (dmd, sup, breakout, press) = _zones.GetFeatures(symbol);
                    var now = DateTime.UtcNow;
                    _bus.Publish(symbol, now, "zone.dist_to_demand_atr", dmd);
                    _bus.Publish(symbol, now, "zone.dist_to_supply_atr", sup);
                    _bus.Publish(symbol, now, "zone.breakout_score", breakout);
                    _bus.Publish(symbol, now, "zone.pressure", press);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[ZONE-FEATURES] Error publishing zone features");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private readonly string[] _tracked = new[] { "ES", "NQ" };
}