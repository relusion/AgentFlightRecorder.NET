using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Sinks.Jsonl;

namespace AzureOpenAISample.Scenarios;

public static class VerifyScenario
{
    public static async Task RunAsync(
        string traceFile,
        ICanonicalJsonSerializer serializer,
        byte[] hmacKeyBytes,
        List<string> replayedOutputs)
    {
        // Load events from trace file
        var replayStore = new JsonlReplayStore(Path.GetDirectoryName(traceFile)!, serializer);
        var events = await replayStore.LoadFromFileAsync(traceFile);

        // --- Step 1: Hash Chain Verification ---
        Console.WriteLine("  [Step 1] Hash Chain Verification");
        var integrityProvider = new Sha256IntegrityProvider(serializer);
        var verifier = new IntegrityVerifier(integrityProvider);
        var result = verifier.Verify(events);
        Console.WriteLine($"    Hash chain valid: {result.IsValid}");
        if (!result.IsValid)
            Console.WriteLine($"    Error: {result.Message}");
        Console.WriteLine();

        // --- Step 2: HMAC Signature Verification ---
        Console.WriteLine("  [Step 2] HMAC Signature Verification");
        var runCompleted = events.LastOrDefault(e => e.Type == FlightEventTypes.RunCompleted);
        if (runCompleted is not null)
        {
            var payload = runCompleted.GetPayload<RunCompletedPayload>();
            if (payload.RunSignature is not null && runCompleted.Integrity is not null)
            {
                var hmacValid = HmacSignatureProvider.VerifySignature(
                    runCompleted.Integrity.PrevHash, payload.RunSignature, hmacKeyBytes);
                Console.WriteLine($"    HMAC signature valid: {hmacValid}");
            }
            else
            {
                Console.WriteLine("    HMAC signature: not present");
            }
        }
        else
        {
            Console.WriteLine("    RunCompleted event not found.");
        }
        Console.WriteLine();

        // --- Step 3: Output Comparison (Trace vs Replay) ---
        // Extract recorded outputs from the trace file (post-redaction) so both
        // sides of the comparison are at the same redaction level.
        var recordedOutputs = ExtractAssistantOutputs(events);

        Console.WriteLine("  [Step 3] Output Comparison (Trace vs Replay)");
        var allMatch = recordedOutputs.Count == replayedOutputs.Count;
        for (int i = 0; i < Math.Min(recordedOutputs.Count, replayedOutputs.Count); i++)
        {
            var match = recordedOutputs[i] == replayedOutputs[i];
            allMatch = allMatch && match;
            Console.WriteLine($"    Turn {i + 1}: {(match ? "MATCH" : "MISMATCH")}");
        }
        Console.WriteLine($"    All outputs identical: {allMatch}");
        Console.WriteLine();

        // --- Step 4: Event Summary ---
        Console.WriteLine("  [Step 4] Event Summary");
        Console.WriteLine($"    Total events: {events.Count}");
        var grouped = events.GroupBy(e => e.Type).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            Console.WriteLine($"    {group.Key}: {group.Count()}");
        }
    }

    /// <summary>
    /// Extracts assistant text outputs from LlmResponse events that contain no tool calls
    /// (i.e., final assistant turns rather than intermediate tool-calling steps).
    /// </summary>
    private static List<string> ExtractAssistantOutputs(IReadOnlyList<FlightEvent> events)
    {
        var outputs = new List<string>();

        foreach (var evt in events.Where(e => e.Type == FlightEventTypes.LlmResponse))
        {
            var payload = evt.GetPayload<LlmResponsePayload>();

            // Skip intermediate responses that request tool calls
            if (payload.ToolCalls is not null
                && payload.ToolCalls.Value.ValueKind == JsonValueKind.Array
                && payload.ToolCalls.Value.GetArrayLength() > 0)
                continue;

            var text = payload.Content.ValueKind == JsonValueKind.String
                ? payload.Content.GetString() ?? ""
                : payload.Content.GetRawText();

            outputs.Add(text);
        }

        return outputs;
    }
}
