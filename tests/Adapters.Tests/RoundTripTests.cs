using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Testing.Xunit;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Adapters.Tests;

public sealed class RoundTripTests
{
    [Fact]
    public async Task Record_ThenReplay_ProducesIdenticalOutput()
    {
        var serializer = new CanonicalJsonSerializer();
        var sink = new InMemorySink();

        // --- RECORD PHASE ---
        var recorder = new FlightRecorder(new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true,
        });

        var mockInnerClient = new ScriptedLlmClient(
        [
            new LlmResponse(JsonSerializer.SerializeToElement("Hello! How can I help you today?")),
            new LlmResponse(JsonSerializer.SerializeToElement("The weather in Seattle is 72°F and sunny.")),
        ]);

        var recordingClient = new RecordingLlmClient(mockInnerClient, recorder, serializer);
        var adapterOptions = new SemanticKernelAdapterOptions
        {
            ProviderName = "TestProvider",
            DefaultModelName = "test-model",
            RecordFunctionCalls = false,
        };
        var recordAdapter = new SemanticKernelChatCompletionAdapter(recordingClient, adapterOptions);

        await recorder.StartAsync();

        // Turn 1
        var history = new ChatHistory();
        history.AddUserMessage("Hello");
        var recordResult1 = await recordAdapter.GetChatMessageContentsAsync(history);

        // Turn 2
        history.Add(recordResult1[0]);
        history.AddUserMessage("What's the weather in Seattle?");
        var recordResult2 = await recordAdapter.GetChatMessageContentsAsync(history);

        await recorder.StopAsync();

        // --- REPLAY PHASE ---
        var replayIndex = new ReplayIndex(sink.Events, serializer);
        var replayClient = new ReplayLlmClient(replayIndex, serializer);
        var replayAdapter = new SemanticKernelChatCompletionAdapter(replayClient, adapterOptions);

        // Replay Turn 1
        var replayHistory = new ChatHistory();
        replayHistory.AddUserMessage("Hello");
        var replayResult1 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);

        // Replay Turn 2
        replayHistory.Add(replayResult1[0]);
        replayHistory.AddUserMessage("What's the weather in Seattle?");
        var replayResult2 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);

        // --- VERIFY ---
        Assert.Equal(recordResult1[0].Content, replayResult1[0].Content);
        Assert.Equal(recordResult2[0].Content, replayResult2[0].Content);
    }

    [Fact]
    public async Task Record_WithToolCalls_ThenReplay()
    {
        var serializer = new CanonicalJsonSerializer();
        var sink = new InMemorySink();

        // --- RECORD PHASE ---
        var recorder = new FlightRecorder(new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true,
        });

        // LLM response includes a tool call
        var toolCallsJson = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                functionName = "GetWeather",
                pluginName = "Weather",
                id = "call_1",
                args = new { location = "Seattle" }
            }
        });

        var mockInnerClient = new ScriptedLlmClient(
        [
            new LlmResponse(JsonSerializer.SerializeToElement((string?)null), toolCallsJson),
            new LlmResponse(JsonSerializer.SerializeToElement("The weather in Seattle is 72°F.")),
        ]);

        var recordingClient = new RecordingLlmClient(mockInnerClient, recorder, serializer);
        var adapterOptions = new SemanticKernelAdapterOptions
        {
            ProviderName = "TestProvider",
            DefaultModelName = "test-model",
            RecordFunctionCalls = false, // We're testing LLM-level tool calls in response, not filter interception
        };
        var recordAdapter = new SemanticKernelChatCompletionAdapter(recordingClient, adapterOptions);

        await recorder.StartAsync();

        var history = new ChatHistory();
        history.AddUserMessage("What's the weather?");
        var result1 = await recordAdapter.GetChatMessageContentsAsync(history);

        // Verify tool call is in the result
        var funcCalls = result1[0].Items.OfType<FunctionCallContent>().ToList();
        Assert.Single(funcCalls);
        Assert.Equal("GetWeather", funcCalls[0].FunctionName);

        // Simulate adding tool result and getting final answer
        history.Add(result1[0]);
        var toolResultItems = new ChatMessageContentItemCollection
        {
            new FunctionResultContent("GetWeather", "Weather", "call_1", "72°F, Sunny")
        };
        history.Add(new ChatMessageContent(AuthorRole.Tool, toolResultItems));
        var result2 = await recordAdapter.GetChatMessageContentsAsync(history);

        await recorder.StopAsync();

        // --- REPLAY PHASE ---
        var replayIndex = new ReplayIndex(sink.Events, serializer);
        var replayClient = new ReplayLlmClient(replayIndex, serializer);
        var replayAdapter = new SemanticKernelChatCompletionAdapter(replayClient, adapterOptions);

        var replayHistory = new ChatHistory();
        replayHistory.AddUserMessage("What's the weather?");
        var replayResult1 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);

        var replayFuncCalls = replayResult1[0].Items.OfType<FunctionCallContent>().ToList();
        Assert.Single(replayFuncCalls);
        Assert.Equal("GetWeather", replayFuncCalls[0].FunctionName);

        replayHistory.Add(replayResult1[0]);
        var replayToolItems = new ChatMessageContentItemCollection
        {
            new FunctionResultContent("GetWeather", "Weather", "call_1", "72°F, Sunny")
        };
        replayHistory.Add(new ChatMessageContent(AuthorRole.Tool, replayToolItems));
        var replayResult2 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);

        // --- VERIFY ---
        Assert.Equal(result2[0].Content, replayResult2[0].Content);
    }

    private sealed class ScriptedLlmClient : ILlmClient
    {
        private readonly Queue<LlmResponse> _responses;

        public ScriptedLlmClient(IEnumerable<LlmResponse> responses)
        {
            _responses = new Queue<LlmResponse>(responses);
        }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more scripted responses available");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
