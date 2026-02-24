namespace AgentFlightRecorder.Core.Events;

/// <summary>
/// Constants for all recognized event type discriminators.
/// </summary>
public static class FlightEventTypes
{
    public const string RunStarted = "RunStarted";
    public const string RunCompleted = "RunCompleted";
    public const string SpanStarted = "SpanStarted";
    public const string SpanCompleted = "SpanCompleted";
    public const string LlmRequest = "LlmRequest";
    public const string LlmResponse = "LlmResponse";
    public const string LlmError = "LlmError";
    public const string ToolCallStarted = "ToolCallStarted";
    public const string ToolCallCompleted = "ToolCallCompleted";
    public const string ToolCallFailed = "ToolCallFailed";
    public const string StateCheckpoint = "StateCheckpoint";
    public const string Annotation = "Annotation";
    public const string RecorderError = "RecorderError";
}
