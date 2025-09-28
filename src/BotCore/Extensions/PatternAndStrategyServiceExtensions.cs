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
        services.AddSingleton<PatternEngine>();

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
        
        // Register DSL loader configuration
        services.Configure<DslLoaderOptions>(
            configuration.GetSection("StrategyCatalog"));

        // Register knowledge graph configuration
        services.Configure<StrategyKnowledgeGraphOptions>(
            configuration.GetSection("StrategyKnowledgeGraph"));

        // Register core DSL services
        services.AddSingleton<DslLoader>();
        services.AddSingleton<ExpressionEvaluator>();
        services.AddSingleton<FeatureBusMapper>();

        // Register Phase 5 services - Knowledge Graph Implementation
        services.AddSingleton<FeatureProbe>();
        services.AddSingleton<StrategyKnowledgeGraph>();

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

        // Register required dependencies for production implementations
        services.AddSingleton<FeatureBusAdapter>();
        services.AddSingleton<IFeatureBusWithProbe>(provider => provider.GetRequiredService<FeatureBusAdapter>());
        
        // Register production services with real implementations
        services.AddSingleton<IRiskManager, ProductionRiskManager>();
        services.AddSingleton<IMLRLMetricsService, ProductionMLRLMetricsService>();

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
    public PatternFamilyConfig Candlestick { get; set; } = new() { Enabled = true, Weight = 1.0 };
    public PatternFamilyConfig Structural { get; set; } = new() { Enabled = true, Weight = 0.9 };
    public PatternFamilyConfig Continuation { get; set; } = new() { Enabled = true, Weight = 0.8 };
    public PatternFamilyConfig Reversal { get; set; } = new() { Enabled = true, Weight = 0.8 };
}

/// <summary>
/// Configuration for individual pattern family
/// </summary>
public class PatternFamilyConfig
{
    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
}