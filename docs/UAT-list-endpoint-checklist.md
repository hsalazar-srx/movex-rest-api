# UAT checklist — list endpoints after the MvxSock framing fix

**What changed:** `MvxSockWrapper.ExecuteAsync` now reads only the bytes still missing from a message
(`f8fa855`). The old code could swallow the start of the next record when a list body arrived in more than
one read. Only list transactions are exposed to this (M3 sends the next record before it is asked for), so
this checklist covers every transaction with `returnsList: true`. Single-record transactions, including all
creates and updates, are not affected.

**Run on:** UAT, company 300, branch `feat-enabling-CRS610MI-customer-list` (stop any running API first so the
build can replace the exe).

## Before you start

- [ ] Branch built and API running; note the build/commit (`git log --oneline -1`).
- [ ] `KEY` and `BASE` set: `BASE=http://localhost:5000`, `KEY=<api key>`.
- [ ] Always save responses to a file (`-o`). Pasting from a terminal drops spaces where long lines wrap.
- [ ] Know the M3 limit: **a list call returns at most 100 records.** For a list whose true size is above 100,
      the expected result of a direct call is exactly 100. That is not a failure of this change, but write it down.

## How to run each check

Run each call **5 times** (the old bug depended on timing, so one clean run proves little):

```bash
# example: set URL, then
for i in 1 2 3 4 5; do curl -s "$URL" -H "X-API-Key: $KEY" -o run$i.json; done
for i in 1 2 3 4 5; do sed 's/"timestamp":"[^"]*"//' run$i.json | md5sum; done | sort | uniq -c   # expect ONE line
grep -o '"count":[0-9]*' run1.json                                                                  # compare with truth
grep -c 'YREP\|\\u0000' run1.json                                                                   # expect 0
```

**Every check passes only if all four hold:**
1. the five runs give identical content (one line from `uniq -c`);
2. `count` equals the DB2 truth, or equals **100** when the truth is above 100;
3. no fragments in the response (`YREP`, `\u0000`, text in numeric fields);
4. no `Could not parse ... as decimal` warnings in the API console during the runs.

## Checks

| # | Endpoint | Call | DB2 truth (company 300) | Result |
|---|---|---|---|---|
| 1 | CRS610MI `LstByNumber` | `GET $BASE/api/customers/basic?cono=300&status=20,90` | `SELECT COUNT(*) FROM MVXC300.OCUSMA WHERE OKCONO=300 AND OKSTAT IN ('20','90')` → expect **1,434** (already confirmed 2026-10-28). Direct route `/api/CRS610MI/LstByNumber?CONO=300` returns 100 by design | |
| 2 | MMS200MI `LstItmFac` | `GET $BASE/api/MMS200MI/LstItmFac?CONO=300&ITNO=<item>` — pick an item registered in many facilities; repeat with `FACI=<facility>` and no `ITNO` (large) | `SELECT COUNT(*) FROM MVXC300.MITFAC WHERE M9CONO=300 AND TRIM(M9ITNO)='<item>'` (and `TRIM(M9FACI)='<faci>'` for the second call) | |
| 3 | PDS002MI `LstComponent` | `GET $BASE/api/PDS002MI/LstComponent?CONO=300&FACI=<f>&PRNO=<prno>&STRT=001` — one BOM under 100 lines, one over 100 | `SELECT COUNT(*) FROM MVXC300.MPDMAT WHERE PMCONO=300 AND TRIM(PMFACI)='<f>' AND TRIM(PMPRNO)='<prno>' AND TRIM(PMSTRT)='001'`. Cross-check with `GET $BASE/api/bom/<prno>?cono=300&faci=<f>&includeExpired=true` | |
| 4 | PDS002MI `LstOperation` | `GET $BASE/api/PDS002MI/LstOperation?CONO=300&FACI=<f>&PRNO=<prno>&STRT=001` | `SELECT COUNT(*) FROM MVXC300.MPDOPE WHERE POCONO=300 AND TRIM(POFACI)='<f>' AND TRIM(POPRNO)='<prno>' AND TRIM(POSTRT)='001'` ⚠ `POFACI/POPRNO/POSTRT` not confirmed — describe the table if DB2 rejects them | |
| 5 | PMS100MI `Select` | `POST $BASE/api/PMS100MI/Select` body `{"CONO":"300","FACF":"<f>","FACT":"<f>","STSF":"20","STST":"90"}` (POST, JSON) — a wide filter (>100 MOs) and a narrow one (<100) | `SELECT COUNT(*) FROM MVXC300.MWOHED WHERE VHCONO=300 AND TRIM(VHFACI)='<f>' AND VHWHST BETWEEN '20' AND '90'` | |
| 6 | PMS100MI `SelOperations` | `POST $BASE/api/PMS100MI/SelOperations` body `{"CONO":"300","FACI":"<f>","PRNO":"<prno>","MFNO":"<mo>"}` | `SELECT COUNT(*) FROM MVXC300.MWOOPE WHERE VOCONO=300 AND TRIM(VOFACI)='<f>' AND TRIM(VOMFNO)='<mo>'` ⚠ `VO` prefix not confirmed | |
| 7 | PMS100MI `SelMaterials` | `POST $BASE/api/PMS100MI/SelMaterials` body `{"CONO":"300","FACI":"<f>","MFNO":"<mo>"}` | `SELECT COUNT(*) FROM MVXC300.MWOMAT WHERE VMCONO=300 AND TRIM(VMFACI)='<f>' AND TRIM(VMMFNO)='<mo>'` | |
| 8 | PMS100MI `SelErrOrders` | `POST $BASE/api/PMS100MI/SelErrOrders` body `{"CONO":"300","FACI":"<f>"}` | Usually empty or very few rows — confirm an empty result returns `count: 0` cleanly, not an error | |

Finding test data: for #3 and #4, `SELECT PMPRNO, COUNT(*) FROM MVXC300.MPDMAT WHERE PMCONO=300 GROUP BY PMPRNO ORDER BY 2 DESC FETCH FIRST 5 ROWS ONLY`
gives BOMs of different sizes; for #2 group `MITFAC` by `M9ITNO`.

## Also check

- [ ] **One single-record read** (for example `MMS200MI/GetItmBasic` for a known item) returns the same fields as before.
- [ ] **A write, once, supervised, on UAT only:** one `MMS175MI` or `MMS310MI` update of a test item, then confirm it in MOVEX. Same framing code, single message.
- [ ] After the runs, API log has no `Socket closed by remote host` or `Cannot run command` errors.
- [ ] Optional baseline: repeat #3 and #5 on the `develop` build (before the fix) and keep both outputs. Differences
      show what the old reader got wrong.

## Record the outcome

| Item | Notes |
|---|---|
| Build/commit tested | |
| Date, tester | |
| Any list over 100 rows returned complete? | Expected no. If a consumer needs complete lists from #2, #3, #5 or #6, raise it — the 100-record limit applies there too |
| Failures | |

Known gap, not tested: a message header that arrives in fewer than 4 bytes makes the reader return empty.
