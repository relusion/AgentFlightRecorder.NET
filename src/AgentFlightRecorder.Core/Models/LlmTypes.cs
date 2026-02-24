using System.Text.Json;
using AgentFlightRecorder.Core.Events;

namespace AgentFlightRecorder.Core.Models;

public sealed record LlmRequest(
    string Provider,
    string Model,
    JsonElement Messages,
    IReadOnlyDictionary<string, object>? Settings = null,
    JsonElement? ToolDefinitions = null);

public sealed record LlmResponse(
    JsonElement Content,
    JsonElement? ToolCalls = null,
    TokenUsage? Usage = null);
