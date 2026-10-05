# Reproducing and extending the BLE decoders

This guide records the implementation and validation of Victron Instant Readout,
Ruuvi RAWv2, and BTHome v2 as of 2026-10-04. BTHome is a multi-manufacturer protocol,
used by compatible Shelly BLU devices and others; it is not a manufacturer.
The code decodes advertisements without making a GATT connection. These are
**decoders**, not encoders or device-control implementations.

## Start here

From the repository root (`NetworkMonitorLib`), the reproduction test command is:

```bash
dotnet test NetworkMonitor.csproj --no-restore \
  --filter 'FullyQualifiedName~Ble|FullyQualifiedName~VictronDeviceRecordDecoderTests|FullyQualifiedName~PlatformAwareCmdProcessorTests' \
  --nologo -p:IsTestProject=true \
  -p:BaseOutputPath=/tmp/nm-ble-sensors/bin/ \
  -p:IntermediateOutputPath=/tmp/nm-ble-sensors/obj/
```

Use .NET 10; the implementation was tested with SDK **10.0.401**, targeting
`net10.0`. A fresh checkout needs `dotnet restore NetworkMonitor.csproj` first,
with access to its configured NuGet feeds. `--no-restore` assumes valid restore
assets already exist. Restore/build the named project, rather than an arbitrary
solution or mobile project. The project is a library containing xUnit tests;
`-p:IsTestProject=true` ensures test execution. Build artifacts above go to `/tmp`,
while restore assets still use the project's configured location.

The regression filter passed **185 tests**, with zero failures/skips, when the
three protocols were implemented. Counts can change as tests are added. A build
alone, or a test command that discovers no tests, is not equivalent evidence.
No physical-device or Android/Windows platform build verification was performed
in that run. The final section describes those additional checks.

## Source map and implementation sequence

All paths below are relative to this guide's directory unless stated otherwise.

| File | Responsibility |
| --- | --- |
| [BlePayloadDecoderRegistry.cs](BlePayloadDecoderRegistry.cs) | `IBlePayloadDecoder`, `BlePayload`, immutable default protocol registry |
| [BleAdvertisementData.cs](BleAdvertisementData.cs) | Select manufacturer/service blocks from raw AD structures; normalize identifiers |
| [BleKeyParser.cs](BleKeyParser.cs) | Decode key text, then ask the selected protocol to validate key size |
| [VictronPayloadDecoder.cs](VictronPayloadDecoder.cs) | Victron framing, AES, record selection, diagnostic information |
| [VictronDeviceRecordDecoders.cs](VictronDeviceRecordDecoders.cs) | Registered device layouts, bit reader, signed/NA/scaled fields |
| [VictronSolarChargerRecordDecoder.cs](VictronSolarChargerRecordDecoder.cs) | Solar record and `IVictronRecordDecoder` interface |
| [RuuviPayloadDecoder.cs](RuuviPayloadDecoder.cs) | Ruuvi format 5 |
| [BTHomePayloadDecoder.cs](BTHomePayloadDecoder.cs) | BTHome v2 objects and AES-CCM verification |
| [BleBroadcastCmdProcessor.cs](../CommandProcessors/BleBroadcastCmdProcessor.cs) | Address-targeted, single-advertisement command |
| [BleBroadcastListenCmdProcessor.cs](../CommandProcessors/BleBroadcastListenCmdProcessor.cs) | Address-free, capped, best-effort listen command |
| [BleBroadcastConnect.cs](../BleBroadcastConnect.cs) | Endpoint arguments, structured metric selection and sample encoding |
| [BleBroadcastListenConnect.cs](../BleBroadcastListenConnect.cs) | Endpoint config to listen arguments |

To reconstruct the refactor from the former monolithic processors:

1. Extract their duplicated Victron framing, crypto, and packet checks into one
   protocol decoder. Keep existing CLI names and text labels used by Connect.
2. Introduce the registry and route both processors' decoding through it. Route
   platform callback acceptance and default company IDs through the same decoder.
3. Separate device records from Victron framing/crypto; register layouts by
   record type. Add bit-level parsing for fields not aligned to whole bytes.
4. Add Ruuvi and BTHome implementations to the default registry.
5. Make payload source, service UUID, and key validation protocol defaults.
   Leave explicit caller payload/service/company selections effective.
6. Apply callbacks' protocol acceptance checks to unencrypted-capable protocols
   even when there is no key. Keep key-first-byte checks specific to Victron.
7. Correct service matching on both platforms: Android matches service data;
   Windows inspects matching service-data sections without requiring a separate
   service-UUID list. Do not return unrelated service data when a UUID was given.
8. Add independent specification vectors, synthetic layout tests, malformed/key
   cases, and command-processor routing tests before relying on live scans.

`IBlePayloadDecoder` exposes `Format`, `ManufacturerId`, `RequiresKey`,
`DefaultPayloadMode`, `ServiceUuid`, `GetKeyError`, `Accepts`, `Describe`, and
`TryDecode`. Default payload mode is manufacturer data. `RequiresKey` means the
whole protocol requires a key, not that any particular packet is encrypted.
BTHome sets it to false and checks the packet's encryption bit during decoding.
The acceptance API currently receives the first key byte; only Victron uses it.
Acceptance is a scan prefilter, not cryptographic authentication.

## Payload and endpoint contracts

| Payload type | Bytes supplied to decoder |
| --- | --- |
| `manufacturer` | Selected manufacturer block, including its two-byte little-endian company ID |
| `service` | Selected service data; normally UUID stripped by platform code |
| `raw` | Complete advertising-data structures: length, AD type, AD bytes |
| `raw_input` | CLI `--raw_payload` override, interpreted as a protocol body; Ruuvi/BTHome also accept their identifier prefix |

A full raw advertisement is the AD payload, not an HCI dump or entire over-the-air
packet. Length counts the type plus data, excluding the length byte. Parsers must
check bounds and select the requested block rather than the first arbitrary
manufacturer/service block. Ruuvi and BTHome share normalization; Victron retains
its framing helper for compatibility with its existing fixtures.

The **Address** field selects a device for `blebroadcast`. **Password** becomes
`--key`; it is an advertisement encryption key, not a Bluetooth pairing PIN.
**Args** should contain `--format victron`, `--format ruuvi`, or `--format bthome`.
A bare `victron` or `--args victron` is not the command processor's syntax.
The endpoint uses Username as a legacy arguments fallback when Args is empty.

Keys may be hex (optional `0x`), base64, or UTF-8 text. Even-length hexadecimal
text is treated as hex first. Victron accepts empty or 16-byte keys at argument
validation; decoding needs 16 bytes. BTHome accepts empty or 16 bytes, then an
encrypted packet requires 16. Ruuvi rejects nonempty keys. Generic `aesgcm` and
`aesctr` modes still accept 16/24/32-byte keys. Their nonce/tag CLI options do not
specify Victron or BTHome crypto layouts.

Listen returns successful best-effort capture output even when an individual
packet cannot decode; inspect per-capture errors. Targeted protocol decoding
returns failure for decoding errors. The timeout/capture-limit semantics remain
in the existing processors. One key is supplied per command; there is no
per-device key resolver for a mixed encrypted fleet.

## Victron: sources, framing, and record layouts

Primary sources:

- [Extra Manufacturer Data PDF, 2022-12-14](https://communityarchive.victronenergy.com/storage/attachments/extra-manufacturer-data-2022-12-14.pdf): record headers, encryption, field tables, units, and NA values.
- [Victron Bluetooth advertising discussion](https://communityarchive.victronenergy.com/questions/187303/victron-bluetooth-advertising-protocol.html): vendor protocol context and advertisement-key discussion.
- [Victron staff's Orion XS layout and AC charger follow-up](https://community.victronenergy.com/t/orion-xs-12v-12v-50a-bluetooth-advertising-data/2183): Orion XS update from September 2024; AC charger follow-up from January 2025.

Cross-checks, not substitutes for vendor documentation:

- [victron-ble device implementations](https://github.com/keshavdv/victron-ble/tree/main/victron_ble/devices).
- [Battery Protect implementation](https://github.com/keshavdv/victron-ble/blob/main/victron_ble/devices/smart_battery_protect.py).
- [SmartLithium implementation](https://github.com/keshavdv/victron-ble/blob/main/victron_ble/devices/smart_lithium.py).

Manufacturer ID is `0x02E1` (`E1 02` on the wire). After stripping the company ID,
product advertisements start with `0x10`; skip four product-advertisement bytes to
reach the extra record. The decoder also accepts direct extra records.
The record header is: type (one byte), nonce (two bytes, little-endian), first key
byte (one byte), then encrypted data. Record type selects the device decoder;
address selects the transmitter, and key decrypts it. No user model argument is
needed. Product IDs are not currently mapped to exact models.

The current crypto code handles 1–16 encrypted bytes using one AES block:
place nonce bytes at positions 0–1 of a zeroed 16-byte counter block, encrypt it
with AES-128 ECB/no padding, then XOR the resulting keystream with ciphertext.
This implements the first block of the documented CTR operation. The first-key-
byte match is only a plausibility check; CTR has no authentication tag. Extending
beyond 16 bytes requires actual CTR counter advancement and additional tests;
the vendor spec permits future record extensions, so the current cap is a
known implementation limit rather than a permanent protocol rule.

| Record | Family | Minimum decrypted bytes |
| --- | --- | --- |
| `01` | Solar charger | 10; optional load current needs 12 |
| `02` | Battery monitor / SmartShunt family | 15 |
| `03` | Inverter | 11 |
| `04` | DC/DC converter | 10 |
| `05` | SmartLithium | 16 |
| `06` | Inverter RS | 12 |
| `08` | AC charger | 13 |
| `09` | Smart Battery Protect | 15 |
| `0A` | Lynx Smart BMS | 16 |
| `0B` | Multi RS | 14 |
| `0C` | VE.Bus | 13 |
| `0D` | DC energy meter | 11 |
| `0F` | Orion XS | 14 |

GX `07` is provisional in the PDF, and test record `00` is not implemented.
Unknown records can return plaintext when decoded directly/replayed, but live
protocol acceptance filters out unregistered types. Use raw capture to discover
them. The old solar-only acceptance helper now delegates to registered records;
existing private processor fixture hooks remain compatibility wrappers.

Most vendor start-bit offsets include the 32-bit header; subtract 32 for offsets
into decrypted plaintext. Orion XS staff's table already uses plaintext offsets.
The Battery Protect PDF has an inconsistent start-bit column: our layout starts
with consecutive state, output-state, and error bytes, matching the linked
implementation. AC charger compatibility still needs verification against actual
firmware. Do not treat a synthetic fixture as proof of that compatibility.

Read bits least-significant first. Compare NA against the raw unsigned pattern
**before** sign extension. Then sign-extend to the declared width and scale.
Battery monitors pack signed 22-bit current, 20-bit consumed Ah, and 10-bit SOC;
VE.Bus packs signed 19-bit power. Auxiliary mode selects starter/aux voltage,
midpoint voltage, Kelvin temperature converted to Celsius, or no reading.
SmartLithium cell values 0 and 126 represent bounds (`<2.61 V`, `>3.85 V`),
and 127 means unavailable; they must not be reported as exact cell voltages.

## Ruuvi: sources and RAWv2 behavior

Primary sources:

- [Advertisement framing](https://docs.ruuvi.com/communication/bluetooth-advertisements).
- [RAWv2 / format 5 specification and independent test vectors](https://docs.ruuvi.com/communication/bluetooth-advertisements/data-format-5-rawv2).
- [Vendor protocol repository](https://github.com/ruuvi/ruuvi-sensor-protocols/blob/master/broadcast_formats.md).

Default manufacturer ID is `0x0499`, wire bytes `99 04`. After removing that
prefix, require format byte `05` and at least 24 bytes. This implementation
supports RAWv2 only; other Ruuvi formats need additional decoders.
Multi-byte sensor values are **big-endian**, unlike the company ID and Victron
fields. Signed values use two's complement. Reserved unsigned maxima and signed
minima mean unavailable; power's battery/TX subfields have separate NA markers.

The decoder exposes environmental measurements, acceleration, battery/TX power,
movement and sequence counters, and embedded MAC. Follow the linked offset and
scaling table. Existing tests use the vendor page's valid, maximum, minimum, and
unavailable vectors; they are not generated by our decoder.

The valid body fixture is:

```text
0512FC5394C37C0004FFFC040CAC364200CDCBB8334C884F
```

Expected highlights: temperature 24.3 °C, humidity 53.49%, pressure 100044 Pa,
battery 2.977 V, TX 4 dBm, acceleration Y -0.004 g, sequence 205.
Leave Password empty. The output's RuuviTag label identifies the format/family,
not a product-ID lookup.

## BTHome / Shelly BLU: sources and v2 behavior

Primary sources:

- [BTHome v2 format, object table, and examples](https://bthome.io/format/).
- [BTHome encryption and known-answer fixture](https://bthome.io/encryption/).
- [BTHome-linked reference parser](https://github.com/Bluetooth-Devices/bthome-ble): implementation cross-check, not the normative specification.
- [Shelly BLE common features](https://shelly-api-docs.shelly.cloud/docs-ble/common/): vendor context for compatible Shelly devices.

Service UUID is `0xFCD2`, wire bytes `D2 FC`, canonical UUID
`0000fcd2-0000-1000-8000-00805f9b34fb`. Default payload source is `service`.
Do not require a separate advertised service-UUID list: service data can be the
only reference to the UUID. Android's filter uses `SetServiceData`; Windows
matches the UUID within `DataSections`. A requested service mismatch must not
return another service's bytes.

After the optional UUID prefix comes a device-info byte: upper three bits must
indicate version 2; bit 0 indicates encryption; bit 2 indicates trigger-based
updates. Remaining unencrypted data is a sequence of object ID/value pairs.
Widths, signedness, units, and factors come from the published table. Text/raw
objects carry lengths; command objects carry argument length plus an opcode;
button and dimmer events have their own layouts. Command objects are only
formatted, never executed. Repeated labels get numbered suffixes.

Unknown object IDs have no known width: stop parsing, retain earlier readings,
and print a diagnostic. Do not guess a width or search forward for plausible IDs.
Truncation, invalid binary values, or invalid UTF-8 fail decoding. BTHome v1 is
not supported. Object IDs are intended to be ascending; the current parser does
not enforce ordering. Firmware/type-ID objects are displayed numerically rather
than mapped to manufacturer models.

Encrypted body layout is `info | ciphertext | counter[4] | MIC[4]`.
Use AES-128 CCM with a four-byte authentication tag and no additional data for v2.
Nonce is 13 bytes: sender MAC in display byte order (6), `D2 FC` (2), info (1),
counter in transmitted little-endian order (4). Reject wrong keys, addresses,
and modified ciphertext/tag without returning decoded measurements.
Counters are displayed, but there is no persistent replay protection, duplicate
suppression, or rule rejecting unencrypted messages when a key is configured.

Independent published encryption fixture:

```text
MAC:       54:48:E6:8F:80:A5
Key:       231D39C1D7CC1AB1AEE224CD096DB932
Service:   D2FC41E445F3C9962B332211006C7C4519
Nonce:     5448E68F80A5D2FC4133221100
Plaintext: 02CA0903BF13
```

This is a public test key, not a real deployment credential. Expected readings
are temperature 25.06 °C and humidity 50.55%, with counter 1122867. The example
unencrypted service body `4002C40903BF13` instead yields temperature 25 °C.
The difference between `CA09` and `C409` is intentional.

## Replaying packets and verifying code without hardware

CLI argument strings below are passed to `RunCommand`; they are not executable
shell commands. `NetworkMonitor.csproj` builds a library, not a BLE console app.
Use the existing test harness or the normal agent command interface.

```text
--address AA:BB:CC:DD:EE:FF --format ruuvi --raw_payload 0512FC5394C37C0004FFFC040CAC364200CDCBB8334C884F
--address AA:BB:CC:DD:EE:FF --format bthome --raw_payload 4002C40903BF13
--address 54:48:E6:8F:80:A5 --format bthome --key 231D39C1D7CC1AB1AEE224CD096DB932 --raw_payload D2FC41E445F3C9962B332211006C7C4519
```

For unencrypted listen overrides, omit `--address`; listen does not accept that
argument. Encrypted BTHome override needs the targeted processor because an
address-free override supplies no sender MAC. Live listen supplies the actual
sender MAC from its capture callback.

Direct decoder replay example (inside a test/method, with
`using NetworkMonitor.Connection`):

```csharp
var decoder = BlePayloadDecoderRegistry.Default.Find("bthome")!;
var capture = new BlePayload("54:48:E6:8F:80:A5", "raw_input",
    Convert.FromHexString("D2FC41E445F3C9962B332211006C7C4519"));
bool ok = decoder.TryDecode(capture,
    Convert.FromHexString("231D39C1D7CC1AB1AEE224CD096DB932"),
    out string message, out string error);
```

To replay a full advertisement, set payload type `raw` rather than `raw_input`.
The BTHome published full AD fixture is
`0201060B094449592D73656E736F720A16D2FC4002C40903BF13`.
For unencrypted data pass `Array.Empty<byte>()` as the key.

Tests and their evidence:

| Test file, relative to Connection | What it establishes |
| --- | --- |
| [CommandProcessors/Tests/BleDecoderRegistryTests.cs](../CommandProcessors/Tests/BleDecoderRegistryTests.cs) | Registry selection/duplicate rejection, custom record registration, shared encrypted dispatch |
| [CommandProcessors/Tests/VictronDeviceRecordDecoderTests.cs](../CommandProcessors/Tests/VictronDeviceRecordDecoderTests.cs) | All 13 record families, direct/product frames, synthetic values, bit packing, signed/NA/clipped values, minimum lengths, unknown records, keys, manufacturer selection |
| [CommandProcessors/Tests/BleSensorDecoderTests.cs](../CommandProcessors/Tests/BleSensorDecoderTests.cs) | Independent Ruuvi/BTHome vectors, CCM known answer/tampering, events and repeated objects, malformed/unknown packets, protocol keys, both processor entry points |
| [CommandProcessors/Tests/BleBroadcastCmdProcessorTests.cs](../CommandProcessors/Tests/BleBroadcastCmdProcessorTests.cs) and [listen equivalent](../CommandProcessors/Tests/BleBroadcastListenCmdProcessorTests.cs) | Compatibility of original Victron packet extraction/recognition fixtures |
| [Tests/BleBroadcastConnectTest.cs](../Tests/BleBroadcastConnectTest.cs) and [listen equivalent](../Tests/BleBroadcastListenConnectTest.cs) | Endpoint argument/status/measurement behavior |
| [CommandProcessors/Tests/PlatformAwareCmdProcessorTests.cs](../CommandProcessors/Tests/PlatformAwareCmdProcessorTests.cs) | Platform dispatch behavior |

Victron's new value fixtures are synthetic, derived from the published layouts;
they are not captures from every physical model. The fixed plaintext hex fixtures
and separate `Put` helper exercise packed offsets independently of the production
bit reader. Its `Encrypt` helper uses public synthetic zero AES keys and nonce
`0x1234`, constructing ciphertext independently with AES ECB. A generated
round-trip alone is not sufficient evidence: keep known expected values and
independent vendor vectors where available.

A faster focused run can replace the full filter with
`FullyQualifiedName~BleSensorDecoderTests` or
`FullyQualifiedName~VictronDeviceRecordDecoderTests`.
Use the full regression filter after changing shared parsing, output, defaults,
callback selection, or key handling. Do not broaden to unrelated integration
suites unless the change affects them.

If the runner fails with `SocketException (13)` before running tests, its local
communication socket was blocked by the execution environment. Run in an
approved environment that permits the test socket; this is not a decoder failure.
If restore assets are missing, restore first. Do not interpret a platform build
failure, missing workload, or NuGet error as a failed packet vector.

## Platform and physical-device test procedure

These checks remain outstanding; record actual evidence when performed.

1. On an appropriately provisioned host, restore/build
   `NetworkMonitor-Maui-Android.csproj` or `NetworkMonitor-Maui.csproj` (Windows).
   Android needs its .NET workload and Android SDK; the Android project currently
   specifies `/usr/lib/android-sdk`. Use the Windows SDK/toolchain for Windows.
   Keep platform intermediates separate from the desktop test project when
   switching targets; do not reuse a single restored assets file across them.
2. On the device/host, enable Bluetooth and the normal application's scan
   permissions. Configure an actual BLE address and Args for each protocol.
   Enable Victron Instant Readout in VictronConnect and retrieve its advertisement
   key from product/Instant Readout information; follow current vendor guidance.
   Use the correct BTHome key if that sensor broadcasts encrypted packets.
3. Run targeted captures and compare multiple changing readings with the vendor
   app/display. Include negative current/power where applicable and NA states.
   Record model, firmware, platform/build, payload type, AD bytes, expected units,
   and corresponding displayed readings. Save only public or explicitly designated
   test keys in repository fixtures.
4. Run listen mode with a small capture limit and a finite timeout. Confirm the
   reported sender addresses, limit/timeout end reason, and per-capture errors.
   For encrypted tests, use devices with the supplied key; mixed devices with
   different keys cannot all decode in one current listen invocation.
5. Specifically test BTHome packets containing service data but no separate UUID
   list, plus advertisements with unrelated manufacturer/service blocks first.
6. Test wrong keys, wrong MAC in encrypted replay, malformed/truncated data,
   unknown record/object IDs, and sensors outside the supported format versions.
7. Add verified captures as fixtures with provenance and expected readings, then
   rerun the desktop regression suite and the changed platform build.

Linux supports the `--raw_payload` replay path through its processors but live
capture is still a `NotSupportedException` stub requiring BlueZ/D-Bus integration.
The mobile/window scanning code may use active BLE scanning, which requests scan
responses; absence of a GATT connection does not imply strictly passive radio
operation. Hardware-free tests do not exercise actual adapter permissions,
scan filtering, radio delivery, or platform AES-CCM availability.

## Adding another manufacturer or updating a record

1. Identify whether measurements are advertised at all; GATT-only measurements
   need a connection implementation rather than an advertisement decoder.
2. Find vendor framing/field/encryption documentation and record its date/version
   and exact links. Use implementation repositories as cross-checks; assess
   licensing before copying code. The implementations here use specifications and
   existing project code rather than adding those external libraries.
3. Write down company/service ID, packet discriminator/version, byte order,
   field widths/offsets, signedness, scale/offset, NA/clipping rules, key source,
   key size, nonce order, authentication tag, and minimum/optional lengths.
4. Implement `IBlePayloadDecoder` and register it once in the default registry.
   For a new Victron record use `IVictronRecordDecoder` or add a declarative layout
   to `VictronDeviceRecordDecoders.CreateDefaults()` instead. Keep protocol code
   out of platform callbacks.
5. Add a public known-answer vector or independently annotated capture. Test
   normal/boundary/unavailable values, truncated data, wrong identifiers, unknown
   types, repeated measurements, and encryption errors. Exercise targeted and
   listen command entry points. Unknown lengths must never be guessed.
6. Update help, source links, coverage/limits, and this guide; run the relevant
   regression filter and platform/hardware checks affected by the change.

Numeric monitoring now uses structured readings and explicit metric selection,
as documented below. Per-device keys, exact product catalogues, persistent
deduplication/replay checks and a shared platform scanning service remain
separate future work.

## Selecting a monitoring metric (.NET and ESP32)

No JSON configuration file is needed. Each decoder emits `BleDecodedPayload`
containing its diagnostic text and numeric `BleReading` values. Targeted command
results carry this object in the existing `ResultObj.Data` field. The endpoint
selects a reading directly; display formatting does not affect its recorded value.
The text-only decoder API remains available for discovery and existing callers.

Set endpoint `blebroadcast`, the device MAC, protocol key if required, and Args:

```text
--format bthome --metric temperature
--format ruuvi --metric humidity
--format victron --metric state_of_charge
--format victron --metric battery_voltage_2
--format bthome --metric temperature_2
```

Names are decoded field labels in lower case with punctuation/spaces replaced
by underscores. Examples: `AC in power` -> `ac_in_power`, `Cell 1 voltage` ->
`cell_1_voltage`, `TX power` -> `tx_power`, `PM2.5` -> `pm2_5`.
Repeated BTHome labels are numbered (`temperature`, `temperature_2`, etc.).
Victron channel/cell numbers remain part of the name. All numeric fields from
all 13 Victron families, Ruuvi RAWv2 and BTHome v2 are selectable, including
numeric state/flags, counts, booleans and timestamps. BTHome `button` and `dimmer`
store their protocol event codes; `dimmer_steps` stores the step count. Repeated
events are numbered too. Text, raw bytes, MAC addresses and firmware-version
strings are diagnostic fields, not numeric metrics. Missing/unavailable/clipped
readings and ambiguous names return `BLE Metric Error` for explicit selections.
Command diagnostics and selection errors list the packet's available numeric metric names. Authentication or
malformed-packet failures expose no structured readings.

One monitor records one metric. Add separate monitors for temperature, humidity,
etc., using the same device address. Do not use the listen endpoint for numeric
value selection; it remains a capture/discovery report.

### Automatic encoding and display

Users supply only `--format` and `--metric`; no user JSON or scale calculation is
required. `BleMetricCatalogue.cs` derives unit, physical range and resolution
from the decoder schemas. The shipped `metric-encodings-v2.json` is a reviewed
snapshot used by a regression test and by the C generator. It is development
metadata, not user configuration. There are distinct names for BTHome objects
whose units differ: `mass_kg`, `mass_lb`, `distance_mm`, `distance_m`, `volume_l`
and `volume_ml` (and occurrence suffixes).

For each protocol/metric, combine all supported layouts' physical ranges, use
`offset = min(0, minimum)`, and choose
`scale = max(finest_resolution, (maximum - offset) / 65534)`.
The shared sample/display contract is:

```text
sample = round_away_from_zero((physical_value - offset) / scale)
physical_value = sample * scale + offset
```

The valid sample range is 0..65534: 65535 is reserved for failed probes. Signed
values use a fixed negative origin. Protocol unavailable sentinels and clipped
cell voltages never become valid samples. Encoding rejects missing, ambiguous,
unavailable and out-of-range values. Maximum quantization error is half a scale
step: large 24/32-bit counters and wide signed ranges lose precision in the
existing 16-bit storage field. Supporting their full native precision would
require a wider sample schema. Flags/events are numeric codes, not labels.

A fixed status `BLE v2:<format>:<metric>` identifies the chosen metric; changing
values stay in the numeric field and diagnostics. Do not silently change shipped
v2 definitions. Introduce another version if changing an existing definition.
The compatibility work for historical readings is intentionally omitted: API
and charts apply the monitor's currently configured metadata to all its data.
Start a new dataset if a monitor's metric or encoding changes.

`EndpointMeasurement` / `EndpointMeasurementDefinition` now include `Offset`
(default zero). The .NET processor publishes these definitions through its
existing catalogue. `EndpointMeasurementSelector` selects by both protocol and
metric, including repeated BTHome objects. NetworkMonitorData applies registered
metadata or the shared built-in defaults (also used for ESP32 processors, which
do not publish a measurement catalogue). API monitor results carry Unit, Scale,
Offset; the React list, detail, charts and reports apply the inverse conversion.
Negative physical measurements remain visible; negative raw failure markers
remain missing data. Alerts continue to use their existing up/down policy.

Without `--metric`, existing implicit solar/receipt behavior remains. The .NET
advanced `--metric_scale` / `--metric_offset` overrides remain an escape hatch
with raw display metadata; automatic settings are the supported physical-unit
path. These manual overrides are not supported by ESP32.

### Reproduce, test and deploy

1. Read the manufacturer references earlier in this document. Add decoder fields
   and their range descriptors together; include NA values, signed widths,
   scale, units and repeated-object naming. New binary flags remain 0/1.
2. Review the versioned snapshot and run .NET tests. The snapshot regression
   prevents accidental changes to released definitions; do not refresh it just
   to silence a failing test.
3. Copy a reviewed snapshot to ESP32 `tests/fixtures/ble-metric-encodings-v2.json`,
   run `python3 tools/update-ble-metrics.py`, then `--check`. The C registry uses
   generated constants and a typed selected-reading sink, not parsed text.
4. Run .NET BLE/measurement tests and Data catalogue/PingInfo tests; run React
   `node --test src/components/dashboard/measurement.test.mjs` (Node 24) and
   `npm run build`. Run the ESP32 sanitizer native suite, tooling tests and
   signed `./tools/build-firmware.sh` as described in its documentation.
5. Apply NetworkMonitorData migration
   `20261004221000_EndpointMeasurementOffsets` through the normal deployment
   process before deploying Data/API code. Deploy the shared library and service,
   React UI, and rebuilt .NET/ESP32 processors. No live database, service or
   enrolled board is changed by the build/test steps.
6. Hardware acceptance: replay synthetic `--raw_payload` first, then use a real
   device with its correct broadcast key. Check temperature below zero, battery
   voltage/current, humidity, an unavailable field and a repeated BTHome object.
   Compare diagnostics to API/list/detail/chart/report values within half a scale
   step. Missing values must fail explicitly and never show scan duration as
   the selected metric. Inspect successful backend saves and acknowledgements.

.NET regression tests cover all 13 Victron layouts, published Ruuvi/BTHome
vectors and encryption errors, every encoding's minimum/maximum/midpoint,
reserved failure marker, immutable snapshot and selection independent of text.
Data tests cover persisted signed metadata and automatic selection; UI tests
cover negative physical values versus failure markers. C native tests exercise
production code with OpenSSL-backed test crypto; firmware uses ESP-IDF PSA.

### Physical-value output consumers

The existing API/frontend remains encoded: `physical = sample * Scale + Offset`.
`MeasurementConversion` provides value, total (`total*scale + count*offset`) and
standard-deviation (`deviation*scale`) conversions. Raw failures map to null;
negative physical values remain valid. LLM host summaries use
`PrintMonitorPingInfoProperties` with converted `measurement_average`,
`measurement_minimum`, `measurement_maximum`, metric and unit, even in compact
mode. Existing detailed round-trip aliases also contain physical values.

Downloads now serialize `PhysicalMeasurementResponse` instead of encoded
`HostResponseObj`: Address, Endpoint, Metric, Unit, Average, Minimum, Maximum,
Total, StandardDeviation and Readings (Timestamp, nullable Value, Status). These
values must not be scaled again. Report graphs use converted nullable samples
and physical-unit axes. Report-analysis LLM input uses the same physical DTO;
latency performance categories are omitted for non-millisecond measurements.
API DTOs and stored samples are unchanged. Redeploy Data and Service with the
updated shared library; no additional database migration is required.

### Analysis metadata in the same catalogue

`EndpointMeasurementMetadata`, published `EndpointMeasurementDefinition` and
persisted `EndpointMeasurement` now include Description (512 characters),
AnalysisKind (32) and AnalysisGuidance (2048), beside Unit/Scale/Offset/Type.
`NetConnect.Measurement` is the authoritative primary definition, with
`MeasurementVariants` for subtypes. Connects override Measurement with one
complete immutable definition. There are no separate Unit/Scale/Offset/Type or
analysis properties on NetConnect or INetConnect.

`EndPointTypeFactory` owns registration, UI text and construction. It configures
built-in duration definitions using endpoint identity, preserving distinctions
between endpoints sharing a class (such as HTTP variants). BLE Connects own
receipt/discovery definitions. Decoder schemas own per-metric meaning and kind:
BTHome ObjectSpec kinds/binary flags, Ruuvi metric ranges and Victron fields.
`BleMetricCatalogue` combines those meanings with the versioned encoding and
rejects conflicting meanings for the same format/metric.

`MeasurementAnalysisTemplates` supplies wording for declared kinds and units;
it has no endpoint or decoder lookup. Missing semantics receive an unspecified,
conservative fallback. Adding a metric must declare its kind beside its schema;
do not add another endpoint/metric inference table. Repeated BTHome objects
inherit their base definition. Legacy unversioned BLE aliases retain their
encoding and are explicitly marked unspecified.

The builder collects and validates every definition without running probes.
The published DTO and database entity are transport/storage representations,
not separate sources of measurement meaning. Data's catalogue uses one resolver
for both batched API decoration and report lookup: registered definitions take
precedence, otherwise built-in definitions apply. Both paths use the same Args
and legacy Username fallback and subtype selector.

Example for a new Connect (the values describe stored samples, not user-entered
scaling parameters):

```csharp
public override EndpointMeasurementMetadata Measurement =>
    MeasurementAnalysisTemplates.Metric("Enclosure temperature", "°C") with {
        Scale = 0.1,
        Offset = -40,
        AnalysisGuidance = "Describe temperature trends; operating limits are not configured."
    };
```

For decoder schemas, use `BleMetricRange.Field(..., kind: "counter")` for an
accumulating counter, or attach a complete `Meaning` definition when custom
wording is needed. BTHome object entries declare `Kind`; their Binary flag selects
state semantics. The catalogue checks that repeated schema definitions for one
format/metric agree. Keep numeric encoding fixtures unchanged when updating
analysis text. `PublishedVersionTwoDefinitionsNeverChange` checks all numeric
fields against the existing fixture, independently of wording.

ReportService resolves the selected complete definition and sends its Type,
Unit, Description, AnalysisKind and AnalysisGuidance as `measurement_context`
next to physical readings. LLMReportNode explicitly requests catalogue-aware
analysis. NetworkMonitorLLM `ReportDataToolsBuilder` contains the general system
prompt: preserve units, null is missing, negative values can be valid, use
provided timestamps and do not invent thresholds or safety/health limits. The
existing two-string output contract is unchanged. No threshold evaluation or
processor monitoring contract changes are included.

Apply migration `20261004234500_EndpointMeasurementAnalysis` before deploying
updated Data/Service and LLM code with the shared library. This adds the three
columns to the existing catalogue table; the earlier Offset migration remains
unchanged. No live migration or LLM API call is part of tests. Verify shared
EndpointMeasurement/BleMetric tests, Data catalogue/report tests and LLM
ReportDataToolsBuilderTests. Coverage includes all supported BLE definitions,
custom catalogue persistence and wire roundtrip, actual report input reaching
the orchestrator, and the system prompt's units/missing/counter/state rules.

### Report prompt ownership

Measurement-specific instructions belong in `AnalysisGuidance`. Standard
millisecond duration definitions use the original network performance-analysis
wording (spikes, timeouts, consistency, whole-period summaries and actionable
recommendations). Non-duration definitions use their own metric guidance. The
report system prompt supplies only the common input/output contract and routes
analysis to that guidance; it has no parallel per-kind analysis policy.
The report node retains its original report-generation prefix. Report payloads
use the current physical-value DTO: failures are null, timestamps describe actual
samples, and categories are used only when supplied. The original prompt's -1
failure marker and fixed two-hour assumptions do not describe this DTO and are
not reinstated as data-format claims.

For LLM reports, only millisecond duration definitions with optional
TimingRatingThresholds receive Categories. TimingMeasurementRating is the single
rating implementation for HTML and LLM output; no factory lookup or fallback
limits exist. Missing thresholds mean unrated, including scans, maintenance,
integrity checks, BLE and quantum/certificate-specific operations. HTTP HTTPS
request timings share the HTTP limits. The old limits remain heuristic defaults
for eligible timings and are passed to the LLM explicitly.

Dynamic definitions publish the optional threshold object; the database stores
its three boundaries in nullable TimingExcellent/TimingGood/TimingFair columns in
the same table. Apply migration 20261005001000_EndpointMeasurementTimingRatings.
Limits must be positive, finite, strictly increasing and attached only to duration
measurements in ms. Null disables ratings. These are not operational alert limits.
Unrated durations use completion-time/status guidance instead of latency ratings.
Extended .NET duration endpoints use fixed scales matching their timeout
multipliers: Nmap/Nmap vulnerability scans and BLE listen use 10 ms/sample;
crawl/daily crawl and HuggingFace keep-alive/wake use 20 ms/sample. Recording
uses integer elapsed milliseconds / scale; decoding multiplies by the same
Measurement.Scale. At a base 59000 ms timeout their extended timeout windows
encode below 59000, preserving headroom below the reserved 65535 marker.
Scale 10 supports 655340 ms and scale 20 supports 1310680 ms; quantization loses
less than one scale step. Configured timeouts must keep successful samples in
the representable range. Normal duration endpoints retain direct casts. There
is no clamping helper. Targeted BLE metric encodings are unchanged; its legacy
raw receipt fallback remains unclassified raw data.
