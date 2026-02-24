namespace AgentFlightRecorder.Core;

/// <summary>
/// Provides deterministic JSON serialization with stable property ordering.
/// </summary>
public interface ICanonicalJsonSerializer
{
    byte[] SerializeToUtf8Bytes<T>(T value);
    string Serialize<T>(T value);
    T? Deserialize<T>(ReadOnlySpan<byte> utf8Json);
    T? Deserialize<T>(string json);
}
