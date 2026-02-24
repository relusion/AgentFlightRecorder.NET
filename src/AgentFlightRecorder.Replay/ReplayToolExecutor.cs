using System.Diagnostics;
using System.Security.Cryptography;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;

namespace AgentFlightRecorder.Replay;

/// <summary>
/// Returns recorded tool results from a <see cref="ReplayIndex"/> instead of executing real tools.
/// </summary>
public sealed class ReplayToolExecutor : IToolExecutor
{
    private static readonly ActivitySource s_activitySource = new("AgentFlightRecorder");
    private readonly ReplayIndex _index;
    private readonly ReplayStrictness _strictness;
    private readonly ICanonicalJsonSerializer _serializer;

    public ReplayToolExecutor(ReplayIndex index, ICanonicalJsonSerializer serializer, ReplayStrictness strictness = ReplayStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(serializer);
        _index = index;
        _serializer = serializer;
        _strictness = strictness;
    }

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("Replay.ToolExecute");
        activity?.SetTag("tool.name", invocation.ToolName);

        var argsHash = ComputeArgsHash(invocation);
        var key = new InvocationKey("tool", invocation.ToolName, argsHash, invocation.Attempt, invocation.IdempotencyKey);

        var entry = _index.TryDequeue(key, _strictness);
        if (entry is null)
        {
            var closestMatches = _index.GetClosestMatches(key);
            throw new ReplayMismatchException(key, closestMatches, _index.RemainingCount, _index.PeekNextStrict());
        }

        var responseEvent = entry.ResponseEvent;
        if (responseEvent.Type == FlightEventTypes.ToolCallFailed)
        {
            var errorPayload = responseEvent.GetPayload<ToolCallFailedPayload>();
            throw new InvalidOperationException($"Replay: Tool call failed — {errorPayload.ErrorType}: {errorPayload.ErrorMessage}");
        }

        var payload = responseEvent.GetPayload<ToolCallCompletedPayload>();
        var result = new ToolResult(payload.Result, false);
        return Task.FromResult(result);
    }

    private string ComputeArgsHash(ToolInvocation invocation)
    {
        var bytes = _serializer.SerializeToUtf8Bytes(invocation.Args);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
