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
// SQL notes (IBM i 7.4):
//   - Positional parameters (?) required — named parameters not supported by ODBC driver
//   - All text fields are fixed-width → TRIM() required on every string column
//   - CONO must always appear in WHERE clause (multi-company tables)
//   - Schema is runtime-determined from cono: 100→SchemaCmp100, 300→SchemaCmp300
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveSchema(string cono) => cono.Trim() switch
    {
        "100" => _settings.SchemaCmp100,
        "300" => _settings.SchemaCmp300,
        _     => _settings.SchemaCmp300,   // default to dev schema for unknown CONO
    };

    // Dapper with ODBC requires parameters as an anonymous object with positional names p0..pN
    private static object BuildOdbcParams(object[] values)
    {
        return values.Length switch
        {
            1 => new { p0 = values[0] },
            2 => new { p0 = values[0], p1 = values[1] },
            3 => new { p0 = values[0], p1 = values[1], p2 = values[2] },
            _ => throw new ArgumentOutOfRangeException(nameof(values), "Max 3 positional parameters supported")
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
