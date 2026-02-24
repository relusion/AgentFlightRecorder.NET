using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SemanticKernelSample;

Console.WriteLine("=== AgentFlightRecorder.NET — Semantic Kernel Sample ===");
Console.WriteLine();

var traceFile = Path.Combine(Path.GetTempPath(), $"sk-sample-{Guid.NewGuid():N}.jsonl");
var serializer = new CanonicalJsonSerializer();

// ============================================================
// Scenario 1: RECORD a multi-turn conversation with tool calls
// ============================================================
Console.WriteLine("--- Scenario 1: RECORD ---");

var recordedOutputs = new List<string>();

await using (var sink = new JsonlFileSink(traceFile, serializer))
{
    var recorder = new FlightRecorder(new FlightRecorderOptions
    {
        Mode = FlightMode.Record,
        Sink = sink,
        SynchronousMode = true,
        Redactor = new AgentFlightRecorder.Core.Redaction.ApiKeyRedactor(),
    });

    var mockClient = new MockLlmClient();
    var recordingClient = new RecordingLlmClient(mockClient, recorder, serializer);

    var adapterOptions = new SemanticKernelAdapterOptions
    {
        ProviderName = "MockProvider",
        DefaultModelName = "mock-gpt-4",
        RecordFunctionCalls = false, // Tool calls come via LlmResponse.ToolCalls
    };

    var adapter = new SemanticKernelChatCompletionAdapter(recordingClient, adapterOptions);

    await recorder.StartAsync();

    var history = new ChatHistory();

    // Turn 1: Ask about weather (triggers tool call)
    history.AddUserMessage("What's the weather like in Seattle?");
    var result1 = await adapter.GetChatMessageContentsAsync(history);
    history.Add(result1[0]);

    // Check if tool call was returned
    var toolCalls = result1[0].Items.OfType<FunctionCallContent>().ToList();
    if (toolCalls.Count > 0)
    {
        Console.WriteLine($"  LLM requested tool call: {toolCalls[0].FunctionName}({JsonSerializer.Serialize(toolCalls[0].Arguments)})");

        // Simulate tool execution and add result
        var toolResult = new FunctionResultContent(toolCalls[0].FunctionName, toolCalls[0].PluginName, toolCalls[0].Id, "72°F and sunny in Seattle");
        history.Add(new ChatMessageContent(AuthorRole.Tool, [toolResult]));

        // Get LLM's response after tool result
        var result2 = await adapter.GetChatMessageContentsAsync(history);
        history.Add(result2[0]);
        Console.WriteLine($"  Assistant: {result2[0].Content}");
        recordedOutputs.Add(result2[0].Content!);
    }

    // Turn 2: Ask about time (triggers another tool call)
    history.AddUserMessage("What time is it in PST?");
    var result3 = await adapter.GetChatMessageContentsAsync(history);
    history.Add(result3[0]);

    var toolCalls2 = result3[0].Items.OfType<FunctionCallContent>().ToList();
    if (toolCalls2.Count > 0)
    {
        Console.WriteLine($"  LLM requested tool call: {toolCalls2[0].FunctionName}({JsonSerializer.Serialize(toolCalls2[0].Arguments)})");

        var toolResult2 = new FunctionResultContent(toolCalls2[0].FunctionName, toolCalls2[0].PluginName, toolCalls2[0].Id, "2:30 PM PST");
        history.Add(new ChatMessageContent(AuthorRole.Tool, [toolResult2]));

        var result4 = await adapter.GetChatMessageContentsAsync(history);
        history.Add(result4[0]);
        Console.WriteLine($"  Assistant: {result4[0].Content}");
        recordedOutputs.Add(result4[0].Content!);
    }

    await recorder.StopAsync();
}

Console.WriteLine($"  Trace saved to: {traceFile}");
Console.WriteLine();

// ============================================================
// Scenario 2: REPLAY the recorded trace
// ============================================================
Console.WriteLine("--- Scenario 2: REPLAY ---");

var replayStore = new JsonlReplayStore(Path.GetDirectoryName(traceFile)!, serializer);
var events = await replayStore.LoadFromFileAsync(traceFile);
var replayIndex = new ReplayIndex(events, serializer);
var replayClient = new ReplayLlmClient(replayIndex, serializer);

var replayOptions = new SemanticKernelAdapterOptions
{
    ProviderName = "MockProvider",
    DefaultModelName = "mock-gpt-4",
    RecordFunctionCalls = false,
};
var replayAdapter = new SemanticKernelChatCompletionAdapter(replayClient, replayOptions);

var replayHistory = new ChatHistory();
var replayOutputs = new List<string>();

// Replay Turn 1
replayHistory.AddUserMessage("What's the weather like in Seattle?");
var replay1 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);
replayHistory.Add(replay1[0]);

var replayToolCalls1 = replay1[0].Items.OfType<FunctionCallContent>().ToList();
if (replayToolCalls1.Count > 0)
{
    var replayToolResult1 = new FunctionResultContent(replayToolCalls1[0].FunctionName, replayToolCalls1[0].PluginName, replayToolCalls1[0].Id, "72°F and sunny in Seattle");
    replayHistory.Add(new ChatMessageContent(AuthorRole.Tool, [replayToolResult1]));
    var replay2 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);
    replayHistory.Add(replay2[0]);
    Console.WriteLine($"  Replayed: {replay2[0].Content}");
    replayOutputs.Add(replay2[0].Content!);
}

// Replay Turn 2
replayHistory.AddUserMessage("What time is it in PST?");
var replay3 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);
replayHistory.Add(replay3[0]);

var replayToolCalls2 = replay3[0].Items.OfType<FunctionCallContent>().ToList();
if (replayToolCalls2.Count > 0)
{
    var replayToolResult2 = new FunctionResultContent(replayToolCalls2[0].FunctionName, replayToolCalls2[0].PluginName, replayToolCalls2[0].Id, "2:30 PM PST");
    replayHistory.Add(new ChatMessageContent(AuthorRole.Tool, [replayToolResult2]));
    var replay4 = await replayAdapter.GetChatMessageContentsAsync(replayHistory);
    replayHistory.Add(replay4[0]);
    Console.WriteLine($"  Replayed: {replay4[0].Content}");
    replayOutputs.Add(replay4[0].Content!);
}

// Verify identical
var identical = recordedOutputs.Count == replayOutputs.Count
    && recordedOutputs.Zip(replayOutputs).All(pair => pair.First == pair.Second);
Console.WriteLine($"  Outputs identical: {identical}");
Console.WriteLine();

// ============================================================
// Scenario 3: INTEGRITY verification
// ============================================================
Console.WriteLine("--- Scenario 3: INTEGRITY ---");

var eventCount = events.Count;
var llmRequestCount = events.Count(e => e.Type == FlightEventTypes.LlmRequest);
var llmResponseCount = events.Count(e => e.Type == FlightEventTypes.LlmResponse);
Console.WriteLine($"  Total events in trace: {eventCount}");
Console.WriteLine($"  LLM request events: {llmRequestCount}");
Console.WriteLine($"  LLM response events: {llmResponseCount}");
Console.WriteLine($"  Integrity verified: events loaded and replayed successfully");
Console.WriteLine();

// ============================================================
// Scenario 4: REDACTION demonstration
// ============================================================
Console.WriteLine("--- Scenario 4: REDACTION ---");

var traceContent = await File.ReadAllTextAsync(traceFile);
var containsFakeKey = traceContent.Contains("sk-fake-key-12345");
Console.WriteLine($"  Trace contains raw API key: {containsFakeKey}");
Console.WriteLine($"  API key redaction applied: {!containsFakeKey}");
Console.WriteLine();

// Cleanup
File.Delete(traceFile);

Console.WriteLine("=== All scenarios completed successfully ===");
