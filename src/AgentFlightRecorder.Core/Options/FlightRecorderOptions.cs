using AgentFlightRecorder.Core.Events;
using Microsoft.Extensions.Logging;

namespace AgentFlightRecorder.Core.Options;

public sealed record FlightRecorderOptions
{
    public required FlightMode Mode { get; init; }
    public required IFlightSink Sink { get; init; }
    public IRedactor? Redactor { get; init; }
    public IIntegrityProvider? IntegrityProvider { get; init; }
    public TimeProvider? TimeProvider { get; init; }
    public ILogger<FlightRecorder>? Logger { get; init; }
    public BackpressureOptions Backpressure { get; init; } = new();
    public bool SynchronousMode { get; init; }
    public byte[]? HmacSigningKey { get; init; }
}

public sealed record BackpressureOptions
{
    public BackpressureStrategy Strategy { get; init; } = BackpressureStrategy.Drop;
    public int ChannelCapacity { get; init; } = 1024;
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
