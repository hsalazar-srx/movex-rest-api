// Controllers/Db2QueryController.cs
// Custom DB2 direct-query endpoints — resource-oriented routes, no M3 MI equivalent.
// These query MVXCDTA tables directly via ODBC rather than going through an MI transaction.
//
// GET /api/parts/search-alias — reverse alias lookup (POPN → ITNO), queries MITPOP
// GET /api/parts/next-sequence — next available sequence for a part number prefix, queries MITMAS
// GET /api/customers/list      — active customer list (ECN customer dropdown), queries OCUSMA
// GET /api/bom/{itno}          — single-level BOM (B-1), queries MPDHED+MPDMAT+MITMAS
// GET /api/bom/{itno}/indented — multi-level BOM explosion (B-2), recursive CTE over MPDMAT
// GET /api/bom/where-used/{mtno} — where-used (B-3), reverse MPDMAT lookup
// GET /api/bom/{prno}/circuit-refs — circuit references (C-1), queries ZECNCIRF (Stargile)
// GET /api/mpm/export          — paged ZECNMPMS export (M-1), Stargile manufacturer part master
//
// Renamed 2026-06-16 from table-named routes (mitpop/search, mitmas/next-sequence) to
// resource-oriented names, consistent with the new customers/list endpoint.
//
// BOM endpoints use app-side effectivity filtering — never filter by FDAT/TDAT in the DB2 query
// because FDAT is a cursor-seek position in M3's MPDMAT index, not a filter field (source-verified
// 2026-05-08, PDS002MI-routing-analysis.md, applies to both LstComponent and LstOperation).
//
// All endpoints are consumed by Oskar. See Oskar src/adapters/erp/movex.py for caller contracts.
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
    [HttpGet("api/parts/search-alias")]
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
        {
            _logger.LogError(ex, "Db2:ConnectionString is not configured");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 connection string is not configured. Set Db2:ConnectionString in user-secrets (dev) or the IIS env var Db2__ConnectionString (UAT/prod).",
                detail  = ex.Message,
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
    /// Search the item master by item number or description substring.
    ///
    /// Queries MITMAS directly. MMS200MI.LstItmFac can return the same data, but
    /// M3's list protocol is stateful per socket and cannot filter server-side,
    /// which makes it a poor fit for a search that fires on keystrokes.
    ///
    /// Minimum two characters — a shorter query matches most of the item master
    /// and is expensive as well as useless.
    /// </summary>
    /// <param name="cono">Company number (default: M3:DEFAULT_COMPANY from config)</param>
    /// <param name="q">Item number or description substring. Required, min 2 chars.</param>
    /// <param name="limit">Max rows to return (1-100, default 25).</param>
    /// <param name="includeInactive">Include obsolete items (MMSTAT 90). Default false.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("api/items/search")]
    [ProducesResponseType(typeof(ItemSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SearchItems(
        [FromQuery] string? cono,
        [FromQuery] string? q,
        [FromQuery] int limit,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return BadRequest(new { success = false, error = "q is required and must be at least 2 characters" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();
        var limitResolved = limit <= 0 ? 25 : Math.Min(limit, 100);

        try
        {
            var rows = await _db2.SearchItemsAsync(
                conoResolved, q.Trim(), limitResolved, includeInactive, cancellationToken);

            // SearchItemsAsync fetches limit+1 so truncation is known without a
            // second COUNT(*) over the same predicate.
            var truncated = rows.Count > limitResolved;
            var page = truncated ? rows.Take(limitResolved).ToList() : rows.ToList();

            return Ok(new ItemSearchResponse
            {
                Success = true,
                Cono    = conoResolved,
                Query   = q.Trim(),
                Data    = new ItemSearchData
                {
                    Records = page.Select(r => new ItemSearchRecordDto
                    {
                        ITNO = r.ITNO,
                        ITDS = r.ITDS,
                        ITTY = r.ITTY,
                        STAT = r.STAT,
                        UNMS = r.UNMS,
                        ITCL = r.ITCL,
                    }).ToList(),
                    Count     = page.Count,
                    Truncated = truncated,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
        {
            _logger.LogError(ex, "Db2:ConnectionString is not configured");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 connection string is not configured. Set Db2:ConnectionString in user-secrets (dev) or the IIS env var Db2__ConnectionString (UAT/prod).",
                detail  = ex.Message,
            });
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error during MITMAS item search: cono={Cono} q={Query}", conoResolved, q);
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
    [HttpGet("api/parts/next-sequence")]
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
        {
            _logger.LogError(ex, "Db2:ConnectionString is not configured");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 connection string is not configured. Set Db2:ConnectionString in user-secrets (dev) or the IIS env var Db2__ConnectionString (UAT/prod).",
                detail  = ex.Message,
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
    /// <summary>
    /// List active Movex customers (OCUSMA, OKSTAT='20') for ECN customer selection in Oskar.
    ///
    /// Returns OKCUNO (2-char code embedded in Scanfil APAC part numbers) + OKCUNM (name).
    /// </summary>
    /// <param name="cono">Company number (default: M3:DEFAULT_COMPANY from config)</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("api/customers/list")]
    [ProducesResponseType(typeof(CustomerListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListCustomers(
        [FromQuery] string? cono,
        CancellationToken cancellationToken)
    {
        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        try
        {
            var records = await _db2.ListCustomersAsync(conoResolved, cancellationToken);

            return Ok(new CustomerListResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new CustomerListData
                {
                    Records = records.Select(r => new CustomerRecordDto
                    {
                        CUNO = r.CUNO,
                        NAME = r.NAME,
                    }).ToList(),
                    Count = records.Count,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
        {
            _logger.LogError(ex, "Db2:ConnectionString is not configured");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 connection string is not configured. Set Db2:ConnectionString in user-secrets (dev) or the IIS env var Db2__ConnectionString (UAT/prod).",
                detail  = ex.Message,
            });
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error during customer list: cono={Cono}", conoResolved);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                error   = "DB2 query failed. Check DB2 connection string and IBM i ODBC driver.",
                detail  = ex.Message,
            });
        }
    }

    // ── BOM helpers ───────────────────────────────────────────────────────────

    private static IActionResult Db2Error(ILogger logger, OdbcException ex, string context)
    {
        logger.LogError(ex, "DB2 error in {Context}", context);
        return new ObjectResult(new { success = false, error = "DB2 query failed.", detail = ex.Message })
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
    }

    private IActionResult NotConfigured(ILogger logger, InvalidOperationException ex)
    {
        logger.LogError(ex, "Db2:ConnectionString is not configured");
        return StatusCode(StatusCodes.Status502BadGateway, new
        {
            success = false,
            error = "DB2 connection string is not configured. Set Db2:ConnectionString in user-secrets (dev) or the IIS env var Db2__ConnectionString (UAT/prod).",
            detail = ex.Message,
        });
    }

    // ── B-1: Single-level BOM ─────────────────────────────────────────────────

    /// <summary>
    /// Single-level BOM for an item: MPDHED header + MPDMAT component lines joined MITMAS.
    /// App-side effectivity filter applied. Returns 404 if no MPDHED record exists.
    /// </summary>
    [HttpGet("api/bom/{itno}")]
    [ProducesResponseType(typeof(BomSingleLevelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetSingleLevelBom(
        string itno,
        [FromQuery] string? cono,
        [FromQuery] string? faci,
        [FromQuery] string? strt,
        [FromQuery] int? effectiveOn,
        [FromQuery] bool includeExpired = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(faci))
            return BadRequest(new { success = false, error = "faci is required" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();
        var strtResolved = string.IsNullOrWhiteSpace(strt) ? "001" : strt.Trim();

        try
        {
            var (head, lines) = await _db2.GetSingleLevelBomAsync(
                conoResolved, faci.Trim(), itno.Trim(), strtResolved, effectiveOn, includeExpired, cancellationToken);

            if (head is null)
                return NotFound(new { success = false, error = $"No BOM head found for item '{itno}' in facility '{faci}' structure type '{strtResolved}'" });

            return Ok(new BomSingleLevelResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new BomSingleLevelData
                {
                    Head    = new BomHeadDto(head.PRNO, head.STRT, head.FACI, head.ITDS),
                    Records = lines.Select(l => new BomLineDto(l.MSEQ, l.MTNO, l.ITDS, l.OPNO, l.CNQT, l.PEUN, l.FDAT, l.TDAT, l.ITTY, l.STAT, l.WHLO)).ToList(),
                    Count   = lines.Count,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
            { return NotConfigured(_logger, ex); }
        catch (OdbcException ex)
            { return Db2Error(_logger, ex, $"BOM single-level itno={itno}"); }
    }

    // ── B-2: Multi-level (indented) BOM explosion ─────────────────────────────

    /// <summary>
    /// Multi-level BOM explosion via recursive CTE. PDZ100MI is broken in M3; do not use it.
    /// Flat depth-first output; Oskar assembles the tree client-side.
    /// Performance gate: must return &lt;2s against the largest UAT BOM before Slice B.
    /// </summary>
    [HttpGet("api/bom/{itno}/indented")]
    [ProducesResponseType(typeof(BomIndentedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetIndentedBom(
        string itno,
        [FromQuery] string? cono,
        [FromQuery] string? faci,
        [FromQuery] string? strt,
        [FromQuery] int levl = 12,
        [FromQuery] int? effectiveOn = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(faci))
            return BadRequest(new { success = false, error = "faci is required" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();
        var strtResolved = string.IsNullOrWhiteSpace(strt) ? "001" : strt.Trim();
        var eon = effectiveOn ?? int.Parse(DateTime.Today.ToString("yyyyMMdd"));

        try
        {
            var records = await _db2.GetIndentedBomAsync(
                conoResolved, faci.Trim(), itno.Trim(), strtResolved, levl, eon, cancellationToken);

            return Ok(new BomIndentedResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new BomIndentedData
                {
                    Records = records.Select(r => new BomIndentedDto(
                        r.LEVL, r.PRNO, r.MSEQ, r.MTNO, r.ITDS, r.OPNO, r.CNQT, r.PEUN,
                        r.FDAT, r.TDAT, r.STRT, r.WHLO, r.ITTY)).ToList(),
                    Count   = records.Count,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
            { return NotConfigured(_logger, ex); }
        catch (OdbcException ex)
            { return Db2Error(_logger, ex, $"BOM indented itno={itno}"); }
    }

    // ── B-3: Where-used ───────────────────────────────────────────────────────

    /// <summary>
    /// Where-used: reverse MPDMAT lookup — all parent assemblies that contain this component.
    /// </summary>
    [HttpGet("api/bom/where-used/{mtno}")]
    [ProducesResponseType(typeof(WhereUsedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetWhereUsed(
        string mtno,
        [FromQuery] string? cono,
        [FromQuery] string? faci,
        [FromQuery] int? effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        try
        {
            var records = await _db2.GetWhereUsedAsync(conoResolved, faci, mtno.Trim(), effectiveOn, cancellationToken);

            return Ok(new WhereUsedResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new WhereUsedData
                {
                    Records = records.Select(r => new WhereUsedDto(
                        r.PRNO, r.STRT, r.FACI, r.MSEQ, r.MTNO, r.OPNO, r.CNQT, r.PEUN, r.FDAT, r.TDAT)).ToList(),
                    Count   = records.Count,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
            { return NotConfigured(_logger, ex); }
        catch (OdbcException ex)
            { return Db2Error(_logger, ex, $"Where-used mtno={mtno}"); }
    }

    // ── C-1: Circuit references ───────────────────────────────────────────────

    /// <summary>
    /// Circuit references from Stargile ZECNCIRF (migration/backfill only — retired after cutover).
    /// CIRF is a comma-delimited list with range notation (R1-R5 expanded to R1..R5).
    /// </summary>
    [HttpGet("api/bom/{prno}/circuit-refs")]
    [ProducesResponseType(typeof(CircuitRefResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetCircuitRefs(
        string prno,
        [FromQuery] string? cono,
        [FromQuery] string? faci,
        [FromQuery] string? strt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(faci))
            return BadRequest(new { success = false, error = "faci is required" });

        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();
        var strtResolved = string.IsNullOrWhiteSpace(strt) ? "001" : strt.Trim();

        try
        {
            var records = await _db2.GetCircuitRefsAsync(conoResolved, faci.Trim(), prno.Trim(), strtResolved, cancellationToken);

            return Ok(new CircuitRefResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new CircuitRefData
                {
                    Records = records.Select(r => new CircuitRefDto(r.FACI, r.PRNO, r.STRT, r.MSEQ, r.FDAT, r.CIRF.ToList())).ToList(),
                    Count   = records.Count,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
            { return NotConfigured(_logger, ex); }
        catch (OdbcException ex)
            { return Db2Error(_logger, ex, $"Circuit refs prno={prno}"); }
    }

    // ── M-1: ZECNMPMS export ──────────────────────────────────────────────────

    /// <summary>
    /// Paged export of Stargile manufacturer part master (ZECNMPMS, migration only).
    /// Raw/untransformed — normalisation is the migration script's job.
    /// </summary>
    [HttpGet("api/mpm/export")]
    [ProducesResponseType(typeof(ZecnmpmsExportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ExportZecnmpms(
        [FromQuery] string? cono,
        [FromQuery] int offset = 0,
        [FromQuery] int limit  = 1000,
        CancellationToken cancellationToken = default)
    {
        var conoResolved = string.IsNullOrWhiteSpace(cono) ? _defaultCono : cono.Trim();

        if (limit < 1 || limit > 5000)
            return BadRequest(new { success = false, error = "limit must be between 1 and 5000" });

        try
        {
            var (records, total) = await _db2.ExportZecnmpmsAsync(conoResolved, offset, limit, cancellationToken);

            return Ok(new ZecnmpmsExportResponse
            {
                Success = true,
                Cono    = conoResolved,
                Data    = new ZecnmpmsExportData
                {
                    Records = records.Select(r => new ZecnmpmsDto(
                        r.ITNO, r.SUNO, r.MPZMANPN, r.MPTX30, r.MPZDEFFL, r.MPZEEFDT, r.MPZECNID,
                        r.MPFDAT, r.MPTDAT, r.MPMPRC, r.MPCUCD, r.MPZMPMOQ, r.MPZMPSPQ,
                        r.MPZCAWID, r.MPZREWID, r.MPZMNCNR, r.MPFACI, r.MPLMDT, r.MPLMTM)).ToList(),
                    Offset  = offset,
                    Limit   = limit,
                    Total   = total,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString"))
            { return NotConfigured(_logger, ex); }
        catch (OdbcException ex)
            { return Db2Error(_logger, ex, $"ZECNMPMS export cono={conoResolved}"); }
    }
}

// ── Response models ───────────────────────────────────────────────────────────

public sealed class ItemSearchResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public string Query   { get; init; } = string.Empty;
    public ItemSearchData Data { get; init; } = new();
}

public sealed class ItemSearchData
{
    public List<ItemSearchRecordDto> Records { get; init; } = [];
    public int Count { get; init; }

    /// <summary>True when the result hit the limit — the caller should narrow
    /// the query rather than present a partial list as complete.</summary>
    public bool Truncated { get; init; }
}

public sealed class ItemSearchRecordDto
{
    public string  ITNO { get; init; } = string.Empty;
    public string? ITDS { get; init; }
    public string? ITTY { get; init; }
    public string? STAT { get; init; }
    public string? UNMS { get; init; }
    public string? ITCL { get; init; }
}

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

public sealed class CustomerListResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public CustomerListData Data { get; init; } = new();
}

public sealed class CustomerListData
{
    public List<CustomerRecordDto> Records { get; init; } = [];
    public int Count { get; init; }
}

public sealed class CustomerRecordDto
{
    public string  CUNO { get; init; } = string.Empty;
    public string? NAME { get; init; }
}

// ── BOM DTOs ─────────────────────────────────────────────────────────────────

public sealed record BomHeadDto(string PRNO, string STRT, string FACI, string? ITDS);

public sealed record BomLineDto(
    decimal  MSEQ,
    string   MTNO,
    string?  ITDS,
    decimal  OPNO,
    decimal  CNQT,
    string?  PEUN,
    decimal  FDAT,
    decimal  TDAT,
    string?  ITTY,
    string?  STAT,
    string?  WHLO);

public sealed class BomSingleLevelResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public BomSingleLevelData Data { get; init; } = new();
}

public sealed class BomSingleLevelData
{
    public BomHeadDto    Head    { get; init; } = new("", "", "", null);
    public List<BomLineDto> Records { get; init; } = [];
    public int           Count   { get; init; }
}

public sealed record BomIndentedDto(
    decimal  LEVL,
    string   PRNO,
    decimal  MSEQ,
    string   MTNO,
    string?  ITDS,
    decimal  OPNO,
    decimal  CNQT,
    string?  PEUN,
    decimal  FDAT,
    decimal  TDAT,
    string?  STRT,
    string?  WHLO,
    string?  ITTY);

public sealed class BomIndentedResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public BomIndentedData Data { get; init; } = new();
}

public sealed class BomIndentedData
{
    public List<BomIndentedDto> Records { get; init; } = [];
    public int Count { get; init; }
}

public sealed record WhereUsedDto(
    string   PRNO,
    string?  STRT,
    string?  FACI,
    decimal  MSEQ,
    string   MTNO,
    decimal  OPNO,
    decimal  CNQT,
    string?  PEUN,
    decimal  FDAT,
    decimal  TDAT);

public sealed class WhereUsedResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public WhereUsedData Data { get; init; } = new();
}

public sealed class WhereUsedData
{
    public List<WhereUsedDto> Records { get; init; } = [];
    public int Count { get; init; }
}

public sealed record CircuitRefDto(
    string          FACI,
    string          PRNO,
    string          STRT,
    decimal         MSEQ,
    decimal         FDAT,
    List<string>    CIRF);

public sealed class CircuitRefResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public CircuitRefData Data { get; init; } = new();
}

public sealed class CircuitRefData
{
    public List<CircuitRefDto> Records { get; init; } = [];
    public int Count { get; init; }
}

// All numeric fields are decimal? — DB2 for i returns all numeric types as decimal over ODBC.
public sealed record ZecnmpmsDto(
    string?  ITNO,
    string?  SUNO,
    string?  MPZMANPN,
    string?  MPTX30,
    decimal? MPZDEFFL,
    decimal? MPZEEFDT,
    string?  MPZECNID,
    decimal? MPFDAT,
    decimal? MPTDAT,
    decimal? MPMPRC,
    string?  MPCUCD,
    decimal? MPZMPMOQ,
    decimal? MPZMPSPQ,
    decimal? MPZCAWID,
    decimal? MPZREWID,
    decimal? MPZMNCNR,
    string?  MPFACI,
    decimal? MPLMDT,
    decimal? MPLMTM);

public sealed class ZecnmpmsExportResponse
{
    public bool   Success { get; init; }
    public string Cono    { get; init; } = string.Empty;
    public ZecnmpmsExportData Data { get; init; } = new();
}

public sealed class ZecnmpmsExportData
{
    public List<ZecnmpmsDto> Records { get; init; } = [];
    public int Offset { get; init; }
    public int Limit  { get; init; }
    public int Total  { get; init; }
}
