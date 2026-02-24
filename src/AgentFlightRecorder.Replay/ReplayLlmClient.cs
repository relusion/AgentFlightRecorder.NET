using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;

namespace AgentFlightRecorder.Replay;

/// <summary>
/// Returns recorded LLM responses from a <see cref="ReplayIndex"/> instead of calling a real provider.
/// </summary>
public sealed class ReplayLlmClient : ILlmClient
{
    private static readonly ActivitySource s_activitySource = new("AgentFlightRecorder");
    private readonly ReplayIndex _index;
    private readonly ReplayStrictness _strictness;
    private readonly ICanonicalJsonSerializer _serializer;

    public ReplayLlmClient(ReplayIndex index, ICanonicalJsonSerializer serializer, ReplayStrictness strictness = ReplayStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(serializer);
        _index = index;
        _serializer = serializer;
        _strictness = strictness;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("Replay.LlmComplete");
        activity?.SetTag("llm.model", request.Model);

        var argsHash = ComputeArgsHash(request);
        var key = new InvocationKey("llm", request.Model, argsHash, 1, null);

        var entry = _index.TryDequeue(key, _strictness);
        if (entry is null)
        {
            var closestMatches = _index.GetClosestMatches(key);
            throw new ReplayMismatchException(key, closestMatches, _index.RemainingCount, _index.PeekNextStrict());
        }

        var responseEvent = entry.ResponseEvent;
        if (responseEvent.Type == FlightEventTypes.LlmError)
        {
            var errorPayload = responseEvent.GetPayload<LlmErrorPayload>();
            throw new InvalidOperationException($"Replay: LLM error recorded — {errorPayload.ErrorType}: {errorPayload.ErrorMessage}");
        }

        var payload = responseEvent.GetPayload<LlmResponsePayload>();
        var response = new LlmResponse(payload.Content, payload.ToolCalls, payload.Usage);
        return Task.FromResult(response);
    }

    private string ComputeArgsHash(LlmRequest request)
    {
        var payloadObj = new LlmRequestPayload(request.Provider, request.Model, request.Messages, request.Settings, request.ToolDefinitions);
        var bytes = _serializer.SerializeToUtf8Bytes(payloadObj);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
