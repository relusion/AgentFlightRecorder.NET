using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;
using AzureOpenAISample.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AzureOpenAISample.Scenarios;

// In test projects, FlightRecorderFixture automates this setup.
public static class ReplayScenario
{
    public static async Task<List<string>> RunAsync(
        string traceFile,
        ICanonicalJsonSerializer serializer,
        string deploymentName)
    {
        var outputs = new List<string>();

        // Load recorded events from JSONL
        var replayStore = new JsonlReplayStore(Path.GetDirectoryName(traceFile)!, serializer);
        var events = await replayStore.LoadFromFileAsync(traceFile);
        Console.WriteLine($"  Loaded {events.Count} events from trace.");

        // Build replay index — maps request hashes to recorded responses
        var replayIndex = new ReplayIndex(events, serializer);
        var replayClient = new ReplayLlmClient(replayIndex, serializer);
        var replayToolExecutor = new ReplayToolExecutor(replayIndex, serializer);

        // Adapter and filter mirror the recording configuration.
        var adapterOptions = new SemanticKernelAdapterOptions
        {
            ProviderName = "AzureOpenAI",
            DefaultModelName = deploymentName,
            RecordFunctionCalls = true,
        };
        var adapter = new SemanticKernelChatCompletionAdapter(replayClient, adapterOptions);
        var filter = new FlightRecorderFunctionFilter(replayToolExecutor, adapterOptions);

        // Build kernel with same plugins as recording — required so that tool definitions
        // in the serialized LlmRequest match the recorded hash. During replay, the
        // FlightRecorderFunctionFilter intercepts tool calls and serves recorded results
        // instead of executing real plugin code.
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(adapter);
        kernelBuilder.Services.AddSingleton<IAutoFunctionInvocationFilter>(filter);
        kernelBuilder.Plugins.AddFromType<WeatherPlugin>();
        kernelBuilder.Plugins.AddFromType<UtilityPlugin>();
        var kernel = kernelBuilder.Build();

        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory(
            "You are a helpful weather assistant. You have access to tools for looking up weather, " +
            "searching locations, and converting temperatures. Always use the tools when asked about " +
            "weather or locations. Be concise in your responses.");

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Replay the same 4-turn conversation with identical messages.
        // SK's auto-function-calling loop drives the same sequence of LLM calls
        // and tool invocations as recording, with all responses served from the replay index.
        string[] userMessages =
        [
            "What's the weather like in Seattle?",
            "Can you find more about this location?",
            "Convert 72°F to Celsius",
            "Give me a brief summary of everything"
        ];

        for (int i = 0; i < userMessages.Length; i++)
        {
            history.AddUserMessage(userMessages[i]);
            var result = await chatService.GetChatMessageContentsAsync(history, settings, kernel);
            var content = result[^1].Content ?? "";
            history.Add(result[^1]);
            Console.WriteLine($"  [Turn {i + 1}] Replayed: {ConsoleHelper.Truncate(content)}");
            outputs.Add(content);
        }

        return outputs;
    }
}
