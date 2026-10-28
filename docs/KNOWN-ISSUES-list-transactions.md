# Known issues and unverified areas — list transactions and `/api/customers/basic`

Written 2026-10-28 when `/api/customers/basic` was made available for UAT. Test plan: [UAT-list-endpoint-checklist.md](UAT-list-endpoint-checklist.md).

## What was verified

- `GET /api/customers/basic?cono=300&status=20,90` returns **1,434** customers on UAT (135 + 1,299, matching the DB2 counts), paged by customer number.
- Loopback framing tests (`MvxSockWrapperFramingTests`, 4 tests) pass on the fixed reader; two of them fail on the previous reader. Full suite: 55 passing.

## What was not verified

| # | Issue | Detail | Impact |
|---|---|---|---|
| 1 | **The socket fix changes the shared reader** (`MvxSockWrapper.ExecuteAsync`, commit `f8fa855`) | Only the customer list was run on UAT. The other 7 list transactions (`MMS200MI/LstItmFac`, `PDS002MI/LstComponent`, `LstOperation`, `PMS100MI/Select`, `SelOperations`, `SelMaterials`, `SelErrOrders`) were **not** run. Single-record reads and writes go through the same code but a single message cannot trigger the old bug, so they are expected to behave the same. That is reasoning, not a test | Unlikely, but a list consumer could see different results than before |
| 2 | **M3 returns at most 100 records per list call** | Observed on `CRS610MI/LstByNumber` (MiTEST showed the same, and raising MiTEST's max-records returned everything). The REST API cannot raise it. Other lists very probably have the same limit. Only `/api/customers/basic` pages around it | A consumer of `LstItmFac`, `LstComponent`, `Select` or `SelOperations` may have been receiving the first 100 rows without any error |
| 3 | **Existing list output may change** | Before the fix, lists could return fragments or a different record count on each call. A consumer that adapted to that (for example Oskar's BOM reads) could see different, now correct, results | Compare with the DB2 equivalent after deployment |
| 4 | **Header split gap** | If a message header arrives in fewer than 4 bytes, the reader returns an empty string. Not triggered in any test or run; not handled | Rare; would show as an empty response |
| 5 | **Production not tested** | The UAT run used company 300 as `SRXAPI300`. Whether the deployed API's M3 user can read company 100 is unchecked. Expected production count: 1,439 for `status=20,90` (141 + 1,298, from the `OKSTAT` counts queried on company 100 on 2026-10-28: 3 / 141 / 1,298) | Confirm before telling MES to use `cono=100` |
| 6 | **Page boundary on a non-alphanumeric `CUNO`** | The paging passes the last `CUNO` as the next start value. Input validation allows letters, digits and a few symbols, so a page ending exactly on `????` would be rejected. `????` and `INTERNAL` sort first, so the risk is low | A 400/502 from `/api/customers/basic` |
| 7 | **Customer list speed** | Each list call opens a fresh M3 connection (list connections are always discarded), so about 15 logins per full customer list. Around 1 second per page on UAT | Slow for frequent polling; MES should cache |
| 8 | **Customer number format** | `CUNO` is returned trimmed but as stored (`0283`, not `283`). Two numbers are not numeric: `????` and `INTERNAL` (status 10 templates). MES must match as text | Lookups fail if MES converts to a number |
| 9 | **Default status** | `status` defaults to `20` when omitted; status 10 has 3 customers, meaning unconfirmed | Callers that omit `status` miss inactive customers |

## Why the old reader failed (inferred, not documented by Infor)

After the 4-byte header the old loop allocated a full-size buffer on every pass. When a body arrived in two or more reads and M3 had already written the next record, the second read took the start of that record. Fragments such as `\u0000\u0000\u0003YREP` (a length header plus `REP`) appeared inside customer names. That M3 writes records ahead of the next command is an inference from those fragments.

## Not a cause

- The 500-record loop cap in `TransactionService` was suspected and was **not** the limit (it was restored).
- `CONO` reading 0 in list results is not a fault: M3 does not send `CONO` back for `LstByNumber`.
- Missing spaces in names pasted from a terminal were line-wrap artifacts. Save responses with `curl -o` before inspecting them.
