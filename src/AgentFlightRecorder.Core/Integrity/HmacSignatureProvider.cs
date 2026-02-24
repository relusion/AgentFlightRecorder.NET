using System.Security.Cryptography;
using System.Text;

namespace AgentFlightRecorder.Core.Integrity;

/// <summary>
/// Computes and verifies HMAC-SHA256 run signatures.
/// </summary>
public sealed class HmacSignatureProvider
{
    public static string ComputeSignature(string finalChainHash, byte[] signingKey)
    {
        var hashBytes = Encoding.UTF8.GetBytes(finalChainHash);
        var hmac = HMACSHA256.HashData(signingKey, hashBytes);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    public static bool VerifySignature(string finalChainHash, string signature, byte[] signingKey)
    {
        var expected = ComputeSignature(finalChainHash, signingKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
