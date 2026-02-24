using System.Text.Json;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Events;

public sealed class FlightEventTests
{
    private readonly CanonicalJsonSerializer _serializer = new();

    [Fact]
    public void FlightEvent_AllRequiredFieldsPopulated_Serializes()
    {
        var evt = CreateSampleEvent();
        var json = _serializer.Serialize(evt);

        Assert.Contains("\"schemaVersion\":\"1.0\"", json);
        Assert.Contains("\"runId\":", json);
        Assert.Contains("\"eventId\":", json);
        Assert.Contains("\"sequence\":", json);
        Assert.Contains("\"type\":\"Annotation\"", json);
    }

    [Fact]
    public void FlightEvent_RoundTrip()
    {
        var evt = CreateSampleEvent();
        var json = _serializer.Serialize(evt);
        var deserialized = _serializer.Deserialize<FlightEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(evt.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(evt.RunId, deserialized.RunId);
        Assert.Equal(evt.EventId, deserialized.EventId);
        Assert.Equal(evt.Sequence, deserialized.Sequence);
        Assert.Equal(evt.TraceId, deserialized.TraceId);
        Assert.Equal(evt.SpanId, deserialized.SpanId);
        Assert.Equal(evt.Type, deserialized.Type);
        Assert.Null(deserialized.ParentSpanId);
        Assert.Null(deserialized.Integrity);
    }

    [Fact]
    public void FlightEvent_WithIntegrity_RoundTrip()
    {
        var evt = CreateSampleEvent(integrity: new FlightIntegrity("", "abc123", "SHA-256"));

        var json = _serializer.Serialize(evt);
        var deserialized = _serializer.Deserialize<FlightEvent>(json);

        Assert.NotNull(deserialized?.Integrity);
        Assert.Equal("abc123", deserialized.Integrity.Hash);
        Assert.Equal("SHA-256", deserialized.Integrity.Algorithm);
    }

    [Fact]
    public void FlightEvent_GetPayload_ReturnsTypedPayload()
    {
        var payload = new AnnotationPayload("hello", null);
        var payloadElement = JsonSerializer.SerializeToElement(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var evt = CreateSampleEvent(payload: payloadElement);

        var extracted = evt.GetPayload<AnnotationPayload>();
        Assert.Equal("hello", extracted.Message);
    }

    [Fact]
    public void FlightEvent_WithParentSpanId_Serializes()
    {
        var evt = CreateSampleEvent(parentSpanId: "parent-span-1");
        var json = _serializer.Serialize(evt);

        Assert.Contains("\"parentSpanId\":\"parent-span-1\"", json);
    }

    [Fact]
    public void FlightEvent_PropertyOrder_IsAlphabetical()
    {
        var evt = CreateSampleEvent();
        var json = _serializer.Serialize(evt);

        // Verify key ordering: eventId < payload < runId < schemaVersion < sequence < spanId < timestampUtc < traceId < type
        var eventIdIdx = json.IndexOf("\"eventId\":", StringComparison.Ordinal);
        var payloadIdx = json.IndexOf("\"payload\":", StringComparison.Ordinal);
        var runIdIdx = json.IndexOf("\"runId\":", StringComparison.Ordinal);
        var schemaIdx = json.IndexOf("\"schemaVersion\":", StringComparison.Ordinal);
        var sequenceIdx = json.IndexOf("\"sequence\":", StringComparison.Ordinal);
        var spanIdIdx = json.IndexOf("\"spanId\":", StringComparison.Ordinal);
        var timestampIdx = json.IndexOf("\"timestampUtc\":", StringComparison.Ordinal);
        var traceIdIdx = json.IndexOf("\"traceId\":", StringComparison.Ordinal);
        var typeIdx = json.IndexOf("\"type\":", StringComparison.Ordinal);

        Assert.True(eventIdIdx < payloadIdx);
        Assert.True(payloadIdx < runIdIdx);
        Assert.True(runIdIdx < schemaIdx);
        Assert.True(schemaIdx < sequenceIdx);
        Assert.True(sequenceIdx < spanIdIdx);
        Assert.True(spanIdIdx < timestampIdx);
        Assert.True(timestampIdx < traceIdIdx);
        Assert.True(traceIdIdx < typeIdx);
    }

    private static FlightEvent CreateSampleEvent(
        string? parentSpanId = null,
        FlightIntegrity? integrity = null,
        JsonElement? payload = null) => new()
    {
        SchemaVersion = "1.0",
        RunId = "run-001",
        EventId = "evt-001",
        Sequence = 1,
        TimestampUtc = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
        TraceId = "trace-001",
        SpanId = "span-001",
        ParentSpanId = parentSpanId,
        Type = FlightEventTypes.Annotation,
        Payload = payload ?? JsonDocument.Parse("{\"message\":\"hello\"}").RootElement,
        Integrity = integrity
    };
}
