using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Records agent run events into a structured trace.
/// </summary>
public interface IFlightRecorder : IAsyncDisposable
{
    string RunId { get; }
    FlightMode Mode { get; }
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync(CancellationToken ct = default);
    IFlightSpan StartSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal);
    ValueTask CheckpointAsync(string name, object state, IStateSerializer serializer, CancellationToken ct = default);
    ValueTask AnnotateAsync(string message, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default);
}
