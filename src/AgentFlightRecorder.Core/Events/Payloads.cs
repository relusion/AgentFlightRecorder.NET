using System.Text.Json;

namespace AgentFlightRecorder.Core.Events;

public sealed record RunStartedPayload(
    string? RunName,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record RunCompletedPayload(
    string Status,
    string? RunSignature,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record LlmRequestPayload(
    string Provider,
    string Model,
    JsonElement Messages,
    IReadOnlyDictionary<string, object>? Settings,
    JsonElement? ToolDefinitions);

public sealed record LlmResponsePayload(
    string Provider,
    string Model,
    JsonElement Content,
    JsonElement? ToolCalls,
    TokenUsage? Usage);

public sealed record TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

public sealed record LlmErrorPayload(
    string Provider,
    string Model,
    string ErrorType,
    string ErrorMessage);

public sealed record ToolCallStartedPayload(
    string ToolName,
    JsonElement Args,
    string ArgsHash,
    int Attempt,
    string? IdempotencyKey);

public sealed record ToolCallCompletedPayload(
    string ToolName,
    JsonElement Args,
    string ArgsHash,
    JsonElement Result,
    long DurationMs,
    int Attempt,
    string? IdempotencyKey);

public sealed record ToolCallFailedPayload(
    string ToolName,
    JsonElement Args,
    string ArgsHash,
    string ErrorType,
    string ErrorMessage,
    string? StackTrace,
    long DurationMs,
    int Attempt,
    string? IdempotencyKey);

public sealed record StateCheckpointPayload(
    string CheckpointName,
    JsonElement State,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record AnnotationPayload(
    string Message,
    IReadOnlyDictionary<string, string>? Tags);

public sealed record RecorderErrorPayload(
    string ErrorType,
    string ErrorMessage,
    long? SourceEventSequence,
    string? SourceEventType);

public sealed record SpanStartedPayload(
    string SpanName,
    string Kind);

public sealed record SpanCompletedPayload(
    string SpanName,
    string Kind,
    long DurationMs);
