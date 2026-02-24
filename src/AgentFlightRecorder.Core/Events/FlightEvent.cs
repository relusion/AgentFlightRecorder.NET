using System.Text.Json;

namespace AgentFlightRecorder.Core.Events;

/// <summary>
/// The universal event envelope for all flight recorder events.
/// Uses a string <see cref="Type"/> discriminator with a <see cref="JsonElement"/> payload
/// for flexible, lazy deserialization.
/// </summary>
public sealed class FlightEvent
{
    public required string SchemaVersion { get; init; }
    public required string RunId { get; init; }
    public required string EventId { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public required string Type { get; init; }
    public required JsonElement Payload { get; init; }
    public FlightIntegrity? Integrity { get; init; }

    private static readonly JsonSerializerOptions s_payloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserializes the <see cref="Payload"/> into a strongly-typed payload record.
    /// </summary>
    public T GetPayload<T>() where T : class =>
        Payload.Deserialize<T>(s_payloadOptions)
        ?? throw new InvalidOperationException($"Failed to deserialize payload to {typeof(T).Name}.");
}
