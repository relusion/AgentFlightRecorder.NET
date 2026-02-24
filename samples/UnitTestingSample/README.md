# Unit Testing Sample

Demonstrates xUnit test patterns for AgentFlightRecorder.NET. Shows how to write CI-friendly tests using recording, replay, and golden trace assertions.

## Test Classes

| File | Pattern |
|---|---|
| `BasicRecordingTests.cs` | Record events and assert on the captured trace |
| `ReplayTests.cs` | Record, then replay with Strict/Lenient/Lookup modes |
| `GoldenTraceTests.cs` | Load a committed golden trace and verify replay matches |
| `IntegrityTests.cs` | Verify SHA-256 hash chain integrity of recorded traces |
| `AssertionPatternTests.cs` | `FlightAssert` helpers for event sequence and payload comparison |

## Running

```bash
dotnet test samples/UnitTestingSample
```

## Golden Trace Workflow

1. Run `GoldenTraceTests.GenerateGoldenTrace` to produce `TestData/golden-trace.jsonl`
2. Commit the trace file to source control
3. CI runs the remaining tests, which replay against the golden trace
4. Any change to prompts, tool signatures, or agent logic that alters the call sequence will fail the replay tests

## See Also

- `samples/SemanticKernelSample/` — Offline Semantic Kernel integration demo
- `samples/AzureOpenAISample/` — Real Azure OpenAI integration (requires credentials)
