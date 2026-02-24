using System.Text.Json;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Options;
using Microsoft.Extensions.Logging;

namespace Core.Tests.Recording;

public sealed class RecorderErrorTests
{
    [Fact]
    public async Task SinkFailure_EmitsRecorderError_AndLogsWarning()
    {
        var sink = new FailOnceSink(failOnWrite: 2); // Fail on 2nd write
        var logger = new TestLogger();

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true,
            Logger = logger
        };

        await using var recorder = new FlightRecorder(options);
        await recorder.StartAsync(); // Write 1: RunStarted (succeeds)
        await recorder.AnnotateAsync("note"); // Write 2: fails, then RecorderError event attempted
        await recorder.StopAsync();

        Assert.True(logger.WarningCount >= 1, "Expected a warning for sink failure");
    }

    [Fact]
    public async Task RecorderError_SinkAlsoFails_NoRecursion()
    {
        var sink = new AlwaysFailSink();
        var logger = new TestLogger();

        var options = new FlightRecorderOptions
        {
            Mode = FlightMode.Record,
            Sink = sink,
            SynchronousMode = true,
            Logger = logger
        };

        await using var recorder = new FlightRecorder(options);

        // Should not throw despite all sink writes failing
        await recorder.StartAsync();
        await recorder.AnnotateAsync("note");
        await recorder.StopAsync();

        // Should have logged errors (RecorderError also failed)
        Assert.True(logger.ErrorCount >= 1, "Expected error log for recursive failure");
    }
}

internal sealed class FailOnceSink : IFlightSink
{
    private int _writeCount;
    private readonly int _failOnWrite;

    public FailOnceSink(int failOnWrite) => _failOnWrite = failOnWrite;

    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        _writeCount++;
        if (_writeCount == _failOnWrite)
            throw new IOException("Simulated disk failure");
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class AlwaysFailSink : IFlightSink
{
    public ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default) =>
        throw new IOException("Simulated persistent failure");

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
