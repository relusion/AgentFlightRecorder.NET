using AgentFlightRecorder.Core.Models;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Sends requests to an LLM provider.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}
