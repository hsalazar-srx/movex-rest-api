# Lessons Learned: M3 MI List Transaction Protocol

**Date:** 2026-07-01  
**Discovered during:** PMS100MI/SelMaterials integration  
**Author:** hsalazar

---

## The Problem

List transactions in M3 MI (e.g. `SelMaterials`, `SelOperations`, `Select`) were returning only
the first record instead of the full result set.

## How M3 MI List Transactions Actually Work

M3 MI does **not** return all records in a single response. Each call to the socket returns
**exactly one record**. To retrieve all records, the client must re-send the same transaction
string on the **same open socket session** repeatedly until M3 signals end-of-list.

### Protocol sequence

```
Client → M3:  SelMaterials [input fields]   ← first call
M3 → Client:  REP [data: record 1]

Client → M3:  SelMaterials [input fields]   ← re-send same string
M3 → Client:  REP [data: record 2]

Client → M3:  SelMaterials [input fields]   ← re-send again
M3 → Client:  REP [data: record N]          ← last data record

Client → M3:  SelMaterials [input fields]   ← re-send again
M3 → Client:  OK  [data: record N repeated] ← END-OF-LIST SIGNAL

(if re-sent once more, M3 wraps to record 1)
```

### Termination signals

| Prefix | Meaning |
|--------|---------|
| `REP`  | Valid data record — add to result set |
| `OK`   | End-of-list — **do not add**, stop the loop |
| `NOK`  | Error or no records found — stop the loop |

The `OK` response contains a **repeat of the last data record** with the prefix changed from
`REP` to `OK`. It must not be added to the result set — it is purely a termination signal.

The `NOK` response means either a genuine error or an empty result set (no records matched).
For list transactions, `NOK` on the first call should be treated as `success: true` with an
empty array, not a failure.

### Why naive single-call doesn't work

A single `SendReceiveAsync` call only exchanges one message. To iterate, the client must call
`SendReceiveNextAsync` (same session, same socket, same transaction string) in a loop.

### Why the connection must not be reused mid-stream

M3 maintains cursor state server-side, tied to the open socket session. If the loop exits
early (raw mode, duplicate detection, error), the cursor is left open. Returning that
connection to the pool and reusing it for a subsequent request will cause the next call to
receive records from the middle of the previous list instead of starting fresh.

**Fix:** mark `PooledConnection.IsHealthy = false` whenever the loop exits without consuming
the `OK` terminator. This forces the connection to be discarded rather than pooled.

---

## Implementation in This Project

### `MvxSockWrapper.SendReceiveNextAsync`

Re-sends the transaction on the already-open session without re-connecting or re-logging in:

```csharp
public async Task<string> SendReceiveNextAsync(string request)
{
    if (_socket == null || !_socket.Connected || !_loggedIn)
        throw new InvalidOperationException("Cannot fetch next record: session is not open.");
    return await ExecuteAsync(request, true) ?? string.Empty;
}
```

### `TransactionService` — list loop

```csharp
for (int i = 0; i < maxIterations; i++)
{
    string trimmed = currentRaw.TrimStart();

    // OK = end-of-list (last record repeated with OK prefix)
    // NOK = error or no records
    if (trimmed.StartsWith("NOK", ...) || trimmed.StartsWith("OK", ...))
    {
        cursorExhausted = true;
        break;
    }

    var parsed = _responseParser.ParseResponse(currentRaw, txDef.ResponseFields);
    if (parsed.Count > 0) allRecords.Add(parsed);

    string nextRaw = await pooled.Connection.SendReceiveNextAsync(transactionString);
    if (string.IsNullOrEmpty(nextRaw)) { cursorExhausted = true; break; }
    currentRaw = nextRaw;
}

if (!cursorExhausted)
    pooled.IsHealthy = false; // cursor still open — discard connection
```

### Config: `returnsList: true`

Transactions that return multiple records must be flagged in their JSON config:

```json
{
  "name": "SelMaterials",
  "returnsList": true,
  ...
}
```

This triggers `ParseResponse` (single buffer → single record dict) instead of
`ParseResponseList`, inside the iteration loop.

---

## Things That Did Not Work (and Why)

| Approach | Why it failed |
|---|---|
| Single `SendReceiveAsync` call, parse buffer as multi-record | M3 only puts one record per response buffer |
| Loop using `ParseResponseList` on each buffer | Each buffer is one record; `ParseResponseList` split by fixed width produced garbage on records that aren't multiples of the record length |
| HashSet on full raw payload to detect wrap-around | The 15-char M3 prefix (`REP` + spaces) is constant but the `OK` prefix is different — the HashSet didn't match `OK` vs `REP` payloads |
| Checking for NOK only as terminator | PMS100MI sends `OK`, not `NOK`, after the last record |
| Fetch-next after add, check NOK on next iteration | The `OK` record was parsed and added before the check fired on the following iteration |

---

## Affected Transactions

Any transaction with `"returnsList": true` in its JSON config uses this loop:

- `PMS100MI/SelMaterials`
- `PMS100MI/SelOperations`
- `PMS100MI/Select`
- Any future list transaction added to any program config

---

## See Also

- [Knowledge Management vault entry](../../../../Knowledge-Management/vault/learnings/cross-project/m3-mi-list-protocol-ok-terminator.md)
- `Services/TransactionService.cs` — list loop implementation
- `Infrastructure/M3Protocol/MvxSockWrapper.cs` — `SendReceiveNextAsync`
- `transactions/PMS100MI.json` — reference config with `returnsList: true`
