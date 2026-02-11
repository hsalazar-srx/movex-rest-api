# Lessons Learned: M3 Response Success Validation

## Summary
Some M3 programs return successful responses without an `MSID` field. When that happens, the API previously classified valid responses as failures because success validation relied on `MSID` alone.

## What Happened
- The controller checks `MSID` (and `MSDT`) for parsed responses to decide success.
- Certain responses are valid but do **not** include `MSID`.
- These responses were marked as unsuccessful, even when the raw response started with `OK`.

## Root Cause
Success validation depended on `MSID` for parsed responses, but the raw response prefix (`OK`/`NOK`) is the authoritative indicator of success in M3.

## Fix Implemented
- The service now inspects the raw response prefix:
  - `NOK` responses are returned as raw strings for consistent error handling.
  - `OK` responses are parsed, and when `MSID` is missing, it is injected as `"000"` with `MSDT="OK"`.
- The controller now handles list responses when evaluating `MSID`.

## Additional Lesson: Pooled Connection Program Context
### What Happened
- Workflows chained calls to different MI programs using the same pooled socket.
- The socket stayed logged into the first program, so subsequent transactions ran under the wrong program context.

### Root Cause
The socket client only logged in once per connection and did not re-login when the requested program/library changed.

### Fix Implemented
- Track the current library/program on each socket.
- Close the existing session and re-login when `apiprogram` changes to ensure the correct MI context for each transaction.

## How to Prevent This
1. Treat `OK`/`NOK` as the primary success signal.
2. Do not assume `MSID` exists for all programs/transactions.
3. When parsing responses, either carry the raw prefix forward or ensure success metadata is added consistently.
4. Add a sample raw response for each new transaction during onboarding to confirm prefix behavior.
5. When using pooled connections, always verify the program/library context is refreshed between different MI programs.

## Quick Checklist
- [ ] Does the raw response start with `OK` or `NOK`?
- [ ] Are `MSID`/`MSDT` present for this program?
- [ ] If not present, do we preserve success status via the prefix?
- [ ] Are list responses evaluated consistently?
- [ ] Are pooled sockets re-authenticated when switching MI programs?
