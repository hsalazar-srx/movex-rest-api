// Controllers/Db2QueryController.cs
// Custom DB2 direct-query endpoints for part number intelligence.
// These endpoints have no M3 MI equivalent — they query MVXCDTA tables directly via ODBC.
//
// GET /api/mitpop/search      — reverse alias lookup (POPN → ITNO)
// GET /api/mitmas/next-sequence — next available sequence for a part number prefix
//
// Both endpoints are consumed by Oskar (S3-1, S3-2). See Oskar src/adapters/erp/movex.py
// methods lookup_by_alias() and get_next_itno_sequence() for the caller contract.
using System.Data.Odbc;
using Microsoft.AspNetCore.Mvc;
using Movex.API.Services;

namespace Movex.API.Controllers;

[ApiController]
public class Db2QueryController : ControllerBase
{
    private readonly Db2QueryService _db2;
    private readonly ILogger<Db2QueryController> _logger;
    private readonly string _defaultCono;

    public Db2QueryController(
        Db2QueryService db2,
        ILogger<Db2QueryController> logger,
        IConfiguration configuration)
    {
        _db2         = db2;
        _logger      = logger;
        _defaultCono = configuration["M3:DEFAULT_COMPANY"] ?? "300";
    }

    /// <summary>
    /// Reverse alias lookup: customer part number (POPN) → Scanfil APAC ITNO(s).
    ///
    /// Queries MVXCDTA.MITPOP WHERE MPPOPN = popn [AND MPE0PA = e0pa].
    /// Returns all matching ITNOs so the caller can apply full_match / partial_match / no_match logic.
    ///
    /// No M3 MI program supports this direction (MMS025MI.LstAlias is ITNO-first).
    /// </summary>
    /// <param name="cono">Company number (default: M3:DEFAULT_COMPANY from config)</param>
    /// <param name="popn">Customer / external part number (MITPOP.MPPOPN). Required.</param>
    /// <param name="e0pa">Optional partner/customer code filter (MITPOP.MPE0PA = OCUSMA.OKCUNO).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("api/mitpop/search")]
    [ProducesResponseType(typeof(MitpopSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SearchMitpop(
        [FromQuery] string? cono,
        [FromQuery] string? popn,
        [FromQuery] string? e0pa,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(popn))
            return BadRequest(new { success = false, error = "popn is required" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        try
        {
            var records = await _db2.SearchByAliasAsync(conoResolved, popn.Trim(), e0pa?.Trim(), cancellationToken);

            return Ok(new MitpopSearchResponse
            {
                Success = true,
                Cono    = conoResolved,
                Popn    = popn.Trim(),
                Data    = new MitpopSearchData
                {
                    Records = records.Select(r => new MitpopRecordDto
                    {
                        ITNO = r.ITNO,
                        POPN = r.POPN,
                        ALWT = r.ALWT,
                        ALWQ = r.ALWQ,
                        E0PA = r.E0PA,
                    }).ToList(),
                    Count = records.Count,
                }
            });
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error during MITPOP search: cono={Cono} popn={Popn}", conoResolved, popn);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 query failed. Check DB2 connection string and IBM i ODBC driver.",
                detail  = ex.Message,
            });
        }
    }

    /// <summary>
    /// Get the next available sequence number for a Scanfil APAC part number prefix.
    ///
    /// Queries MAX(TRIM(MMITNO)) FROM MVXCDTA.MITMAS WHERE MMITNO LIKE prefix||'%'.
    /// Returns next_seq = (last 4-digit sequence found) + 1, or 1 if no matches exist.
    ///
    /// PN format: LF{CUNO(2)}{commodity(2)}{seq(4)}.
    /// 'LF' is the company prefix (Scanfil APAC legacy) — not a lead-free marker.
    /// </summary>
    /// <param name="cono">Company number (default: M3:DEFAULT_COMPANY from config)</param>
    /// <param name="prefix">Part number prefix to scan (e.g. "LFAP11"). Required.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("api/mitmas/next-sequence")]
    [ProducesResponseType(typeof(NextSequenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetNextSequence(
        [FromQuery] string? cono,
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return BadRequest(new { success = false, error = "prefix is required" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        try
        {
            var nextSeq = await _db2.GetNextSequenceAsync(conoResolved, prefix.Trim(), cancellationToken);

            return Ok(new NextSequenceResponse
            {
                Success = true,
                Cono    = conoResolved,
                Prefix  = prefix.Trim().ToUpperInvariant(),
                Data    = new NextSequenceData { NextSeq = nextSeq }
            });
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error during MITMAS next-sequence: cono={Cono} prefix={Prefix}", conoResolved, prefix);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 query failed. Check DB2 connection string and IBM i ODBC driver.",
                detail  = ex.Message,
            });
        }
    }
}

// ── Response models ───────────────────────────────────────────────────────────

public sealed class MitpopSearchResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public string Popn    { get; init; } = string.Empty;
    public MitpopSearchData Data { get; init; } = new();
}

public sealed class MitpopSearchData
{
    public List<MitpopRecordDto> Records { get; init; } = [];
    public int Count { get; init; }
}

public sealed class MitpopRecordDto
{
    public string  ITNO { get; init; } = string.Empty;
    public string  POPN { get; init; } = string.Empty;
    public string? ALWT { get; init; }
    public string? ALWQ { get; init; }
    public string? E0PA { get; init; }
}

public sealed class NextSequenceResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public string Prefix  { get; init; } = string.Empty;
    public NextSequenceData Data { get; init; } = new();
}

public sealed class NextSequenceData
{
    public int NextSeq { get; init; }
}
