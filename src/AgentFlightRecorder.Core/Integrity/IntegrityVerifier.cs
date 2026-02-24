using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Integrity;

/// <summary>
/// Verifies the integrity hash chain of a sequence of flight events.
/// </summary>
public sealed class IntegrityVerifier
{
    private readonly IIntegrityProvider _provider;

    public IntegrityVerifier(IIntegrityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>
    /// Verifies the hash chain for all events. Returns the index of the first invalid event, or -1 if all valid.
    /// </summary>
    public IntegrityVerificationResult Verify(IReadOnlyList<FlightEvent> events)
    {
        FlightIntegrity? previous = null;

        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (evt.Integrity is null)
            {
                return new IntegrityVerificationResult(false, i, "Event has no integrity data");
            }

            if (!_provider.Verify(previous, evt, evt.Integrity))
            {
                return new IntegrityVerificationResult(false, i,
                    $"Hash chain broken at event {i} (sequence={evt.Sequence}, type={evt.Type})");
            }

            previous = evt.Integrity;
        }

        return new IntegrityVerificationResult(true, -1, null);
    }
}

public sealed record IntegrityVerificationResult(
    bool IsValid,
    int FirstInvalidIndex,
    string? Message);
