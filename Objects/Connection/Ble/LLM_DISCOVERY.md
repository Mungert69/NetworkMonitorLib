# BLE configuration discovery for monitoring tools

`get_available_endpoints` keeps its compact agent-to-endpoint lists when called
without documentation filters. Optional filters progressively select documentation:

1. `endpoint=blebroadcast`: common arguments and formats.
2. Add `format=victron`: valid record names. Ruuvi and BTHome return metric lists.
3. Add `record=solar_charger`: only metrics from the solar charger decoder.
4. Add `metric=yield_today`: units, meaning, interpretation guidance and an example.

`record` is a discovery filter, not a host argument. Device advertisements select
their record type. Metrics are supported capabilities, not a guarantee that an
individual device advertises a valid value. Generic raw/AES payload formats have
no named physical metrics. Listen mode returns raw discovery captures, not decoded
physical readings. Keys belong in the host password, not discovery calls.

Metric membership and meanings are read from decoder definitions and the shared
BLE catalogue. `EndpointDocumentationCatalogue` routes to endpoint providers; BLE endpoint
providers route to injected format providers. New providers can be registered at
the composition root without changing routing or shared metric formatting.
Configuration examples and protocol-specific guidance belong to providers.
Argument help, types and defaults are projected directly from
`BleCommandArguments`, the same fresh schemas used by command parsers.
Discovery describes backend support; agent endpoint availability is checked but
individual firmware versions may support a narrower set of protocols or options.

## Interactive data responses

Compact `get_host_data` output is unchanged. With `detail_response=true`, the LLM
response adds `measurement_contexts`, a dictionary keyed by response-local IDs.
Each row has `measurement_context_id`. Definitions are deduplicated using complete
immutable metadata equality, including encoding and timing ratings; sharing a
metric name alone does not merge definitions. Existing values, units and metric
fields remain usable independently. Values are already in physical units.

The secondary tool result preserves its `dataJson` array and places
`measurement_contexts` beside it. Consumers retaining context references must
retain the sibling dictionary as well. Full interpretation is not repeated on
individual rows. Missing agent metadata produces conservative unspecified context.

The catalogue reader attaches the selected definition to a NotMapped,
serialization-ignored property for this projection. No database migration is
needed. ReportService retains its existing selection, `measurement_context`
(singular), physical conversion and analysis projection. Interactive deduplication
does not participate in reports or change BLE collection/averaging.

## Object projection and response contracts

`LlmMeasurementRowProjector` builds the shared typed row and date range. The legacy string
formatter serializes that object; the batch projector attaches context IDs to the
same row type without a JSON round trip. Compact and detailed row DTOs preserve
the existing optional-field behaviour. Named context, batch, date-range and tool
result contracts keep both response paths type checked. Both response envelopes
reuse the batch's rows and context dictionary; neither recalculates measurements.
ReportService continues to use its independent existing report projection.
