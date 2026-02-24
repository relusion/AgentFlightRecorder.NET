using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFlightRecorder.Core.Serialization;

/// <summary>
/// Provides deterministic JSON serialization with alphabetical property ordering,
/// no whitespace, camelCase naming, and UTF-8 output.
/// </summary>
public sealed class CanonicalJsonSerializer : ICanonicalJsonSerializer
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new SortedPropertyJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> used by this serializer.
    /// Useful for components that need compatible deserialization.
    /// </summary>
    internal static JsonSerializerOptions Options => s_options;

    public byte[] SerializeToUtf8Bytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, s_options);

    public string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, s_options);

    public T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<T>(utf8Json, s_options);

    public T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, s_options);
}
