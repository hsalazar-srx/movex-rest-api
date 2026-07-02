using Microsoft.AspNetCore.Mvc;
using Movex.API.Core.Interfaces;
using Movex.API.Middleware;

namespace Movex.API.Controllers
{
    /// <summary>
    /// WMS API surface — M3 transactions available to the Smart Warehouse system.
    /// Permitted transactions are declared in ApiKeys:WmsAllowedTransactions in config.
    /// Primary and Admin key holders can also call these routes without restriction.
    /// </summary>
    [ApiController]
    [Route("wms")]
    public class WmsTransactionController : ControllerBase
    {
        private readonly ITransactionService _service;
        private readonly ITransactionConfigurationService _configService;
        private readonly ILogger<WmsTransactionController> _logger;
        private readonly IConfiguration _configuration;

        public WmsTransactionController(
            ITransactionService service,
            ITransactionConfigurationService configService,
            ILogger<WmsTransactionController> logger,
            IConfiguration configuration)
        {
            _service = service;
            _configService = configService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Execute a permitted M3 transaction via POST body.
        /// </summary>
        [HttpPost("{program}/{transaction}")]
        public async Task<IActionResult> ExecuteTransaction(
            string program,
            string transaction,
            [FromBody] Dictionary<string, object> request)
        {
            if (!IsAccessible(program, transaction, out var denyReason))
            {
                _logger.LogWarning("[WMS] Forbidden: {Program}/{Transaction} — {Reason}", program, transaction, denyReason);
                return StatusCode(403, new { success = false, error = $"Transaction '{program}/{transaction}' is not available on this API surface" });
            }

            var txDef = _configService.GetTransaction(program, transaction);
            if (txDef == null)
                return NotFound(new { success = false, error = $"Transaction '{transaction}' not found in program '{program}'" });

            if (!string.IsNullOrEmpty(txDef.HttpMethod) && txDef.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, error = "Transaction is configured for GET. Use the GET endpoint." });

            var fieldValues = request.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "");

            try
            {
                var response = await _service.ExecuteTransactionAsync(program, transaction, fieldValues);
                return BuildResult(program, transaction, response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, error = ex.Message, program, transaction });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WMS] {Program}/{Transaction} failed: {Message}", program, transaction, ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message, program, transaction });
            }
        }

        /// <summary>
        /// Execute a permitted M3 transaction via GET query parameters.
        /// </summary>
        [HttpGet("{program}/{transaction}")]
        public async Task<IActionResult> ExecuteTransactionGet(string program, string transaction)
        {
            if (!IsAccessible(program, transaction, out var denyReason))
            {
                _logger.LogWarning("[WMS] Forbidden: {Program}/{Transaction} — {Reason}", program, transaction, denyReason);
                return StatusCode(403, new { success = false, error = $"Transaction '{program}/{transaction}' is not available on this API surface" });
            }

            var txDef = _configService.GetTransaction(program, transaction);
            if (txDef == null)
                return NotFound(new { success = false, error = $"Transaction '{transaction}' not found in program '{program}'" });

            if (!string.Equals(txDef.HttpMethod ?? "POST", "GET", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, error = "Transaction is not configured for GET. Use POST." });

            var fieldValues = Request.Query
                .Where(kvp => !kvp.Key.Equals("raw", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

            try
            {
                var response = await _service.ExecuteTransactionAsync(program, transaction, fieldValues);
                return BuildResult(program, transaction, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WMS] GET {Program}/{Transaction} failed: {Message}", program, transaction, ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message, program, transaction });
            }
        }

        // Returns false (with reason) when the caller holds a Wms key and the transaction
        // is not on the allowed list. Primary/Admin key holders are never restricted.
        private bool IsAccessible(string program, string transaction, out string reason)
        {
            reason = string.Empty;
            var tier = HttpContext.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem] as string;

            if (tier != "Wms")
                return true; // Primary / Admin — unrestricted

            var allowed = _configuration
                .GetSection("ApiKeys:WmsAllowedTransactions")
                .Get<List<string>>();

            if (allowed == null || allowed.Count == 0)
            {
                reason = "no transactions configured for this API surface";
                return false;
            }

            var key = $"{program}/{transaction}";
            if (!allowed.Any(e => e.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "not in allowed list";
                return false;
            }

            return true;
        }

        private IActionResult BuildResult(string program, string transaction, object response)
        {
            if (response is string raw && raw.TrimStart().StartsWith("NOK", StringComparison.OrdinalIgnoreCase))
                return StatusCode(422, new { success = false, error = raw.TrimStart()[4..].Trim(), program, transaction });

            object data = response switch
            {
                Dictionary<string, object?> dict => dict,
                List<Dictionary<string, object?>> list => new { records = list, count = list.Count },
                _ => new { raw = response?.ToString() }
            };

            _logger.LogInformation("[WMS] {Program}/{Transaction} completed", program, transaction);
            return Ok(new { success = true, program, transaction, data, timestamp = DateTime.UtcNow });
        }
    }
}
