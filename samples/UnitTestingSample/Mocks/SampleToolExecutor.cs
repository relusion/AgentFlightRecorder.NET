using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;

namespace UnitTestingSample.Mocks;

/// <summary>
/// Returns fixed tool results by tool name. Use for scenarios with known tool calls.
/// Consumers can replace this with their preferred mocking framework (Moq, NSubstitute, etc.).
/// </summary>
public sealed class SampleToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, ToolResult> _results;

    public SampleToolExecutor(Dictionary<string, ToolResult> results) =>
        _results = results;

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(_results[invocation.ToolName]);

    /// <summary>
    /// Creates the tool results for the weather/convert scenario used in golden trace generation.
    /// </summary>
    public static SampleToolExecutor CreateWeatherScenario() => new(new Dictionary<string, ToolResult>
    {
        ["get_weather"] = new ToolResult(
            JsonSerializer.SerializeToElement(new { temperature = 72, unit = "F", condition = "sunny", city = "Seattle" })),
        ["convert_temperature"] = new ToolResult(
            JsonSerializer.SerializeToElement(new { result = 22.2, from = "F", to = "C" })),
    });
}
