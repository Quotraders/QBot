extern alias BotCoreTest;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using BotCoreTest::BotCore.Extensions;
using TradingBot.RLAgent;

namespace UnitTests.DI
{
    public class ServiceRegistrationTests
    {
        [Fact]
        public void AddTopstepAuthentication_ShouldRegisterAllServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configuration = new ConfigurationBuilder().Build();
            var customAuthProvider = (CancellationToken ct) => Task.FromResult("test-jwt");

            // Act
            services.AddTopstepAuthentication(configuration, customAuthProvider);
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var httpClient = serviceProvider.GetService<BotCoreTest::BotCore.Services.ITopstepXHttpClient>();
            Assert.NotNull(httpClient);
        }

        [Fact]
        public void AddTopstepAuthentication_WithAuth_ShouldRegisterAuthService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configuration = new ConfigurationBuilder().Build();
            
            var customAuthProvider = (CancellationToken ct) => Task.FromResult("test-jwt");

            // Act
            services.AddTopstepAuthentication(configuration, customAuthProvider);
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var authService = serviceProvider.GetService<BotCoreTest::BotCore.Auth.ITopstepAuth>();
            var httpClient = serviceProvider.GetService<BotCoreTest::BotCore.Services.ITopstepXHttpClient>();
            
            Assert.NotNull(authService);
            Assert.NotNull(httpClient);
        }

        [Fact]
        public void ServiceRegistration_ShouldUseSingletonInstances()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configuration = new ConfigurationBuilder().Build();
            var customAuthProvider = (CancellationToken ct) => Task.FromResult("test-jwt");

            // Act
            services.AddTopstepAuthentication(configuration, customAuthProvider);
            var serviceProvider = services.BuildServiceProvider();

            // Assert - Should return same instance
            var httpClient1 = serviceProvider.GetService<BotCoreTest::BotCore.Services.ITopstepXHttpClient>();
            var httpClient2 = serviceProvider.GetService<BotCoreTest::BotCore.Services.ITopstepXHttpClient>();
            
            Assert.Same(httpClient1, httpClient2);
        }

        [Fact]
        public async Task AuthService_WithCustomProvider_ShouldWork()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configuration = new ConfigurationBuilder().Build();
            var testJwt = "test.jwt.token";
            
            var customAuthProvider = (CancellationToken ct) => Task.FromResult(testJwt);

            services.AddTopstepAuthentication(configuration, customAuthProvider);
            var serviceProvider = services.BuildServiceProvider();

            // Act
            var authService = serviceProvider.GetRequiredService<BotCoreTest::BotCore.Auth.ITopstepAuth>();
            var (jwt, _) = await authService.GetFreshJwtAsync();

            // Assert
            Assert.Equal(testJwt, jwt);
        }

        [Fact]
        public void ModelHotReloadManager_ShouldBeRegisteredAsSingleton()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configData = new Dictionary<string, string?>
            {
                ["ModelHotReload:WatchDirectory"] = "models/rl",
                ["ModelHotReload:DebounceDelayMs"] = "2000",
                ["OnnxEnsemble:MaxBatchSize"] = "32"
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            services.Configure<ModelHotReloadOptions>(configuration.GetSection("ModelHotReload"));
            services.Configure<OnnxEnsembleOptions>(configuration.GetSection("OnnxEnsemble"));
            services.AddSingleton<OnnxEnsembleWrapper>();
            services.AddSingleton<ModelHotReloadManager>();
            services.AddHostedService<ModelHotReloadManager>(provider => 
                provider.GetRequiredService<ModelHotReloadManager>());

            // Act
            var serviceProvider = services.BuildServiceProvider();

            // Assert - Verify ModelHotReloadManager can be resolved
            var hotReloadManager = serviceProvider.GetService<ModelHotReloadManager>();
            Assert.NotNull(hotReloadManager);

            // Verify it's registered as IHostedService too
            var hostedServices = serviceProvider.GetServices<IHostedService>();
            Assert.Contains(hostedServices, service => service is ModelHotReloadManager);
        }

        [Fact]
        public async Task ModelHotReloadManager_StartAsync_ShouldSucceed()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var configData = new Dictionary<string, string?>
            {
                ["ModelHotReload:WatchDirectory"] = "/tmp/test-models",
                ["ModelHotReload:DebounceDelayMs"] = "2000",
                ["OnnxEnsemble:MaxBatchSize"] = "32"
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            services.Configure<ModelHotReloadOptions>(configuration.GetSection("ModelHotReload"));
            services.Configure<OnnxEnsembleOptions>(configuration.GetSection("OnnxEnsemble"));
            services.AddSingleton<OnnxEnsembleWrapper>();
            services.AddSingleton<ModelHotReloadManager>();

            var serviceProvider = services.BuildServiceProvider();
            var hotReloadManager = serviceProvider.GetRequiredService<ModelHotReloadManager>();

            // Ensure directory exists
            System.IO.Directory.CreateDirectory("/tmp/test-models");

            // Act
            var exception = await Record.ExceptionAsync(async () => 
                await hotReloadManager.StartAsync(CancellationToken.None));

            // Assert - StartAsync should complete without throwing
            Assert.Null(exception);

            // Cleanup
            await hotReloadManager.StopAsync(CancellationToken.None);
            hotReloadManager.Dispose();
        }
    }
}