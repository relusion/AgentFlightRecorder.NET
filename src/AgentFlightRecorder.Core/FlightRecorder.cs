using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentFlightRecorder.Core;

/// <summary>
/// Records agent run events into a structured trace via a channel-based pipeline.
/// </summary>
public sealed class FlightRecorder : IFlightRecorder, IAsyncDisposable
{
    private readonly FlightRecorderOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly EventPipeline? _pipeline;
    private readonly Channel<FlightEvent>? _channel;
    private static readonly ActivitySource s_activitySource = new("AgentFlightRecorder");
    private Task? _consumerTask;
    private long _sequence;
    private bool _started;
    private bool _stopped;
    private string _currentTraceId;
    private string _currentSpanId;

    public string RunId { get; }
    public FlightMode Mode => _options.Mode;

    public FlightRecorder(FlightRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = options.Logger ?? NullLogger<FlightRecorder>.Instance;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        RunId = Guid.NewGuid().ToString();
        _currentTraceId = Guid.NewGuid().ToString("N");
        _currentSpanId = Guid.NewGuid().ToString("N");

        if (options.Mode == FlightMode.Passthrough)
        {
            // True no-op: no channel, no pipeline, no background task
            _pipeline = null;
            _channel = null;
            return;
        }

        if (options.Mode == FlightMode.Record)
        {
            _pipeline = new EventPipeline(
                options.Sink,
                options.Redactor,
                options.IntegrityProvider,
                _logger);

            if (!options.SynchronousMode)
            {
                var channelOptions = new BoundedChannelOptions(options.Backpressure.ChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    // For Drop mode, use Wait so TryWrite returns false when full (allows us to log drops).
                    // For Block mode, use Wait for WriteAsync blocking.
                    // For Buffer mode, use DropOldest to discard oldest entries.
                    FullMode = options.Backpressure.Strategy switch
                    {
                        BackpressureStrategy.Drop => BoundedChannelFullMode.Wait,
                        BackpressureStrategy.Block => BoundedChannelFullMode.Wait,
                        BackpressureStrategy.Buffer => BoundedChannelFullMode.DropOldest,
                        _ => BoundedChannelFullMode.Wait
                    }
                };
                _channel = Channel.CreateBounded<FlightEvent>(channelOptions);
            }
        }
    }

    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        if (_options.Mode == FlightMode.Passthrough) return;
        if (_started) return;
        _started = true;

        using var activity = s_activitySource.StartActivity("FlightRecorder.Start");

        if (!_options.SynchronousMode && _channel is not null)
        {
            _consumerTask = Task.Run(() => ConsumeAsync(ct), ct);
        }

        var payload = new RunStartedPayload(null, null);
        await EmitAsync(FlightEventTypes.RunStarted, payload, ct);
    }

    public async ValueTask StopAsync(CancellationToken ct = default)
    {
        if (_options.Mode == FlightMode.Passthrough) return;
        if (_stopped) return;
        _stopped = true;

        using var activity = s_activitySource.StartActivity("FlightRecorder.Stop");

        string? runSignature = null;
        if (_options.HmacSigningKey is not null && _pipeline?.LastIntegrity is not null)
        {
            runSignature = HmacSignatureProvider.ComputeSignature(
                _pipeline.LastIntegrity.Hash, _options.HmacSigningKey);
        }

        var payload = new RunCompletedPayload("Completed", runSignature, null);
        await EmitAsync(FlightEventTypes.RunCompleted, payload, ct);

        if (_channel is not null)
        {
            _channel.Writer.Complete();

            if (_consumerTask is not null)
            {
                var drainTimeout = _options.Backpressure.DrainTimeout;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(drainTimeout);

                try
                {
                    await _consumerTask.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("Channel drain timed out after {Timeout}", drainTimeout);
                }
            }
        }

        await _options.Sink.FlushAsync(ct);
    }

    public IFlightSpan StartSpan(string name, FlightSpanKind kind = FlightSpanKind.Internal)
    {
        if (_options.Mode == FlightMode.Passthrough)
            return new NoOpFlightSpan(name, kind);

        return new FlightSpan(name, kind, _currentSpanId, this, _timeProvider);
    }

    public async ValueTask CheckpointAsync(string name, object state, IStateSerializer serializer, CancellationToken ct = default)
    {
        if (_options.Mode == FlightMode.Passthrough) return;

        var payload = new StateCheckpointPayload(name, serializer.Serialize(state), null);
        await EmitAsync(FlightEventTypes.StateCheckpoint, payload, ct);
    }

    public async ValueTask AnnotateAsync(string message, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        if (_options.Mode == FlightMode.Passthrough) return;

        var payload = new AnnotationPayload(message, tags);
        await EmitAsync(FlightEventTypes.Annotation, payload, ct);
    }

    internal async ValueTask EmitAsync(string eventType, object payload, CancellationToken ct, string? spanId = null, string? parentSpanId = null)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var evt = new FlightEvent
        {
            SchemaVersion = "1.0",
            RunId = RunId,
            EventId = Guid.NewGuid().ToString(),
            Sequence = sequence,
            TimestampUtc = _timeProvider.GetUtcNow(),
            TraceId = _currentTraceId,
            SpanId = spanId ?? _currentSpanId,
            ParentSpanId = parentSpanId,
            Type = eventType,
            Payload = JsonSerializer.SerializeToElement(payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })
        };

        if (_options.SynchronousMode && _pipeline is not null)
        {
            await _pipeline.ProcessAsync(evt, ct);
            return;
        }

        if (_channel is not null)
        {
            if (_options.Backpressure.Strategy == BackpressureStrategy.Drop)
            {
                if (!_channel.Writer.TryWrite(evt))
                {
                    _logger.LogWarning("Event {Sequence} ({Type}) dropped — channel full", evt.Sequence, evt.Type);
                }
            }
            else
            {
                await _channel.Writer.WriteAsync(evt, ct);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel!.Reader.ReadAllAsync(ct))
            {
                await _pipeline!.ProcessAsync(evt, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline consumer crashed unexpectedly");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped && _started)
        {
            await StopAsync();
        }

        if (_options.Mode != FlightMode.Passthrough)
        {
            await _options.Sink.DisposeAsync();
        }
    }
}
