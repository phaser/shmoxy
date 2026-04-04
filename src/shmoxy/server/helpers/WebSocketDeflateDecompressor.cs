using System.IO.Compression;

namespace shmoxy.server.helpers;

/// <summary>
/// Decompresses WebSocket frame payloads per RFC 7692 (permessage-deflate).
/// Uses per-message decompression with graceful fallback on failure.
/// </summary>
public static class WebSocketDeflateDecompressor
{
    /// <summary>
    /// RFC 7692 section 7.2.2: sync flush marker appended before decompression.
    /// </summary>
    private static readonly byte[] SyncFlushMarker = [0x00, 0x00, 0xFF, 0xFF];

    /// <summary>
    /// Checks if the server response negotiated permessage-deflate.
    /// </summary>
    public static bool IsDeflateNegotiated(List<KeyValuePair<string, string>> responseHeaders)
    {
        return responseHeaders.Any(h =>
            h.Key.Equals("Sec-WebSocket-Extensions", StringComparison.OrdinalIgnoreCase) &&
            h.Value.Contains("permessage-deflate", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Attempts to decompress a WebSocket frame payload per RFC 7692.
    /// Returns the decompressed bytes, or null if decompression fails.
    /// </summary>
    public static byte[]? TryDecompress(byte[] compressedPayload)
    {
        if (compressedPayload.Length == 0)
            return [];

        try
        {
            // RFC 7692 section 7.2.2: append 0x00 0x00 0xFF 0xFF before decompression
            var input = new byte[compressedPayload.Length + SyncFlushMarker.Length];
            Buffer.BlockCopy(compressedPayload, 0, input, 0, compressedPayload.Length);
            Buffer.BlockCopy(SyncFlushMarker, 0, input, compressedPayload.Length, SyncFlushMarker.Length);

            using var ms = new MemoryStream(input);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
