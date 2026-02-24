using System.Text;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Replay;

/// <summary>
/// Thrown when a replay invocation does not match any recorded entry.
/// </summary>
public sealed class ReplayMismatchException : Exception
{
    public InvocationKey RequestedKey { get; }
    public IReadOnlyList<InvocationKey> ClosestMatches { get; }
    public int RemainingEntries { get; }
    public ReplayEntry? ExpectedEntry { get; }

    public ReplayMismatchException(
        InvocationKey requestedKey,
        IReadOnlyList<InvocationKey> closestMatches,
        int remainingEntries,
        ReplayEntry? expectedEntry)
        : base(FormatMessage(requestedKey, closestMatches, remainingEntries, expectedEntry))
    {
        RequestedKey = requestedKey;
        ClosestMatches = closestMatches;
        RemainingEntries = remainingEntries;
        ExpectedEntry = expectedEntry;
    }

    private static string FormatMessage(
        InvocationKey requestedKey,
        IReadOnlyList<InvocationKey> closestMatches,
        int remainingEntries,
        ReplayEntry? expectedEntry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Replay mismatch: no recorded entry matches the current invocation.");
        sb.AppendLine();
        sb.AppendLine($"  Requested: {requestedKey.Kind} '{requestedKey.Name}' (argsHash={requestedKey.ArgsHash}, attempt={requestedKey.Attempt})");

        if (expectedEntry is not null)
        {
            sb.AppendLine($"  Expected:  {expectedEntry.Key.Kind} '{expectedEntry.Key.Name}' (argsHash={expectedEntry.Key.ArgsHash}, attempt={expectedEntry.Key.Attempt})");
        }

        if (closestMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Closest matches (same name, different args):");
            foreach (var match in closestMatches)
            {
                sb.AppendLine($"    - {match.Kind} '{match.Name}' (argsHash={match.ArgsHash}, attempt={match.Attempt})");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"  Remaining unconsumed entries: {remainingEntries}");

        return sb.ToString();
    }
}
