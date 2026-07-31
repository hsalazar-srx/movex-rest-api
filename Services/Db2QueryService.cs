// Services/Db2QueryService.cs
// Direct DB2/AS400 queries for endpoints that have no M3 MI equivalent.
//
// MITPOP reverse alias lookup: no MI program supports POPN→ITNO direction (confirmed 2026-05-11).
//   MMS025MI.GetAlias / LstAlias are both ITNO-first. Custom DB2 query is the only solution.
//   Source: Oskar ai/memory/02-movex-erp-authority.md §7.
//
// MITMAS next sequence: used to suggest the next available Scanfil APAC part number
//   (LF + CUNO(2) + commodity(2) + seq(4)). No MI program exposes MAX(ITNO) by prefix.
//
// OCUSMA active customer list: no MI program lists customers for dropdown selection.
//   Used by Oskar to populate the ECN customer field (S-task, 2026-06-16).
//
// BOM endpoints (B-1/B-2/B-3): DB2 direct queries over MPDHED/MPDMAT/MITMAS in MVXCDTA.
//   LstComponent MI transaction exists but FDAT cursor-seek gotcha makes it unreliable for
//   full BOM reads (same bug as LstOperation — confirmed PDS002MI-routing-analysis.md 2026-05-08).
//   All BOM reads fetch all rows then apply effectivity filtering in app code.
//   PDZ100MI (multi-level explosion) is broken in M3 — recursive CTE used instead.
//
// C-1 / M-1: Stargile custom tables ZECNCIRF and ZECNMPMS, schema MOVEX (SchemaStargile).
//   No MI program equivalent — DDL confirmed from SuperTool Stargile source 2026-07-28.
//
// SQL notes (IBM i 7.4):
//   - Positional parameters (?) required — named parameters not supported by ODBC driver
//   - All text fields are fixed-width → TRIM() required on every string column
//   - CONO must always appear in WHERE clause (multi-company tables)
//   - Schema is runtime-determined from cono: 100→SchemaCmp100, 300→SchemaCmp300
//   - Stargile tables use SchemaStargile (independent of CONO)
using System.Data.Odbc;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movex.API.Infrastructure.Configuration;

namespace Movex.API.Services;

public sealed class Db2QueryService
{
    private readonly Db2Settings _settings;
    private readonly ILogger<Db2QueryService> _logger;

    public Db2QueryService(IOptions<Db2Settings> settings, ILogger<Db2QueryService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;

        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
            _logger.LogWarning("Db2:ConnectionString is not configured — /api/parts/search-alias, /api/parts/next-sequence, and /api/customers/list will return 502 until it is set.");
    }

    // ── MITPOP reverse alias lookup ───────────────────────────────────────────

    /// <summary>
    /// Query MVXCDTA.MITPOP for all ITNOs that carry a given customer part number (MPPOPN).
    /// Optionally filter by partner/customer code (MPE0PA = OCUSMA.OKCUNO).
    ///
    /// Returns a list of records with trimmed ITNO, POPN, ALWT, ALWQ, E0PA.
    /// Returns an empty list when no alias is found — callers map this to "no_match".
    /// </summary>
    public async Task<IReadOnlyList<MitpopRecord>> SearchByAliasAsync(
        string cono,
        string popn,
        string? e0pa = null,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        // Build SQL — optional E0PA filter appended when provided
        var sql = e0pa is not null
            ? $"SELECT TRIM(MPITNO) AS ITNO, TRIM(MPPOPN) AS POPN, TRIM(MPALWT) AS ALWT, TRIM(MPALWQ) AS ALWQ, TRIM(MPE0PA) AS E0PA FROM {schema}.MITPOP WHERE MPCONO = ? AND TRIM(MPPOPN) = ? AND TRIM(MPE0PA) = ? ORDER BY MPITNO"
            : $"SELECT TRIM(MPITNO) AS ITNO, TRIM(MPPOPN) AS POPN, TRIM(MPALWT) AS ALWT, TRIM(MPALWQ) AS ALWQ, TRIM(MPE0PA) AS E0PA FROM {schema}.MITPOP WHERE MPCONO = ? AND TRIM(MPPOPN) = ? ORDER BY MPITNO";

        var parameters = e0pa is not null
            ? new object[] { cono, popn.Trim(), e0pa.Trim() }
            : new object[] { cono, popn.Trim() };

        _logger.LogDebug("MITPOP search: cono={Cono} popn={Popn} e0pa={E0pa}", cono, popn, e0pa ?? "(none)");

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var cmd = new CommandDefinition(
                sql,
                BuildOdbcParams(parameters),
                commandTimeout: _settings.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            var results = await conn.QueryAsync<MitpopRecord>(cmd);
            return results.ToList();
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in MITPOP search: cono={Cono} popn={Popn}", cono, popn);
            throw;
        }
    }

    // ── MITMAS next-sequence lookup ───────────────────────────────────────────

    /// <summary>
    /// Find the next available sequence number for a part number prefix.
    ///
    /// Queries MAX(TRIM(MMITNO)) FROM MITMAS WHERE MMCONO=? AND MMITNO LIKE ?||'%'.
    /// Extracts the trailing 4-digit sequence from the longest matching ITNO, increments by 1.
    /// Returns 1 when no items with this prefix exist yet.
    ///
    /// Thread-safety note: this is advisory — the engineer must confirm before creating
    /// the item in Movex. A concurrent create between suggest and confirm is unlikely but
    /// handled gracefully: the duplicate check in Oskar (S3-1) will catch it.
    /// </summary>
    public async Task<int> GetNextSequenceAsync(
        string cono,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        // MAX on a fixed-width char column: longest match with this prefix
        var sql = $"SELECT MAX(TRIM(MMITNO)) FROM {schema}.MITMAS WHERE MMCONO = ? AND TRIM(MMITNO) LIKE ?";
        var likeParam = prefix.Trim().ToUpperInvariant() + "%";

        _logger.LogDebug("MITMAS next-sequence: cono={Cono} prefix={Prefix}", cono, prefix);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var cmd = new CommandDefinition(
                sql,
                BuildOdbcParams(new object[] { cono, likeParam }),
                commandTimeout: _settings.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            var maxItno = await conn.ExecuteScalarAsync<string?>(cmd);

            if (string.IsNullOrWhiteSpace(maxItno))
                return 1;

            // PN format: LF{CUNO(2)}{commodity(2)}{seq(4)} — prefix is 8 chars, seq is last 4
            var seqPart = maxItno.Length >= prefix.Length + 4
                ? maxItno[^4..]
                : maxItno[prefix.Length..];

            return int.TryParse(seqPart, out var seq) ? seq + 1 : 1;
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in MITMAS next-sequence: cono={Cono} prefix={Prefix}", cono, prefix);
            throw;
        }
    }

    // ── OCUSMA active customer list ───────────────────────────────────────────

    /// <summary>
    /// List active customers (OKSTAT = '20') for a company, ordered by customer number.
    /// Used by Oskar to populate the ECN customer dropdown — OKCUNO is the 2-char code
    /// embedded in Scanfil APAC part numbers (LF + CUNO + commodity + seq).
    /// </summary>
    public async Task<IReadOnlyList<CustomerRecord>> ListCustomersAsync(
        string cono,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        var sql = $"SELECT TRIM(OKCUNO) AS CUNO, TRIM(OKCUNM) AS NAME FROM {schema}.OCUSMA WHERE OKCONO = ? AND OKSTAT = ? ORDER BY TRIM(OKCUNO)";

        _logger.LogDebug("Customer list: cono={Cono}", cono);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var cmd = new CommandDefinition(
                sql,
                BuildOdbcParams(new object[] { cono, "20" }),
                commandTimeout: _settings.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            var results = await conn.QueryAsync<CustomerRecord>(cmd);
            return results.ToList();
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in customer list: cono={Cono}", cono);
            throw;
        }
    }

    // ── BOM — single-level (B-1) ──────────────────────────────────────────────

    /// <summary>
    /// Single-level BOM: MPDHED head + MPDMAT lines joined MITMAS.
    /// App-side effectivity filter applied — FDAT cursor-seek gotcha means we must never
    /// filter in the DB2 query (passing FDAT to M3 MI repositions the index cursor, not a filter).
    /// Returns null head when no MPDHED record exists for (cono, faci, itno, strt) → caller returns 404.
    /// </summary>
    public async Task<(BomHeadRecord? Head, IReadOnlyList<BomLineRecord> Lines)> GetSingleLevelBomAsync(
        string cono,
        string faci,
        string itno,
        string strt,
        int? effectiveOn,
        bool includeExpired,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        var headSql = $@"SELECT TRIM(PHPRNO) AS PRNO, TRIM(PHSTRT) AS STRT, TRIM(PHFACI) AS FACI,
       TRIM(MMITDS) AS ITDS
FROM {schema}.MPDHED
JOIN {schema}.MITMAS ON MMCONO = PHCONO AND TRIM(MMITNO) = TRIM(PHPRNO)
WHERE PHCONO = ? AND TRIM(PHFACI) = ? AND TRIM(PHPRNO) = ? AND TRIM(PHSTRT) = ?
FETCH FIRST 1 ROW ONLY";

        var linesSql = $@"SELECT PMMSEQ AS MSEQ, TRIM(PMMTNO) AS MTNO, TRIM(MMITDS) AS ITDS,
       PMOPNO AS OPNO, PMCNQT AS CNQT, TRIM(PMPEUN) AS PEUN,
       PMFDAT AS FDAT, PMTDAT AS TDAT,
       TRIM(MMITTY) AS ITTY, TRIM(MMSTAT) AS STAT, TRIM(PMWHLO) AS WHLO
FROM {schema}.MPDMAT
JOIN {schema}.MITMAS ON MMCONO = PMCONO AND TRIM(MMITNO) = TRIM(PMMTNO)
WHERE PMCONO = ? AND TRIM(PMFACI) = ? AND TRIM(PMPRNO) = ? AND TRIM(PMSTRT) = ?
ORDER BY PMMSEQ";

        _logger.LogDebug("BOM single-level: cono={Cono} faci={Faci} itno={Itno} strt={Strt}", cono, faci, itno, strt);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var p = BuildOdbcParams(new object[] { cono, faci.Trim(), itno.Trim(), strt.Trim() });

            var head = await conn.QueryFirstOrDefaultAsync<BomHeadRecord>(
                new CommandDefinition(headSql, p, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));

            if (head is null)
                return (null, []);

            var lines = (await conn.QueryAsync<BomLineRecord>(
                new CommandDefinition(linesSql, p, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken))).ToList();

            // App-side effectivity filter (never filter in SQL — FDAT is a cursor-seek key in M3)
            lines = ApplyEffectivityFilter(lines, effectiveOn, includeExpired);

            return (head, lines);
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in BOM single-level: cono={Cono} itno={Itno}", cono, itno);
            throw;
        }
    }

    // ── BOM — multi-level indented (B-2) ─────────────────────────────────────

    /// <summary>
    /// Multi-level BOM explosion via recursive CTE over MPDMAT.
    /// Depth-capped (default 12) and cycle-guarded via ANCESTORS path string.
    /// PDZ100MI is broken in M3 — this CTE is the only reliable approach.
    /// Result is flat, depth-first; Oskar assembles the tree client-side.
    /// </summary>
    public async Task<IReadOnlyList<BomIndentedRecord>> GetIndentedBomAsync(
        string cono,
        string faci,
        string itno,
        string strt,
        int maxDepth,
        int effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        // Recursive CTE: anchor selects direct children, recursive step walks assemblies.
        // ANCESTORS path string guards against cycles (LOCATE check).
        // effectiveOn filters both anchor and recursive step for active lines only.
        // 9 params: cono, faci, itno, strt, effectiveOn (anchor), cono (recursive join),
        //           maxDepth, effectiveOn (recursive), cono (MITMAS join)
        var sql = $@"WITH BOM_CTE (LEVL, PRNO, STRT, FACI, MSEQ, MTNO, OPNO, CNQT, PEUN, FDAT, TDAT,
              WHLO, ANCESTORS) AS (
  SELECT 1, TRIM(PMPRNO), TRIM(PMSTRT), TRIM(PMFACI),
         PMMSEQ, TRIM(PMMTNO), PMOPNO, PMCNQT, TRIM(PMPEUN), PMFDAT, PMTDAT,
         TRIM(PMWHLO),
         CAST(TRIM(PMPRNO) AS VARCHAR(900))
  FROM {schema}.MPDMAT
  WHERE PMCONO = ? AND TRIM(PMFACI) = ? AND TRIM(PMPRNO) = ? AND TRIM(PMSTRT) = ?
    AND (PMTDAT = 0 OR PMTDAT = 99999999 OR PMTDAT >= ?)

  UNION ALL

  SELECT p.LEVL + 1, TRIM(c.PMPRNO), TRIM(c.PMSTRT), TRIM(c.PMFACI),
         c.PMMSEQ, TRIM(c.PMMTNO), c.PMOPNO, c.PMCNQT, TRIM(c.PMPEUN),
         c.PMFDAT, c.PMTDAT,
         TRIM(c.PMWHLO),
         p.ANCESTORS CONCAT '|' CONCAT TRIM(c.PMPRNO)
  FROM {schema}.MPDMAT c
  JOIN BOM_CTE p ON TRIM(c.PMPRNO) = p.MTNO AND TRIM(c.PMFACI) = p.FACI
                 AND TRIM(c.PMSTRT) = p.STRT
  WHERE c.PMCONO = ? AND p.LEVL < ?
    AND (c.PMTDAT = 0 OR c.PMTDAT = 99999999 OR c.PMTDAT >= ?)
    AND LOCATE(TRIM(c.PMMTNO), p.ANCESTORS) = 0
)
SELECT b.LEVL, b.PRNO AS PRNO, b.MSEQ, b.MTNO, TRIM(m.MMITDS) AS ITDS,
       b.OPNO, b.CNQT, b.PEUN, b.FDAT, b.TDAT, b.STRT, b.WHLO,
       TRIM(m.MMITTY) AS ITTY
FROM BOM_CTE b
JOIN {schema}.MITMAS m ON m.MMCONO = ? AND TRIM(m.MMITNO) = b.MTNO
ORDER BY b.LEVL, b.PRNO, b.MSEQ";

        var p = BuildOdbcParams(new object[] { cono, faci.Trim(), itno.Trim(), strt.Trim(), effectiveOn, cono, maxDepth, effectiveOn, cono });

        _logger.LogDebug("BOM indented: cono={Cono} faci={Faci} itno={Itno} strt={Strt} maxDepth={MaxDepth} effectiveOn={EffectiveOn}",
            cono, faci, itno, strt, maxDepth, effectiveOn);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var results = await conn.QueryAsync<BomIndentedRecord>(
                new CommandDefinition(sql, p, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));
            return results.ToList();
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in BOM indented: cono={Cono} itno={Itno}", cono, itno);
            throw;
        }
    }

    // ── BOM — where-used (B-3) ────────────────────────────────────────────────

    /// <summary>
    /// Where-used: reverse MPDMAT lookup on PMMTNO — find all parent assemblies that contain this component.
    /// </summary>
    public async Task<IReadOnlyList<WhereUsedRecord>> GetWhereUsedAsync(
        string cono,
        string? faci,
        string mtno,
        int? effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var schema = ResolveSchema(cono);

        var hasFaci = !string.IsNullOrWhiteSpace(faci);
        var eon = effectiveOn ?? int.Parse(DateTime.Today.ToString("yyyyMMdd"));

        var sql = hasFaci
            ? $@"SELECT TRIM(PMPRNO) AS PRNO, TRIM(PMSTRT) AS STRT, TRIM(PMFACI) AS FACI,
       PMMSEQ AS MSEQ, TRIM(PMMTNO) AS MTNO, PMOPNO AS OPNO,
       PMCNQT AS CNQT, TRIM(PMPEUN) AS PEUN, PMFDAT AS FDAT, PMTDAT AS TDAT
FROM {schema}.MPDMAT
WHERE PMCONO = ? AND TRIM(PMMTNO) = ? AND TRIM(PMFACI) = ?
  AND (PMTDAT = 0 OR PMTDAT = 99999999 OR PMTDAT >= ?)
ORDER BY TRIM(PMPRNO), PMMSEQ"
            : $@"SELECT TRIM(PMPRNO) AS PRNO, TRIM(PMSTRT) AS STRT, TRIM(PMFACI) AS FACI,
       PMMSEQ AS MSEQ, TRIM(PMMTNO) AS MTNO, PMOPNO AS OPNO,
       PMCNQT AS CNQT, TRIM(PMPEUN) AS PEUN, PMFDAT AS FDAT, PMTDAT AS TDAT
FROM {schema}.MPDMAT
WHERE PMCONO = ? AND TRIM(PMMTNO) = ?
  AND (PMTDAT = 0 OR PMTDAT = 99999999 OR PMTDAT >= ?)
ORDER BY TRIM(PMPRNO), PMMSEQ";

        var parameters = hasFaci
            ? new object[] { cono, mtno.Trim(), faci!.Trim(), eon }
            : new object[] { cono, mtno.Trim(), eon };

        _logger.LogDebug("Where-used: cono={Cono} mtno={Mtno} faci={Faci}", cono, mtno, faci ?? "(all)");

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var results = await conn.QueryAsync<WhereUsedRecord>(
                new CommandDefinition(sql, BuildOdbcParams(parameters), commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));
            return results.ToList();
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in where-used: cono={Cono} mtno={Mtno}", cono, mtno);
            throw;
        }
    }

    // ── Circuit references — ZECNCIRF (C-1) ──────────────────────────────────

    /// <summary>
    /// Circuit references from Stargile's ZECNCIRF table (schema MOVEX, independent of CONO).
    /// CRZCIRRF is a CLOB with comma-delimited ref designators; range notation (R1-R5) is expanded.
    /// Migration/backfill only — retired after Stargile decommission.
    /// </summary>
    public async Task<IReadOnlyList<CircuitRefRecord>> GetCircuitRefsAsync(
        string cono,
        string faci,
        string prno,
        string strt,
        CancellationToken cancellationToken = default)
    {
        var sql = $@"SELECT TRIM(CRFACI) AS FACI, TRIM(CRPRNO) AS PRNO, TRIM(CRSTRT) AS STRT,
       CRMSEQ AS MSEQ, CRFDAT AS FDAT, CAST(CRZCIRRF AS VARCHAR(4096)) AS CIRF_RAW
FROM {_settings.SchemaStargile}.ZECNCIRF
WHERE CRCONO = ? AND TRIM(CRFACI) = ? AND TRIM(CRPRNO) = ? AND TRIM(CRSTRT) = ?
ORDER BY CRMSEQ, CRFDAT";

        _logger.LogDebug("Circuit refs: cono={Cono} faci={Faci} prno={Prno} strt={Strt}", cono, faci, prno, strt);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);
            var raw = await conn.QueryAsync<CircuitRefRaw>(
                new CommandDefinition(sql, BuildOdbcParams(new object[] { cono, faci.Trim(), prno.Trim(), strt.Trim() }),
                    commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));

            return raw.Select(r => new CircuitRefRecord(
                r.FACI, r.PRNO, r.STRT, r.MSEQ, r.FDAT,
                ExpandCircuitRefs(r.CIRF_RAW))).ToList();
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in circuit refs: cono={Cono} prno={Prno}", cono, prno);
            throw;
        }
    }

    // ── ZECNMPMS export — paged (M-1) ────────────────────────────────────────

    /// <summary>
    /// Paged export of Stargile manufacturer part master (ZECNMPMS, schema MOVEX).
    /// Raw/untransformed — all normalisation is the migration script's job.
    /// </summary>
    public async Task<(IReadOnlyList<ZecnmpmsRecord> Records, int Total)> ExportZecnmpmsAsync(
        string cono,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var countSql = $"SELECT COUNT(*) FROM {_settings.SchemaStargile}.ZECNMPMS WHERE MPCONO = ?";

        // Column names are the real DB2 names (source-verified 2026-07-27 from MPMDetail.cml / MPMBrowse.cml).
        // MPDIST/MPDISTNM do not exist on this table — they were removed after source verification.
        // ITNO/SUNO are trimmed aliases for MPITNO/MPSUNO (matching the fixture CSV header).
        var dataSql = $@"SELECT TRIM(MPITNO) AS ITNO, TRIM(MPSUNO) AS SUNO, TRIM(MPZMANPN) AS MPZMANPN,
       TRIM(MPTX30) AS MPTX30, MPZDEFFL, MPZEEFDT, TRIM(MPZECNID) AS MPZECNID,
       MPFDAT, MPTDAT, MPMPRC, TRIM(MPCUCD) AS MPCUCD, MPZMPMOQ, MPZMPSPQ,
       MPZCAWID, MPZREWID, MPZMNCNR, TRIM(MPFACI) AS MPFACI, MPLMDT, MPLMTM
FROM {_settings.SchemaStargile}.ZECNMPMS
WHERE MPCONO = ?
ORDER BY TRIM(MPITNO), TRIM(MPSUNO), TRIM(MPZMANPN)
OFFSET ? ROWS FETCH NEXT ? ROWS ONLY";

        _logger.LogDebug("ZECNMPMS export: cono={Cono} offset={Offset} limit={Limit}", cono, offset, limit);

        try
        {
            using var conn = new OdbcConnection(_settings.ConnectionString);

            var total = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(countSql, BuildOdbcParams(new object[] { cono }),
                    commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));

            var records = (await conn.QueryAsync<ZecnmpmsRecord>(
                new CommandDefinition(dataSql, BuildOdbcParams(new object[] { cono, offset, limit }),
                    commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken))).ToList();

            return (records, total);
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex, "DB2 error in ZECNMPMS export: cono={Cono}", cono);
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveSchema(string cono) => cono.Trim() switch
    {
        "100" => _settings.SchemaCmp100,
        "300" => _settings.SchemaCmp300,
        _     => _settings.SchemaCmp300,   // default to dev schema for unknown CONO
    };

    private static List<BomLineRecord> ApplyEffectivityFilter(
        List<BomLineRecord> lines, int? effectiveOn, bool includeExpired)
    {
        if (includeExpired)
            return lines;

        if (effectiveOn.HasValue)
        {
            var eon = effectiveOn.Value;
            return lines.Where(l => l.FDAT <= eon && (l.TDAT == 0 || l.TDAT >= eon)).ToList();
        }

        // Default: active-only (open-ended or no expiry)
        return lines.Where(l => l.TDAT == 0 || l.TDAT == 99999999).ToList();
    }

    // Expands a comma-delimited CLOB string of ref designators; supports range notation (R1-R5).
    private static List<string> ExpandCircuitRefs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var result = new List<string>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dashIdx = token.LastIndexOf('-');
            if (dashIdx > 0)
            {
                var prefix = new string(token.Take(dashIdx).TakeWhile(char.IsLetter).ToArray());
                var fromStr = token[prefix.Length..dashIdx];
                var toStr   = token[(dashIdx + 1)..];

                if (!string.IsNullOrEmpty(prefix)
                    && int.TryParse(fromStr, out var from)
                    && int.TryParse(toStr,   out var to)
                    && from <= to)
                {
                    for (var i = from; i <= to; i++)
                        result.Add(prefix + i);
                    continue;
                }
            }
            result.Add(token);
        }
        return result;
    }

    // Dapper with ODBC requires parameters as an anonymous object with positional names p0..pN
    private static object BuildOdbcParams(object[] values)
    {
        return values.Length switch
        {
            1 => new { p0 = values[0] },
            2 => new { p0 = values[0], p1 = values[1] },
            3 => new { p0 = values[0], p1 = values[1], p2 = values[2] },
            4 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3] },
            5 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3], p4 = values[4] },
            6 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3], p4 = values[4], p5 = values[5] },
            7 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3], p4 = values[4], p5 = values[5], p6 = values[6] },
            8 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3], p4 = values[4], p5 = values[5], p6 = values[6], p7 = values[7] },
            9 => new { p0 = values[0], p1 = values[1], p2 = values[2], p3 = values[3], p4 = values[4], p5 = values[5], p6 = values[6], p7 = values[7], p8 = values[8] },
            _ => throw new ArgumentOutOfRangeException(nameof(values), "Max 9 positional parameters supported")
        };
    }
}

/// <summary>Record returned by MITPOP alias search.</summary>
public sealed record MitpopRecord(
    string ITNO,
    string POPN,
    string? ALWT,
    string? ALWQ,
    string? E0PA);

/// <summary>Record returned by OCUSMA active customer list.</summary>
public sealed record CustomerRecord(
    string CUNO,
    string? NAME);

/// <summary>MPDHED header record for a product structure.</summary>
public sealed record BomHeadRecord(
    string PRNO,
    string STRT,
    string FACI,
    string? ITDS);

/// <summary>MPDMAT component line with MITMAS item description/type/status.</summary>
public sealed record BomLineRecord(
    int    MSEQ,
    string MTNO,
    string? ITDS,
    int    OPNO,
    decimal CNQT,
    string? PEUN,
    long   FDAT,
    long   TDAT,
    string? ITTY,
    string? STAT,
    string? WHLO);

/// <summary>Flat row from the recursive BOM explosion CTE (B-2).</summary>
public sealed record BomIndentedRecord(
    int    LEVL,
    string PRNO,
    int    MSEQ,
    string MTNO,
    string? ITDS,
    int    OPNO,
    decimal CNQT,
    string? PEUN,
    long   FDAT,
    long   TDAT,
    string? STRT,
    string? WHLO,
    string? ITTY);

/// <summary>MPDMAT where-used row (reverse lookup on PMMTNO).</summary>
public sealed record WhereUsedRecord(
    string PRNO,
    string? STRT,
    string? FACI,
    int    MSEQ,
    string MTNO,
    int    OPNO,
    decimal CNQT,
    string? PEUN,
    long   FDAT,
    long   TDAT);

/// <summary>Internal Dapper intermediate for ZECNCIRF rows before ref expansion.</summary>
internal sealed record CircuitRefRaw(
    string FACI,
    string PRNO,
    string STRT,
    int    MSEQ,
    long   FDAT,
    string? CIRF_RAW);

/// <summary>Circuit reference row with expanded CIRF list (C-1).</summary>
public sealed record CircuitRefRecord(
    string FACI,
    string PRNO,
    string STRT,
    int    MSEQ,
    long   FDAT,
    IReadOnlyList<string> CIRF);

/// <summary>
/// Raw ZECNMPMS row for manufacturer part master export (M-1).
/// Column names source-verified 2026-07-27 from MPMDetail.cml / MPMBrowse.cml.
/// MPDIST/MPDISTNM do not exist on this table — removed after source verification.
/// </summary>
public sealed record ZecnmpmsRecord(
    string   ITNO,
    string?  SUNO,
    string?  MPZMANPN,
    string?  MPTX30,
    int?     MPZDEFFL,
    long?    MPZEEFDT,
    string?  MPZECNID,
    long?    MPFDAT,
    long?    MPTDAT,
    decimal? MPMPRC,
    string?  MPCUCD,
    decimal? MPZMPMOQ,
    decimal? MPZMPSPQ,
    int?     MPZCAWID,
    int?     MPZREWID,
    int?     MPZMNCNR,
    string?  MPFACI,
    long?    MPLMDT,
    int?     MPLMTM);
