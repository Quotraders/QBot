using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using BotCore.Patterns;
using BotCore.Patterns.Detectors;
using BotCore.StrategyDsl;
using BotCore.Strategy;
using BotCore.Fusion;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Zones;

namespace BotCore.Extensions;

/// <summary>
/// Service registration extensions for pattern recognition and strategy DSL system
/// Provides production-ready dependency injection configuration for all pattern and strategy components
/// </summary>
public static class PatternAndStrategyServiceExtensions
{
    /// <summary>
    /// Register pattern recognition services
    /// Adds pattern detectors, pattern engine, and feature bus integration
    /// </summary>
    public static IServiceCollection AddPatternRecognitionServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        
        // Register pattern engine configuration
        services.Configure<PatternEngineOptions>(
            configuration.GetSection("PatternEngine"));

        // Register pattern detectors as collections that will create instances for all pattern types
        services.AddSingleton<IEnumerable<IPatternDetector>>(provider =>
        {
            var detectors = new List<IPatternDetector>();
            
            // Add candlestick pattern detectors for all types
            foreach (var type in Enum.GetValues<CandlestickType>())
            {
                detectors.Add(new CandlestickPatternDetector(type));
            }
            
            // Add structural pattern detectors for all types
            foreach (var type in Enum.GetValues<StructuralType>())
            {
                detectors.Add(new StructuralPatternDetector(type));
            }
            
            // Add continuation pattern detectors for all types
            foreach (var type in Enum.GetValues<ContinuationType>())
            {
                detectors.Add(new ContinuationPatternDetector(type));
            }
            
            // Add reversal pattern detectors for all types
            foreach (var type in Enum.GetValues<ReversalType>())
            {
                detectors.Add(new ReversalPatternDetector(type));
            }
            
            return detectors;
        });

        // Register pattern engine
        services.AddSingleton<PatternEngine>(provider =>
            new PatternEngine(
                provider.GetRequiredService<ILogger<PatternEngine>>(),
                provider.GetRequiredService<IFeatureBus>(),
                provider.GetRequiredService<IEnumerable<IPatternDetector>>(),
                provider));

        return services;
    }

    /// <summary>
    /// Register strategy DSL services
    /// Adds YAML strategy loader, expression evaluator, and feature bus mapper
    /// </summary>
    public static IServiceCollection AddStrategyDslServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        
        // Register DSL configuration - configurations handled per service
        // No centralized DSL loader options needed with SimpleDslLoader

        // Register knowledge graph configuration - remove reference to missing options
        // Configuration handled directly in production services

        // Register core DSL services - DslLoader replaced by SimpleDslLoader in production services
        services.AddSingleton<ExpressionEvaluator>();
        services.AddSingleton<FeatureBusMapper>();

        // Legacy registration removed - consolidated to AddStrategyKnowledgeGraphServices
        // Old StrategyKnowledgeGraph replaced by new production implementation

        return services;
    }

    /// <summary>
    /// Register Strategy Knowledge Graph & Decision Fusion services (seed_version 1.0)
    /// Adds complete strategy evaluation and fusion system with Neural-UCB and CVaR-PPO integration
    /// </summary>
    public static IServiceCollection AddStrategyKnowledgeGraphServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        // Register required dependencies for production implementations - NO MOCKS
        services.AddSingleton<FeatureBusAdapter>(provider =>
            new FeatureBusAdapter(
                provider.GetRequiredService<Zones.IFeatureBus>(),
                provider.GetRequiredService<ILogger<FeatureBusAdapter>>(),
                provider));
        services.AddSingleton<IFeatureBusWithProbe>(provider => provider.GetRequiredService<FeatureBusAdapter>());
        
        // Register REAL production services with EnhancedRiskManager and RealTradingMetricsService
        services.AddSingleton<IRiskManagerForFusion, ProductionRiskManager>();
        services.AddSingleton<IMlrlMetricsServiceForFusion, ProductionMlrlMetricsService>();

        // Load Strategy DSL cards from YAML files
        var strategyFolder = configuration["StrategyCatalog:Folder"] ?? "config/strategies";
        var strategyCards = SimpleDslLoader.LoadAll(strategyFolder);
        services.AddSingleton(strategyCards);

        // Register production feature probe for real-time feature access
        services.AddSingleton<IFeatureProbe, ProductionFeatureProbe>();

        // Register production regime service with real regime detection
        services.AddSingleton<IRegimeService, ProductionRegimeService>();

        // Register Strategy Knowledge Graph with new implementation
        services.AddSingleton<IStrategyKnowledgeGraph, StrategyKnowledgeGraphNew>();

        // Register production UCB and PPO services with real ML integration
        services.AddSingleton<IUcbStrategyChooser, ProductionUcbStrategyChooser>();
        services.AddSingleton<IPpoSizer, ProductionPpoSizer>();

        // Register production ML configuration service with real config loading
        services.AddSingleton<IMLConfigurationService, ProductionMLConfigurationService>();

        // Register production metrics service with real telemetry
        services.AddSingleton<IMetrics, ProductionMetrics>();

        // Register Decision Fusion Coordinator
        services.AddSingleton<DecisionFusionCoordinator>();

        return services;
    }

    /// <summary>
    /// Register all pattern recognition and strategy DSL services together
    /// Complete registration for production-ready pattern analysis and strategy reasoning
    /// </summary>
    public static IServiceCollection AddPatternAndStrategyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPatternRecognitionServices(configuration);
        services.AddStrategyDslServices(configuration);
        
        // Add Strategy Knowledge Graph & Decision Fusion services
        services.AddStrategyKnowledgeGraphServices(configuration);
        
        return services;
    }
}

/// <summary>
/// Configuration options for pattern engine
/// </summary>
public class PatternEngineOptions
{
    public bool Enabled { get; set; } = true;
    public double MinPatternScore { get; set; } = 0.1;
    public int MaxPatternsPerAnalysis { get; set; } = 25;
    public bool FeatureBusPublishing { get; set; } = true;
    public PatternFamilyOptions PatternFamilies { get; set; } = new();
}

/// <summary>
/// Configuration options for pattern families
/// </summary>
public class PatternFamilyOptions
{
    // S109 Pattern weight constants
    private const double CandlestickPatternWeight = 1.0;
    private const double StructuralPatternWeight = 0.9;
    private const double ContinuationPatternWeight = 0.8;
    private const double ReversalPatternWeight = 0.8;
    
    public PatternFamilyConfig Candlestick { get; set; } = new() { Enabled = true, Weight = CandlestickPatternWeight };
    public PatternFamilyConfig Structural { get; set; } = new() { Enabled = true, Weight = StructuralPatternWeight };
    public PatternFamilyConfig Continuation { get; set; } = new() { Enabled = true, Weight = ContinuationPatternWeight };
    public PatternFamilyConfig Reversal { get; set; } = new() { Enabled = true, Weight = ReversalPatternWeight };
}

/// <summary>
/// Configuration for individual pattern family
/// </summary>
public class PatternFamilyConfig
{
    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
}