using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using BotCore.Intelligence.Models;
using BotCore.Services;
using System.Text;

namespace BotCore.Intelligence;

/// <summary>
/// Service that synthesizes collected data with LLM intelligence
/// Sends data to Ollama and gets trading intelligence back
/// </summary>
public class IntelligenceSynthesizerService
{
    private readonly ILogger<IntelligenceSynthesizerService> _logger;
    private readonly OllamaClient _ollamaClient;
    private readonly MarketDataReader _dataReader;
    private readonly IMemoryCache _cache;
    
    private const string QuickSentimentCacheKey = "quick_sentiment";
    private const string FullIntelligenceCacheKey = "full_intelligence";
    private const int QuickSentimentCacheTtlMinutes = 5;
    private const int FullIntelligenceCacheTtlMinutes = 15;
    private const int LlmTimeoutSeconds = 10;
    private const decimal CacheMissThreshold = 0.5m; // 50%
    
    private int _cacheHits = 0;
    private int _cacheMisses = 0;
    private NewsSentiment? _previousSentiment;

    public IntelligenceSynthesizerService(
        ILogger<IntelligenceSynthesizerService> logger,
        OllamaClient ollamaClient,
        MarketDataReader dataReader,
        IMemoryCache cache)
    {
        _logger = logger;
        _ollamaClient = ollamaClient;
        _dataReader = dataReader;
        _cache = cache;
    }

    /// <summary>
    /// Gets quick sentiment for 5-minute fast updates
    /// </summary>
    public async Task<NewsSentiment> GetQuickSentimentAsync()
    {
        try
        {
            // Check cache first
            if (_cache.TryGetValue(QuickSentimentCacheKey, out NewsSentiment? cached) && cached != null)
            {
                _cacheHits++;
                cached.Timestamp = DateTime.UtcNow;
                _logger.LogDebug("[INTELLIGENCE] Quick sentiment cache hit");
                return cached;
            }

            _cacheMisses++;
            _logger.LogDebug("[INTELLIGENCE] Quick sentiment cache miss, generating new");

            // Get latest news sentiment
            var sentimentData = await _dataReader.GetNewsSentimentSummaryAsync().ConfigureAwait(false);
            
            if (sentimentData == null)
            {
                return CreateFallbackSentiment();
            }

            var (bullish, bearish, neutral, headlines) = sentimentData.Value;

            // Build prompt for quick analysis
            var topHeadline = headlines.Count > 0 ? headlines[0] : "No recent news";
            var prompt = $"News sentiment check: {bullish:F1}% bullish / {bearish:F1}% bearish / {neutral:F1}% neutral. " +
                        $"Top headline: {topHeadline}. " +
                        $"Quick analysis: Is this bullish or bearish for ES/NQ? One sentence.";

            // Send to LLM with timeout
            string llmResponse;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                llmResponse = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[INTELLIGENCE] LLM timeout on quick sentiment, using fallback");
                return CreateFallbackSentiment(bullish, bearish, neutral, headlines);
            }

            // Parse response into NewsSentiment
            var sentiment = new NewsSentiment
            {
                BullishPercentage = bullish,
                BearishPercentage = bearish,
                NeutralPercentage = neutral,
                TopHeadlines = headlines,
                Timestamp = DateTime.UtcNow
            };

            // Determine sentiment shift
            if (_previousSentiment != null)
            {
                var bullishChange = bullish - _previousSentiment.BullishPercentage;
                if (Math.Abs(bullishChange) > 10)
                {
                    sentiment.SentimentShift = bullishChange > 0 ? SentimentShift.MoreBullish : SentimentShift.MoreBearish;
                }
            }

            _previousSentiment = sentiment;

            // Cache for 5 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(QuickSentimentCacheTtlMinutes));
            _cache.Set(QuickSentimentCacheKey, sentiment, cacheOptions);

            _logger.LogInformation("[INTELLIGENCE] Quick sentiment generated: {Bullish}% bullish, {Bearish}% bearish",
                bullish, bearish);

            return sentiment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTELLIGENCE] Error getting quick sentiment");
            return CreateFallbackSentiment();
        }
    }

    /// <summary>
    /// Gets full market intelligence for 15-minute comprehensive analysis
    /// </summary>
    public async Task<MarketIntelligence> GetFullMarketIntelligenceAsync()
    {
        try
        {
            // Check cache first
            if (_cache.TryGetValue(FullIntelligenceCacheKey, out MarketIntelligence? cached) && cached != null)
            {
                _cacheHits++;
                cached.CacheHit = true;
                _logger.LogDebug("[INTELLIGENCE] Full intelligence cache hit");
                return cached;
            }

            _cacheMisses++;
            _logger.LogDebug("[INTELLIGENCE] Full intelligence cache miss, generating new");

            // Gather all data
            var marketData = await _dataReader.GetLatestMarketDataAsync().ConfigureAwait(false);
            var fedData = await _dataReader.GetLatestFedDataAsync().ConfigureAwait(false);
            var sentimentData = await _dataReader.GetNewsSentimentSummaryAsync().ConfigureAwait(false);
            var events = await _dataReader.GetUpcomingEconomicEventsAsync().ConfigureAwait(false);
            var systemHealth = await _dataReader.GetSystemHealthAsync().ConfigureAwait(false);

            // Assess data quality
            int dataSourcesAvailable = 0;
            if (marketData != null) dataSourcesAvailable++;
            if (fedData != null) dataSourcesAvailable++;
            if (sentimentData != null) dataSourcesAvailable++;
            if (events != null) dataSourcesAvailable++;
            if (systemHealth != null) dataSourcesAvailable++;

            var dataQuality = dataSourcesAvailable >= 4 ? DataQuality.Complete :
                             dataSourcesAvailable >= 2 ? DataQuality.Partial :
                             DataQuality.Insufficient;

            if (dataQuality == DataQuality.Insufficient)
            {
                _logger.LogWarning("[INTELLIGENCE] Insufficient data for intelligence generation");
                return CreateFallbackIntelligence(dataQuality);
            }

            // Build comprehensive prompt
            var prompt = BuildComprehensivePrompt(marketData, fedData, sentimentData, events, systemHealth);

            // Send to LLM with timeout
            string llmResponse;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                llmResponse = await _ollamaClient.AskAsync(prompt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[INTELLIGENCE] LLM timeout on full intelligence, using fallback");
                return CreateFallbackIntelligence(dataQuality, marketData, fedData, sentimentData, events);
            }

            // Parse LLM response
            var intelligence = ParseLlmResponse(llmResponse, marketData, fedData, sentimentData, events);
            intelligence.DataQuality = dataQuality;
            intelligence.RawLlmResponse = llmResponse;
            intelligence.CacheHit = false;

            // Cache for 15 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(FullIntelligenceCacheTtlMinutes));
            _cache.Set(FullIntelligenceCacheKey, intelligence, cacheOptions);

            _logger.LogInformation("[INTELLIGENCE] Full intelligence generated: {Regime}, Bias={Bias}, Confidence={Confidence:F1}",
                intelligence.RegimeAnalysis, intelligence.RecommendedBias, intelligence.ConfidenceLevel);

            // Check cache miss rate and adjust TTL if needed
            AdjustCacheTtlIfNeeded();

            return intelligence;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTELLIGENCE] Error getting full intelligence");
            return CreateFallbackIntelligence(DataQuality.Insufficient);
        }
    }

    /// <summary>
    /// Gets combined intelligence used by trading brain
    /// Merges quick sentiment with full intelligence
    /// </summary>
    public async Task<MarketIntelligence> GetCombinedIntelligenceAsync()
    {
        try
        {
            // Get quick sentiment (5-min cache)
            var sentiment = await GetQuickSentimentAsync().ConfigureAwait(false);
            
            // Get full intelligence (15-min cache)
            var fullIntelligence = await GetFullMarketIntelligenceAsync().ConfigureAwait(false);

            // If sentiment is fresher, override the bias in full intelligence
            if (sentiment.Timestamp > fullIntelligence.Timestamp)
            {
                // Update bias based on fresh sentiment
                if (sentiment.BullishPercentage > sentiment.BearishPercentage + 20)
                {
                    fullIntelligence.RecommendedBias = MarketBias.Bullish;
                }
                else if (sentiment.BearishPercentage > sentiment.BullishPercentage + 20)
                {
                    fullIntelligence.RecommendedBias = MarketBias.Bearish;
                }
                else
                {
                    fullIntelligence.RecommendedBias = MarketBias.Neutral;
                }

                _logger.LogDebug("[INTELLIGENCE] Updated bias from fresh sentiment: {Bias}", fullIntelligence.RecommendedBias);
            }

            return fullIntelligence;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTELLIGENCE] Error getting combined intelligence");
            return CreateFallbackIntelligence(DataQuality.Insufficient);
        }
    }

    private string BuildComprehensivePrompt(
        Dictionary<string, object>? marketData,
        Dictionary<string, object>? fedData,
        (decimal Bullish, decimal Bearish, decimal Neutral, List<string> TopHeadlines)? sentimentData,
        List<Dictionary<string, object>>? events,
        Dictionary<string, object>? systemHealth)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("COMPREHENSIVE MARKET ANALYSIS REQUEST:");
        prompt.AppendLine();

        // Market data section
        if (marketData != null && marketData.Count > 0)
        {
            prompt.AppendLine("MARKET DATA:");
            if (marketData.ContainsKey("SPX"))
                prompt.AppendLine($"  SPX: {marketData["SPX"]}");
            if (marketData.ContainsKey("VIX"))
                prompt.AppendLine($"  VIX: {marketData["VIX"]}");
            if (marketData.ContainsKey("VIXTermStructure"))
                prompt.AppendLine($"  VIX Term Structure: {marketData["VIXTermStructure"]}");
            if (marketData.ContainsKey("DXY"))
                prompt.AppendLine($"  DXY: {marketData["DXY"]}");
            if (marketData.ContainsKey("TNX"))
                prompt.AppendLine($"  TNX: {marketData["TNX"]}%");
            prompt.AppendLine();
        }

        // Fed balance sheet section
        if (fedData != null && fedData.Count > 0)
        {
            prompt.AppendLine("FED BALANCE SHEET:");
            if (fedData.ContainsKey("TotalAssets"))
                prompt.AppendLine($"  Assets: ${Convert.ToDecimal(fedData["TotalAssets"]) / 1_000_000_000_000:F2}T");
            if (fedData.ContainsKey("TotalAssetsWoWChange"))
                prompt.AppendLine($"  WoW Change: {fedData["TotalAssetsWoWChange"]}");
            if (fedData.ContainsKey("QTStatus"))
                prompt.AppendLine($"  QT Status: {fedData["QTStatus"]}");
            prompt.AppendLine();
        }

        // News sentiment section
        if (sentimentData != null)
        {
            var (bullish, bearish, neutral, headlines) = sentimentData.Value;
            prompt.AppendLine("NEWS SENTIMENT:");
            prompt.AppendLine($"  {bullish:F1}% bullish / {bearish:F1}% bearish / {neutral:F1}% neutral");
            if (headlines.Count > 0)
            {
                prompt.AppendLine("  Top Headlines:");
                foreach (var headline in headlines.Take(3))
                {
                    prompt.AppendLine($"    - {headline}");
                }
            }
            prompt.AppendLine();
        }

        // Upcoming events section
        if (events != null && events.Count > 0)
        {
            prompt.AppendLine("UPCOMING EVENTS:");
            foreach (var evt in events.Take(5))
            {
                if (evt.ContainsKey("title") && evt.ContainsKey("date") && evt.ContainsKey("impact"))
                {
                    prompt.AppendLine($"  - {evt["title"]} ({evt["date"]}, Impact: {evt["impact"]})");
                }
            }
            prompt.AppendLine();
        }

        // System health section
        if (systemHealth != null && systemHealth.Count > 0)
        {
            prompt.AppendLine("SYSTEM HEALTH:");
            if (systemHealth.ContainsKey("workflow_success_rate"))
                prompt.AppendLine($"  Workflow Success Rate: {systemHealth["workflow_success_rate"]}");
            prompt.AppendLine();
        }

        prompt.AppendLine("TASK: Analyze complete market regime. Should we be bullish/bearish/neutral on ES? " +
                         "What's the risk level? Any event risks to avoid in next 24 hours?");

        return prompt.ToString();
    }

    private MarketIntelligence ParseLlmResponse(
        string llmResponse,
        Dictionary<string, object>? marketData,
        Dictionary<string, object>? fedData,
        (decimal Bullish, decimal Bearish, decimal Neutral, List<string> TopHeadlines)? sentimentData,
        List<Dictionary<string, object>>? events)
    {
        var intelligence = new MarketIntelligence
        {
            RegimeAnalysis = llmResponse.Length > 200 ? llmResponse.Substring(0, 200) : llmResponse,
            Timestamp = DateTime.UtcNow
        };

        // Parse recommended bias
        var lowerResponse = llmResponse.ToLower();
        if (lowerResponse.Contains("bullish") && !lowerResponse.Contains("not bullish"))
        {
            intelligence.RecommendedBias = MarketBias.Bullish;
            intelligence.ConfidenceLevel = 65;
        }
        else if (lowerResponse.Contains("bearish") && !lowerResponse.Contains("not bearish"))
        {
            intelligence.RecommendedBias = MarketBias.Bearish;
            intelligence.ConfidenceLevel = 65;
        }
        else
        {
            intelligence.RecommendedBias = MarketBias.Neutral;
            intelligence.ConfidenceLevel = 50;
        }

        // Extract risk factors
        if (lowerResponse.Contains("risk") || lowerResponse.Contains("volatil"))
        {
            intelligence.RiskFactors.Add("Elevated volatility mentioned");
        }
        if (lowerResponse.Contains("fed") || lowerResponse.Contains("fomc"))
        {
            intelligence.RiskFactors.Add("Federal Reserve risk factor");
        }
        if (lowerResponse.Contains("cpi") || lowerResponse.Contains("inflation"))
        {
            intelligence.RiskFactors.Add("Inflation data risk");
        }

        // Add event risks
        if (events != null)
        {
            foreach (var evt in events.Take(3))
            {
                if (evt.ContainsKey("title") && evt.ContainsKey("impact"))
                {
                    var impact = evt["impact"].ToString()?.ToLower() ?? "";
                    if (impact == "high")
                    {
                        intelligence.EventRisks.Add($"{evt["title"]}");
                    }
                }
            }
        }

        // Store key metrics
        if (marketData != null)
        {
            foreach (var kvp in marketData)
            {
                intelligence.KeyMetrics[kvp.Key] = kvp.Value;
            }
        }

        return intelligence;
    }

    private NewsSentiment CreateFallbackSentiment(
        decimal bullish = 33.3m,
        decimal bearish = 33.3m,
        decimal neutral = 33.4m,
        List<string>? headlines = null)
    {
        return new NewsSentiment
        {
            BullishPercentage = bullish,
            BearishPercentage = bearish,
            NeutralPercentage = neutral,
            TopHeadlines = headlines ?? new List<string> { "No news data available" },
            SentimentShift = SentimentShift.Unchanged,
            Timestamp = DateTime.UtcNow
        };
    }

    private MarketIntelligence CreateFallbackIntelligence(
        DataQuality dataQuality,
        Dictionary<string, object>? marketData = null,
        Dictionary<string, object>? fedData = null,
        (decimal Bullish, decimal Bearish, decimal Neutral, List<string> TopHeadlines)? sentimentData = null,
        List<Dictionary<string, object>>? events = null)
    {
        var intelligence = new MarketIntelligence
        {
            RegimeAnalysis = "Unable to generate full analysis - using basic rules",
            RecommendedBias = MarketBias.Neutral,
            ConfidenceLevel = 40,
            DataQuality = dataQuality,
            Timestamp = DateTime.UtcNow
        };

        // Apply simple rules if we have some data
        if (marketData != null)
        {
            if (marketData.ContainsKey("VIX"))
            {
                var vix = Convert.ToDecimal(marketData["VIX"]);
                if (vix > 25)
                {
                    intelligence.RecommendedBias = MarketBias.Bearish;
                    intelligence.RiskFactors.Add("Elevated VIX indicating fear");
                }
            }

            if (marketData.ContainsKey("SPX"))
            {
                // Store for reference
                intelligence.KeyMetrics["SPX"] = marketData["SPX"];
            }
        }

        if (sentimentData != null)
        {
            var (bullish, bearish, neutral, headlines) = sentimentData.Value;
            if (bullish > bearish + 20)
            {
                intelligence.RecommendedBias = MarketBias.Bullish;
            }
            else if (bearish > bullish + 20)
            {
                intelligence.RecommendedBias = MarketBias.Bearish;
            }
        }

        return intelligence;
    }

    private void AdjustCacheTtlIfNeeded()
    {
        var total = _cacheHits + _cacheMisses;
        if (total > 10)
        {
            var missRate = (decimal)_cacheMisses / total;
            if (missRate > CacheMissThreshold)
            {
                _logger.LogWarning("[INTELLIGENCE] High cache miss rate: {Rate:F2}, consider increasing TTL", missRate);
            }
        }
    }
}
