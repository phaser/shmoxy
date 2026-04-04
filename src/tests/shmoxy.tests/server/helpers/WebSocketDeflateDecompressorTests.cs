using System.IO.Compression;
using System.Text;
using shmoxy.server.helpers;

namespace shmoxy.tests.server.helpers;

public class WebSocketDeflateDecompressorTests
{
    [Fact]
    public void IsDeflateNegotiated_WithExtensionHeader_ReturnsTrue()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Upgrade", "websocket"),
            new("Sec-WebSocket-Extensions", "permessage-deflate; server_no_context_takeover")
        };

        Assert.True(WebSocketDeflateDecompressor.IsDeflateNegotiated(headers));
    }

    [Fact]
    public void IsDeflateNegotiated_CaseInsensitive_ReturnsTrue()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("sec-websocket-extensions", "Permessage-Deflate")
        };

        Assert.True(WebSocketDeflateDecompressor.IsDeflateNegotiated(headers));
    }

    [Fact]
    public void IsDeflateNegotiated_WithoutExtensionHeader_ReturnsFalse()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Upgrade", "websocket"),
            new("Connection", "Upgrade")
        };

        Assert.False(WebSocketDeflateDecompressor.IsDeflateNegotiated(headers));
    }

    [Fact]
    public void IsDeflateNegotiated_WithOtherExtension_ReturnsFalse()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Sec-WebSocket-Extensions", "x-webkit-deflate-frame")
        };

        Assert.False(WebSocketDeflateDecompressor.IsDeflateNegotiated(headers));
    }

    [Fact]
    public void TryDecompress_ValidCompressedData_ReturnsDecompressed()
    {
        const string original = "Hello, WebSocket!";
        var compressed = CompressForTest(original);

        var decompressed = WebSocketDeflateDecompressor.TryDecompress(compressed);

        Assert.NotNull(decompressed);
        Assert.Equal(original, Encoding.UTF8.GetString(decompressed));
    }

    [Fact]
    public void TryDecompress_EmptyPayload_ReturnsEmpty()
    {
        var result = WebSocketDeflateDecompressor.TryDecompress([]);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void TryDecompress_InvalidData_ReturnsNull()
    {
        // Garbage bytes that are not valid deflate data
        var garbage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 };

        var result = WebSocketDeflateDecompressor.TryDecompress(garbage);

        Assert.Null(result);
    }

    [Fact]
    public void TryDecompress_LargerMessage_RoundTrips()
    {
        // Test with a larger JSON-like message typical of WebSocket traffic
        const string original = """{"type":"update","data":{"id":12345,"values":[1,2,3,4,5],"nested":{"key":"value"}}}""";
        var compressed = CompressForTest(original);

        var decompressed = WebSocketDeflateDecompressor.TryDecompress(compressed);

        Assert.NotNull(decompressed);
        Assert.Equal(original, Encoding.UTF8.GetString(decompressed));
    }

    /// <summary>
    /// Compresses text using RFC 7692 format: raw deflate with the trailing
    /// sync flush marker (0x00 0x00 0xFF 0xFF) stripped, matching what a
    /// WebSocket peer sends on the wire.
    /// </summary>
    private static byte[] CompressForTest(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        var compressed = ms.ToArray();

        // RFC 7692: strip trailing 0x00 0x00 0xFF 0xFF if present
        if (compressed.Length >= 4 &&
            compressed[^4] == 0x00 && compressed[^3] == 0x00 &&
            compressed[^2] == 0xFF && compressed[^1] == 0xFF)
        {
            return compressed[..^4];
        }

        return compressed;
    }
}
