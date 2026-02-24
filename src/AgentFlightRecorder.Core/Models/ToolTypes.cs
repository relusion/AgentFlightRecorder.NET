using System.Text.Json;

namespace AgentFlightRecorder.Core.Models;

public sealed record ToolInvocation(
    string ToolName,
    JsonElement Args,
    int Attempt = 1,
    string? IdempotencyKey = null);

public sealed record ToolResult(
    JsonElement Data,
    bool IsError = false);
