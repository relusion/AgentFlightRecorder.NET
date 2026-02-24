using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Testing.Xunit;

/// <summary>
/// Result of comparing two event traces.
/// </summary>
public sealed record TraceDiffResult(
    bool AreEqual,
    IReadOnlyList<TraceDiffEntry> Differences)
{
    public override string ToString()
    {
        if (AreEqual) return "Traces are identical.";

        var lines = new List<string> { $"Found {Differences.Count} difference(s):" };
        foreach (var diff in Differences)
            lines.Add($"  [{diff.Position}] {diff.Kind}: {diff.Description}");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A single difference between two traces.
/// </summary>
public sealed record TraceDiffEntry(
    int Position,
    TraceDiffKind Kind,
    string Description);

/// <summary>
/// The kind of difference found between two traces.
/// </summary>
public enum TraceDiffKind
{
    TypeMismatch,
    Missing,
    Extra,
    PayloadMismatch
}

/// <summary>
/// Compares two flight event traces and produces a structured diff.
/// </summary>
public static class TraceDiff
{
    /// <summary>
    /// Compare two event sequences by type and optionally by payload content.
    /// </summary>
    public static TraceDiffResult Compare(
        IReadOnlyList<FlightEvent> expected,
        IReadOnlyList<FlightEvent> actual,
        bool comparePayloads = false)
    {
        var diffs = new List<TraceDiffEntry>();
        var maxLen = Math.Max(expected.Count, actual.Count);

        for (int i = 0; i < maxLen; i++)
        {
            if (i >= expected.Count)
            {
                diffs.Add(new TraceDiffEntry(i, TraceDiffKind.Extra,
                    $"Extra event: {actual[i].Type} (sequence={actual[i].Sequence})"));
                continue;
            }

            if (i >= actual.Count)
            {
                diffs.Add(new TraceDiffEntry(i, TraceDiffKind.Missing,
                    $"Missing event: {expected[i].Type} (sequence={expected[i].Sequence})"));
                continue;
            }

            if (expected[i].Type != actual[i].Type)
            {
                diffs.Add(new TraceDiffEntry(i, TraceDiffKind.TypeMismatch,
                    $"Expected type '{expected[i].Type}' but got '{actual[i].Type}'"));
            }
            else if (comparePayloads)
            {
                var expectedPayload = expected[i].Payload.ToString();
                var actualPayload = actual[i].Payload.ToString();
                if (expectedPayload != actualPayload)
                {
                    diffs.Add(new TraceDiffEntry(i, TraceDiffKind.PayloadMismatch,
                        $"Payload differs at event type '{expected[i].Type}' (sequence={expected[i].Sequence})"));
                }
            }
        }

        return new TraceDiffResult(diffs.Count == 0, diffs);
    }

    /// <summary>
    /// Compare two traces by event type sequence only (ignoring payloads, timestamps, IDs).
    /// </summary>
    public static TraceDiffResult CompareTypes(
        IReadOnlyList<FlightEvent> expected,
        IReadOnlyList<FlightEvent> actual) =>
        Compare(expected, actual, comparePayloads: false);
}
