using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Options;

public sealed record ReplayOptions
{
    public ReplayStrictness Strictness { get; init; } = ReplayStrictness.Strict;
}
