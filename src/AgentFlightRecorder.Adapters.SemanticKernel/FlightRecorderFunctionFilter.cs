using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;

namespace AgentFlightRecorder.Adapters.SemanticKernel;

/// <summary>
/// Intercepts SK auto-function-calling invocations and delegates to <see cref="IToolExecutor"/>
/// for recording or replay.
/// </summary>
public sealed class FlightRecorderFunctionFilter : IAutoFunctionInvocationFilter
{
    private readonly IToolExecutor? _toolExecutor;
    private readonly Func<Kernel, IToolExecutor>? _executorFactory;
    private readonly SemanticKernelAdapterOptions _options;
    private IToolExecutor? _lazyExecutor;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public FlightRecorderFunctionFilter(IToolExecutor toolExecutor, SemanticKernelAdapterOptions options)
    {
        _toolExecutor = toolExecutor;
        _options = options;
    }

    /// <summary>
    /// Creates a filter that defers tool executor creation until the first invocation,
    /// when the Kernel is available from the invocation context.
    /// Used by DI registration where Kernel isn't available at service build time.
    /// </summary>
    internal FlightRecorderFunctionFilter(Func<Kernel, IToolExecutor> executorFactory, SemanticKernelAdapterOptions options)
    {
        _executorFactory = executorFactory;
        _options = options;
    }

    private IToolExecutor GetExecutor(Kernel kernel)
    {
        if (_toolExecutor is not null)
            return _toolExecutor;
        return _lazyExecutor ??= _executorFactory!(kernel);
    }

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        if (!_options.RecordFunctionCalls)
        {
            await next(context);
            return;
        }

        var toolName = context.Function.PluginName is not null
            ? $"{context.Function.PluginName}-{context.Function.Name}"
            : context.Function.Name;

        var args = SerializeArguments(context.Arguments);
        var invocation = new ToolInvocation(toolName, args);

        var executor = GetExecutor(context.Kernel);

        var toolResult = await executor.ExecuteAsync(invocation, context.CancellationToken);

        var resultValue = toolResult.Data.ValueKind == JsonValueKind.String
            ? toolResult.Data.GetString()
            : toolResult.Data.GetRawText();

        var functionResult = new FunctionResult(context.Function, resultValue);

        context.Result = functionResult;
        // Execution fully replaced — do not call next(context)
    }

    private static JsonElement SerializeArguments(KernelArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return JsonSerializer.SerializeToElement(new object(), s_jsonOptions);

        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in arguments)
        {
            // Skip execution settings that may be in KernelArguments
            if (kvp.Key == "$$execution_settings$$")
                continue;

            sorted[kvp.Key] = kvp.Value;
        }

        return JsonSerializer.SerializeToElement(sorted, s_jsonOptions);
    }
}
