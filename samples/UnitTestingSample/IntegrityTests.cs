using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Testing.Xunit;
using UnitTestingSample.Mocks;
using Xunit;

namespace UnitTestingSample;

/// <summary>
/// Sha256IntegrityProvider for tamper-resistant recordings and
/// FlightAssert.IntegrityChainValid for chain verification.
/// </summary>
public sealed class IntegrityTests
{
    private readonly CanonicalJsonSerializer _serializer = new();

    /// <summary>
    /// When Sha256IntegrityProvider is configured, every recorded event carries
    /// an Integrity field with PrevHash, Hash, and Algorithm.
    /// </summary>
    [Fact]
    public async Task RecordWithIntegrity_ProducesHashChain()
    {
        var integrityProvider = new Sha256IntegrityProvider(_serializer);
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"test\"}]").RootElement);
        await llm.CompleteAsync(request, CancellationToken.None);
        await recorder.StopAsync();

        // Every event should have an Integrity field
        foreach (var evt in sink.Events)
        {
            Assert.NotNull(evt.Integrity);
            Assert.Equal("SHA-256", evt.Integrity.Algorithm);
            Assert.False(string.IsNullOrEmpty(evt.Integrity.Hash));
        }

        // First event's PrevHash should be empty (no predecessor)
        Assert.Equal(string.Empty, sink.Events[0].Integrity!.PrevHash);

        // Subsequent events chain to previous hash
        for (int i = 1; i < sink.Events.Count; i++)
        {
            Assert.Equal(sink.Events[i - 1].Integrity!.Hash, sink.Events[i].Integrity!.PrevHash);
        }
    }

    /// <summary>
    /// Modifying an event's payload after recording breaks the integrity chain.
    /// FlightAssert.IntegrityChainValid detects this tampering.
    /// </summary>
    [Fact]
    public async Task IntegrityChain_DetectsTampering()
    {
        var integrityProvider = new Sha256IntegrityProvider(_serializer);
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
        var request = new LlmRequest("openai", "gpt-4",
            JsonDocument.Parse("[{\"role\":\"user\",\"content\":\"test\"}]").RootElement);
        await llm.CompleteAsync(request, CancellationToken.None);
        await recorder.StopAsync();

        // Tamper: create a modified copy of the second event with different payload
        var tamperedEvents = sink.Events.ToList();
        var original = tamperedEvents[1];
        var tamperedPayload = JsonDocument.Parse("{\"tampered\":true}").RootElement;
        tamperedEvents[1] = new FlightEvent
        {
            SchemaVersion = original.SchemaVersion,
            RunId = original.RunId,
            EventId = original.EventId,
            Sequence = original.Sequence,
            TimestampUtc = original.TimestampUtc,
            TraceId = original.TraceId,
            SpanId = original.SpanId,
            Type = original.Type,
            Payload = tamperedPayload,
            Integrity = original.Integrity // Hash no longer matches tampered payload
        };

        // Verification should fail on the tampered event
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            FlightAssert.IntegrityChainValid(tamperedEvents, integrityProvider));
    }

    /// <summary>
    /// An unmodified recording passes integrity chain verification.
    /// </summary>
    [Fact]
    public async Task IntegrityChain_ValidForCleanRecording()
    {
        var integrityProvider = new Sha256IntegrityProvider(_serializer);
        var sink = new InMemorySink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            IntegrityProvider = integrityProvider,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var llm = new RecordingLlmClient(new SampleLlmClient(), recorder, _serializer);
        for (int i = 0; i < 3; i++)
        {
            var request = new LlmRequest("openai", "gpt-4",
                JsonDocument.Parse($"[{{\"role\":\"user\",\"content\":\"message {i}\"}}]").RootElement);
            await llm.CompleteAsync(request, CancellationToken.None);
        }
        await recorder.StopAsync();

        FlightAssert.IntegrityChainValid(sink.Events, integrityProvider);
    }
}
