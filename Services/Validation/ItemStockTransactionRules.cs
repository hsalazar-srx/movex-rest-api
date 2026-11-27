// Services/Validation/ItemStockTransactionRules.cs
// Input rules for GET /api/stock-transactions.
//
// Every value that reaches the DB2 query is checked here first and then bound as a ? parameter
// (never concatenated). The only text placed into the SQL itself is the schema name (from config)
// and the row limit (an int clamped to 1-500).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Movex.API.Services.Validation;

public sealed record ItemStockTransactionQuery(
    string Cono,
    string Itno,
    decimal FromDate, // YYYYMMDD, compared with MITTRA.MTTRDT
    decimal? ToDate,
    string? Puno,     // upper-case, right-justified to 7 characters, as MITTRA.MTRIDN stores it
    int? Ttyp,        // stock transaction type; null = all movements
    int Limit);

public static class ItemStockTransactionRules
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    // MOVEX PO numbers are up to 7 characters; a few carry a letter prefix (D11345, ECO).
    private static readonly Regex PunoPattern = new(@"^[A-Za-z0-9]{1,7}$", RegexOptions.Compiled);
    private static readonly Regex ItnoPattern = new(@"^[A-Za-z0-9._\-/]{1,15}$", RegexOptions.Compiled);

    /// <summary>Returns the query to run, or an error message for a 400 response.</summary>
    public static (ItemStockTransactionQuery? Query, string? Error) Validate(
        string? cono, string? itno, string? fromDate, string? toDate,
        string? puno, int? ttyp, int? limit)
    {
        // No default company: Db2QueryService.ResolveSchema falls back to company 300 for any
        // unknown value, which would silently answer from the wrong company.
        var conoResolved = cono?.Trim();
        if (conoResolved is not ("100" or "300"))
            return (null, "cono is required and must be 100 or 300");

        if (string.IsNullOrWhiteSpace(itno) || !ItnoPattern.IsMatch(itno.Trim()))
            return (null, "itno is required: 1-15 characters: letters, digits, . _ - /");

        // A start date is required so a busy item cannot pull its whole history.
        if (string.IsNullOrWhiteSpace(fromDate))
            return (null, "from is required, in YYYYMMDD form, e.g. 20261101");
        if (!TryDate(fromDate, out var from, out var fromError)) return (null, fromError);
        if (!TryDate(toDate, out var to, out var toError)) return (null, toError);
        if (to.HasValue && from > to)
            return (null, "from must not be after to");

        string? punoResolved = null;
        if (!string.IsNullOrWhiteSpace(puno))
        {
            if (!PunoPattern.IsMatch(puno.Trim()))
                return (null, "puno must be 1-7 letters or digits");
            punoResolved = puno.Trim().ToUpperInvariant().PadLeft(7);
        }

        if (ttyp is < 1 or > 99)
            return (null, "ttyp must be between 1 and 99");

        var limitResolved = limit ?? DefaultLimit;
        if (limitResolved < 1 || limitResolved > MaxLimit)
            return (null, $"limit must be between 1 and {MaxLimit}");

        return (new ItemStockTransactionQuery(
            conoResolved,
            itno.Trim().ToUpperInvariant(),
            from!.Value, to, punoResolved, ttyp, limitResolved), null);
    }

    private static bool TryDate(string? value, out decimal? date, out string? error)
    {
        date = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var v = value.Trim();
        if (v.Length != 8 || !DateTime.TryParseExact(
                v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            error = "dates must be real dates in YYYYMMDD form, e.g. 20261124";
            return false;
        }

        date = decimal.Parse(v, CultureInfo.InvariantCulture);
        return true;
    }
}
