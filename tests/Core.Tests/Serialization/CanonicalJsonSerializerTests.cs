using System.Text.Json;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Serialization;

public sealed class CanonicalJsonSerializerTests
{
    private readonly CanonicalJsonSerializer _serializer = new();

    [Fact]
    public void Serialize_ProducesCamelCasePropertyNames()
    {
        var payload = new AnnotationPayload("hello", null);
        var json = _serializer.Serialize(payload);

        Assert.Contains("\"message\":", json);
        Assert.DoesNotContain("\"Message\":", json);
    }

    [Fact]
    public void Serialize_ProducesAlphabeticalPropertyOrder()
    {
        var payload = new ToolCallCompletedPayload(
            ToolName: "search",
            Args: JsonDocument.Parse("{}").RootElement,
            ArgsHash: "abc123",
            Result: JsonDocument.Parse("{\"found\":true}").RootElement,
            DurationMs: 42,
            Attempt: 1,
            IdempotencyKey: null);

        var json = _serializer.Serialize(payload);

        // Verify properties appear in alphabetical (camelCase) order
        var argsIdx = json.IndexOf("\"args\":", StringComparison.Ordinal);
        var argsHashIdx = json.IndexOf("\"argsHash\":", StringComparison.Ordinal);
        var attemptIdx = json.IndexOf("\"attempt\":", StringComparison.Ordinal);
        var durationIdx = json.IndexOf("\"durationMs\":", StringComparison.Ordinal);
        var resultIdx = json.IndexOf("\"result\":", StringComparison.Ordinal);
        var toolNameIdx = json.IndexOf("\"toolName\":", StringComparison.Ordinal);

        Assert.True(argsIdx < argsHashIdx, "args should come before argsHash");
        Assert.True(argsHashIdx < attemptIdx, "argsHash should come before attempt");
        Assert.True(attemptIdx < durationIdx, "attempt should come before durationMs");
        Assert.True(durationIdx < resultIdx, "durationMs should come before result");
        Assert.True(resultIdx < toolNameIdx, "result should come before toolName");
    }

    [Fact]
    public void Serialize_OmitsNullProperties()
    {
        var payload = new AnnotationPayload("hello", null);
        var json = _serializer.Serialize(payload);

        Assert.DoesNotContain("\"tags\":", json);
    }

    [Fact]
    public void Serialize_IncludesNonNullOptionalProperties()
    {
        var tags = new Dictionary<string, string> { ["env"] = "test" };
        var payload = new AnnotationPayload("hello", tags);
        var json = _serializer.Serialize(payload);

        Assert.Contains("\"tags\":", json);
    }

    [Fact]
    public void Serialize_ProducesNoWhitespace()
    {
        var payload = new RunStartedPayload("test-run", new Dictionary<string, string> { ["a"] = "1" });
        var json = _serializer.Serialize(payload);

        // No indentation, no newlines
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("  ", json);
    }

    [Fact]
    public void Serialize_IsDeterministic_MultipleCalls()
    {
        var payload = new ToolCallStartedPayload(
            ToolName: "search",
            Args: JsonDocument.Parse("{\"query\":\"hello\"}").RootElement,
            ArgsHash: "def456",
            Attempt: 1,
            IdempotencyKey: null);

        var json1 = _serializer.Serialize(payload);
        var json2 = _serializer.Serialize(payload);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void SerializeToUtf8Bytes_IsDeterministic()
    {
        var payload = new AnnotationPayload("test", null);

        var bytes1 = _serializer.SerializeToUtf8Bytes(payload);
        var bytes2 = _serializer.SerializeToUtf8Bytes(payload);

        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void Serialize_GoldenString_Annotation()
    {
        var payload = new AnnotationPayload("checkpoint reached", null);
        var json = _serializer.Serialize(payload);

        Assert.Equal("{\"message\":\"checkpoint reached\"}", json);
    }

    [Fact]
    public void Serialize_GoldenString_TokenUsage()
    {
        var usage = new TokenUsage(100, 50, 150);
        var json = _serializer.Serialize(usage);

        Assert.Equal("{\"completionTokens\":50,\"promptTokens\":100,\"totalTokens\":150}", json);
    }

    [Fact]
    public void RoundTrip_AnnotationPayload()
    {
        var original = new AnnotationPayload("test message", new Dictionary<string, string> { ["key"] = "value" });
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<AnnotationPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Message, deserialized.Message);
        Assert.Equal(original.Tags, deserialized.Tags);
    }

    [Fact]
    public void RoundTrip_RunStartedPayload()
    {
        var original = new RunStartedPayload("my-run", new Dictionary<string, string> { ["env"] = "prod" });
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<RunStartedPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RunName, deserialized.RunName);
        Assert.Equal(original.Metadata, deserialized.Metadata);
    }

    [Fact]
    public void RoundTrip_TokenUsage()
    {
        var original = new TokenUsage(100, 50, 150);
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TokenUsage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void RoundTrip_Utf8Bytes()
    {
        var original = new AnnotationPayload("utf8 test", null);
        var bytes = _serializer.SerializeToUtf8Bytes(original);
        var deserialized = _serializer.Deserialize<AnnotationPayload>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Message, deserialized.Message);
    }

    [Fact]
    public void RoundTrip_InvocationKey()
    {
        var key = new InvocationKey("tool", "search", "abc123", 1, null);
        var json = _serializer.Serialize(key);
        var deserialized = _serializer.Deserialize<InvocationKey>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(key, deserialized);
    }

    [Fact]
    public void Serialize_GoldenString_InvocationKey()
    {
        var key = new InvocationKey("tool", "search", "abc123", 1, null);
        var json = _serializer.Serialize(key);

        Assert.Equal("{\"argsHash\":\"abc123\",\"attempt\":1,\"kind\":\"tool\",\"name\":\"search\"}", json);
    }

    [Fact]
    public void Serialize_FlightIntegrity()
    {
        var integrity = new FlightIntegrity("prev123", "hash456", "SHA-256");
        var json = _serializer.Serialize(integrity);

        Assert.Equal("{\"algorithm\":\"SHA-256\",\"hash\":\"hash456\",\"prevHash\":\"prev123\"}", json);
    }

    [Fact]
    public void RoundTrip_FlightIntegrity()
    {
        var original = new FlightIntegrity("prev123", "hash456", "SHA-256");
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<FlightIntegrity>(json);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void RoundTrip_LlmErrorPayload()
    {
        var original = new LlmErrorPayload("openai", "gpt-4", "RateLimitError", "Too many requests");
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<LlmErrorPayload>(json);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void RoundTrip_StateCheckpointPayload()
    {
        var state = JsonDocument.Parse("{\"step\":5}").RootElement;
        var original = new StateCheckpointPayload("step-5", state, null);
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<StateCheckpointPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.CheckpointName, deserialized.CheckpointName);
    }

    [Fact]
    public void InvocationKey_EqualityWorks()
    {
        var key1 = new InvocationKey("tool", "search", "abc", 1, null);
        var key2 = new InvocationKey("tool", "search", "abc", 1, null);
        var key3 = new InvocationKey("tool", "search", "xyz", 1, null);

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
    }

    [Fact]
    public void FlightEventTypes_AllConstantsUnique()
    {
        var type = typeof(FlightEventTypes);
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.True(fields.Count > 0, "Should have at least one event type constant");
        Assert.Equal(fields.Count, fields.Distinct().Count());
    }

    [Fact]
    public void FlightEventTypes_ContainsAllExpectedTypes()
    {
        var expected = new[]
        {
            "RunStarted", "RunCompleted", "SpanStarted", "SpanCompleted",
            "LlmRequest", "LlmResponse", "LlmError",
            "ToolCallStarted", "ToolCallCompleted", "ToolCallFailed",
            "StateCheckpoint", "Annotation", "RecorderError"
        };

        foreach (var eventType in expected)
        {
            var field = typeof(FlightEventTypes).GetField(eventType,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            Assert.Equal(eventType, (string)field.GetRawConstantValue()!);
        }
    }
}
