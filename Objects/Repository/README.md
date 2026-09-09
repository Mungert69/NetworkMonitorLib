# RabbitMQ message security policy

`MessageSecurityPolicyRegistry` is the authoritative map of protection requirements
for protected RabbitMQ operations. It is deliberately located in `NetworkMonitorLib`
so every publisher and listener uses the same policy definitions.

## How the registry is used

The registry describes an operation, its target trust domain, and its protection:

- `MlDsa` — asymmetric ML-DSA signatures for messages crossing a backend or agent
  control boundary.
- `BackendHmac` — the backend-only HMAC trust domain for high-volume internal events.
- `LlmHmac` — the separate HMAC trust domain used by LLM services.

Routes can be exact, operation-prefix routes (for example, an operation followed by a
processor AppID), or the special query-index-result route. Policies also record whether
the payload is signed without a body (`IsPayloadFree`), or contains an email batch that
needs the decorator's special handling (`WrapsEmailBatch`). Dynamic targets are allowed
only for explicitly marked policies such as query-index requests.

`BackendSignedRabbitRepo`, `BackendHmacRabbitRepo`, and `LlmHmacRabbitRepo` use
`TryResolve` to decide whether and how to sign an outgoing message. Consumers validate
the same operation/target/protection pairing before invoking the cryptographic verifier.
An unregistered operation or a protection mismatch is rejected rather than silently
falling back to another mechanism.

The registry is a routing/policy layer; the ML-DSA and HMAC service classes remain
generic cryptographic primitives. This keeps them useful for isolated tests and for
canonicalization while ensuring production RabbitMQ paths cannot diverge.

## Security model

ML-DSA is the preferred protection for control-plane messages and messages addressed to
agents, Data, Alert, or processor-state consumers. Publishers hold signing keys and
consumers need only the corresponding public verification key. A compromised consumer
therefore cannot forge messages for other consumers.

Backend HMAC is retained only for the existing backend-internal operations where all
participants already belong to one trusted backend boundary and message volume makes a
shared symmetric secret operationally useful. It must not be added to agent-facing
operations: an agent that can verify a shared backend HMAC key could also forge backend
messages.

LLM HMAC is a separate key and trust domain from backend HMAC. It protects the existing
LLM service/session routes, but does not make those messages valid in the backend or
agent domains.

The processor agent's `processorAuthKey` exchange uses its dedicated AuthKey ML-DSA
verification path. The remaining agent operations retain their existing AMQP-only or
specialized behavior; adding a new protection requires an explicit policy decision.

## Adding or changing an operation

1. Add one policy entry here, including its route form and target domain.
2. Update the publisher payload type if it does not implement `IBackendSignedMessage`.
3. Add the matching listener-side `Requires` check before verification.
4. Add route-resolution and positive/negative protection tests.
5. Build dependent repositories with a single MSBuild node (`-m:1`) and review the
   security behavior in the relevant service README.

Do not maintain a second operation allowlist in an individual repository. If an operation
is intentionally unauthenticated, document that decision beside its listener rather than
silently omitting it from a protected route.
