using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;

namespace UnitTestingSample.Mocks;

/// <summary>
/// Returns responses from a queue in FIFO order. Use for multi-turn agent scenarios
/// such as golden trace generation. Consumers can replace this with their preferred mocking framework.
/// </summary>
public sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Queue<LlmResponse> _responses;
    private readonly int _totalResponses;

    public ScriptedLlmClient(IEnumerable<LlmResponse> responses)
    {
        _responses = new(responses);
        _totalResponses = _responses.Count;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"ScriptedLlmClient exhausted all {_totalResponses} scripted responses. " +
                "Add more responses or verify the test scenario matches the script.");
        return Task.FromResult(_responses.Dequeue());
    }

    /// <summary>
    /// Creates the scripted responses for the weather/convert/summarize scenario
    /// used in golden trace generation.
    /// </summary>
    public static ScriptedLlmClient CreateWeatherScenario() => new(
    [
        // Turn 1: LLM wants to call get_weather tool
        new LlmResponse(
            JsonSerializer.SerializeToElement(new { text = "Let me check the weather for you." }),
            JsonSerializer.SerializeToElement(new[] { new { name = "get_weather", arguments = new { city = "Seattle" } } }),
            new TokenUsage(15, 20, 35)),

        // Turn 2: LLM wants to call convert_temperature tool
        new LlmResponse(
            JsonSerializer.SerializeToElement(new { text = "I'll convert that to Celsius." }),
            JsonSerializer.SerializeToElement(new[] { new { name = "convert_temperature", arguments = new { value = 72, from = "F", to = "C" } } }),
            new TokenUsage(25, 15, 40)),

        // Turn 3: Final summary, no tool calls
        new LlmResponse(
            JsonSerializer.SerializeToElement(new { text = "The weather in Seattle is 72°F (22.2°C) and sunny." }),
            null,
            new TokenUsage(30, 25, 55)),
    ]);
}
