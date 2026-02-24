using System.Text;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Integrity;

public sealed class IntegrityTests
{
    private readonly CanonicalJsonSerializer _serializer = new();
    private readonly Sha256IntegrityProvider _provider;

    public IntegrityTests()
    {
        _provider = new Sha256IntegrityProvider(_serializer);
    }

    [Fact]
    public void Compute_FirstEvent_HasEmptyPrevHash()
    {
        var evt = CreateEvent(1);
        var integrity = _provider.Compute(null, evt);

        Assert.Equal(string.Empty, integrity.PrevHash);
        Assert.NotEmpty(integrity.Hash);
        Assert.Equal("SHA-256", integrity.Algorithm);
    }

    [Fact]
    public void Compute_SecondEvent_ChainsToPrevious()
    {
        var evt1 = CreateEvent(1);
        var integrity1 = _provider.Compute(null, evt1);

        var evt2 = CreateEvent(2);
        var integrity2 = _provider.Compute(integrity1, evt2);

        Assert.Equal(integrity1.Hash, integrity2.PrevHash);
        Assert.NotEqual(integrity1.Hash, integrity2.Hash);
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        var evt = CreateEvent(1);
        var integrity1 = _provider.Compute(null, evt);
        var integrity2 = _provider.Compute(null, evt);

        Assert.Equal(integrity1.Hash, integrity2.Hash);
    }

    [Fact]
    public void Verify_ValidChain_ReturnsTrue()
    {
        var evt = CreateEvent(1);
        var integrity = _provider.Compute(null, evt);

        Assert.True(_provider.Verify(null, evt, integrity));
    }

    [Fact]
    public void Verify_TamperedEvent_ReturnsFalse()
    {
        var evt = CreateEvent(1);
        var integrity = _provider.Compute(null, evt);

        // Tamper with the event
        var tampered = new FlightEvent
        {
            SchemaVersion = evt.SchemaVersion,
            RunId = evt.RunId,
            EventId = evt.EventId,
            Sequence = evt.Sequence,
            TimestampUtc = evt.TimestampUtc,
            TraceId = evt.TraceId,
            SpanId = evt.SpanId,
            Type = evt.Type,
            Payload = JsonDocument.Parse("{\"tampered\":true}").RootElement,
            Integrity = integrity
        };

        Assert.False(_provider.Verify(null, tampered, integrity));
    }

    [Fact]
    public void IntegrityVerifier_ValidChain_Succeeds()
    {
        var events = BuildChain(10);
        var verifier = new IntegrityVerifier(_provider);

        var result = verifier.Verify(events);

        Assert.True(result.IsValid);
        Assert.Equal(-1, result.FirstInvalidIndex);
    }

    [Fact]
    public void IntegrityVerifier_TamperedEvent_FailsAtTamperedIndex()
    {
        var events = BuildChain(10).ToList();
        var verifier = new IntegrityVerifier(_provider);

        // Tamper with event at index 5
        var original = events[5];
        events[5] = new FlightEvent
        {
            SchemaVersion = original.SchemaVersion,
            RunId = original.RunId,
            EventId = original.EventId,
            Sequence = original.Sequence,
            TimestampUtc = original.TimestampUtc,
            TraceId = original.TraceId,
            SpanId = original.SpanId,
            Type = original.Type,
            Payload = JsonDocument.Parse("{\"tampered\":true}").RootElement,
            Integrity = original.Integrity
        };

        var result = verifier.Verify(events);

        Assert.False(result.IsValid);
        Assert.Equal(5, result.FirstInvalidIndex);
    }

    [Fact]
    public void HmacSignature_ComputeAndVerify()
    {
        var signingKey = Encoding.UTF8.GetBytes("my-secret-key");
        var finalHash = "abc123def456";

        var signature = HmacSignatureProvider.ComputeSignature(finalHash, signingKey);
        Assert.NotEmpty(signature);
        Assert.True(HmacSignatureProvider.VerifySignature(finalHash, signature, signingKey));
    }

    [Fact]
    public void HmacSignature_WrongKey_VerifyFails()
    {
        var correctKey = Encoding.UTF8.GetBytes("correct-key");
        var wrongKey = Encoding.UTF8.GetBytes("wrong-key");
        var finalHash = "abc123def456";

        var signature = HmacSignatureProvider.ComputeSignature(finalHash, correctKey);
        Assert.False(HmacSignatureProvider.VerifySignature(finalHash, signature, wrongKey));
    }

    [Fact]
    public async Task RedactionOccursBeforeIntegrity_VerifiedByChain()
    {
        var serializer = new CanonicalJsonSerializer();
        var provider = new Sha256IntegrityProvider(serializer);
        var redactor = new AgentFlightRecorder.Core.Redaction.ApiKeyRedactor();
        var sink = new InMemorySink();

        var options = new AgentFlightRecorder.Core.Options.FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true,
            Redactor = redactor,
            IntegrityProvider = provider
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.AnnotateAsync("key=sk-abcdefghijklmnopqrstuvwxyz");
        await recorder.StopAsync();

        // All events should have integrity
        foreach (var evt in sink.Events)
        {
            Assert.NotNull(evt.Integrity);
        }

        // Hash chain should be valid (computed over redacted data)
        var verifier = new IntegrityVerifier(provider);
        var result = verifier.Verify(sink.Events.ToList());
        Assert.True(result.IsValid);

        // The annotation event should be redacted
        var annotation = sink.Events.First(e => e.Type == FlightEventTypes.Annotation);
        var payloadJson = annotation.Payload.GetRawText();
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", payloadJson);
    }

    private IReadOnlyList<FlightEvent> BuildChain(int count)
    {
        var events = new List<FlightEvent>();
        FlightIntegrity? previous = null;

        for (int i = 0; i < count; i++)
        {
            var evt = CreateEvent(i + 1);
            var integrity = _provider.Compute(previous, evt);
            var withIntegrity = new FlightEvent
            {
                SchemaVersion = evt.SchemaVersion,
                RunId = evt.RunId,
                EventId = evt.EventId,
                Sequence = evt.Sequence,
                TimestampUtc = evt.TimestampUtc,
                TraceId = evt.TraceId,
                SpanId = evt.SpanId,
                Type = evt.Type,
                Payload = evt.Payload,
                Integrity = integrity
            };
            events.Add(withIntegrity);
            previous = integrity;
        }

        return events;
    }

    private static FlightEvent CreateEvent(long sequence) => new()
    {
        SchemaVersion = "1.0",
        RunId = "run-001",
        EventId = $"evt-{sequence:D3}",
        Sequence = sequence,
        TimestampUtc = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero).AddSeconds(sequence),
        TraceId = "trace-001",
        SpanId = "span-001",
        Type = FlightEventTypes.Annotation,
        Payload = JsonDocument.Parse($"{{\"message\":\"event {sequence}\"}}").RootElement
    };
}

internal sealed class InMemorySink : IFlightSink
{
    private readonly List<FlightEvent> _events = [];
    public IReadOnlyList<FlightEvent> Events => _events;
    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default) { _events.Add(evt); return ValueTask.CompletedTask; }
    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
