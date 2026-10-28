// Controllers/CustomerController.cs
// GET /api/customers/basic — customer number, name and status for chosen statuses (?status=20,90).
// Wraps CRS610MI/LstByNumber, which has no status input: the full list is read and filtered on STAT here.
using Microsoft.AspNetCore.Mvc;
using Movex.API.Core.Interfaces;

namespace Movex.API.Controllers;

[ApiController]
public class CustomerController : ControllerBase
{
    private readonly ITransactionService _service;
    private readonly ILogger<CustomerController> _logger;
    private readonly string _defaultCono;

    public CustomerController(
        ITransactionService service,
        ILogger<CustomerController> logger,
        IConfiguration configuration)
    {
        _service     = service;
        _logger      = logger;
        _defaultCono = configuration["M3:DEFAULT_COMPANY"] ?? "300";
    }

    /// <summary>
    /// Customers (CUNO, CUNM, STAT) whose status is in the list, ordered by customer number.
    /// </summary>
    /// <param name="cono">Company number (default: M3:DEFAULT_COMPANY from config)</param>
    /// <param name="status">Comma-separated status codes, e.g. "20", "20,90", "20,90,10". Default "20".</param>
    [HttpGet("api/customers/basic")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListCustomersBasic([FromQuery] string? cono, [FromQuery] string? status)
    {
        var statuses = (string.IsNullOrWhiteSpace(status) ? "20" : status)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (statuses.Count is 0 or > 5 || statuses.Any(s => s.Length > 2 || !s.All(char.IsAsciiLetterOrDigit)))
            return BadRequest(new { success = false, error = "status must be 1-5 comma-separated codes of up to 2 letters or digits, e.g. 20,90" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        try
        {
            var response = await _service.ExecuteTransactionAsync(
                "CRS610MI", "LstByNumber", new Dictionary<string, string> { ["CONO"] = conoResolved });

            if (response is not List<Dictionary<string, object?>> all)
            {
                _logger.LogError("CRS610MI/LstByNumber returned an unexpected response: {Response}", response);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = "CRS610MI/LstByNumber did not return a list." });
            }

            var records = all
                .Where(r => r.TryGetValue("STAT", out var s) && statuses.Contains(s?.ToString() ?? string.Empty))
                .ToList();

            return Ok(new
            {
                success  = true,
                cono     = conoResolved,
                statuses,
                data     = new { records, count = records.Count },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer basic failed: cono={Cono}", conoResolved);
            return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = "M3 request failed.", detail = ex.Message });
        }
    }
}
