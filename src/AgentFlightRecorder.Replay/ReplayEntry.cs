using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Replay;

/// <summary>
/// A matched pair of request/response events for replay.
/// </summary>
public sealed record ReplayEntry(
    InvocationKey Key,
    long Sequence,
    FlightEvent RequestEvent,
    FlightEvent ResponseEvent);
