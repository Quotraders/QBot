using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using BotCore.Patterns;
using BotCore.Patterns.Detectors;
using BotCore.StrategyDsl;
using Microsoft.Extensions.Logging;

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

        // Register DSL loader
        services.AddSingleton<DslLoader>();

        // Register expression evaluator
        services.AddSingleton<ExpressionEvaluator>();

        // Register feature bus mapper
        services.AddSingleton<FeatureBusMapper>();

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