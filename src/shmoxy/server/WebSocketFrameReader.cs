using System.Buffers.Binary;
using shmoxy.models;

namespace shmoxy.server;

/// <summary>
/// Reads and writes WebSocket frames per RFC 6455.
/// </summary>
public static class WebSocketFrameReader
{
    /// <summary>
    /// Reads a single WebSocket frame from the stream.
    /// Returns null if the stream has ended (zero bytes read).
    /// </summary>
    public static async Task<WebSocketFrame?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[2];
        var bytesRead = await ReadExactAsync(stream, header, ct);
        if (bytesRead == 0)
            return null;

        if (bytesRead < 2)
            throw new InvalidOperationException("Unexpected end of stream reading frame header");

        bool fin = (header[0] & 0x80) != 0;
        var opcode = (WebSocketOpcode)(header[0] & 0x0F);
        bool masked = (header[1] & 0x80) != 0;
        ulong payloadLength = (ulong)(header[1] & 0x7F);

        if (payloadLength == 126)
        {
            var extLen = new byte[2];
            await ReadExactRequiredAsync(stream, extLen, ct);
            payloadLength = BinaryPrimitives.ReadUInt16BigEndian(extLen);
        }
        else if (payloadLength == 127)
        {
            var extLen = new byte[8];
            await ReadExactRequiredAsync(stream, extLen, ct);
            payloadLength = BinaryPrimitives.ReadUInt64BigEndian(extLen);
        }

        byte[]? maskKey = null;
        if (masked)
        {
            maskKey = new byte[4];
            await ReadExactRequiredAsync(stream, maskKey, ct);
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await ReadExactRequiredAsync(stream, payload, ct);
        }

        if (masked && maskKey != null)
        {
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] ^= maskKey[i % 4];
            }
        }

        return new WebSocketFrame
        {
            Fin = fin,
            Opcode = opcode,
            Payload = payload,
            IsMasked = masked
        };
    }

    /// <summary>
    /// Writes a single WebSocket frame to the stream.
    /// When mask is true, generates a random mask key and masks the payload per RFC 6455 §5.3
    /// (required for client-to-server frames).
    /// </summary>
    public static async Task WriteFrameAsync(Stream stream, WebSocketFrame frame, CancellationToken ct, bool mask = false)
    {
        byte firstByte = (byte)((frame.Fin ? 0x80 : 0x00) | (byte)frame.Opcode);
        await stream.WriteAsync(new[] { firstByte }, ct);

        int length = frame.Payload.Length;

        if (length < 126)
        {
            byte lengthByte = (byte)length;
            if (mask) lengthByte |= 0x80;
            await stream.WriteAsync(new[] { lengthByte }, ct);
        }
        else if (length < 65536)
        {
            byte marker = mask ? (byte)(126 | 0x80) : (byte)126;
            await stream.WriteAsync(new[] { marker }, ct);
            var extLen = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(extLen, (ushort)length);
            await stream.WriteAsync(extLen, ct);
        }
        else
        {
            byte marker = mask ? (byte)(127 | 0x80) : (byte)127;
            await stream.WriteAsync(new[] { marker }, ct);
            var extLen = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(extLen, (ulong)length);
            await stream.WriteAsync(extLen, ct);
        }

        if (mask)
        {
            var maskKey = new byte[4];
            Random.Shared.NextBytes(maskKey);
            await stream.WriteAsync(maskKey, ct);

            if (frame.Payload.Length > 0)
            {
                var maskedPayload = new byte[frame.Payload.Length];
                for (int i = 0; i < frame.Payload.Length; i++)
                {
                    maskedPayload[i] = (byte)(frame.Payload[i] ^ maskKey[i % 4]);
                }
                await stream.WriteAsync(maskedPayload, ct);
            }
        }
        else if (frame.Payload.Length > 0)
        {
            await stream.WriteAsync(frame.Payload, ct);
        }
    }

    /// <summary>
    /// Reads exactly buffer.Length bytes from the stream.
    /// Returns the number of bytes read (0 means stream ended immediately).
    /// </summary>
    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
                return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Reads exactly buffer.Length bytes from the stream.
    /// Throws if the stream ends before the buffer is filled.
    /// </summary>
    private static async Task ReadExactRequiredAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = await ReadExactAsync(stream, buffer, ct);
        if (totalRead < buffer.Length)
            throw new InvalidOperationException(
                $"Unexpected end of stream: expected {buffer.Length} bytes but got {totalRead}");
    }
}
