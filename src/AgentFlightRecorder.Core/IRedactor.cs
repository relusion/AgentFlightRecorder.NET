using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Redacts sensitive data from flight events before persistence.
/// </summary>
public interface IRedactor
{
    FlightEvent Redact(FlightEvent evt);
}
