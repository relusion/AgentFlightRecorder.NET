using System.Text.Json;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Redaction;

namespace Core.Tests.Redaction;

public sealed class RedactionTests
{
    [Fact]
    public void ApiKeyRedactor_RedactsBearerTokens()
    {
        var evt = CreateEventWithPayload("{\"auth\":\"Bearer sk-abc123def456ghi789jkl\"}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("sk-abc123def456ghi789jkl", json);
        Assert.Contains("[REDACTED]", json);
    }

    [Fact]
    public void ApiKeyRedactor_RedactsOpenAiKeys()
    {
        var evt = CreateEventWithPayload("{\"key\":\"sk-abcdefghijklmnopqrstuvwxyz\"}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", json);
        Assert.Contains("[REDACTED]", json);
    }

    [Fact]
    public void ApiKeyRedactor_RedactsApiKeyParams()
    {
        var evt = CreateEventWithPayload("{\"url\":\"https://api.example.com?api_key=mySecretKeyValue123\"}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("mySecretKeyValue123", json);
    }

    [Fact]
    public void ApiKeyRedactor_LeavesCleanDataUntouched()
    {
        var evt = CreateEventWithPayload("{\"message\":\"Hello world\",\"count\":42}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);

        // Should be the same event (no changes)
        Assert.Equal(evt.Payload.GetRawText(), redacted.Payload.GetRawText());
    }

    [Fact]
    public void PiiRedactor_RedactsEmails()
    {
        var evt = CreateEventWithPayload("{\"contact\":\"user@example.com\"}");
        var redactor = new PiiRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("user@example.com", json);
        Assert.Contains("[PII_REDACTED]", json);
    }

    [Fact]
    public void PiiRedactor_RedactsPhoneNumbers()
    {
        var evt = CreateEventWithPayload("{\"phone\":\"(555) 123-4567\"}");
        var redactor = new PiiRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("555", json);
        Assert.Contains("[PII_REDACTED]", json);
    }

    [Fact]
    public void CompositeRedactor_AppliesMultipleRedactors()
    {
        var evt = CreateEventWithPayload("{\"auth\":\"Bearer sk-abc123def456ghi789jkl\",\"email\":\"test@example.com\"}");
        var redactor = new CompositeRedactor([new ApiKeyRedactor(), new PiiRedactor()]);

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("sk-abc123def456ghi789jkl", json);
        Assert.DoesNotContain("test@example.com", json);
    }

    [Fact]
    public void Redactor_ReturnsNewInstance_OriginalUnchanged()
    {
        var evt = CreateEventWithPayload("{\"key\":\"sk-abcdefghijklmnopqrstuvwxyz\"}");
        var originalPayloadText = evt.Payload.GetRawText();

        var redactor = new ApiKeyRedactor();
        var redacted = redactor.Redact(evt);

        // Original should be unchanged
        Assert.Equal(originalPayloadText, evt.Payload.GetRawText());
        // Redacted should be different
        Assert.NotEqual(originalPayloadText, redacted.Payload.GetRawText());
    }

    [Fact]
    public void Redactor_HandlesNestedObjects()
    {
        var evt = CreateEventWithPayload("{\"outer\":{\"inner\":{\"key\":\"sk-abcdefghijklmnopqrstuvwxyz\"}}}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", json);
    }

    [Fact]
    public void Redactor_HandlesArrays()
    {
        var evt = CreateEventWithPayload("{\"keys\":[\"sk-abcdefghijklmnopqrstuvwxyz\",\"safe-value\"]}");
        var redactor = new ApiKeyRedactor();

        var redacted = redactor.Redact(evt);
        var json = redacted.Payload.GetRawText();

        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", json);
        Assert.Contains("safe-value", json);
    }

    private static FlightEvent CreateEventWithPayload(string payloadJson) => new()
    {
        SchemaVersion = "1.0",
        RunId = "run-001",
        EventId = "evt-001",
        Sequence = 1,
        TimestampUtc = DateTimeOffset.UtcNow,
        TraceId = "trace-001",
        SpanId = "span-001",
        Type = FlightEventTypes.LlmRequest,
        Payload = JsonDocument.Parse(payloadJson).RootElement
    };
}
