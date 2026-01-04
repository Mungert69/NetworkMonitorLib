# Utils

Low-level utilities and converters used across NetworkMonitorLib. Keep reusable
helpers here (parsers, formatters, converters).

## BLE crypto helpers
`BleCryptoHelper` and `BleCryptoOptions` provide generic AES-GCM/AES-CTR support
for BLE broadcasts when a device publishes encrypted payloads with known nonce/tag
layout. These helpers are shared so dynamic command processors can reuse the same
logic.
