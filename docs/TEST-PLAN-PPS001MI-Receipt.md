# Test plan — `PPS001MI/Receipt` (CONO 300)

**Status:** Definition added 2026-10-29 (`transactions/PPS001MI.json`), not yet run against Movex.
**Layout source:** MRS003, verified 2026-09-22 — 15 input fields, positions 16–147, no output fields.
**Guarded by:** `Movex.API.Tests/Configuration/Pps001MiReceiptLayoutTests.cs` (every field at its MRS003 position).

Run everything in **CONO 300** (`MVXC300`). A receipt moves stock, so never point this at CONO 100 until
all steps below pass.

---

## 1. Pick a test PO line

```sql
SELECT IBPUNO, IBPNLI, IBPNLS, IBITNO, IBWHLO, IBPUSL, IBPUST, IBGRMT, IBRVQA
FROM   MVXC300.MPLINE
WHERE  IBCONO = 300 AND IBPUSL < '50'
ORDER  BY IBPUNO DESC
FETCH FIRST 20 ROWS ONLY;
```

Pick one line with an open quantity. Note `IBGRMT` (goods receiving method): it decides whether the receipt
lands at inspection (status 50) or direct put-away (75).

Also note the **last number of series 21/1 "Goods Receiving No."** in CRS165. It should move by one
when the receipt is booked, and that number is the receipt number MES needs.

## 2. Book one receipt

```http
POST /api/PPS001MI/Receipt
X-API-Key: <full-access key>
Content-Type: application/json

{
  "CONO": "300",
  "TRDT": "20261029",
  "RESP": "<your Movex user>",
  "PUNO": "<PO>",
  "PNLI": "<line>",
  "PNLS": "0",
  "RVQA": "1",
  "WHLO": "<IBWHLO>"
}
```

Expected: `OK`, no data (Receipt has no output fields).

## 3. Check what changed

| Check | Expect |
|---|---|
| `MPLINE` row (query from step 1) | `IBRVQA` up by the received qty; status 50 or 75 per `IBGRMT` |
| CRS165 series 21/1 | Last number up by one: that is the receipt number |
| Newest `MITTRA` rows for the PO (`SELECT * FROM MVXC300.MITTRA WHERE MTRIDN = '<PO>' ORDER BY MTRGDT DESC, MTRGTM DESC FETCH FIRST 5 ROWS ONLY`) | A receipt transaction, and the column holding the receipt number. Record its name: MES will read it from here |
| PPS300 / PPS310 on screen | The receipt shows like a manual one. If at 50, it waits in PPS310 (inspection stays manual) |

## 4. Settle the open questions

| Question | How |
|---|---|
| `RESP` (12 chars, required): what value? | Step 2 with your Movex user ID. If rejected, note the message |
| `PNLS` for a line with no subline | `0` in step 2. If rejected, try the line number |
| `RVQA` is alpha(11): number or string? | Send `"1"` (step 2), then a JSON number `1`, then a decimal for an item with decimals |
| Does a failure come back as a real error? | Send `PUNO` = `9999999`. Expect `NOK` with Movex's message, **not** `M3_NULL_RESPONSE` |
| `WHLO` vs `WHSL` | Step 2 sends `WHLO` only. Then repeat with an explicit `WHSL` location |

## Results — 2026-10-29 (local API, CONO 300, PO D11345 line 1)

| Check | Result |
|---|---|
| Invalid PO (`9999999`) | `success: false`, `"Purchase order line 1 does not exist"`: a real Movex error, not `M3_NULL_RESPONSE` |
| `PUNO` sent as `"D11345"` | **Rejected: "Purchase order line 1 does not exist".** PO numbers are stored **right-aligned** in `MPLINE.IBPUNO` (`[ D11345]`, `[    ECO]`), and the API pads text on the right |
| `PUNO` sent as `" D11345"` | **OK.** Line 1: status 20 → **75** (goods receiving method 010 = direct put-away), `IBRVQA` 0 → 1 |
| `MITTRA` | New row: type **25**, item N0238-016-998, warehouse D01, location **DEFAULT** (no `WHSL` sent), qty 1, `MTRIDL` = 1000 |
| Receipt number | Series 21/1 moved 8536751 → **8536752**. `MITTRA.MTREPN` (Receiving number) = **8536752001**: the series number plus a 3-digit suffix |
| `RESP` | The API's configured M3 user was accepted, and stored in `MTRESP` |
| `PNLS` = `0`, `RVQA` = `"1"` (string) | Accepted |

**For MES:** read the receipt number from `MITTRA.MTREPN` for the PO + line after the call; the Receipt reply holds none.

**Fix needed before use:** `PUNO` must be right-aligned to 7 characters. Until the definition supports that, callers must left-pad the PO number with spaces.

## 5. Record results

Update the vault note `m3-knowledge/transactions/pps001mi-po-receipt.md`: answers to step 4, the
`MITTRA` receipt-number column, and the date verified. Then decide whether `Receipt` goes onto any API
key tier beyond full access (the WMS tier is read-only by design; adding a write there needs an ADR).
