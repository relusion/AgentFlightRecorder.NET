using AgentFlightRecorder.Adapters.SemanticKernel.Internal;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentFlightRecorder.Adapters.SemanticKernel;

/// <summary>
/// Bridges a real <see cref="IChatCompletionService"/> to <see cref="ILlmClient"/> for record mode.
/// </summary>
internal sealed class WrappedChatCompletionClient : ILlmClient
{
    private readonly IChatCompletionService _inner;

    public WrappedChatCompletionClient(IChatCompletionService inner)
    {
        _inner = inner;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var history = ChatHistorySerializer.DeserializeHistory(request.Messages);

        // Recover original settings — FunctionChoiceBehavior is lost during serialization round-trip
        var ctx = LlmCallContext.Current;
        var settings = ctx?.Settings ?? ExecutionSettingsMapper.MapFromSettings(request.Settings);
        var kernel = ctx?.Kernel;

        // Disable auto-invocation; the adapter drives the tool-call loop
        var savedBehavior = settings?.FunctionChoiceBehavior;
        if (savedBehavior is not null)
            settings!.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false);

        try
        {
            var results = await _inner.GetChatMessageContentsAsync(history, settings, kernel, ct);
            return ResponseTranslator.FromMessageContents(results);
        }
        finally
        {
            if (settings is not null)
                settings.FunctionChoiceBehavior = savedBehavior;
        }
    }
}
