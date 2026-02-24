using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel.Internal;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentFlightRecorder.Adapters.SemanticKernel;

/// <summary>
/// Bridges Semantic Kernel's <see cref="IChatCompletionService"/> into AgentFlightRecorder's
/// <see cref="ILlmClient"/> for deterministic recording and replay.
/// Streaming uses buffer-then-yield to ensure recording fidelity.
/// </summary>
public sealed class SemanticKernelChatCompletionAdapter : IChatCompletionService
{
    private const int MaxAutoInvokeIterations = 10;

    private readonly ILlmClient _llmClient;
    private readonly SemanticKernelAdapterOptions _options;
    private readonly IReadOnlyDictionary<string, object?> _attributes;

    public SemanticKernelChatCompletionAdapter(
        ILlmClient llmClient,
        SemanticKernelAdapterOptions options)
    {
        _llmClient = llmClient;
        _options = options;
        _attributes = new Dictionary<string, object?>
        {
            ["ModelId"] = options.DefaultModelName,
            ["ProviderName"] = options.ProviderName,
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProviderName();
        var model = ResolveModelName(executionSettings);
        bool autoInvoke = executionSettings?.FunctionChoiceBehavior is not null && kernel is not null;

        for (int iteration = 0; iteration < MaxAutoInvokeIterations; iteration++)
        {
            var messages = ChatHistorySerializer.SerializeHistory(chatHistory);
            var settings = ExecutionSettingsMapper.MapToSettings(executionSettings);
            var toolDefinitions = ToolDefinitionSerializer.SerializeDefinitions(kernel);

            var request = new LlmRequest(provider, model, messages, settings, toolDefinitions);

            LlmResponse response;
            using (LlmCallContext.Push(kernel, executionSettings))
            {
                response = await _llmClient.CompleteAsync(request, cancellationToken);
            }

            var results = ResponseTranslator.ToMessageContents(response, model);
            var lastMessage = results[^1];
            var functionCalls = lastMessage.Items.OfType<FunctionCallContent>().ToList();

            if (functionCalls.Count == 0 || !autoInvoke)
                return results;

            chatHistory.Add(lastMessage);

            // Route tool calls through the filter chain for recording/replay
            var filters = kernel!.GetAllServices<IAutoFunctionInvocationFilter>().ToList();

            foreach (var fc in functionCalls)
            {
                KernelFunction? function = null;
                if (fc.PluginName is not null
                    && kernel.Plugins.TryGetPlugin(fc.PluginName, out var plugin))
                {
                    plugin.TryGetFunction(fc.FunctionName, out function);
                }
                else
                {
                    foreach (var p in kernel.Plugins)
                    {
                        if (p.TryGetFunction(fc.FunctionName, out function))
                            break;
                    }
                }

                function ??= KernelFunctionFactory.CreateFromMethod(() => $"Function '{fc.FunctionName}' not found");

                var kernelArgs = new KernelArguments();
                if (fc.Arguments is not null)
                {
                    foreach (var arg in fc.Arguments)
                        kernelArgs[arg.Key] = arg.Value;
                }

                var initialResult = new FunctionResult(function, "");
                var context = new AutoFunctionInvocationContext(
                    kernel, function, initialResult, chatHistory, lastMessage)
                {
                    Arguments = kernelArgs,
                    CancellationToken = cancellationToken,
                    RequestSequenceIndex = iteration,
                    FunctionSequenceIndex = functionCalls.IndexOf(fc),
                    FunctionCount = functionCalls.Count,
                };

                // Build filter chain: last filter → ... → first filter → default invoke
                Func<AutoFunctionInvocationContext, Task> next = async ctx =>
                {
                    var result = await ctx.Function.InvokeAsync(kernel, ctx.Arguments, cancellationToken);
                    ctx.Result = result;
                };

                for (int f = filters.Count - 1; f >= 0; f--)
                {
                    var filter = filters[f];
                    var currentNext = next;
                    next = ctx => filter.OnAutoFunctionInvocationAsync(ctx, currentNext);
                }

                await next(context);

                if (context.Terminate)
                    return results;

                var resultValue = context.Result?.GetValue<object>()?.ToString() ?? "";
                var resultContent = new FunctionResultContent(
                    fc.FunctionName, fc.PluginName, fc.Id, resultValue);
                chatHistory.Add(new ChatMessageContent(AuthorRole.Tool, [resultContent]));
            }

        }

        return [];
    }

    /// <summary>
    /// Buffers the full response then yields individual <see cref="StreamingChatMessageContent"/> items.
    /// Streaming is simulated to ensure recording/replay fidelity.
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

        foreach (var message in results)
        {
            yield return new StreamingChatMessageContent(
                message.Role,
                message.Content,
                message.InnerContent,
                0,
                message.ModelId,
                message.Encoding,
                message.Metadata);
        }
    }

    private string ResolveProviderName()
    {
        return _options.ProviderName ?? "SemanticKernel";
    }

    private string ResolveModelName(PromptExecutionSettings? executionSettings)
    {
        if (executionSettings?.ModelId is not null)
            return executionSettings.ModelId;

        if (_options.DefaultModelName is not null)
            return _options.DefaultModelName;

        return "unknown";
    }
}
