using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentFlightRecorder.Core.Redaction;

/// <summary>
/// Traverses a <see cref="JsonElement"/> and replaces string values matching a regex pattern.
/// </summary>
internal static class JsonRedactionHelper
{
    public static JsonElement RedactStrings(
        JsonElement element,
        Regex pattern,
        string replacement = "[REDACTED]",
        IReadOnlySet<string>? skipProperties = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = element.GetString();
                if (value is not null && pattern.IsMatch(value))
                {
                    var redacted = pattern.Replace(value, replacement);
                    return JsonDocument.Parse($"\"{EscapeJsonString(redacted)}\"").RootElement;
                }
                return element;
            }
            case JsonValueKind.Object:
            {
                using var doc = JsonDocument.Parse("{}");
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject())
                    {
                        writer.WritePropertyName(prop.Name);
                        if (skipProperties is not null && skipProperties.Contains(prop.Name))
                        {
                            prop.Value.WriteTo(writer);
                        }
                        else
                        {
                            var redacted = RedactStrings(prop.Value, pattern, replacement, skipProperties);
                            redacted.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                return JsonDocument.Parse(stream.ToArray()).RootElement;
            }
            case JsonValueKind.Array:
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        var redacted = RedactStrings(item, pattern, replacement, skipProperties);
                        redacted.WriteTo(writer);
                    }
                    writer.WriteEndArray();
                }
                return JsonDocument.Parse(stream.ToArray()).RootElement;
            }
            default:
                return element;
        }
    }

    private static string EscapeJsonString(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");
}
