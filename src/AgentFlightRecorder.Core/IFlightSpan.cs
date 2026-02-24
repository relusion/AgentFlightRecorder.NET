using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Represents a logical span within a recorded run.
/// </summary>
public interface IFlightSpan : IDisposable
{
    string SpanId { get; }
    string Name { get; }
    FlightSpanKind Kind { get; }
    IFlightSpan StartChildSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal);
}
