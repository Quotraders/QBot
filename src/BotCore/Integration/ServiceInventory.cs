using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BotCore.Integration;

/// <summary>
/// Service inventory for tracking all services that touch bars, features, and decisions
/// Provides comprehensive audit and dependency mapping for production deployment
/// </summary>
public sealed class ServiceInventory
{
    private readonly ILogger<ServiceInventory> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    // LoggerMessage delegates for high-performance logging
    private static readonly Action<ILogger, int, int, Exception?> LogServiceInventoryGenerated =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, nameof(GenerateInventoryReport)),
            "Service inventory generated with {CategoryCount} categories and {ServiceCount} services");
    
    public ServiceInventory(ILogger<ServiceInventory> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    /// <summary>
    /// Enumerate all services touching bars, features, and decisions
    /// </summary>
    public ServiceInventoryReport GenerateInventoryReport()
    {
        var report = new ServiceInventoryReport
        {
            GeneratedAt = DateTime.UtcNow,
            Services = new Dictionary<string, List<ServiceInfo>>()
        };
        
        // Market Data Services
        report.Services["MarketData"] = new List<ServiceInfo>
        {
            new() { Name = "EnhancedMarketDataFlowService", Type = "BotCore.Services.EnhancedMarketDataFlowService", IsRegistered = IsServiceRegistered("BotCore.Services.EnhancedMarketDataFlowService") },
            new() { Name = "MarketDataStalenessService", Type = "BotCore.Services.MarketDataStalenessService", IsRegistered = IsServiceRegistered("BotCore.Services.MarketDataStalenessService") },
            new() { Name = "ZoneMarketDataBridge", Type = "BotCore.Services.ZoneMarketDataBridge", IsRegistered = IsServiceRegistered("BotCore.Services.ZoneMarketDataBridge") }
        };
        
        // Bar Processing Services
        report.Services["BarProcessing"] = new List<ServiceInfo>
        {
            new() { Name = "BarAggregator", Type = "BotCore.Market.BarAggregator", IsRegistered = IsServiceRegistered("BotCore.Market.BarAggregator") },
            new() { Name = "BarPyramid", Type = "BotCore.Market.BarPyramid", IsRegistered = IsServiceRegistered("BotCore.Market.BarPyramid") },
            new() { Name = "TradingSystemBarConsumer", Type = "BotCore.Services.TradingSystemBarConsumer", IsRegistered = IsServiceRegistered("BotCore.Services.TradingSystemBarConsumer") }
        };
        
        // Zone Services
        report.Services["ZoneServices"] = new List<ServiceInfo>
        {
            new() { Name = "ZoneService", Type = "Zones.ZoneServiceProduction", IsRegistered = IsServiceRegistered("Zones.IZoneService") },
            new() { Name = "ZoneFeaturePublisher", Type = "Zones.ZoneFeaturePublisher", IsRegistered = IsServiceRegistered("Zones.ZoneFeaturePublisher") },
            new() { Name = "ZoneFeatureSource", Type = "Zones.IZoneFeatureSource", IsRegistered = IsServiceRegistered("Zones.IZoneFeatureSource") }
        };
        
        // Pattern Engine Services
        report.Services["PatternEngine"] = new List<ServiceInfo>
        {
            new() { Name = "PatternEngine", Type = "BotCore.Patterns.PatternEngine", IsRegistered = IsServiceRegistered("BotCore.Patterns.PatternEngine") },
            new() { Name = "PatternDetectors", Type = "IEnumerable<BotCore.Patterns.IPatternDetector>", IsRegistered = true }
        };
        
        // DSL Engine Services  
        report.Services["DslEngine"] = new List<ServiceInfo>
        {
            new() { Name = "StrategyKnowledgeGraph", Type = "BotCore.StrategyDsl.IStrategyKnowledgeGraph", IsRegistered = IsServiceRegistered("BotCore.StrategyDsl.IStrategyKnowledgeGraph") },
            new() { Name = "SimpleDslLoader", Type = "BotCore.StrategyDsl.SimpleDslLoader", IsRegistered = true }, // Static class
            new() { Name = "ProductionFeatureProbe", Type = "BotCore.StrategyDsl.ProductionFeatureProbe", IsRegistered = IsServiceRegistered("BotCore.StrategyDsl.IFeatureProbe") }
        };
        
        // Feature Bus Services
        report.Services["FeatureBus"] = new List<ServiceInfo>
        {
            new() { Name = "ProductionFeatureBus", Type = "BotCore.Services.ProductionFeatureBus", IsRegistered = IsServiceRegistered("Zones.IFeatureBus") },
            new() { Name = "FeatureBusAdapter", Type = "BotCore.Fusion.FeatureBusAdapter", IsRegistered = IsServiceRegistered("BotCore.Fusion.IFeatureBusWithProbe") }
        };
        
        // Fusion Coordinator Services
        report.Services["FusionCoordinator"] = new List<ServiceInfo>
        {
            new() { Name = "DecisionFusionCoordinator", Type = "BotCore.Fusion.DecisionFusionCoordinator", IsRegistered = IsServiceRegistered("BotCore.Fusion.DecisionFusionCoordinator") }
        };
        
        // Risk Manager Services
        report.Services["RiskManager"] = new List<ServiceInfo>
        {
            new() { Name = "EnhancedRiskManager", Type = "BotCore.Services.EnhancedRiskManager", IsRegistered = IsServiceRegistered("BotCore.Services.EnhancedRiskManager") },
            new() { Name = "ES_NQ_PortfolioHeatManager", Type = "BotCore.Services.ES_NQ_PortfolioHeatManager", IsRegistered = IsServiceRegistered("BotCore.Services.IPortfolioHeatManager") }
        };
        
        // Metrics Services
        report.Services["MetricsService"] = new List<ServiceInfo>
        {
            new() { Name = "RealTradingMetricsService", Type = "BotCore.Services.RealTradingMetricsService", IsRegistered = IsServiceRegistered("BotCore.Services.RealTradingMetricsService") }
        };
        
        LogServiceInventoryGenerated(_logger, report.Services.Count, report.Services.Values.Sum(s => s.Count), null);
            
        return report;
    }
    
    /// <summary>
    /// Check if a service type is registered in the DI container
    /// </summary>
    private bool IsServiceRegistered(string serviceTypeName)
    {
        try
        {
            // Attempt to resolve the service type by name
            var serviceType = Type.GetType(serviceTypeName);
            if (serviceType == null)
            {
                return false;
            }
            
            // Check if service is registered in DI container
            var service = _serviceProvider.GetService(serviceType);
            return service != null;
        }
        catch (TypeLoadException)
        {
            // Type could not be loaded
            return false;
        }
        catch (TargetInvocationException)
        {
            // Error during service instantiation
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid type name
            return false;
        }
    }
    
    /// <summary>
    /// Generate a comprehensive audit log of the service inventory
    /// </summary>
    public string GenerateAuditLog()
    {
        var report = GenerateInventoryReport();
        var audit = new StringBuilder();
        
        audit.AppendLine("=== PRODUCTION SERVICE INVENTORY AUDIT ===");
        audit.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        audit.AppendLine($"Total Categories: {report.Services.Count}");
        audit.AppendLine($"Total Services: {report.Services.Values.Sum(s => s.Count)}");
        audit.AppendLine();
        
        foreach (var category in report.Services)
        {
            audit.AppendLine($"[{category.Key}]");
            foreach (var service in category.Value)
            {
                var status = service.IsRegistered ? "✅ REGISTERED" : "❌ NOT REGISTERED";
                audit.AppendLine($"  {service.Name}: {status}");
                audit.AppendLine($"    Type: {service.Type}");
            }
            audit.AppendLine();
        }
        
        return audit.ToString();
    }
}

/// <summary>
/// Service inventory report data structure
/// </summary>
public sealed class ServiceInventoryReport
{
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, List<ServiceInfo>> Services { get; set; } = new();
}

/// <summary>
/// Individual service information
/// </summary>
public sealed class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRegistered { get; set; }
}