using System.Security.Cryptography;

namespace shmoxy.server.helpers;

/// <summary>
/// Simple wrapper for random bytes generation.
/// </summary>
// ReSharper disable once InconsistentNaming
internal static class RNGCryptoServiceProvider
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static byte[] GetRandomBytes(int count)
    {
        var buffer = new byte[count];
        Rng.GetBytes(buffer);
        return buffer;
    }
}
