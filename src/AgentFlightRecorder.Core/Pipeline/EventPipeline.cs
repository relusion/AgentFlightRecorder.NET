using System.Diagnostics;
using AgentFlightRecorder.Core.Events;
using Microsoft.Extensions.Logging;

namespace AgentFlightRecorder.Core.Pipeline;

/// <summary>
/// Sequential processing stages: redaction → integrity → sink.
/// Handles sink errors with a recursive-failure guard.
/// </summary>
internal sealed class EventPipeline
{
    private static readonly ActivitySource s_activitySource = new("AgentFlightRecorder");
    private readonly IRedactor? _redactor;
    private readonly IIntegrityProvider? _integrityProvider;
    private readonly IFlightSink _sink;
    private readonly ILogger? _logger;
    private FlightIntegrity? _previousIntegrity;
    private bool _inErrorHandler;

    internal FlightIntegrity? LastIntegrity => _previousIntegrity;

    public EventPipeline(
        IFlightSink sink,
        IRedactor? redactor = null,
        IIntegrityProvider? integrityProvider = null,
        ILogger? logger = null)
    {
        _sink = sink;
        _redactor = redactor;
        _integrityProvider = integrityProvider;
        _logger = logger;
    }

    public async ValueTask ProcessAsync(FlightEvent evt, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("EventPipeline.Process");
        activity?.SetTag("event.type", evt.Type);
        activity?.SetTag("event.sequence", evt.Sequence);

        try
        {
            if (_redactor is not null)
                evt = _redactor.Redact(evt);

            if (_integrityProvider is not null)
            {
                var integrity = _integrityProvider.Compute(_previousIntegrity, evt);
                evt = new FlightEvent
                {
                    SchemaVersion = evt.SchemaVersion,
                    RunId = evt.RunId,
                    EventId = evt.EventId,
                    Sequence = evt.Sequence,
                    TimestampUtc = evt.TimestampUtc,
                    TraceId = evt.TraceId,
                    SpanId = evt.SpanId,
                    ParentSpanId = evt.ParentSpanId,
                    Type = evt.Type,
                    Payload = evt.Payload,
                    Integrity = integrity
                };
                _previousIntegrity = integrity;
            }

            await _sink.WriteAsync(evt, ct);
        }
        catch (Exception ex) when (!_inErrorHandler)
        {
            _logger?.LogWarning(ex, "Sink write failed for event {Sequence} ({Type})", evt.Sequence, evt.Type);

            // Attempt to write a RecorderError event — guard against recursion
            _inErrorHandler = true;
            try
            {
                var errorPayload = new RecorderErrorPayload(
                    ex.GetType().Name,
                    ex.Message,
                    evt.Sequence,
                    evt.Type);

                var errorEvent = new FlightEvent
                {
                    SchemaVersion = evt.SchemaVersion,
                    RunId = evt.RunId,
                    EventId = Guid.NewGuid().ToString(),
                    Sequence = evt.Sequence,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    TraceId = evt.TraceId,
                    SpanId = evt.SpanId,
                    Type = FlightEventTypes.RecorderError,
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(errorPayload,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                };

                await _sink.WriteAsync(errorEvent, ct);
            }
            catch (Exception innerEx)
            {
                _logger?.LogError(innerEx, "Failed to write RecorderError event — recursive failure guard triggered");
            }
            finally
            {
                _inErrorHandler = false;
            }
        }
    }
}
