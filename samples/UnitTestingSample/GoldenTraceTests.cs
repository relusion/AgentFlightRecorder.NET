using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Sinks.Jsonl;
using AgentFlightRecorder.Testing.Xunit;
using UnitTestingSample.Mocks;
using Xunit;

namespace UnitTestingSample;

/// <summary>
/// Golden trace generation, loading, and comparison using GoldenTraceFixture and TraceDiff.
/// <para>GoldenTraceFixture requires a file path constructor parameter and is sealed,
/// so it cannot be used with IClassFixture&lt;T&gt;. This class shows the manual lifecycle pattern
/// via <see cref="IAsyncLifetime"/>.</para>
/// </summary>
public sealed class GoldenTraceTests : IAsyncLifetime
{
    private const string GoldenFileName = "golden-trace.jsonl";
    private readonly CanonicalJsonSerializer _serializer = new();
    private GoldenTraceFixture? _fixture;

    private static string GetSourceTestDataDir([CallerFilePath] string filePath = "")
        => Path.Combine(Path.GetDirectoryName(filePath)!, "TestData");

    public async Task InitializeAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", GoldenFileName);
        if (File.Exists(path))
        {
            _fixture = new GoldenTraceFixture(path);
            await _fixture.InitializeAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Generates TestData/golden-trace.jsonl by recording a multi-turn weather scenario.
    /// Writes to the source directory via [CallerFilePath] for easy regeneration.
    /// </summary>
    [Fact]
    [Trait("Category", "Generator")]
    public async Task GenerateGoldenTrace()
    {
        var testDataDir = GetSourceTestDataDir();
        Directory.CreateDirectory(testDataDir);
        var filePath = Path.Combine(testDataDir, GoldenFileName);

        await using var sink = new JsonlFileSink(filePath, _serializer);
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llmClient = new RecordingLlmClient(
            ScriptedLlmClient.CreateWeatherScenario(), recorder, _serializer);
        var toolExecutor = new RecordingToolExecutor(
            SampleToolExecutor.CreateWeatherScenario(), recorder, _serializer);

        await RunWeatherScenario(llmClient, toolExecutor);
        await recorder.StopAsync();

        Assert.True(File.Exists(filePath), $"Golden trace should exist at {filePath}");
    }

    /// <summary>
    /// Loads the golden trace and verifies it contains the expected number of events.
    /// </summary>
    [Fact]
    public void GoldenTrace_LoadsExpectedEventCount()
    {
        EnsureGoldenTraceExists();

        // RunStarted + 3*(LlmRequest+LlmResponse) + 2*(ToolCallStarted+ToolCallCompleted) + RunCompleted = 12
        Assert.Equal(12, _fixture!.Events.Count);
    }

    /// <summary>
    /// Uses TraceDiff.CompareTypes to verify the event type sequence matches expectations.
    /// </summary>
    [Fact]
    public async Task GoldenTrace_ReplayProducesSameSequence()
    {
        EnsureGoldenTraceExists();

        // Re-record the same scenario and compare event type sequences
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
            ScriptedLlmClient.CreateWeatherScenario(), recorder, _serializer);
        var toolExecutor = new RecordingToolExecutor(
            SampleToolExecutor.CreateWeatherScenario(), recorder, _serializer);

        await RunWeatherScenario(llmClient, toolExecutor);
        await recorder.StopAsync();

        var result = TraceDiff.CompareTypes(_fixture!.Events, sink.Events);
        Assert.True(result.AreEqual, $"Event type sequences should match.\n{result}");
    }

    /// <summary>
    /// TraceDiff.Compare with comparePayloads=true detects payload drift across recordings.
    /// </summary>
    [Fact]
    public async Task GoldenTrace_PayloadComparisonDetectsDrift()
    {
        EnsureGoldenTraceExists();

        // Re-record the same scenario — payloads will match since mocks are deterministic
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
            ScriptedLlmClient.CreateWeatherScenario(), recorder, _serializer);
        var toolExecutor = new RecordingToolExecutor(
            SampleToolExecutor.CreateWeatherScenario(), recorder, _serializer);

        await RunWeatherScenario(llmClient, toolExecutor);
        await recorder.StopAsync();

        // comparePayloads: true checks both type sequence and payload content
        var result = TraceDiff.Compare(_fixture!.Events, sink.Events, comparePayloads: true);

        // Types must always match
        var typeOnlyDiffs = result.Differences.Where(d => d.Kind == TraceDiffKind.TypeMismatch).ToList();
        Assert.Empty(typeOnlyDiffs);

        // Payload differences are expected on dynamic fields (timestamps, eventIds, runId)
        // but the count tells you how much drift exists
        if (!result.AreEqual)
        {
            var payloadDiffs = result.Differences.Where(d => d.Kind == TraceDiffKind.PayloadMismatch).ToList();
            Assert.True(payloadDiffs.Count > 0,
                "If traces differ, differences should be payload-level (dynamic fields), not type-level.");
        }
    }

    private void EnsureGoldenTraceExists()
    {
        Assert.True(_fixture is not null,
            $"Golden trace file not found at TestData/{GoldenFileName}. " +
            "Run the GenerateGoldenTrace test first.");
    }

    private static async Task RunWeatherScenario(ILlmClient llmClient, IToolExecutor toolExecutor)
    {
        var turn1 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What's the weather in Seattle?\"}]").RootElement);
        await llmClient.CompleteAsync(turn1, CancellationToken.None);

        var weatherCall = new ToolInvocation("get_weather",
            JsonDocument.Parse("{\"city\":\"Seattle\"}").RootElement);
        await toolExecutor.ExecuteAsync(weatherCall, CancellationToken.None);

        var turn2 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"Convert that to Celsius\"}]").RootElement);
        await llmClient.CompleteAsync(turn2, CancellationToken.None);

        var convertCall = new ToolInvocation("convert_temperature",
            JsonDocument.Parse("{\"value\":72,\"from\":\"F\",\"to\":\"C\"}").RootElement);
        await toolExecutor.ExecuteAsync(convertCall, CancellationToken.None);

        var turn3 = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"Summarize the weather\"}]").RootElement);
        await llmClient.CompleteAsync(turn3, CancellationToken.None);
    }
}
