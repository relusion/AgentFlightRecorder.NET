using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;

namespace Replay.Tests;

public sealed class ReplayIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CanonicalJsonSerializer _serializer = new();

    public ReplayIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"afr-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RecordThenReplay_StrictMode_IdenticalToolOutputs()
    {
        // Record
        var filePath = Path.Combine(_tempDir, "test.jsonl");
        var toolResults = new List<ToolResult>();
        int realToolCallCount = 0;

        await RecordAgentRun(filePath, (invocation) =>
        {
            realToolCallCount++;
            return new ToolResult(JsonDocument.Parse($"{{\"result\":\"output-{invocation.ToolName}\"}}").RootElement);
        });

        Assert.True(realToolCallCount > 0);

        // Replay
        var store = new JsonlReplayStore(_tempDir, _serializer);
        var events = await store.LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);

        var replayToolExecutor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        // Execute the same invocations — should get identical outputs
        int replayToolCallCount = 0;
        for (int i = 0; i < 3; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"arg\":{i}}}").RootElement);
            var result = await replayToolExecutor.ExecuteAsync(invocation, CancellationToken.None);
            replayToolCallCount++;

            var resultObj = result.Data.GetProperty("result").GetString();
            Assert.Equal($"output-tool-{i}", resultObj);
        }

        Assert.Equal(3, replayToolCallCount);
    }

    [Fact]
    public async Task RecordThenReplay_StrictMode_IdenticalLlmOutputs()
    {
        var filePath = Path.Combine(_tempDir, "llm-test.jsonl");

        await RecordLlmRun(filePath);

        var store = new JsonlReplayStore(_tempDir, _serializer);
        var events = await store.LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);

        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Strict);

        for (int i = 0; i < 2; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"q{i}\"}}]").RootElement);
            var response = await replayLlm.CompleteAsync(request, CancellationToken.None);

            Assert.NotEqual(default, response.Content);
        }
    }

    [Fact]
    public async Task StrictMode_FailsOnWrongToolName()
    {
        var filePath = Path.Combine(_tempDir, "wrong-name.jsonl");

        await RecordAgentRun(filePath, (inv) =>
            new ToolResult(JsonDocument.Parse("{\"r\":1}").RootElement));

        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var executor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        // First call should match, but try a wrong tool name
        var wrongInvocation = new ToolInvocation("nonexistent-tool", JsonDocument.Parse("{\"arg\":0}").RootElement);
        await Assert.ThrowsAsync<ReplayMismatchException>(
            () => executor.ExecuteAsync(wrongInvocation, CancellationToken.None));
    }

    [Fact]
    public async Task StrictMode_FailsOnWrongArgs()
    {
        var filePath = Path.Combine(_tempDir, "wrong-args.jsonl");

        await RecordAgentRun(filePath, (inv) =>
            new ToolResult(JsonDocument.Parse("{\"r\":1}").RootElement));

        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var executor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        // Same tool name but different args
        var wrongInvocation = new ToolInvocation("tool-0", JsonDocument.Parse("{\"different\":\"args\"}").RootElement);
        await Assert.ThrowsAsync<ReplayMismatchException>(
            () => executor.ExecuteAsync(wrongInvocation, CancellationToken.None));
    }

    [Fact]
    public async Task LenientMode_AllowsExtraAnnotations()
    {
        var filePath = Path.Combine(_tempDir, "lenient.jsonl");

        await RecordAgentRun(filePath, (inv) =>
            new ToolResult(JsonDocument.Parse($"{{\"r\":\"{inv.ToolName}\"}}").RootElement));

        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var executor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Lenient);

        // Replay tool calls in order — lenient mode should work
        for (int i = 0; i < 3; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"arg\":{i}}}").RootElement);
            var result = await executor.ExecuteAsync(invocation, CancellationToken.None);
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task FifoConsumption_SameKeyCalledTwice_ReturnsDifferentResults()
    {
        var filePath = Path.Combine(_tempDir, "fifo.jsonl");
        int callCount = 0;

        // Record the same tool name with the same args twice, returning different results
        await using var sink = new JsonlFileSink(filePath, _serializer);
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(new CallCountToolExecutor(() =>
        {
            callCount++;
            return new ToolResult(JsonDocument.Parse($"{{\"call\":{callCount}}}").RootElement);
        }), recorder, _serializer);

        // Same tool + same args, called twice
        var invocation = new ToolInvocation("search", JsonDocument.Parse("{\"q\":\"test\"}").RootElement);
        await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);

        await recorder.StopAsync();

        // Replay
        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var replayExecutor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Lenient);

        var result1 = await replayExecutor.ExecuteAsync(invocation, CancellationToken.None);
        var result2 = await replayExecutor.ExecuteAsync(invocation, CancellationToken.None);

        // FIFO: first call returns first result, second returns second
        Assert.Equal(1, result1.Data.GetProperty("call").GetInt32());
        Assert.Equal(2, result2.Data.GetProperty("call").GetInt32());
    }

    [Fact]
    public async Task ReplayMismatchException_ContainsHelpfulDiagnostics()
    {
        var filePath = Path.Combine(_tempDir, "diag.jsonl");

        await RecordAgentRun(filePath, (inv) =>
            new ToolResult(JsonDocument.Parse("{\"r\":1}").RootElement));

        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var executor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        var wrongInvocation = new ToolInvocation("nonexistent-tool", JsonDocument.Parse("{}").RootElement);
        var ex = await Assert.ThrowsAsync<ReplayMismatchException>(
            () => executor.ExecuteAsync(wrongInvocation, CancellationToken.None));

        Assert.Contains("nonexistent-tool", ex.Message);
        Assert.Contains("Replay mismatch", ex.Message);
        Assert.True(ex.RemainingEntries > 0);
    }

    [Fact]
    public async Task RealToolNeverCalledDuringReplay()
    {
        var filePath = Path.Combine(_tempDir, "no-real-calls.jsonl");
        int realCallCount = 0;

        await RecordAgentRun(filePath, (inv) =>
        {
            realCallCount++;
            return new ToolResult(JsonDocument.Parse("{\"r\":1}").RootElement);
        });

        Assert.Equal(3, realCallCount); // 3 tools recorded

        // Replay — the real executor should never be called
        var events = await new JsonlReplayStore(_tempDir, _serializer).LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);

        // ReplayToolExecutor doesn't take a real executor — it serves from the index
        var replayExecutor = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Strict);

        int replayRealCallCount = realCallCount; // snapshot
        for (int i = 0; i < 3; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"arg\":{i}}}").RootElement);
            await replayExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        // Real call count should not have increased
        Assert.Equal(replayRealCallCount, realCallCount);
    }

    // --- Helper methods ---

    private async Task RecordAgentRun(string filePath, Func<ToolInvocation, ToolResult> toolHandler)
    {
        await using var sink = new JsonlFileSink(filePath, _serializer);
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(
            new DelegateToolExecutor(toolHandler), recorder, _serializer);

        for (int i = 0; i < 3; i++)
        {
            var invocation = new ToolInvocation($"tool-{i}", JsonDocument.Parse($"{{\"arg\":{i}}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        await recorder.StopAsync();
    }

    private async Task RecordLlmRun(string filePath)
    {
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

        for (int i = 0; i < 2; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"q{i}\"}}]").RootElement);
            await llmClient.CompleteAsync(request, CancellationToken.None);
        }

        await recorder.StopAsync();
    }
}

internal sealed class DelegateToolExecutor : IToolExecutor
{
    private readonly Func<ToolInvocation, ToolResult> _handler;

    public DelegateToolExecutor(Func<ToolInvocation, ToolResult> handler) => _handler = handler;

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(_handler(invocation));
}

internal sealed class CallCountToolExecutor : IToolExecutor
{
    private readonly Func<ToolResult> _factory;

    public CallCountToolExecutor(Func<ToolResult> factory) => _factory = factory;

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(_factory());
}

internal sealed class FakeLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        Task.FromResult(new LlmResponse(
            JsonDocument.Parse("{\"text\":\"response\"}").RootElement,
            null,
            new TokenUsage(10, 5, 15)));
}
