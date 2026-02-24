using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;

namespace AgentFlightRecorder.Core.Integrity;

/// <summary>
/// Computes and verifies SHA-256 hash chain integrity for flight events.
/// </summary>
public sealed class Sha256IntegrityProvider : IIntegrityProvider
{
    private const string Algorithm = "SHA-256";
    private readonly ICanonicalJsonSerializer _serializer;

    public Sha256IntegrityProvider(ICanonicalJsonSerializer serializer)
    {
        _serializer = serializer;
    }

    public FlightIntegrity Compute(FlightIntegrity? previous, FlightEvent evt)
    {
        var prevHash = previous?.Hash ?? string.Empty;

        var stripped = new FlightEvent
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
            Integrity = null
        };

        var canonicalBytes = _serializer.SerializeToUtf8Bytes(stripped);
        var prevHashBytes = Encoding.UTF8.GetBytes(prevHash);

        // Concatenate: previousHash + canonicalEventBytes
        var combined = new byte[prevHashBytes.Length + canonicalBytes.Length];
        prevHashBytes.CopyTo(combined, 0);
        canonicalBytes.CopyTo(combined, prevHashBytes.Length);

        var hash = SHA256.HashData(combined);
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();

        return new FlightIntegrity(prevHash, hashHex, Algorithm);
    }

    public bool Verify(FlightIntegrity? previous, FlightEvent evt, FlightIntegrity integrity)
    {
        var expected = Compute(previous, evt);
        return expected.Hash == integrity.Hash
            && expected.PrevHash == integrity.PrevHash
            && expected.Algorithm == integrity.Algorithm;
    }
}
