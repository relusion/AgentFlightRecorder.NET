using System.Diagnostics;
using System.Security.Cryptography;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Serialization;

namespace AgentFlightRecorder.Core.Recording;

/// <summary>
/// Decorator that wraps an <see cref="ILlmClient"/> and records LLM call events.
/// </summary>
public sealed class RecordingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly FlightRecorder _recorder;
    private readonly ICanonicalJsonSerializer _serializer;

    public RecordingLlmClient(ILlmClient inner, FlightRecorder recorder, ICanonicalJsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(serializer);
        _inner = inner;
        _recorder = recorder;
        _serializer = serializer;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (_recorder.Mode == FlightMode.Passthrough)
            return await _inner.CompleteAsync(request, ct);

        var requestPayload = new LlmRequestPayload(
            request.Provider,
            request.Model,
            request.Messages,
            request.Settings,
            request.ToolDefinitions);

        await _recorder.EmitAsync(FlightEventTypes.LlmRequest, requestPayload, ct);

        try
        {
            var response = await _inner.CompleteAsync(request, ct);

            var responsePayload = new LlmResponsePayload(
                request.Provider,
                request.Model,
                response.Content,
                response.ToolCalls,
                response.Usage);

            await _recorder.EmitAsync(FlightEventTypes.LlmResponse, responsePayload, ct);
            return response;
        }
        catch (Exception ex)
        {
            var errorPayload = new LlmErrorPayload(
                request.Provider,
                request.Model,
                ex.GetType().Name,
                ex.Message);

            await _recorder.EmitAsync(FlightEventTypes.LlmError, errorPayload, ct);
            throw;
        }
    }
}
