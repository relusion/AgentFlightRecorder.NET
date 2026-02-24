using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;

namespace UnitTestingSample.Mocks;

/// <summary>
/// Returns a single canned LLM response. Use for basic recording tests.
/// Consumers can replace this with their preferred mocking framework (Moq, NSubstitute, etc.).
/// </summary>
public sealed class SampleLlmClient : ILlmClient
{
    private static readonly JsonElement CannedContent =
        JsonSerializer.SerializeToElement(new { text = "Hello! I'm an assistant." });

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        Task.FromResult(new LlmResponse(CannedContent));
}
