using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Testing.Xunit;
using UnitTestingSample.Mocks;
using Xunit;

namespace UnitTestingSample;

/// <summary>
/// Basic recording using FlightRecorderFixture and InMemorySink.
/// </summary>
public sealed class BasicRecordingTests(FlightRecorderFixture fixture) : IClassFixture<FlightRecorderFixture>
{
    /// <summary>
    /// Minimal recording: one LLM call produces LlmRequest + LlmResponse events.
    /// </summary>
    [Fact]
    public async Task SimpleQA_RecordsLlmRequestAndResponse()
    {
        var llm = new RecordingLlmClient(new SampleLlmClient(), fixture.Recorder, fixture.Serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"What is 2+2?\"}]").RootElement);

        var response = await llm.CompleteAsync(request, CancellationToken.None);

        Assert.NotEqual(default, response.Content);
        FlightAssert.ContainsEventType(fixture.Events, FlightEventTypes.LlmRequest);
        FlightAssert.ContainsEventType(fixture.Events, FlightEventTypes.LlmResponse);
    }

    /// <summary>
    /// Multiple LLM calls produce proportionally more events.
    /// </summary>
    [Fact]
    public async Task MultiTurnConversation_RecordsAllTurns()
    {
        var llm = new RecordingLlmClient(new SampleLlmClient(), fixture.Recorder, fixture.Serializer);
        var eventsBefore = fixture.Events.Count;

        for (int i = 0; i < 3; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"Turn {i}\"}}]").RootElement);
            await llm.CompleteAsync(request, CancellationToken.None);
        }

        // Each LLM call emits LlmRequest + LlmResponse = 2 events per turn
        var newEvents = fixture.Events.Count - eventsBefore;
        Assert.True(newEvents >= 6, $"Expected at least 6 new events for 3 turns, got {newEvents}");
    }

    /// <summary>
    /// RecordingToolExecutor wraps tool calls and emits ToolCallStarted/ToolCallCompleted events.
    /// </summary>
    [Fact]
    public async Task ToolCallRecording_CapturesToolEvents()
    {
        var tools = new SampleToolExecutor(new Dictionary<string, ToolResult>
        {
            ["calculator"] = new ToolResult(
                JsonDocument.Parse("{\"result\":4}").RootElement),
        });
        var recordingTools = new RecordingToolExecutor(tools, fixture.Recorder, fixture.Serializer);

        var invocation = new ToolInvocation("calculator",
            JsonDocument.Parse("{\"expression\":\"2+2\"}").RootElement);
        var result = await recordingTools.ExecuteAsync(invocation, CancellationToken.None);

        Assert.Equal(4, result.Data.GetProperty("result").GetInt32());
        FlightAssert.ContainsEventType(fixture.Events, FlightEventTypes.ToolCallStarted);
        FlightAssert.ContainsEventType(fixture.Events, FlightEventTypes.ToolCallCompleted);
    }

    /// <summary>
    /// FlightRecorderFixture automatically emits RunStarted on initialization.
    /// </summary>
    [Fact]
    public void RecorderLifecycle_StartEmitsRunStartedEvent()
    {
        FlightAssert.ContainsEventType(fixture.Events, FlightEventTypes.RunStarted);
    }

    /// <summary>
    /// Demonstrates SynchronousMode=false with explicit drain. Most tests use SynchronousMode=true
    /// (via FlightRecorderFixture) for determinism; this shows the async alternative.
    /// </summary>
    [Fact]
    public async Task AsyncMode_RecordsEvents()
    {
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = false,
            Backpressure = new BackpressureOptions { ChannelCapacity = 16 }
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, fixture.Serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"async test\"}]").RootElement);
        await llm.CompleteAsync(request, CancellationToken.None);

        // StopAsync drains the channel, ensuring all events are flushed
        await recorder.StopAsync();

        Assert.True(sink.Events.Count >= 4, "Expected RunStarted + LlmRequest + LlmResponse + RunCompleted");
        FlightAssert.ContainsEventType(sink.Events, FlightEventTypes.LlmRequest);
    }
}
