using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;

namespace SemanticKernelSample;

/// <summary>
/// Scripted LLM client that returns deterministic responses for the sample scenario.
/// No API keys or external services required.
/// </summary>
internal sealed class MockLlmClient : ILlmClient
{
    private readonly Queue<LlmResponse> _responses = new();

    public MockLlmClient()
    {
        // Turn 1: User asks about weather → LLM requests GetWeather tool
        var toolCall1 = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                functionName = "GetWeather",
                pluginName = "Tools",
                id = "call_weather_1",
                args = new { location = "Seattle", apiKey = "sk-fake-key-12345" }
            }
        });
        _responses.Enqueue(new LlmResponse(
            JsonSerializer.SerializeToElement((string?)null),
            toolCall1,
            new TokenUsage(50, 20, 70)));

        // Turn 2: After tool result → LLM gives weather answer
        _responses.Enqueue(new LlmResponse(
            JsonSerializer.SerializeToElement("The weather in Seattle is 72°F and sunny. It's a beautiful day!"),
            null,
            new TokenUsage(120, 30, 150)));

        // Turn 3: User asks about time → LLM requests GetCurrentTime tool
        var toolCall2 = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                functionName = "GetCurrentTime",
                pluginName = "Tools",
                id = "call_time_1",
                args = new { timezone = "PST", apiKey = (string?)null }
            }
        });
        _responses.Enqueue(new LlmResponse(
            JsonSerializer.SerializeToElement((string?)null),
            toolCall2,
            new TokenUsage(80, 15, 95)));

        // Turn 4: After tool result → LLM gives time answer
        _responses.Enqueue(new LlmResponse(
            JsonSerializer.SerializeToElement("The current time in PST is 2:30 PM. Have a great afternoon!"),
            null,
            new TokenUsage(150, 25, 175)));
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException("No more scripted responses.");

        return Task.FromResult(_responses.Dequeue());
    }
}
