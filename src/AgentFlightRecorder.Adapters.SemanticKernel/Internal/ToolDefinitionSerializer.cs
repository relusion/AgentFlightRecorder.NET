using System.Text.Json;
using Microsoft.SemanticKernel;

namespace AgentFlightRecorder.Adapters.SemanticKernel.Internal;

/// <summary>
/// Serializes Kernel plugin function metadata to canonical JsonElement.
/// </summary>
internal static class ToolDefinitionSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonElement? SerializeDefinitions(Kernel? kernel)
    {
        if (kernel is null)
            return null;

        var plugins = kernel.Plugins;
        if (!plugins.Any())
            return null;

        var definitions = new List<SortedDictionary<string, JsonElement>>();

        foreach (var plugin in plugins.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            foreach (var function in plugin.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var funcDef = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["functionName"] = JsonSerializer.SerializeToElement(function.Name, s_options),
                    ["pluginName"] = JsonSerializer.SerializeToElement(plugin.Name, s_options),
                };

                if (function.Description is not null)
                    funcDef["description"] = JsonSerializer.SerializeToElement(function.Description, s_options);

                var parameters = function.Metadata.Parameters;
                if (parameters.Count > 0)
                {
                    var paramList = new List<SortedDictionary<string, JsonElement>>();
                    foreach (var param in parameters)
                    {
                        var paramDef = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["name"] = JsonSerializer.SerializeToElement(param.Name, s_options),
                        };

                        if (param.Description is not null)
                            paramDef["description"] = JsonSerializer.SerializeToElement(param.Description, s_options);

                        if (param.IsRequired)
                            paramDef["isRequired"] = JsonSerializer.SerializeToElement(true, s_options);

                        if (param.ParameterType is not null)
                            paramDef["type"] = JsonSerializer.SerializeToElement(param.ParameterType.Name, s_options);

                        paramList.Add(paramDef);
                    }
                    funcDef["parameters"] = JsonSerializer.SerializeToElement(paramList, s_options);
                }

                definitions.Add(funcDef);
            }
        }

        return definitions.Count > 0
            ? JsonSerializer.SerializeToElement(definitions, s_options)
            : null;
    }
}
