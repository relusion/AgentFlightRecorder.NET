# AgentFlightRecorder.NET

**Software Development Specification (SDS)**
**Doc version:** 0.1
**Date:** 2026-02-24 (Europe/Berlin)

---

## 1) Purpose

AgentFlightRecorder.NET is a **.NET library** that provides a **deterministic, replayable “flight recorder”** for agentic AI runs (LLM planning + tool execution + state transitions + retries/loops). It captures structured events during execution and enables **offline replay** for debugging, audits, and regression testing.

This document specifies the **final intended state** and everything needed for a development team to implement the project from scratch.

---

## 2) Goals and Non-Goals

### Goals

1. **Record** multi-step agent runs as structured, append-only events:

   * LLM requests/responses
   * Tool calls (args, results, errors, latency)
   * State checkpoints
   * Exceptions and annotations
2. **Replay** recorded runs deterministically:

   * No real tool calls
   * No real LLM calls
   * Strict matching against recorded invocations
3. **Enable CI regression tests**:

   * “Golden trace” replays
   * Diffs between runs (optional, minimal)
4. **Support pluggable storage backends**:

   * JSONL (baseline)
   * SQLite (optional)
   * Postgres (optional)
5. **Provide security/forensics basics**:

   * Redaction hooks (PII/secret stripping)
   * Tamper-evident hash chain over events (optional HMAC signing)

### Non-Goals (explicit boundaries)

* Not an agent framework, orchestrator, or workflow engine.
* Not a monitoring platform/dashboard (no hosted UI).
* Not an observability backend (can emit events; doesn’t store/visualize beyond sinks).
* Not a “prompt library” or “chain framework.”
* No guarantee of cross-model semantic equivalence (replay validates *recorded I/O*, not “intelligence”).

---

## 3) Target Users

* **.NET teams** building production agentic workflows needing:

  * Reproducible debugging
  * Audit trails of tool usage and outputs
  * Deterministic regression testing in CI/CD
* Libraries/framework maintainers who want to add “record/replay” capabilities via adapters.

---

## 4) Primary Use Cases

### UC1: Debugging “can’t reproduce” failures

* Record production-like runs; replay locally with identical tool/LLM I/O.
* Identify the exact failing step and inputs.

### UC2: CI regression testing (golden traces)

* Record a successful run once.
* Replay after code changes, prompt changes, model upgrades, or tool changes.
* Fail tests when behavior deviates from recorded expectations.

### UC3: Auditing tool usage

* Prove which tools were called, with what inputs, and what results were returned.
* Produce an immutable evidence artifact for incident reviews.

### UC4: Safety validation (basic)

* Ensure redaction and permission checks happened (via emitted events and policies).
* Detect prompt-injection-ish patterns in tool outputs (optional heuristic marker only).

---

## 5) System Overview

### High-level concept

AgentFlightRecorder.NET wraps/intercepts:

* **LLM client calls**
* **Tool invocations**
* **State updates/checkpoints**

It writes an **ordered event stream** to a sink (JSONL/DB/etc.). In replay mode, it swaps the LLM and tool executors with **deterministic stubs** that return recorded outputs.

### Execution modes

* **Record Mode:** real LLM + real tools, events persisted
* **Replay Mode:** stubbed LLM + stubbed tools, deterministic outputs
* **Passthrough Mode:** disabled/no-op for minimal overhead

---

## 6) Architecture

### Components

1. **Core**

   * Event model, IDs, schema versioning
   * Recorder context + lifecycle
   * Canonical serialization + hashing utilities
2. **Interceptors**

   * LLM interceptor (records requests/responses; wraps provider)
   * Tool interceptor (records tool calls, args, results, exceptions)
3. **State Checkpoint API**

   * App/framework can emit checkpoints (serialized snapshots)
4. **Sinks / Storage**

   * JSONL sink (baseline)
   * Optional: SQLite, Postgres sinks
5. **Replay Engine**

   * Indexed lookup of recorded invocations
   * Strict matching and deterministic stub responses
6. **Redaction & Security**

   * Redactor pipeline (pre-persist)
   * Tamper-evident hash chain (per event)
   * Optional HMAC signature for a run
7. **Testing Helpers**

   * xUnit assertions for “replay equals expected”
   * Trace diff helpers (optional, minimal)

### Logical flow diagrams

#### Record mode

1. Run starts → `RunStarted`
2. LLM call → `LlmRequest` → provider → `LlmResponse`
3. Tool call → `ToolCallStarted` → tool → `ToolCallCompleted` (or `ToolCallFailed`)
4. Checkpoint(s) → `StateCheckpoint`
5. Run ends → `RunCompleted`

#### Replay mode

1. Load trace → build invocation index
2. LLM call → match recorded `LlmRequest` → return recorded `LlmResponse`
3. Tool call → match recorded `ToolCallStarted` → return recorded result event
4. Validate ordering/strictness; fail fast on mismatch

---

## 7) Repository and Package Layout

### Repo structure

```
/src
  /AgentFlightRecorder.Core
  /AgentFlightRecorder.Sinks.Jsonl
  /AgentFlightRecorder.Sinks.Sqlite          (optional)
  /AgentFlightRecorder.Sinks.Postgres        (optional)
  /AgentFlightRecorder.Replay
  /AgentFlightRecorder.Testing.Xunit
  /AgentFlightRecorder.Adapters.ExtensionsAI (optional but recommended)
  /AgentFlightRecorder.Adapters.SemanticKernel (optional)
/tests
  /Core.Tests
  /Replay.Tests
  /JsonlSink.Tests
  /Adapters.Tests
/docs
  architecture.md
  schema.md
  quickstart.md
```

### NuGet packages (intended)

* `AgentFlightRecorder.Core`
* `AgentFlightRecorder.Sinks.Jsonl`
* `AgentFlightRecorder.Replay`
* `AgentFlightRecorder.Testing.Xunit`
* Optional adapters as separate packages (to avoid hard deps).

---

## 8) Functional Requirements

### FR1: Run lifecycle

* Start/stop recording with a `RunId`.
* Must support nested “spans” or step grouping (planning vs acting).

### FR2: Record LLM interactions

Capture:

* Provider name, model, settings (temperature, max tokens, etc.)
* Prompt/messages (with redaction hook)
* Tool/function call requests emitted by the model (if present)
* Response text/structured output
* Token counts/cost fields if provided by caller (optional)

### FR3: Record tool calls

Capture:

* Tool name
* Canonical args (structured)
* Start/end timestamps and duration
* Result payload (structured) or exception details
* Retry metadata: attempt number, idempotency key (if supplied)

### FR4: State checkpoints

* Allow the host app/framework to emit a checkpoint with:

  * checkpoint name
  * serialized state blob (user-defined)
  * optional metadata (e.g., “before tool X”, “after plan update”)

### FR5: Pluggable sinks

* Sinks must be async-friendly and safe for high-frequency events.
* JSONL sink required.

### FR6: Replay engine

* Load trace(s) from sink.
* Build a deterministic index mapping invocation keys → recorded results.
* Provide:

  * `ReplayLlmClient` (stub)
  * `ReplayToolExecutor` (stub)
* Strict mode:

  * Fail if a call is not found
  * Fail if call parameters do not match canonical form
  * Fail if calls occur in unexpected order (optional “order strictness levels”)

### FR7: Tamper-evident integrity (optional but part of final state)

* Each event includes `PrevHash` and `Hash`.
* Run can include a final signature/HMAC over the event chain.

### FR8: Redaction pipeline

* Configurable redactors applied to:

  * LLM input/output
  * tool args/results
  * checkpoints
* Provide built-in “basic” redactors:

  * common secret patterns (API keys)
  * email/phone (simple regex; configurable)

### FR9: Minimal adapter layer (recommended)

* Provide optional adapters:

  * `Microsoft.Extensions.AI` (wrap chat client, etc.)
  * Semantic Kernel (wrap connectors / tool calls)
* Adapters are thin and only translate calls into Core events.

---

## 9) Non-Functional Requirements

### NFR1: Performance

* Recorder overhead target: **<5%** wall-clock overhead in typical tool-heavy runs.
* JSONL writing must support batching and async flushing.

### NFR2: Reliability

* Sinks must handle partial failures:

  * Backpressure strategy (drop, block, or buffer; configurable)
* The recorder must never crash the host by default:

  * errors in sink write → log + continue (configurable fail-fast)

### NFR3: Determinism

* Replay must be deterministic given a trace:

  * Canonical JSON serialization
  * Stable hashing of invocations
  * Strict matching rules

### NFR4: Security & Privacy

* Redaction must occur **before persistence**.
* Provide guidance for users to avoid storing raw secrets.

### NFR5: Compatibility

* Target framework: **.NET 8** (`net8.0`)
* Avoid exotic dependencies; keep <10 dependencies per package.

---

## 10) Data Model and Event Schema

### Core identifiers

* `RunId`: GUID/UUID string
* `TraceId`, `SpanId`, `ParentSpanId`: W3C compatible (strings)
* `Sequence`: monotonically increasing `long` per run (assigned by recorder)

### Base event envelope

All events serialized as JSON objects (JSONL: one object per line):

```json
{
  "schemaVersion": "1.0",
  "runId": "…",
  "eventId": "…",
  "sequence": 42,
  "timestampUtc": "2026-02-24T12:34:56.789Z",
  "traceId": "…",
  "spanId": "…",
  "parentSpanId": "…",
  "type": "ToolCallCompleted",
  "payload": { },
  "integrity": {
    "prevHash": "…",
    "hash": "…",
    "algo": "SHA-256"
  }
}
```

### Event types (final list)

* `RunStarted`, `RunCompleted`
* `SpanStarted`, `SpanCompleted`
* `LlmRequest`, `LlmResponse`, `LlmError`
* `ToolCallStarted`, `ToolCallCompleted`, `ToolCallFailed`
* `StateCheckpoint`
* `Annotation` (developer notes / tags)
* `RecorderError` (sink failures, etc.)

### Invocation key (for replay matching)

For LLM/tool calls:

* `kind`: `llm` | `tool`
* `name`: model name or tool name
* `argsHash`: hash of canonical JSON args
* `attempt`: int
* optional `idempotencyKey`

Canonical JSON rules:

* Use `System.Text.Json` with:

  * stable property ordering
  * no insignificant whitespace
  * normalized numbers (as emitted by serializer)
* Provide a dedicated `ICanonicalJson` service to guarantee stability.

---

## 11) Public API Specification (C#)

> Names are indicative; adjust during implementation but keep the surface small and obvious.

### Core: recorder lifecycle

```csharp
public sealed record FlightRecorderOptions(
    bool Enabled,
    FlightMode Mode, // Record | Replay | Passthrough
    IFlightSink Sink,
    IRedactor? Redactor = null,
    IIntegrityProvider? IntegrityProvider = null,
    ReplayOptions? Replay = null);

public enum FlightMode { Record, Replay, Passthrough }

public interface IFlightRecorder : IAsyncDisposable
{
    string RunId { get; }
    FlightMode Mode { get; }

    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync(CancellationToken ct = default);

    IFlightSpan StartSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal);
    ValueTask CheckpointAsync(string name, object state, IStateSerializer serializer, CancellationToken ct = default);
    ValueTask AnnotateAsync(string message, IReadOnlyDictionary<string,string>? tags = null, CancellationToken ct = default);
}
```

### Sinks

```csharp
public interface IFlightSink : IAsyncDisposable
{
    ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
}

public sealed class JsonlFileSink : IFlightSink
{
    public JsonlFileSink(string path, JsonlSinkOptions? options = null);
}
```

### Tool interception

```csharp
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct);
}

public sealed class RecordingToolExecutor : IToolExecutor
{
    public RecordingToolExecutor(IToolExecutor inner, IFlightRecorder recorder, ToolRecordingOptions? options = null);
}
```

### LLM interception (generic)

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed class RecordingLlmClient : ILlmClient
{
    public RecordingLlmClient(ILlmClient inner, IFlightRecorder recorder, LlmRecordingOptions? options = null);
}
```

### Replay engine

```csharp
public interface IReplayStore
{
    Task<ReplayIndex> LoadAsync(string runId, CancellationToken ct);
}

public sealed class ReplayToolExecutor : IToolExecutor
{
    public ReplayToolExecutor(ReplayIndex index, ReplayStrictness strictness = ReplayStrictness.Strict);
}

public sealed class ReplayLlmClient : ILlmClient
{
    public ReplayLlmClient(ReplayIndex index, ReplayStrictness strictness = ReplayStrictness.Strict);
}
```

### Redaction and integrity

```csharp
public interface IRedactor
{
    object RedactEventPayload(string eventType, object payload);
}

public interface IIntegrityProvider
{
    FlightIntegrity Compute(FlightIntegrity? previous, FlightEvent evt);
}
```

---

## 12) Storage Backends

### JSONL (required)

* One file per run:

  * `run-{runId}.jsonl`
* Pros: simplest, diff-friendly, easy to ship as artifact.

### SQLite (optional)

* Tables:

  * `runs`
  * `events` (runId, sequence, type, timestamp, payloadJson, hash, prevHash)
* Goal: fast local indexing and replay.

### Postgres (optional)

* Similar schema; supports multi-run querying for team environments.

**Final state requirement:** JSONL must be complete and sufficient; DB sinks are optional add-ons.

---

## 13) Replay Semantics

### Matching rules

* Invocation match is based on:

  * tool/llm `name`
  * canonical args hash
  * attempt number (and/or sequence in strict mode)
* Strictness levels:

  * **Strict:** order must match; no extra calls
  * **Lenient:** allow extra spans/annotations; still requires invocation matches
  * **Lookup:** match by key only; order ignored (useful for debugging but weaker)

### Failure behavior

* Missing call in trace:

  * throw `ReplayMismatchException` with a helpful diff:

    * expected invocation key
    * closest matches (same name, different args hash)
* Multiple matches for same key:

  * require sequence matching or consume in order (configurable).

---

## 14) Security and Privacy

### Redaction

* Redaction happens before sink write.
* Provide configuration:

  * redaction on/off per event type
  * allow custom redactors

### Tamper-evident chain

* Each event includes a hash derived from:

  * previous hash + canonical serialized event (minus current hash fields)
* Optional run signature:

  * HMAC-SHA256 using user-provided key
  * stored in `RunCompleted` payload

This is not “compliance.” It’s basic integrity so traces can be trusted in incident reviews.

---

## 15) Logging and Diagnostics

* Use `Microsoft.Extensions.Logging`:

  * `RecorderError` events generated for sink failures (if configured)
* Provide optional `ActivitySource` integration hooks (no hard dependency on OpenTelemetry):

  * Create spans for record/replay operations (internal only)

---

## 16) Quality and Testing Plan

### Unit tests

* Canonical JSON stability tests (ordering, hashing)
* Event schema serialization/deserialization round-trip
* Integrity chain verification
* Replay matching:

  * exact match
  * mismatch: tool name mismatch, args mismatch, order mismatch

### Integration tests

* Record a synthetic “agent loop” (LLM stub + tool stub) and replay it.
* Verify:

  * replay returns identical outputs
  * real tool/LLM are never called during replay (assert with counters)

### Golden trace tests

* Commit small sample traces under `/tests/TestData/`
* CI validates replay doesn’t change behavior unexpectedly.

### Performance tests (lightweight)

* Record 10k events with JSONL sink using batching
* Ensure memory stable and throughput acceptable.

**Definition of Done for v1**

* Replay strict mode passes with 100% deterministic outputs for provided traces
* JSONL sink reliable with flush + graceful stop
* Redaction and integrity optional features working and tested

---

## 17) Build, CI/CD, and Release

### Build

* `dotnet build` / `dotnet test` for all projects
* Analyzer settings:

  * nullable enabled
  * warnings as errors in CI

### CI pipeline (GitHub Actions)

* Windows + Linux builds
* Test run
* Pack NuGet artifacts on tagged releases
* Publish:

  * prerelease tags to NuGet (optional)
  * stable tags to NuGet (required for v1.0)

### Versioning

* SemVer for packages (`1.0.0`)
* Schema version independent (start at `1.0`)

### License

* MIT or Apache-2.0 (choose one and apply consistently)

---

## 18) Documentation Deliverables

* `README.md` with:

  * Quickstart record/replay examples
  * Safety notes (redaction)
  * CI golden trace recipe
* `/docs/schema.md` describing event types and payloads
* `/docs/architecture.md` with diagrams and adapter guidance

---

## 19) Milestones

### Milestone A: MVP (record + replay core, JSONL)

**Acceptance criteria**

* Record LLM + tool calls + checkpoints to JSONL
* Replay stubs work with strict matching
* Minimal tests + examples

### Milestone B: v1.0 (production-grade library)

* Redaction pipeline
* Integrity hash chain
* Better error messages and mismatch diffs
* `AgentFlightRecorder.Testing.Xunit`

### Milestone C: Optional add-ons

* SQLite sink + replay store
* Postgres sink
* Adapters for specific ecosystems (Extensions.AI, Semantic Kernel)

---

## 20) Example Usage (intended)

### Record mode

```csharp
var sink = new JsonlFileSink("run-logs/run-123.jsonl");
var recorder = new FlightRecorder(new FlightRecorderOptions(
    Enabled: true,
    Mode: FlightMode.Record,
    Sink: sink));

await recorder.StartAsync();

var llm = new RecordingLlmClient(realLlm, recorder);
var tools = new RecordingToolExecutor(realTools, recorder);

// app agent loop calls llm/tools...
await recorder.CheckpointAsync("after-plan", stateObj, MyStateSerializer.Instance);

await recorder.StopAsync();
```

### Replay mode

```csharp
var index = await JsonlReplayStore.FromFile("run-logs/run-123.jsonl").LoadAsync();
var llm = new ReplayLlmClient(index);
var tools = new ReplayToolExecutor(index);

// run same workflow; it should deterministically replay recorded outputs
```

---

## 21) Risks and Mitigations

* **Canonical JSON drift breaks replay**

  * Mitigate with a dedicated canonical serializer + tests.
* **Users store secrets in traces**

  * Mitigate with redaction hooks + docs + default basic redactors.
* **Framework-specific integration complexity**

  * Mitigate by keeping adapters thin and optional, core stays generic.

---

## Appendix A: Acceptance Checklist (Final State)

* [ ] Core event schema stable + documented
* [ ] JSONL sink reliable + tested
* [ ] Replay engine strict matching works + mismatch diffs are actionable
* [ ] Redaction pipeline exists and runs pre-persist
* [ ] Tamper-evident hash chain implemented + verified
* [ ] xUnit testing helpers delivered
* [ ] Minimal adapter(s) delivered (at least one)
* [ ] CI pipeline with pack + test + release

