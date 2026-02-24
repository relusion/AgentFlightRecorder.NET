using System.Text;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AzureOpenAISample.Scenarios;

public static class RecordScenario
{
    public static async Task<List<string>> RunAsync(
        Kernel kernel,
        IFlightRecorder recorder,
        ICanonicalJsonSerializer serializer)
    {
        var outputs = new List<string>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        await recorder.StartAsync();

        var history = new ChatHistory(
            "You are a helpful weather assistant. You have access to tools for looking up weather, " +
            "searching locations, and converting temperatures. Always use the tools when asked about " +
            "weather or locations. Be concise in your responses.");

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Planning span: wraps initial setup
        using (recorder.StartSpan("planning", FlightSpanKind.Planning))
        {
            Console.WriteLine("  [Planning] System prompt configured, tools registered.");
        }

        // Acting span: wraps the multi-turn conversation
        using (recorder.StartSpan("conversation", FlightSpanKind.Acting))
        {
            // --- Turn 1: Weather query → triggers GetWeather tool call ---
            Console.WriteLine("  [Turn 1] User: What's the weather like in Seattle?");
            history.AddUserMessage("What's the weather like in Seattle?");
            var result1 = await chatService.GetChatMessageContentsAsync(history, settings, kernel);
            var content1 = result1[^1].Content ?? "";
            history.Add(result1[^1]);
            Console.WriteLine($"  [Turn 1] Assistant: {ConsoleHelper.Truncate(content1)}");
            outputs.Add(content1);

            // --- Turn 2: Location query → triggers SearchLocation tool call ---
            Console.WriteLine("  [Turn 2] User: Can you find more about this location?");
            history.AddUserMessage("Can you find more about this location?");
            var result2 = await chatService.GetChatMessageContentsAsync(history, settings, kernel);
            var content2 = result2[^1].Content ?? "";
            history.Add(result2[^1]);
            Console.WriteLine($"  [Turn 2] Assistant: {ConsoleHelper.Truncate(content2)}");
            outputs.Add(content2);

            // --- Turn 3: Temperature conversion → triggers ConvertTemperature tool call ---
            Console.WriteLine("  [Turn 3] User: Convert 72°F to Celsius");
            history.AddUserMessage("Convert 72°F to Celsius");
            var result3 = await chatService.GetChatMessageContentsAsync(history, settings, kernel);
            var content3 = result3[^1].Content ?? "";
            history.Add(result3[^1]);
            Console.WriteLine($"  [Turn 3] Assistant: {ConsoleHelper.Truncate(content3)}");
            outputs.Add(content3);

            // Checkpoint: save conversation state after turn 3
            await recorder.CheckpointAsync(
                "after-turn-3",
                new { TurnCount = 3, MessageCount = history.Count },
                new JsonStateSerializer());
            Console.WriteLine("  [Checkpoint] Conversation state saved after turn 3.");

            // --- Turn 4: Summary request (streaming) ---
            Console.WriteLine("  [Turn 4] User: Give me a brief summary of everything");
            history.AddUserMessage("Give me a brief summary of everything");

            var streamBuilder = new StringBuilder();
            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, settings, kernel))
            {
                if (chunk.Content is not null)
                    streamBuilder.Append(chunk.Content);
            }

            var content4 = streamBuilder.ToString();
            history.AddAssistantMessage(content4);
            Console.WriteLine($"  [Turn 4] Assistant (streamed): {ConsoleHelper.Truncate(content4)}");
            outputs.Add(content4);

            // Annotation: tag this turn as the conversation summary
            await recorder.AnnotateAsync(
                "Conversation summary completed via streaming",
                new Dictionary<string, string>
                {
                    ["type"] = "summary",
                    ["turn"] = "4",
                    ["streaming"] = "true"
                });
            Console.WriteLine("  [Annotation] Tagged as conversation_summary.");
        }

        await recorder.StopAsync();
        return outputs;
    }

    private sealed class JsonStateSerializer : IStateSerializer
    {
        public JsonElement Serialize(object state) =>
            JsonSerializer.SerializeToElement(state,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
