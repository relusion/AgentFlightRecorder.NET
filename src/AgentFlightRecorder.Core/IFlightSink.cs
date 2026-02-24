using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Persists flight events to a storage backend.
/// </summary>
public interface IFlightSink : IAsyncDisposable
{
    ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
}
