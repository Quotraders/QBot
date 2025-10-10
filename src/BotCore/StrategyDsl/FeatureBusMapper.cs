using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BotCore.StrategyDsl;

/// <summary>
/// Maps DSL identifiers to feature bus keys and handles feature value retrieval
/// </summary>
public class FeatureBusMapper
{
    private readonly ILogger<FeatureBusMapper> _logger;
    private readonly Dictionary<string, string> _keyMappings = new();

    // Default feature value constants
    private const int DefaultMinutesValue = 60;                // Default minutes for time-based features
    private const double DefaultStrengthValue = 0.5;           // Default strength/confidence value

    // LoggerMessage delegates for performance
    private static readonly Action<ILogger, int, Exception?> LogMappingsInitialized =
        LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(7001, nameof(LogMappingsInitialized)),
            "Initialized {Count} feature bus mappings");

    private static readonly Action<ILogger, string, string, Exception?> LogCustomMappingAdded =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(7002, nameof(LogCustomMappingAdded)),
            "Added custom mapping: {DSL} -> {FeatureBus}");

    public FeatureBusMapper(ILogger<FeatureBusMapper> logger)
    {
        _logger = logger;
        InitializeDefaultMappings();
    }

    /// <summary>
    /// Initialize default DSL to feature bus key mappings
    /// </summary>
    private void InitializeDefaultMappings()
    {
        InitializeZoneMappings();
        InitializePatternMappings();
        InitializeMarketRegimeMappings();
        InitializeMomentumMappings();
        InitializeVolumeMappings();
        InitializeVolatilityMappings();
        InitializeTimeMappings();
        InitializeTrendMappings();
        InitializePullbackMappings();
        InitializeFibonacciMappings();
        InitializeBreakoutMappings();
    }

    private void InitializeZoneMappings()
    {
        // Zone-related mappings
        _keyMappings["zone.distance_atr"] = "zone.distance_atr";
        _keyMappings["zone.breakout_score"] = "zone.breakout_score";
        _keyMappings["zone.pressure"] = "zone.pressure";
        _keyMappings["zone.strength"] = "zone.strength";
        _keyMappings["zone.test_count"] = "zone.test_count";
        _keyMappings["zone.type"] = "zone.type";
        _keyMappings["zone.alignment"] = "zone.alignment";
    }

    private void InitializePatternMappings()
    {
        // Pattern-related mappings
        _keyMappings["pattern.bull_score"] = "pattern.bull_score";
        _keyMappings["pattern.bear_score"] = "pattern.bear_score";
        _keyMappings["pattern.total_count"] = "pattern.total_count";

        // Individual pattern mappings (pattern.kind::PatternName format)
        var patternNames = new[]
        {
            "Doji", "Hammer", "ShootingStar", "Engulfing", "Harami", "Marubozu", "SpinningTop",
            "ThreeBlackCrows", "ThreeWhiteSoldiers", "MorningStar", "EveningStar", 
            "PiercingLine", "DarkCloudCover", "TweezerTops", "TweezerBottoms",
            "HeadAndShoulders", "InverseHeadAndShoulders", "DoubleTop", "DoubleBottom",
            "TripleTop", "TripleBottom", "CupAndHandle", "Rectangle", "AscendingTriangle",
            "DescendingTriangle", "SymmetricalTriangle", "RoundingBottom", "RoundingTop",
            "BullFlag", "BearFlag", "BullPennant", "BearPennant", "Compression", "Consolidation",
            "BreakoutRetest", "KeyReversal", "FailedBreakout", "ExhaustionGap", "ClimaxReversal"
        };

        foreach (var pattern in patternNames)
        {
            _keyMappings[$"pattern.kind::{pattern}"] = $"pattern.kind::{pattern}";
            _keyMappings[$"pattern.direction::{pattern}"] = $"pattern.direction::{pattern}";
            _keyMappings[$"pattern.confidence::{pattern}"] = $"pattern.confidence::{pattern}";
        }
    }

    private void InitializeMarketRegimeMappings()
    {
        // Market regime mappings
        _keyMappings["market_regime"] = "regime.current";
        _keyMappings["volatility_z_score"] = "volatility.z_score";
        _keyMappings["news_impact_score"] = "news.impact_score";
    }

    private void InitializeMomentumMappings()
    {
        // Momentum mappings
        _keyMappings["momentum.z_score"] = "momentum.z_score";
        _keyMappings["momentum.acceleration"] = "momentum.acceleration";
        _keyMappings["momentum.bullish"] = "momentum.bullish";
        _keyMappings["momentum.reversal"] = "momentum.reversal";
        _keyMappings["momentum.divergence"] = "momentum.divergence";
        _keyMappings["momentum.weakness"] = "momentum.weakness";

        // VWAP mappings
        _keyMappings["vwap.distance"] = "vwap.distance";
    }

    private void InitializeVolumeMappings()
    {
        // Volume mappings  
        _keyMappings["volume.relative"] = "volume.relative";
        _keyMappings["volume.spike"] = "volume.spike";
        _keyMappings["volume_profile.breakout_ready"] = "volume_profile.breakout_ready";
    }

    private void InitializeVolatilityMappings()
    {
        // Volatility mappings
        _keyMappings["volatility_contraction"] = "volatility.contraction";
        _keyMappings["atr.ratio"] = "atr.ratio";
    }

    private void InitializeTimeMappings()
    {
        // Time-based mappings
        _keyMappings["time_of_day"] = "time.of_day";
        _keyMappings["time_to_close_minutes"] = "time.to_close_minutes";
        _keyMappings["minutes_since_news"] = "news.minutes_since";
        _keyMappings["session_volume"] = "session.volume_pct";
        _keyMappings["economic_event_minutes"] = "economic.event_minutes";
    }

    private void InitializeTrendMappings()
    {
        // Trend mappings
        _keyMappings["trend.strength"] = "trend.strength";
        _keyMappings["trend.direction"] = "trend.direction";
        _keyMappings["trend_duration_bars"] = "trend.duration_bars";
        _keyMappings["ema.alignment"] = "ema.alignment";
        _keyMappings["ema.spacing"] = "ema.spacing";
    }

    private void InitializePullbackMappings()
    {
        // Pullback mappings
        _keyMappings["pullback.depth"] = "pullback.depth";
        _keyMappings["pullback.complete"] = "pullback.complete";
        _keyMappings["pullback_duration"] = "pullback.duration";
    }

    private void InitializeFibonacciMappings()
    {
        // Fibonacci mappings
        _keyMappings["fibonacci.level"] = "fibonacci.level";
    }

    private void InitializeBreakoutMappings()
    {
        // Breakout mappings
        _keyMappings["breakout.confirmed"] = "breakout.confirmed";
        _keyMappings["breakout.direction"] = "breakout.direction";
        _keyMappings["breakout_failure.confirmed"] = "breakout.failure.confirmed";
        _keyMappings["breakout_attempt_age"] = "breakout.attempt_age";
        _keyMappings["false_breakout.recent"] = "breakout.false_recent";

        // Pattern-specific mappings
        _keyMappings["compression.upper_bound"] = "compression.upper_bound";
        _keyMappings["compression.lower_bound"] = "compression.lower_bound";
        _keyMappings["rectangle.test_count"] = "rectangle.test_count";
        _keyMappings["rectangle.duration"] = "rectangle.duration";
        _keyMappings["rectangle.height"] = "rectangle.height";
        _keyMappings["rectangle.upper_boundary"] = "rectangle.upper_boundary";
        _keyMappings["rectangle.lower_boundary"] = "rectangle.lower_boundary";
        _keyMappings["rectangle.maturity"] = "rectangle.maturity";
        _keyMappings["rectangle.touches_zones"] = "rectangle.touches_zones";

        // Cup and handle mappings
        _keyMappings["cup.depth"] = "cup.depth";
        _keyMappings["cup.symmetry"] = "cup.symmetry";
        _keyMappings["cup.orientation"] = "cup.orientation";
        _keyMappings["handle.depth"] = "handle.depth";
        _keyMappings["handle.consolidation_time"] = "handle.consolidation_time";
        _keyMappings["handle.formation_complete"] = "handle.formation_complete";
        _keyMappings["handle.slope_angle"] = "handle.slope_angle";
        _keyMappings["handle.resistance_break"] = "handle.resistance_break";

        // Price rejection mappings
        _keyMappings["price_rejection.strength"] = "rejection.strength";
        _keyMappings["price_rejection.direction"] = "rejection.direction";

        LogMappingsInitialized(_logger, _keyMappings.Count, null);
    }

    /// <summary>
    /// Map a DSL identifier to its feature bus key
    /// </summary>
    public string MapToFeatureBusKey(string dslIdentifier)
    {
        if (_keyMappings.TryGetValue(dslIdentifier, out var mappedKey))
        {
            return mappedKey;
        }

        // If no explicit mapping exists, return the identifier as-is (pass-through)
        return dslIdentifier;
    }

    /// <summary>
    /// Map multiple DSL identifiers to feature bus keys
    /// </summary>
    public Dictionary<string, string> MapMultiple(IEnumerable<string> dslIdentifiers)
    {
        return dslIdentifiers.ToDictionary(id => id, MapToFeatureBusKey);
    }

    /// <summary>
    /// Extract all unique DSL identifiers from an expression
    /// </summary>
    // CA1822/S2325: Made static as it doesn't access instance data
    public static HashSet<string> ExtractIdentifiers(string expression)
    {
        var identifiers = new HashSet<string>();
        
        if (string.IsNullOrWhiteSpace(expression))
            return identifiers;

        // Use regex to find all dot-notation identifiers (e.g., zone.distance_atr, pattern.kind::Doji)
        var matches = System.Text.RegularExpressions.Regex.Matches(
            expression, 
            @"([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*(?:::[a-zA-Z_][a-zA-Z0-9_]*)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var identifier = match.Groups[1].Value;
            
            // Skip reserved words and operators
            if (!IsReservedWord(identifier))
            {
                identifiers.Add(identifier);
            }
        }

        return identifiers;
    }

    /// <summary>
    /// Extract identifiers from multiple expressions
    /// </summary>
    // CA1822/S2325: Made static as it doesn't access instance data
    public static HashSet<string> ExtractIdentifiers(IEnumerable<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        
        var allIdentifiers = new HashSet<string>();
        
        foreach (var expression in expressions)
        {
            var identifiers = ExtractIdentifiers(expression);
            foreach (var id in identifiers)
            {
                allIdentifiers.Add(id);
            }
        }

        return allIdentifiers;
    }

    /// <summary>
    /// Create feature dictionary from DSL expressions by mapping identifiers to feature bus keys
    /// </summary>
    public Dictionary<string, object> CreateFeatureDictionary(IEnumerable<string> expressions, 
                                                              Dictionary<string, object> rawFeatureValues)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(rawFeatureValues);
        
        var identifiers = ExtractIdentifiers(expressions);
        var featureDict = new Dictionary<string, object>();

        foreach (var identifier in identifiers)
        {
            var featureKey = MapToFeatureBusKey(identifier);
            
            if (rawFeatureValues.TryGetValue(featureKey, out var value))
            {
                featureDict[identifier] = value;
            }
            else
            {
                // Try to find a close match or default value
                var defaultValue = GetDefaultValueForIdentifier(identifier);
                if (defaultValue != null)
                {
                    featureDict[identifier] = defaultValue;
                }
            }
        }

        return featureDict;
    }

    /// <summary>
    /// Add or update a custom mapping
    /// </summary>
    public void AddMapping(string dslIdentifier, string featureBusKey)
    {
        _keyMappings[dslIdentifier] = featureBusKey;
        LogCustomMappingAdded(_logger, dslIdentifier, featureBusKey, null);
    }

    /// <summary>
    /// Get all current mappings
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllMappings()
    {
        return _keyMappings.AsReadOnly();
    }

    private static bool IsReservedWord(string word)
    {
        var reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "true", "false", "IN", "null"
        };

        return reservedWords.Contains(word);
    }

    private static object? GetDefaultValueForIdentifier(string identifier)
    {
        // Provide sensible defaults for common identifiers when values are missing
        return identifier.ToUpperInvariant() switch
        {
            var id when id.Contains("TIME_OF_DAY", StringComparison.Ordinal) => TimeSpan.FromHours(12), // Noon
            var id when id.Contains("_SCORE", StringComparison.Ordinal) => 0.0,
            var id when id.Contains("DISTANCE", StringComparison.Ordinal) => 1.0,
            var id when id.Contains("RATIO", StringComparison.Ordinal) => 1.0,
            var id when id.Contains("COUNT", StringComparison.Ordinal) => 0,
            var id when id.Contains("MINUTES", StringComparison.Ordinal) => DefaultMinutesValue,
            var id when id.Contains("STRENGTH", StringComparison.Ordinal) => DefaultStrengthValue,
            var id when id.EndsWith("_CONFIRMED", StringComparison.Ordinal) || id.EndsWith("_READY", StringComparison.Ordinal) => false,
            var id when id.Contains("ALIGNMENT", StringComparison.Ordinal) => "neutral",
            var id when id.Contains("DIRECTION", StringComparison.Ordinal) => "neutral",
            var id when id.Contains("REGIME", StringComparison.Ordinal) => "Range",
            _ => null
        };
    }
}