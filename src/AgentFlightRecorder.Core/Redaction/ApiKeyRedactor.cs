using System.Text.RegularExpressions;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Redaction;

/// <summary>
/// Redacts common API key patterns from event payloads.
/// </summary>
public sealed partial class ApiKeyRedactor : IRedactor
{
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled)]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex(@"(?:api[_-]?key|apikey)\s*[=:]\s*[A-Za-z0-9\-._~+/]{10,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyPattern();

    private static readonly Regex[] s_patterns =
    [
        BearerTokenPattern(),
        OpenAiKeyPattern(),
        ApiKeyPattern()
    ];

    public FlightEvent Redact(FlightEvent evt)
    {
        var payload = evt.Payload;
        foreach (var pattern in s_patterns)
        {
            payload = JsonRedactionHelper.RedactStrings(payload, pattern);
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
