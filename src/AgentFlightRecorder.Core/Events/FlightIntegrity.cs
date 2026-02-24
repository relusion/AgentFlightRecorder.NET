namespace AgentFlightRecorder.Core.Events;

/// <summary>
/// Hash chain integrity data attached to each event.
/// </summary>
public sealed record FlightIntegrity(
    string PrevHash,
    string Hash,
    string Algorithm);
