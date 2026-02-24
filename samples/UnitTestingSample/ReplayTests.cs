using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;
using AgentFlightRecorder.Testing.Xunit;
using Xunit;

namespace UnitTestingSample;

/// <summary>
/// ReplayLlmClient and ReplayToolExecutor with Strict, Lenient, and Lookup
/// strictness levels. Replay allows offline testing against recorded golden traces.
/// </summary>
public sealed class ReplayTests : IAsyncLifetime
{
    private const string GoldenFileName = "golden-trace.jsonl";
    private readonly CanonicalJsonSerializer _serializer = new();
    private IReadOnlyList<FlightEvent>? _events;

    public async Task InitializeAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", GoldenFileName);
        if (File.Exists(path))
        {
            var store = new JsonlReplayStore(Path.GetDirectoryName(path)!, _serializer);
            _events = await store.LoadFromFileAsync(path);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Strict mode requires calls in the exact recorded order. The replay client returns
    /// the recorded response for each matching request.
    /// </summary>
    [Fact]
    public async Task StrictReplay_MatchesExactSequence()
    {
        EnsureGoldenTraceExists();
        var index = new ReplayIndex(_events!, _serializer);
        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Strict);
        var replayTool = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        // Replay the exact same weather scenario in the same order
        var turn1 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What's the weather in Seattle?\"}]").RootElement);
        var response1 = await replayLlm.CompleteAsync(turn1, CancellationToken.None);
        Assert.NotEqual(default, response1.Content);

        var weatherCall = new ToolInvocation("get_weather",
            JsonDocument.Parse("{\"city\":\"Seattle\"}").RootElement);
        var weatherResult = await replayTool.ExecuteAsync(weatherCall, CancellationToken.None);
        Assert.NotEqual(default, weatherResult.Data);

        var turn2 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"Convert that to Celsius\"}]").RootElement);
        var response2 = await replayLlm.CompleteAsync(turn2, CancellationToken.None);
        Assert.NotEqual(default, response2.Content);

        var convertCall = new ToolInvocation("convert_temperature",
            JsonDocument.Parse("{\"value\":72,\"from\":\"F\",\"to\":\"C\"}").RootElement);
        var convertResult = await replayTool.ExecuteAsync(convertCall, CancellationToken.None);
        Assert.NotEqual(default, convertResult.Data);

        var turn3 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"Summarize the weather\"}]").RootElement);
        var response3 = await replayLlm.CompleteAsync(turn3, CancellationToken.None);
        Assert.NotEqual(default, response3.Content);
    }

    /// <summary>
    /// Lenient mode allows calls in any order as long as the key matches a recorded entry.
    /// Useful when agent decision logic changes but the set of calls remains the same.
    /// </summary>
    [Fact]
    public async Task LenientReplay_AllowsReordering()
    {
        EnsureGoldenTraceExists();
        var index = new ReplayIndex(_events!, _serializer);
        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Lenient);

        // Call in different order than recorded — Lenient mode tolerates this
        var turn3 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"Summarize the weather\"}]").RootElement);
        var response3 = await replayLlm.CompleteAsync(turn3, CancellationToken.None);
        Assert.NotEqual(default, response3.Content);

        var turn1 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What's the weather in Seattle?\"}]").RootElement);
        var response1 = await replayLlm.CompleteAsync(turn1, CancellationToken.None);
        Assert.NotEqual(default, response1.Content);
    }

    /// <summary>
    /// Lookup mode matches by key only, same as Lenient. Both use per-key FIFO queues
    /// without position tracking.
    /// </summary>
    [Fact]
    public async Task LookupReplay_MatchesByKeyOnly()
    {
        EnsureGoldenTraceExists();
        var index = new ReplayIndex(_events!, _serializer);
        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Lookup);

        var turn1 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What's the weather in Seattle?\"}]").RootElement);
        var response = await replayLlm.CompleteAsync(turn1, CancellationToken.None);
        Assert.NotEqual(default, response.Content);
    }

    /// <summary>
    /// Strict mode throws ReplayMismatchException when the request doesn't match the next
    /// expected entry. The exception includes diagnostic information: closest matches,
    /// remaining entry count, and the expected entry.
    /// </summary>
    [Fact]
    public async Task StrictReplay_MismatchThrowsWithDiagnostics()
    {
        EnsureGoldenTraceExists();
        var index = new ReplayIndex(_events!, _serializer);
        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Strict);

        // Send a request that doesn't match the first recorded entry
        var wrongRequest = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"This was never recorded\"}]").RootElement);

        var ex = await Assert.ThrowsAsync<ReplayMismatchException>(
            () => replayLlm.CompleteAsync(wrongRequest, CancellationToken.None));

        Assert.NotNull(ex.RequestedKey);
        Assert.True(ex.RemainingEntries > 0, "Should report remaining unconsumed entries");
    }

    private void EnsureGoldenTraceExists()
    {
        Assert.True(_events is not null,
            $"Golden trace file not found at TestData/{GoldenFileName}. " +
            "Run GoldenTraceTests.GenerateGoldenTrace first.");
    }
}
