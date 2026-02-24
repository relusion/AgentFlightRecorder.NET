using System.Text.RegularExpressions;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Redaction;

/// <summary>
/// Redacts PII patterns (email, phone) from event payloads.
/// </summary>
public sealed partial class PiiRedactor : IRedactor
{
    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    private static readonly Regex[] s_defaultPatterns =
    [
        EmailPattern(),
        PhonePattern()
    ];

    private static readonly HashSet<string> s_hashProperties = new(StringComparer.Ordinal)
    {
        "argsHash",
    };

    private readonly Regex[] _patterns;

    public PiiRedactor(IEnumerable<Regex>? additionalPatterns = null)
    {
        _patterns = additionalPatterns is not null
            ? [.. s_defaultPatterns, .. additionalPatterns]
            : s_defaultPatterns;
    }

    public FlightEvent Redact(FlightEvent evt)
    {
        var payload = evt.Payload;
        foreach (var pattern in _patterns)
        {
            payload = JsonRedactionHelper.RedactStrings(payload, pattern, "[PII_REDACTED]", s_hashProperties);
        }

        if (payload.GetRawText() == evt.Payload.GetRawText())
            return evt;

        return new FlightEvent
        {
            SchemaVersion = evt.SchemaVersion,
            RunId = evt.RunId,
            EventId = evt.EventId,
            Sequence = evt.Sequence,
            TimestampUtc = evt.TimestampUtc,
            TraceId = evt.TraceId,
            SpanId = evt.SpanId,
            ParentSpanId = evt.ParentSpanId,
            Type = evt.Type,
            Payload = payload,
            Integrity = evt.Integrity
        };
    }
}
