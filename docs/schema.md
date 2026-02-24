# AgentFlightRecorder.NET — Event Schema Reference

Schema version: `1.0`

---

## Event Envelope

Every recorded event is wrapped in a `FlightEvent` envelope. The `type` field acts as a discriminator,
and the `payload` field carries the per-type structured data.

| Field | Type | Required | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | Always `"1.0"` |
| `runId` | string (GUID) | yes | Unique identifier for the agent run |
| `eventId` | string (GUID) | yes | Unique identifier for this event |
| `sequence` | long | yes | Monotonically increasing counter per run (1-based) |
| `timestampUtc` | DateTimeOffset | yes | UTC wall-clock time when the event was emitted |
| `traceId` | string | yes | W3C-compatible trace identifier |
| `spanId` | string | yes | W3C-compatible span identifier |
| `parentSpanId` | string | no | Parent span identifier; omitted for root spans |
| `type` | string | yes | Event type discriminator (see Event Types below) |
| `payload` | object | yes | Typed payload; shape determined by `type` |
| `integrity` | FlightIntegrity | no | Hash chain entry; present when integrity is enabled |

### Example envelope (JSONL line)

```json
{"eventId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","integrity":{"algorithm":"SHA-256","hash":"a3f1...","prevHash":"00000..."},"parentSpanId":null,"payload":{"model":"gpt-4o","provider":"openai","runName":null},"runId":"d290f1ee-6c54-4b01-90e6-d701748f0851","schemaVersion":"1.0","sequence":1,"spanId":"0af7651916cd43dd8448eb211c80319c","timestampUtc":"2026-02-24T10:00:00.000+00:00","traceId":"4bf92f3577b34da6a3ce929d0e0e4736","type":"RunStarted"}
```

---

## FlightIntegrity

Present on each event when `UseIntegrity()` is configured on the builder. Forms a forward-linked
SHA-256 hash chain over the canonical JSON representation of consecutive events.

| Field | Type | Description |
|---|---|---|
| `prevHash` | string | Hash of the preceding event; empty string for the first event |
| `hash` | string | SHA-256 hex digest of `prevHash + canonicalEventJson` (lowercase) |
| `algorithm` | string | Always `"SHA-256"` |

The hash input is `UTF-8(prevHash) || UTF-8(canonicalJson(event without integrity field))`.

---

## Event Types

### RunStarted

Emitted once at the start of a run.

| Field | Type | Required | Description |
|---|---|---|---|
| `runName` | string | no | Human-readable name for the run |
| `metadata` | object (string→string) | no | Arbitrary key-value annotations |

```json
{"runName":"invoice-processing","metadata":{"environment":"production"}}
```

---

### RunCompleted

Emitted once at the end of a run.

| Field | Type | Required | Description |
|---|---|---|---|
| `status` | string | yes | Completion status; e.g. `"Completed"` |
| `runSignature` | string | no | HMAC-SHA256 hex signature over the final chain hash; present when `UseHmacSigning()` is configured |
| `metadata` | object (string→string) | no | Arbitrary key-value annotations |

```json
{"metadata":null,"runSignature":"9f86d0...","status":"Completed"}
```

---

### SpanStarted

Emitted when a named span begins.

| Field | Type | Required | Description |
|---|---|---|---|
| `spanName` | string | yes | Logical name of the span |
| `kind` | string | yes | Span kind: `"Internal"`, `"Planning"`, `"Acting"`, or `"ToolExecution"` |

```json
{"kind":"Planning","spanName":"ReasonAboutNextStep"}
```

---

### SpanCompleted

Emitted when a named span ends.

| Field | Type | Required | Description |
|---|---|---|---|
| `spanName` | string | yes | Logical name of the span |
| `kind` | string | yes | Span kind (same values as SpanStarted) |
| `durationMs` | long | yes | Wall-clock duration of the span in milliseconds |

```json
{"durationMs":142,"kind":"Planning","spanName":"ReasonAboutNextStep"}
```

---

### LlmRequest

Emitted immediately before an LLM call is dispatched.

| Field | Type | Required | Description |
|---|---|---|---|
| `provider` | string | yes | Provider identifier, e.g. `"openai"` |
| `model` | string | yes | Model identifier, e.g. `"gpt-4o"` |
| `messages` | array | yes | Message array in the provider's native format |
| `settings` | object (string→any) | no | Optional inference parameters (temperature, max_tokens, etc.) |
| `toolDefinitions` | array | no | Tool/function definitions available to the model |

```json
{"messages":[{"content":"Summarize this document.","role":"user"}],"model":"gpt-4o","provider":"openai","settings":{"temperature":0.2},"toolDefinitions":null}
```

---

### LlmResponse

Emitted after a successful LLM call.

| Field | Type | Required | Description |
|---|---|---|---|
| `provider` | string | yes | Provider identifier |
| `model` | string | yes | Model identifier |
| `content` | any | yes | Raw response content from the provider |
| `toolCalls` | array | no | Tool call requests returned by the model |
| `usage` | TokenUsage | no | Token consumption breakdown |

`TokenUsage` fields: `promptTokens` (int), `completionTokens` (int), `totalTokens` (int).

```json
{"content":"The document discusses...","model":"gpt-4o","provider":"openai","toolCalls":null,"usage":{"completionTokens":85,"promptTokens":312,"totalTokens":397}}
```

---

### LlmError

Emitted when an LLM call throws an exception.

| Field | Type | Required | Description |
|---|---|---|---|
| `provider` | string | yes | Provider identifier |
| `model` | string | yes | Model identifier |
| `errorType` | string | yes | Exception type name |
| `errorMessage` | string | yes | Exception message |

```json
{"errorMessage":"Request timed out after 30s","errorType":"TaskCanceledException","model":"gpt-4o","provider":"openai"}
```

---

### ToolCallStarted

Emitted immediately before a tool is invoked.

| Field | Type | Required | Description |
|---|---|---|---|
| `toolName` | string | yes | Registered name of the tool |
| `args` | object | yes | Arguments passed to the tool |
| `argsHash` | string | yes | SHA-256 hex digest of the canonical JSON of `args` |
| `attempt` | int | yes | Attempt number; 1 for the first attempt |
| `idempotencyKey` | string | no | Caller-supplied idempotency key |

```json
{"args":{"path":"/data/invoices","recursive":true},"argsHash":"b94f6f...","attempt":1,"idempotencyKey":null,"toolName":"ListFiles"}
```

---

### ToolCallCompleted

Emitted after a tool call succeeds.

| Field | Type | Required | Description |
|---|---|---|---|
| `toolName` | string | yes | Registered name of the tool |
| `args` | object | yes | Arguments passed to the tool |
| `argsHash` | string | yes | SHA-256 hex digest of the canonical JSON of `args` |
| `result` | any | yes | Return value from the tool |
| `durationMs` | long | yes | Wall-clock execution time in milliseconds |
| `attempt` | int | yes | Attempt number |
| `idempotencyKey` | string | no | Caller-supplied idempotency key |

```json
{"args":{"path":"/data/invoices","recursive":true},"argsHash":"b94f6f...","attempt":1,"durationMs":38,"idempotencyKey":null,"result":["invoice_001.pdf","invoice_002.pdf"],"toolName":"ListFiles"}
```

---

### ToolCallFailed

Emitted when a tool call throws an exception.

| Field | Type | Required | Description |
|---|---|---|---|
| `toolName` | string | yes | Registered name of the tool |
| `args` | object | yes | Arguments passed to the tool |
| `argsHash` | string | yes | SHA-256 hex digest of the canonical JSON of `args` |
| `errorType` | string | yes | Exception type name |
| `errorMessage` | string | yes | Exception message |
| `stackTrace` | string | no | Full stack trace; may be omitted |
| `durationMs` | long | yes | Wall-clock time elapsed before failure |
| `attempt` | int | yes | Attempt number |
| `idempotencyKey` | string | no | Caller-supplied idempotency key |

```json
{"args":{"path":"/missing"},"argsHash":"e3b0c4...","attempt":1,"durationMs":5,"errorMessage":"Directory not found","errorType":"DirectoryNotFoundException","idempotencyKey":null,"stackTrace":null,"toolName":"ListFiles"}
```

---

### StateCheckpoint

Emitted when `CheckpointAsync` is called to snapshot agent state.

| Field | Type | Required | Description |
|---|---|---|---|
| `checkpointName` | string | yes | Logical label for the checkpoint |
| `state` | any | yes | Serialized state value (shape defined by the caller's `IStateSerializer`) |
| `metadata` | object (string→string) | no | Arbitrary key-value annotations |

```json
{"checkpointName":"AfterCategorization","metadata":null,"state":{"completedItems":14,"pendingItems":3}}
```

---

### Annotation

Emitted by the caller to attach a free-form message to the trace.

| Field | Type | Required | Description |
|---|---|---|---|
| `message` | string | yes | Human-readable annotation text |
| `tags` | object (string→string) | no | Arbitrary key-value tags |

```json
{"message":"Retrying after rate limit backoff","tags":{"retryCount":"2"}}
```

---

### RecorderError

Emitted by the pipeline itself when a sink write fails. Acts as an internal diagnostic event.

| Field | Type | Required | Description |
|---|---|---|---|
| `errorType` | string | yes | Exception type name of the sink failure |
| `errorMessage` | string | yes | Exception message |
| `sourceEventSequence` | long | no | Sequence number of the event that failed to persist |
| `sourceEventType` | string | no | Type discriminator of the event that failed to persist |

```json
{"errorMessage":"Disk quota exceeded","errorType":"IOException","sourceEventSequence":47,"sourceEventType":"LlmResponse"}
```

---

## InvocationKey

Used by the replay engine to match recorded invocations against live replay requests.

| Field | Type | Description |
|---|---|---|
| `kind` | string | Invocation category: `"llm"` or `"tool"` |
| `name` | string | Model name (LLM) or tool name (tool call) |
| `argsHash` | string | SHA-256 hex digest of the canonical JSON of the request arguments |
| `attempt` | int | Attempt number |
| `idempotencyKey` | string | Optional caller-supplied idempotency key |

For LLM invocations the `argsHash` is computed over the typed `LlmRequestPayload` object
(not a raw `JsonElement`) to ensure consistent hashing across serialization boundaries.

---

## Canonical JSON Rules

All event payloads and hash inputs use a deterministic JSON encoding:

- **Property ordering**: alphabetical (A-Z) at every nesting level, enforced by `SortedPropertyJsonConverterFactory`
- **Whitespace**: none (compact)
- **Property naming**: camelCase
- **Null fields**: omitted (`DefaultIgnoreCondition.WhenWritingNull`)
- **Number handling**: strict (no quoted numbers)
- **Enum values**: camelCase strings

These rules ensure that two logically identical events always produce the same byte sequence,
which is required for deterministic hash-chain integrity and replay key matching.
