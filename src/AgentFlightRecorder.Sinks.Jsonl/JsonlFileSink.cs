using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Serialization;

namespace AgentFlightRecorder.Sinks.Jsonl;

/// <summary>
/// Writes flight events as one JSON object per line to a JSONL file.
/// </summary>
public sealed class JsonlFileSink : IFlightSink, IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly ICanonicalJsonSerializer _serializer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public JsonlFileSink(string filePath, ICanonicalJsonSerializer serializer, JsonlSinkOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        var encoding = options?.Encoding ?? System.Text.Encoding.UTF8;
        _writer = new StreamWriter(stream, encoding) { AutoFlush = false };
    }

    public async ValueTask WriteAsync(FlightEvent evt, CancellationToken ct = default)
    {
        var json = _serializer.Serialize(evt);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask FlushAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await FlushAsync();
        await _writer.DisposeAsync();
        _writeLock.Dispose();
    }
}
