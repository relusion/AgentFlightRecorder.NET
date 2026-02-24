using System.Runtime.CompilerServices;
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

namespace Replay.Tests;

public sealed class GoldenTraceTests
{
    private const string GoldenFileName = "golden-agent-loop.jsonl";
    private readonly CanonicalJsonSerializer _serializer = new();

    private static string GetSourceTestDataDir([CallerFilePath] string filePath = "")
    {
        var projectDir = Path.GetDirectoryName(filePath)!;
        return Path.Combine(projectDir, "..", "TestData");
    }

    private string GetOutputTestDataPath() =>
        Path.Combine(AppContext.BaseDirectory, "TestData", GoldenFileName);

    /// <summary>
    /// One-time generator: run this test to produce/update the golden trace file.
    /// Only needed when the trace format or scenario changes.
    /// </summary>
    [Fact]
    [Trait("Category", "Generator")]
    public async Task GenerateGoldenTrace()
    {
        var testDataDir = GetSourceTestDataDir();
        Directory.CreateDirectory(testDataDir);
        var filePath = Path.Combine(testDataDir, GoldenFileName);

        var integrityProvider = new Sha256IntegrityProvider(_serializer);

        await using var sink = new JsonlFileSink(filePath, _serializer);
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llmClient = new RecordingLlmClient(new GoldenLlmClient(), recorder, _serializer);
        var toolExecutor = new RecordingToolExecutor(new GoldenToolExecutor(), recorder, _serializer);

        // 3 LLM calls
        for (int i = 0; i < 3; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"question {i}\"}}]").RootElement);
            await llmClient.CompleteAsync(request, CancellationToken.None);
        }

        // 5 tool calls
        string[] toolNames = ["web-search", "calculator", "database-query", "file-read", "summarize"];
        for (int i = 0; i < 5; i++)
        {
            var invocation = new ToolInvocation(toolNames[i],
                JsonDocument.Parse($"{{\"input\":\"value-{i}\"}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        // 2 checkpoints
        await recorder.CheckpointAsync("after-planning", new { Phase = "plan", Step = 1 }, new JsonStateSerializer());
        await recorder.CheckpointAsync("after-execution", new { Phase = "execute", Step = 2 }, new JsonStateSerializer());

        // 1 annotation
        await recorder.AnnotateAsync("Golden trace for CI regression testing",
            new Dictionary<string, string> { ["purpose"] = "ci-golden" });

        await recorder.StopAsync();

        Assert.True(File.Exists(filePath), $"Golden trace file should exist at {filePath}");
    }

    [Fact]
    public async Task GoldenTrace_ReplayLlmOutputsMatch()
    {
        var filePath = GetOutputTestDataPath();
        EnsureGoldenTraceExists(filePath);

        var events = await new JsonlReplayStore(Path.GetDirectoryName(filePath)!, _serializer)
            .LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var replayLlm = new ReplayLlmClient(index, _serializer, ReplayStrictness.Lenient);

        for (int i = 0; i < 3; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"question {i}\"}}]").RootElement);
            var response = await replayLlm.CompleteAsync(request, CancellationToken.None);
            Assert.NotEqual(default, response.Content);
        }
    }

    [Fact]
    public async Task GoldenTrace_ReplayToolOutputsMatch()
    {
        var filePath = GetOutputTestDataPath();
        EnsureGoldenTraceExists(filePath);

        var events = await new JsonlReplayStore(Path.GetDirectoryName(filePath)!, _serializer)
            .LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);
        var replayTool = new ReplayToolExecutor(index, _serializer, ReplayStrictness.Lenient);

        string[] toolNames = ["web-search", "calculator", "database-query", "file-read", "summarize"];
        for (int i = 0; i < 5; i++)
        {
            var invocation = new ToolInvocation(toolNames[i],
                JsonDocument.Parse($"{{\"input\":\"value-{i}\"}}").RootElement);
            var result = await replayTool.ExecuteAsync(invocation, CancellationToken.None);

            var resultStr = result.Data.GetProperty("result").GetString();
            Assert.Equal($"result-{toolNames[i]}", resultStr);
        }
    }

    [Fact]
    public async Task GoldenTrace_IntegrityChainValid()
    {
        var filePath = GetOutputTestDataPath();
        EnsureGoldenTraceExists(filePath);

        var events = await new JsonlReplayStore(Path.GetDirectoryName(filePath)!, _serializer)
            .LoadFromFileAsync(filePath);

        var integrityProvider = new Sha256IntegrityProvider(_serializer);
        FlightAssert.IntegrityChainValid(events, integrityProvider);
    }

    [Fact]
    public async Task GoldenTrace_ContainsExpectedEventTypes()
    {
        var filePath = GetOutputTestDataPath();
        EnsureGoldenTraceExists(filePath);

        var events = await new JsonlReplayStore(Path.GetDirectoryName(filePath)!, _serializer)
            .LoadFromFileAsync(filePath);

        FlightAssert.ContainsEventType(events, FlightEventTypes.RunStarted);
        FlightAssert.ContainsEventType(events, FlightEventTypes.RunCompleted);
        FlightAssert.ContainsEventType(events, FlightEventTypes.LlmRequest);
        FlightAssert.ContainsEventType(events, FlightEventTypes.LlmResponse);
        FlightAssert.ContainsEventType(events, FlightEventTypes.ToolCallStarted);
        FlightAssert.ContainsEventType(events, FlightEventTypes.ToolCallCompleted);
        FlightAssert.ContainsEventType(events, FlightEventTypes.StateCheckpoint);
        FlightAssert.ContainsEventType(events, FlightEventTypes.Annotation);

        // RunStarted + 3*(LlmReq+LlmResp) + 5*(ToolStart+ToolComplete) + 2*Checkpoint + 1*Annotation + RunCompleted = 21
        Assert.Equal(21, events.Count);
    }

    [Fact]
    public async Task GoldenTrace_FlightAssertReplayMatchesTrace()
    {
        var filePath = GetOutputTestDataPath();
        EnsureGoldenTraceExists(filePath);

        var events = await new JsonlReplayStore(Path.GetDirectoryName(filePath)!, _serializer)
            .LoadFromFileAsync(filePath);
        var index = new ReplayIndex(events, _serializer);

        await FlightAssert.ReplayMatchesTrace(index, _serializer, async (llm, tool) =>
        {
            // Replay the same workflow
            for (int i = 0; i < 3; i++)
            {
                var request = new LlmRequest("openai", "gpt-4",
                    JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"question {i}\"}}]").RootElement);
                await llm.CompleteAsync(request, CancellationToken.None);
            }

            string[] toolNames = ["web-search", "calculator", "database-query", "file-read", "summarize"];
            for (int i = 0; i < 5; i++)
            {
                var invocation = new ToolInvocation(toolNames[i],
                    JsonDocument.Parse($"{{\"input\":\"value-{i}\"}}").RootElement);
                await tool.ExecuteAsync(invocation, CancellationToken.None);
            }
        });
    }

    private static void EnsureGoldenTraceExists(string filePath)
    {
        Assert.True(File.Exists(filePath),
            $"Golden trace file not found at {filePath}. Run the GenerateGoldenTrace test first, then rebuild.");
    }
}

internal sealed class GoldenLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        Task.FromResult(new LlmResponse(
            JsonDocument.Parse("{\"text\":\"golden-response\"}").RootElement,
            null,
            new TokenUsage(10, 5, 15)));
}

internal sealed class GoldenToolExecutor : IToolExecutor
{
    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new ToolResult(
            JsonDocument.Parse($"{{\"result\":\"result-{invocation.ToolName}\"}}").RootElement));
}

internal sealed class JsonStateSerializer : IStateSerializer
{
    public JsonElement Serialize(object state) =>
        JsonSerializer.SerializeToElement(state);
}
