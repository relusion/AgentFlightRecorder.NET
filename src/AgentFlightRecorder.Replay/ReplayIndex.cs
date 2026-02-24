using System.Security.Cryptography;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Replay;

/// <summary>
/// Index of recorded invocations for deterministic replay matching.
/// Supports Strict, Lenient, and Lookup modes with FIFO consumption.
/// </summary>
public sealed class ReplayIndex
{
    private readonly Dictionary<InvocationKey, Queue<ReplayEntry>> _entries = new();
    private readonly List<ReplayEntry> _orderedEntries = [];
    private int _strictPosition;
    private readonly ICanonicalJsonSerializer _serializer;

    public int TotalEntries => _orderedEntries.Count;
    public int ConsumedCount => _strictPosition;

    public ReplayIndex(IReadOnlyList<FlightEvent> events, ICanonicalJsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
        BuildIndex(events);
    }

    private void BuildIndex(IReadOnlyList<FlightEvent> events)
    {
        // Pair request events with their corresponding response events
        var pendingRequests = new Dictionary<string, (InvocationKey Key, FlightEvent RequestEvent)>();

        foreach (var evt in events.OrderBy(e => e.Sequence))
        {
            switch (evt.Type)
            {
                case FlightEventTypes.LlmRequest:
                {
                    var payload = evt.GetPayload<LlmRequestPayload>();
                    var argsHash = ComputeArgsHashFromTyped(payload);
                    var key = new InvocationKey("llm", payload.Model, argsHash, 1, null);
                    // Store under a composite tracking key to handle multiple pending requests
                    var trackingKey = $"llm:{payload.Model}:{argsHash}:{evt.Sequence}";
                    pendingRequests[trackingKey] = (key, evt);
                    break;
                }
                case FlightEventTypes.LlmResponse:
                case FlightEventTypes.LlmError:
                {
                    // Find the most recent unmatched LLM request
                    var matchKey = pendingRequests.Keys
                        .Where(k => k.StartsWith("llm:"))
                        .OrderByDescending(k => pendingRequests[k].RequestEvent.Sequence)
                        .FirstOrDefault();

                    if (matchKey is not null)
                    {
                        var (invKey, reqEvt) = pendingRequests[matchKey];
                        pendingRequests.Remove(matchKey);

                        var entry = new ReplayEntry(invKey, reqEvt.Sequence, reqEvt, evt);
                        AddEntry(entry);
                    }
                    break;
                }
                case FlightEventTypes.ToolCallStarted:
                {
                    var payload = evt.GetPayload<ToolCallStartedPayload>();
                    var key = new InvocationKey("tool", payload.ToolName, payload.ArgsHash, payload.Attempt, payload.IdempotencyKey);
                    var trackingKey = $"tool:{payload.ToolName}:{payload.ArgsHash}:{payload.Attempt}:{evt.Sequence}";
                    pendingRequests[trackingKey] = (key, evt);
                    break;
                }
                case FlightEventTypes.ToolCallCompleted:
                case FlightEventTypes.ToolCallFailed:
                {
                    // Find the most recent unmatched tool call
                    var matchKey = pendingRequests.Keys
                        .Where(k => k.StartsWith("tool:"))
                        .OrderByDescending(k => pendingRequests[k].RequestEvent.Sequence)
                        .FirstOrDefault();

                    if (matchKey is not null)
                    {
                        var (invKey, reqEvt) = pendingRequests[matchKey];
                        pendingRequests.Remove(matchKey);

                        var entry = new ReplayEntry(invKey, reqEvt.Sequence, reqEvt, evt);
                        AddEntry(entry);
                    }
                    break;
                }
            }
        }
    }

    private void AddEntry(ReplayEntry entry)
    {
        if (!_entries.TryGetValue(entry.Key, out var queue))
        {
            queue = new Queue<ReplayEntry>();
            _entries[entry.Key] = queue;
        }
        queue.Enqueue(entry);
        _orderedEntries.Add(entry);
    }

    /// <summary>
    /// Attempt to dequeue a matching replay entry.
    /// </summary>
    public ReplayEntry? TryDequeue(InvocationKey key, ReplayStrictness strictness)
    {
        switch (strictness)
        {
            case ReplayStrictness.Strict:
            {
                if (_strictPosition >= _orderedEntries.Count)
                    return null;

                var expected = _orderedEntries[_strictPosition];
                if (!expected.Key.Equals(key))
                    return null;

                _strictPosition++;
                if (_entries.TryGetValue(key, out var queue) && queue.Count > 0)
                    queue.Dequeue();

                return expected;
            }

            case ReplayStrictness.Lenient:
            case ReplayStrictness.Lookup:
            {
                if (!_entries.TryGetValue(key, out var queue) || queue.Count == 0)
                    return null;

                return queue.Dequeue();
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Get closest matches for diagnostics when a mismatch occurs.
    /// </summary>
    public IReadOnlyList<InvocationKey> GetClosestMatches(InvocationKey key, int maxResults = 5)
    {
        return _entries.Keys
            .Where(k => k.Name == key.Name && k.Kind == key.Kind)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Count of remaining unconsumed entries.
    /// </summary>
    public int RemainingCount => _entries.Values.Sum(q => q.Count);

    /// <summary>
    /// Get the next expected entry in strict mode (for error messages).
    /// </summary>
    public ReplayEntry? PeekNextStrict() =>
        _strictPosition < _orderedEntries.Count ? _orderedEntries[_strictPosition] : null;

    private string ComputeArgsHashFromTyped<T>(T payload)
    {
        var bytes = _serializer.SerializeToUtf8Bytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
