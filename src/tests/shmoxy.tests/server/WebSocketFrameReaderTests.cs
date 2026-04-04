using System.Buffers.Binary;
using shmoxy.models;
using shmoxy.server;

namespace shmoxy.tests.server;

public class WebSocketFrameReaderTests
{
    [Fact]
    public async Task ReadFrame_ParsesSmallTextFrame()
    {
        // Arrange: FIN=1, opcode=Text(1), unmasked, payload="Hello"
        var payload = "Hello"u8.ToArray();
        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0x81; // FIN | Text
        frameBytes[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.False(frame.Rsv1);
        Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
        Assert.False(frame.IsMasked);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_ParsesExtended16BitPayload()
    {
        // Arrange: FIN=1, opcode=Binary(2), unmasked, payload of 300 bytes
        var payload = new byte[300];
        Random.Shared.NextBytes(payload);

        var frameBytes = new byte[2 + 2 + payload.Length]; // header + ext length + payload
        frameBytes[0] = 0x82; // FIN | Binary
        frameBytes[1] = 126;  // extended 16-bit length marker
        BinaryPrimitives.WriteUInt16BigEndian(frameBytes.AsSpan(2), (ushort)payload.Length);
        Array.Copy(payload, 0, frameBytes, 4, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
        Assert.Equal(payload.Length, frame.Payload.Length);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_ParsesExtended64BitPayload()
    {
        // Arrange: FIN=1, opcode=Binary(2), unmasked, payload of 70000 bytes (> 65535)
        var payload = new byte[70_000];
        Random.Shared.NextBytes(payload);

        var frameBytes = new byte[2 + 8 + payload.Length]; // header + ext length + payload
        frameBytes[0] = 0x82; // FIN | Binary
        frameBytes[1] = 127;  // extended 64-bit length marker
        BinaryPrimitives.WriteUInt64BigEndian(frameBytes.AsSpan(2), (ulong)payload.Length);
        Array.Copy(payload, 0, frameBytes, 10, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
        Assert.Equal(payload.Length, frame.Payload.Length);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_UnmasksMaskedFrame()
    {
        // Arrange: FIN=1, opcode=Text(1), masked, payload="Hello"
        var unmaskedPayload = "Hello"u8.ToArray();
        byte[] maskKey = [0x37, 0xFA, 0x21, 0x3D];

        var maskedPayload = new byte[unmaskedPayload.Length];
        for (int i = 0; i < unmaskedPayload.Length; i++)
        {
            maskedPayload[i] = (byte)(unmaskedPayload[i] ^ maskKey[i % 4]);
        }

        var frameBytes = new byte[2 + 4 + maskedPayload.Length]; // header + mask key + payload
        frameBytes[0] = 0x81; // FIN | Text
        frameBytes[1] = (byte)(0x80 | maskedPayload.Length); // mask bit set | length
        Array.Copy(maskKey, 0, frameBytes, 2, 4);
        Array.Copy(maskedPayload, 0, frameBytes, 6, maskedPayload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
        Assert.True(frame.IsMasked);
        Assert.Equal(unmaskedPayload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_ParsesUnmaskedFrame()
    {
        // Arrange: FIN=1, opcode=Binary(2), unmasked, payload of 10 bytes
        var payload = new byte[10];
        Random.Shared.NextBytes(payload);

        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0x82; // FIN | Binary
        frameBytes[1] = (byte)payload.Length; // no mask bit
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.False(frame.IsMasked);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_ParsesCloseFrame()
    {
        // Arrange: FIN=1, opcode=Close(8), unmasked, with status code 1000 + reason "bye"
        var reason = "bye"u8.ToArray();
        var payload = new byte[2 + reason.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload, 1000); // status code
        Array.Copy(reason, 0, payload, 2, reason.Length);

        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0x88; // FIN | Close
        frameBytes[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.Equal(WebSocketOpcode.Close, frame.Opcode);
        Assert.Equal(payload.Length, frame.Payload.Length);

        // Verify the status code in the payload
        var statusCode = BinaryPrimitives.ReadUInt16BigEndian(frame.Payload);
        Assert.Equal((ushort)1000, statusCode);

        // Verify the reason text
        var reasonText = System.Text.Encoding.UTF8.GetString(frame.Payload, 2, frame.Payload.Length - 2);
        Assert.Equal("bye", reasonText);
    }

    [Fact]
    public async Task ReadFrame_ParsesPingFrame()
    {
        // Arrange: FIN=1, opcode=Ping(9), unmasked, with payload "ping"
        var payload = "ping"u8.ToArray();
        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0x89; // FIN | Ping
        frameBytes[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.Equal(WebSocketOpcode.Ping, frame.Opcode);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_ReturnsNull_OnEmptyStream()
    {
        // Arrange: empty stream
        using var stream = new MemoryStream([]);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.Null(frame);
    }

    [Fact]
    public async Task WriteFrame_RoundTrips()
    {
        // Arrange: write a frame then read it back
        var original = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = "Hello, WebSocket!"u8.ToArray(),
            IsMasked = false
        };

        using var stream = new MemoryStream();

        // Act: write
        await WebSocketFrameReader.WriteFrameAsync(stream, original, CancellationToken.None);

        // Reset stream position to read back
        stream.Position = 0;

        // Act: read
        var roundTripped = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.Equal(original.Fin, roundTripped.Fin);
        Assert.Equal(original.Opcode, roundTripped.Opcode);
        Assert.Equal(original.Payload, roundTripped.Payload);
        Assert.False(roundTripped.IsMasked);
    }

    [Fact]
    public async Task WriteFrame_WithMask_SetsMaskBitAndMaskKey()
    {
        var payload = "Hello"u8.ToArray();
        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = payload,
            IsMasked = false
        };

        using var stream = new MemoryStream();
        await WebSocketFrameReader.WriteFrameAsync(stream, frame, CancellationToken.None, mask: true);

        var written = stream.ToArray();
        // Second byte should have mask bit set (0x80)
        Assert.True((written[1] & 0x80) != 0, "Mask bit should be set");
        // Total length: 2 (header) + 4 (mask key) + payload
        Assert.Equal(2 + 4 + payload.Length, written.Length);
    }

    [Fact]
    public async Task WriteFrame_WithMask_RoundTrips()
    {
        var originalPayload = "Masked WebSocket frame!"u8.ToArray();
        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = originalPayload,
            IsMasked = false
        };

        using var stream = new MemoryStream();
        await WebSocketFrameReader.WriteFrameAsync(stream, frame, CancellationToken.None, mask: true);

        stream.Position = 0;
        var roundTripped = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.IsMasked);
        Assert.Equal(originalPayload, roundTripped.Payload);
    }

    [Fact]
    public async Task WriteFrame_WithoutMask_DoesNotSetMaskBit()
    {
        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = "test"u8.ToArray(),
            IsMasked = false
        };

        using var stream = new MemoryStream();
        await WebSocketFrameReader.WriteFrameAsync(stream, frame, CancellationToken.None, mask: false);

        var written = stream.ToArray();
        Assert.True((written[1] & 0x80) == 0, "Mask bit should NOT be set");
    }

    [Fact]
    public async Task WriteFrame_UsesExtendedLength_ForLargePayloads()
    {
        // Arrange: payload of 300 bytes should use 16-bit extended length
        var payload = new byte[300];
        Random.Shared.NextBytes(payload);

        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Binary,
            Payload = payload,
            IsMasked = false
        };

        using var stream = new MemoryStream();

        // Act: write
        await WebSocketFrameReader.WriteFrameAsync(stream, frame, CancellationToken.None);

        // Assert: verify raw bytes use extended 16-bit length encoding
        var written = stream.ToArray();
        Assert.Equal(0x82, written[0]); // FIN | Binary
        Assert.Equal(126, written[1]);  // 16-bit extended length marker
        var encodedLength = BinaryPrimitives.ReadUInt16BigEndian(written.AsSpan(2));
        Assert.Equal((ushort)300, encodedLength);
        Assert.Equal(2 + 2 + payload.Length, written.Length); // header + ext len + payload

        // Also verify round-trip
        stream.Position = 0;
        var roundTripped = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);
        Assert.NotNull(roundTripped);
        Assert.Equal(payload, roundTripped.Payload);
    }

    [Fact]
    public async Task ReadFrame_ParsesFrameWithRsv1Bit()
    {
        // Arrange: FIN=1, RSV1=1, opcode=Text(1), unmasked, payload="compressed"
        var payload = "compressed"u8.ToArray();
        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0xC1; // FIN(0x80) | RSV1(0x40) | Text(0x01)
        frameBytes[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Fin);
        Assert.True(frame.Rsv1);
        Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task ReadFrame_NoRsv1_DefaultsFalse()
    {
        // Arrange: FIN=1, RSV1=0, opcode=Text(1) — standard non-compressed frame
        var payload = "hello"u8.ToArray();
        var frameBytes = new byte[2 + payload.Length];
        frameBytes[0] = 0x81; // FIN | Text (no RSV1)
        frameBytes[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frameBytes, 2, payload.Length);

        using var stream = new MemoryStream(frameBytes);

        // Act
        var frame = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.NotNull(frame);
        Assert.False(frame.Rsv1);
    }

    [Fact]
    public async Task WriteFrame_WithRsv1_PreservesBit()
    {
        // Arrange: frame with RSV1 set
        var original = new WebSocketFrame
        {
            Fin = true,
            Rsv1 = true,
            Opcode = WebSocketOpcode.Text,
            Payload = "test data"u8.ToArray(),
            IsMasked = false
        };

        using var stream = new MemoryStream();

        // Act: write then read back
        await WebSocketFrameReader.WriteFrameAsync(stream, original, CancellationToken.None);
        stream.Position = 0;
        var roundTripped = await WebSocketFrameReader.ReadFrameAsync(stream, CancellationToken.None);

        // Assert: RSV1 is preserved through round-trip
        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.Rsv1);
        Assert.True(roundTripped.Fin);
        Assert.Equal(WebSocketOpcode.Text, roundTripped.Opcode);
        Assert.Equal(original.Payload, roundTripped.Payload);

        // Also verify raw first byte has RSV1 bit set
        stream.Position = 0;
        var firstByte = stream.ReadByte();
        Assert.Equal(0xC1, firstByte); // FIN(0x80) | RSV1(0x40) | Text(0x01)
    }
}
