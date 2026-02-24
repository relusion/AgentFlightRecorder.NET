using System.Diagnostics;
using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;

namespace Core.Tests.Recording;

public sealed class PerformanceTests : IAsyncDisposable
{
    private readonly PerfCountingSink _sink = new();
    private readonly CanonicalJsonSerializer _serializer = new();

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync();
    }

    [Fact]
    public async Task Record10kEvents_CompletesWithStableMemory()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = true
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(new PerfToolExecutor(), recorder, _serializer);

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();

        // Record 5000 tool invocations = 10,000 events (ToolCallStarted + ToolCallCompleted each)
        for (int i = 0; i < 5000; i++)
        {
            var invocation = new ToolInvocation("perf-tool",
                JsonDocument.Parse($"{{\"i\":{i}}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        sw.Stop();
        await recorder.StopAsync();

        var memAfter = GC.GetTotalMemory(forceFullCollection: true);

        // Verify all events recorded: RunStarted + 10000 tool events + RunCompleted = 10002
        Assert.Equal(10002, _sink.EventCount);

        // Memory growth should be bounded (< 50MB for 10k events)
        var memGrowthMb = (memAfter - memBefore) / (1024.0 * 1024.0);
        Assert.True(memGrowthMb < 50,
            $"Memory grew by {memGrowthMb:F1}MB — expected < 50MB for 10k events");

        // Throughput should be reasonable (> 1000 events/sec even on slow CI)
        var eventsPerSec = 10000.0 / sw.Elapsed.TotalSeconds;
        Assert.True(eventsPerSec > 1000,
            $"Throughput was {eventsPerSec:F0} events/sec — expected > 1000");
    }

    [Fact]
    public async Task AsyncMode10kEvents_CompletesSuccessfully()
    {
        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = _sink,
            SynchronousMode = false,
            Backpressure = new BackpressureOptions
            {
                Strategy = BackpressureStrategy.Block,
                ChannelCapacity = 1024
            }
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync();

        var toolExecutor = new RecordingToolExecutor(new PerfToolExecutor(), recorder, _serializer);

        for (int i = 0; i < 5000; i++)
        {
            var invocation = new ToolInvocation("perf-tool",
                JsonDocument.Parse($"{{\"i\":{i}}}").RootElement);
            await toolExecutor.ExecuteAsync(invocation, CancellationToken.None);
        }

        await recorder.StopAsync();

        Assert.Equal(10002, _sink.EventCount);
    }
}

internal sealed class PerfCountingSink : IFlightSink
{
    private int _count;
    public int EventCount => _count;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PerfToolExecutor : IToolExecutor
{
    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new ToolResult(
            JsonDocument.Parse("{\"ok\":true}").RootElement));
}
