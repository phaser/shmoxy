using shmoxy.server.helpers;

namespace shmoxy.tests.server.helpers;

public class BufferedReadStreamTests
{
    [Fact]
    public async Task ReadHeadersAsync_PreservesBodyBytesReadInSameSegment()
    {
        var message = "POST / HTTP/1.1\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        await using var inner = new MemoryStream(message);
        await using var stream = new BufferedReadStream(inner, leaveOpen: false);

        var headers = await stream.ReadHeadersAsync(65536, CancellationToken.None);
        var body = new byte[5];
        var bodyRead = await stream.ReadAsync(body);

        Assert.Equal(
            "POST / HTTP/1.1\r\nContent-Length: 5\r\n\r\n",
            System.Text.Encoding.Latin1.GetString(headers!));
        Assert.Equal(5, bodyRead);
        Assert.Equal("hello"u8.ToArray(), body);
    }

    [Fact]
    public async Task ReadHeadersAsync_RejectsHeadersAboveLimit()
    {
        var message = System.Text.Encoding.Latin1.GetBytes(
            $"GET / HTTP/1.1\r\nX-Large: {new string('x', 100)}\r\n\r\n");
        await using var inner = new MemoryStream(message);
        await using var stream = new BufferedReadStream(inner, leaveOpen: false);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => stream.ReadHeadersAsync(32, CancellationToken.None));

        Assert.Contains("32-byte", exception.Message);
    }
}
