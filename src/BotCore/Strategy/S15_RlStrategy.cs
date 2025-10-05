using System;
using System.Collections.Generic;
using System.Linq;
using BotCore.Features;
using BotCore.Models;
using BotCore.Risk;

namespace BotCore.Strategy;

/// <summary>
/// S15_RL Strategy - Reinforcement Learning based trading strategy.
/// Competes with S2/S3/S6/S11 strategies within the S7 gate regime.
/// Uses ONNX model for action prediction based on 12 standardized features.
/// </summary>
public static class S15_RlStrategy
{
    // Strategy configuration constants (config-driven)
    private const decimal DefaultAtrMultiplierStop = 2.0m;      // ATR multiplier for stop loss
    private const decimal DefaultAtrMultiplierTarget = 3.0m;    // ATR multiplier for target
    private const decimal MinAtrForTrade = 0.25m;               // Minimum ATR required for trade
    private const decimal DefaultConfidenceThreshold = 0.6m;    // Minimum confidence for trade
    private const int DefaultQty = 1;                           // Default position size
    
    /// <summary>
    /// Generate trading candidates using RL policy inference.
    /// </summary>
    /// <param name="symbol">Trading symbol (e.g., "ES", "NQ")</param>
    /// <param name="env">Market environment data</param>
    /// <param name="levels">Order book levels data</param>
    /// <param name="bars">Historical price bars</param>
    /// <param name="risk">Risk management engine</param>
    /// <param name="policy">RL policy for action prediction</param>
    /// <param name="featureBuilder">Feature builder for standardized features</param>
    /// <param name="currentPos">Current position (-1 short, 0 flat, 1 long)</param>
    /// <param name="currentTime">Current time for session detection</param>
    /// <returns>List of trading candidates</returns>
    public static List<Candidate> GenerateCandidates(
        string symbol,
        Env env,
        Levels levels,
        IList<Bar> bars,
        RiskEngine risk,
        IRlPolicy policy,
        FeatureBuilder featureBuilder,
        int currentPos,
        DateTime currentTime)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(featureBuilder);

        var candidates = new List<Candidate>();

        // Require minimum bars for feature computation
        if (bars.Count < 20)
        {
            return candidates;
        }

        // Get ATR from environment
        var atr = env.atr ?? 0m;
        if (atr < MinAtrForTrade)
        {
            // Insufficient volatility for trading
            return candidates;
        }

        // Get current price
        var lastBar = bars[^1];
        var currentPrice = lastBar.Close;

        // Build feature vector
        decimal[] features;
        try
        {
            // Convert Models.Bar (IList) to Market.Bar (List) for FeatureBuilder
            // Models.Bar doesn't have End, so use Start + 1 minute as approximation
            var marketBars = new List<Market.Bar>();
            foreach (var bar in bars)
            {
                var endTime = bar.Start.AddMinutes(1); // Assume 1-minute bars
                marketBars.Add(new Market.Bar(bar.Start, endTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
            }
            features = featureBuilder.BuildFeatures(symbol, marketBars, currentPos, currentTime, env, levels);
        }
        catch (Exception)
        {
            // Feature computation failed, return no candidates
            return candidates;
        }

        // Get action prediction and confidence from policy
        int action;
        decimal confidence;
        try
        {
            action = policy.PredictAction(features);
            confidence = policy.GetConfidence(features);
        }
        catch (Exception)
        {
            // Policy inference failed, return no candidates
            return candidates;
        }

        // Check confidence threshold
        if (confidence < DefaultConfidenceThreshold)
        {
            // Confidence too low, no trade
            return candidates;
        }

        // Generate candidate based on action
        if (action == 1) // Long
        {
            var entry = currentPrice;
            var stop = entry - (DefaultAtrMultiplierStop * atr);
            var target = entry + (DefaultAtrMultiplierTarget * atr);

            // Validate risk-reward
            var risk_amount = Math.Abs(entry - stop);
            var reward_amount = Math.Abs(target - entry);
            if (risk_amount <= 0 || reward_amount / risk_amount < 1.0m)
            {
                return candidates; // Poor risk-reward ratio
            }

            var candidate = new Candidate
            {
                strategy_id = "S15_RL",
                symbol = symbol,
                side = Side.BUY,
                entry = entry,
                stop = stop,
                t1 = target,
                expR = reward_amount / risk_amount,
                qty = DefaultQty,
                atr_ok = true,
                vol_z = env.volz,
                Score = (decimal)confidence * 100, // Use confidence as score
                QScore = confidence // Quality score for session gating
            };

            candidates.Add(candidate);
        }
        else if (action == -1) // Short
        {
            var entry = currentPrice;
            var stop = entry + (DefaultAtrMultiplierStop * atr);
            var target = entry - (DefaultAtrMultiplierTarget * atr);

            // Validate risk-reward
            var risk_amount = Math.Abs(entry - stop);
            var reward_amount = Math.Abs(target - entry);
            if (risk_amount <= 0 || reward_amount / risk_amount < 1.0m)
            {
                return candidates; // Poor risk-reward ratio
            }

            var candidate = new Candidate
            {
                strategy_id = "S15_RL",
                symbol = symbol,
                side = Side.SELL,
                entry = entry,
                stop = stop,
                t1 = target,
                expR = reward_amount / risk_amount,
                qty = DefaultQty,
                atr_ok = true,
                vol_z = env.volz,
                Score = (decimal)confidence * 100, // Use confidence as score
                QScore = confidence // Quality score for session gating
            };

            candidates.Add(candidate);
        }
        // else action == 0 (flat/hold), return empty list

        return candidates;
    }
}
