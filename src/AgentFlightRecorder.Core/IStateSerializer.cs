using System.Text.Json;

namespace AgentFlightRecorder.Core;

/// <summary>
/// User-provided serializer for state checkpoint objects.
/// </summary>
public interface IStateSerializer
{
    JsonElement Serialize(object state);
}
