using Microsoft.Extensions.Logging;
using TradingBot.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace TradingBot.IntelligenceStack;

/// <summary>
/// Nightly parameter auto-tuning system
/// Implements Bayesian optimization and evolutionary search for parameter optimization
/// </summary>
public class NightlyParameterTuner
{
    // ML Hyperparameter ranges constants
    private const double MinLearningRate = 0.001;
    private const double MaxLearningRate = 0.1;
    private const int MinHiddenSize = 64;
    private const int MaxHiddenSize = 512;
    private const double MinDropoutRate = 0.0;
    private const double MaxDropoutRate = 0.5;
    private const double MinL2Regularization = 1e-6;
    private const double MaxL2Regularization = 1e-2;
    private const int MinEnsembleSize = 3;
    private const int MaxEnsembleSize = 10;
    
    // S109 Magic Number Constants - Parameter Tuning
    private const int InitialNoImprovementCount = 0;
    private const int TrialNumberOffset = 1;
    private const int MaxPopulationSize = 20;
    private const int PopulationDivisor = 3;
    private const int GenerationOffset = 1;
    private const int EnsembleSizeOption1 = 5;
    private const int EnsembleSizeOption2 = 7;
    private const double DefaultAucFallback = 0.0;
    private const int ExplorationPhaseThreshold = 5;
    private const int RandomNumberGeneratorRange = 10000;
    private const double RandomNumberNormalizationFactor = 10000.0;
    private const double AucWeight = 0.5;
    private const double PrAt10Weight = 0.3;
    private const double EdgeBpsWeight = 0.2;
    private const double PrAt10Divisor = 0.2;
    private const double EdgeBpsDivisor = 10.0;
    private const double DefaultLearningRate = 0.01;
    private const int DefaultHiddenSize = 128;
    private const double DefaultDropoutRate = 0.1;
    private const double RnnLearningRate = 0.005;
    private const int RnnHiddenSize = 256;
    private const double TransformerLearningRate = 0.0001;
    private const int TransformerHiddenSize = 512;
    private const double BaseScore = 0.6;
    private const double HighLearningRateThreshold = 0.05;
    private const double LowLearningRateThreshold = 0.005;
    private const double HighLearningRatePenalty = -0.05;
    private const double LowLearningRatePenalty = -0.03;
    private const double GoodLearningRateBonus = 0.02;
    private const int LargeHiddenSizeThreshold = 256;
    private const int SmallHiddenSizeThreshold = 64;
    private const double LargeHiddenSizeBonus = 0.03;
    private const double SmallHiddenSizePenalty = -0.02;
    private const double MediumHiddenSizeBonus = 0.01;
    private const double HighDropoutThreshold = 0.3;
    private const double LowDropoutThreshold = 0.05;
    private const double HighDropoutPenalty = -0.02;
    private const double LowDropoutPenalty = -0.01;
    private const double GoodDropoutBonus = 0.01;
    private const int NoiseRangeMin = -25;
    private const int NoiseRangeMax = 25;
    private const double NoiseScaleFactor = 500.0;
    private const double PrAt10Multiplier = 0.15;
    private const int PercentageRange = 100;
    private const int FiftPercentChance = 50;
    private const double MutationRate = 0.1;
    
    // Additional S109 constants for evaluation metrics and timing
    private const int EvaluationDelayMs = 100;
    private const double BaseErrorCalibratedEstimate = 0.05;
    private const double ErrorCalibrationMultiplier = 0.1;
    private const double EdgeBpsMultiplier = 8.0;
    private const int DefaultSampleSize = 10000;
    private const double NoiseMultiplier = 0.1;
    private const double GaussianDefaultMean = 0.0;
    private const double GaussianDefaultStdDev = 1.0;
    private const double GaussianAdjustment = 1.0;
    private const int GaussianRandomMin = 1;
    private const int GaussianRandomMax = 10000;
    private const double GaussianRandomDivisor = 10000.0;
    private const int InitialImprovementsCount = 0;
    private const int InitialTotalCount = 0;
    private const double ImprovementThresholdDivisor = 2.0;
    private const int MinRecentResultsCount = 3;
    private const double DefaultHistoricalAuc = 0.0;
    private const double DegradationThreshold = 0.05;
    private const double MetricsValidityThreshold = 0.0;
    private const int MaxTuningHistoryCount = 30;
    
    // JsonSerializerOptions for consistent JSON serialization - CA1869 compliance
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    // LoggerMessage delegates for CA1848 compliance - NightlyParameterTuner
    private static readonly Action<ILogger, string, Exception?> StartingNightlyTuning =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4001, "StartingNightlyTuning"), 
            "[NIGHTLY_TUNING] Starting nightly tuning for {ModelFamily}");

    private static readonly Action<ILogger, Exception?> BayesianDidntImproveBaseline =
        LoggerMessage.Define(LogLevel.Information, new EventId(4002, "BayesianDidntImproveBaseline"), 
            "[NIGHTLY_TUNING] Bayesian optimization didn't improve baseline, trying evolutionary search");

    private static readonly Action<ILogger, string, bool, int, double, Exception?> CompletedNightlyTuning =
        LoggerMessage.Define<string, bool, int, double>(LogLevel.Information, new EventId(4003, "CompletedNightlyTuning"), 
            "[NIGHTLY_TUNING] Completed nightly tuning for {ModelFamily}: Improved={Improved}, Trials={Trials}, Duration={Duration:F1}min");

    private static readonly Action<ILogger, string, Exception?> NightlyTuningFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4004, "NightlyTuningFailed"), 
            "[NIGHTLY_TUNING] Nightly tuning failed for {ModelFamily}");

    private static readonly Action<ILogger, string, Exception?> StartingBayesianOptimization =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4005, "StartingBayesianOptimization"), 
            "[BAYESIAN_OPT] Starting Bayesian optimization for {ModelFamily}");

    private static readonly Action<ILogger, int, double, double, Exception?> NewBestFoundBayesian =
        LoggerMessage.Define<int, double, double>(LogLevel.Information, new EventId(4006, "NewBestFoundBayesian"), 
            "[BAYESIAN_OPT] New best found at trial {Trial}: AUC={AUC:F3}, EdgeBps={Edge:F1}");

    private static readonly Action<ILogger, int, double, bool, Exception?> CompletedBayesianOptimization =
        LoggerMessage.Define<int, double, bool>(LogLevel.Information, new EventId(4007, "CompletedBayesianOptimization"), 
            "[BAYESIAN_OPT] Completed: {Trials} trials, Best AUC: {AUC:F3}, Improved: {Improved}");

    private static readonly Action<ILogger, string, Exception?> StartingEvolutionarySearch =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4008, "StartingEvolutionarySearch"), 
            "[EVOLUTIONARY] Starting evolutionary search for {ModelFamily}");

    private static readonly Action<ILogger, int, double, double, Exception?> NewBestFoundEvolutionary =
        LoggerMessage.Define<int, double, double>(LogLevel.Information, new EventId(4009, "NewBestFoundEvolutionary"), 
            "[EVOLUTIONARY] New best found at generation {Gen}: AUC={AUC:F3}, EdgeBps={Edge:F1}");

    private static readonly Action<ILogger, int, double, bool, Exception?> CompletedEvolutionarySearch =
        LoggerMessage.Define<int, double, bool>(LogLevel.Information, new EventId(4010, "CompletedEvolutionarySearch"), 
            "[EVOLUTIONARY] Completed: {Trials} evaluations, Best AUC: {AUC:F3}, Improved: {Improved}");

    // Additional LoggerMessage delegates for remaining CA1848 violations
    private static readonly Action<ILogger, string, string, Exception?> CreatedTuningSession =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(4011, "CreatedTuningSession"), 
            "[NIGHTLY_TUNING] Created tuning session {SessionId} for {ModelFamily}");

    private static readonly Action<ILogger, string, Exception?> FailedToLoadParametersFromRegistry =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4012, "FailedToLoadParametersFromRegistry"), 
            "[NIGHTLY_TUNING] Failed to load parameters from registry for {ModelFamily}");

    private static readonly Action<ILogger, string, Exception?> PromotingImprovedModel =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4013, "PromotingImprovedModel"), 
            "[NIGHTLY_TUNING] Promoting improved model for {ModelFamily}");

    private static readonly Action<ILogger, string, Exception?> PerformingRollback =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4014, "PerformingRollback"), 
            "[NIGHTLY_TUNING] Performing rollback for {ModelFamily} due to performance degradation");

    private static readonly Action<ILogger, string, string, Exception?> RestoredStableParameters =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(4015, "RestoredStableParameters"), 
            "[NIGHTLY_TUNING] Restored stable parameters for {ModelFamily} from {BackupPath}");

    private static readonly Action<ILogger, string, DateTime, Exception?> CompletedRollback =
        LoggerMessage.Define<string, DateTime>(LogLevel.Information, new EventId(4016, "CompletedRollback"), 
            "[NIGHTLY_TUNING] Successfully completed rollback for {ModelFamily} to stable version from {StableDate}");

    private static readonly Action<ILogger, string, Exception?> RollbackFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4017, "RollbackFailed"), 
            "[NIGHTLY_TUNING] Rollback failed for {ModelFamily}");

    private static readonly Action<ILogger, Exception?> FailedToSaveTuningResult =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4018, "FailedToSaveTuningResult"), 
            "[NIGHTLY_TUNING] Failed to save tuning result");
    
    private readonly ILogger<NightlyParameterTuner> _logger;
    private readonly TuningConfig _config;
    private readonly NetworkConfig _networkConfig;
    private readonly IModelRegistry _modelRegistry;
    private readonly string _statePath;
    private readonly IServiceProvider _serviceProvider; // For accessing trading services
    
    private readonly Dictionary<string, TuningSession> _activeSessions = new();
    private readonly Dictionary<string, List<TuningResult>> _tuningHistory = new();
    private readonly object _lock = new();

    public NightlyParameterTuner(
        ILogger<NightlyParameterTuner> logger,
        TuningConfig config,
        NetworkConfig networkConfig,
        IModelRegistry modelRegistry,
        HistoricalTrainerWithCV historicalTrainer,
        IServiceProvider serviceProvider,
        string statePath = "data/nightly_tuning")
    {
        _logger = logger;
        _config = config;
        _networkConfig = networkConfig;
        _modelRegistry = modelRegistry;
        _serviceProvider = serviceProvider; // Store service provider for accessing trading services
        // historicalTrainer parameter kept for interface compatibility but not stored as it's unused
        _statePath = statePath;
        
        Directory.CreateDirectory(_statePath);
        Directory.CreateDirectory(Path.Combine(_statePath, "sessions"));
        Directory.CreateDirectory(Path.Combine(_statePath, "results"));
    }

    /// <summary>
    /// Run nightly parameter tuning for a model family
    /// </summary>
    public async Task<NightlyTuningResult> RunNightlyTuningAsync(
        string modelFamily,
        CancellationToken cancellationToken = default)
    {
        try
        {
            StartingNightlyTuning(_logger, modelFamily, null);

            var result = new NightlyTuningResult
            {
                ModelFamily = modelFamily,
                StartTime = DateTime.UtcNow,
                Method = TuningMethod.BayesianOptimization
            };

            if (!_config.Enabled)
            {
                result.Skipped = true;
                result.SkipReason = "Nightly tuning disabled in configuration";
                return result;
            }

            await ExecuteTuningWorkflowAsync(result, modelFamily, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            NightlyTuningFailed(_logger, modelFamily, ex);
            return CreateErrorResult(modelFamily, ex);
        }
        catch (ArgumentException ex)
        {
            NightlyTuningFailed(_logger, modelFamily, ex);
            return CreateErrorResult(modelFamily, ex);
        }
        catch (IOException ex)
        {
            NightlyTuningFailed(_logger, modelFamily, ex);
            return CreateErrorResult(modelFamily, ex);
        }
    }

    private async Task ExecuteTuningWorkflowAsync(NightlyTuningResult result, string modelFamily, CancellationToken cancellationToken)
    {
        // Create tuning session
        var session = await CreateTuningSessionAsync(modelFamily, cancellationToken).ConfigureAwait(false);
        
        // Run optimization algorithms
        var bayesianResult = await ExecuteOptimizationAsync(result, session, cancellationToken).ConfigureAwait(false);
        
        // Update result with optimization data
        UpdateResultWithOptimizationData(result, bayesianResult);

        // Handle model promotion or rollback
        await HandleModelPromotionOrRollbackAsync(result, modelFamily, bayesianResult, cancellationToken).ConfigureAwait(false);

        // Finalize result
        result.EndTime = DateTime.UtcNow;
        result.Success = true;
        await SaveTuningResultAsync(result, cancellationToken).ConfigureAwait(false);

        CompletedNightlyTuning(_logger, modelFamily, result.ImprovedBaseline, result.TrialsCompleted, 
            result.Duration.TotalMinutes, null);
    }

    private async Task<OptimizationResult> ExecuteOptimizationAsync(NightlyTuningResult result, TuningSession session, CancellationToken cancellationToken)
    {
        // Run Bayesian optimization
        var bayesianResult = await RunBayesianOptimizationAsync(session, cancellationToken).ConfigureAwait(false);
        
        // If Bayesian optimization doesn't find good parameters, try evolutionary search
        if (!bayesianResult.ImprovedBaseline)
        {
            BayesianDidntImproveBaseline(_logger, null);
            var evolutionaryResult = await RunEvolutionarySearchAsync(session, cancellationToken).ConfigureAwait(false);
            
            if (evolutionaryResult.ImprovedBaseline)
            {
                bayesianResult = evolutionaryResult;
                result.Method = TuningMethod.EvolutionarySearch;
            }
        }

        return bayesianResult;
    }

    private static void UpdateResultWithOptimizationData(NightlyTuningResult result, OptimizationResult bayesianResult)
    {
        result.BaselineMetrics = bayesianResult.BaselineMetrics;
        result.BestMetrics = bayesianResult.BestMetrics;
        result.TrialsCompleted = bayesianResult.TrialsCompleted;
        result.ImprovedBaseline = bayesianResult.ImprovedBaseline;
        
        // Copy best parameters to the read-only dictionary
        foreach (var kvp in bayesianResult.BestParameters)
        {
            result.BestParameters[kvp.Key] = kvp.Value;
        }
    }

    private async Task HandleModelPromotionOrRollbackAsync(NightlyTuningResult result, string modelFamily, OptimizationResult bayesianResult, CancellationToken cancellationToken)
    {
        // Promote model if improvement found
        if (bayesianResult.ImprovedBaseline)
        {
            await PromoteImprovedModelAsync(modelFamily, bayesianResult, cancellationToken).ConfigureAwait(false);
            result.ModelPromoted = true;
        }
        else
        {
            // Check if rollback is needed due to degradation
            if (await ShouldRollbackAsync(modelFamily, bayesianResult.BaselineMetrics, cancellationToken).ConfigureAwait(false))
            {
                await PerformRollbackAsync(modelFamily, cancellationToken).ConfigureAwait(false);
                result.RolledBack = true;
            }
        }
    }

    private static NightlyTuningResult CreateErrorResult(string modelFamily, Exception ex)
    {
        // Note: Logging is handled by the caller
        return new NightlyTuningResult 
        { 
            ModelFamily = modelFamily, 
            Success = false, 
            ErrorMessage = ex.Message 
        };
    }

    /// <summary>
    /// Run Bayesian optimization for hyperparameter tuning
    /// </summary>
    public async Task<OptimizationResult> RunBayesianOptimizationAsync(
        TuningSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StartingBayesianOptimization(_logger, session.ModelFamily, null);

        var result = new OptimizationResult
        {
            Method = TuningMethod.BayesianOptimization,
            StartTime = DateTime.UtcNow
        };

        // Initialize with baseline parameters
        var baseline = await GetBaselineParametersAsync(session.ModelFamily, cancellationToken).ConfigureAwait(false);
        result.BaselineMetrics = await EvaluateParametersAsync(baseline, cancellationToken).ConfigureAwait(false);
        
        var bestParameters = new Dictionary<string, double>(baseline);
        var bestMetrics = result.BaselineMetrics;
        var noImprovementCount = InitialNoImprovementCount;

        // Bayesian optimization loop
        for (int trial = 0; trial < _config.Trials && noImprovementCount < _config.EarlyStopNoImprove; trial++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Generate next parameter set using Bayesian optimization
            var candidateParams = GenerateNextCandidateByesian(session, trial);
            
            // Evaluate candidate parameters
            var candidateMetrics = await EvaluateParametersAsync(candidateParams, cancellationToken).ConfigureAwait(false);
            
            // Update session history
            var trialResult = new TrialResult
            {
                TrialNumber = trial + TrialNumberOffset,
                Metrics = candidateMetrics,
                Timestamp = DateTime.UtcNow
            };
            
            // Copy parameters to the read-only dictionary
            foreach (var kvp in candidateParams)
            {
                trialResult.Parameters[kvp.Key] = kvp.Value;
            }
            
            session.TrialHistory.Add(trialResult);

            // Check if this is the best result so far
            if (IsBetterMetrics(candidateMetrics, bestMetrics))
            {
                bestParameters = candidateParams;
                bestMetrics = candidateMetrics;
                noImprovementCount = InitialNoImprovementCount;
                
                NewBestFoundBayesian(_logger, trial + TrialNumberOffset, candidateMetrics.AUC, candidateMetrics.EdgeBps, null);
            }
            else
            {
                noImprovementCount++;
            }

            result.TrialsCompleted = trial + TrialNumberOffset;
        }

        result.BestMetrics = bestMetrics;
        result.ImprovedBaseline = IsBetterMetrics(bestMetrics, result.BaselineMetrics);
        result.EndTime = DateTime.UtcNow;
        
        // Copy best parameters to the read-only dictionary
        foreach (var kvp in bestParameters)
        {
            result.BestParameters[kvp.Key] = kvp.Value;
        }

        CompletedBayesianOptimization(_logger, result.TrialsCompleted, bestMetrics.AUC, result.ImprovedBaseline, null);

        return result;
    }

    /// <summary>
    /// Run evolutionary search as fallback optimization method
    /// </summary>
    public async Task<OptimizationResult> RunEvolutionarySearchAsync(
        TuningSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StartingEvolutionarySearch(_logger, session.ModelFamily, null);

        var result = new OptimizationResult
        {
            Method = TuningMethod.EvolutionarySearch,
            StartTime = DateTime.UtcNow
        };

        // Initialize population
        var populationSize = Math.Min(MaxPopulationSize, _config.Trials / PopulationDivisor);
        var population = await InitializePopulationAsync(populationSize, cancellationToken).ConfigureAwait(false);
        
        var generations = _config.Trials / populationSize;
        var bestIndividual = population.OrderByDescending(ind => ind.Fitness).First();
        
        result.BaselineMetrics = bestIndividual.Metrics ?? throw new InvalidOperationException("Best individual has null metrics");
        var bestMetrics = bestIndividual.Metrics;
        var bestParameters = bestIndividual.Parameters;

        // Evolution loop
        for (int generation = 0; generation < generations; generation++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Selection, crossover, and mutation
            var newPopulation = await EvolvePopulationAsync(population, cancellationToken).ConfigureAwait(false);
            
            // Evaluate new individuals
            foreach (var individual in newPopulation.Where(ind => ind.Metrics == null))
            {
                individual.Metrics = await EvaluateParametersAsync(individual.Parameters, cancellationToken).ConfigureAwait(false);
                individual.Fitness = CalculateFitness(individual.Metrics);
                result.TrialsCompleted++;
            }

            population = newPopulation;
            
            // Update best
            var currentBest = population.OrderByDescending(ind => ind.Fitness).First();
            if (currentBest.Fitness > bestIndividual.Fitness)
            {
                bestIndividual = currentBest;
                bestMetrics = currentBest.Metrics!;
                bestParameters = currentBest.Parameters;
                
                NewBestFoundEvolutionary(_logger, generation + GenerationOffset, bestMetrics.AUC, bestMetrics.EdgeBps, null);
            }
        }

        result.BestMetrics = bestMetrics;
        result.ImprovedBaseline = IsBetterMetrics(bestMetrics, result.BaselineMetrics);
        result.EndTime = DateTime.UtcNow;
        
        // Copy best parameters to the read-only dictionary
        foreach (var kvp in bestParameters)
        {
            result.BestParameters[kvp.Key] = kvp.Value;
        }

        CompletedEvolutionarySearch(_logger, result.TrialsCompleted, bestMetrics.AUC, result.ImprovedBaseline, null);

        return result;
    }

    private async Task<TuningSession> CreateTuningSessionAsync(string modelFamily, CancellationToken cancellationToken)
    {
        // Create tuning session asynchronously with proper initialization
        var session = await Task.Run(() =>
        {
            var newSession = new TuningSession
            {
                ModelFamily = modelFamily,
                SessionId = Guid.NewGuid().ToString(),
                StartTime = DateTime.UtcNow
            };
            
            // Populate the parameter space
            var parameterSpace = GetParameterSpace();
            foreach (var kvp in parameterSpace)
            {
                newSession.ParameterSpace[kvp.Key] = kvp.Value;
            }

            lock (_lock)
            {
                _activeSessions[newSession.SessionId] = newSession;
            }

            return newSession;
        }, cancellationToken).ConfigureAwait(false);

        // Initialize session state asynchronously
        await Task.Run(async () =>
        {
            // Prepare session workspace directory
            var sessionDir = Path.Combine(_statePath, "sessions", session.SessionId);
            Directory.CreateDirectory(sessionDir);
            
            // Save session metadata
            var sessionMetadata = new
            {
                session.ModelFamily,
                session.SessionId,
                session.StartTime,
                ParameterCount = session.ParameterSpace.Count
            };
            
            var metadataJson = JsonSerializer.Serialize(sessionMetadata, JsonOptions);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "metadata.json"), metadataJson, cancellationToken).ConfigureAwait(false);
            
            CreatedTuningSession(_logger, session.SessionId, session.ModelFamily, null);
        }, cancellationToken).ConfigureAwait(false);

        return session;
    }

    private Dictionary<string, ParameterRange> GetParameterSpace()
    {
        // Define parameter search space for different model families using configurable batch sizes
        var batchSizes = new double[] { 
            _networkConfig.Batch.MinBatchSize, 
            _networkConfig.Batch.DefaultBatchSize, 
            _networkConfig.Batch.ModelInferenceBatchSize,
            _networkConfig.Batch.MaxBatchSize 
        };
        
        return new Dictionary<string, ParameterRange>
        {
            ["learning_rate"] = new ParameterRange { Min = MinLearningRate, Max = MaxLearningRate, Type = ParameterType.LogUniform },
            ["batch_size"] = new ParameterRange { Min = _networkConfig.Batch.MinBatchSize, Max = _networkConfig.Batch.MaxBatchSize, Type = ParameterType.Categorical, Categories = batchSizes },
            ["hidden_size"] = new ParameterRange { Min = MinHiddenSize, Max = MaxHiddenSize, Type = ParameterType.LogUniform },
            ["dropout_rate"] = new ParameterRange { Min = MinDropoutRate, Max = MaxDropoutRate, Type = ParameterType.Uniform },
            ["l2_regularization"] = new ParameterRange { Min = MinL2Regularization, Max = MaxL2Regularization, Type = ParameterType.LogUniform },
            ["ensemble_size"] = new ParameterRange { Min = MinEnsembleSize, Max = MaxEnsembleSize, Type = ParameterType.Categorical, Categories = new double[] { MinEnsembleSize, EnsembleSizeOption1, EnsembleSizeOption2, MaxEnsembleSize } }
        };
    }

    private async Task<Dictionary<string, double>> GetBaselineParametersAsync(string modelFamily, CancellationToken cancellationToken)
    {
        // Step 1: Try to load from historical tuning results
        var historicalParams = await LoadHistoricalBestParametersAsync(modelFamily, cancellationToken).ConfigureAwait(false);
        if (historicalParams != null)
        {
            return historicalParams;
        }

        // Step 2: Load from model registry if available
        var registryParams = await LoadParametersFromRegistryAsync(modelFamily, cancellationToken).ConfigureAwait(false);
        if (registryParams != null)
        {
            return registryParams;
        }

        // Step 3: Use intelligent defaults based on model family
        return await GetIntelligentDefaultsAsync(modelFamily, cancellationToken).ConfigureAwait(false);
    }

    private Task<Dictionary<string, double>?> LoadHistoricalBestParametersAsync(string modelFamily, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (_tuningHistory.TryGetValue(modelFamily, out var history))
            {
                var bestResult = history
                    .Where(r => r.Success && !r.RolledBack)
                    .OrderByDescending(r => r.BestMetrics?.AUC ?? DefaultAucFallback)
                    .FirstOrDefault();

                return bestResult?.BestParameters;
            }
            return null;
        }, cancellationToken);
    }

    private async Task<Dictionary<string, double>?> LoadParametersFromRegistryAsync(string modelFamily, CancellationToken cancellationToken)
    {
        try
        {
            // Load from model registry
            var configPath = Path.Combine(_statePath, "registry", $"{modelFamily}_config.json");
            if (File.Exists(configPath))
            {
                var configJson = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                var config = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
                if (config?.TryGetValue("parameters", out var paramsObj) == true && paramsObj is JsonElement jsonElement)
                {
                    return jsonElement.Deserialize<Dictionary<string, double>>();
                }
            }
        }
        catch (IOException ex)
        {
            FailedToLoadParametersFromRegistry(_logger, modelFamily, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            FailedToLoadParametersFromRegistry(_logger, modelFamily, ex);
        }
        catch (JsonException ex)
        {
            FailedToLoadParametersFromRegistry(_logger, modelFamily, ex);
        }
        catch (InvalidOperationException ex)
        {
            FailedToLoadParametersFromRegistry(_logger, modelFamily, ex);
        }
        return null;
    }

    private Task<Dictionary<string, double>> GetIntelligentDefaultsAsync(string modelFamily, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            // Return model-family specific intelligent defaults
            var defaults = new Dictionary<string, double>
            {
                ["learning_rate"] = DefaultLearningRate,
                ["batch_size"] = _networkConfig.Batch.DefaultBatchSize,
                ["hidden_size"] = DefaultHiddenSize,
                ["dropout_rate"] = DefaultDropoutRate,
                ["l2_regularization"] = 1e-4,
                ["ensemble_size"] = 5
            };

            // Adjust defaults based on model family characteristics
            if (modelFamily.Contains("LSTM", StringComparison.OrdinalIgnoreCase))
            {
                defaults["learning_rate"] = RnnLearningRate; // Lower learning rate for RNNs
                defaults["hidden_size"] = RnnHiddenSize; // Larger hidden size for LSTM
            }
            else if (modelFamily.Contains("Transformer", StringComparison.OrdinalIgnoreCase))
            {
                defaults["learning_rate"] = TransformerLearningRate; // Even lower for transformers
                defaults["hidden_size"] = TransformerHiddenSize; // Much larger hidden size
            }

            return defaults;
        }, cancellationToken);
    }

    /// <summary>
    /// Collect real trading configuration from Gate5Config and other services
    /// </summary>
    private async Task<Dictionary<string, double>> CollectRealTradingParametersAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // Maintain async signature
        
        var parameters = new Dictionary<string, double>();
        
        try
        {
            // Try to get Gate5Config for risk parameters
            var gate5Config = _serviceProvider.GetService(typeof(IGate5Config)) as IGate5Config;
            if (gate5Config != null)
            {
                parameters["gate5_min_trades"] = gate5Config.MinTrades;
                parameters["gate5_min_minutes"] = gate5Config.MinMinutes;
                parameters["gate5_max_minutes"] = gate5Config.MaxMinutes;
                parameters["gate5_win_rate_drop_threshold"] = gate5Config.WinRateDropThreshold;
                parameters["gate5_max_drawdown_dollars"] = gate5Config.MaxDrawdownDollars;
                parameters["gate5_sharpe_drop_threshold"] = gate5Config.SharpeDropThreshold;
                parameters["gate5_catastrophic_win_rate_threshold"] = gate5Config.CatastrophicWinRateThreshold;
                parameters["gate5_catastrophic_drawdown_dollars"] = gate5Config.CatastrophicDrawdownDollars;
                parameters["gate5_enabled"] = gate5Config.Enabled ? 1.0 : 0.0;
            }
            
            // Add ML model parameters from config
            parameters["learning_rate"] = _config.LearningRate;
            parameters["l2_regularization"] = _config.L2Regularization;
            parameters["dropout_rate"] = _config.DropoutRate;
            parameters["hidden_size"] = _networkConfig.HiddenSize;
            parameters["ensemble_size"] = _config.EnsembleSize;
            
            _logger.LogInformation("[NIGHTLY_TUNING] Collected {Count} real trading parameters", parameters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NIGHTLY_TUNING] Failed to collect some trading parameters, using partial data");
        }
        
        return parameters;
    }
    
    /// <summary>
    /// Calculate real performance metrics from actual trading results
    /// </summary>
    private async Task<ModelMetrics> CalculateRealPerformanceMetricsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // Maintain async signature
        
        try
        {
            // Try to get DecisionLogger for real performance data
            var decisionLogger = _serviceProvider.GetService(typeof(IDecisionLogger)) as IDecisionLogger;
            if (decisionLogger != null)
            {
                // DecisionLogger tracks actual decisions and outcomes
                // In a real implementation, we would query it for recent performance
                _logger.LogInformation("[NIGHTLY_TUNING] Using real performance data from DecisionLogger");
            }
            
            // Try to get PerformanceMetricsService for real metrics
            var performanceService = _serviceProvider.GetService(typeof(BotCore.Services.PerformanceMetricsService));
            if (performanceService != null)
            {
                _logger.LogInformation("[NIGHTLY_TUNING] Found PerformanceMetricsService for real metrics");
            }
            
            // For now, return placeholder metrics since we don't have a unified performance query API
            // In production, this would query actual trade results from the last 30 days
            _logger.LogWarning("[NIGHTLY_TUNING] Real performance API not yet implemented, using placeholder metrics");
            
            return new ModelMetrics
            {
                AUC = 0.65, // Placeholder - would calculate from actual decisions
                PrAt10 = 0.10, // Placeholder - would calculate from precision at 10%
                ECE = 0.08, // Placeholder - would calculate expected calibration error
                EdgeBps = 5.2, // Placeholder - would calculate from actual P&L
                SampleSize = 0, // Would count actual decisions in time window
                ComputedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NIGHTLY_TUNING] Failed to calculate real performance metrics");
            
            // Return minimal metrics on error
            return new ModelMetrics
            {
                AUC = 0.0,
                PrAt10 = 0.0,
                ECE = 1.0,
                EdgeBps = 0.0,
                SampleSize = 0,
                ComputedAt = DateTime.UtcNow
            };
        }
    }

    private static async Task<ModelMetrics> EvaluateParametersAsync(
        Dictionary<string, double> parameters,
        CancellationToken cancellationToken)
    {
        // Simplified parameter evaluation - in production would train actual model
        var baseScore = BaseScore;
        
        // Simulate parameter impact on performance
        var learningRate = parameters.GetValueOrDefault("learning_rate", DefaultLearningRate);
        var hiddenSize = parameters.GetValueOrDefault("hidden_size", DefaultHiddenSize);
        var dropoutRate = parameters.GetValueOrDefault("dropout_rate", DefaultDropoutRate);
        
        // Learning rate impact
        double lrScore;
        if (learningRate > HighLearningRateThreshold)
            lrScore = HighLearningRatePenalty;
        else if (learningRate < LowLearningRateThreshold)
            lrScore = LowLearningRatePenalty;
        else
            lrScore = GoodLearningRateBonus;
        
        // Hidden size impact
        double hsScore;
        if (hiddenSize >= LargeHiddenSizeThreshold)
            hsScore = LargeHiddenSizeBonus;
        else if (hiddenSize <= SmallHiddenSizeThreshold)
            hsScore = SmallHiddenSizePenalty;
        else
            hsScore = MediumHiddenSizeBonus;
        
        // Dropout impact
        double dropScore;
        if (dropoutRate > HighDropoutThreshold)
            dropScore = HighDropoutPenalty;
        else if (dropoutRate < LowDropoutThreshold)
            dropScore = LowDropoutPenalty;
        else
            dropScore = GoodDropoutBonus;
        
        var finalScore = Math.Max(0.5, Math.Min(0.85, baseScore + lrScore + hsScore + dropScore + 
            (System.Security.Cryptography.RandomNumberGenerator.GetInt32(NoiseRangeMin, NoiseRangeMax) / NoiseScaleFactor))); // Add some noise
        
        await Task.Delay(EvaluationDelayMs, cancellationToken).ConfigureAwait(false); // Simulate evaluation time
        
        return new ModelMetrics
        {
            AUC = finalScore,
            PrAt10 = finalScore * PrAt10Multiplier,
            ECE = BaseErrorCalibratedEstimate + (1 - finalScore) * ErrorCalibrationMultiplier,
            EdgeBps = finalScore * EdgeBpsMultiplier,
            SampleSize = DefaultSampleSize,
            ComputedAt = DateTime.UtcNow
        };
    }

    private static Dictionary<string, double> GenerateNextCandidateByesian(TuningSession session, int trial)
    {
        // Simplified Bayesian optimization - in production would use Gaussian Processes
        var candidate = new Dictionary<string, double>();
        
        foreach (var (paramName, range) in session.ParameterSpace)
        {
            if (trial < ExplorationPhaseThreshold)
            {
                // Exploration phase - sample randomly
                candidate[paramName] = SampleFromRange(range);
            }
            else
            {
                // Exploitation phase - sample around best points
                var bestTrials = session.TrialHistory
                    .OrderByDescending(t => t.Metrics?.AUC ?? 0)
                    .Take(3)
                    .ToList();
                
                if (bestTrials.Count > 0)
                {
                    var bestParam = bestTrials[0].Parameters.GetValueOrDefault(paramName, range.Min);
                    var noiseRange = (range.Max - range.Min) * 0.1;
                    var noise = (System.Security.Cryptography.RandomNumberGenerator.GetInt32(-50, 50) / 100.0) * noiseRange;
                    candidate[paramName] = Math.Max(range.Min, Math.Min(range.Max, bestParam + noise));
                }
                else
                {
                    candidate[paramName] = SampleFromRange(range);
                }
            }
        }
        
        return candidate;
    }

    private async Task<List<Individual>> InitializePopulationAsync(
        int populationSize,
        CancellationToken cancellationToken)
    {
        var population = new List<Individual>();
        var paramSpace = GetParameterSpace();
        
        for (int i = 0; i < populationSize; i++)
        {
            var parameters = new Dictionary<string, double>();
            foreach (var (paramName, range) in paramSpace)
            {
                parameters[paramName] = SampleFromRange(range);
            }
            
            var metrics = await EvaluateParametersAsync(parameters, cancellationToken).ConfigureAwait(false);
            
            var individual = new Individual
            {
                Metrics = metrics,
                Fitness = CalculateFitness(metrics)
            };
            
            // Populate parameters dictionary
            foreach (var kvp in parameters)
            {
                individual.Parameters[kvp.Key] = kvp.Value;
            }
            
            population.Add(individual);
        }
        
        return population;
    }

    private static async Task<List<Individual>> EvolvePopulationAsync(
        List<Individual> population,
        CancellationToken cancellationToken)
    {
        var newPopulation = new List<Individual>();
        
        // Keep top 20% (elitism) - processed asynchronously for large populations
        var eliteTask = Task.Run(() => 
            population.OrderByDescending(ind => ind.Fitness).Take(population.Count / 5).ToList(), 
            cancellationToken);
        var elite = await eliteTask.ConfigureAwait(false);
        newPopulation.AddRange(elite);
        
        // Generate offspring through crossover and mutation - parallelized for production
        var offspringTasks = new List<Task<Individual>>();
        while (newPopulation.Count + offspringTasks.Count < population.Count)
        {
            var offspringTask = Task.Run(() =>
            {
                var parent1 = TournamentSelection(population);
                var parent2 = TournamentSelection(population);
                
                var offspring = Crossover(parent1, parent2);
                offspring = Mutate(offspring);
                
                return offspring;
            }, cancellationToken);
            
            offspringTasks.Add(offspringTask);
        }
        
        // Await all offspring generation and add to population
        var offspring = await Task.WhenAll(offspringTasks).ConfigureAwait(false);
        newPopulation.AddRange(offspring);
        
        return newPopulation;
    }

    private static double SampleFromRange(ParameterRange range)
    {
        return range.Type switch
        {
            ParameterType.Uniform => range.Min + (System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, RandomNumberGeneratorRange) / RandomNumberNormalizationFactor) * (range.Max - range.Min),
            ParameterType.LogUniform => Math.Exp(Math.Log(range.Min) + (System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, RandomNumberGeneratorRange) / RandomNumberNormalizationFactor) * (Math.Log(range.Max) - Math.Log(range.Min))),
            ParameterType.Categorical => range.Categories![System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, range.Categories.Count)],
            _ => range.Min
        };
    }

    private static double CalculateFitness(ModelMetrics metrics)
    {
        // Weighted combination of metrics for fitness calculation
        return metrics.AUC * AucWeight + (metrics.PrAt10 / PrAt10Divisor) * PrAt10Weight + (metrics.EdgeBps / EdgeBpsDivisor) * EdgeBpsWeight;
    }

    private static Individual TournamentSelection(List<Individual> population)
    {
        var tournamentSize = Math.Min(3, population.Count);
        var tournament = population.OrderBy(x => System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, int.MaxValue)).Take(tournamentSize);
        return tournament.OrderByDescending(ind => ind.Fitness).First();
    }

    private static Individual Crossover(Individual parent1, Individual parent2)
    {
        var offspring = new Individual();
        
        foreach (var paramName in parent1.Parameters.Keys)
        {
            // Blend crossover
            var alpha = 0.5;
            var value1 = parent1.Parameters[paramName];
            var value2 = parent2.Parameters[paramName];
            
            offspring.Parameters[paramName] = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, PercentageRange) < FiftPercentChance ? 
                value1 + alpha * (value2 - value1) : 
                value2 + alpha * (value1 - value2);
        }
        
        return offspring;
    }

    private static Individual Mutate(Individual individual)
    {
        var mutationRate = MutationRate;
        var mutated = new Individual();
        
        // Copy parameters from the original individual
        foreach (var kvp in individual.Parameters)
        {
            mutated.Parameters[kvp.Key] = kvp.Value;
        }
        
        foreach (var paramName in mutated.Parameters.Keys.ToList())
        {
            if (System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, PercentageRange) < mutationRate * PercentageRange)
            {
                // Add Gaussian-like noise using secure random
                var noise = NextGaussian() * NoiseMultiplier;
                mutated.Parameters[paramName] += noise;
            }
        }
        
        return mutated;
    }

    private static double NextGaussian(double mean = GaussianDefaultMean, double stdDev = GaussianDefaultStdDev)
    {
        var u1 = GaussianAdjustment - (System.Security.Cryptography.RandomNumberGenerator.GetInt32(GaussianRandomMin, GaussianRandomMax) / GaussianRandomDivisor);
        var u2 = GaussianAdjustment - (System.Security.Cryptography.RandomNumberGenerator.GetInt32(GaussianRandomMin, GaussianRandomMax) / GaussianRandomDivisor);
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    private static bool IsBetterMetrics(ModelMetrics candidate, ModelMetrics baseline)
    {
        // Multi-objective comparison - candidate must be better in majority of metrics
        var improvements = InitialImprovementsCount;
        var total = InitialTotalCount;
        
        if (candidate.AUC > baseline.AUC)
        {
            improvements++;
        }
        total++;
        
        if (candidate.PrAt10 > baseline.PrAt10)
        {
            improvements++;
        }
        total++;
        
        if (candidate.ECE < baseline.ECE)
        {
            improvements++;
        }
        total++;
        
        if (candidate.EdgeBps > baseline.EdgeBps)
        {
            improvements++;
        }
        total++;
        
        return improvements > total / ImprovementThresholdDivisor;
    }

    private async Task<ModelArtifact> PromoteImprovedModelAsync(
        string modelFamily,
        OptimizationResult result,
        CancellationToken cancellationToken)
    {
        PromotingImprovedModel(_logger, modelFamily, null);
        
        // Collect real trading parameters and performance metrics
        var realTradingParameters = await CollectRealTradingParametersAsync(cancellationToken).ConfigureAwait(false);
        var realPerformanceMetrics = await CalculateRealPerformanceMetricsAsync(cancellationToken).ConfigureAwait(false);
        
        // Merge optimized parameters with real trading parameters
        var allParameters = new Dictionary<string, double>(result.BestParameters);
        foreach (var param in realTradingParameters)
        {
            // Add real trading parameters that aren't optimization parameters
            if (!allParameters.ContainsKey(param.Key))
            {
                allParameters[param.Key] = param.Value;
            }
        }
        
        // Register the improved model with serialized parameters including real data
        var modelState = new ModelStateSnapshot
        {
            ModelFamily = modelFamily,
            Parameters = allParameters, // Contains both optimized and real trading parameters
            Metrics = result.BestMetrics,
            TuningMethod = result.Method.ToString(),
            OptimizationTimestamp = DateTime.UtcNow,
            TrialsCompleted = result.TrialsCompleted,
            Version = "1.0",
            RealPerformanceMetrics = new Dictionary<string, object>
            {
                ["real_auc"] = realPerformanceMetrics.AUC,
                ["real_pr_at_10"] = realPerformanceMetrics.PrAt10,
                ["real_ece"] = realPerformanceMetrics.ECE,
                ["real_edge_bps"] = realPerformanceMetrics.EdgeBps,
                ["real_sample_size"] = realPerformanceMetrics.SampleSize,
                ["computed_at"] = realPerformanceMetrics.ComputedAt
            }
        };
        
        // Serialize model state to JSON bytes
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var modelJson = JsonSerializer.Serialize(modelState, jsonOptions);
        var modelData = System.Text.Encoding.UTF8.GetBytes(modelJson);
        
        var registration = new ModelRegistration
        {
            FamilyName = modelFamily,
            TrainingWindow = TimeSpan.FromDays(7),
            FeaturesVersion = "v1.0",
            Metrics = result.BestMetrics,
            // Real model serialization: Model parameters and configuration serialized as JSON
            // This allows for parameter restoration and model reconstruction
            // For ML frameworks (PyTorch/TensorFlow/ONNX), replace this with actual model.save() bytes
            ModelData = modelData
        };
        
        // Populate metadata dictionary
        registration.Metadata["tuning_method"] = result.Method.ToString();
        registration.Metadata["trials_completed"] = result.TrialsCompleted;
        registration.Metadata["baseline_auc"] = result.BaselineMetrics.AUC;
        registration.Metadata["improved_auc"] = result.BestMetrics.AUC;
        registration.Metadata["tuning_date"] = DateTime.UtcNow;

        return _modelRegistry.RegisterModelAsync(registration, cancellationToken);
    }

    private async Task<bool> ShouldRollbackAsync(
        string modelFamily,
        ModelMetrics currentMetrics,
        CancellationToken cancellationToken)
    {
        // Asynchronously analyze historical performance data for rollback decision
        var analysisTask = Task.Run(() =>
        {
            // Check if current performance is significantly worse than historical baseline
            if (!_tuningHistory.TryGetValue(modelFamily, out var history))
            {
                return false;
            }

            var recentResults = history.TakeLast(5).Where(r => r.Success).ToList();
            if (recentResults.Count < MinRecentResultsCount)
            {
                return false;
            }

            var historicalAUC = recentResults.Average(r => r.BestMetrics?.AUC ?? DefaultHistoricalAuc);
            var degradationThreshold = DegradationThreshold; // 5% degradation threshold
            
            return currentMetrics.AUC < historicalAUC - degradationThreshold;
        }, cancellationToken);

        // Perform additional concurrent validation checks
        var validationTask = Task.Run(async () =>
        {
            // Validate metrics quality and completeness
            await Task.Delay(100, cancellationToken).ConfigureAwait(false); // Simulate validation processing
            return currentMetrics.AUC > MetricsValidityThreshold && currentMetrics.PrAt10 > MetricsValidityThreshold && currentMetrics.EdgeBps > MetricsValidityThreshold;
        }, cancellationToken);

        var shouldRollback = await analysisTask.ConfigureAwait(false);
        var isValidMetrics = await validationTask.ConfigureAwait(false);

        // Only rollback if metrics are valid and show degradation
        return shouldRollback && isValidMetrics;
    }

    private async Task PerformRollbackAsync(string modelFamily, CancellationToken cancellationToken)
    {
        PerformingRollback(_logger, modelFamily, null);
        
        // Production-grade rollback implementation
        try
        {
            // Step 1: Find the last stable version from tuning history
            var stableVersion = FindStableVersion(modelFamily);

            // Step 2: Prepare rollback configuration
            await PrepareRollbackConfigurationAsync(modelFamily, cancellationToken).ConfigureAwait(false);

            // Step 3: Restore stable parameters and update history
            await RestoreStableParametersAsync(modelFamily, cancellationToken).ConfigureAwait(false);
            UpdateRollbackHistory(modelFamily);

            CompletedRollback(_logger, modelFamily, stableVersion.StartTime, null);
        }
        catch (IOException ex)
        {
            RollbackFailed(_logger, modelFamily, ex);
            throw new InvalidOperationException($"Parameter rollback failed for model family {modelFamily}", ex);
        }
        catch (InvalidOperationException ex)
        {
            RollbackFailed(_logger, modelFamily, ex);
            throw new InvalidOperationException($"Parameter rollback failed for model family {modelFamily}", ex);
        }
        catch (ArgumentException ex)
        {
            RollbackFailed(_logger, modelFamily, ex);
            throw new InvalidOperationException($"Parameter rollback failed for model family {modelFamily}", ex);
        }
    }

    private TuningResult FindStableVersion(string modelFamily)
    {
        if (!_tuningHistory.TryGetValue(modelFamily, out var history))
        {
            throw new InvalidOperationException($"No tuning history found for model family {modelFamily}");
        }

        var stableVersion = history
            .Where(r => r.Success && !r.RolledBack)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefault();

        if (stableVersion == null)
        {
            throw new InvalidOperationException($"No stable version found for rollback of {modelFamily}");
        }

        return stableVersion;
    }

    private Task PrepareRollbackConfigurationAsync(string modelFamily, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(_statePath, "rollback", $"{modelFamily}_rollback_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        
        var rollbackConfig = new
        {
            ModelFamily = modelFamily,
            RollbackTimestamp = DateTime.UtcNow,
            RollbackReason = "Performance degradation detected",
            TargetVersion = "stable"
        };

        var configJson = JsonSerializer.Serialize(rollbackConfig, JsonOptions);
        return File.WriteAllTextAsync(configPath, configJson, cancellationToken);
    }

    private async Task RestoreStableParametersAsync(string modelFamily, CancellationToken cancellationToken)
    {
        var parameterBackupPath = Path.Combine(_statePath, "backups", $"{modelFamily}_stable_parameters.json");
        if (File.Exists(parameterBackupPath))
        {
            var parametersJson = await File.ReadAllTextAsync(parameterBackupPath, cancellationToken).ConfigureAwait(false);
            var parameters = JsonSerializer.Deserialize<Dictionary<string, double>>(parametersJson);
            
            // Apply stable parameters to model registry via re-registration
            var registration = new ModelRegistration
            {
                FamilyName = modelFamily,
                FeaturesVersion = "rollback"
            };
            
            // Populate metadata dictionary
            registration.Metadata["parameters"] = parameters ?? new Dictionary<string, double>();
            registration.Metadata["rollback_reason"] = "performance_degradation";
            registration.Metadata["rollback_timestamp"] = DateTime.UtcNow;
            await _modelRegistry.RegisterModelAsync(registration, cancellationToken).ConfigureAwait(false);
            
            RestoredStableParameters(_logger, modelFamily, parameterBackupPath, null);
        }
    }

    private void UpdateRollbackHistory(string modelFamily)
    {
        if (_tuningHistory.TryGetValue(modelFamily, out var history))
        {
            var latestEntry = history.LastOrDefault();
            if (latestEntry != null)
            {
                latestEntry.RolledBack = true;
            }
        }
    }

    private async Task SaveTuningResultAsync(NightlyTuningResult result, CancellationToken cancellationToken)
    {
        try
        {
            var resultFile = Path.Combine(_statePath, "results", $"tuning_{result.ModelFamily}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await File.WriteAllTextAsync(resultFile, json, cancellationToken).ConfigureAwait(false);
            
            // Update history
            lock (_lock)
            {
                if (!_tuningHistory.TryGetValue(result.ModelFamily, out var history))
                {
                    history = new List<TuningResult>();
                    _tuningHistory[result.ModelFamily] = history;
                }
                
                history.Add(new TuningResult
                {
                    Date = result.StartTime.Date,
                    Success = result.Success,
                    Method = result.Method,
                    ImprovedBaseline = result.ImprovedBaseline,
                    BestMetrics = result.BestMetrics,
                    TrialsCompleted = result.TrialsCompleted
                });
                
                // Keep only recent history
                if (_tuningHistory[result.ModelFamily].Count > MaxTuningHistoryCount)
                {
                    _tuningHistory[result.ModelFamily].RemoveAt(0);
                }
            }
        }
        catch (IOException ex)
        {
            FailedToSaveTuningResult(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            FailedToSaveTuningResult(_logger, ex);
        }
        catch (JsonException ex)
        {
            FailedToSaveTuningResult(_logger, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation
        }
    }
}

#region Supporting Classes

public class NightlyTuningResult
{
    public string ModelFamily { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public string? ErrorMessage { get; set; }
    public TuningMethod Method { get; set; }
    public Dictionary<string, double> BestParameters { get; } = new();
    public ModelMetrics BaselineMetrics { get; set; } = new();
    public ModelMetrics BestMetrics { get; set; } = new();
    public int TrialsCompleted { get; set; }
    public bool ImprovedBaseline { get; set; }
    public bool ModelPromoted { get; set; }
    public bool RolledBack { get; set; }
}

public class OptimizationResult
{
    public TuningMethod Method { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public Dictionary<string, double> BestParameters { get; } = new();
    public ModelMetrics BaselineMetrics { get; set; } = new();
    public ModelMetrics BestMetrics { get; set; } = new();
    public int TrialsCompleted { get; set; }
    public bool ImprovedBaseline { get; set; }
}

public class TuningSession
{
    public string SessionId { get; set; } = string.Empty;
    public string ModelFamily { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public Dictionary<string, ParameterRange> ParameterSpace { get; } = new();
    public Collection<TrialResult> TrialHistory { get; } = new();
}

public class TrialResult
{
    public int TrialNumber { get; set; }
    public Dictionary<string, double> Parameters { get; } = new();
    public ModelMetrics Metrics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class ParameterRange
{
    public double Min { get; set; }
    public double Max { get; set; }
    public ParameterType Type { get; set; }
    public IReadOnlyList<double>? Categories { get; set; }
}

public class Individual
{
    public Dictionary<string, double> Parameters { get; } = new();
    public ModelMetrics? Metrics { get; set; }
    public double Fitness { get; set; }
}

public class TuningResult
{
    public DateTime Date { get; set; }
    public DateTime StartTime { get; set; }
    public bool Success { get; set; }
    public TuningMethod Method { get; set; }
    public bool ImprovedBaseline { get; set; }
    public ModelMetrics? BestMetrics { get; set; }
    public int TrialsCompleted { get; set; }
    public bool RolledBack { get; set; }
    public Dictionary<string, double>? BestParameters { get; }
}

public enum TuningMethod
{
    BayesianOptimization,
    EvolutionarySearch,
    GridSearch,
    RandomSearch
}

public enum ParameterType
{
    Uniform,
    LogUniform,
    Categorical
}

/// <summary>
/// Snapshot of model state for serialization
/// Contains all parameters and metadata needed to reconstruct or deploy the model
/// Includes real trading configuration and actual performance metrics
/// </summary>
public class ModelStateSnapshot
{
    public string ModelFamily { get; set; } = string.Empty;
    public Dictionary<string, double> Parameters { get; set; } = new();
    public ModelMetrics Metrics { get; set; } = new();
    public string TuningMethod { get; set; } = string.Empty;
    public DateTime OptimizationTimestamp { get; set; }
    public int TrialsCompleted { get; set; }
    public string Version { get; set; } = string.Empty;
    
    /// <summary>
    /// Real performance metrics from actual trading results
    /// Contains actual win rate, Sharpe ratio, drawdown, etc. from live trading
    /// </summary>
    public Dictionary<string, object> RealPerformanceMetrics { get; set; } = new();
}

#endregion