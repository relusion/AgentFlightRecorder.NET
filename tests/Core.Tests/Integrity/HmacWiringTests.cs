using System.Text;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Integrity;

public sealed class HmacWiringTests : IAsyncDisposable
{
    private readonly HmacTestSink _sink = new();
    private readonly CanonicalJsonSerializer _serializer = new();

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WithHmacKey_StoresRunSignature()
    {
        var signingKey = Encoding.UTF8.GetBytes("test-signing-key-32bytes!padding!");
        var integrityProvider = new Sha256IntegrityProvider(_serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            IntegrityProvider = integrityProvider,
            HmacSigningKey = signingKey,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.AnnotateAsync("test event");
        await recorder.StopAsync();

        var runCompleted = _sink.Events.Last(e => e.Type == FlightEventTypes.RunCompleted);
        var payload = runCompleted.GetPayload<RunCompletedPayload>();

        Assert.NotNull(payload.RunSignature);
        Assert.NotEmpty(payload.RunSignature);
    }

    [Fact]
    public async Task StopAsync_WithHmacKey_SignatureIsVerifiable()
    {
        var signingKey = Encoding.UTF8.GetBytes("test-signing-key-32bytes!padding!");
        var integrityProvider = new Sha256IntegrityProvider(_serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            IntegrityProvider = integrityProvider,
            HmacSigningKey = signingKey,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.AnnotateAsync("event 1");
        await recorder.AnnotateAsync("event 2");
        await recorder.StopAsync();

        var runCompleted = _sink.Events.Last(e => e.Type == FlightEventTypes.RunCompleted);
        var payload = runCompleted.GetPayload<RunCompletedPayload>();

        // The signature should be over the chain hash before RunCompleted
        // Find the event just before RunCompleted
        var eventsBeforeRunCompleted = _sink.Events
            .TakeWhile(e => e.Type != FlightEventTypes.RunCompleted)
            .ToList();
        var lastEventBeforeRunCompleted = eventsBeforeRunCompleted.Last();

        Assert.NotNull(lastEventBeforeRunCompleted.Integrity);
        Assert.True(HmacSignatureProvider.VerifySignature(
            lastEventBeforeRunCompleted.Integrity.Hash, payload.RunSignature!, signingKey));
    }

    [Fact]
    public async Task StopAsync_WithoutHmacKey_RunSignatureIsNull()
    {
        var integrityProvider = new Sha256IntegrityProvider(_serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.StopAsync();

        var runCompleted = _sink.Events.Last(e => e.Type == FlightEventTypes.RunCompleted);
        var payload = runCompleted.GetPayload<RunCompletedPayload>();

        Assert.Null(payload.RunSignature);
    }

    [Fact]
    public async Task StopAsync_WithHmacKey_WrongKeyFailsVerification()
    {
        var signingKey = Encoding.UTF8.GetBytes("test-signing-key-32bytes!padding!");
        var wrongKey = Encoding.UTF8.GetBytes("wrong-signing-key-32bytes-padded");
        var integrityProvider = new Sha256IntegrityProvider(_serializer);

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            IntegrityProvider = integrityProvider,
            HmacSigningKey = signingKey,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();
        await recorder.AnnotateAsync("test");
        await recorder.StopAsync();

        var runCompleted = _sink.Events.Last(e => e.Type == FlightEventTypes.RunCompleted);
        var payload = runCompleted.GetPayload<RunCompletedPayload>();

        var eventsBeforeRunCompleted = _sink.Events
            .TakeWhile(e => e.Type != FlightEventTypes.RunCompleted)
            .ToList();
        var lastEventBeforeRunCompleted = eventsBeforeRunCompleted.Last();

        Assert.False(HmacSignatureProvider.VerifySignature(
            lastEventBeforeRunCompleted.Integrity!.Hash, payload.RunSignature!, wrongKey));
    }
}

internal sealed class HmacTestSink : IFlightSink
{
    private readonly List<FlightEvent> _events = [];
    public IReadOnlyList<FlightEvent> Events => _events;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        _events.Add(evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
