namespace AgentFlightRecorder.Core.Events;

/// <summary>
/// Uniquely identifies an invocation for replay matching.
/// </summary>
public sealed record InvocationKey(
    string Kind,
    string Name,
    string ArgsHash,
    int Attempt,
    string? IdempotencyKey);
