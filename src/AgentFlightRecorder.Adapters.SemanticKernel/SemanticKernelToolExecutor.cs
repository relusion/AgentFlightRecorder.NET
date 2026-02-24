using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;

namespace AgentFlightRecorder.Adapters.SemanticKernel;

/// <summary>
/// Real tool executor that invokes KernelFunctions during recording.
/// During replay, <see cref="AgentFlightRecorder.Replay.ReplayToolExecutor"/> is used instead.
/// </summary>
internal sealed class SemanticKernelToolExecutor : IToolExecutor
{
    private readonly Kernel _kernel;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public SemanticKernelToolExecutor(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct)
    {
        // Parse tool name: "pluginName-functionName" or just "functionName"
        string? pluginName = null;
        string functionName = invocation.ToolName;

        var dashIndex = invocation.ToolName.IndexOf('-');
        if (dashIndex > 0)
        {
            pluginName = invocation.ToolName[..dashIndex];
            functionName = invocation.ToolName[(dashIndex + 1)..];
        }

        KernelFunction? function = null;
        if (pluginName is not null)
        {
            if (_kernel.Plugins.TryGetPlugin(pluginName, out var plugin))
            {
                plugin.TryGetFunction(functionName, out function);
            }
        }
        else
        {
            foreach (var plugin in _kernel.Plugins)
            {
                if (plugin.TryGetFunction(functionName, out function))
                    break;
            }
        }

        if (function is null)
        {
            var errorData = JsonSerializer.SerializeToElement(
                $"Function '{invocation.ToolName}' not found in kernel plugins", s_jsonOptions);
            return new ToolResult(errorData, IsError: true);
        }

        var kernelArgs = new KernelArguments();
        if (invocation.Args.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in invocation.Args.EnumerateObject())
            {
                kernelArgs[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText(),
                };
            }
        }

        try
        {
            var result = await function.InvokeAsync(_kernel, kernelArgs, ct);
            var resultValue = result.GetValue<object>();
            var data = JsonSerializer.SerializeToElement(resultValue, s_jsonOptions);
            return new ToolResult(data);
        }
        catch (Exception ex)
        {
            var errorData = JsonSerializer.SerializeToElement(ex.Message, s_jsonOptions);
            return new ToolResult(errorData, IsError: true);
        }
    }
}
