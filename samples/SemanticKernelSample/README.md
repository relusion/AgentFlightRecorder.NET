# Semantic Kernel Sample

Demonstrates AgentFlightRecorder.NET integration with Microsoft Semantic Kernel using a mock LLM client. **No API keys or cloud services required** — runs entirely offline.

## What It Shows

1. **Record** — Multi-turn conversation with tool calls captured to a JSONL trace
2. **Replay** — Deterministic replay from the recorded trace (no real LLM calls)
3. **Integrity** — SHA-256 hash chain verification of the trace
4. **Redaction** — API key scrubbing in recorded payloads

## Running

```bash
dotnet run --project samples/SemanticKernelSample
```

## Files

| File | Purpose |
|---|---|
| `Program.cs` | Main sample demonstrating record, replay, integrity, and redaction |
| `MockLlmClient.cs` | Fake LLM client that returns canned responses with tool calls |
| `SampleTools.cs` | Semantic Kernel plugin with sample tool functions |

## See Also

- `samples/AzureOpenAISample/` — Real Azure OpenAI integration (requires credentials)
- `samples/UnitTestingSample/` — xUnit test patterns for CI golden trace testing
