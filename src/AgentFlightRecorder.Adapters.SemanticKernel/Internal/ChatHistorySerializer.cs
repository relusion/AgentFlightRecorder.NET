using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentFlightRecorder.Adapters.SemanticKernel.Internal;

/// <summary>
/// Serializes/deserializes ChatHistory to/from canonical JsonElement with sorted keys.
/// </summary>
internal static class ChatHistorySerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonElement SerializeHistory(ChatHistory history)
    {
        var messages = new List<SortedDictionary<string, JsonElement>>();

        foreach (var message in history)
        {
            messages.Add(SerializeMessageToSorted(message));
        }

        return JsonSerializer.SerializeToElement(messages, s_options);
    }

    public static ChatHistory DeserializeHistory(JsonElement element)
    {
        var history = new ChatHistory();

        foreach (var msgElement in element.EnumerateArray())
        {
            var role = msgElement.GetProperty("role").GetString()!;
            var authorRole = new AuthorRole(role);

            string? content = null;
            if (msgElement.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                content = contentProp.GetString();
            }

            var items = new ChatMessageContentItemCollection();
            if (msgElement.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemElement in itemsProp.EnumerateArray())
                {
                    var item = DeserializeContentItem(itemElement);
                    if (item is not null)
                        items.Add(item);
                }
            }

            string? modelId = null;
            if (msgElement.TryGetProperty("modelId", out var modelIdProp) && modelIdProp.ValueKind == JsonValueKind.String)
            {
                modelId = modelIdProp.GetString();
            }

            var msg = new ChatMessageContent(authorRole, items, modelId);
            history.Add(msg);
        }

        return history;
    }

    private static SortedDictionary<string, JsonElement> SerializeMessageToSorted(ChatMessageContent message)
    {
        var dict = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["content"] = message.Content is not null
                ? JsonSerializer.SerializeToElement(message.Content, s_options)
                : JsonSerializer.SerializeToElement((string?)null, s_options),
            ["items"] = SerializeItems(message.Items),
            ["metadata"] = SerializeMetadata(message.Metadata),
            ["role"] = JsonSerializer.SerializeToElement(message.Role.Label, s_options),
        };

        if (message.ModelId is not null)
        {
            dict["modelId"] = JsonSerializer.SerializeToElement(message.ModelId, s_options);
        }

        return dict;
    }

    private static JsonElement SerializeItems(ChatMessageContentItemCollection items)
    {
        var serializedItems = new List<SortedDictionary<string, JsonElement>>();

        foreach (var item in items)
        {
            serializedItems.Add(SerializeContentItem(item));
        }

        return JsonSerializer.SerializeToElement(serializedItems, s_options);
    }

    private static SortedDictionary<string, JsonElement> SerializeContentItem(KernelContent item)
    {
        return item switch
        {
            TextContent text => new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["kind"] = JsonSerializer.SerializeToElement("text", s_options),
                ["text"] = JsonSerializer.SerializeToElement(text.Text, s_options),
            },
            ImageContent image => new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["kind"] = JsonSerializer.SerializeToElement("image", s_options),
                ["uri"] = JsonSerializer.SerializeToElement(image.Uri?.ToString(), s_options),
            },
            FunctionCallContent funcCall => SerializeFunctionCall(funcCall),
            FunctionResultContent funcResult => SerializeFunctionResult(funcResult),
            _ => new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["kind"] = JsonSerializer.SerializeToElement("unknown", s_options),
                ["type"] = JsonSerializer.SerializeToElement(item.GetType().Name, s_options),
                ["value"] = JsonSerializer.SerializeToElement(item.ToString(), s_options),
            },
        };
    }

    private static SortedDictionary<string, JsonElement> SerializeFunctionCall(FunctionCallContent funcCall)
    {
        var dict = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["kind"] = JsonSerializer.SerializeToElement("functionCall", s_options),
            ["functionName"] = JsonSerializer.SerializeToElement(funcCall.FunctionName, s_options),
        };

        if (funcCall.Arguments is not null)
        {
            var sortedArgs = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kvp in funcCall.Arguments)
            {
                sortedArgs[kvp.Key] = kvp.Value;
            }
            dict["args"] = JsonSerializer.SerializeToElement(sortedArgs, s_options);
        }

        if (funcCall.Id is not null)
            dict["id"] = JsonSerializer.SerializeToElement(funcCall.Id, s_options);

        if (funcCall.PluginName is not null)
            dict["pluginName"] = JsonSerializer.SerializeToElement(funcCall.PluginName, s_options);

        return dict;
    }

    private static SortedDictionary<string, JsonElement> SerializeFunctionResult(FunctionResultContent funcResult)
    {
        var dict = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["kind"] = JsonSerializer.SerializeToElement("functionResult", s_options),
            ["functionName"] = JsonSerializer.SerializeToElement(funcResult.FunctionName, s_options),
        };

        if (funcResult.CallId is not null)
            dict["id"] = JsonSerializer.SerializeToElement(funcResult.CallId, s_options);

        if (funcResult.PluginName is not null)
            dict["pluginName"] = JsonSerializer.SerializeToElement(funcResult.PluginName, s_options);

        if (funcResult.Result is not null)
            dict["result"] = JsonSerializer.SerializeToElement(funcResult.Result, s_options);

        return dict;
    }

    private static JsonElement SerializeMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return JsonSerializer.SerializeToElement(new SortedDictionary<string, object?>(StringComparer.Ordinal), s_options);

        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in metadata)
        {
            sorted[kvp.Key] = kvp.Value;
        }
        return JsonSerializer.SerializeToElement(sorted, s_options);
    }

    private static KernelContent? DeserializeContentItem(JsonElement element)
    {
        if (!element.TryGetProperty("kind", out var kindProp))
            return null;

        var kind = kindProp.GetString();
        return kind switch
        {
            "text" => new TextContent(element.GetProperty("text").GetString()),
            "image" => element.TryGetProperty("uri", out var uriProp) && uriProp.ValueKind == JsonValueKind.String
                ? new ImageContent(new Uri(uriProp.GetString()!))
                : new ImageContent("about:blank"),
            "functionCall" => DeserializeFunctionCall(element),
            "functionResult" => DeserializeFunctionResult(element),
            _ => null,
        };
    }

    private static FunctionCallContent DeserializeFunctionCall(JsonElement element)
    {
        var functionName = element.GetProperty("functionName").GetString()!;
        string? pluginName = element.TryGetProperty("pluginName", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString() : null;
        string? id = element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() : null;

        KernelArguments? args = null;
        if (element.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
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

        return new FunctionCallContent(functionName, pluginName, id, args);
    }

    private static FunctionResultContent DeserializeFunctionResult(JsonElement element)
    {
        var functionName = element.GetProperty("functionName").GetString()!;
        string? pluginName = element.TryGetProperty("pluginName", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString() : null;
        string? id = element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() : null;

        object? result = null;
        if (element.TryGetProperty("result", out var resultProp))
        {
            result = resultProp.ValueKind switch
            {
                JsonValueKind.String => resultProp.GetString(),
                _ => resultProp.GetRawText(),
            };
        }

        return new FunctionResultContent(functionName, pluginName, id, result);
    }
}
