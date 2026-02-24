using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFlightRecorder.Core.Serialization;

/// <summary>
/// A JSON converter factory that ensures all object properties are written in alphabetical order.
/// Required for canonical serialization — identical logical data must produce identical byte sequences.
/// </summary>
internal sealed class SortedPropertyJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        // Convert any object type that isn't a primitive, collection, or JsonElement
        !typeToConvert.IsPrimitive
        && typeToConvert != typeof(string)
        && typeToConvert != typeof(decimal)
        && typeToConvert != typeof(JsonElement)
        && typeToConvert != typeof(DateTimeOffset)
        && typeToConvert != typeof(DateTime)
        && typeToConvert != typeof(Guid)
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(SortedPropertyJsonConverter<>).MakeGenericType(typeToConvert),
            options)!;
}

internal sealed class SortedPropertyJsonConverter<T> : JsonConverter<T>
{
    private readonly (string JsonName, Func<T, object?> Getter, Type PropertyType)[] _sortedProperties;
    private readonly JsonSerializerOptions _innerOptions;

    public SortedPropertyJsonConverter(JsonSerializerOptions options)
    {
        // Create inner options without this converter to avoid infinite recursion
        _innerOptions = new JsonSerializerOptions(options);
        for (int i = _innerOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (_innerOptions.Converters[i] is SortedPropertyJsonConverterFactory)
            {
                _innerOptions.Converters.RemoveAt(i);
                break;
            }
        }

        var type = typeof(T);
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead)
            .OrderBy(p =>
            {
                var attr = p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                    .FirstOrDefault() as JsonPropertyNameAttribute;
                return attr?.Name ?? options.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name;
            }, StringComparer.Ordinal)
            .Select(p =>
            {
                var attr = p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                    .FirstOrDefault() as JsonPropertyNameAttribute;
                var jsonName = attr?.Name ?? options.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name;
                var getter = new Func<T, object?>(obj => p.GetValue(obj));
                return (jsonName, getter, p.PropertyType);
            })
            .ToArray();

        _sortedProperties = properties;
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(ref reader, _innerOptions);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        foreach (var (jsonName, getter, propertyType) in _sortedProperties)
        {
            var propValue = getter(value);

            if (propValue is null && options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
                continue;

            writer.WritePropertyName(jsonName);
            JsonSerializer.Serialize(writer, propValue, propertyType, _innerOptions);
        }

        writer.WriteEndObject();
    }
}
