using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Computes and verifies the integrity hash chain for flight events.
/// </summary>
public interface IIntegrityProvider
{
    FlightIntegrity Compute(FlightIntegrity? previous, FlightEvent evt);
    bool Verify(FlightIntegrity? previous, FlightEvent evt, FlightIntegrity integrity);
}
