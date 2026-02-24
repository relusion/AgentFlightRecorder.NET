using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Serialization;
using Xunit;

namespace AgentFlightRecorder.Testing.Xunit;

/// <summary>
/// xUnit fixture that sets up a recorder with an in-memory sink for test classes.
/// Manages lifecycle automatically.
/// </summary>
public sealed class FlightRecorderFixture : IAsyncLifetime
{
    private FlightRecorder? _recorder;

    public InMemorySink Sink { get; } = new();
    public CanonicalJsonSerializer Serializer { get; } = new();

    public FlightRecorder Recorder => _recorder ?? throw new InvalidOperationException("Recorder not initialized. Call InitializeAsync first.");
    public IReadOnlyList<FlightEvent> Events => Sink.Events;

    public async Task InitializeAsync()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = Sink,
            SynchronousMode = true
        };

        _recorder = new FlightRecorder(options);
        await _recorder.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_recorder is not null)
        {
            await _recorder.StopAsync();
            await _recorder.DisposeAsync();
        }
    }
}
