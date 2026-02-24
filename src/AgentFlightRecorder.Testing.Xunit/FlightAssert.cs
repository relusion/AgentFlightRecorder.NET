using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using Xunit;

namespace AgentFlightRecorder.Testing.Xunit;

/// <summary>
/// Custom xUnit assertions for flight recorder testing.
/// </summary>
public static class FlightAssert
{
    /// <summary>
    /// Runs a workflow using replay stubs and asserts no mismatch occurs.
    /// </summary>
    public static async Task ReplayMatchesTrace(
        ReplayIndex index,
        ICanonicalJsonSerializer serializer,
        Func<ILlmClient, IToolExecutor, Task> workflow,
        ReplayStrictness strictness = ReplayStrictness.Strict)
    {
        var replayLlm = new ReplayLlmClient(index, serializer, strictness);
        var replayTool = new ReplayToolExecutor(index, serializer, strictness);

        try
        {
            await workflow(replayLlm, replayTool);
        }
        catch (ReplayMismatchException ex)
        {
            Assert.Fail($"Replay mismatch during workflow execution:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that two event sequences have the same types in the same order.
    /// </summary>
    public static void EventSequenceEquals(
        IReadOnlyList<FlightEvent> expected,
        IReadOnlyList<FlightEvent> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Type, actual[i].Type);
        }
    }

    /// <summary>
    /// Verifies the integrity hash chain of a list of events.
    /// </summary>
    public static void IntegrityChainValid(
        IReadOnlyList<FlightEvent> events,
        IIntegrityProvider provider)
    {
        FlightIntegrity? previous = null;

        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            Assert.NotNull(evt.Integrity);

            var isValid = provider.Verify(previous, evt, evt.Integrity);
            Assert.True(isValid,
                $"Integrity chain broken at event {i} (sequence={evt.Sequence}, type={evt.Type}).\n" +
                $"Expected prevHash: {previous?.Hash ?? "(none)"}\n" +
                $"Actual prevHash: {evt.Integrity.PrevHash}");

            previous = evt.Integrity;
        }
    }

    /// <summary>
    /// Asserts that the event list contains at least one event of the given type.
    /// </summary>
    public static void ContainsEventType(IReadOnlyList<FlightEvent> events, string eventType)
    {
        Assert.Contains(events, e => e.Type == eventType);
    }

    /// <summary>
    /// Asserts that the event list does not contain any event of the given type.
    /// </summary>
    public static void DoesNotContainEventType(IReadOnlyList<FlightEvent> events, string eventType)
    {
        Assert.DoesNotContain(events, e => e.Type == eventType);
    }
}
