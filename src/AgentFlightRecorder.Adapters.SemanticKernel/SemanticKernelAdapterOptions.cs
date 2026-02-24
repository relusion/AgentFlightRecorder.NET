namespace AgentFlightRecorder.Adapters.SemanticKernel;

/// <summary>
/// Configuration options for the Semantic Kernel flight recorder adapter.
/// </summary>
public sealed record SemanticKernelAdapterOptions
{
    /// <summary>
    /// Override the provider name in LlmRequest. Defaults to the inner service type's simple name.
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// Default model name when not available from PromptExecutionSettings.
    /// </summary>
    public string? DefaultModelName { get; set; }

    /// <summary>
    /// Whether to intercept and record/replay function calls via IAutoFunctionInvocationFilter. Default: true.
    /// </summary>
    public bool RecordFunctionCalls { get; set; } = true;
}
