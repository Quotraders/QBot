using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotCore.Services;

/// <summary>
/// Interface for real-time news monitoring service
/// </summary>
public interface INewsMonitorService
{
    /// <summary>
    /// Initialize the news monitoring service
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current news context for trading decisions
    /// </summary>
    Task<NewsContext> GetCurrentNewsContextAsync();
    
    /// <summary>
    /// Check if service is healthy and operational
    /// </summary>
    bool IsHealthy { get; }
}

/// <summary>
/// News context for trading decisions
/// </summary>
public class NewsContext
{
    public bool HasBreakingNews { get; set; }
    public string? LatestHeadline { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public decimal SentimentScore { get; set; } = 0.5m; // 0 = bearish, 0.5 = neutral, 1 = bullish
    public List<string> RecentHeadlines { get; set; } = new();
    public bool IsHighVolatilityPeriod { get; set; }
}

/// <summary>
/// NewsAPI.org response models
/// </summary>
internal class NewsApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("articles")]
    public List<NewsArticle> Articles { get; set; } = new();
}

internal class NewsArticle
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("publishedAt")]
    public DateTime PublishedAt { get; set; }
    
    [JsonPropertyName("source")]
    public NewsSource? Source { get; set; }
}

internal class NewsSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Production-grade news monitoring service with NewsAPI.org integration
/// </summary>
public class NewsMonitorService : INewsMonitorService, IDisposable
{
    private readonly ILogger<NewsMonitorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Timer? _monitoringTimer;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    
    private NewsContext _currentContext = new();
    private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
    private int _consecutiveFailures = 0;
    private bool _isInitialized = false;
    private bool _disposed = false;
    
    // Configuration
    private readonly string? _apiKey;
    private readonly int _pollIntervalMinutes;
    private readonly string[] _keywords;
    private readonly int _maxArticlesToAnalyze;
    private readonly TimeSpan _newsRecencyThreshold;
    
    // Constants
    private const string NewsApiBaseUrl = "https://newsapi.org/v2/everything";
    private const int MaxConsecutiveFailuresBeforeWarning = 3;
    private const int RateLimitDelayMilliseconds = 1000; // 1 second between requests
    private const int RequestTimeoutSeconds = 10;
    private const int MaxRecentHeadlines = 10;
    
    // Sentiment keywords
    private static readonly string[] PositiveKeywords = new[]
    {
        "growth", "rise", "gain", "positive", "strong", "boost", "recovery",
        "surge", "rally", "bullish", "optimistic", "improve", "expansion"
    };
    
    private static readonly string[] NegativeKeywords = new[]
    {
        "fall", "decline", "drop", "negative", "weak", "crisis", "concern",
        "crash", "plunge", "bearish", "pessimistic", "worsen", "recession",
        "emergency", "failure", "bankruptcy", "collapse"
    };
    
    private static readonly string[] HighImpactKeywords = new[]
    {
        "federal reserve", "fomc", "powell", "emergency", "trump", "biden",
        "rate decision", "war", "attack", "crisis", "crash", "halt", "suspend"
    };

    public bool IsHealthy => _isInitialized && 
                            _consecutiveFailures < MaxConsecutiveFailuresBeforeWarning &&
                            (DateTime.UtcNow - _lastSuccessfulUpdate).TotalMinutes < (_pollIntervalMinutes * 2);

    public NewsMonitorService(
        ILogger<NewsMonitorService> logger,
        HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        // Load configuration from environment
        _apiKey = Environment.GetEnvironmentVariable("NEWSAPI_KEY");
        _pollIntervalMinutes = int.Parse(
            Environment.GetEnvironmentVariable("NEWSAPI_POLL_INTERVAL_MINUTES") ?? "5",
            System.Globalization.CultureInfo.InvariantCulture);
        
        var keywordsEnv = Environment.GetEnvironmentVariable("NEWSAPI_KEYWORDS") ?? 
            "Federal Reserve,FOMC,Trump,rate,emergency,tariff,Powell";
        _keywords = keywordsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .ToArray();
        
        _maxArticlesToAnalyze = int.Parse(
            Environment.GetEnvironmentVariable("NEWSAPI_MAX_ARTICLES") ?? "20",
            System.Globalization.CultureInfo.InvariantCulture);
        
        _newsRecencyThreshold = TimeSpan.FromMinutes(int.Parse(
            Environment.GetEnvironmentVariable("NEWSAPI_RECENCY_MINUTES") ?? "60",
            System.Globalization.CultureInfo.InvariantCulture));
        
        // Configure HttpClient
        _httpClient.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TradingBot/1.0");
        
        // Only create timer if API key is configured
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _monitoringTimer = new Timer(
                MonitoringTimerCallback,
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogInformation("[NewsMonitor] Already initialized");
            return;
        }
        
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("[NewsMonitor] ⚠️ NEWSAPI_KEY not configured - news monitoring disabled");
            _logger.LogInformation("[NewsMonitor] To enable: Set NEWSAPI_KEY environment variable (free at newsapi.org)");
            _isInitialized = false;
            return;
        }
        
        _logger.LogInformation("[NewsMonitor] 📰 Initializing news monitoring service...");
        _logger.LogInformation("[NewsMonitor] Poll interval: {Minutes} minutes", _pollIntervalMinutes);
        _logger.LogInformation("[NewsMonitor] Keywords: {Keywords}", string.Join(", ", _keywords));
        
        try
        {
            // Initial fetch
            await FetchAndAnalyzeNewsAsync(cancellationToken).ConfigureAwait(false);
            
            // Start monitoring timer
            if (_monitoringTimer != null)
            {
                _monitoringTimer.Change(
                    TimeSpan.FromMinutes(_pollIntervalMinutes),
                    TimeSpan.FromMinutes(_pollIntervalMinutes));
            }
            
            _isInitialized = true;
            _logger.LogInformation("[NewsMonitor] ✅ News monitoring initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NewsMonitor] ❌ Failed to initialize news monitoring");
            _isInitialized = false;
            throw;
        }
    }

    private void MonitoringTimerCallback(object? state)
    {
        // Fire and forget - don't block timer thread
        _ = Task.Run(async () =>
        {
            try
            {
                await FetchAndAnalyzeNewsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NewsMonitor] Error in monitoring timer callback");
            }
        });
    }

    private async Task FetchAndAnalyzeNewsAsync(CancellationToken cancellationToken)
    {
        if (!await _updateLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("[NewsMonitor] Update already in progress, skipping");
            return;
        }
        
        try
        {
            _logger.LogDebug("[NewsMonitor] 🔍 Fetching latest news...");
            
            // Rate limiting
            await Task.Delay(RateLimitDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            
            // Build query URL with FINANCIAL CONTEXT FILTER
            // This prevents garbage results like Drake lawsuits, hurricanes, solar projects
            var keywordQuery = "(" + string.Join(" OR ", _keywords.Select(k => $"\"{k}\"")) + ")";
            var financialFilter = "(stock OR futures OR market OR trading OR S&P OR Nasdaq OR \"Wall Street\")";
            var query = $"{keywordQuery} AND {financialFilter}";
            
            var fromDate = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var url = $"{NewsApiBaseUrl}?q={Uri.EscapeDataString(query)}&from={fromDate}&sortBy=publishedAt&language=en&pageSize={_maxArticlesToAnalyze}&apiKey={_apiKey}";
            
            // Fetch news
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                
                // Check for rate limit error (429 or 426 for free tier limit)
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                    (int)response.StatusCode == 426 || 
                    errorContent.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[NewsMonitor] ⚠️ NewsAPI rate limit reached (100 requests/24h on free tier). Consider upgrading to paid plan or disabling news monitoring with NEWS_ENABLED=false");
                    _consecutiveFailures++;
                    return;
                }
                
                _logger.LogWarning("[NewsMonitor] NewsAPI request failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                
                _consecutiveFailures++;
                
                if (_consecutiveFailures >= MaxConsecutiveFailuresBeforeWarning)
                {
                    _logger.LogError("[NewsMonitor] ❌ {Count} consecutive failures - check API key and quota", 
                        _consecutiveFailures);
                }
                
                return;
            }
            
            var newsResponse = await response.Content.ReadFromJsonAsync<NewsApiResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            
            if (newsResponse?.Status != "ok")
            {
                _logger.LogWarning("[NewsMonitor] Invalid response from NewsAPI");
                _consecutiveFailures++;
                return;
            }
            
            // Analyze articles (with AI sentiment)
            var articles = newsResponse.Articles ?? new List<NewsArticle>();
            var context = await AnalyzeArticles(articles).ConfigureAwait(false);
            
            // Update current context (thread-safe)
            _currentContext = context;
            _lastSuccessfulUpdate = DateTime.UtcNow;
            _consecutiveFailures = 0;
            
            // Log results
            if (context.HasBreakingNews)
            {
                _logger.LogWarning("[NewsMonitor] 🔥 Breaking news detected: {Headline}", context.LatestHeadline);
                _logger.LogInformation("[NewsMonitor] Sentiment: {Sentiment:F2}, High volatility: {HighVol}",
                    context.SentimentScore, context.IsHighVolatilityPeriod);
            }
            else
            {
                _logger.LogDebug("[NewsMonitor] ✅ {Count} articles analyzed, no breaking news", 
                    articles.Count);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[NewsMonitor] News fetch timeout - will retry next cycle");
            _consecutiveFailures++;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[NewsMonitor] Network error fetching news - will retry next cycle");
            _consecutiveFailures++;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[NewsMonitor] JSON parsing error - invalid NewsAPI response");
            _consecutiveFailures++;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NewsMonitor] Unexpected error fetching news");
            _consecutiveFailures++;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task<NewsContext> AnalyzeArticles(List<NewsArticle> articles)
    {
        var context = new NewsContext
        {
            LastUpdateTime = DateTime.UtcNow
        };
        
        if (articles == null || articles.Count == 0)
        {
            return context;
        }
        
        // Check freshness of most recent article
        var mostRecent = articles.OrderByDescending(a => a.PublishedAt).First();
        var ageMinutes = (DateTime.UtcNow - mostRecent.PublishedAt).TotalMinutes;
        
        // FUTURES TRADE 23 HOURS/DAY - News should be fresher than 2 hours for actionable intel
        if (ageMinutes > 120)
        {
            _logger.LogWarning("[NewsMonitor] ⚠️ Stale news data - most recent article is {Minutes} minutes old (NewsAPI free tier limitation)", 
                (int)ageMinutes);
            _logger.LogInformation("[NewsMonitor] 💡 Relying on GitHub workflows + VIX monitoring for current market intelligence");
        }
        
        // Filter for recent articles only
        var recentArticles = articles
            .Where(a => (DateTime.UtcNow - a.PublishedAt) < _newsRecencyThreshold)
            .OrderByDescending(a => a.PublishedAt)
            .ToList();
        
        if (recentArticles.Count == 0)
        {
            _logger.LogDebug("[NewsMonitor] No articles within recency threshold ({Minutes} min)", 
                _newsRecencyThreshold.TotalMinutes);
            return context;
        }
        
        // Store recent headlines
        context.RecentHeadlines = recentArticles
            .Take(MaxRecentHeadlines)
            .Select(a => $"{a.Title} ({a.PublishedAt:HH:mm})")
            .ToList();
        
        // Check for high-impact keywords (breaking news)
        var latestArticle = recentArticles.First();
        var latestText = $"{latestArticle.Title} {latestArticle.Description}".ToUpperInvariant();
        
        context.HasBreakingNews = HighImpactKeywords.Any(keyword => 
            latestText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        if (context.HasBreakingNews)
        {
            context.LatestHeadline = latestArticle.Title;
            context.IsHighVolatilityPeriod = true;
        }
        
        // Calculate aggregate sentiment using AI (with keyword fallback)
        var sentimentTasks = recentArticles
            .Select(a => CalculateSentimentWithAIAsync($"{a.Title} {a.Description}"))
            .ToList();
        
        var sentimentScores = await Task.WhenAll(sentimentTasks).ConfigureAwait(false);
        
        context.SentimentScore = sentimentScores.Any() 
            ? sentimentScores.Average() 
            : 0.5m;
        
        return context;
    }

    private async Task<decimal> CalculateSentimentWithAIAsync(string text)
    {
        // Try AI-powered sentiment analysis first (if Ollama available)
        var ollamaEnabled = Environment.GetEnvironmentVariable("OLLAMA_ENABLED")?.ToLowerInvariant() == "true";
        
        if (ollamaEnabled && _httpClient != null)
        {
            try
            {
                var prompt = $@"You are a financial sentiment analyzer for ES/NQ futures trading.

Analyze this news headline and description for market sentiment:
{text}

Respond with ONLY a number from 0.0 to 1.0:
- 0.0 = Maximum bearish (expect market to fall)
- 0.5 = Neutral (no clear direction)
- 1.0 = Maximum bullish (expect market to rise)

Consider:
- Fed policy: Hawkish (bearish), Dovish (bullish)
- Rate decisions: Hikes (bearish for stocks), Cuts (bullish for stocks)
- Economic data: Strong (bullish), Weak (bearish)
- Geopolitical: War/tariffs (bearish), Peace/trade deals (bullish)
- Market keywords: Crash/plunge (bearish), Rally/surge (bullish)

Number only (e.g., 0.3 or 0.7):";

                var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
                var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "gemma2:2b";
                
                var requestBody = new
                {
                    model = ollamaModel,
                    prompt = prompt,
                    stream = false
                };
                
                var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.PostAsync(
                    new Uri($"{ollamaUrl}/api/generate"),
                    content,
                    cts.Token).ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    var responseObject = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(responseJson);
                    
                    if (responseObject?.Response != null)
                    {
                        // Extract number from response
                        var match = System.Text.RegularExpressions.Regex.Match(responseObject.Response, @"0?\.\d+|[01]\.?\d*");
                        if (match.Success && decimal.TryParse(match.Value, out var sentiment))
                        {
                            // Clamp to valid range
                            sentiment = Math.Max(0.0m, Math.Min(1.0m, sentiment));
                            _logger.LogDebug("[NewsMonitor] 🤖 AI sentiment: {Sentiment:F2} for: {Text}", 
                                sentiment, text.Substring(0, Math.Min(100, text.Length)));
                            return sentiment;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[NewsMonitor] AI sentiment failed, falling back to keyword analysis");
            }
        }
        
        // FALLBACK: Keyword-based sentiment (if AI unavailable or fails)
        return CalculateSentimentKeywords(text);
    }
    
    private static decimal CalculateSentimentKeywords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0.5m;
        
        var upperText = text.ToUpperInvariant();
        
        // Enhanced keyword analysis with Fed-specific terms
        var positiveCount = PositiveKeywords.Count(keyword => 
            upperText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        var negativeCount = NegativeKeywords.Count(keyword => 
            upperText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        // Fed-specific adjustments
        if (upperText.Contains("RATE CUT", StringComparison.OrdinalIgnoreCase) || 
            upperText.Contains("DOVISH", StringComparison.OrdinalIgnoreCase))
        {
            positiveCount += 2; // Rate cuts are bullish for stocks
        }
        
        if (upperText.Contains("RATE HIKE", StringComparison.OrdinalIgnoreCase) || 
            upperText.Contains("HAWKISH", StringComparison.OrdinalIgnoreCase))
        {
            negativeCount += 2; // Rate hikes are bearish for stocks
        }
        
        if (positiveCount == 0 && negativeCount == 0)
            return 0.5m; // Neutral
        
        var totalMentions = positiveCount + negativeCount;
        return (decimal)positiveCount / totalMentions;
    }
    
    private static decimal CalculateSentiment(string text)
    {
        // Synchronous wrapper for backward compatibility
        return CalculateSentimentKeywords(text);
    }
    
    // Ollama response model
    private class OllamaResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("response")]
        public string? Response { get; set; }
    }

    public Task<NewsContext> GetCurrentNewsContextAsync()
    {
        // Return cached context (thread-safe read)
        return Task.FromResult(_currentContext);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        
        if (disposing)
        {
            _monitoringTimer?.Dispose();
            _updateLock?.Dispose();
        }
        
        _disposed = true;
    }
}
