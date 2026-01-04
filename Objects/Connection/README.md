# Connection

Connection and command processor infrastructure for NetworkMonitorLib. This includes
connection factories, command processors, protocol helpers, and scan/CLI parsing.

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
