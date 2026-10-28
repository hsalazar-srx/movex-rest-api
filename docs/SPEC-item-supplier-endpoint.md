# Spec — Item/supplier (PPS040 / `MITVEN`) read and price-validity update

**Status:** Draft 2026-10-26 — not implemented. Gated on the open checks at the end.
**Raised by:** Purchasing price-validity request (Syafiny), Max Wittmann's PPS040 recommendation (2026-10-22)
**Type:** New DB2 direct-query endpoints. **First DB2 write in this service** — needs an ADR before go-live.
**Source analysed:** `analysis/PPS040.txt` (RPG/400, *Work with Item/Vendor*, release 12.4, 11,766 lines).
Line numbers below refer to that file.

---

## Why this is needed

Purchasing wants a price **and its validity dates** per item per supplier. PPS040 (panel F) is where
Movex holds them, in `MITVEN`. There is no MI that reads or writes `MITVEN`:

| Candidate | Finding |
|---|---|
| `PPS040MI` | Does not exist in this instance |
| `MMS200MI.UpdItmPrice` | Writes **item master** fields (`MMSUNO`, `MMPUPR`, `MMCUCD`, `MMSAPR`…) via `MNGITE` — `analysis/MMS200MI.txt` 9343–9575. **No reference to `MITVEN` anywhere in MMS200MI.** It is not the PPS040 price. |
| Any other MI source in `analysis/` | Only `PPS040.txt` references `MITVEN` |

So, as with `items/search`, this has to be a DB2 direct query — but here it also **writes**, so
PPS040's own rules are ported into the service. The rest of this spec is that port.

---

## What PPS040 does when panel F is saved

### Record key — five fields, not two

`KITVE0` (11195–11200): **CONO + ITNO + PRCS + SUFI + SUNO**. `PRCS` (service process) and `SUFI`
(service ID) are blank for ordinary purchased items and populated for subcontract/service records.
Every query and update must include all five; the API defaults `prcs` and `sufi` to blank.

### Concurrency — optimistic, on `IFCHNO`

- On display, PPS040 reads the row **without a lock** and keeps `IFCHNO` in `XXCHNO` (7104).
- On save (`PFUPD`, 8376+) it re-reads **with** a lock, retrying until it gets it (8382–8384), then:
  - row gone → `XDE0001` *The record has been deleted by another user* (8386–8396)
  - `XXCHNO ≠ IFCHNO` → `XUP0001` *The record has been changed by user &1* (`IFCHID`) (8398–8411)
- It then stamps (8455–8464):
  - `IFLMDT` = today, `YYYYMMDD`
  - `IFCHID` = the signed-on Movex user (`DSUSS`)
  - `IFCHNO` = `IFCHNO + 1`

### Price-line validation (`PFCHK` 8086–8362)

Two price lines, each with price, valid-from, valid-to and setup price:

| | Line 1 | Line 2 |
|---|---|---|
| Purchase price | `IFPUPR` | `IFPUP1` |
| Valid from | `IFFVDT` | `IFFVD1` |
| Valid to | `IFUVDT` | `IFUVD1` |
| Setup price | `IFSUPR` | `IFSUP1` |
| Price quantity (shared) | `IFPUCD` | |

Rules, applied to each line in turn:

| # | Rule | PPS040 message | Lines |
|---|---|---|---|
| R1 | If price, from **or** to is entered, **valid-from must be a valid date** | `WFVD101` Valid from &1 is invalid | 8109–8130, 8242–8263 |
| R2 | Valid-to entered with no valid-from and no price is rejected | `WUVD101` Valid to &1 is invalid | 8134–8165, 8267–8298 |
| R3 | **Blank valid-to with a price and a valid-from is stored as `99999999`** (open-ended) | — | 8167–8171, 8300–8304 |
| R4 | Valid-to must not be earlier than valid-from | `XDT0002` From date is later than to date | 8173–8182, 8306–8315 |
| R5 | A valid-from with a zero price is rejected | `WPU0102` Purchase price must be entered | 8184–8194, 8317–8327 |
| R6 | **Line 2's valid-from must be after line 1's valid-to** | `PP04012` Overlapping date on price 1 | 8330–8340 |
| R7 | On save, a zero valid-from forces valid-to to zero | — (fix 24346) | 8447–8453 |

Consequence of R3 + R6: **while line 1 is open-ended (`99999999`), line 2 cannot be set.** To queue a
future price in line 2, line 1 needs an explicit end date.

Number formats: the price is converted with the item's price-decimal code `MMPDCC` and the price
quantity `IFPUCD` (`COMPRI`, 8087–8093). The setup price is converted with 11 digits (`COMNUM`, 8197–8204).

### Side effects of a save

| What changed | What PPS040 also writes |
|---|---|
| Price lines, dates, setup price | **Nothing else** — the `MITVEN` row only (8467) |
| `IFLOQT` (MOQ), and for new records `IFLEA1`/`IFUNMU` (PRCS/SUFI blank only) | `MITBAL` for every warehouse where this supplier is the main supplier of a purchased item (UNMU/LOQT/LEA1), and `MITPCC` planning-recalculation flag `MCRFPC = 2` (8483–8576), plus alternate items via `MITALT` (8579–8612) |
| Anything on panel F | `/COPY SRSCPPXU` *Save file value (PxUPD)* runs at the end (8652) — **not expanded in this listing** (open check 1) |

Script mode (`ZZSMOD`, copying the row to another company, 10961–10998) only runs when PPS040 is
launched from a script/template session (`CRS780DS`, 11470–11474), not in normal use.

### Other behaviour that matters to callers

- **Currency is not on `MITVEN`.** PPS040 never reads or writes `IFCUCD` (0 references). Panel F shows
  the **supplier's** currency, `CIDVEN.IICUCD` (7015–7020). A price is always in the supplier's currency.
- **The "Valid to" near the top of panel F is `IFQCUD`**, the *quality class* valid-to (8063–8084), not a
  price expiry. Don't use it for price validity.
- **Item status ≥ 50 protects the panel** (`MMSTAT >= '50'` sets indicator 71, 5155–5159). Whether
  that covers the panel F price fields depends on the display file, which isn't in this source (open check 4).
- Panel F validates the whole panel on save: Responsible required and must exist (`WRE0102/3`), country,
  quality class, packaging, communication code, MOQ. This endpoint changes none of those fields, so it
  doesn't re-validate them.

---

## Endpoints

### `GET /api/item-suppliers/{itno}/{suno}?cono=&prcs=&sufi=`

```sql
SELECT V.IFITNO, V.IFPRCS, V.IFSUFI, V.IFSUNO, V.IFISRS, V.IFRESP,
       V.IFPUPR, V.IFFVDT, V.IFUVDT, V.IFSUPR,
       V.IFPUP1, V.IFFVD1, V.IFUVD1, V.IFSUP1, V.IFPUCD,
       V.IFLOQT, V.IFLEA1, V.IFCHNO, V.IFCHID, V.IFLMDT,
       S.IICUCD, M.MMSTAT
FROM   {schema}.MITVEN V
LEFT JOIN {schema}.CIDVEN S ON S.IICONO = V.IFCONO AND S.IISUNO = V.IFSUNO
LEFT JOIN {schema}.MITMAS M ON M.MMCONO = V.IFCONO AND M.MMITNO = V.IFITNO
WHERE  V.IFCONO = ? AND V.IFITNO = ? AND V.IFPRCS = ? AND V.IFSUFI = ? AND V.IFSUNO = ?
```

Response: two `priceLines` (`price`, `validFrom`, `validTo`, `setupPrice`), with dates as `YYYYMMDD`
integers and `0` meaning blank. `validTo = 99999999` is returned as-is with `openEnded: true`. Also
returns `priceQuantity`, `currency` (from CIDVEN), `itemStatus`, `changeNumber`, `changedBy`, `changedDate`.

`GET /api/item-suppliers/{itno}?cono=` lists every supplier record for the item, with the same shape.

### `PATCH /api/item-suppliers/{itno}/{suno}?cono=&prcs=&sufi=`

```json
{
  "changeNumber": 4,
  "line1": { "price": 10.25, "validFrom": 20260101, "validTo": 20261231, "setupPrice": 0 },
  "line2": { "price": 10.90, "validFrom": 20270101, "validTo": 0 }
}
```

- `changeNumber` is **required** (the `IFCHNO` from the GET).
- Only price-line fields are accepted. Any other field → `400`. MOQ, lead time and status are out of
  scope because they trigger the `MITBAL`/`MITPCC` side effects above.
- Partial bodies are merged onto the current row, then **R1–R7 run on the merged result for both lines**,
  so the line 2 overlap rule (R6) always runs.
- Stricter than PPS040: negative prices are rejected. PPS040 only checks for non-zero.

Write — one statement, no read-modify-write window:

```sql
UPDATE {schema}.MITVEN
SET    IFPUPR = ?, IFFVDT = ?, IFUVDT = ?, IFSUPR = ?,
       IFPUP1 = ?, IFFVD1 = ?, IFUVD1 = ?, IFSUP1 = ?,
       IFLMDT = INT(VARCHAR_FORMAT(CURRENT DATE, 'YYYYMMDD')),
       IFCHID = ?,
       IFCHNO = IFCHNO + 1
WHERE  IFCONO = ? AND IFITNO = ? AND IFPRCS = ? AND IFSUFI = ? AND IFSUNO = ?
  AND  IFCHNO = ?
```

`IFLMDT` uses the IBM i date, not the IIS server clock. `IFCHID` is a configured Movex user ID for the
service (`ItemSupplier:ChangedByUser`, must exist in `CSYUSR`, 10 characters). The real caller goes to
the API log. So a PPS040 user who hits a conflict sees `XUP0001 … changed by <service user>`.

Responses:

| Outcome | HTTP | PPS040 equivalent |
|---|---|---|
| 1 row updated | `200` + re-read record | — |
| 0 rows, record missing | `404` | `XDE0001` |
| 0 rows, record exists (change number moved) | `409` + current `changedBy` / `changeNumber` | `XUP0001` |
| `SQL0913` (row locked — usually an open PPS040 session) | `423` *record is open in Movex* | PPS040 waits; we don't |
| Rule R1–R6 failed | `422` + PPS040 message ID + field | as table |
| Item status ≥ 50 | `422` | indicator 71 |

### Bulk load

`POST /api/item-suppliers/price-validity/batch?dryRun=true` takes the same body per row (plus key and
`changeNumber`). With `dryRun=true`, nothing is written and the response is a per-row validation report.
Rows are applied independently. A failed row doesn't roll back the others, and the response lists each
row's outcome.

Access: a separate API key tier with only this write. The WMS tier stays read-only.

---

## Open checks before implementation

| # | Check | How |
|---|---|---|
| 1 | What `SRSCPPXU` does at the end of every panel save (8652) | Pull source member `MVXCPYSRC,SRSCPPXU` (and `SRSCPPEI`, `SRSCPUPA`) the same way as `PPS040.txt` |
| 2 | Is `MITVEN` journaled? Decides commitment control (`*NONE` if not) | `SELECT JOURNALED FROM TABLE(QSYS2.OBJECT_STATISTICS('MVXCDTA','*FILE','MITVEN'))` |
| 3 | Any triggers on `MITVEN` | `SELECT * FROM QSYS2.SYSTRIGGERS WHERE EVENT_OBJECT_TABLE = 'MITVEN'` |
| 4 | Does item status ≥ 50 protect the price fields on panel F? | Open PPS040/F in CONO 300 for an item with status ≥ 50 |
| 5 | Blank valid-from with a price (R1): is it rejected? | CONO 300: enter a price with no valid-from and save |
| 6 | Which price line a PO picks on a given date | Not in PPS040 — PPS170/PPS200 pricing. Business question, not a blocker for the API |
| 7 | Confirm column names and types (`IFCHNO`, `IFCHID`, `IFLMDT`, `IFPUCD`) | `QSYS2.SYSCOLUMNS WHERE TABLE_NAME = 'MITVEN'`. The CIDVEN join key `IICONO + IISUNO` is confirmed (`KIDVEN`, 11388–11390) |
