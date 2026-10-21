# Chat setup protocol and service messages

`LLMServiceObj` is the internal RabbitMQ payload used by the LLM worker and
routing backend. It is not the browser/Razor WebSocket wire format.

## Relevant internal fields and events

| Field/event | Role |
| --- | --- |
| `RequestSessionId` | Backend conversation/request routing key; the WebSocket controller includes the user ID when creating it. |
| `SessionId` | Worker runner-session identity returned during startup. |
| `LLMRunnerType` | Selected runner implementation/type. |
| `LlmChainStartName` | Chain identity derived from the stack or `SourceLlm`; the backend uses it to recognize primary startup acknowledgements. |
| `IsSystemLlm` | Selects system-LLM startup handling rather than client readiness. |
| `LlmMessage` | Client output or notification payload for the message-output path. |
| `llmStartSession{expert}` | Backend request to start or revalidate a worker session. |
| `llmServiceMessage` | Worker output/status event. |
| `llmServiceStarted` | Worker startup acknowledgement; the backend registers the session and conditionally sends readiness. |

The internal events use their existing authentication/signature checks.
The WebSocket client receives text payloads, not these RabbitMQ envelopes.

## Client-visible contract

Each backend WebSocket send is a complete text message. Clients must classify
that message before accumulating conversation output. .NET receivers must
reassemble transport fragments before classification.

- Exactly `</llm-ready>` is a readiness control event.
- Prefixes `</llm-success>`, `</llm-info>`, `</llm-warning>`, and `</llm-error>`
  identify notification text, separate from the conversation.
- `<history-display-name>...</history-display-name>` and
  `<function-data>...</function-data>` carry structured data.
- `<end-of-line>` controls the processing indicator, not setup readiness.
- Ordinary conversation payloads are accumulated as chat text after control
  and structured messages have been classified.

These markers are literal strings, not balanced XML/HTML. In particular, the
worker's startup greeting is a success notification.

`llm-ready` means the primary startup acknowledgement has been processed and
the backend session registered. It does not mean every expert is ready,
notifications are finished, or history replay has completed. The two output
events use separate backend queues, so startup notifications may arrive before
or after readiness. Neither notification order nor concatenated raw captures
should be used to infer that readiness was emitted before runner startup.

Existing clients release an explicitly queued history request on readiness.
Repeated readiness does not instruct them to clear chat state or request
history again. Automatic reconnect is separate from an explicit chat reset.

There are no setup-attempt/sequence fields, universal 20-second setup timer, or
separate `socket-ready` message in the current protocol. A failed startup can
produce an error without readiness. There is no missed-response-chunk recovery.
