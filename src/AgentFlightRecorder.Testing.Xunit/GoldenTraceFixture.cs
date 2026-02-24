using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using AgentFlightRecorder.Sinks.Jsonl;
using Xunit;

namespace AgentFlightRecorder.Testing.Xunit;

/// <summary>
/// xUnit fixture that loads a golden trace file and provides replay stubs.
/// </summary>
public sealed class GoldenTraceFixture : IAsyncLifetime
{
    private readonly string _traceFilePath;
    private IReadOnlyList<FlightEvent>? _events;
    private ReplayIndex? _index;

    public CanonicalJsonSerializer Serializer { get; } = new();
    public IReadOnlyList<FlightEvent> Events => _events ?? throw new InvalidOperationException("Not initialized");
    public ReplayIndex Index => _index ?? throw new InvalidOperationException("Not initialized");

    public GoldenTraceFixture(string traceFilePath)
    {
        _traceFilePath = traceFilePath;
    }

    public ILlmClient CreateReplayLlmClient(ReplayStrictness strictness = ReplayStrictness.Strict) =>
        new ReplayLlmClient(Index, Serializer, strictness);

    public IToolExecutor CreateReplayToolExecutor(ReplayStrictness strictness = ReplayStrictness.Strict) =>
        new ReplayToolExecutor(Index, Serializer, strictness);

    public async Task InitializeAsync()
    {
        var store = new JsonlReplayStore(Path.GetDirectoryName(_traceFilePath)!, Serializer);
        _events = await store.LoadFromFileAsync(_traceFilePath);
        _index = new ReplayIndex(_events, Serializer);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
