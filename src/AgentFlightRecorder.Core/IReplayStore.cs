using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Loads recorded events for replay.
/// </summary>
public interface IReplayStore
{
    Task<IReadOnlyList<FlightEvent>> LoadAsync(string runId, CancellationToken ct = default);
}
