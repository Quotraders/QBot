using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Services;
using System.Collections.Generic;

namespace BotCore.Tests.Integration;

/// <summary>
/// Tests to verify Portfolio Risk Management configuration binding
/// Validates that CorrelationCapConfiguration and VolOfVolConfiguration bind correctly from root sections
/// </summary>
public sealed class PortfolioRiskConfigurationTests
{
    [Fact]
    public void CorrelationCapConfiguration_BindsFromRootSection_WithValidValues()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["CorrelationCapConfiguration:CorrelationThreshold"] = "0.7",
            ["CorrelationCapConfiguration:CorrelationWindowMinutes"] = "30",
            ["CorrelationCapConfiguration:MinDataPoints"] = "20",
            ["CorrelationCapConfiguration:DefaultReductionFactor"] = "0.5",
            ["CorrelationCapConfiguration:MinReductionFactor"] = "0.3",
            ["CorrelationCapConfiguration:MaxReductionAmount"] = "0.7"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.Configure<CorrelationCapConfiguration>(configuration.GetSection("CorrelationCapConfiguration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var config = serviceProvider.GetRequiredService<IOptions<CorrelationCapConfiguration>>().Value;

        // Assert
        Assert.Equal(0.7, config.CorrelationThreshold);
        Assert.Equal(30, config.CorrelationWindowMinutes);
        Assert.Equal(20, config.MinDataPoints);
        Assert.Equal(0.5, config.DefaultReductionFactor);
        Assert.Equal(0.3, config.MinReductionFactor);
        Assert.Equal(0.7, config.MaxReductionAmount);
    }

    [Fact]
    public void VolOfVolConfiguration_BindsFromRootSection_WithValidValues()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["VolOfVolConfiguration:VolOfVolThreshold"] = "0.15",
            ["VolOfVolConfiguration:VolSpikeSizeReduction"] = "0.5",
            ["VolOfVolConfiguration:VolSpikeStopWidening"] = "1.5",
            ["VolOfVolConfiguration:VolSpikeOffsetTightening"] = "0.8",
            ["VolOfVolConfiguration:VolHistoryWindowMinutes"] = "60",
            ["VolOfVolConfiguration:MinVolDataPoints"] = "10"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.Configure<VolOfVolConfiguration>(configuration.GetSection("VolOfVolConfiguration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var config = serviceProvider.GetRequiredService<IOptions<VolOfVolConfiguration>>().Value;

        // Assert
        Assert.Equal(0.15, config.VolOfVolThreshold);
        Assert.Equal(0.5, config.VolSpikeSizeReduction);
        Assert.Equal(1.5, config.VolSpikeStopWidening);
        Assert.Equal(0.8, config.VolSpikeOffsetTightening);
        Assert.Equal(60, config.VolHistoryWindowMinutes);
        Assert.Equal(10, config.MinVolDataPoints);
    }

    [Fact]
    public void CorrelationAwareCapService_ThrowsOnInvalidConfiguration()
    {
        // Arrange - Invalid configuration with CorrelationThreshold = 0
        var configData = new Dictionary<string, string>
        {
            ["CorrelationCapConfiguration:CorrelationThreshold"] = "0",
            ["CorrelationCapConfiguration:CorrelationWindowMinutes"] = "30",
            ["CorrelationCapConfiguration:MinDataPoints"] = "20",
            ["CorrelationCapConfiguration:DefaultReductionFactor"] = "0.5",
            ["CorrelationCapConfiguration:MinReductionFactor"] = "0.3",
            ["CorrelationCapConfiguration:MaxReductionAmount"] = "0.7"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.Configure<CorrelationCapConfiguration>(configuration.GetSection("CorrelationCapConfiguration"));
        services.AddSingleton<CorrelationAwareCapService>();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Should throw during service construction
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<CorrelationAwareCapService>());
        
        Assert.Contains("CorrelationThreshold must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void VolOfVolGuardService_ThrowsOnInvalidConfiguration()
    {
        // Arrange - Invalid configuration with VolOfVolThreshold = 0
        var configData = new Dictionary<string, string>
        {
            ["VolOfVolConfiguration:VolOfVolThreshold"] = "0",
            ["VolOfVolConfiguration:VolSpikeSizeReduction"] = "0.5",
            ["VolOfVolConfiguration:VolSpikeStopWidening"] = "1.5",
            ["VolOfVolConfiguration:VolSpikeOffsetTightening"] = "0.8",
            ["VolOfVolConfiguration:VolHistoryWindowMinutes"] = "60",
            ["VolOfVolConfiguration:MinVolDataPoints"] = "10"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.Configure<VolOfVolConfiguration>(configuration.GetSection("VolOfVolConfiguration"));
        services.AddSingleton<VolOfVolGuardService>();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Should throw during service construction
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<VolOfVolGuardService>());
        
        Assert.Contains("VolOfVolThreshold must be positive", exception.Message);
    }
}
