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

If you add a new processor, register it in `CmdProcessorProvider` and update
endpoint descriptions in `EndPointTypeFactory`.
