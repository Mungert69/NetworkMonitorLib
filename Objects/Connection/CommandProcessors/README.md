# CommandProcessors

Command processors are the on-demand execution layer for agent commands. Each
processor defines its own CLI schema, runs within a queue, and returns a
`ResultObj` that the agent and UI surface to users.

## BLE broadcast processors

Both processors share one continuous platform scanner. Scheduled BLE endpoints
read immutable snapshots and complete without waiting for advertisements.

- `BleBroadcastCmdProcessor` (targeted): requires `--address`. Decodes matching
  packets in the endpoint's `Timeout × multiplier` lookback window and averages
  the selected metric in physical units. The default timeout is 7,000 ms with
  the existing 10× multiplier: a 70-second window. Existing scale/offset encoding is applied
  once by the connect. Diagnostic text remains the latest decoded advertisement.
- `BleBroadcastListenCmdProcessor` (listen): no address required. Returns all
  retained raw advertisements since its previous collection, without decryption,
  a scan timeout, or a capture-count limit.

At cycle start, enabled targeted endpoints establish per-address retention of
**twice the longest lookback window**. At cycle end, the listener publishes the
next immutable snapshot before evicting expired protected packets and all
unprotected packets. There are no buffer capacity limits. The first cycle has
an empty snapshot; later cycles report the preceding snapshot.

### Targeted crypto options

Targeted formats include `raw|aesgcm|aesctr|victron|ruuvi|bthome`. Generic AES
formats accept `--nonce_len` (default 12), `--tag_len` (default 16, AES-GCM), and
`--nonce_at start|end`. These do not override manufacturer protocol layouts.
Encrypted manufacturer packets require the correct key; unencrypted protocols
do not. Listen accepts legacy crypto and `--max_captures` options but ignores
them. Its payload/manufacturer/service filters remain available.

`--raw_payload` remains available for hardware-free replay. Linux live scanning
still requires BlueZ/D-Bus integration and is not implemented.

See [the BLE reproduction guide](../Ble/REPRODUCING.md#continuous-net-ble-reception-and-cycle-snapshots)
for lifecycle, averaging, retention, test commands and hardware verification.

If you add a new processor, register it in `CmdProcessorProvider` and update
endpoint descriptions in `EndPointTypeFactory`.
