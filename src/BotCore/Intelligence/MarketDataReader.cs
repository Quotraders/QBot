using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using System.Text.Json;

namespace BotCore.Intelligence;

/// <summary>
/// Service for reading market data from workflow-generated files
/// Reads parquet files for features and JSON files for news/events
/// </summary>
public class MarketDataReader
{
    private readonly ILogger<MarketDataReader> _logger;
    private readonly string _datasetsPath;
    private readonly string _telemetryPath;
    private Dictionary<string, object>? _cachedMarketData;
    private Dictionary<string, object>? _cachedFedData;
    private DateTime _lastMarketDataRead = DateTime.MinValue;
    private DateTime _lastFedDataRead = DateTime.MinValue;

    public MarketDataReader(ILogger<MarketDataReader> logger)
    {
        _logger = logger;
        _datasetsPath = Path.Combine(Directory.GetCurrentDirectory(), "datasets");
        _telemetryPath = Path.Combine(Directory.GetCurrentDirectory(), "telemetry");
    }

    /// <summary>
    /// Gets latest market data from parquet files
    /// Returns dictionary with SPX, NDX, VIX, VIX9D, VIX3M, TNX, IRX, DXY prices and VIX term structure
    /// </summary>
    public async Task<Dictionary<string, object>?> GetLatestMarketDataAsync()
    {
        try
        {
            var marketFeaturesPath = Path.Combine(_datasetsPath, "features", "market_features.parquet");
            
            if (!File.Exists(marketFeaturesPath))
            {
                _logger.LogDebug("[INTELLIGENCE] Market features file not found: {Path}", marketFeaturesPath);
                return _cachedMarketData;
            }

            var fileInfo = new FileInfo(marketFeaturesPath);
            if (fileInfo.LastWriteTimeUtc <= _lastMarketDataRead && _cachedMarketData != null)
            {
                _logger.LogDebug("[INTELLIGENCE] Using cached market data");
                return _cachedMarketData;
            }

            using var stream = File.OpenRead(marketFeaturesPath);
            using var parquetReader = await ParquetReader.CreateAsync(stream).ConfigureAwait(false);
            
            var result = new Dictionary<string, object>();
            
            // Read all row groups
            for (int i = 0; i < parquetReader.RowGroupCount; i++)
            {
                using var groupReader = parquetReader.OpenRowGroupReader(i);
                var fields = await parquetReader.Schema.GetDataFieldsAsync().ConfigureAwait(false);
                
                foreach (var field in fields)
                {
                    var column = await groupReader.ReadColumnAsync(field).ConfigureAwait(false);
                    var data = column.Data;
                    
                    // Get the last value from the column (most recent)
                    if (data.Length > 0)
                    {
                        var lastValue = data.GetValue(data.Length - 1);
                        if (lastValue != null)
                        {
                            result[field.Name] = lastValue;
                        }
                    }
                }
            }
            
            // Calculate VIX term structure if we have VIX, VIX9D, VIX3M
            if (result.ContainsKey("VIX") && result.ContainsKey("VIX9D") && result.ContainsKey("VIX3M"))
            {
                var vix = Convert.ToDecimal(result["VIX"]);
                var vix9d = Convert.ToDecimal(result["VIX9D"]);
                var vix3m = Convert.ToDecimal(result["VIX3M"]);
                
                // Calculate term structure
                if (vix9d > vix)
                {
                    result["VIXTermStructure"] = "contango";
                }
                else if (vix9d < vix)
                {
                    result["VIXTermStructure"] = "backwardation";
                }
                else
                {
                    result["VIXTermStructure"] = "flat";
                }
            }
            
            _cachedMarketData = result;
            _lastMarketDataRead = DateTime.UtcNow;
            
            _logger.LogInformation("[INTELLIGENCE] Market data loaded with {Count} fields", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[INTELLIGENCE] Failed to read market features, using cached data");
            return _cachedMarketData;
        }
    }

    /// <summary>
    /// Gets latest Fed balance sheet data from parquet files
    /// Returns Fed total assets, securities held, reserve balances with week-over-week changes
    /// </summary>
    public async Task<Dictionary<string, object>?> GetLatestFedDataAsync()
    {
        try
        {
            var fedBalanceSheetPath = Path.Combine(_datasetsPath, "features", "fed_balance_sheet.parquet");
            
            if (!File.Exists(fedBalanceSheetPath))
            {
                _logger.LogDebug("[INTELLIGENCE] Fed balance sheet file not found: {Path}", fedBalanceSheetPath);
                return _cachedFedData;
            }

            var fileInfo = new FileInfo(fedBalanceSheetPath);
            if (fileInfo.LastWriteTimeUtc <= _lastFedDataRead && _cachedFedData != null)
            {
                _logger.LogDebug("[INTELLIGENCE] Using cached Fed data");
                return _cachedFedData;
            }

            using var stream = File.OpenRead(fedBalanceSheetPath);
            using var parquetReader = await ParquetReader.CreateAsync(stream).ConfigureAwait(false);
            
            var result = new Dictionary<string, object>();
            var allRows = new List<Dictionary<string, object>>();
            
            // Read all row groups
            for (int i = 0; i < parquetReader.RowGroupCount; i++)
            {
                using var groupReader = parquetReader.OpenRowGroupReader(i);
                var fields = await parquetReader.Schema.GetDataFieldsAsync().ConfigureAwait(false);
                
                // Read all columns for this group
                var columns = new Dictionary<string, Array>();
                foreach (var field in fields)
                {
                    var column = await groupReader.ReadColumnAsync(field).ConfigureAwait(false);
                    columns[field.Name] = column.Data;
                }
                
                // Build rows from columns
                if (columns.Count > 0)
                {
                    var rowCount = columns.First().Value.Length;
                    for (int r = 0; r < rowCount; r++)
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var kvp in columns)
                        {
                            var value = kvp.Value.GetValue(r);
                            if (value != null)
                            {
                                row[kvp.Key] = value;
                            }
                        }
                        allRows.Add(row);
                    }
                }
            }
            
            if (allRows.Count == 0)
            {
                return _cachedFedData;
            }
            
            // Get latest and previous week data
            var latestRow = allRows[allRows.Count - 1];
            result["TotalAssets"] = latestRow.ContainsKey("TotalAssets") ? latestRow["TotalAssets"] : 0;
            result["SecuritiesHeld"] = latestRow.ContainsKey("SecuritiesHeld") ? latestRow["SecuritiesHeld"] : 0;
            result["ReserveBalances"] = latestRow.ContainsKey("ReserveBalances") ? latestRow["ReserveBalances"] : 0;
            
            // Calculate week-over-week changes if we have enough data
            if (allRows.Count > 1)
            {
                var previousRow = allRows[allRows.Count - 2];
                
                if (latestRow.ContainsKey("TotalAssets") && previousRow.ContainsKey("TotalAssets"))
                {
                    var currentAssets = Convert.ToDecimal(latestRow["TotalAssets"]);
                    var previousAssets = Convert.ToDecimal(previousRow["TotalAssets"]);
                    result["TotalAssetsWoWChange"] = currentAssets - previousAssets;
                }
                
                result["QTStatus"] = result.ContainsKey("TotalAssetsWoWChange") && 
                                    Convert.ToDecimal(result["TotalAssetsWoWChange"]) < 0 
                                    ? "contracting" : "expanding";
            }
            
            _cachedFedData = result;
            _lastFedDataRead = DateTime.UtcNow;
            
            _logger.LogInformation("[INTELLIGENCE] Fed data loaded with {Count} fields", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[INTELLIGENCE] Failed to read Fed balance sheet, using cached data");
            return _cachedFedData;
        }
    }

    /// <summary>
    /// Gets news sentiment summary from JSON files
    /// Returns percentages: bullish/bearish/neutral plus top headlines
    /// </summary>
    public async Task<(decimal Bullish, decimal Bearish, decimal Neutral, List<string> TopHeadlines)?> GetNewsSentimentSummaryAsync()
    {
        try
        {
            var newsFlagsPath = Path.Combine(_datasetsPath, "news_flags");
            
            if (!Directory.Exists(newsFlagsPath))
            {
                _logger.LogDebug("[INTELLIGENCE] News flags directory not found: {Path}", newsFlagsPath);
                return null;
            }

            var jsonFiles = Directory.GetFiles(newsFlagsPath, "*.json");
            if (jsonFiles.Length == 0)
            {
                _logger.LogDebug("[INTELLIGENCE] No news JSON files found");
                return null;
            }

            int bullishCount = 0;
            int bearishCount = 0;
            int neutralCount = 0;
            var allHeadlines = new List<(string headline, DateTime timestamp, string sentiment)>();

            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    var newsData = JsonSerializer.Deserialize<JsonElement>(json);
                    
                    if (newsData.ValueKind == JsonValueKind.Object)
                    {
                        // Extract sentiment
                        if (newsData.TryGetProperty("sentiment", out var sentiment))
                        {
                            var sentimentValue = sentiment.GetString()?.ToLower() ?? "neutral";
                            if (sentimentValue.Contains("bull"))
                                bullishCount++;
                            else if (sentimentValue.Contains("bear"))
                                bearishCount++;
                            else
                                neutralCount++;
                        }
                        
                        // Extract headline
                        if (newsData.TryGetProperty("headline", out var headline))
                        {
                            var headlineText = headline.GetString() ?? "";
                            var timestamp = DateTime.UtcNow;
                            
                            if (newsData.TryGetProperty("timestamp", out var ts))
                            {
                                DateTime.TryParse(ts.GetString(), out timestamp);
                            }
                            
                            var sentimentStr = newsData.TryGetProperty("sentiment", out var s) ? 
                                s.GetString() ?? "neutral" : "neutral";
                            
                            allHeadlines.Add((headlineText, timestamp, sentimentStr));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[INTELLIGENCE] Failed to parse news file: {File}", file);
                }
            }

            var total = bullishCount + bearishCount + neutralCount;
            if (total == 0)
            {
                return null;
            }

            var bullishPct = (decimal)bullishCount / total * 100;
            var bearishPct = (decimal)bearishCount / total * 100;
            var neutralPct = (decimal)neutralCount / total * 100;
            
            // Get top 3 headlines sorted by timestamp
            var topHeadlines = allHeadlines
                .OrderByDescending(h => h.timestamp)
                .Take(3)
                .Select(h => h.headline)
                .ToList();

            _logger.LogInformation("[INTELLIGENCE] News sentiment: {Bullish}% bullish, {Bearish}% bearish, {Neutral}% neutral",
                bullishPct, bearishPct, neutralPct);
            
            return (bullishPct, bearishPct, neutralPct, topHeadlines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[INTELLIGENCE] Failed to read news sentiment");
            return null;
        }
    }

    /// <summary>
    /// Gets upcoming high-impact economic events from ForexFactory calendar
    /// Returns events in next 7 days sorted by impact level
    /// </summary>
    public async Task<List<Dictionary<string, object>>?> GetUpcomingEconomicEventsAsync()
    {
        try
        {
            var calendarPath = Path.Combine(_datasetsPath, "economic_calendar", "forexfactory_events.json");
            
            if (!File.Exists(calendarPath))
            {
                _logger.LogDebug("[INTELLIGENCE] Economic calendar file not found: {Path}", calendarPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(calendarPath).ConfigureAwait(false);
            var events = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            
            if (events == null || events.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var sevenDaysFromNow = now.AddDays(7);
            var upcomingEvents = new List<Dictionary<string, object>>();

            foreach (var evt in events)
            {
                if (evt.TryGetValue("date", out var dateElement))
                {
                    if (DateTime.TryParse(dateElement.GetString(), out var eventDate))
                    {
                        if (eventDate >= now && eventDate <= sevenDaysFromNow)
                        {
                            var eventDict = new Dictionary<string, object>
                            {
                                ["date"] = eventDate
                            };
                            
                            foreach (var kvp in evt)
                            {
                                if (kvp.Key != "date")
                                {
                                    eventDict[kvp.Key] = kvp.Value.ToString();
                                }
                            }
                            
                            upcomingEvents.Add(eventDict);
                        }
                    }
                }
            }

            // Sort by impact level (high -> medium -> low)
            upcomingEvents = upcomingEvents
                .OrderBy(e => {
                    if (e.ContainsKey("impact"))
                    {
                        var impact = e["impact"].ToString()?.ToLower() ?? "";
                        if (impact == "high") return 0;
                        if (impact == "medium") return 1;
                        return 2;
                    }
                    return 3;
                })
                .ThenBy(e => (DateTime)e["date"])
                .ToList();

            _logger.LogInformation("[INTELLIGENCE] Found {Count} upcoming economic events", upcomingEvents.Count);
            return upcomingEvents;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[INTELLIGENCE] Failed to read economic calendar");
            return null;
        }
    }

    /// <summary>
    /// Gets system health from telemetry files
    /// Returns workflow success rates and failing components
    /// </summary>
    public async Task<Dictionary<string, object>?> GetSystemHealthAsync()
    {
        try
        {
            var systemMetricsPath = Path.Combine(_telemetryPath, "system_metrics.json");
            
            if (!File.Exists(systemMetricsPath))
            {
                _logger.LogDebug("[INTELLIGENCE] System metrics file not found: {Path}", systemMetricsPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(systemMetricsPath).ConfigureAwait(false);
            var metrics = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            if (metrics == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>();
            
            foreach (var kvp in metrics)
            {
                result[kvp.Key] = kvp.Value.ToString();
            }

            _logger.LogInformation("[INTELLIGENCE] System health loaded with {Count} metrics", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[INTELLIGENCE] Failed to read system metrics");
            return null;
        }
    }
}
