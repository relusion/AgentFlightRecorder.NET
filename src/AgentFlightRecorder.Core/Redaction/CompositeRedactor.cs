using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Redaction;

/// <summary>
/// Chains multiple redactors, applying each in sequence.
/// </summary>
public sealed class CompositeRedactor : IRedactor
{
    private readonly IReadOnlyList<IRedactor> _redactors;

    public CompositeRedactor(IEnumerable<IRedactor> redactors)
    {
        ArgumentNullException.ThrowIfNull(redactors);
        _redactors = redactors.ToList();
    }

    public FlightEvent Redact(FlightEvent evt)
    {
        foreach (var redactor in _redactors)
        {
            evt = redactor.Redact(evt);
        }
        return evt;
    }
}
