# AgentFlightRecorder.NET — Architecture Reference

---

## Package Overview

AgentFlightRecorder.NET is distributed as four NuGet packages with the following dependency graph:

```
AgentFlightRecorder.Core
        |
        +---> AgentFlightRecorder.Sinks.Jsonl
        |
        +---> AgentFlightRecorder.Replay
                    |
AgentFlightRecorder.Testing.Xunit ---> Core, Replay, Sinks.Jsonl
```

| Package | Responsibility |
|---|---|
| `AgentFlightRecorder.Core` | Events, interfaces, pipeline, serialization, redaction, integrity, DI wiring |
| `AgentFlightRecorder.Sinks.Jsonl` | `JsonlFileSink` (write) and `JsonlReplayStore` (read) backed by `.jsonl` files |
| `AgentFlightRecorder.Replay` | `ReplayIndex`, `ReplayLlmClient`, `ReplayToolExecutor` for deterministic replay |
| `AgentFlightRecorder.Testing.Xunit` | `FlightAssert`, `InMemorySink`, xUnit fixtures, golden-trace diffing |

`Core` has no project dependencies. All other packages depend on `Core`. `Testing.Xunit` depends on all three other packages and is intended only as a test dependency.

---

## Internal Pipeline

Events flow through a two-thread pipeline. The caller thread performs only cheap, lock-free work
(sequence allocation, timestamp capture, channel write). All I/O-bound work runs on the background
consumer thread.

```
Caller Thread(s)
  |
  |-- Interlocked.Increment(sequence)
  |-- TimeProvider.GetUtcNow()
  |-- JsonSerializer.SerializeToElement(payload)
  |-- FlightEvent constructed
  |
  v
Channel<FlightEvent>   (BoundedChannel, SingleReader=true, SingleWriter=false)
  |
  v
Background Consumer Task (single goroutine, FIFO)
  |
  +-- [1] Redaction      IRedactor.Redact(evt)         -- strips secrets from payload
  |
  +-- [2] Integrity      IIntegrityProvider.Compute()  -- attaches SHA-256 chain hash
  |
  +-- [3] Sink           IFlightSink.WriteAsync(evt)   -- persists the final event
```

In **synchronous mode** the channel is bypassed and the caller thread runs all three pipeline
stages inline before returning.

On sink failure in stage 3, the pipeline emits a `RecorderError` event through the same sink.
A re-entrancy guard prevents infinite recursion if the error event itself fails.

---

## Execution Modes

### Record

The default mode. All `RecordingLlmClient` and `RecordingToolExecutor` calls emit events into the
channel. The background consumer processes them through the redaction → integrity → sink pipeline.

### Replay

The `FlightRecorder` is configured with `FlightMode.Replay`. `ReplayLlmClient` and
`ReplayToolExecutor` intercept calls and serve recorded responses from the `ReplayIndex` instead of
invoking real backends. No new events are written to a sink during replay unless the caller
explicitly opts in.

### Passthrough

A true no-op mode. No channel, no background task, and no pipeline are created. All decorators
call through directly to their wrapped implementations without emitting any events. Useful for
disabling recording in production without changing call sites.

### Synchronous Mode

Enabled via `UseSynchronousMode()` on the builder. The channel and background consumer task are
skipped entirely. Each `EmitAsync` call runs the full redaction → integrity → sink pipeline on the
caller's thread before returning. Eliminates buffering latency at the cost of blocking the caller.
Primarily useful in test contexts where ordering guarantees are critical.

---

## Design Patterns

### Decorator

`RecordingLlmClient` and `RecordingToolExecutor` implement `ILlmClient` and `IToolExecutor`
respectively, each wrapping a real implementation passed at construction time.

```csharp
public sealed class RecordingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly FlightRecorder _recorder;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        await _recorder.EmitAsync(FlightEventTypes.LlmRequest, ...);
        var response = await _inner.CompleteAsync(request, ct); // delegate to real client
        await _recorder.EmitAsync(FlightEventTypes.LlmResponse, ...);
        return response;
    }
}
```

In Passthrough mode the decorators skip event emission and delegate directly, adding zero overhead.

### Pipeline

`EventPipeline` is an internal class that composes the three sequential processing stages.
Each stage receives the (potentially mutated) `FlightEvent` output of the previous stage.
Because `FlightEvent` is a sealed class with `init`-only properties, each mutating stage
constructs a new instance rather than modifying in place.

### Builder

`FlightRecorderBuilder` provides a fluent API for configuring the recorder through the
`IServiceCollection` DI container. It accumulates configuration state and resolves all
dependencies when `Build()` is called internally by `ServiceCollectionExtensions.AddFlightRecorder`.

```csharp
services.AddFlightRecorder(builder => builder
    .SetMode(FlightMode.Record)
    .UseSink(sp => new JsonlFileSink("run.jsonl", sp.GetRequiredService<ICanonicalJsonSerializer>()))
    .UseRedaction(r => r.AddApiKeyRedactor())
    .UseIntegrity()
    .ConfigureBackpressure(BackpressureStrategy.Drop, capacity: 2048));
```

---

## Backpressure Strategies

The channel is bounded. When the consumer cannot keep up with producers, one of three strategies
applies, selected via `ConfigureBackpressure`.

| Strategy | `BoundedChannelFullMode` | Behavior when full |
|---|---|---|
| `Drop` | `Wait` | `TryWrite` is attempted; if it returns `false` the event is discarded and a warning is logged |
| `Block` | `Wait` | `WriteAsync` awaits channel space; the caller is suspended until capacity is available |
| `Buffer` | `DropOldest` | Channel discards the oldest unconsumed entry to make room for the new event |

The default strategy is `Drop` with a capacity of 1024 events. A drain timeout (default 30 s)
limits how long `StopAsync` will wait for the consumer to flush buffered events at shutdown.

---

## Replay Matching

### InvocationKey

Every recorded LLM call or tool call is indexed by an `InvocationKey`:

```csharp
public sealed record InvocationKey(
    string Kind,         // "llm" or "tool"
    string Name,         // model name or tool name
    string ArgsHash,     // SHA-256 of canonical JSON of request arguments
    int Attempt,
    string? IdempotencyKey);
```

Entries are stored in a `Dictionary<InvocationKey, Queue<ReplayEntry>>`. Each queue provides FIFO
consumption: the first recorded occurrence of a given key is returned on the first replay hit,
the second on the second, and so on. This supports agents that call the same tool with the same
arguments multiple times.

### Strictness Levels

Three levels control how the replay engine matches incoming requests against the index.

| Level | Behavior |
|---|---|
| `Strict` | The next entry in global sequence order must exactly match the requested key. Any mismatch throws `ReplayMismatchException`. |
| `Lenient` | Dequeues from the keyed queue for the requested key regardless of global order. Tolerates reordering. |
| `Lookup` | Same dequeue behavior as Lenient; intended for pure read-only lookups where consumption order is irrelevant. |

### Index Construction

The `ReplayIndex` is built from the ordered sequence of recorded events. Request events
(`LlmRequest`, `ToolCallStarted`) are paired with their corresponding response events
(`LlmResponse`/`LlmError`, `ToolCallCompleted`/`ToolCallFailed`) by tracking pending requests
and matching the most recent unmatched request to each response.

For LLM entries, `argsHash` is computed over the typed `LlmRequestPayload` object to guarantee
consistency with how the live replay client hashes incoming requests.

---

## Security Architecture

### Redaction

Redaction runs as the first pipeline stage, before integrity computation and persistence.
This ordering ensures that sensitive data is never present in the final hash chain or stored output.

Built-in redactors:

- `ApiKeyRedactor` — scrubs Bearer tokens, `sk-` prefixed OpenAI keys, and `api_key=` patterns using compiled regular expressions
- `PiiRedactor` — scrubs common PII patterns from string values in payloads
- `CompositeRedactor` — chains multiple `IRedactor` instances in registration order

Custom redactors are added via `AddCustomRedactor(IRedactor)` on the `RedactionBuilder`.

### Hash Chain Integrity

`Sha256IntegrityProvider` maintains a per-run forward hash chain. For each event:

```
input  = UTF-8(prevHash) || UTF-8(canonicalJson(event, integrity=null))
hash   = SHA-256(input)
stored = FlightIntegrity { prevHash, hash, algorithm="SHA-256" }
```

The first event in a run uses an empty string as `prevHash`. Any tampering with event content or
ordering is detectable by replaying the chain and comparing hashes.

### HMAC Run Signature

When `UseHmacSigning(byte[] signingKey)` is configured, the `RunCompleted` event carries a
`runSignature` field computed as:

```
runSignature = HMAC-SHA256(signingKey, UTF-8(finalChainHash))
```

This binds the integrity of the entire run to a secret key, allowing a consumer to verify that
the trace was produced by a trusted recorder and has not been replayed or forged.

### Observability

`FlightRecorder` and `EventPipeline` both create `Activity` instances via a shared
`ActivitySource` named `"AgentFlightRecorder"`. This integrates natively with OpenTelemetry and
.NET's `System.Diagnostics` tracing infrastructure. Event type and sequence number are attached
as activity tags.

---

## Extension Points

### Custom Sink

Implement `IFlightSink` to persist events to any backend (database, message queue, cloud storage, etc.):

```csharp
public interface IFlightSink : IAsyncDisposable
{
    ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
}
```

Register via `builder.UseSink(sp => new MyCustomSink(...))`.

### Custom Redactor

Implement `IRedactor` to apply domain-specific scrubbing logic:

```csharp
public interface IRedactor
{
    FlightEvent Redact(FlightEvent evt);
}
```

Register via `builder.UseRedaction(r => r.AddCustomRedactor(new MyRedactor()))`. Multiple
redactors are composed into a `CompositeRedactor` and applied in registration order.

### Custom State Serializer

Implement `IStateSerializer` to control how agent state objects are serialized into
`StateCheckpoint` payloads:

```csharp
public interface IStateSerializer
{
    JsonElement Serialize(object state);
}
```

Pass the serializer instance to `recorder.CheckpointAsync(name, state, mySerializer, ct)`.
This allows callers to use custom converters, omit sensitive fields, or apply schema versioning
to checkpoint state independently of the main event schema.
