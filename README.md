# AgentFlightRecorder.NET

[![CI](https://github.com/relusion/AgentFlightRecorder.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/relusion/AgentFlightRecorder.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A .NET 8 library for deterministic, replayable flight recording of agentic AI runs. Wrap your LLM client and tool executor with recording decorators to capture a structured JSONL trace of every LLM call, tool invocation, and state checkpoint. Replay the trace offline — no real API calls — for deterministic testing and CI golden trace regression.

## Packages

| Package | Purpose |
|---|---|
| `AgentFlightRecorder.Core` | Events, pipeline, redaction, integrity, DI extensions |
| `AgentFlightRecorder.Sinks.Jsonl` | JSONL file sink and replay store |
| `AgentFlightRecorder.Replay` | Deterministic replay clients and executor |
| `AgentFlightRecorder.Testing.Xunit` | xUnit helpers, fixtures, and in-memory sink |
| `AgentFlightRecorder.Adapters.SemanticKernel` | Semantic Kernel adapter for recording and replay |

Install via NuGet:

```
dotnet add package AgentFlightRecorder.Core
dotnet add package AgentFlightRecorder.Sinks.Jsonl
dotnet add package AgentFlightRecorder.Replay
dotnet add package AgentFlightRecorder.Testing.Xunit  # test projects only
```

## Quick Start: Record Mode

```csharp
var sink = new JsonlFileSink("traces/run-123.jsonl", new CanonicalJsonSerializer());
var options = new FlightRecorderOptions { Mode = FlightMode.Record, Sink = sink, SynchronousMode = true };
await using var recorder = new FlightRecorder(options);
await recorder.StartAsync();

var llm = new RecordingLlmClient(realLlmClient, recorder, new CanonicalJsonSerializer());
var tools = new RecordingToolExecutor(realToolExecutor, recorder, new CanonicalJsonSerializer());

// ... run agent loop using llm and tools ...

await recorder.StopAsync();
```

`RecordingLlmClient` and `RecordingToolExecutor` are transparent decorators. Swap them in wherever your agent loop accepts `ILlmClient` and `IToolExecutor` — no other changes required.

## Quick Start: Replay Mode

```csharp
var serializer = new CanonicalJsonSerializer();
var events = await new JsonlReplayStore("traces/", serializer).LoadFromFileAsync("traces/run-123.jsonl");
var index = new ReplayIndex(events, serializer);

var replayLlm = new ReplayLlmClient(index, serializer);
var replayTools = new ReplayToolExecutor(index, serializer);

// ... run the same agent loop — returns recorded outputs, no real API calls ...
```

`ReplayIndex` matches incoming requests to recorded events using an `InvocationKey`. Three strictness levels are available: `Strict`, `Lenient`, and `Lookup`.

## Dependency Injection

```csharp
services.AddFlightRecorder(builder => builder
    .SetMode(FlightMode.Record)
    .UseSink(sp => new JsonlFileSink("traces/run.jsonl", sp.GetRequiredService<ICanonicalJsonSerializer>()))
    .UseRedaction(r => r.AddApiKeyRedactor().AddPiiRedactor())
    .UseIntegrity());
```

## CI Golden Trace Recipe

1. Run your agent in record mode once and commit the resulting JSONL trace to source control.
2. In CI, run in replay mode against the committed trace.
3. Use `FlightAssert` (from `AgentFlightRecorder.Testing.Xunit`) to assert the replayed event sequence matches the golden trace.

Any change to prompts, tool signatures, or agent logic that alters the call sequence will cause the replay to fail, surfacing regressions before they reach production.

```csharp
// xUnit test example
public class GoldenTraceTests(FlightReplayFixture fixture) : IClassFixture<FlightReplayFixture>
{
    [Fact]
    public async Task AgentLoop_MatchesGoldenTrace()
    {
        var result = await fixture.ReplayAsync("traces/golden.jsonl", RunAgentAsync);
        FlightAssert.EventSequenceMatches(fixture.GoldenEvents, result.Events);
    }
}
```

## Redaction

Configure redaction before recording any production data. The redaction pipeline runs before events are written to the sink; raw values are never persisted.

```csharp
builder.UseRedaction(r => r
    .AddApiKeyRedactor()   // scrubs Authorization headers and bearer tokens
    .AddPiiRedactor());    // scrubs common PII patterns (email, phone, etc.)
```

Custom redactors implement `IRedactor` and can be registered with `.AddRedactor<T>()`.

## Integrity

Enabling integrity attaches a SHA-256 hash chain to each event. An optional HMAC signature can be added for tamper-evidence in regulated environments.

```csharp
builder.UseIntegrity(o => o.EnableHmac(Environment.GetEnvironmentVariable("TRACE_HMAC_KEY")));
```

Hash chain verification is available via `IntegrityVerifier` in `AgentFlightRecorder.Core`.

## Backpressure

Three backpressure modes are available on `FlightRecorderOptions.BackpressureMode`:

- `Block` — the recording call blocks until the pipeline drains (default).
- `Drop` — events are dropped if the internal channel is full; dropped events are counted on `FlightRecorder.DroppedEventCount`.
- `Buffer` — an unbounded overflow buffer absorbs bursts before the bounded channel.

## Key Features

- Structured JSONL traces of LLM calls, tool invocations, and state checkpoints
- Deterministic offline replay with no real API calls
- CI golden trace regression testing via xUnit helpers
- Redaction pipeline for API keys and PII, applied before any event is persisted
- SHA-256 hash chain integrity with optional HMAC signing
- Configurable backpressure: drop, block, or buffer
- Canonical JSON serialization with alphabetically sorted properties for stable hashes
- Channel-based async pipeline: single-consumer FIFO with redaction and integrity stages
- Full DI integration via `AddFlightRecorder`

## Samples

| Sample | Description |
|---|---|
| `samples/SemanticKernelSample/` | Offline demo with mock LLM — no API keys required |
| `samples/AzureOpenAISample/` | Real Azure OpenAI integration with full feature showcase |
| `samples/UnitTestingSample/` | xUnit test patterns for golden trace CI testing |

## Documentation

Detailed event schema, architecture notes, and sink/replay configuration reference are in the `docs/` directory.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT — see [LICENSE](LICENSE) for details.
