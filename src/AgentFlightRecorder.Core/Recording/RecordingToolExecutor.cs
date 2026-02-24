using System.Diagnostics;
using System.Security.Cryptography;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Serialization;

namespace AgentFlightRecorder.Core.Recording;

/// <summary>
/// Decorator that wraps an <see cref="IToolExecutor"/> and records tool call events.
/// </summary>
public sealed class RecordingToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly FlightRecorder _recorder;
    private readonly ICanonicalJsonSerializer _serializer;

    public RecordingToolExecutor(IToolExecutor inner, FlightRecorder recorder, ICanonicalJsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(serializer);
        _inner = inner;
        _recorder = recorder;
        _serializer = serializer;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct)
    {
        if (_recorder.Mode == FlightMode.Passthrough)
            return await _inner.ExecuteAsync(invocation, ct);

        var argsHash = ComputeArgsHash(invocation.Args);

        var startPayload = new ToolCallStartedPayload(
            invocation.ToolName,
            invocation.Args,
            argsHash,
            invocation.Attempt,
            invocation.IdempotencyKey);

        await _recorder.EmitAsync(FlightEventTypes.ToolCallStarted, startPayload, ct);

        var startTicks = Stopwatch.GetTimestamp();
        try
        {
            var result = await _inner.ExecuteAsync(invocation, ct);
            var durationMs = GetElapsedMs(startTicks);

            var completedPayload = new ToolCallCompletedPayload(
                invocation.ToolName,
                invocation.Args,
                argsHash,
                result.Data,
                durationMs,
                invocation.Attempt,
                invocation.IdempotencyKey);

            await _recorder.EmitAsync(FlightEventTypes.ToolCallCompleted, completedPayload, ct);
            return result;
        }
        catch (Exception ex)
        {
            var durationMs = GetElapsedMs(startTicks);

            var failedPayload = new ToolCallFailedPayload(
                invocation.ToolName,
                invocation.Args,
                argsHash,
                ex.GetType().Name,
                ex.Message,
                ex.StackTrace,
                durationMs,
                invocation.Attempt,
                invocation.IdempotencyKey);

            await _recorder.EmitAsync(FlightEventTypes.ToolCallFailed, failedPayload, ct);
            throw;
        }
    }

    private string ComputeArgsHash(System.Text.Json.JsonElement args)
    {
        var bytes = _serializer.SerializeToUtf8Bytes(args);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long GetElapsedMs(long startTicks)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTicks);
        return (long)elapsed.TotalMilliseconds;
    }
}
