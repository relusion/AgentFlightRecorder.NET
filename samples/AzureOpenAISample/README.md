# Azure OpenAI Sample

Demonstrates the full AgentFlightRecorder.NET feature set with real Azure OpenAI API calls using Semantic Kernel.

## Prerequisites

- .NET 8 SDK
- Azure subscription with an Azure OpenAI resource
- A deployed model (e.g., `gpt-4o-mini`)

## Setup

Set the required environment variables:

```bash
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_API_KEY="your-api-key"

# Optional (defaults to gpt-4o-mini)
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"

# Optional (uses a demo key if not set)
export TRACE_HMAC_KEY="your-signing-key"
```

## Running

```bash
dotnet run --project samples/AzureOpenAISample
```

The sample runs three phases sequentially:

1. **RECORD** — Multi-turn conversation with Azure OpenAI (4 turns with tool calls and streaming)
2. **REPLAY** — Replays the recorded trace offline (no Azure calls)
3. **VERIFY** — Validates hash chain integrity, HMAC signature, and output fidelity

## What This Sample Demonstrates

| Feature | Description |
|---------|-------------|
| FlightRecorderBuilder | Fluent DI configuration via `AddFlightRecorder()` |
| Azure OpenAI Integration | Real API calls via `AddAzureOpenAIChatCompletion` |
| KernelBuilder.AddFlightRecorder | DI integration with Semantic Kernel |
| Auto-function-calling | SK `FunctionChoiceBehavior.Auto()` with `FlightRecorderFunctionFilter` |
| CompositeRedactor | `ApiKeyRedactor` + `PiiRedactor` chained together |
| Spans (hierarchy) | Planning + Acting + ToolExecution span nesting |
| Checkpoints | `CheckpointAsync` for conversation state snapshots |
| Annotations | `AnnotateAsync` with typed tags dictionary |
| Streaming | `GetStreamingChatMessageContentsAsync` for Turn 4 |
| HMAC Signing | SHA-256 hash chain + HMAC-SHA256 run signature |
| IntegrityVerifier | Full hash chain verification in the Verify phase |
| Backpressure | Drop strategy with 512 channel capacity |
| Replay | Offline deterministic replay via `ReplayIndex` + `ReplayLlmClient` |
| Output Comparison | Bit-identical verification of recorded vs replayed outputs |

## Project Structure

```
AzureOpenAISample/
├── Program.cs              # Composition root — DI setup, credential validation, phase orchestration
├── appsettings.json        # Placeholder configuration (no real credentials)
├── Scenarios/
│   ├── RecordScenario.cs   # Phase 1: Record with Azure OpenAI
│   ├── ReplayScenario.cs   # Phase 2: Replay from recorded trace
│   └── VerifyScenario.cs   # Phase 3: Integrity + output comparison
└── Plugins/
    ├── WeatherPlugin.cs    # GetWeather, SearchLocation (returns PII-triggering data)
    └── UtilityPlugin.cs    # ConvertTemperature
```

## Adapting for Standard OpenAI

Replace `AddAzureOpenAIChatCompletion` with `AddOpenAIChatCompletion` in `Program.cs`:

```csharp
kernelBuilder.AddOpenAIChatCompletion("gpt-4o-mini", apiKey);
```

## Offline Alternative

For a sample that requires no Azure credentials or API access, see [SemanticKernelSample](../SemanticKernelSample/).
