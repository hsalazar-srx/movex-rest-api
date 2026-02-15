using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Movex.API.Middleware
{
    public class ApiKeyOptions
    {
        public string? Primary { get; set; }
        public string? Admin { get; set; }
    }

    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
        private readonly IOptionsMonitor<ApiKeyOptions> _optionsMonitor;
        private readonly IHostEnvironment _environment;

        private const string ApiKeyHeader = "X-API-Key";
        private const string AdminKeyHeader = "X-Admin-Key";

        public ApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            IOptionsMonitor<ApiKeyOptions> optionsMonitor,
            ILogger<ApiKeyAuthenticationMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _optionsMonitor = optionsMonitor;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Allow health endpoint without key
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await _next(context);
                return;
            }

            // Allow Swagger UI in Development without key
            if (_environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger"))
            {
                await _next(context);
                return;
            }

            var options = _optionsMonitor.CurrentValue;

            // If admin key is provided and valid, allow (for admin-only routes and ops)
            if (context.Request.Headers.TryGetValue(AdminKeyHeader, out var adminHeaderValue) &&
                !string.IsNullOrWhiteSpace(options.Admin) &&
                string.Equals(adminHeaderValue, options.Admin, System.StringComparison.Ordinal))
            {
                _logger.LogInformation("Admin access granted for {Path}", context.Request.Path);
                await _next(context);
                return;
            }

            // Otherwise require regular API key
            var configuredKey = options.Primary;
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                _logger.LogWarning("API key not configured; denying request");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("API key not configured");
                return;
            }

            if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
                !string.Equals(providedKey, configuredKey, System.StringComparison.Ordinal))
            {
                _logger.LogWarning("API key missing or invalid for {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            await _next(context);
        }
    }
}