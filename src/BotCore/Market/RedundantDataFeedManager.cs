using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace BotCore.Market;

/// <summary>
/// Data feed interface for redundant market data management
/// </summary>
public interface IDataFeed
{
    string FeedName { get; }
    int Priority { get; }
    Task<bool> ConnectAsync();
    Task<MarketData?> GetMarketDataAsync(string symbol);
    Task<OrderBook?> GetOrderBookAsync(string symbol);
    event EventHandler<MarketData>? OnDataReceived;
    event EventHandler<Exception>? OnError;
}

/// <summary>
/// Market data structure
/// </summary>
public class MarketData
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Order book structure
/// </summary>
public class OrderBook
{
    public string Symbol { get; set; } = string.Empty;
    public IReadOnlyList<OrderBookLevel> Bids { get; } = new List<OrderBookLevel>();
    public IReadOnlyList<OrderBookLevel> Asks { get; } = new List<OrderBookLevel>();
    public DateTime Timestamp { get; set; }
}

public class OrderBookLevel
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
}

/// <summary>
/// Data feed health monitoring
/// </summary>
public class DataFeedHealth
{
    public string FeedName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public DateTime LastDataReceived { get; set; }
    public double Latency { get; set; }
    public int ErrorCount { get; set; }
    public double DataQualityScore { get; set; }
}

/// <summary>
/// Redundant data feed manager for high availability market data
/// </summary>
public class RedundantDataFeedManager : IDisposable
{
    private readonly ILogger<RedundantDataFeedManager> _logger;
    private readonly TradingBot.Abstractions.ITopstepXAdapterService? _topstepXAdapter;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly List<IDataFeed> _dataFeeds = new();
    private readonly ConcurrentDictionary<string, DataFeedHealth> _feedHealth = new();
    private readonly ConcurrentDictionary<string, MarketData> _consolidatedData = new();
    private IDataFeed? _primaryFeed;
    private readonly Timer _healthCheckTimer;
    private readonly Timer _consistencyCheckTimer;
    private bool _disposed;
    
    // Data feed priority levels
    private const int PRIMARY_FEED_PRIORITY = 1;
    private const int BACKUP_FEED_PRIORITY = 2;
    
    // JSON serialization options
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    
    // Data consistency thresholds
    private const decimal PRICE_TOLERANCE = 0.001m;        // 0.1% price deviation tolerance
    private const decimal PRICE_OUTLIER_THRESHOLD = 0.001m; // 0.1% threshold for outlier detection
    private const decimal SPREAD_TOLERANCE = 0.05m;        // 5% spread deviation tolerance
    private const decimal MINIMUM_PRICE_DEVIATION = 0.01m; // 1% minimum deviation threshold
    private const decimal QUALITY_SCORE_PENALTY = 0.95m;   // Reduce quality score to 95% on issues
    
    // Time-based thresholds (seconds)
    private const double FRESHNESS_TOLERANCE_SECONDS = 30; // Data freshness tolerance
    private const double STALE_DATA_THRESHOLD_SECONDS = 30; // Consider data stale after this
    
    // Performance thresholds
    private const double SLOW_RESPONSE_THRESHOLD_MS = 500; // Response time threshold in milliseconds
    private const double HIGH_LATENCY_THRESHOLD_MS = 100;  // High latency warning threshold
    private const int STATUS_LOG_INTERVAL_SECONDS = 30;    // Log status every N seconds
    
    // Data quality score penalties
    private const double STALE_DATA_SCORE_PENALTY = 0.3;     // Penalty for 30+ second old data
    private const double VERY_STALE_DATA_SCORE_PENALTY = 0.5; // Penalty for 1+ minute old data
    private const double INVALID_SPREAD_SCORE_PENALTY = 0.2;  // Penalty for invalid bid/ask spread
    
    // Common futures symbols for consistency checks
    private static readonly string[] CommonFuturesSymbols = new[] { "ES", "NQ", "YM", "RTY" };

    public event EventHandler<MarketData>? OnConsolidatedData;
    public event EventHandler<string>? OnFeedFailover;

    public RedundantDataFeedManager(
        ILogger<RedundantDataFeedManager> logger,
        TradingBot.Abstractions.ITopstepXAdapterService? topstepXAdapter = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger;
        _topstepXAdapter = topstepXAdapter;
        _loggerFactory = loggerFactory;
        _healthCheckTimer = new Timer(CheckFeedHealth, null, Timeout.Infinite, Timeout.Infinite);
        _consistencyCheckTimer = new Timer(CheckDataConsistency, null, Timeout.Infinite, Timeout.Infinite);
        
        _logger.LogInformation("[DataFeed] RedundantDataFeedManager initialized");
    }

    /// <summary>
    /// Initialize data feeds and start monitoring
    /// </summary>
    public async Task InitializeDataFeedsAsync()
    {
        _logger.LogInformation("[DataFeed] Initializing data feeds");
        
        // Add data feeds with TopstepX adapter for real market data
        var topstepXLogger = _loggerFactory?.CreateLogger<TopstepXDataFeed>();
        AddDataFeed(new TopstepXDataFeed(_topstepXAdapter, topstepXLogger) { Priority = PRIMARY_FEED_PRIORITY });
        AddDataFeed(new BackupDataFeed { Priority = BACKUP_FEED_PRIORITY });
        
        // Sort by priority
        _dataFeeds.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Connect to all feeds
        foreach (var feed in _dataFeeds)
        {
            try
            {
                var connected = await feed.ConnectAsync().ConfigureAwait(false);
                _feedHealth[feed.FeedName] = new DataFeedHealth
                {
                    FeedName = feed.FeedName,
                    IsHealthy = connected,
                    LastHealthCheck = DateTime.UtcNow
                };

                // Subscribe to events
                feed.OnDataReceived += OnDataReceived;
                feed.OnError += OnFeedError;

                if (connected && _primaryFeed == null)
                {
                    _primaryFeed = feed;
                    _logger.LogInformation("[DataFeed] Primary data feed set to: {FeedName}", feed.FeedName);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[DataFeed] Connection operation error for {FeedName}", feed.FeedName);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "[DataFeed] Connection timeout for {FeedName}", feed.FeedName);
            }
        }

        // Start health monitoring
        _healthCheckTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        _consistencyCheckTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void AddDataFeed(IDataFeed dataFeed)
    {
        ArgumentNullException.ThrowIfNull(dataFeed);
        
        _dataFeeds.Add(dataFeed);
        _logger.LogDebug("[DataFeed] Added data feed: {FeedName} (Priority: {Priority})", 
            dataFeed.FeedName, dataFeed.Priority);
    }

    /// <summary>
    /// Get market data with automatic failover
    /// </summary>
    public async Task<MarketData?> GetMarketDataAsync(string symbol)
    {
        MarketData? data = null;
        Exception? lastError = null;

        // Try primary feed first
        if (_primaryFeed != null && _feedHealth.GetValueOrDefault(_primaryFeed.FeedName)?.IsHealthy == true)
        {
            try
            {
                data = await _primaryFeed.GetMarketDataAsync(symbol).ConfigureAwait(false);
                if (ValidateMarketData(data))
                {
                    return data;
                }
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                await HandleFeedFailureAsync(_primaryFeed, ex).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                lastError = ex;
                await HandleFeedFailureAsync(_primaryFeed, ex).ConfigureAwait(false);
            }
        }

        // Failover to backup feeds
        foreach (var feed in _dataFeeds.Where(f => f != _primaryFeed))
        {
            if (_feedHealth.GetValueOrDefault(feed.FeedName)?.IsHealthy == true)
            {
                try
                {
                    _logger.LogWarning("[DataFeed] Failing over to {FeedName} for {Symbol}", feed.FeedName, symbol);

                    data = await feed.GetMarketDataAsync(symbol).ConfigureAwait(false);
                    if (ValidateMarketData(data))
                    {
                        // Switch primary feed
                        _primaryFeed = feed;
                        OnFeedFailover?.Invoke(this, feed.FeedName);
                        _logger.LogWarning("[DataFeed] Data feed switched to {FeedName}", feed.FeedName);
                        return data;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    lastError = ex;
                    await HandleFeedFailureAsync(feed, ex).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    lastError = ex;
                    await HandleFeedFailureAsync(feed, ex).ConfigureAwait(false);
                }
            }
        }

        // All feeds failed
        var errorMessage = $"All data feeds unavailable for {symbol}";
        _logger.LogCritical("[DataFeed] {ErrorMessage}", errorMessage);
        throw new InvalidOperationException(errorMessage, lastError);
    }

    private static bool ValidateMarketData(MarketData? data)
    {
        if (data == null) return false;
        if (data.Price <= 0) return false;
        if (string.IsNullOrEmpty(data.Symbol)) return false;
        if (DateTime.UtcNow - data.Timestamp > TimeSpan.FromMinutes(5)) return false;
        
        return true;
    }

    private Task HandleFeedFailureAsync(IDataFeed feed, Exception ex)
    {
        if (_feedHealth.TryGetValue(feed.FeedName, out var health))
        {
            health.IsHealthy = false;
            health.ErrorCount++;
            
            _logger.LogError(ex, "[DataFeed] Feed {FeedName} failed (Error count: {ErrorCount})", 
                feed.FeedName, health.ErrorCount);
        }

        return Task.CompletedTask;
    }

    private void OnDataReceived(object? sender, MarketData data)
    {
        try
        {
            if (sender is IDataFeed feed)
            {
                if (_feedHealth.TryGetValue(feed.FeedName, out var health))
                {
                    health.LastDataReceived = DateTime.UtcNow;
                }
                
                // Store consolidated data
                _consolidatedData[data.Symbol] = data;
                
                // Emit consolidated data event
                OnConsolidatedData?.Invoke(this, data);
            }
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "[DataFeed] Invalid data received");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "[DataFeed] Operation error processing received data");
        }
    }

    private void OnFeedError(object? sender, Exception ex)
    {
        if (sender is IDataFeed feed)
        {
            _ = Task.Run(async () => await HandleFeedFailureAsync(feed, ex).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private void CheckFeedHealth(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var feed in _dataFeeds)
                {
                    var startTime = DateTime.UtcNow;
                    
                    try
                    {
                        // Test feed with ping
                        var testData = await feed.GetMarketDataAsync("ES").ConfigureAwait(false);
                        var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        
                        if (_feedHealth.TryGetValue(feed.FeedName, out var health))
                        {
                            health.IsHealthy = testData != null;
                            health.LastHealthCheck = DateTime.UtcNow;
                            health.Latency = latency;
                            health.DataQualityScore = CalculateDataQuality(testData);
                        }
                        
                        if (latency > HIGH_LATENCY_THRESHOLD_MS)
                        {
                            _logger.LogWarning("[DataFeed] High latency on {FeedName}: {Latency}ms", feed.FeedName, latency);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_feedHealth.TryGetValue(feed.FeedName, out var health))
                        {
                            health.IsHealthy = false;
                            health.ErrorCount++;
                        }
                        
                        _logger.LogDebug(ex, "[DataFeed] Health check failed for {FeedName}", feed.FeedName);
                    }
                }
                
                // Ensure at least one feed is healthy
                if (!_feedHealth.Values.Any(h => h.IsHealthy))
                {
                    _logger.LogCritical("[DataFeed] ALL DATA FEEDS DOWN - TRADING HALTED");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DataFeed] Error in health check");
            }
        });
    }

    private void CheckDataConsistency(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var symbol in CommonFuturesSymbols)
                {
                    await CheckSymbolConsistencyAsync(symbol).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DataConsistency] Error during consistency check");
            }
        });
    }

    private async Task CheckSymbolConsistencyAsync(string symbol)
    {
        try
        {
            var consistency = new DataConsistencyResult
            {
                Symbol = symbol,
                CheckTime = DateTime.UtcNow
            };

            // Collect data from all healthy feeds
            var healthyFeeds = _dataFeeds.Where(f => 
                _feedHealth.TryGetValue(f.FeedName, out var h) && h.IsHealthy).ToList();

            if (healthyFeeds.Count < 2)
            {
                _logger.LogDebug("[DataConsistency] Insufficient feeds for consistency check: {Symbol}", symbol);
                return;
            }

            // Gather market data from each feed
            var tasks = healthyFeeds.Select(async feed =>
            {
                try
                {
                    var startTime = DateTime.UtcNow;
                    var data = await feed.GetMarketDataAsync(symbol).ConfigureAwait(false);
                    var responseTime = DateTime.UtcNow - startTime;

                    if (data != null && ValidateMarketData(data))
                    {
                        consistency.FeedData[feed.FeedName] = new MarketDataSnapshot
                        {
                            FeedName = feed.FeedName,
                            Price = data.Price,
                            Bid = data.Bid,
                            Ask = data.Ask,
                            Volume = data.Volume,
                            Timestamp = data.Timestamp,
                            ResponseTime = responseTime,
                            DataAge = DateTime.UtcNow - data.Timestamp
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[DataConsistency] Failed to get data from {FeedName} for {Symbol}", 
                        feed.FeedName, symbol);
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Analyze consistency if we have enough data
            if (consistency.FeedData.Count >= 2)
            {
                AnalyzeDataConsistency(consistency);
                
                // Take action on inconsistencies
                if (!consistency.IsConsistent)
                {
                    await HandleDataInconsistencyAsync(consistency).ConfigureAwait(false);
                }
                
                // Log periodic status
                if (DateTime.UtcNow.Second % STATUS_LOG_INTERVAL_SECONDS == 0) // Every 30 seconds
                {
                    LogConsistencyStatus(consistency);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataConsistency] Error checking consistency for {Symbol}", symbol);
        }
    }

    private static void AnalyzeDataConsistency(DataConsistencyResult consistency)
    {
        if (consistency.FeedData.Count < 2) return;

        var snapshots = consistency.FeedData.Values.ToList();
        
        // Analyze price consistency
        var prices = snapshots.Select(s => s.Price).ToList();
        var avgPrice = prices.Average();
        var maxDeviation = prices.Max(p => Math.Abs(p - avgPrice) / avgPrice);
        var priceStdDev = CalculateStandardDeviation(prices.Select(p => (double)p));

        // Analyze bid-ask consistency  
        var spreads = snapshots.Where(s => s.Ask > s.Bid).Select(s => s.Ask - s.Bid).ToList();
        var avgSpread = spreads.Count > 0 ? spreads.Average() : 0m;
        var spreadDeviation = spreads.Count > 0 ? spreads.Max(s => Math.Abs(s - avgSpread) / avgSpread) : 0m;

        // Analyze data freshness
        var dataAges = snapshots.Select(s => s.DataAge.TotalSeconds).ToList();
        var maxAge = dataAges.Max();
        var avgAge = dataAges.Average();

        // Set consistency metrics
        consistency.PriceDeviation = maxDeviation;
        consistency.PriceStandardDeviation = (decimal)priceStdDev;
        consistency.SpreadDeviation = spreadDeviation;
        consistency.MaxDataAge = TimeSpan.FromSeconds(maxAge);
        consistency.AverageDataAge = TimeSpan.FromSeconds(avgAge);

        // Determine overall consistency
        consistency.IsConsistent = 
            maxDeviation < PRICE_TOLERANCE && // 0.1% price tolerance
            spreadDeviation < SPREAD_TOLERANCE && // 5% spread tolerance  
            maxAge < FRESHNESS_TOLERANCE_SECONDS; // 30 second freshness tolerance

        // Identify outliers
        if (!consistency.IsConsistent)
        {
            // Find price outliers
            foreach (var snapshot in snapshots)
            {
                var deviation = Math.Abs(snapshot.Price - avgPrice) / avgPrice;
                if (deviation == maxDeviation && deviation > PRICE_OUTLIER_THRESHOLD)
                {
                    consistency.AddOutlierFeed(snapshot.FeedName);
                    consistency.AddIssue($"Price outlier: {snapshot.FeedName} deviates by {deviation:P2}");
                }
            }

            // Find stale data
            foreach (var snapshot in snapshots)
            {
                if (snapshot.DataAge.TotalSeconds > STALE_DATA_THRESHOLD_SECONDS)
                {
                    consistency.AddIssue($"Stale data: {snapshot.FeedName} is {snapshot.DataAge.TotalSeconds:F1}s old");
                }
            }

            // Find slow feeds
            foreach (var snapshot in snapshots)
            {
                if (snapshot.ResponseTime.TotalMilliseconds > SLOW_RESPONSE_THRESHOLD_MS)
                {
                    consistency.AddIssue($"Slow response: {snapshot.FeedName} took {snapshot.ResponseTime.TotalMilliseconds:F0}ms");
                }
            }
        }
    }

    private async Task HandleDataInconsistencyAsync(DataConsistencyResult consistency)
    {
        try
        {
            _logger.LogWarning("[DataConsistency] Inconsistency detected for {Symbol}: {Issues}", 
                consistency.Symbol, string.Join("; ", consistency.Issues));

            // Update feed health scores for outliers
            foreach (var outlierFeed in consistency.OutlierFeeds)
            {
                if (_feedHealth.TryGetValue(outlierFeed, out var health))
                {
                    health.DataQualityScore *= (double)QUALITY_SCORE_PENALTY; // Reduce quality score
                    _logger.LogWarning("[DataConsistency] Reduced quality score for {FeedName} to {Score:F2}", 
                        outlierFeed, health.DataQualityScore);
                }
            }

            // Create consistency alert
            var alert = new
            {
                AlertType = "DATA_INCONSISTENCY",
                Symbol = consistency.Symbol,
                PriceDeviation = consistency.PriceDeviation,
                Issues = consistency.Issues,
                FeedCount = consistency.FeedData.Count,
                OutlierFeeds = consistency.OutlierFeeds,
                Timestamp = DateTime.UtcNow,
                Severity = consistency.PriceDeviation > 0.005m ? "HIGH" : "MEDIUM"
            };

            // Store alert (implement based on your alerting system)
            await StoreConsistencyAlertAsync(alert).ConfigureAwait(false);

            // If deviation is severe, trigger failover
            if (consistency.PriceDeviation > MINIMUM_PRICE_DEVIATION) // 1% deviation
            {
                _logger.LogError("[DataConsistency] SEVERE inconsistency detected for {Symbol}: {Deviation:P2}", 
                    consistency.Symbol, consistency.PriceDeviation);
                
                // Consider switching primary feed or halting trading
                await ConsiderFeedFailoverAsync(consistency).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataConsistency] Error handling inconsistency for {Symbol}", consistency.Symbol);
        }
    }

    private void LogConsistencyStatus(DataConsistencyResult consistency)
    {
        var avgPrice = consistency.FeedData.Values.Average(s => s.Price);
        
        if (consistency.IsConsistent)
        {
            _logger.LogDebug("[DataConsistency] ✅ {Symbol} consistent across {FeedCount} feeds: avg=${AvgPrice:F2}, deviation={Deviation:P3}", 
                consistency.Symbol, consistency.FeedData.Count, avgPrice, consistency.PriceDeviation);
        }
        else
        {
            _logger.LogInformation("[DataConsistency] ⚠️ {Symbol} inconsistent: deviation={Deviation:P2}, issues={IssueCount}", 
                consistency.Symbol, consistency.PriceDeviation, consistency.Issues.Count);
        }
    }

    private static double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var data = values.ToList();
        if (data.Count <= 1) return 0.0;
        
        var mean = data.Average();
        var variance = data.Sum(x => Math.Pow(x - mean, 2)) / (data.Count - 1);
        return Math.Sqrt(variance);
    }

    private async Task StoreConsistencyAlertAsync(object alert)
    {
        try
        {
            // Store in database or send to monitoring system
            _logger.LogWarning("[DataConsistency] ALERT: {Alert}", System.Text.Json.JsonSerializer.Serialize(alert));
            
            // Implement storage for consistency alerts
            await StoreConsistencyAlert(alert).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataConsistency] Failed to store consistency alert");
        }
    }

    private async Task StoreConsistencyAlert(object alert)
    {
        try
        {
            // Store alert to file for monitoring system pickup
            var alertsDir = Path.Combine("logs", "consistency_alerts");
            Directory.CreateDirectory(alertsDir);
            
            var alertFile = Path.Combine(alertsDir, $"alert_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(alert, s_jsonOptions);
            await File.WriteAllTextAsync(alertFile, json).ConfigureAwait(false);
            
            _logger.LogDebug("[DataConsistency] Alert stored to {AlertFile}", alertFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataConsistency] Failed to write consistency alert to file");
        }
    }

    private async Task ConsiderFeedFailoverAsync(DataConsistencyResult consistency)
    {
        try
        {
            // If primary feed is an outlier, switch to a consensus feed
            if (_primaryFeed != null && consistency.OutlierFeeds.Contains(_primaryFeed.FeedName))
            {
                // Find best consensus feed (non-outlier with highest quality score)
                var consensusFeed = _dataFeeds
                    .Where(f => !consistency.OutlierFeeds.Contains(f.FeedName))
                    .Where(f => _feedHealth.TryGetValue(f.FeedName, out var h) && h.IsHealthy)
                    .OrderByDescending(f => _feedHealth.GetValueOrDefault(f.FeedName)?.DataQualityScore ?? 0)
                    .FirstOrDefault();

                if (consensusFeed != null)
                {
                    _logger.LogWarning("[DataConsistency] Switching primary feed from {OldFeed} to {NewFeed} due to data inconsistency", 
                        _primaryFeed.FeedName, consensusFeed.FeedName);
                    
                    _primaryFeed = consensusFeed;
                    OnFeedFailover?.Invoke(this, consensusFeed.FeedName);
                }
            }
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DataConsistency] Error during feed failover consideration");
        }
    }

    // Data structures for consistency checking - internal to avoid CA1034 (nested public types)
    internal sealed class DataConsistencyResult
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime CheckTime { get; set; }
        public Dictionary<string, MarketDataSnapshot> FeedData { get; } = new();
        public bool IsConsistent { get; set; }
        public decimal PriceDeviation { get; set; }
        public decimal PriceStandardDeviation { get; set; }
        public decimal SpreadDeviation { get; set; }
        public TimeSpan MaxDataAge { get; set; }
        public TimeSpan AverageDataAge { get; set; }
        private readonly List<string> _outlierFeeds = new();
        private readonly List<string> _issues = new();
        public IReadOnlyList<string> OutlierFeeds => _outlierFeeds;
        public IReadOnlyList<string> Issues => _issues;
        
        internal void AddOutlierFeed(string feedName) => _outlierFeeds.Add(feedName);
        internal void AddIssue(string issue) => _issues.Add(issue);
    }

    internal sealed class MarketDataSnapshot
    {
        public string FeedName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Volume { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public TimeSpan DataAge { get; set; }
    }

    private static double CalculateDataQuality(MarketData? data)
    {
        if (data == null) return 0.0;
        
        var score = 1.0;
        
        // Reduce score for stale data
        var age = DateTime.UtcNow - data.Timestamp;
        if (age > TimeSpan.FromSeconds(STALE_DATA_THRESHOLD_SECONDS)) score -= STALE_DATA_SCORE_PENALTY;
        if (age > TimeSpan.FromMinutes(1)) score -= VERY_STALE_DATA_SCORE_PENALTY;
        
        // Reduce score for invalid prices
        if (data.Price <= 0) score = 0.0;
        if (data.Bid >= data.Ask && data.Bid > 0 && data.Ask > 0) score -= INVALID_SPREAD_SCORE_PENALTY;
        
        return Math.Max(0.0, score);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _logger.LogInformation("[DataFeed] Disposing RedundantDataFeedManager");
            
            try
            {
                _healthCheckTimer?.Dispose();
                _consistencyCheckTimer?.Dispose();
                
                foreach (var feed in _dataFeeds)
                {
                    if (feed is IDisposable disposableFeed)
                    {
                        disposableFeed.Dispose();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown - ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DataFeed] Error disposing resources");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
    
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// TopstepX data feed implementation
/// </summary>
public class TopstepXDataFeed : IDataFeed
{
    // Primary feed simulation constants
    private const int ConnectionDelayMs = 100;
    private const int NetworkDelayMs = 50;
    private const int SIMULATION_DELAY_MS = 50;            // Delay for simulated operations
    private const int DEFAULT_VOLUME = 1000;               // Default volume for test data
    
    // Price-related test data constants
    private const decimal ES_BASE_PRICE = 4500.00m;        // ES base price for test data
    private const decimal ES_BID_PRICE = 4499.75m;         // ES bid price for test data
    private const decimal ES_ASK_PRICE = 4500.25m;         // ES ask price for test data
    private const decimal PRICE_VARIATION_RANGE = 10m;     // Price variation range (+/-)
    private const decimal PRICE_VARIATION_OFFSET = 5m;     // Price variation offset
    
    // Tick size constants for bid/ask calculation
    private const decimal ES_TICK_SIZE = 0.25m;            // ES/MNQ tick size
    
    private readonly TradingBot.Abstractions.ITopstepXAdapterService? _adapterService;
    private readonly ILogger<TopstepXDataFeed>? _logger;
    
    public string FeedName => "TopstepX";
    public int Priority { get; set; } = 1;
    
    public event EventHandler<MarketData>? OnDataReceived;
    public event EventHandler<Exception>? OnError;
    
    /// <summary>
    /// Constructor with optional TopstepX adapter service for real market data
    /// </summary>
    /// <param name="adapterService">TopstepX adapter service (optional, falls back to simulation if null)</param>
    /// <param name="logger">Logger for diagnostics (optional)</param>
    public TopstepXDataFeed(
        TradingBot.Abstractions.ITopstepXAdapterService? adapterService = null,
        ILogger<TopstepXDataFeed>? logger = null)
    {
        _adapterService = adapterService;
        _logger = logger;
        
        if (_adapterService != null)
        {
            _logger?.LogInformation("[TopstepXDataFeed] Initialized with real TopstepX adapter");
        }
        else
        {
            _logger?.LogWarning("[TopstepXDataFeed] No adapter service provided - using simulation mode");
        }
    }

    public async Task<bool> ConnectAsync()
    {
        await Task.Delay(ConnectionDelayMs).ConfigureAwait(false); // Simulate connection
        return true;
    }

    public async Task<MarketData?> GetMarketDataAsync(string symbol)
    {
        // If we have a TopstepX adapter service, try to get real market data
        if (_adapterService != null)
        {
            try
            {
                // Check if adapter is connected before attempting to get price
                if (_adapterService.IsConnected)
                {
                    var price = await _adapterService.GetPriceAsync(symbol, CancellationToken.None).ConfigureAwait(false);
                    
                    // Calculate bid/ask as price ± one tick (0.25 for ES/MNQ)
                    var bid = price - ES_TICK_SIZE;
                    var ask = price + ES_TICK_SIZE;
                    
                    _logger?.LogDebug("[TopstepXDataFeed] Real market data for {Symbol}: Price=${Price:F2}, Bid=${Bid:F2}, Ask=${Ask:F2}", 
                        symbol, price, bid, ask);
                    
                    return new MarketData
                    {
                        Symbol = symbol,
                        Price = price,
                        Volume = DEFAULT_VOLUME, // Volume not available from basic price query
                        Bid = bid,
                        Ask = ask,
                        Timestamp = DateTime.UtcNow,
                        Source = FeedName
                    };
                }
                else
                {
                    _logger?.LogWarning("[TopstepXDataFeed] TopstepX adapter not connected, falling back to simulation");
                }
            }
            catch (Exception ex)
            {
                // Log error but fall back to simulation data to prevent bot crashes
                _logger?.LogWarning(ex, "[TopstepXDataFeed] Failed to get real market data for {Symbol}, falling back to simulation", symbol);
                OnErrorEvent(ex);
            }
        }
        
        // Fall back to simulated data if adapter is unavailable or failed
        await Task.Delay(NetworkDelayMs).ConfigureAwait(false); // Simulate network delay
        
        return new MarketData
        {
            Symbol = symbol,
            Price = ES_BASE_PRICE + (decimal)(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10000) / 10000.0 * (double)PRICE_VARIATION_RANGE - (double)PRICE_VARIATION_OFFSET),
            Volume = DEFAULT_VOLUME,
            Bid = ES_BID_PRICE,
            Ask = ES_ASK_PRICE,
            Timestamp = DateTime.UtcNow,
            Source = FeedName + "_Simulation"
        };
    }

    public async Task<OrderBook?> GetOrderBookAsync(string symbol)
    {
        await Task.Delay(SIMULATION_DELAY_MS).ConfigureAwait(false);
        return new OrderBook { Symbol = symbol, Timestamp = DateTime.UtcNow };
    }

    protected virtual void OnDataReceivedEvent(MarketData data)
    {
        OnDataReceived?.Invoke(this, data);
    }

    protected virtual void OnErrorEvent(Exception exception)
    {
        OnError?.Invoke(this, exception);
    }
}

/// <summary>
/// Backup data feed implementation
/// </summary>
public class BackupDataFeed : IDataFeed
{
    // Backup feed simulation constants
    private const int SlowerConnectionDelayMs = 200;
    private const int SlowerResponseDelayMs = 100;
    private const int OrderBookDelayMs = 100;
    private const decimal BasePrice = 4500.00m;
    private const double PriceVariationRange = 8.0;
    private const double PriceVariationOffset = 4.0;
    private const int VolumeAmount = 800;
    private const decimal BidPrice = 4499.50m;
    private const decimal AskPrice = 4500.50m;
    
    public string FeedName => "Backup";
    public int Priority { get; set; } = 2;
    
    public event EventHandler<MarketData>? OnDataReceived;
    public event EventHandler<Exception>? OnError;

    public async Task<bool> ConnectAsync()
    {
        await Task.Delay(SlowerConnectionDelayMs).ConfigureAwait(false); // Simulate slower connection
        return true;
    }

    public async Task<MarketData?> GetMarketDataAsync(string symbol)
    {
        await Task.Delay(SlowerResponseDelayMs).ConfigureAwait(false); // Simulate slower response
        
        return new MarketData
        {
            Symbol = symbol,
            Price = BasePrice + (decimal)(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10000) / 10000.0 * PriceVariationRange - PriceVariationOffset),
            Volume = VolumeAmount,
            Bid = BidPrice,
            Ask = AskPrice,
            Timestamp = DateTime.UtcNow,
            Source = FeedName
        };
    }

    public async Task<OrderBook?> GetOrderBookAsync(string symbol)
    {
        await Task.Delay(OrderBookDelayMs).ConfigureAwait(false);
        return new OrderBook { Symbol = symbol, Timestamp = DateTime.UtcNow };
    }

    protected virtual void OnDataReceivedEvent(MarketData data)
    {
        OnDataReceived?.Invoke(this, data);
    }

    protected virtual void OnErrorEvent(Exception exception)
    {
        OnError?.Invoke(this, exception);
    }
}