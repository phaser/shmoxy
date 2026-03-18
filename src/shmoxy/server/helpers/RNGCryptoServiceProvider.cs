using System.Security.Cryptography;

namespace shmoxy.server.helpers;

/// <summary>
/// Simple wrapper for random bytes generation.
/// </summary>
internal static class RNGCryptoServiceProvider
{
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    public static byte[] GetRandomBytes(int count)
    {
        var buffer = new byte[count];
        _rng.GetBytes(buffer);
        return buffer;
    }
}
