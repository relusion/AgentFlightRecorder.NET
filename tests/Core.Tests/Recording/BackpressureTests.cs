using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Options;
using Microsoft.Extensions.Logging;

namespace Core.Tests.Recording;

public sealed class BackpressureTests
{
    [Fact]
    public async Task DropStrategy_FullChannel_DoesNotBlock()
    {
        var sink = new SlowSink(delay: TimeSpan.FromMilliseconds(100));
        var logger = new TestLogger();

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = false,
            Logger = logger,
            Backpressure = new BackpressureOptions
            {
                Strategy = BackpressureStrategy.Drop,
                ChannelCapacity = 4 // Very small
            }
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        // Flood the channel — some events should be dropped
        for (int i = 0; i < 50; i++)
        {
            await recorder.AnnotateAsync($"note {i}");
        }

        await recorder.StopAsync();

        // Some events should have been dropped (logged at warning level)
        Assert.True(logger.WarningCount > 0, "Expected some events to be dropped");
    }

    [Fact]
    public async Task GracefulDrain_AllBufferedEventsProcessed()
    {
        var sink = new CountingSink();
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = false,
            Backpressure = new BackpressureOptions
            {
                Strategy = BackpressureStrategy.Block,
                ChannelCapacity = 1024
            }
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        for (int i = 0; i < 10; i++)
        {
            await recorder.AnnotateAsync($"note {i}");
        }

        await recorder.StopAsync();

        // RunStarted + 10 annotations + RunCompleted = 12
        Assert.Equal(12, sink.Count);
    }
}

internal sealed class SlowSink : IFlightSink
{
    private readonly TimeSpan _delay;

    public SlowSink(TimeSpan delay) => _delay = delay;

    public async ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct);
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class CountingSink : IFlightSink
{
    private int _count;
    public int Count => _count;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class TestLogger : ILogger<FlightRecorder>
{
    public int WarningCount;
    public int ErrorCount;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) Interlocked.Increment(ref WarningCount);
        if (logLevel == LogLevel.Error) Interlocked.Increment(ref ErrorCount);
    }
}
