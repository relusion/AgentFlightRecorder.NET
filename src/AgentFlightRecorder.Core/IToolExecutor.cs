using AgentFlightRecorder.Core.Models;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Executes tool invocations.
/// </summary>
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct);
}
