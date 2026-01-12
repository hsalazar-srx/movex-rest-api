// Controllers/SecurityController.cs - Security endpoints
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Movex.API.Middleware;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Movex.API.Controllers
{
    [ApiController]
    [Route("api/security")] 
    public class SecurityController : ControllerBase
    {
        private readonly IOptionsMonitor<ApiKeyOptions> _optionsMonitor;
        private readonly IHostEnvironment _env;

        public SecurityController(IOptionsMonitor<ApiKeyOptions> optionsMonitor, IHostEnvironment env)
        {
            _optionsMonitor = optionsMonitor;
            _env = env;
        }

        /// <summary>
        /// Returns whether the primary API key is configured (does not reveal the key)
        /// </summary>
        [HttpGet("api-key/status")]
        public IActionResult GetApiKeyStatus()
        {
            var configured = !string.IsNullOrWhiteSpace(_optionsMonitor.CurrentValue.Primary);
            return Ok(new
            {
                success = true,
                configured,
                environment = _env.EnvironmentName
            });
        }

        /// <summary>
        /// Admin-only: Rotate the primary API key. Development only.
        /// Requires X-Admin-Key header to match configuration.
        /// Generates a new 256-bit Base64 key, stores it via User Secrets, and returns it.
        /// </summary>
        [HttpPost("api-key/rotate")]
        public IActionResult RotateApiKey([FromQuery] string format = "Base64", [FromQuery] int bytes = 32)
        {
            // Ensure admin header is present and valid (defense-in-depth; middleware already allows admin bypass)
            var options = _optionsMonitor.CurrentValue;
            if (string.IsNullOrWhiteSpace(options.Admin))
            {
                return StatusCode(503, new { success = false, error = "Admin key not configured" });
            }
            if (!Request.Headers.TryGetValue("X-Admin-Key", out var adminHeader) || !string.Equals(adminHeader, options.Admin, StringComparison.Ordinal))
            {
                return StatusCode(403, new { success = false, error = "Forbidden" });
            }

            // Only allow rotation via user-secrets in Development
            if (!_env.IsDevelopment())
            {
                return StatusCode(501, new { success = false, error = "Rotation via API is only supported in Development (user-secrets)" });
            }

            // Generate new key
            var rngBytes = new byte[Math.Max(16, bytes)]; // minimum 128-bit
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(rngBytes);
            string newKey = format.Equals("Hex", StringComparison.OrdinalIgnoreCase)
                ? BitConverter.ToString(rngBytes).Replace("-", string.Empty).ToLowerInvariant()
                : Convert.ToBase64String(rngBytes);

            // Compute project path for user-secrets (assumes MMS200MI.API.csproj at content root)
            var csprojPath = System.IO.Path.Combine(_env.ContentRootPath, "MMS200MI.API.csproj");
            if (!System.IO.File.Exists(csprojPath))
            {
                return StatusCode(500, new { success = false, error = $"Project file not found: {csprojPath}" });
            }

            // Run: dotnet user-secrets set "ApiKeys:Primary" "<newKey>" --project "<csproj>"
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("user-secrets");
                psi.ArgumentList.Add("set");
                psi.ArgumentList.Add("ApiKeys:Primary");
                psi.ArgumentList.Add(newKey);
                psi.ArgumentList.Add("--project");
                psi.ArgumentList.Add(csprojPath);

                using var proc = Process.Start(psi);
                var stdout = proc!.StandardOutput.ReadToEnd();
                var stderr = proc!.StandardError.ReadToEnd();
                proc!.WaitForExit();
                if (proc!.ExitCode != 0)
                {
                    return StatusCode(500, new { success = false, error = "Failed to set secret via user-secrets", details = stderr });
                }

                // With AddUserSecrets(..., reloadOnChange: true) and IOptionsMonitor, new value should be picked up automatically.
                return Ok(new
                {
                    success = true,
                    message = "API key rotated",
                    format = format,
                    bytes = rngBytes.Length,
                    apiKey = newKey
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
