using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Testing.Xunit;

/// <summary>
/// In-memory sink that stores events in a list for test assertions.
/// </summary>
public sealed class InMemorySink : IFlightSink
{
    private readonly List<FlightEvent> _events = [];

    public IReadOnlyList<FlightEvent> Events => _events;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        _events.Add(evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
