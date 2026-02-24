using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;

namespace AgentFlightRecorder.Sinks.Jsonl;

/// <summary>
/// Loads recorded events from a JSONL file for replay.
/// </summary>
public sealed class JsonlReplayStore : IReplayStore
{
    private readonly string _directory;
    private readonly ICanonicalJsonSerializer _serializer;

    public JsonlReplayStore(string directory, ICanonicalJsonSerializer serializer)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(serializer);
        _directory = directory;
        _serializer = serializer;
    }

    public async Task<IReadOnlyList<FlightEvent>> LoadAsync(string runId, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_directory, $"{runId}.jsonl");
        if (!File.Exists(filePath))
        {
            // Also try without extension in case the user passed a full filename
            if (File.Exists(runId))
                filePath = runId;
            else
                throw new FileNotFoundException($"Trace file not found for run '{runId}'", filePath);
        }

        return await LoadFromFileAsync(filePath, ct);
    }

    public async Task<IReadOnlyList<FlightEvent>> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        var events = new List<FlightEvent>();
        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var evt = _serializer.Deserialize<FlightEvent>(line);
            if (evt is not null)
                events.Add(evt);
        }

        return events;
    }
}
