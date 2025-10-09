using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Integration;

/// <summary>
/// Pattern score resolver - extracts bull/bear scores from real pattern engine
/// </summary>
public sealed class PatternScoreResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _isBullScore;
    private readonly ILogger<PatternScoreResolver> _logger;
    
    public PatternScoreResolver(IServiceProvider serviceProvider, bool isBullScore)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _isBullScore = isBullScore;
        _logger = serviceProvider.GetRequiredService<ILogger<PatternScoreResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            var value = _isBullScore ? patternScores.BullScore : patternScores.BearScore;
            var scoreType = _isBullScore ? "bull" : "bear";
            
            _logger.LogTrace("Pattern {ScoreType} score for {Symbol}: {Value}", scoreType, symbol, value);
            return value;
        }
        catch (Exception ex)
        {
            var scoreType = _isBullScore ? "bull" : "bear";
            _logger.LogError(ex, "Failed to resolve pattern {ScoreType} score for symbol {Symbol}", scoreType, symbol);
            throw new InvalidOperationException($"Production pattern {scoreType} score resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pattern signal resolver - detects specific patterns from pattern scores
/// </summary>
public sealed class PatternSignalResolver : IFeatureResolver
{
    // Pattern detection thresholds
    private const double DojiMaxScoreDifference = 0.1;
    private const double DojiMinConfidence = 0.70;
    private const double HammerMinBullScore = 0.70;
    private const double HammerMinConfidence = 0.75;
    
    private readonly IServiceProvider _serviceProvider;
    private readonly string _patternType;
    private readonly ILogger<PatternSignalResolver> _logger;
    
    public PatternSignalResolver(IServiceProvider serviceProvider, string patternType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _patternType = patternType ?? throw new ArgumentNullException(nameof(patternType));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternSignalResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Pattern signal detection based on scores and confidence
            var isPatternPresent = _patternType.ToUpperInvariant() switch
            {
                "DOJI" => DetectDojiPattern(patternScores),
                "HAMMER" => DetectHammerPattern(patternScores),
                _ => throw new InvalidOperationException($"Unknown pattern type: {_patternType}")
            };
            
            var signalValue = isPatternPresent ? 1.0 : 0.0;
            _logger.LogTrace("Pattern signal {Pattern} for {Symbol}: {Value}", _patternType, symbol, signalValue);
            return signalValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern signal {Pattern} for symbol {Symbol}", _patternType, symbol);
            throw new InvalidOperationException($"Production pattern signal resolution failed for '{_patternType}' on '{symbol}': {ex.Message}", ex);
        }
    }
    
    private static bool DetectDojiPattern(dynamic patternScores)
    {
        // Doji pattern: neutral sentiment with high confidence
        var bullScore = (double)patternScores.BullScore;
        var bearScore = (double)patternScores.BearScore;
        var confidence = (double)patternScores.OverallConfidence;
        
        var scoreDifference = Math.Abs(bullScore - bearScore);
        return scoreDifference <= DojiMaxScoreDifference && confidence >= DojiMinConfidence; // Balanced scores with high confidence
    }
    
    private static bool DetectHammerPattern(dynamic patternScores)
    {
        // Hammer pattern: strong bull score with high confidence
        var bullScore = (double)patternScores.BullScore;
        var confidence = (double)patternScores.OverallConfidence;
        
        return bullScore >= HammerMinBullScore && confidence >= HammerMinConfidence; // Strong bullish sentiment
    }
}

/// <summary>
/// Pattern confirmation resolver - validates pattern confirmation using confidence thresholds
/// </summary>
public sealed class PatternConfirmationResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PatternConfirmationResolver> _logger;
    
    public PatternConfirmationResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternConfirmationResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Pattern is confirmed if confidence is above 70%
            var isConfirmed = patternScores.OverallConfidence >= 0.70;
            var confirmationValue = isConfirmed ? 1.0 : 0.0;
            
            _logger.LogTrace("Pattern confirmation for {Symbol}: {Value} (confidence: {Confidence})", 
                symbol, confirmationValue, patternScores.OverallConfidence);
            
            return confirmationValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern confirmation for symbol {Symbol}", symbol);
            throw new InvalidOperationException($"Production pattern confirmation resolution failed for '{symbol}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pattern reliability resolver - measures pattern reliability via overall confidence
/// </summary>
public sealed class PatternReliabilityResolver : IFeatureResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _patternType;
    private readonly ILogger<PatternReliabilityResolver> _logger;
    
    public PatternReliabilityResolver(IServiceProvider serviceProvider, string patternType)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _patternType = patternType ?? throw new ArgumentNullException(nameof(patternType));
        _logger = serviceProvider.GetRequiredService<ILogger<PatternReliabilityResolver>>();
    }
    
    public async Task<double?> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var patternEngine = _serviceProvider.GetRequiredService<BotCore.Patterns.PatternEngine>();
            var patternScores = await patternEngine.GetCurrentScoresAsync(symbol, cancellationToken).ConfigureAwait(false);
            
            // Pattern reliability is directly the overall confidence for the specific pattern type
            var reliability = patternScores.OverallConfidence;
            
            _logger.LogTrace("Pattern reliability {Pattern} for {Symbol}: {Value}", _patternType, symbol, reliability);
            return reliability;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve pattern reliability {Pattern} for symbol {Symbol}", _patternType, symbol);
            throw new InvalidOperationException($"Production pattern reliability resolution failed for '{_patternType}' on '{symbol}': {ex.Message}", ex);
        }
    }
}