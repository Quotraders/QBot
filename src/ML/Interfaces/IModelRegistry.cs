using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.ML.Models;

namespace TradingBot.ML.Interfaces;

/// <summary>
/// Model registry interface for managing ML models
/// </summary>
public interface IModelRegistry
{
    Task<bool> RegisterModelAsync(string modelName, string version, string modelPath, ModelMetrics metrics);
    string? GetModelPath(string modelName, string? version = null);
    ModelMetrics? GetModelMetrics(string modelName, string? version = null);
    List<string> GetAvailableModels();
    List<string> GetModelVersions(string modelName);
    Task<bool> DeleteModelAsync(string modelName, string? version = null);
}

/// <summary>
/// Feature store interface for managing training features
/// </summary>
public interface IFeatureStore
{
    Task<Dictionary<string, double[]>> GetFeaturesAsync(DateTime startDate, DateTime endDate);
    Task<bool> StoreFeaturesAsync(string featureName, DateTime timestamp, double[] values);
    Task<string[]> GetAvailableFeaturesAsync();
    Task<Dictionary<string, double[]>> GetLatestFeaturesAsync(int count = 100);
}