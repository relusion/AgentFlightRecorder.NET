using System.Text;

namespace AgentFlightRecorder.Core.Options;

/// <summary>
/// Configuration options for the JSONL file sink.
/// </summary>
public sealed record JsonlSinkOptions
{
    /// <summary>
    /// Text encoding for the JSONL file. Defaults to UTF-8.
    /// </summary>
    public Encoding Encoding { get; init; } = Encoding.UTF8;
}
