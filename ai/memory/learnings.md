# Agent Learnings — MOVEX REST API

Accumulated lessons from working in this project.
Read this file at the start of each session. Newest-first.

---

## 2026-09-02 — PDS002MI GetComponent/LstComponent response offsets; config hot-reload reads bin/

**Context:** Oskar reported `GetComponent` returning `FDAT=200` / `TDAT=1205` for a real
`20240119`/`99999999`, and `MTNO=None`.

**Root cause:** the `responseFields` offsets were derived from RPG source and never verified against
a live response — the same failure class as the `UpdateComponent` offsets corrected in `d6ec625`.
`ECVE` was at 92 (should be 89) and sliced into the item number; everything after it shifted, so
`MTPL` read `M050002     202` and `FDAT` read `40119999`.

**Fix:** rebuilt both `GetComponent` and `LstComponent` `responseFields` (51 -> 58) from the
PDS002MI2 output layout supplied by hsalazar, verified field-by-field against a live raw response
for `LFAM050001` / FACI `D` / STRT `001` (CONO 300).

**Two things worth carrying forward:**

1. **The API loads transaction JSON from `bin/Debug/net8.0/win-x64/transactions/`, not the source
   `transactions/` folder.** Editing the source file does nothing to a running API — hot-reload
   watches the bin copy. Either rebuild, or `cp transactions/X.json bin/Debug/net8.0/win-x64/transactions/`.
   Confirm with `GET /api/{program}/{transaction}/info` and check the field count before concluding
   a config change did not work. A stale bin copy here looked exactly like "the fix had no effect".
2. **`MTPL` is the component item number.** RPG `PDS002MI` moves `DCMTNO` into `Y7MTPL`
   (analysis/PDS002MI.txt), so consumers looking for `MTNO` will find it under `MTPL`. Not renamed —
   that would break existing consumers — but worth knowing.

**MSEQ is capped at 4 digits (0-9999) by design, not a bug.** `Z-ADD4 PXFLDD` precedes every MSEQ
conversion in all five transactions; `MPDMAT.PMMSEQ` / `MPDOPE.POMSEQ` are both `Numeric 4`. The cap
applies to every PDS002MI transaction, not just `Delete`. Now documented in each field description.

**Verification method that worked:** capture a raw response (`?raw=true`), locate known values in it
(the child item number, a real date), and derive offsets from those anchors. Do not infer response
offsets from RPG source alone — this file has now broken that way twice.

---

## 2026-09-01 — This API is NOT the place for bulk data extracts (Inventory List `.dqy`)

**Context:** A broken legacy Excel query (`Inventory List.dqy`) needed replacing with a live item
master extract — every non-obsolete item plus its item group description. This API was the obvious
first candidate, since `MMS200MI` is already configured and DB2 direct-query endpoints already exist
here.

**Conclusion: rejected as the source. Do not add bulk extracts here.**

Two independent reasons:

1. **`MMS200MI.GetItmBasic` is a single-item lookup, not a list.** M3 MI list transactions also
   return one record per call and must be looped on the same session (see the 2026-07-01 entry on
   the `OK` terminator) — pulling a full item master that way is slow for no benefit.
2. **The DB2 endpoints here are a narrow, deliberate exception — not a general-purpose data tap.**
   All three (`/api/parts/search-alias`, `/api/parts/next-sequence`, `/api/customers/list`) exist
   *only because no MI equivalent exists*, and each returns a small targeted result. This project is
   an **M3 MI transaction gateway** over a managed TCP socket; a tens-of-thousands-of-rows table dump
   inverts that architecture.

**Where it belongs instead:** `Reporting-Service` — already a catalog-driven reporting service over
MOVEX DB2 with a working ClosedXML Excel renderer. Adding a report there is one catalog entry plus
one fetcher class.

**The general rule:** if the need is *"give me a lot of rows for analysis or export"*, it is a
reporting concern. If it is *"execute or look up one transaction"*, it belongs here. The existing DB2
exception endpoints are not a precedent for the former.

Design retained (build deferred pending ADR-028):
`c:/Projects/Knowledge-Management/vault/projects/inventory-list-dqy-replacement.md`

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
