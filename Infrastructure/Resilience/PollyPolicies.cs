using Polly;

namespace Movex.API.Infrastructure.Resilience
{
    /// <summary>
    /// Polly resilience policies for M3 transaction execution
    /// Includes retry with exponential backoff and circuit breaker pattern
    /// </summary>
    public static class PollyPolicies
    {
        /// <summary>
        /// Combined retry + circuit breaker for async operations
        /// Strategy: Retry 3 times with exponential backoff (200ms, 400ms, 800ms)
        /// Then if 3 failures occur, circuit breaker opens for 30 seconds
        /// </summary>
        public static IAsyncPolicy GetCombinedPolicyAsync()
        {
            // Retry: 3 attempts with exponential backoff
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<OperationCanceledException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(
                        Math.Pow(2, attempt) * 100));

            // Create circuit breaker using builder
            var circuitBreakerPolicyBuilder = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<OperationCanceledException>();

            // Apply circuit breaker with 3 failures before breaking, 30 second timeout
            var circuitBreakerPolicy = circuitBreakerPolicyBuilder
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));

            // Wrap both policies: retry first, then circuit breaker
            return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
        }
    }
}



