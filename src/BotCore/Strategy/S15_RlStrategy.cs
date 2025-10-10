using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BotCore.Features;
using BotCore.Models;
using BotCore.Risk;

namespace BotCore.Strategy;

/// <summary>
/// S15_RL Strategy - Reinforcement Learning based trading strategy.
/// SHADOW-ONLY MODE: Records predictions and learns continuously without emitting live orders.
/// Competes with S2/S3/S6/S11 strategies within the S7 gate regime.
/// Uses ONNX model for action prediction based on 12 standardized features.
/// 
/// Configuration:
/// - S15_TRADING_ENABLED=0 : Shadow mode (default) - records predictions, no live orders
/// - S15_SHADOW_LEARNING_ENABLED=1 : Continuous learning from observations
/// </summary>
public static class S15RlStrategy
{
    // Strategy configuration constants (config-driven)
    private const decimal DefaultAtrMultiplierStop = 2.0m;      // ATR multiplier for stop loss
    private const decimal DefaultAtrMultiplierTarget = 3.0m;    // ATR multiplier for target
    private const decimal MinAtrForTrade = 0.25m;               // Minimum ATR required for trade
    private const decimal DefaultConfidenceThreshold = 0.6m;    // Minimum confidence for trade
    private const int DefaultQty = 1;                           // Default position size
    private const int MinimumBarsRequired = 20;                 // Minimum bars for feature computation
    private const decimal MinimumRiskRewardRatio = 1.0m;        // Minimum acceptable risk-reward ratio
    
    // Shadow-only mode flag (environment-driven)
    private static bool IsTradingEnabled => 
        Environment.GetEnvironmentVariable("S15_TRADING_ENABLED")?.Trim() == "1";
    
    private static bool IsShadowLearningEnabled => 
        Environment.GetEnvironmentVariable("S15_SHADOW_LEARNING_ENABLED")?.Trim() == "1";
    
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
    public static IReadOnlyList<Candidate> GenerateCandidates(
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
        if (bars.Count < MinimumBarsRequired)
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
        catch (ArgumentException)
        {
            // Invalid arguments for feature computation
            return candidates;
        }
        catch (InvalidOperationException)
        {
            // Feature computation state error
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
        catch (ArgumentException)
        {
            // Invalid feature vector for policy inference
            return candidates;
        }
        catch (InvalidOperationException)
        {
            // Policy inference state error
            return candidates;
        }

        // Check confidence threshold
        if (confidence < DefaultConfidenceThreshold)
        {
            // Confidence too low, no trade - but log for shadow learning
            if (IsShadowLearningEnabled)
            {
                LogShadowPrediction(symbol, currentTime, action, confidence, currentPrice, "low_confidence");
            }
            return candidates;
        }

        // Shadow learning: always record prediction regardless of trading mode
        if (IsShadowLearningEnabled)
        {
            LogShadowPrediction(symbol, currentTime, action, confidence, currentPrice, "prediction");
        }

        // If trading is disabled (shadow-only mode), skip candidate emission but continue learning
        if (!IsTradingEnabled)
        {
            // Log that we're in shadow mode and would have traded
            Console.WriteLine($"[S15-RL-SHADOW] Would trade {symbol}: action={action}, confidence={confidence:F2}, price={currentPrice} (trading disabled)");
            return candidates; // Return empty list - no live orders
        }

        // Generate candidate based on action (only if trading is enabled)
        if (action == 1) // Long
        {
            var entry = currentPrice;
            var stop = entry - (DefaultAtrMultiplierStop * atr);
            var target = entry + (DefaultAtrMultiplierTarget * atr);

            // Validate risk-reward
            var risk_amount = Math.Abs(entry - stop);
            var reward_amount = Math.Abs(target - entry);
            if (risk_amount <= 0 || reward_amount / risk_amount < MinimumRiskRewardRatio)
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
                Score = confidence * 100, // Use confidence as score
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
            if (risk_amount <= 0 || reward_amount / risk_amount < MinimumRiskRewardRatio)
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
                Score = confidence * 100, // Use confidence as score
                QScore = confidence // Quality score for session gating
            };

            candidates.Add(candidate);
        }
        // else action == 0 (flat/hold), return empty list

        return candidates;
    }
    
    /// <summary>
    /// Log shadow prediction for learning without executing trades.
    /// Writes to shadow learning log for continuous model improvement.
    /// </summary>
    private static void LogShadowPrediction(string symbol, DateTime time, int action, decimal confidence, decimal price, string reason)
    {
        try
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "shadow_learning");
            Directory.CreateDirectory(logDir);
            
            var logFile = Path.Combine(logDir, $"s15_shadow_{DateTime.UtcNow:yyyyMMdd}.log");
            var logEntry = $"{time:yyyy-MM-dd HH:mm:ss},{symbol},{action},{confidence:F4},{price:F2},{reason}";
            
            File.AppendAllLines(logFile, new[] { logEntry });
        }
        catch (System.IO.IOException ex)
        {
            // File I/O failed - don't disrupt trading for logging issues
            Console.WriteLine($"[S15-RL] Shadow logging I/O failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            // File access denied - don't disrupt trading for logging issues
            Console.WriteLine($"[S15-RL] Shadow logging access denied: {ex.Message}");
        }
        catch (System.Security.SecurityException ex)
        {
            // Security exception - don't disrupt trading for logging issues
            Console.WriteLine($"[S15-RL] Shadow logging security error: {ex.Message}");
        }
    }
}
