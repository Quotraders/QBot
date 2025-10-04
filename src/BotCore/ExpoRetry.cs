using System;
using Polly;

namespace BotCore
{
    /// <summary>
    /// Exponential backoff retry policy
    /// </summary>
    public static class ExpoRetry
    {
        private const int MaxRetryAttempts = 4;
        private const int SecondRetryAttempt = 2;
        private const int ThirdRetryAttempt = 3;

        /// <summary>
        /// Creates an exponential backoff retry policy
        /// </summary>
        /// <returns>Configured retry policy</returns>
        public static ResiliencePipeline CreatePolicy()
        {
            return new ResiliencePipelineBuilder()
                .AddRetry(new Polly.Retry.RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(30)
                })
                .Build();
        }

        /// <summary>
        /// Gets delay for a specific retry attempt
        /// </summary>
        /// <param name="retryCount">The retry attempt number</param>
        /// <returns>Delay for the retry attempt</returns>
        public static TimeSpan GetRetryDelay(int retryCount) => retryCount switch
        {
            0 => TimeSpan.FromSeconds(1),
            1 => TimeSpan.FromSeconds(2),
            SecondRetryAttempt => TimeSpan.FromSeconds(5),
            ThirdRetryAttempt => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(30),
        };
    }
}
