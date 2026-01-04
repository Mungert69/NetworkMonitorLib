# Factory

Factory helpers that map endpoint types to their runtime implementations. The
`EndPointTypeFactory` class defines:
- UI-facing endpoint descriptions and icons.
- Response-time thresholds by endpoint.
- `INetConnect` implementations used by agents.

## BLE endpoints
`EndPointTypeFactory` now exposes two BLE modes:
- `blebroadcast`: targeted address capture with optional decrypt.
- `blebroadcastlisten`: listen mode for any broadcast, capped and timeout-safe.

Keep endpoint descriptions here in sync with command processor CLI help to avoid
LLM/UI confusion.
