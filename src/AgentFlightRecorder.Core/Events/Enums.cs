namespace AgentFlightRecorder.Core.Events;

public enum FlightMode
{
    Record,
    Replay,
    Passthrough
}

public enum FlightSpanKind
{
    Internal,
    Planning,
    Acting,
    ToolExecution
}

public enum ReplayStrictness
{
    Strict,
    Lenient,
    Lookup
}

public enum BackpressureStrategy
{
    Drop,
    Block,
    Buffer
}
