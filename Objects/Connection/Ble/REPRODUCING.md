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
| [BleBroadcastConnect.cs](../BleBroadcastConnect.cs) | Endpoint config to arguments; existing text-to-metric extraction |
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

Results currently use text, and `BleBroadcastConnect` extracts its existing metric
subset using regex into unsigned-short storage. New decoder readings do not
implicitly become selectable numeric monitoring metrics; signed values can be
clamped by the existing Connect. Structured readings, expanded metric selection,
per-device keys, exact product catalogues, persistent deduplication/replay checks,
and a shared platform scanning service are separate future work.
