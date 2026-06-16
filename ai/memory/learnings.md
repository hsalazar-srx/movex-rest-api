# Agent Learnings — MOVEX REST API

Accumulated lessons from working in this project.
Read this file at the start of each session. Newest-first.

---

## 2026-06-16 — Customer list endpoint + route rename to resource-oriented paths

**Context:** Oskar's ECN header needs a customer selector backed by real Movex customer data —
no MI program lists customers, so this follows the same custom-DB2-query pattern as the existing
MITPOP/MITMAS endpoints.

**What was added:**
- `GET /api/customers/list?cono=` — queries `OCUSMA` directly, filtered to active customers,
  returns `CUNO` + `NAME`. New `Db2QueryService.ListCustomersAsync()` + `CustomerRecord` type.

**Route rename (breaking change, coordinated with Oskar):**
- `/api/mitpop/search` → `/api/parts/search-alias`
- `/api/mitmas/next-sequence` → `/api/parts/next-sequence`

Renamed for consistency — the original routes were named after the raw M3 table (`MITPOP`,
`MITMAS`), leaking an implementation detail to API consumers. All three DB2 direct-query
endpoints now use resource-oriented names. Oskar's `movex.py` adapter, tests, and docs were
updated in the same change to avoid a window where the two sides are out of sync.

---

## 2026-06-05 — DB2 direct-query endpoints (S3-1, S3-2 for Oskar)

**Context:** Oskar needs two part number intelligence endpoints that have no M3 MI equivalent:
- Reverse alias lookup (`POPN → ITNO`): `MMS025MI.LstAlias` is ITNO-first only — confirmed 2026-05-11.
- Next available part number sequence: no MI program exposes `MAX(ITNO)` by prefix.

**What was added:**
- `GET /api/mitpop/search?cono=&popn=[&e0pa=]` — queries `MVXCDTA.MITPOP` directly
- `GET /api/mitmas/next-sequence?cono=&prefix=` — queries `MAX(MMITNO)` from `MVXCDTA.MITMAS`
- New files: `Infrastructure/Configuration/Db2Settings.cs`, `Services/Db2QueryService.cs`, `Controllers/Db2QueryController.cs`
- New packages: `Dapper 2.1.35`, `System.Data.Odbc 8.0.0`

**Key decisions:**
- `System.Data.Odbc` is NOT in-box on .NET 8 — it must be added as a NuGet package explicitly.
  On .NET Framework it was built-in; this has confused developers before.
- Dapper with ODBC: positional `?` parameters only. Named parameters (`@name`) are not supported
  by the IBM i ODBC driver. Build param objects with `p0`, `p1`, `p2` property names.
- IBM i text fields are fixed-width → always `TRIM()` in SQL. Queries that omit TRIM will match
  padded values only, causing silent no-match results.
- CONO must always be in the WHERE clause — MITPOP and MITMAS are multi-company tables.
- Schema is resolved at runtime: CONO 100 → `SchemaCmp100` (default `MVXCDTA`), CONO 300 → `SchemaCmp300` (default `MVXC300`).

**Deployment prerequisite:** IBM i Access ODBC Driver must be installed on SRXWEBAPP1.
The driver is separate from the .NET packages — it is a Windows system driver.
If `Db2:ConnectionString` is empty or the driver is absent, these two endpoints return 502.
All M3 MI endpoints continue to work — they use the separate `mvxsock.dll` path.

**Branch:** `feat-db2-direct-query-endpoints` — merged to `develop` after review.

---
