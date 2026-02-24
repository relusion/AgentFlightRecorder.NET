using Microsoft.SemanticKernel;

namespace AgentFlightRecorder.Adapters.SemanticKernel.Internal;

/// <summary>
/// Ambient context that threads the Kernel and PromptExecutionSettings through the
/// ILlmClient pipeline so WrappedChatCompletionClient can pass them to the inner service.
/// Uses AsyncLocal to stay scoped to the current async call chain.
/// </summary>
internal sealed class LlmCallContext : IDisposable
{
    private static readonly AsyncLocal<LlmCallContext?> s_current = new();

    public static LlmCallContext? Current => s_current.Value;

    public Kernel? Kernel { get; }
    public PromptExecutionSettings? Settings { get; }

    private readonly LlmCallContext? _previous;

    private LlmCallContext(Kernel? kernel, PromptExecutionSettings? settings)
    {
        Kernel = kernel;
        Settings = settings;
        _previous = s_current.Value;
        s_current.Value = this;
    }

    public static LlmCallContext Push(Kernel? kernel, PromptExecutionSettings? settings)
        => new(kernel, settings);

    public void Dispose() => s_current.Value = _previous;
}
