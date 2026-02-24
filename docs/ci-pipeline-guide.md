# CI Pipeline Guide: Golden Trace Testing

This guide shows how to integrate AgentFlightRecorder.NET golden trace testing into GitHub Actions and Azure DevOps pipelines. The patterns are based on the [`samples/UnitTestingSample`](../samples/UnitTestingSample/) project.

## How Golden Trace Testing Works

1. **Record** — Run your agent with mock/scripted LLM clients to produce a deterministic JSONL trace file
2. **Commit** — Check the trace file (`golden-trace.jsonl`) into source control
3. **Replay** — In CI, replay the agent against the committed trace — no real API calls
4. **Assert** — Use `FlightAssert` and `TraceDiff` to verify the replayed sequence matches the golden trace

Any change to prompts, tool signatures, or agent logic that alters the call sequence will fail the replay, catching regressions before they reach production.

## Prerequisites

Your test project needs these packages:

```xml
<ItemGroup>
  <PackageReference Include="AgentFlightRecorder.Core" Version="0.1.0" />
  <PackageReference Include="AgentFlightRecorder.Replay" Version="0.1.0" />
  <PackageReference Include="AgentFlightRecorder.Sinks.Jsonl" Version="0.1.0" />
  <PackageReference Include="AgentFlightRecorder.Testing.Xunit" Version="0.1.0" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  <PackageReference Include="xunit" Version="2.9.3" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
</ItemGroup>
```

Ensure golden trace files are copied to the test output:

```xml
<ItemGroup>
  <None Include="TestData\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Step 1: Generate the Golden Trace

Create a test that records a deterministic scenario using scripted mock clients:

```csharp
[Fact]
[Trait("Category", "Generator")]
public async Task GenerateGoldenTrace()
{
    var serializer = new CanonicalJsonSerializer();
    var filePath = Path.Combine(GetSourceTestDataDir(), "golden-trace.jsonl");

    await using var sink = new JsonlFileSink(filePath, serializer);
    var options = new FlightRecorderOptions
    {
        Mode = FlightMode.Record,
        Sink = sink,
        SynchronousMode = true
    };

    await using var recorder = new FlightRecorder(options);
    await recorder.StartAsync();

    var llmClient = new RecordingLlmClient(
        ScriptedLlmClient.CreateWeatherScenario(), recorder, serializer);
    var toolExecutor = new RecordingToolExecutor(
        SampleToolExecutor.CreateWeatherScenario(), recorder, serializer);

    await RunAgentScenario(llmClient, toolExecutor);
    await recorder.StopAsync();
}
```

Key points:
- Use `SynchronousMode = true` for deterministic event ordering
- Use scripted/mock clients (`ScriptedLlmClient`, `SampleToolExecutor`) — never real APIs
- Use `[CallerFilePath]` to write the trace back to the source `TestData/` directory

Run the generator locally:

```bash
dotnet test --filter "Category=Generator"
```

Then commit the resulting `TestData/golden-trace.jsonl` to source control.

## Step 2: Write CI Regression Tests

### Event Sequence Comparison

Verify the event type sequence matches the golden trace:

```csharp
[Fact]
public async Task GoldenTrace_ReplayProducesSameSequence()
{
    var goldenEvents = await LoadGoldenTrace("TestData/golden-trace.jsonl");

    // Re-record the same scenario
    var sink = new InMemorySink();
    var options = new FlightRecorderOptions
    {
        Mode = FlightMode.Record,
        Sink = sink,
        SynchronousMode = true
    };

    await using var recorder = new FlightRecorder(options);
    await recorder.StartAsync();

    var llmClient = new RecordingLlmClient(
        ScriptedLlmClient.CreateWeatherScenario(), recorder, serializer);
    var toolExecutor = new RecordingToolExecutor(
        SampleToolExecutor.CreateWeatherScenario(), recorder, serializer);

    await RunAgentScenario(llmClient, toolExecutor);
    await recorder.StopAsync();

    var result = TraceDiff.CompareTypes(goldenEvents, sink.Events);
    Assert.True(result.AreEqual, $"Event type sequences should match.\n{result}");
}
```

### Replay Strict Mode

Use `ReplayLlmClient` and `ReplayToolExecutor` to replay the exact recorded sequence without any real API calls:

```csharp
[Fact]
public async Task StrictReplay_MatchesExactSequence()
{
    var events = await LoadGoldenTrace("TestData/golden-trace.jsonl");
    var index = new ReplayIndex(events, serializer);

    var replayLlm = new ReplayLlmClient(index, serializer, ReplayStrictness.Strict);
    var replayTool = new ReplayToolExecutor(index, serializer, ReplayStrictness.Strict);

    // Run the same agent scenario — returns recorded responses
    await RunAgentScenario(replayLlm, replayTool);
    // If the call sequence doesn't match, ReplayMismatchException is thrown
}
```

### Payload Drift Detection

Detect changes in LLM response content or tool results:

```csharp
var result = TraceDiff.Compare(goldenEvents, replayedEvents, comparePayloads: true);
Assert.True(result.AreEqual, $"Payload drift detected:\n{result}");
```

### Integrity Chain Verification

Verify that committed trace files haven't been tampered with:

```csharp
[Fact]
public async Task IntegrityChain_Valid()
{
    var integrityProvider = new Sha256IntegrityProvider(serializer);
    var events = await LoadGoldenTrace("TestData/golden-trace.jsonl");
    FlightAssert.IntegrityChainValid(events, integrityProvider);
}
```

## Step 3: Configure Your CI Pipeline

### GitHub Actions

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Required for SourceLink

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Run tests (includes golden trace regression)
        run: dotnet test --no-build --configuration Release --verbosity normal

      # Optional: separate step for golden trace tests only
      - name: Golden trace regression
        run: dotnet test --no-build --configuration Release --filter "FullyQualifiedName~GoldenTraceTests"

      # Optional: publish test results
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/TestResults/**"
```

To exclude the generator test from CI (it writes files), add a filter:

```yaml
      - name: Run tests (exclude generators)
        run: dotnet test --no-build --configuration Release --filter "Category!=Generator"
```

### Azure DevOps

```yaml
trigger:
  branches:
    include:
      - main

pr:
  branches:
    include:
      - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'
  dotnetVersion: '8.0.x'

steps:
  - checkout: self
    fetchDepth: 0  # Required for SourceLink

  - task: UseDotNet@2
    displayName: 'Install .NET SDK'
    inputs:
      packageType: 'sdk'
      version: '$(dotnetVersion)'

  - task: DotNetCoreCLI@2
    displayName: 'Restore'
    inputs:
      command: 'restore'

  - task: DotNetCoreCLI@2
    displayName: 'Build'
    inputs:
      command: 'build'
      arguments: '--no-restore --configuration $(buildConfiguration)'

  - task: DotNetCoreCLI@2
    displayName: 'Run tests (includes golden trace regression)'
    inputs:
      command: 'test'
      arguments: '--no-build --configuration $(buildConfiguration) --filter "Category!=Generator" --logger trx --results-directory $(Agent.TempDirectory)/TestResults'
      publishTestResults: true

  # Optional: separate step for golden trace tests only
  - task: DotNetCoreCLI@2
    displayName: 'Golden trace regression'
    inputs:
      command: 'test'
      arguments: '--no-build --configuration $(buildConfiguration) --filter "FullyQualifiedName~GoldenTraceTests&Category!=Generator"'
```

To publish test results as a build artifact:

```yaml
  - task: PublishBuildArtifacts@1
    displayName: 'Publish test results'
    condition: always()
    inputs:
      pathToPublish: '$(Agent.TempDirectory)/TestResults'
      artifactName: 'TestResults'
```

## Workflow: Updating a Golden Trace

When your agent logic changes intentionally (new prompts, different tool calls, etc.):

1. Update the scripted mock clients to reflect the new behavior
2. Run the generator test locally:
   ```bash
   dotnet test --filter "Category=Generator"
   ```
3. Review the diff in `TestData/golden-trace.jsonl`
4. Commit the updated trace file alongside your code changes
5. CI will now validate against the new golden trace

## FlightAssert Reference

The `AgentFlightRecorder.Testing.Xunit` package provides these assertion helpers:

| Method | Purpose |
|---|---|
| `FlightAssert.ContainsEventType(events, type)` | At least one event of the given type exists |
| `FlightAssert.DoesNotContainEventType(events, type)` | No events of the given type exist (e.g., no errors) |
| `FlightAssert.EventSequenceEquals(expected, actual)` | Event type sequences match in order |
| `FlightAssert.IntegrityChainValid(events, provider)` | SHA-256 hash chain is unbroken |
| `FlightAssert.ReplayMatchesTrace(index, serializer, action)` | Replay completes without `ReplayMismatchException` |

`TraceDiff` provides deeper comparison:

| Method | Purpose |
|---|---|
| `TraceDiff.CompareTypes(expected, actual)` | Compare event type sequences only |
| `TraceDiff.Compare(expected, actual, comparePayloads)` | Compare types and optionally payloads |

## Replay Strictness Modes

| Mode | Behavior | Use Case |
|---|---|---|
| `Strict` | Calls must match in exact recorded order | Default for CI regression |
| `Lenient` | Calls can arrive in any order, matched by key | Agent reordering is acceptable |
| `Lookup` | Same as Lenient, key-based matching | Exploratory testing |

## Tips

- **Always use `SynchronousMode = true`** in tests for deterministic event ordering
- **Filter out generator tests in CI** with `--filter "Category!=Generator"` to avoid writing files during CI runs
- **Use `InMemorySink`** for fast, allocation-light tests that don't need file I/O
- **Commit golden traces alongside code changes** — treat them as test fixtures, not generated artifacts
- **Review golden trace diffs in PRs** — they show exactly how agent behavior changed
- **Add integrity verification** for regulated environments where trace tampering must be detectable
