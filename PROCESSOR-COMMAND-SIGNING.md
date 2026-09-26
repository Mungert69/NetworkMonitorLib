# Processor command signatures

ML-DSA-65 remains the default. Existing .NET payloads, signatures, topics,
trust anchor and verification are unchanged. `ProcessorObj.IsQuantumCapable`
selects the signer: `true` uses ML-DSA; `false` uses ECDSA. Its C# initializer
and database migration default to `true`, including existing records.

Processor `appsettings.json` has a root-level `IsQuantumCapable` boolean.
Omitting it defaults to `true`. Keep it true for .NET processors; set it to
false in the configuration used to provision ESP32. The provisioning script
copies it into the device configuration. .NET registration carries the value
in ProcessorObj; ESP32 readiness carries it after AuthKey authentication.
Data persists it and distributes changes through the existing signed
processor-state messages. Signers read current shared processor state; no
AppID lists or transport inference remain. Unknown processors retain the
ML-DSA default; C-only OTA commands fail closed until false is recorded.

Apply the `ProcessorIsQuantumCapable` Data migration before deploying the
updated backends. Rebuild all four backends against the updated shared library.

Add at the root of **Data, Service and Scheduler** appsettings. Alert now
supports the same configuration as well:

```json
"ProcessorCommandSigning": {
  "PrivateKeyPath": "/app/keys/processor-command-ecdsa-p256-private.pem"
}
```

Mount the separate P-256 private key read-only on those containers:

```yaml
volumes:
  - ${FILES_DIR}/processor-command-ecdsa-p256-private.pem:/app/keys/processor-command-ecdsa-p256-private.pem:ro
```

The private key was created on the development machine at
`~/code/securefiles/processor-command-ecdsa-p256-private.pem` (mode 600).
Transfer it securely outside Git and container builds; make it readable only
by the runtime service account and administrators. Never relax it to public
readability to accommodate a non-root container. Its public half is pinned in
ESP32 `esp32/main/command-signing-public.pem`. Keep the existing ML-DSA mounts;
they still protect .NET processors and backend-to-backend requests. This key
is also distinct from the firmware application signing key. Alert's current
processor operations are outside the .NET signing policy, so it does not read
either signing private key for those messages. Its shared signing pipeline
will require the appropriate key if it publishes a protected operation in the
future. Adding the pipeline does not extend the operation policy or require
new key mounts for Alert's existing behavior.

Quantum-capable processors do not cause ECDSA key-file reads. For a processor
recorded as non-quantum-capable, a missing/invalid key, wrong curve or oversized protected
message fails publishing rather than falling back to unsigned/ML-DSA.
Use v2 routing for ESP32. Provision the capability field and 0.1.4+ firmware
together; older C firmware cannot consume the new envelopes. The running
test emulator has deliberately not been upgraded automatically.

## Responsibilities

- `MessageSecurityPolicyRegistry`: operation protection policy shared with
  existing .NET behavior; AuthKey's separate verification and C OTA operations
  are included by `RequiresProcessorSignature` without changing ML-DSA rules.
- `BackendSignedRabbitRepo`: routes publishing through the configured profile.
- `IProcessorCommandSigner` / `EcdsaProcessorCommandSigner`: processor-state
  lookup and P-256 SHA-256 signing; original ML-DSA signer remains unchanged.
- `BackendMessageSignaturePayload`: shared length-prefixed operation, target
  and serialized message bytes. Existing ML-DSA byte format is preserved.
- ESP32 `command_security.c`: bounded envelope decoding, signature verification
  and operation/target binding before parsing authenticated JSON. Cross-language
  tests assert its operation-policy parity with the registry.

The device verification algorithm is pinned, not selected by incoming commands. The signed
envelope carries exact bytes so the C device never has to reproduce .NET JSON
serialization. Unsigned connect/wakeup, removePingInfos and alert operations
remain unsigned to match .NET coverage. This is not blanket message signing.
Ordinary commands retain .NET's replay semantics; OTA keeps separate expiry,
request-ID and firmware-version checks. A shared backend key authenticates a
trusted signer, not uniquely the Data service. Secure Boot is still disabled.
