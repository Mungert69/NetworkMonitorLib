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

For specification links, implementation rationale, packet fixtures, exact test
commands, and platform/hardware checks, see
[Reproducing and extending the BLE decoders](Ble/REPRODUCING.md).
Two BLE modes are supported to keep CLI/LLM help simple:
- `blebroadcast` (targeted) uses `BleBroadcastConnect` and requires an address. It
  captures a single advertisement and optionally decrypts/decodes it.
- `blebroadcastlisten` (listen) uses `BleBroadcastListenConnect` and does not require
  an address. It collects a capped set of broadcasts until timeout and returns a
  best-effort decoded list without treating the timeout as an error.

Both endpoints live in this folder and are wired in `EndPointTypeFactory`.

## Generic crypto layout
When callers select `aesgcm` or `aesctr`, command processors can attempt generic
decryption with the supplied key. Manufacturer/protocol decoders own their crypto layouts. Optional layout flags let callers describe the nonce/tag
layout so the decoder can work with different device formats.

## Measurement definitions

Connects inherit constant `Unit = "ms"`, `Scale = 1`, and `Type = ""` properties.
Custom endpoints can override these (for example `Unit => "V"` and `Scale => 0.01`
for a reading stored in hundredths of a volt). Scale is a display multiplier;
storage and probe status semantics do not change. Keep one measurement meaning
per endpoint/subtype definition. Do not derive these properties from host configuration or
individual probe results. Blank Type is the general/fallback definition.
`MeasurementVariants` can declare constant `EndpointMeasurementMetadata(Unit, Scale, Type)`
subtypes. The shared selector matches case-insensitive whole tokens in host Args:
exactly one distinct subtype selects it; zero or multiple matches use general.
Repeating the same token is not ambiguous. This intentionally does not parse
option semantics: an unrelated argument containing the token can select it.

The provider builds and sends only its custom Connect catalogue on startup, processor init,
and successful custom Connect changes, through the existing processor data route.
Delivery is best effort. Ordinary monitoring publications do not contain it.
Built-in metadata is built once per backend process by `EndpointMeasurementDefaults`
using the fixed Connect classes. The immutable catalogue keeps only non-default
definitions and never initializes or executes probes. No built-in database rows
or processor-specific copies are needed.
Targeted BLE defines metric subtypes: voltage V/0.01, current A/0.1, yield kWh/0.01,
and PV power W/1. Without an unambiguous metric token it remains `raw value`.
These labels describe configured metrics: missing-metric elapsed-time fallbacks
cannot be distinguished by this simple selector. Negative currents are already
clamped by existing probes; scaling does not recover them.
See NetworkMonitorData/tools/endpoint-measurements.md for the
database and frontend flow.

## Extending BLE decoding

Both BLE command processors use `Ble/BlePayloadDecoderRegistry.cs`. To add a
protocol, implement `IBlePayloadDecoder` and register it in the default catalogue.
The decoder supplies its default manufacturer ID, packet acceptance rules,
diagnostics, and decoding. Platform callbacks delegate these decisions to the
selected decoder. Existing `--format victron` and raw/AES modes remain supported.
Manufacturer payloads currently retain their two-byte company ID; `raw` means a
complete advertisement, while `raw_input` preserves the existing payload override.

For another Victron device record, implement `IVictronRecordDecoder` and add it to
the `VictronDeviceRecordDecoders.CreateDefaults()` catalogue. Advertisement framing, key checks,
and AES handling remain shared. Unknown record types still return decrypted
plaintext. Record decoders can be supplied explicitly for isolated fixture tests.

This first extraction keeps the existing text result contract and endpoint metric
parsing. Structured readings, per-device key lookup, and a shared platform scan
service are subsequent changes. Linux live capture still requires BlueZ integration.

### Victron device coverage

`--format victron` now decodes solar chargers (0x01), battery monitors/SmartShunt
(0x02), inverters (0x03), DC/DC converters (0x04), SmartLithium (0x05), Inverter RS
(0x06), AC chargers (0x08), Smart Battery Protect (0x09), Lynx Smart BMS (0x0A),
Multi RS (0x0B), VE.Bus (0x0C), DC energy meters (0x0D), and Orion XS (0x0F).
Coverage refers to advertisement record formats; a device must support and enable
Instant Readout and the caller must supply its advertisement AES key.

Layouts follow [Victron Extra Manufacturer Data, 2022-12-14](https://communityarchive.victronenergy.com/storage/attachments/extra-manufacturer-data-2022-12-14.pdf)
and [Victron staff's Orion XS and AC charger update](https://community.victronenergy.com/t/orion-xs-12v-12v-50a-bluetooth-advertising-data/2183).
The published AC charger layout remains firmware-dependent and needs validation
against real hardware. Battery Protect's inconsistent PDF start-bit column is
interpreted as consecutive decrypted fields, consistent with the
[victron-ble implementation](https://github.com/keshavdv/victron-ble/blob/main/victron_ble/devices/smart_battery_protect.py).
GX (0x07), test records (0x00), and unknown types have no device decoder and retain
the decrypted plaintext fallback. Live protocol filtering accepts registered types
only; use raw capture when discovering unsupported records.

Signed readings retain their sign in command output; protocol unavailable markers
print `NA`. Battery-monitor/DC-meter auxiliary data follows its mode (aux voltage,
midpoint voltage, temperature, or none). Packed fields use little-endian bit order.
The targeted Connect still supports its existing metric list and ushort storage;
adding a record decoder does not add endpoint metric selection or signed storage.

### Ruuvi and BTHome sensors

Use the existing BLE endpoints with the device address and these Args:

| Device/protocol | Args | Password |
| --- | --- | --- |
| RuuviTag RAWv2 | `--format ruuvi` | Empty |
| BTHome v2, unencrypted | `--format bthome` | Empty |
| BTHome v2, encrypted | `--format bthome` | Device's 16-byte AES key, usually 32 hex characters |

Ruuvi automatically selects manufacturer ID `0x0499`. The decoder supports format
5 (RAWv2), including temperature, humidity, pressure, acceleration, battery voltage,
TX power, movement/sequence counters, and the embedded MAC. Unavailable values
print `NA`. Other Ruuvi formats are not yet decoded.

BTHome automatically selects service data with UUID `0xFCD2`; there is no need to
set `--payload service` or `--service_uuid`. Explicit payload/service arguments
still override the defaults. Matching service data does not require a separate
service UUID list in the advertisement. The decoder supports the documented v2
numeric and binary measurements, button/dimmer events, text/raw values, command
objects (display only), and device/firmware information. Unknown object IDs stop
parsing with a diagnostic while preserving earlier readings. Truncated objects
fail decoding. Repeated measurements/events receive numbered labels.

Encrypted BTHome uses AES-CCM and verifies the packet's authentication tag. Its
nonce includes the actual device MAC, service UUID, device-info byte, and counter.
The encryption counter is reported; the decoder does not maintain persistent
replay protection. BTHome v1 is not supported. Protocol identification does not
imply identification of an exact manufacturer/model.

Keys are parsed consistently from hex, base64, or raw text, then validated by the
selected protocol. Unencrypted Ruuvi requires no key; Victron and encrypted
BTHome use AES-128. Generic AES modes retain 16/24/32-byte key support. Both
command processors apply protocol packet filters even without a key when the
protocol permits unencrypted messages.

For hardware-free checks, `--raw_payload` accepts a Ruuvi RAWv2 body (optionally
with company ID), or BTHome v2 service-data body (optionally with UUID). To replay
a complete advertisement, use a decoder fixture with payload type `raw`.
Encrypted BTHome replay needs the sender's MAC: use the targeted command's
`--address`; address-free listen overrides cannot decrypt encrypted packets.
Live listen captures contain the sender address and can decode them normally.

These additions provide command output; the Connect's numeric metric selection
still uses its existing metric catalogue. Linux live BLE scanning still requires
BlueZ integration. No physical Ruuvi/Shelly hardware validation was performed.

Protocol references and independent test vectors:
[Ruuvi RAWv2](https://docs.ruuvi.com/communication/bluetooth-advertisements/data-format-5-rawv2),
[BTHome v2 format](https://bthome.io/format/), and
[BTHome encryption](https://bthome.io/encryption/).
