using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Sinks.Jsonl;

namespace JsonlSink.Tests;

public sealed class JsonlFileSinkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CanonicalJsonSerializer _serializer = new();

    public JsonlFileSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"afr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RecordToJsonl_ProducesValidJsonlFile()
    {
        var filePath = Path.Combine(_tempDir, "test-run.jsonl");
        await using var sink = new JsonlFileSink(filePath, _serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
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
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"q{i}\"}}]").RootElement);
            await llmClient.CompleteAsync(request, CancellationToken.None);
        }

        // 5 tool calls
        for (int i = 0; i < 5; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"i\":{i}}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        // 2 checkpoints
        await recorder.CheckpointAsync("s1", new { Step = 1 }, new JsonStateSerializer());
        await recorder.CheckpointAsync("s2", new { Step = 2 }, new JsonStateSerializer());

        await recorder.StopAsync();

        // Verify file exists and has content
        Assert.True(File.Exists(filePath));
        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.Equal(20, lines.Length);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            var doc = JsonDocument.Parse(line);
            Assert.NotNull(doc.RootElement.GetProperty("schemaVersion").GetString());
            Assert.NotNull(doc.RootElement.GetProperty("type").GetString());
        }

        // First event should be RunStarted, last should be RunCompleted
        var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("RunStarted", first.RootElement.GetProperty("type").GetString());

        var last = JsonDocument.Parse(lines[^1]);
        Assert.Equal("RunCompleted", last.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RecordToJsonl_SequenceNumbersMonotonic()
    {
        var filePath = Path.Combine(_tempDir, "seq-test.jsonl");
        await using var sink = new JsonlFileSink(filePath, _serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        for (int i = 0; i < 10; i++)
            await recorder.AnnotateAsync($"note {i}");

        await recorder.StopAsync();

        var lines = await File.ReadAllLinesAsync(filePath);
        long prevSequence = 0;
        foreach (var line in lines)
        {
            var doc = JsonDocument.Parse(line);
            var seq = doc.RootElement.GetProperty("sequence").GetInt64();
            Assert.True(seq > prevSequence, $"Sequence {seq} should be > {prevSequence}");
            prevSequence = seq;
        }
    }
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
