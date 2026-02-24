using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Recording;

public sealed class RecordingIntegrationTests : IAsyncDisposable
{
    private readonly InMemorySink _sink = new();
    private readonly CanonicalJsonSerializer _serializer = new();

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync();
    }

    [Fact]
    public async Task SynchronousMode_RecordsFullAgentLoop()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llmClient = new RecordingLlmClient(new FakeLlmClient(), recorder, _serializer);
        var toolExecutor = new RecordingToolExecutor(new FakeToolExecutor(), recorder, _serializer);

        // 3 LLM calls
        for (int i = 0; i < 3; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"question {i}\"}}]").RootElement);
            await llmClient.CompleteAsync(request, CancellationToken.None);
        }

        // 5 tool calls
        for (int i = 0; i < 5; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"arg\":{i}}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        // 2 checkpoints
        await recorder.CheckpointAsync("step-1", new { Step = 1 }, new JsonStateSerializer());
        await recorder.CheckpointAsync("step-2", new { Step = 2 }, new JsonStateSerializer());

        await recorder.StopAsync();

        // Verify event count: RunStarted + 3*(LlmRequest+LlmResponse) + 5*(ToolCallStarted+ToolCallCompleted) + 2*StateCheckpoint + RunCompleted
        // = 1 + 6 + 10 + 2 + 1 = 20
        Assert.Equal(20, _sink.Events.Count);
    }

    [Fact]
    public async Task SynchronousMode_EventTypesInCorrectOrder()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llmClient = new RecordingLlmClient(new FakeLlmClient(), recorder, _serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"hello\"}]").RootElement);
        await llmClient.CompleteAsync(request, CancellationToken.None);

        await recorder.StopAsync();

        Assert.Equal(FlightEventTypes.RunStarted, _sink.Events[0].Type);
        Assert.Equal(FlightEventTypes.LlmRequest, _sink.Events[1].Type);
        Assert.Equal(FlightEventTypes.LlmResponse, _sink.Events[2].Type);
        Assert.Equal(FlightEventTypes.RunCompleted, _sink.Events[^1].Type);
    }

    [Fact]
    public async Task SynchronousMode_SequenceNumbersMonotonicallyIncreasing()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(new FakeToolExecutor(), recorder, _serializer);
        for (int i = 0; i < 3; i++)
        {
            await toolExecutor.ExecuteAsync(
                new ToolInvocation("search", JsonDocument.Parse("{}").RootElement),
                CancellationToken.None);
        }

        await recorder.StopAsync();

        for (int i = 1; i < _sink.Events.Count; i++)
        {
            Assert.True(_sink.Events[i].Sequence > _sink.Events[i - 1].Sequence,
                $"Event {i} sequence {_sink.Events[i].Sequence} should be > event {i - 1} sequence {_sink.Events[i - 1].Sequence}");
        }
    }

    [Fact]
    public async Task SynchronousMode_TimestampsAreOrdered()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        await recorder.AnnotateAsync("note 1");
        await recorder.AnnotateAsync("note 2");
        await recorder.AnnotateAsync("note 3");

        await recorder.StopAsync();

        for (int i = 1; i < _sink.Events.Count; i++)
        {
            Assert.True(_sink.Events[i].TimestampUtc >= _sink.Events[i - 1].TimestampUtc);
        }
    }

    [Fact]
    public async Task SynchronousMode_ToolCallEventsContainCorrectToolName()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(new FakeToolExecutor(), recorder, _serializer);
        await toolExecutor.ExecuteAsync(
            new ToolInvocation("web-search", JsonDocument.Parse("{\"q\":\"hello\"}").RootElement),
            CancellationToken.None);

        await recorder.StopAsync();

        var startEvent = _sink.Events.First(e => e.Type == FlightEventTypes.ToolCallStarted);
        var payload = startEvent.GetPayload<ToolCallStartedPayload>();
        Assert.Equal("web-search", payload.ToolName);
    }

    [Fact]
    public async Task PassthroughMode_ProducesNoEvents()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Passthrough,
            Sink = _sink,
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.AnnotateAsync("should not appear");
        await recorder.StopAsync();

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task SpanEvents_EmittedCorrectly()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        using (var span = recorder.StartSpan("outer", FlightSpanKind.Planning))
        {
            using (var childSpan = span.StartChildSpan("inner", FlightSpanKind.ToolExecution))
            {
                // Work happens here
            }
        }

        await recorder.StopAsync();

        var spanEvents = _sink.Events.Where(e =>
            e.Type == FlightEventTypes.SpanStarted || e.Type == FlightEventTypes.SpanCompleted).ToList();

        // 2 starts + 2 completes = 4
        Assert.Equal(4, spanEvents.Count);
        Assert.Equal(FlightEventTypes.SpanStarted, spanEvents[0].Type);
        Assert.Equal(FlightEventTypes.SpanStarted, spanEvents[1].Type);
        Assert.Equal(FlightEventTypes.SpanCompleted, spanEvents[2].Type);
        Assert.Equal(FlightEventTypes.SpanCompleted, spanEvents[3].Type);
    }

    [Fact]
    public async Task AsyncMode_RecordsEvents()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = false,
            Backpressure = new BackpressureOptions
            {
                Strategy = BackpressureStrategy.Block,
                ChannelCapacity = 1024
            }
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        await recorder.AnnotateAsync("async note");

        await recorder.StopAsync();

        // RunStarted + Annotation + RunCompleted = 3
        Assert.True(_sink.Events.Count >= 3,
            $"Expected at least 3 events, got {_sink.Events.Count}");
        Assert.Equal(FlightEventTypes.RunStarted, _sink.Events[0].Type);
        Assert.Equal(FlightEventTypes.RunCompleted, _sink.Events[^1].Type);
    }
}

/// <summary>In-memory sink for testing — stores events in a list.</summary>
internal sealed class InMemorySink : IFlightSink
{
    private readonly List<FlightEvent> _events = [];

    public IReadOnlyList<FlightEvent> Events => _events;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        _events.Add(evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        Task.FromResult(new LlmResponse(
            JsonDocument.Parse("{\"text\":\"response\"}").RootElement,
            null,
            new TokenUsage(10, 5, 15)));
}

internal sealed class FakeToolExecutor : IToolExecutor
{
    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new ToolResult(
            JsonDocument.Parse("{\"result\":\"ok\"}").RootElement));
}

internal sealed class JsonStateSerializer : IStateSerializer
{
    public JsonElement Serialize(object state) =>
        JsonSerializer.SerializeToElement(state);
}
