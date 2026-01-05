# CommandProcessors

Command processors are the on-demand execution layer for agent commands. Each
processor defines its own CLI schema, runs within a queue, and returns a
`ResultObj` that the agent and UI surface to users.

## BLE broadcast processors
- `BleBroadcastCmdProcessor` (targeted): requires `--address`. Returns a single
  capture and optionally decrypts payloads.
- `BleBroadcastListenCmdProcessor` (listen): no address required. Collects up to a
  capped number of captures within the timeout window and returns best-effort
  decoded results. Timeout is treated as a normal completion.

### Crypto options (non-Victron)
Both processors accept optional crypto layout flags when `--format` is set to a
non-Victron mode:
- `--format raw|aesgcm|aesctr|victron`
- `--nonce_len <int>` (default 12)
- `--tag_len <int>` (default 16, AES-GCM only)
- `--nonce_at start|end` (default start)
- `--max_captures <int>` (listen mode only, default 10)

If no key is provided, the processors return raw/plaintext output without error.

If you add a new processor, register it in `CmdProcessorProvider` and update
endpoint descriptions in `EndPointTypeFactory`.
