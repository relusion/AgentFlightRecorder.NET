using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;
using AgentFlightRecorder.Testing.Xunit;
using UnitTestingSample.Mocks;
using Xunit;

namespace UnitTestingSample;

/// <summary>
/// All five FlightAssert methods: ContainsEventType, DoesNotContainEventType,
/// EventSequenceEquals, IntegrityChainValid, and ReplayMatchesTrace.
/// </summary>
public sealed class AssertionPatternTests
{
    private readonly CanonicalJsonSerializer _serializer = new();

    /// <summary>
    /// FlightAssert.ContainsEventType asserts that at least one event of a given type exists.
    /// </summary>
    [Fact]
    public async Task ContainsEventType_VerifiesLlmRequestExists()
    {
        var (events, _) = await RecordSimpleQA();
        FlightAssert.ContainsEventType(events, FlightEventTypes.LlmRequest);
    }

    /// <summary>
    /// FlightAssert.DoesNotContainEventType asserts that no events of a given type exist.
    /// Useful for verifying error-free runs.
    /// </summary>
    [Fact]
    public async Task DoesNotContainEventType_VerifiesNoErrors()
    {
        var (events, _) = await RecordSimpleQA();
        FlightAssert.DoesNotContainEventType(events, FlightEventTypes.LlmError);
        FlightAssert.DoesNotContainEventType(events, FlightEventTypes.ToolCallFailed);
    }

    /// <summary>
    /// FlightAssert.EventSequenceEquals verifies two event lists have the same types in order.
    /// </summary>
    [Fact]
    public async Task EventSequenceEquals_VerifiesEventOrdering()
    {
        var (events1, _) = await RecordSimpleQA();
        var (events2, _) = await RecordSimpleQA();
        FlightAssert.EventSequenceEquals(events1, events2);
    }

    /// <summary>
    /// FlightAssert.IntegrityChainValid verifies the SHA-256 hash chain across events.
    /// </summary>
    [Fact]
    public async Task IntegrityChainValid_VerifiesHashChain()
    {
        var integrityProvider = new Sha256IntegrityProvider(_serializer);
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"test\"}]").RootElement);
        await llm.CompleteAsync(request, CancellationToken.None);
        await recorder.StopAsync();

        FlightAssert.IntegrityChainValid(sink.Events, integrityProvider);
    }

    /// <summary>
    /// FlightAssert.ReplayMatchesTrace runs a workflow against replay stubs and asserts
    /// no ReplayMismatchException is thrown.
    /// </summary>
    [Fact]
    public async Task ReplayMatchesTrace_VerifiesWorkflow()
    {
        // First: record a trace to a temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"assert-replay-{Guid.NewGuid()}.jsonl");
        try
        {
            await using (var sink = new JsonlFileSink(tempPath, _serializer))
            {
                var options = new FlightRecorderOptions
                {
                    Mode = FlightMode.Record,
                    Sink = sink,
                    SynchronousMode = true
                };

                await using var recorder = new FlightRecorder(options);
                await recorder.StartAsync();

                var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
                var request = new LlmRequest("openai", "gpt-4",
                    JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"test\"}]").RootElement);
                await llm.CompleteAsync(request, CancellationToken.None);
                await recorder.StopAsync();
            }

            // Then: load and replay
            var store = new JsonlReplayStore(Path.GetDirectoryName(tempPath)!, _serializer);
            var events = await store.LoadFromFileAsync(tempPath);
            var index = new ReplayIndex(events, _serializer);

            await FlightAssert.ReplayMatchesTrace(index, _serializer, async (llm, tool) =>
            {
                var request = new LlmRequest("openai", "gpt-4",
                    JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"test\"}]").RootElement);
                await llm.CompleteAsync(request, CancellationToken.None);
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Records a simple Q&amp;A interaction and returns the events.
    /// </summary>
    private async Task<(IReadOnlyList<FlightEvent> Events, InMemorySink Sink)> RecordSimpleQA()
    {
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What is 2+2?\"}]").RootElement);
        await llm.CompleteAsync(request, CancellationToken.None);
        await recorder.StopAsync();

        return (sink.Events, sink);
    }
}
