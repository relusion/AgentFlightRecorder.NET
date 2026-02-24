using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Represents a logical span that emits SpanStarted/SpanCompleted events.
/// </summary>
internal sealed class FlightSpan : IFlightSpan
{
    private readonly FlightRecorder _recorder;
    private readonly TimeProvider _timeProvider;
    private readonly long _startTimestamp;
    private readonly string? _parentSpanId;
    private bool _disposed;

    public string SpanId { get; }
    public string Name { get; }
    public FlightSpanKind Kind { get; }

    public FlightSpan(
        string name,
        FlightSpanKind kind,
        string? parentSpanId,
        FlightRecorder recorder,
        TimeProvider timeProvider)
    {
        Name = name;
        Kind = kind;
        SpanId = Guid.NewGuid().ToString("N");
        _parentSpanId = parentSpanId;
        _recorder = recorder;
        _timeProvider = timeProvider;
        _startTimestamp = timeProvider.GetTimestamp();

        var payload = new SpanStartedPayload(name, kind.ToString());
        _ = _recorder.EmitAsync(FlightEventTypes.SpanStarted, payload, CancellationToken.None, SpanId, _parentSpanId);
    }

    public IFlightSpan StartChildSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal) =>
        new FlightSpan(name, kind, SpanId, _recorder, _timeProvider);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
        var payload = new SpanCompletedPayload(Name, Kind.ToString(), (long)elapsed.TotalMilliseconds);
        _ = _recorder.EmitAsync(FlightEventTypes.SpanCompleted, payload, CancellationToken.None, SpanId, _parentSpanId);
    }
}

/// <summary>
/// No-op span for Passthrough mode — performs no recording.
/// </summary>
internal sealed class NoOpFlightSpan : IFlightSpan
{
    public string SpanId => string.Empty;
    public string Name { get; }
    public FlightSpanKind Kind { get; }

    public NoOpFlightSpan(string name, FlightSpanKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public IFlightSpan StartChildSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal) =>
        new NoOpFlightSpan(name, kind);

    public void Dispose() { }
}
