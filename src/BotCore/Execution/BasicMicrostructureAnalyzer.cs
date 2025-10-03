using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;

namespace BotCore.Execution;

/// <summary>
/// Basic microstructure analyzer that predicts execution costs and fill probabilities.
/// Uses spread, volatility, and volume analysis for execution recommendations.
/// Now fully configuration-driven instead of hardcoded parameters.
/// </summary>
public class BasicMicrostructureAnalyzer : IMicrostructureAnalyzer
{
    private readonly IMarketDataProvider _marketData;
    private readonly IExecutionGuardsConfig _executionConfig;
    private readonly IExecutionCostConfig _costConfig;
    private readonly Dictionary<string, List<ExecutionHistory>> _executionHistory = new();
    private readonly object _historyLock = new();

    public BasicMicrostructureAnalyzer(
        IMarketDataProvider marketData, 
        IExecutionGuardsConfig executionConfig,
        IExecutionCostConfig costConfig)
    {
        _marketData = marketData;
        _executionConfig = executionConfig;
        _costConfig = costConfig;
    }

    public async Task<MicrostructureState> AnalyzeCurrentStateAsync(string symbol, CancellationToken ct = default)
    {
        // Get configurable timeframes instead of hardcoded values
        var tradeAnalysisMinutes = _executionConfig.GetTradeAnalysisWindowMinutes();
        var volumeAnalysisMinutes = _executionConfig.GetVolumeAnalysisWindowMinutes();
        
        var quote = await _marketData.GetCurrentQuoteAsync(symbol, ct).ConfigureAwait(false);
        var recentTrades = await _marketData.GetRecentTradesAsync(symbol, TimeSpan.FromMinutes(tradeAnalysisMinutes), ct).ConfigureAwait(false);
        var volume = await _marketData.GetRecentVolumeAsync(symbol, TimeSpan.FromMinutes(volumeAnalysisMinutes), ct).ConfigureAwait(false);

        var spread = quote.AskPrice - quote.BidPrice;
        var midPrice = (quote.BidPrice + quote.AskPrice) / 2;
        var spreadBps = spread / midPrice * 10000;

        var volatility = CalculateVolatility(recentTrades);
        var microVolatility = CalculateMicroVolatility(recentTrades);
        var orderImbalance = CalculateOrderImbalance(quote.BidSize, quote.AskSize);
        var tickActivity = CalculateTickActivity(recentTrades);

        return new MicrostructureState
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            BidPrice = quote.BidPrice,
            AskPrice = quote.AskPrice,
            BidAskSpread = spread,
            SpreadBps = spreadBps,
            MidPrice = midPrice,
            BidSize = quote.BidSize,
            AskSize = quote.AskSize,
            OrderImbalance = orderImbalance,
            RecentVolume = volume,
            VolumeRate = volume, // Volume per minute
            Volatility = volatility,
            MicroVolatility = microVolatility,
            TickActivity = tickActivity,
            IsVolatile = microVolatility > _executionConfig.GetMicroVolatilityThreshold(),
            IsLiquid = spreadBps < _executionConfig.GetMaxSpreadBps() && volume > _executionConfig.GetMinVolumeThreshold(),
            Session = DetermineMarketSession(),
            RecentTrades = recentTrades.Select(t => new RecentTrade
            {
                Timestamp = t.Timestamp,
                Price = t.Price,
                Size = t.Size,
                WasBuyerInitiated = t.Price >= midPrice
            }).ToList()
        };
    }

    public async Task<decimal> PredictMarketOrderSlippageAsync(
        string symbol,
        decimal quantity,
        bool isBuy,
        MicrostructureState? currentState = null,
        CancellationToken ct = default)
    {
        currentState ??= await AnalyzeCurrentStateAsync(symbol, ct).ConfigureAwait(false);

        // Base slippage from spread
        var baseSlippageBps = currentState.SpreadBps / 2;

        // Market impact based on order size vs average size
        var avgTradeSize = currentState.RecentTrades.Any()
            ? currentState.RecentTrades.Average(t => t.Size)
            : _costConfig.GetDefaultTradeSize();

        var sizeRatio = (decimal)quantity / (decimal)Math.Max(1, avgTradeSize);
        var maxMarketImpactBps = _costConfig.GetMaxMarketImpactBps();
        var impactMultiplier = _costConfig.GetMarketImpactMultiplier();
        var marketImpactBps = Math.Min(maxMarketImpactBps, sizeRatio * (decimal)impactMultiplier);

        // Volatility adjustment
        var volatilityAdjustment = currentState.IsVolatile ? 3m : 0m;

        // Session adjustment using configuration
        var sessionAdjustment = currentState.Session switch
        {
            MarketSession.OpeningAuction => _costConfig.GetOpeningAuctionAdjustment(),
            MarketSession.ClosingAuction => _costConfig.GetClosingAuctionAdjustment(),
            MarketSession.AfterHours => _costConfig.GetAfterHoursAdjustment(),
            _ => 0m
        };

        // Order imbalance adjustment using configuration
        var imbalanceMultiplier = _costConfig.GetImbalanceAdjustmentMultiplier();
        var imbalanceAdjustment = isBuy
            ? Math.Max(0, -currentState.OrderImbalance * imbalanceMultiplier) // Penalty for buying when asks dominate
            : Math.Max(0, currentState.OrderImbalance * imbalanceMultiplier);  // Penalty for selling when bids dominate

        var totalSlippageBps = baseSlippageBps + marketImpactBps + volatilityAdjustment +
                               sessionAdjustment + imbalanceAdjustment;

        var minSlippage = _costConfig.GetMinSlippageBps();
        return Math.Max(minSlippage, totalSlippageBps);
    }

    public async Task<decimal> EstimateLimitOrderFillProbabilityAsync(
        string symbol,
        decimal limitPrice,
        decimal quantity,
        bool isBuy,
        TimeSpan timeHorizon,
        MicrostructureState? currentState = null,
        CancellationToken ct = default)
    {
        currentState ??= await AnalyzeCurrentStateAsync(symbol, ct).ConfigureAwait(false);

        // Distance from current market
        var marketPrice = isBuy ? currentState.AskPrice : currentState.BidPrice;
        var priceDistance = isBuy
            ? (marketPrice - limitPrice) / currentState.MidPrice
            : (limitPrice - marketPrice) / currentState.MidPrice;

        // Base probability using configuration-driven thresholds (fail-closed requirement)
        var baseProbability = CalculateBaseProbabilityFromConfig(priceDistance);

        // Time adjustment using configuration
        var timeMultiplier = Math.Min(_costConfig.GetMaxTimeMultiplier(), 
            (decimal)timeHorizon.TotalMinutes / _costConfig.GetTimeMultiplierBaseline());

        // Volatility boost (more likely to fill in volatile markets)
        var volatilityBoost = currentState.IsVolatile ? _costConfig.GetVolatilityBoost() : 0m;

        // Volume boost (more likely to fill in active markets)
        var volumeThreshold = _costConfig.GetVolumeThreshold();
        var volumeBoost = currentState.VolumeRate > volumeThreshold ? _costConfig.GetVolumeBoost() : 0m;

        var maxProbability = _costConfig.GetMaxFillProbability();
        var minProbability = _costConfig.GetMinFillProbability();
        
        var adjustedProbability = Math.Min(maxProbability,
            baseProbability * timeMultiplier + volatilityBoost + volumeBoost);

        return Math.Max(minProbability, adjustedProbability);
    }

    public async Task<ExecutionRecommendation> GetExecutionRecommendationAsync(
        TradeIntent intent,
        MicrostructureState? currentState = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        
        currentState ??= await AnalyzeCurrentStateAsync(intent.Symbol, ct).ConfigureAwait(false);

        // Get predictions
        var marketSlippage = await PredictMarketOrderSlippageAsync(
            intent.Symbol, intent.Quantity, intent.IsBuy, currentState, ct).ConfigureAwait(false);

        // Limit fill probability calculation (reserved for future routing decision logic)
        _ = intent.LimitPrice.HasValue
            ? await EstimateLimitOrderFillProbabilityAsync(
                intent.Symbol, intent.LimitPrice.Value, intent.Quantity,
                intent.IsBuy, intent.MaxWaitTime, currentState, ct).ConfigureAwait(false)
            : 0m;

        // Calculate expected values
        var marketOrderEV = CalculateExpectedValue(intent, marketSlippage, 1.0m);

        var bestLimitPrice = GetOptimalLimitPrice(intent, currentState);
        var limitOrderSlippage = CalculateLimitOrderSlippage(intent, bestLimitPrice, currentState);
        var limitOrderFillProb = await EstimateLimitOrderFillProbabilityAsync(
            intent.Symbol, bestLimitPrice, intent.Quantity,
            intent.IsBuy, intent.MaxWaitTime, currentState, ct).ConfigureAwait(false);
        var limitOrderEV = CalculateExpectedValue(intent, limitOrderSlippage, limitOrderFillProb);

        // Choose optimal strategy
        var recommendation = ChooseOptimalStrategy(
            intent, marketOrderEV, limitOrderEV,
            marketSlippage, limitOrderSlippage, limitOrderFillProb, bestLimitPrice);

        // Record execution for learning
        RecordExecutionDecision(intent, recommendation, currentState);

        return recommendation;
    }

    private static decimal CalculateExpectedValue(TradeIntent intent, decimal slippageBps, decimal fillProbability)
    {
        // EV = p(fill) * [p(win) * avgWin - (1-p(win)) * avgLoss] - expectedSlippageCost
        var grossEV = intent.ExpectedWinRate * intent.RMultiple - (1 - intent.ExpectedWinRate) * 1m;
        var slippageCost = slippageBps / 10000m; // Convert bps to decimal

        return fillProbability * grossEV - slippageCost;
    }

    private decimal GetOptimalLimitPrice(TradeIntent intent, MicrostructureState state)
    {
        if (intent.LimitPrice.HasValue)
            return intent.LimitPrice.Value;

        // Default to midpoint or slightly better using configuration
        var spreadPenetration = _costConfig.GetDefaultSpreadPenetration();
        var offset = state.BidAskSpread * spreadPenetration;
        return intent.IsBuy
            ? state.BidPrice + offset
            : state.AskPrice - offset;
    }

    private decimal CalculateLimitOrderSlippage(TradeIntent intent, decimal limitPrice, MicrostructureState state)
    {
        var marketPrice = intent.IsBuy ? state.AskPrice : state.BidPrice;
        var savings = intent.IsBuy
            ? (marketPrice - limitPrice) / state.MidPrice * 10000
            : (limitPrice - marketPrice) / state.MidPrice * 10000;

        var maxSlippageCap = _costConfig.GetMaxSlippageCap();
        return Math.Max(maxSlippageCap, -savings); // Negative means savings, cap at configured max
    }

    private static ExecutionRecommendation ChooseOptimalStrategy(
        TradeIntent intent,
                decimal marketEV,
        decimal limitEV,
        decimal marketSlippage,
        decimal limitSlippage,
        decimal limitFillProb,
        decimal limitPrice)
    {
        var orderType = OrderType.Market;
        var recommendedPrice = (decimal?)null;
        var expectedSlippage = marketSlippage;
        var fillProb = 1.0m;
        var expectedEV = marketEV;
        var reasoning = "Market order for immediate execution";

        // Prefer limit order if significantly better EV and reasonable fill probability
        if (limitEV > marketEV * 1.02m && limitFillProb > 0.7m)
        {
            orderType = OrderType.Limit;
            recommendedPrice = limitPrice;
            expectedSlippage = limitSlippage;
            fillProb = limitFillProb;
            expectedEV = limitEV;
            reasoning = $"Limit order offers {(limitEV - marketEV) * 100:F1}% better EV";
        }

        // Force market order for urgent execution
        if (intent.Urgency >= ExecutionUrgency.High)
        {
            orderType = OrderType.Market;
            recommendedPrice = null;
            expectedSlippage = marketSlippage;
            fillProb = 1.0m;
            expectedEV = marketEV;
            reasoning = "Market order due to high urgency";
        }

        // Risk assessment
        var risk = expectedSlippage switch
        {
            > 15m => ExecutionRisk.Extreme,
            > 8m => ExecutionRisk.High,
            > 4m => ExecutionRisk.Medium,
            _ => ExecutionRisk.Low
        };

        return new ExecutionRecommendation
        {
            RecommendedOrderType = orderType,
            LimitPrice = recommendedPrice,
            PredictedSlippageBps = expectedSlippage,
            FillProbability = fillProb,
            ExpectedCostBps = Math.Max(0, expectedSlippage),
            ExpectedValue = expectedEV,
            Reasoning = reasoning,
            RiskAssessment = risk,
            EstimatedFillTime = orderType == OrderType.Market
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromMinutes((double)(5 / Math.Max(0.1m, fillProb)))
        };
    }

    private void RecordExecutionDecision(
        TradeIntent intent,
        ExecutionRecommendation recommendation,
        MicrostructureState state)
    {
        lock (_historyLock)
        {
            if (!_executionHistory.ContainsKey(intent.Symbol))
                _executionHistory[intent.Symbol] = new List<ExecutionHistory>();

            _executionHistory[intent.Symbol].Add(new ExecutionHistory
            {
                Timestamp = DateTime.UtcNow,
                Intent = intent,
                Recommendation = recommendation,
                MarketState = state
            });

            // Keep only recent history
            var history = _executionHistory[intent.Symbol];
            if (history.Count > 1000)
            {
                history.RemoveRange(0, history.Count - 1000);
            }
        }
    }

    private static decimal CalculateVolatility(List<Trade> trades)
    {
        if (trades.Count < 2) return 0m;

        var returns = trades.Zip(trades.Skip(1), (prev, curr) =>
            Math.Log((double)(curr.Price / prev.Price))).ToList();

        if (!returns.Any()) return 0m;

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / returns.Count;

        return (decimal)Math.Sqrt(variance) * (decimal)Math.Sqrt(252 * 24 * 60); // Annualized
    }

    private static decimal CalculateMicroVolatility(List<Trade> trades)
    {
        if (trades.Count < 10) return 0m;

        var recentTrades = trades.TakeLast(20).ToList();
        var priceRange = recentTrades.Max(t => t.Price) - recentTrades.Min(t => t.Price);
        var midPrice = (recentTrades.Max(t => t.Price) + recentTrades.Min(t => t.Price)) / 2;

        return priceRange / midPrice;
    }

    private static decimal CalculateOrderImbalance(long bidSize, long askSize)
    {
        var total = bidSize + askSize;
        return total > 0 ? (decimal)(bidSize - askSize) / total : 0m;
    }

    private static decimal CalculateTickActivity(List<Trade> trades)
    {
        var timeSpan = TimeSpan.FromMinutes(1);
        var recentTrades = trades.Where(t => DateTime.UtcNow - t.Timestamp <= timeSpan);
        return recentTrades.Count();
    }

    private static MarketSession DetermineMarketSession()
    {
        var time = DateTime.Now.TimeOfDay;

        return time switch
        {
            var t when t < TimeSpan.FromHours(9.5) => MarketSession.PreMarket,
            var t when t < TimeSpan.FromHours(9.75) => MarketSession.OpeningAuction,
            var t when t < TimeSpan.FromHours(15.75) => MarketSession.RegularTrading,
            var t when t < TimeSpan.FromHours(16) => MarketSession.ClosingAuction,
            _ => MarketSession.AfterHours
        };
    }

    /// <summary>
    /// Calculate base fill probability from configuration-driven thresholds (fail-closed requirement)
    /// </summary>
    private decimal CalculateBaseProbabilityFromConfig(decimal priceDistance)
    {
        // Use configuration-driven probability thresholds to avoid hardcoded values
        var thresholds = _costConfig.GetFillProbabilityThresholds();
        
        // At or better than market
        if (priceDistance <= 0)
            return thresholds.AtMarketProbability;

        // Check distance thresholds in order
        foreach (var threshold in thresholds.DistanceThresholds.OrderBy(t => t.MaxDistance))
        {
            if (priceDistance <= (decimal)threshold.MaxDistance)
                return threshold.FillProbability;
        }

        // Very passive - use fallback probability
        return thresholds.PassiveFallbackProbability;
    }
}

/// <summary>
/// Execution history for learning and improvement
/// </summary>
internal sealed record ExecutionHistory
{
    public DateTime Timestamp { get; init; }
    public TradeIntent Intent { get; init; } = null!;
    public ExecutionRecommendation Recommendation { get; init; } = null!;
    public MicrostructureState MarketState { get; init; } = null!;
}

/// <summary>
/// Market data provider interface
/// </summary>
public interface IMarketDataProvider
{
    Task<Quote> GetCurrentQuoteAsync(string symbol, CancellationToken ct = default);
    Task<List<Trade>> GetRecentTradesAsync(string symbol, TimeSpan lookback, CancellationToken ct = default);
    Task<decimal> GetRecentVolumeAsync(string symbol, TimeSpan lookback, CancellationToken ct = default);
}

/// <summary>
/// Market quote data
/// </summary>
public record Quote
{
    public string Symbol { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public long BidSize { get; init; }
    public long AskSize { get; init; }
}

/// <summary>
/// Trade data
/// </summary>
public record Trade
{
    public DateTime Timestamp { get; init; }
    public decimal Price { get; init; }
    public long Size { get; init; }
}
