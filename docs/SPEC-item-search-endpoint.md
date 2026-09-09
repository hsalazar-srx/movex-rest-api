# Spec — `GET /api/items/search` (MITMAS item search)

**Status:** Implemented 2026-09-09 — `GET /api/items/search`
**Not yet verified against live CONO=300** (needs a service restart to load the new binary)
**Raised by:** Oskar (ECN platform), 2026-09-03
**Consumer:** Oskar `GET /api/v1/items/search`, backing the ECN item browser and BOM
component picker
**Type:** New DB2 direct-query endpoint (no MI transaction exists for this)

---

## Why this is needed

Oskar's ECN authoring UI has no way to find an item. Every item number field is free text:
the author must already know the exact number, or go and look it up elsewhere. This blocks
two user-requested features — an item browser that imports existing data, and a component
picker for BOM changes.

**There is currently no endpoint that can serve an item search.** Verified 2026-09-03:

| Candidate | Status |
|---|---|
| `GET /api/items?q=` | **Does not exist.** Oskar's `MovexRestAdapter.search_items` calls it; it would 404. |
| `MMS200MI.LstItmByNo` | **Not configured** — `transactions/MMS200MI.json` has GetItmBasic, GetItmFac, LstItmFac, GetItmWhsBal, and the Add/Upd writes. No by-number list. |
| `MMS200MI.GetItmBasic` | Exact item number only — no partial match, no search. |
| `GET /api/parts/search-alias` | Reverse alias lookup (customer P/N → ITNO). Different question. |

So this needs a new DB2 direct query over `MITMAS`, in the same manner as `search-alias`
and `next-sequence` — both of which exist precisely because M3 offers no MI equivalent.

---

## Endpoint

```
GET /api/items/search?cono={cono}&q={query}&limit={limit}&includeInactive={bool}
```

### Query parameters

| Param | Type | Required | Default | Notes |
|---|---|---|---|---|
| `cono` | string | no | `M3:DEFAULT_COMPANY` | Same resolution as every other Db2Query route |
| `q` | string | **yes** | — | Matched against item number **and** description |
| `limit` | int | no | `25` | Clamp to `1..100` server-side |
| `includeInactive` | bool | no | `false` | When false, excludes status 90 (see below) |

`q` shorter than 2 characters returns `400` — an unbounded prefix scan of MITMAS is
expensive and the result would be useless anyway.

### Response — 200

```json
{
  "success": true,
  "cono": "300",
  "query": "LFAM05",
  "data": {
    "records": [
      {
        "ITNO": "LFAM050001",
        "ITDS": "Widget Assembly A",
        "ITTY": "3",
        "STAT": "20",
        "UNMS": "EA",
        "ITCL": "022"
      }
    ],
    "count": 1,
    "truncated": false
  }
}
```

`truncated` is `true` when the result set hit `limit` — the UI needs to tell the user to
narrow the search rather than silently showing a partial list as if it were complete.

### Errors

Match `SearchMitpop` exactly (`Db2QueryController.cs:95-114`):

| Condition | Status | Body |
|---|---|---|
| `q` missing or < 2 chars | 400 | `{ success: false, error: "q must be at least 2 characters" }` |
| `Db2:ConnectionString` unset | 502 | existing "DB2 connection string is not configured" message |
| `OdbcException` | 502 | existing "DB2 query failed" message |

---

## Implementation

### `Db2QueryService.SearchItemsAsync`

Follows `SearchByAliasAsync` (`Db2QueryService.cs:60-96`) — same connection handling, same
positional `?` parameters via `BuildOdbcParams`, same `OdbcException` log-and-rethrow.

```csharp
public async Task<IReadOnlyList<ItemSearchRecord>> SearchItemsAsync(
    string cono,
    string query,
    int limit = 25,
    bool includeInactive = false,
    CancellationToken cancellationToken = default)
{
    var schema = ResolveSchema(cono);
    ...
}
```

**SQL:**

```sql
SELECT TRIM(MMITNO) AS ITNO,
       TRIM(MMITDS) AS ITDS,
       TRIM(MMITTY) AS ITTY,
       TRIM(MMSTAT) AS STAT,
       TRIM(MMUNMS) AS UNMS,
       TRIM(MMITCL) AS ITCL
FROM   {schema}.MITMAS
WHERE  MMCONO = ?
  AND (UPPER(TRIM(MMITNO)) LIKE ? OR UPPER(TRIM(MMITDS)) LIKE ?)
  [AND MMSTAT <> '90']            -- omitted when includeInactive
ORDER BY
  CASE WHEN UPPER(TRIM(MMITNO)) LIKE ? THEN 0 ELSE 1 END,   -- prefix matches first
  TRIM(MMITNO)
FETCH FIRST ? ROWS ONLY
```

Parameters, in order: `cono`, `%Q%`, `%Q%`, `Q%`, `limit + 1` — where `Q` is
`query.Trim().ToUpperInvariant()`.

**Fetch `limit + 1`, return `limit`.** That is how `truncated` is determined without a
second `COUNT(*)` over the same predicate.

### Notes that matter

**Schema is per-company.** Use `ResolveSchema(cono)` — never a hardcoded schema. CONO 100 is
`MVXCDTA`, CONO 300 is `MVXC300`. This cost real debugging time on the Oskar side in
September 2026 before it was understood; `ResolveSchema` already encodes it correctly
(`Db2QueryService.cs:488-493`).

**`MMCONO` is numeric — pass `cono` as the existing routes do.** Every current Db2Query
method passes the string straight through to a `?` parameter and it works; do the same
rather than inventing a cast.

**Fixed-width columns need `TRIM()`.** MITMAS char columns are space-padded; without TRIM
both the `LIKE` and the returned values carry trailing blanks.

**Case-insensitive match.** `UPPER()` on both sides. Item numbers here are uppercase by
convention, but descriptions are free text and users will type lowercase.

**Prefix matches rank above substring matches.** Someone typing `LFAM05` wants items whose
number starts with it, not every item with that fragment buried in a description. Without
the ORDER BY CASE the intended match can be pages down.

**Status 90.** Excluded by default as the obsolete marker. **Confirm this against real data
before merging** — `Db2QueryService.cs:217` selects `MMSTAT` but nothing in this repo
filters on it, so 90 is inferred from M3 convention, not verified here. If the value differs,
fix the constant, not the shape of the query. `includeInactive=true` bypasses the filter
entirely, which matters for ECNs that revise superseded parts.

### Controller

New action on `Db2QueryController`, modelled on `SearchMitpop` (`:57-115`). Add
`ItemSearchResponse` / `ItemSearchData` / `ItemSearchRecordDto` alongside the existing
response DTOs, and an `ItemSearchRecord` positional record beside `MitpopRecord`
(`Db2QueryService.cs:570`).

---

## Performance

`MITMAS` is large and `LIKE '%…%'` cannot use an index — this is a table scan per query.
Mitigations, in order of importance:

1. **Minimum 2 characters** (enforced, above).
2. **`FETCH FIRST`** caps the rows returned, though not the scan.
3. **Debounce client-side** — Oskar must not fire per keystroke. 300ms is the intended
   behaviour on the consumer side.
4. `CommandTimeoutSeconds` already applies via `_settings`.

If this proves slow against production volumes, the fallback is to restrict matching to
`MMITNO` prefix only (`LIKE 'Q%'`, index-usable) and drop description search. **Measure
before optimising** — `next-sequence` already does a `LIKE` scan on this table and is
acceptable in practice.

---

## Testing

- `q` of 1 char → 400.
- `q` matching nothing → 200, empty `records`, `count: 0`.
- More matches than `limit` → exactly `limit` records, `truncated: true`.
- Prefix match sorts above a description-only match for the same `q`.
- `cono=100` and `cono=300` hit `MVXCDTA` and `MVXC300` respectively.
- Lowercase `q` matches an uppercase item number.
- `includeInactive=false` excludes an obsolete item; `true` includes it.

**Verify against CONO=300 only.** CONO 100 is production (PRE-12).

---

## Consumer contract (Oskar side)

Once this lands, Oskar will:

1. **Rewrite** `MovexRestAdapter.search_items` (`src/adapters/erp/movex.py:470`). It
   currently calls `GET /items?q=` with no CONO and no `data.records` unwrapping — dead
   speculative code that has never run. It needs the same shape as `lookup_by_alias`.
2. Add `GET /api/v1/items/search` in `src/routers/items.py`, mapping 400 → 422 and 502 → 502,
   per `routers/parts.py:407-440`.
3. Build the item browser dialog and the BOM component picker on top of it.

Tracked in Oskar as the open half of **I2-15**.
