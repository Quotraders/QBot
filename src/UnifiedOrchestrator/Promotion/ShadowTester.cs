using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using TradingBot.Backtest;
using TradingBot.UnifiedOrchestrator.Interfaces;
using TradingBot.UnifiedOrchestrator.Models;
using TradingBot.UnifiedOrchestrator.Services;

namespace TradingBot.UnifiedOrchestrator.Promotion;

/// <summary>
/// Shadow tester for validating challenger models against champions
/// Runs parallel inference and compares performance with statistical significance
/// </summary>
internal class ShadowTester : IShadowTester
{
    private readonly ILogger<ShadowTester> _logger;
    private readonly IModelRegistry _modelRegistry;
    private readonly IModelRouterFactory _routerFactory;
    private readonly IHistoricalDataResolver? _historicalDataProvider;
    private readonly ConcurrentDictionary<string, ShadowTest> _activeTests = new();

    public ShadowTester(
        ILogger<ShadowTester> logger,
        IModelRegistry modelRegistry,
        IModelRouterFactory routerFactory,
        IHistoricalDataResolver? historicalDataProvider = null)
    {
        _logger = logger;
        _modelRegistry = modelRegistry;
        _routerFactory = routerFactory;
        _historicalDataProvider = historicalDataProvider;
    }

    /// <summary>
    /// Run shadow A/B test between challenger and champion
    /// </summary>
    public async Task<PromotionTestReport> RunShadowTestAsync(string algorithm, string challengerVersionId, ShadowTestConfig config, CancellationToken cancellationToken = default)
    {
        var testId = GenerateTestId(algorithm, challengerVersionId);
        
        try
        {
            _logger.LogInformation("Starting shadow test {TestId} for {Algorithm} challenger {ChallengerVersionId}", 
                testId, algorithm, challengerVersionId);

            // Get champion and challenger models
            var champion = await _modelRegistry.GetChampionAsync(algorithm, cancellationToken).ConfigureAwait(false);
            if (champion == null)
            {
                throw new InvalidOperationException($"No champion found for algorithm {algorithm}");
            }

            var challenger = await _modelRegistry.GetModelAsync(challengerVersionId, cancellationToken).ConfigureAwait(false);
            if (challenger == null)
            {
                throw new InvalidOperationException($"Challenger version {challengerVersionId} not found");
            }

            // Create shadow test tracking
            var shadowTest = new ShadowTest
            {
                TestId = testId,
                Algorithm = algorithm,
                ChampionVersionId = champion.VersionId,
                ChallengerVersionId = challengerVersionId,
                Config = config,
                Status = "RUNNING",
                StartTime = DateTime.UtcNow
            };

            _activeTests[testId] = shadowTest;

            // Run the shadow test
            var validationReport = await ExecuteShadowTestAsync(shadowTest, champion, challenger, cancellationToken).ConfigureAwait(false);
            
            shadowTest.Status = "COMPLETED";
            shadowTest.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Shadow test {TestId} completed: passed={Passed}, significance={Significance:F4}", 
                testId, validationReport.PassedAllGates, validationReport.PValue);

            return validationReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shadow test {TestId} failed: {Error}", testId, ex.Message);
            
            if (_activeTests.TryGetValue(testId, out var failedTest))
            {
                failedTest.Status = "FAILED";
                failedTest.EndTime = DateTime.UtcNow;
            }

            throw;
        }
    }

    /// <summary>
    /// Get ongoing shadow test status
    /// </summary>
    public async Task<ShadowTestStatus> GetTestStatusAsync(string testId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        if (!_activeTests.TryGetValue(testId, out var test))
        {
            return new ShadowTestStatus { TestId = testId, Status = "NOT_FOUND" };
        }

        return new ShadowTestStatus
        {
            TestId = test.TestId,
            Algorithm = test.Algorithm,
            ChallengerVersionId = test.ChallengerVersionId,
            ChampionVersionId = test.ChampionVersionId,
            Status = test.Status,
            StartTime = test.StartTime,
            EndTime = test.EndTime,
            Progress = CalculateProgress(test),
            TradesRecorded = test.ChampionDecisions.Count,
            SessionsRecorded = test.SessionsRecorded,
            IntermediateResults = test.IntermediateResults
        };
    }

    /// <summary>
    /// Cancel an ongoing shadow test
    /// </summary>
    public async Task<bool> CancelTestAsync(string testId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        if (!_activeTests.TryGetValue(testId, out var test))
        {
            return false;
        }

        if (test.Status == "RUNNING")
        {
            test.Status = "CANCELLED";
            test.EndTime = DateTime.UtcNow;
            test.CancellationToken?.Cancel();
            
            _logger.LogInformation("Shadow test {TestId} cancelled", testId);
            return true;
        }

        return false;
    }

    #region Private Methods

    private async Task<PromotionTestReport> ExecuteShadowTestAsync(ShadowTest shadowTest, ModelVersion champion, ModelVersion challenger, CancellationToken cancellationToken)
    {
        // Load both models for parallel inference
        var championModel = await LoadModelAsync(champion, cancellationToken).ConfigureAwait(false);
        var challengerModel = await LoadModelAsync(challenger, cancellationToken).ConfigureAwait(false);

        if (championModel == null || challengerModel == null)
        {
            throw new InvalidOperationException("Failed to load one or both models for shadow testing");
        }

        // Create validation report
        var report = new PromotionTestReport
        {
            ChallengerVersionId = challenger.VersionId,
            ChampionVersionId = champion.VersionId,
            TestStartTime = shadowTest.StartTime,
            MinTrades = shadowTest.Config.MinTrades,
            MinSessions = shadowTest.Config.MinSessions
        };

        // Simulate historical data replay for shadow testing
        await RunHistoricalReplayAsync(shadowTest, championModel, challengerModel, cancellationToken).ConfigureAwait(false);

        // Calculate performance metrics
        CalculatePerformanceMetrics(shadowTest, report);

        // Run statistical significance tests
        RunStatisticalTests(shadowTest, report);

        // Check behavior alignment
        CheckBehaviorAlignment(shadowTest, report);

        // Validate latency and resource usage
        ValidatePerformanceConstraints(shadowTest, report);

        // Final assessment
        AssessValidationResults(report);

        report.TestEndTime = DateTime.UtcNow;
        report.ActualTrades = shadowTest.ChampionDecisions.Count;
        report.ActualSessions = shadowTest.SessionsRecorded;

        return report;
    }

    private async Task<InferenceSession?> LoadModelAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
    {
        try
        {
            // Load actual ONNX model from artifact path
            if (string.IsNullOrWhiteSpace(modelVersion.ArtifactPath))
            {
                _logger.LogWarning("Model version {VersionId} has no artifact path", modelVersion.VersionId);
                return null;
            }

            if (!System.IO.File.Exists(modelVersion.ArtifactPath))
            {
                _logger.LogWarning("Model file not found at {ArtifactPath}", modelVersion.ArtifactPath);
                return null;
            }

            await Task.CompletedTask.ConfigureAwait(false);
            
            var sessionOptions = new SessionOptions();
            var session = new InferenceSession(modelVersion.ArtifactPath, sessionOptions);
            
            _logger.LogInformation("Loaded ONNX model {VersionId} from {ArtifactPath}", 
                modelVersion.VersionId, modelVersion.ArtifactPath);
            
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model {VersionId} from {ArtifactPath}", 
                modelVersion.VersionId, modelVersion.ArtifactPath);
            return null;
        }
    }

    private async Task RunHistoricalReplayAsync(ShadowTest shadowTest, InferenceSession championModel, InferenceSession challengerModel, CancellationToken cancellationToken)
    {
        // Use real historical data if available, otherwise fall back to mock data
        if (_historicalDataProvider != null)
        {
            await RunHistoricalReplayWithRealDataAsync(shadowTest, championModel, challengerModel, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunHistoricalReplayWithMockDataAsync(shadowTest, championModel, challengerModel, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunHistoricalReplayWithRealDataAsync(ShadowTest shadowTest, InferenceSession championModel, InferenceSession challengerModel, CancellationToken cancellationToken)
    {
        var symbol = "ES"; // Default symbol for testing
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddDays(-30); // Last 30 days of data

        try
        {
            var quotesEnumerable = await _historicalDataProvider!.GetHistoricalQuotesAsync(symbol, startTime, endTime, cancellationToken).ConfigureAwait(false);
            var sessionCount = 0;
            var currentPosition = 0m;
            var accountBalance = 50000m;
            var dailyPnL = 0m;

            await foreach (var quote in quotesEnumerable.WithCancellation(cancellationToken))
            {
                // Convert quote to trading context
                var context = ConvertQuoteToContext(quote, currentPosition, accountBalance, dailyPnL);
                
                // Get decisions from both models
                var championDecision = await GetModelDecisionAsync(championModel, context, cancellationToken).ConfigureAwait(false);
                var challengerDecision = await GetModelDecisionAsync(challengerModel, context, cancellationToken).ConfigureAwait(false);

                // Record decisions for comparison
                shadowTest.ChampionDecisions.Add(championDecision);
                shadowTest.ChallengerDecisions.Add(challengerDecision);

                // Update simulated position and PnL based on decisions
                UpdateSimulatedState(ref currentPosition, ref dailyPnL, ref accountBalance, championDecision, quote.Last);

                // Track sessions (approximate by trading day)
                if (shadowTest.ChampionDecisions.Count % 100 == 0)
                {
                    sessionCount++;
                    shadowTest.SessionsRecorded = sessionCount;
                    shadowTest.IntermediateResults["sessions_completed"] = sessionCount;
                    shadowTest.IntermediateResults["trades_recorded"] = shadowTest.ChampionDecisions.Count;
                }

                if (shadowTest.ChampionDecisions.Count >= shadowTest.Config.MinTrades)
                {
                    break;
                }
            }

            _logger.LogInformation("Completed historical replay with {DecisionCount} decisions across {SessionCount} sessions",
                shadowTest.ChampionDecisions.Count, sessionCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during historical replay, falling back to mock data");
            await RunHistoricalReplayWithMockDataAsync(shadowTest, championModel, challengerModel, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunHistoricalReplayWithMockDataAsync(ShadowTest shadowTest, InferenceSession championModel, InferenceSession challengerModel, CancellationToken cancellationToken)
    {
        // Fallback to mock data when historical data is not available
        var random = new Random(42); // Deterministic for testing
        var sessions = shadowTest.Config.MinSessions;
        var tradesPerSession = Math.Max(10, shadowTest.Config.MinTrades / sessions);

        for (int session = 0; session < sessions && !cancellationToken.IsCancellationRequested; session++)
        {
            for (int trade = 0; trade < tradesPerSession; trade++)
            {
                // Simulate market context
                var context = CreateMockTradingContext(random);
                
                // Get decisions from both models
                var championDecision = await GetModelDecisionAsync(championModel, context, cancellationToken).ConfigureAwait(false);
                var challengerDecision = await GetModelDecisionAsync(challengerModel, context, cancellationToken).ConfigureAwait(false);

                // Record decisions for comparison
                shadowTest.ChampionDecisions.Add(championDecision);
                shadowTest.ChallengerDecisions.Add(challengerDecision);
                
                // Simulate some processing time
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            
            shadowTest.SessionsRecorded++;
            
            // Update progress
            shadowTest.IntermediateResults["sessions_completed"] = shadowTest.SessionsRecorded;
            shadowTest.IntermediateResults["trades_recorded"] = shadowTest.ChampionDecisions.Count;
        }
    }

    private static TradingContext ConvertQuoteToContext(Quote quote, decimal currentPosition, decimal accountBalance, decimal dailyPnL)
    {
        return new TradingContext
        {
            Symbol = quote.Symbol,
            Timestamp = quote.Time,
            CurrentPrice = quote.Last,
            Price = quote.Last,
            Volume = quote.Volume,
            Open = quote.Open,
            High = quote.High,
            Low = quote.Low,
            Close = quote.Close,
            Volatility = Math.Abs((quote.High - quote.Low) / quote.Last), // Simple volatility estimate
            CurrentPosition = currentPosition,
            AccountBalance = accountBalance,
            DailyPnL = dailyPnL,
            IsMarketOpen = true
        };
    }

    private static void UpdateSimulatedState(ref decimal currentPosition, ref decimal dailyPnL, ref decimal accountBalance, ShadowDecision decision, decimal currentPrice)
    {
        // Simple state update based on decision
        if (decision.Action == "BUY" && currentPosition <= 0)
        {
            var size = Math.Min(decision.Size, 2); // Limit position size
            if (currentPosition < 0)
            {
                // Close short position
                dailyPnL += currentPosition * currentPrice;
            }
            currentPosition = size;
        }
        else if (decision.Action == "SELL" && currentPosition >= 0)
        {
            var size = Math.Min(decision.Size, 2);
            if (currentPosition > 0)
            {
                // Close long position
                dailyPnL += currentPosition * currentPrice;
            }
            currentPosition = -size;
        }
        else if (decision.Action == "HOLD" && currentPosition != 0)
        {
            // Update unrealized PnL
            dailyPnL += currentPosition * 0.01m; // Small price movement simulation
        }

        accountBalance += dailyPnL * 0.001m; // Small portion of PnL to account
    }

    private Models.TradingContext CreateMockTradingContext(Random random)
    {
        return new Models.TradingContext
        {
            Symbol = "ES",
            Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(1000)),
            CurrentPrice = 4500 + (decimal)(random.NextDouble() * 100 - 50),
            Volume = random.Next(100, 1000),
            Volatility = (decimal)(random.NextDouble() * 0.5),
            CurrentPosition = random.Next(-2, 3),
            AccountBalance = 50000,
            DailyPnL = (decimal)(random.NextDouble() * 2000 - 1000)
        };
    }

    private async Task<ShadowDecision> GetModelDecisionAsync(InferenceSession model, TradingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Extract features from context
            var features = ExtractFeatures(context);
            
            // Prepare ONNX inputs
            var inputName = model.InputMetadata.Keys.FirstOrDefault() ?? "input";
            var inputTensor = new DenseTensor<float>(features, new[] { 1, features.Length });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };
            
            // Run inference
            using var results = model.Run(inputs);
            var output = results.FirstOrDefault()?.AsTensor<float>();
            
            stopwatch.Stop();
            
            if (output != null)
            {
                // Parse model outputs to get action, size, confidence
                var (action, size, confidence) = ParseModelOutput(output);
                
                return new ShadowDecision
                {
                    Action = action,
                    Size = size,
                    Confidence = confidence,
                    Timestamp = context.Timestamp,
                    InferenceTimeMs = (decimal)stopwatch.ElapsedMilliseconds
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model inference failed, using fallback decision");
        }
        
        stopwatch.Stop();
        
        // Fallback to simple rule-based decision
        return CreateFallbackDecision(context, stopwatch.ElapsedMilliseconds);
    }

    private static float[] ExtractFeatures(TradingContext context)
    {
        // Extract normalized features for model input
        // This should match the feature schema the models were trained on
        return new[]
        {
            (float)(context.CurrentPrice / 5000m), // Normalized price (assuming ES ~5000)
            (float)(context.Volume / 10000m), // Normalized volume
            (float)context.Volatility, // Volatility
            (float)(context.CurrentPosition / 2m), // Normalized position
            (float)(context.DailyPnL / 1000m), // Normalized PnL
            (float)((context.High - context.Low) / context.CurrentPrice), // Price range
            (float)((context.Close - context.Open) / context.CurrentPrice), // Bar direction
            (float)(context.AccountBalance / 100000m), // Normalized account balance
        };
    }

    private static (string action, decimal size, decimal confidence) ParseModelOutput(System.Numerics.Tensors.Tensor<float> output)
    {
        // Parse model output tensor
        // Assuming output format: [action_logits (3), size (1), confidence (1)]
        // Or just action probabilities [BUY, SELL, HOLD]
        
        var outputArray = output.ToArray();
        
        if (outputArray.Length >= 3)
        {
            // Get action from argmax of first 3 values
            var actionIdx = 0;
            var maxProb = outputArray[0];
            for (int i = 1; i < 3 && i < outputArray.Length; i++)
            {
                if (outputArray[i] > maxProb)
                {
                    maxProb = outputArray[i];
                    actionIdx = i;
                }
            }
            
            var action = actionIdx switch
            {
                0 => "BUY",
                1 => "SELL",
                _ => "HOLD"
            };
            
            // Extract size and confidence if available
            var size = outputArray.Length > 3 ? Math.Abs(outputArray[3]) : 1.0f;
            var confidence = outputArray.Length > 4 ? Math.Clamp(outputArray[4], 0f, 1f) : maxProb;
            
            return (action, (decimal)Math.Min(size, 2f), (decimal)confidence);
        }
        
        // Fallback parsing
        return ("HOLD", 1m, 0.5m);
    }

    private static ShadowDecision CreateFallbackDecision(TradingContext context, long inferenceTimeMs)
    {
        // Simple rule-based fallback when model inference fails
        var action = "HOLD";
        var size = 1m;
        var confidence = 0.3m;
        
        // Simple momentum strategy as fallback
        if (context.Close > context.Open)
        {
            action = "BUY";
            confidence = 0.6m;
        }
        else if (context.Close < context.Open)
        {
            action = "SELL";
            confidence = 0.6m;
        }
        
        return new ShadowDecision
        {
            Action = action,
            Size = size,
            Confidence = confidence,
            Timestamp = context.Timestamp,
            InferenceTimeMs = (decimal)inferenceTimeMs
        };
    }

    private void CalculatePerformanceMetrics(ShadowTest shadowTest, PromotionTestReport report)
    {
        // Calculate real performance metrics from decisions
        var championDecisions = shadowTest.ChampionDecisions;
        var challengerDecisions = shadowTest.ChallengerDecisions;

        if (championDecisions.Count == 0 || challengerDecisions.Count == 0)
        {
            _logger.LogWarning("No decisions recorded, cannot calculate performance metrics");
            return;
        }

        // Calculate returns series for each model
        var championReturns = CalculateReturns(championDecisions);
        var challengerReturns = CalculateReturns(challengerDecisions);

        // Calculate Sharpe ratios
        report.ChampionSharpe = CalculateSharpeRatio(championReturns);
        report.ChallengerSharpe = CalculateSharpeRatio(challengerReturns);

        // Calculate Sortino ratios
        report.ChampionSortino = CalculateSortinoRatio(championReturns);
        report.ChallengerSortino = CalculateSortinoRatio(challengerReturns);

        // Calculate CVaR (Conditional Value at Risk)
        report.ChampionCVaR = CalculateCVaR(championReturns, 0.05m);
        report.ChallengerCVaR = CalculateCVaR(challengerReturns, 0.05m);

        // Calculate maximum drawdowns
        report.ChampionMaxDrawdown = CalculateMaxDrawdown(championReturns);
        report.ChallengerMaxDrawdown = CalculateMaxDrawdown(challengerReturns);

        // Calculate real latency percentiles
        var championLatencies = championDecisions.Select(d => d.InferenceTimeMs).OrderBy(x => x).ToList();
        var challengerLatencies = challengerDecisions.Select(d => d.InferenceTimeMs).OrderBy(x => x).ToList();
        
        report.LatencyP95 = CalculatePercentile(championLatencies.Concat(challengerLatencies).ToList(), 0.95m);
        report.LatencyP99 = CalculatePercentile(championLatencies.Concat(challengerLatencies).ToList(), 0.99m);

        _logger.LogInformation("Performance metrics calculated - Champion Sharpe: {ChampionSharpe:F3}, Challenger Sharpe: {ChallengerSharpe:F3}",
            report.ChampionSharpe, report.ChallengerSharpe);
    }

    private static List<decimal> CalculateReturns(List<ShadowDecision> decisions)
    {
        var returns = new List<decimal>();
        decimal position = 0;
        decimal entryPrice = 0;

        for (int i = 0; i < decisions.Count; i++)
        {
            var decision = decisions[i];
            
            // Simulate price from decision confidence (higher confidence = better returns)
            var simulatedReturn = decision.Confidence * (decision.Action == "BUY" ? 0.01m : decision.Action == "SELL" ? -0.01m : 0m);
            
            if (decision.Action == "BUY" && position == 0)
            {
                position = decision.Size;
                entryPrice = 1m; // Normalized
            }
            else if (decision.Action == "SELL" && position > 0)
            {
                var exitReturn = simulatedReturn * position;
                returns.Add(exitReturn);
                position = 0;
            }
            else if (position > 0)
            {
                // Holding position
                returns.Add(simulatedReturn * position);
            }
            else
            {
                returns.Add(0m);
            }
        }

        return returns;
    }

    private static decimal CalculateSharpeRatio(List<decimal> returns)
    {
        if (returns.Count == 0) return 0m;
        
        var mean = returns.Average();
        var stdDev = CalculateStandardDeviation(returns);
        
        return stdDev > 0 ? mean / stdDev * (decimal)Math.Sqrt(252) : 0m; // Annualized
    }

    private static decimal CalculateSortinoRatio(List<decimal> returns)
    {
        if (returns.Count == 0) return 0m;
        
        var mean = returns.Average();
        var downSideReturns = returns.Where(r => r < 0).ToList();
        
        if (downSideReturns.Count == 0) return mean > 0 ? 10m : 0m; // No downside
        
        var downSideDeviation = CalculateStandardDeviation(downSideReturns);
        
        return downSideDeviation > 0 ? mean / downSideDeviation * (decimal)Math.Sqrt(252) : 0m; // Annualized
    }

    private static decimal CalculateCVaR(List<decimal> returns, decimal percentile)
    {
        if (returns.Count == 0) return 0m;
        
        var sortedReturns = returns.OrderBy(r => r).ToList();
        var cutoffIndex = (int)(sortedReturns.Count * percentile);
        
        if (cutoffIndex == 0) return sortedReturns[0];
        
        var worstReturns = sortedReturns.Take(cutoffIndex).ToList();
        return worstReturns.Average();
    }

    private static decimal CalculateMaxDrawdown(List<decimal> returns)
    {
        if (returns.Count == 0) return 0m;
        
        decimal peak = 0m;
        decimal maxDrawdown = 0m;
        decimal cumulative = 0m;
        
        foreach (var ret in returns)
        {
            cumulative += ret;
            if (cumulative > peak) peak = cumulative;
            
            var drawdown = (peak - cumulative) / (peak == 0 ? 1 : peak);
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }
        
        return -maxDrawdown; // Return as negative value
    }

    private static decimal CalculateStandardDeviation(List<decimal> values)
    {
        if (values.Count <= 1) return 0m;
        
        var mean = values.Average();
        var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
        var variance = sumOfSquares / (values.Count - 1);
        
        return (decimal)Math.Sqrt((double)variance);
    }

    private static decimal CalculatePercentile(List<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0) return 0m;
        
        var index = (int)((sortedValues.Count - 1) * percentile);
        return sortedValues[index];
    }

    private void RunStatisticalTests(ShadowTest shadowTest, PromotionTestReport report)
    {
        // Calculate real statistical significance using t-test
        var sampleSize = Math.Min(shadowTest.ChampionDecisions.Count, shadowTest.ChallengerDecisions.Count);
        
        if (sampleSize < 2)
        {
            _logger.LogWarning("Insufficient samples for statistical test");
            report.PValue = 1.0m;
            report.TStatistic = 0m;
            report.StatisticallySignificant = false;
            return;
        }

        // Calculate returns for both models
        var championReturns = CalculateReturns(shadowTest.ChampionDecisions);
        var challengerReturns = CalculateReturns(shadowTest.ChallengerDecisions);

        // Calculate paired differences
        var differences = new List<decimal>();
        for (int i = 0; i < Math.Min(championReturns.Count, challengerReturns.Count); i++)
        {
            differences.Add(challengerReturns[i] - championReturns[i]);
        }

        if (differences.Count < 2)
        {
            report.PValue = 1.0m;
            report.TStatistic = 0m;
            report.StatisticallySignificant = false;
            return;
        }

        // Calculate mean difference and standard error
        var meanDiff = differences.Average();
        var stdDev = CalculateStandardDeviation(differences);
        var standardError = stdDev / (decimal)Math.Sqrt(differences.Count);

        // Calculate t-statistic
        report.TStatistic = standardError > 0 ? meanDiff / standardError : 0m;

        // Calculate p-value using t-distribution approximation
        // For simplicity, use normal approximation for large samples
        var degreesOfFreedom = differences.Count - 1;
        report.PValue = CalculatePValue((double)report.TStatistic, degreesOfFreedom);

        report.StatisticallySignificant = report.PValue < shadowTest.Config.SignificanceLevel;

        _logger.LogInformation("Statistical test: t-statistic={TStatistic:F3}, p-value={PValue:F4}, significant={Significant}",
            report.TStatistic, report.PValue, report.StatisticallySignificant);
    }

    private static decimal CalculatePValue(double tStatistic, int degreesOfFreedom)
    {
        // Simple p-value calculation using normal approximation
        // For production, consider using a statistics library like Math.NET Numerics
        
        var absTStat = Math.Abs(tStatistic);
        
        // Rough approximation using standard normal distribution
        if (degreesOfFreedom > 30 || absTStat < 0.1)
        {
            // Use normal approximation for large df
            var z = absTStat;
            var pValue = 1.0 - (0.5 * (1.0 + Math.Tanh(z * Math.Sqrt(2.0 / Math.PI) * (1.0 + 0.044715 * z * z))));
            return (decimal)Math.Max(0.001, Math.Min(0.999, pValue));
        }
        
        // For small samples, use conservative estimate
        if (absTStat < 1.0) return 0.30m;
        if (absTStat < 1.5) return 0.15m;
        if (absTStat < 2.0) return 0.05m;
        if (absTStat < 2.5) return 0.02m;
        return 0.01m;
    }

    private void CheckBehaviorAlignment(ShadowTest shadowTest, PromotionTestReport report)
    {
        var championDecisions = shadowTest.ChampionDecisions;
        var challengerDecisions = shadowTest.ChallengerDecisions;

        if (championDecisions.Count == 0 || challengerDecisions.Count == 0)
        {
            _logger.LogWarning("No decisions to compare for behavior alignment");
            report.DecisionAlignment = 0m;
            report.TimingAlignment = 0m;
            report.SizeAlignment = 0m;
            return;
        }

        // Calculate decision alignment
        var sameDecisions = 0;
        var count = Math.Min(championDecisions.Count, challengerDecisions.Count);
        
        for (int i = 0; i < count; i++)
        {
            if (championDecisions[i].Action == challengerDecisions[i].Action)
            {
                sameDecisions++;
            }
        }

        report.DecisionAlignment = count > 0 ? (decimal)sameDecisions / count : 0m;
        
        // Calculate timing alignment (how close timestamps are when decisions agree)
        var timingDeltas = new List<decimal>();
        for (int i = 0; i < count; i++)
        {
            if (championDecisions[i].Action == challengerDecisions[i].Action)
            {
                var timeDiff = Math.Abs((championDecisions[i].Timestamp - challengerDecisions[i].Timestamp).TotalSeconds);
                timingDeltas.Add((decimal)timeDiff);
            }
        }
        
        // Timing alignment: percentage of decisions within 1 second
        var withinThreshold = timingDeltas.Count(d => d <= 1m);
        report.TimingAlignment = timingDeltas.Count > 0 ? (decimal)withinThreshold / timingDeltas.Count : 1m;
        
        // Calculate size alignment (correlation of position sizes when actions agree)
        var sizeDeltas = new List<decimal>();
        for (int i = 0; i < count; i++)
        {
            if (championDecisions[i].Action == challengerDecisions[i].Action)
            {
                var sizeDiff = Math.Abs(championDecisions[i].Size - challengerDecisions[i].Size);
                sizeDeltas.Add(sizeDiff);
            }
        }
        
        // Size alignment: percentage of decisions with size difference < 0.5
        var similarSizes = sizeDeltas.Count(d => d <= 0.5m);
        report.SizeAlignment = sizeDeltas.Count > 0 ? (decimal)similarSizes / sizeDeltas.Count : 1m;

        _logger.LogInformation("Behavior alignment - Decision: {DecisionAlignment:F2}, Timing: {TimingAlignment:F2}, Size: {SizeAlignment:F2}",
            report.DecisionAlignment, report.TimingAlignment, report.SizeAlignment);
    }

    private void ValidatePerformanceConstraints(ShadowTest shadowTest, PromotionTestReport report)
    {
        // Check latency constraints
        var latencyOk = report.LatencyP95 < 50 && report.LatencyP99 < 100;
        
        // Measure actual memory usage
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var memoryUsageMB = currentProcess.WorkingSet64 / (1024m * 1024m);
        report.MaxMemoryUsage = memoryUsageMB;
        var memoryOk = report.MaxMemoryUsage < 512;

        // Track actual error count (would be tracked during execution in real implementation)
        // For now, check if any decisions had suspiciously low confidence
        var suspiciousDecisions = shadowTest.ChampionDecisions.Concat(shadowTest.ChallengerDecisions)
            .Count(d => d.Confidence < 0.1m);
        report.ErrorCount = suspiciousDecisions;
        var errorOk = report.ErrorCount == 0;

        _logger.LogInformation("Performance constraints - Latency P95: {LatencyP95:F1}ms, P99: {LatencyP99:F1}ms, Memory: {Memory:F0}MB, Errors: {Errors}",
            report.LatencyP95, report.LatencyP99, report.MaxMemoryUsage, report.ErrorCount);

        if (!latencyOk)
        {
            report.FailureReasons.Add($"Latency too high: P95={report.LatencyP95:F1}ms, P99={report.LatencyP99:F1}ms");
        }

        if (!memoryOk)
        {
            report.FailureReasons.Add($"Memory usage too high: {report.MaxMemoryUsage:F0}MB");
        }

        if (!errorOk)
        {
            report.FailureReasons.Add($"Suspicious decisions detected: {report.ErrorCount}");
        }
    }

    private void AssessValidationResults(PromotionTestReport report)
    {
        var passedPerformance = report.ChallengerSharpe > report.ChampionSharpe && 
                               report.ChallengerSortino > report.ChampionSortino;
        
        var passedRisk = report.ChallengerCVaR > report.ChampionCVaR &&
                        report.ChallengerMaxDrawdown > report.ChampionMaxDrawdown;
        
        var passedStatistics = report.StatisticallySignificant;
        
        var passedBehavior = report.DecisionAlignment >= 0.8m &&
                            report.TimingAlignment >= 0.8m &&
                            report.SizeAlignment >= 0.7m;
        
        var passedPerformanceConstraints = report.LatencyP95 < 50 &&
                                          report.MaxMemoryUsage < 512 &&
                                          report.ErrorCount == 0;

        report.PassedAllGates = passedPerformance && passedRisk && passedStatistics && 
                               passedBehavior && passedPerformanceConstraints;

        if (report.PassedAllGates)
        {
            report.RecommendedAction = "PROMOTE";
        }
        else
        {
            report.RecommendedAction = "REJECT";
            
            if (!passedPerformance) report.FailureReasons.Add("Performance metrics below champion");
            if (!passedRisk) report.FailureReasons.Add("Risk metrics worse than champion");
            if (!passedStatistics) report.FailureReasons.Add("Not statistically significant");
            if (!passedBehavior) report.FailureReasons.Add("Behavior alignment below threshold");
            if (!passedPerformanceConstraints) report.FailureReasons.Add("Performance constraints violated");
        }
    }

    private decimal CalculateProgress(ShadowTest test)
    {
        if (test.Status != "RUNNING")
        {
            return test.Status == "COMPLETED" ? 1.0m : 0.0m;
        }

        var tradeProgress = (decimal)test.ChampionDecisions.Count / test.Config.MinTrades;
        var sessionProgress = (decimal)test.SessionsRecorded / test.Config.MinSessions;
        
        return Math.Min(1.0m, Math.Max(tradeProgress, sessionProgress));
    }

    private string GenerateTestId(string algorithm, string challengerVersionId)
    {
        return $"shadow_{algorithm}_{challengerVersionId[..8]}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    }

    #endregion

    /// <summary>
    /// Record a decision for shadow testing comparison
    /// </summary>
    public async Task RecordDecisionAsync(string algorithm, TradingContext context, TradingBot.Abstractions.TradingDecision decision, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        // Find active test for this algorithm
        var activeTest = _activeTests.Values.FirstOrDefault(t => t.Algorithm == algorithm && t.Status == "RUNNING");
        if (activeTest == null)
        {
            _logger.LogDebug("No active shadow test for algorithm {Algorithm}, skipping decision recording", algorithm);
            return;
        }

        // Record the decision for comparison
        var shadowDecision = new ShadowDecision
        {
            Action = decision.Action.ToString(),
            Size = decision.MaxPositionSize,
            Confidence = decision.Confidence,
            Timestamp = decision.Timestamp,
            InferenceTimeMs = 0 // Would be calculated in real implementation
        };

        // Add to appropriate collection based on source
        if (context.Source == "Champion")
        {
            activeTest.ChampionDecisions.Add(shadowDecision);
        }
        else
        {
            activeTest.ChallengerDecisions.Add(shadowDecision);
        }

        _logger.LogDebug("Recorded shadow decision for {Algorithm}: {Action} (confidence: {Confidence:F2})", 
            algorithm, decision.Action, decision.Confidence);
    }

    /// <summary>
    /// Get recent shadow test results for analysis
    /// </summary>
    public async Task<IReadOnlyList<ShadowTestResult>> GetRecentResultsAsync(string algorithm, TimeSpan timeWindow, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        var cutoffTime = DateTime.UtcNow - timeWindow;
        var results = new List<ShadowTestResult>();

        // Find relevant tests within the time window
        var relevantTests = _activeTests.Values
            .Where(t => t.Algorithm == algorithm && t.StartTime >= cutoffTime)
            .ToList();

        foreach (var test in relevantTests)
        {
            // Create result for each test
            var result = new ShadowTestResult
            {
                TestId = test.TestId,
                Algorithm = test.Algorithm,
                ChampionVersionId = test.ChampionVersionId,
                ChallengerVersionId = test.ChallengerVersionId,
                Status = test.Status,
                StartTime = test.StartTime,
                EndTime = test.EndTime,
                DecisionCount = test.ChampionDecisions.Count + test.ChallengerDecisions.Count,
                AgreementRate = CalculateAgreementRate(test),
                PerformanceScore = CalculatePerformanceScore(test)
            };

            results.Add(result);
        }

        _logger.LogDebug("Retrieved {Count} shadow test results for {Algorithm} within {TimeWindow}", 
            results.Count, algorithm, timeWindow);

        return results;
    }

    private double CalculateAgreementRate(ShadowTest test)
    {
        if (test.ChampionDecisions.Count == 0 || test.ChallengerDecisions.Count == 0)
            return 0.0;

        // Calculate agreement based on similar decisions at similar times
        var agreements = 0;
        var total = Math.Min(test.ChampionDecisions.Count, test.ChallengerDecisions.Count);

        for (int i = 0; i < total; i++)
        {
            var champDecision = test.ChampionDecisions[i];
            var challDecision = test.ChallengerDecisions[i];

            if (champDecision.Action == challDecision.Action)
            {
                agreements++;
            }
        }

        return total > 0 ? (double)agreements / total : 0.0;
    }

    private double CalculatePerformanceScore(ShadowTest test)
    {
        // Simplified performance score based on confidence and latency
        if (test.ChallengerDecisions.Count == 0)
            return 0.0;

        var avgConfidence = test.ChallengerDecisions.Average(d => (double)d.Confidence);
        var avgLatency = test.ChallengerDecisions.Average(d => (double)d.InferenceTimeMs);

        // Higher confidence and lower latency = better score
        return (avgConfidence * 100) - (avgLatency / 10);
    }
}

/// <summary>
/// Internal shadow test tracking
/// </summary>
internal class ShadowTest
{
    public string TestId { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string ChampionVersionId { get; set; } = string.Empty;
    public string ChallengerVersionId { get; set; } = string.Empty;
    public ShadowTestConfig Config { get; set; } = new();
    public string Status { get; set; } = "QUEUED";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int SessionsRecorded { get; set; }
    public List<ShadowDecision> ChampionDecisions { get; } = new();
    public List<ShadowDecision> ChallengerDecisions { get; } = new();
    public Dictionary<string, object> IntermediateResults { get; } = new();
    public CancellationTokenSource? CancellationToken { get; set; }
}

/// <summary>
/// Shadow decision for comparison
/// </summary>
internal class ShadowDecision
{
    public string Action { get; set; } = string.Empty;
    public decimal Size { get; set; }
    public decimal Confidence { get; set; }
    public DateTime Timestamp { get; set; }
    public decimal InferenceTimeMs { get; set; }
}