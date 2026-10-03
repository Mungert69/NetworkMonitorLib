# Connection

Connection and command processor infrastructure for NetworkMonitorLib. This includes
connection factories, command processors, protocol helpers, and scan/CLI parsing.

## Dynamic connect status contract

Dynamic connects must declare an expression-bodied literal vocabulary:

```csharp
public override IReadOnlyCollection<string> StatusLabels =>
    new[] { "Available", "Unavailable", "Timeout", "Exception" };
```

Include `using System.Collections.Generic`. Declarations accept `new[]`,
`new string[]`, or collection expressions of string literals only. There must be
1–64 unique labels, each 1–128 characters with no surrounding whitespace/control
characters. Computed declarations and missing declarations are rejected before
registration. Existing saved dynamic connects must be updated to this contract
before deploying a processor built with this enforcement; built-ins are unaffected.

`ProcessStatus`'s first argument and `ProcessException`'s second argument must
match a declared label exactly (ordinal comparison). Put readings, addresses,
ports, payloads and variable errors in their diagnostic arguments instead.
Each dynamic instance gets a private frozen vocabulary. Undeclared statuses are
replaced by `Invalid connect status`, with bounded diagnostics and a log warning;
success/failure and the numeric sample remain unchanged. The wrapper checks the
final result even after an exception or direct `PingInfo.Status` assignment.
Factories must return the declared class, not substitute another implementation.

This is status-contract validation, **not a security sandbox** for arbitrary C#.
Generated code still runs with the processor's existing privileges. LLM prompt,
tool descriptions and few-shot declarations live in the sibling NetworkMonitorLLM
repo and must be deployed alongside this change. Failed `add_connect` operations
return actionable errors for correction/resubmission.

## BLE broadcast endpoints
Two BLE modes are supported to keep CLI/LLM help simple:
- `blebroadcast` (targeted) uses `BleBroadcastConnect` and requires an address. It
  captures a single advertisement and optionally decrypts/decodes it.
- `blebroadcastlisten` (listen) uses `BleBroadcastListenConnect` and does not require
  an address. It collects a capped set of broadcasts until timeout and returns a
  best-effort decoded list without treating the timeout as an error.

Both endpoints live in this folder and are wired in `EndPointTypeFactory`.

## Generic crypto layout
For non-Victron payloads, command processors can attempt AES-GCM or AES-CTR when
the caller provides a key. Optional layout flags let callers describe the nonce/tag
layout so the decoder can work with different device formats.
