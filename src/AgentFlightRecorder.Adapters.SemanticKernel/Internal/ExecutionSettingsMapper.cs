using Microsoft.SemanticKernel;

namespace AgentFlightRecorder.Adapters.SemanticKernel.Internal;

/// <summary>
/// Maps between PromptExecutionSettings and the dictionary representation used in LlmRequest.Settings.
/// </summary>
internal static class ExecutionSettingsMapper
{
    public static IReadOnlyDictionary<string, object>? MapToSettings(PromptExecutionSettings? settings)
    {
        if (settings is null)
            return null;

        var dict = new SortedDictionary<string, object>(StringComparer.Ordinal);

        if (settings.ModelId is not null)
            dict["modelId"] = settings.ModelId;

        if (settings.ExtensionData is not null)
        {
            foreach (var kvp in settings.ExtensionData.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict.Count > 0 ? dict : null;
    }

    public static PromptExecutionSettings? MapFromSettings(IReadOnlyDictionary<string, object>? settings)
    {
        if (settings is null)
            return null;

        var result = new PromptExecutionSettings();

        if (settings.TryGetValue("modelId", out var modelId))
            result.ModelId = modelId.ToString();

        var extensionData = new Dictionary<string, object>();
        foreach (var kvp in settings)
        {
            if (kvp.Key == "modelId")
                continue;
            extensionData[kvp.Key] = kvp.Value;
        }

        if (extensionData.Count > 0)
            result.ExtensionData = extensionData;

        return result;
    }
}
