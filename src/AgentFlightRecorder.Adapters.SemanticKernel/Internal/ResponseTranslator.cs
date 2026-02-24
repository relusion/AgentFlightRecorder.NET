using System.Text.Json;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentFlightRecorder.Adapters.SemanticKernel.Internal;

/// <summary>
/// Translates between LlmResponse and ChatMessageContent.
/// </summary>
internal static class ResponseTranslator
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyList<ChatMessageContent> ToMessageContents(LlmResponse response, string modelId)
    {
        var items = new ChatMessageContentItemCollection();
        string? textContent = null;

        if (response.Content.ValueKind == JsonValueKind.String)
        {
            textContent = response.Content.GetString();
            if (textContent is not null)
                items.Add(new TextContent(textContent));
        }
        else if (response.Content.ValueKind == JsonValueKind.Object)
        {
            textContent = response.Content.GetRawText();
            items.Add(new TextContent(textContent));
        }

        if (response.ToolCalls is not null && response.ToolCalls.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in response.ToolCalls.Value.EnumerateArray())
            {
                var functionName = toolCall.GetProperty("functionName").GetString()!;
                string? pluginName = toolCall.TryGetProperty("pluginName", out var pn) && pn.ValueKind == JsonValueKind.String
                    ? pn.GetString() : null;
                string? id = toolCall.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString() : null;

                KernelArguments? args = null;
                if (toolCall.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
                {
                    args = new KernelArguments();
                    foreach (var prop in argsProp.EnumerateObject())
                    {
                        args[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.GetRawText(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText(),
                        };
                    }
                }

                items.Add(new FunctionCallContent(functionName, pluginName, id, args));
            }
        }

        var metadata = new Dictionary<string, object?>();
        if (response.Usage is not null)
        {
            metadata["Usage"] = response.Usage;
        }

        var message = new ChatMessageContent(
            AuthorRole.Assistant,
            items,
            modelId,
            metadata: metadata.Count > 0 ? metadata : null);

        return [message];
    }

    public static LlmResponse FromMessageContents(IReadOnlyList<ChatMessageContent> contents)
    {
        if (contents.Count == 0)
            return new LlmResponse(JsonSerializer.SerializeToElement((string?)null, s_options));

        var firstMessage = contents[0];

        var textContent = firstMessage.Content;
        var contentElement = textContent is not null
            ? JsonSerializer.SerializeToElement(textContent, s_options)
            : JsonSerializer.SerializeToElement((string?)null, s_options);

        JsonElement? toolCalls = null;
        var functionCalls = firstMessage.Items.OfType<FunctionCallContent>().ToList();
        if (functionCalls.Count > 0)
        {
            var toolCallList = new List<SortedDictionary<string, JsonElement>>();
            foreach (var fc in functionCalls)
            {
                var tcDict = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["functionName"] = JsonSerializer.SerializeToElement(fc.FunctionName, s_options),
                };

                if (fc.Arguments is not null)
                {
                    var sortedArgs = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var kvp in fc.Arguments)
                    {
                        sortedArgs[kvp.Key] = kvp.Value;
                    }
                    tcDict["args"] = JsonSerializer.SerializeToElement(sortedArgs, s_options);
                }

                if (fc.Id is not null)
                    tcDict["id"] = JsonSerializer.SerializeToElement(fc.Id, s_options);

                if (fc.PluginName is not null)
                    tcDict["pluginName"] = JsonSerializer.SerializeToElement(fc.PluginName, s_options);

                toolCallList.Add(tcDict);
            }
            toolCalls = JsonSerializer.SerializeToElement(toolCallList, s_options);
        }

        TokenUsage? usage = null;
        if (firstMessage.Metadata?.TryGetValue("Usage", out var usageObj) == true && usageObj is TokenUsage tu)
        {
            usage = tu;
        }

        return new LlmResponse(contentElement, toolCalls, usage);
    }
}
