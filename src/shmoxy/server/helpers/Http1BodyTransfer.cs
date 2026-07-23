using System.Globalization;
using System.Text;

namespace shmoxy.server.helpers;

/// <summary>
/// Streams HTTP/1.1 message bodies while preserving wire framing and retaining
/// only a bounded payload preview.
/// </summary>
internal static class Http1BodyTransfer
{
    private const int BufferSize = 8192;
    private const int MaxChunkLineSize = 8192;
    private const int MaxBufferedChunkMetadataBytes = 65536;

    internal sealed record TransferResult(long PayloadBytes, bool Completed);

    internal sealed record ChunkedPrefix(
        byte[] RawBytes,
        byte[] Preview,
        long PayloadBytes,
        bool Completed,
        long PendingChunkBytes);

    public static async Task<byte[]> ReadPrefixAsync(
        Stream source,
        long bodyLength,
        int captureLimit,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        var bytesToRead = checked((int)Math.Min(bodyLength, captureLimit));
        if (bytesToRead == 0)
            return Array.Empty<byte>();

        var prefix = new byte[bytesToRead];
        var offset = 0;
        while (offset < prefix.Length)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var read = await source.ReadAsync(
                prefix.AsMemory(offset, prefix.Length - offset),
                timeoutCts.Token);
            if (read == 0)
                break;

            offset += read;
        }

        return offset == prefix.Length ? prefix : prefix[..offset];
    }

    public static async Task<ChunkedPrefix> ReadChunkedPrefixAsync(
        Stream source,
        int captureLimit,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        if (captureLimit == 0)
        {
            return new ChunkedPrefix(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                0,
                Completed: false,
                PendingChunkBytes: 0);
        }

        var rawCapacity = checked((int)Math.Min((long)captureLimit + 1024, 65536));
        using var raw = new MemoryStream(rawCapacity);
        using var preview = new MemoryStream(Math.Min(captureLimit, 8192));
        var metadataBytes = 0;
        long payloadBytes = 0;

        while (preview.Length < captureLimit && metadataBytes < MaxBufferedChunkMetadataBytes)
        {
            var sizeLine = await ReadLineAsync(source, timeoutCts, timeoutMs);
            if (sizeLine == null)
            {
                return new ChunkedPrefix(
                    raw.ToArray(),
                    preview.ToArray(),
                    payloadBytes,
                    Completed: false,
                    PendingChunkBytes: 0);
            }

            raw.Write(sizeLine);
            metadataBytes += sizeLine.Length;
            var chunkSize = ParseChunkSize(sizeLine);

            if (chunkSize == 0)
            {
                while (true)
                {
                    var trailerLine = await ReadLineAsync(source, timeoutCts, timeoutMs);
                    if (trailerLine == null)
                    {
                        return new ChunkedPrefix(
                            raw.ToArray(),
                            preview.ToArray(),
                            payloadBytes,
                            Completed: false,
                            PendingChunkBytes: 0);
                    }

                    if (metadataBytes + trailerLine.Length > MaxBufferedChunkMetadataBytes)
                    {
                        throw new InvalidDataException(
                            $"Chunk metadata exceeded the {MaxBufferedChunkMetadataBytes}-byte limit.");
                    }

                    raw.Write(trailerLine);
                    metadataBytes += trailerLine.Length;
                    if (trailerLine.Length == 2)
                    {
                        return new ChunkedPrefix(
                            raw.ToArray(),
                            preview.ToArray(),
                            payloadBytes,
                            Completed: true,
                            PendingChunkBytes: 0);
                    }
                }
            }

            var captureRemaining = captureLimit - checked((int)preview.Length);
            var chunkBytesToRead = Math.Min(chunkSize, captureRemaining);
            var chunkBytesRead = await ReadIntoBuffersAsync(
                source,
                raw,
                preview,
                chunkBytesToRead,
                timeoutCts,
                timeoutMs);
            payloadBytes = checked(payloadBytes + chunkBytesRead);

            if (chunkBytesRead < chunkBytesToRead)
            {
                return new ChunkedPrefix(
                    raw.ToArray(),
                    preview.ToArray(),
                    payloadBytes,
                    Completed: false,
                    PendingChunkBytes: chunkSize - chunkBytesRead);
            }

            if (chunkBytesToRead < chunkSize)
            {
                return new ChunkedPrefix(
                    raw.ToArray(),
                    preview.ToArray(),
                    payloadBytes,
                    Completed: false,
                    PendingChunkBytes: chunkSize - chunkBytesRead);
            }

            var chunkTerminator = await ReadExactBytesAsync(source, 2, timeoutCts, timeoutMs);
            raw.Write(chunkTerminator);
            metadataBytes += chunkTerminator.Length;
            if (chunkTerminator.Length != 2 ||
                chunkTerminator[0] != '\r' ||
                chunkTerminator[1] != '\n')
            {
                throw new InvalidDataException("Chunk payload was not followed by CRLF.");
            }
        }

        return new ChunkedPrefix(
            raw.ToArray(),
            preview.ToArray(),
            payloadBytes,
            Completed: false,
            PendingChunkBytes: 0);
    }

    public static async Task<TransferResult> CopyFixedLengthAsync(
        Stream source,
        Stream destination,
        long bodyLength,
        ReadOnlyMemory<byte> prefix,
        BodyPreviewCapture capture,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        if (!prefix.IsEmpty)
            await destination.WriteAsync(prefix, timeoutCts.Token);

        long transferred = prefix.Length;
        var remaining = bodyLength - prefix.Length;
        var buffer = new byte[BufferSize];

        while (remaining > 0)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var bytesToRead = checked((int)Math.Min(remaining, buffer.Length));
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(0, bytesToRead), timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return new TransferResult(transferred, Completed: false);
            }
            catch (IOException)
            {
                return new TransferResult(transferred, Completed: false);
            }

            if (read == 0)
                return new TransferResult(transferred, Completed: false);

            await destination.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
            capture.Append(buffer.AsSpan(0, read));
            transferred = checked(transferred + read);
            remaining -= read;
        }

        return new TransferResult(transferred, Completed: true);
    }

    public static async Task<TransferResult> CopyChunkedAsync(
        Stream source,
        Stream destination,
        ChunkedPrefix prefix,
        BodyPreviewCapture capture,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        if (prefix.RawBytes.Length > 0)
            await destination.WriteAsync(prefix.RawBytes, timeoutCts.Token);

        long payloadBytes = prefix.PayloadBytes;
        if (prefix.Completed)
            return new TransferResult(payloadBytes, Completed: true);

        if (prefix.PendingChunkBytes > 0)
        {
            var pendingResult = await CopyPayloadBytesAsync(
                source,
                destination,
                prefix.PendingChunkBytes,
                capture,
                timeoutCts,
                timeoutMs);
            payloadBytes = checked(payloadBytes + pendingResult.PayloadBytes);
            if (!pendingResult.Completed)
                return new TransferResult(payloadBytes, Completed: false);

            var chunkTerminatorCompleted = await CopyChunkTerminatorAsync(
                source,
                destination,
                timeoutCts,
                timeoutMs);
            if (!chunkTerminatorCompleted)
                return new TransferResult(payloadBytes, Completed: false);
        }

        while (true)
        {
            var sizeLine = await ReadLineAsync(source, timeoutCts, timeoutMs);
            if (sizeLine == null)
                return new TransferResult(payloadBytes, Completed: false);

            await destination.WriteAsync(sizeLine, timeoutCts.Token);
            var chunkSize = ParseChunkSize(sizeLine);
            if (chunkSize == 0)
            {
                while (true)
                {
                    var trailerLine = await ReadLineAsync(source, timeoutCts, timeoutMs);
                    if (trailerLine == null)
                        return new TransferResult(payloadBytes, Completed: false);

                    await destination.WriteAsync(trailerLine, timeoutCts.Token);
                    if (trailerLine.Length == 2)
                        return new TransferResult(payloadBytes, Completed: true);
                }
            }

            var chunkResult = await CopyPayloadBytesAsync(
                source,
                destination,
                chunkSize,
                capture,
                timeoutCts,
                timeoutMs);
            payloadBytes = checked(payloadBytes + chunkResult.PayloadBytes);
            if (!chunkResult.Completed)
                return new TransferResult(payloadBytes, Completed: false);

            var terminatorCompleted = await CopyChunkTerminatorAsync(
                source,
                destination,
                timeoutCts,
                timeoutMs);
            if (!terminatorCompleted)
                return new TransferResult(payloadBytes, Completed: false);
        }
    }

    public static async Task<TransferResult> CopyUntilEofAsync(
        Stream source,
        Stream destination,
        BodyPreviewCapture capture,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        long transferred = 0;
        var buffer = new byte[BufferSize];

        while (true)
        {
            timeoutCts.CancelAfter(timeoutMs);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return new TransferResult(transferred, Completed: false);
            }
            catch (IOException)
            {
                return new TransferResult(transferred, Completed: true);
            }

            if (read == 0)
                return new TransferResult(transferred, Completed: true);

            await destination.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
            capture.Append(buffer.AsSpan(0, read));
            transferred = checked(transferred + read);
        }
    }

    private static async Task<TransferResult> CopyPayloadBytesAsync(
        Stream source,
        Stream destination,
        long byteCount,
        BodyPreviewCapture capture,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        long transferred = 0;
        var buffer = new byte[BufferSize];

        while (transferred < byteCount)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var bytesToRead = checked((int)Math.Min(byteCount - transferred, buffer.Length));
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(0, bytesToRead), timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return new TransferResult(transferred, Completed: false);
            }
            catch (IOException)
            {
                return new TransferResult(transferred, Completed: false);
            }

            if (read == 0)
                return new TransferResult(transferred, Completed: false);

            await destination.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
            capture.Append(buffer.AsSpan(0, read));
            transferred = checked(transferred + read);
        }

        return new TransferResult(transferred, Completed: true);
    }

    private static async Task<bool> CopyChunkTerminatorAsync(
        Stream source,
        Stream destination,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        var bytes = await ReadExactBytesAsync(source, 2, timeoutCts, timeoutMs);
        if (bytes.Length > 0)
            await destination.WriteAsync(bytes, timeoutCts.Token);

        if (bytes.Length != 2)
            return false;

        if (bytes[0] != '\r' || bytes[1] != '\n')
            throw new InvalidDataException("Chunk payload was not followed by CRLF.");

        return true;
    }

    private static async Task<long> ReadIntoBuffersAsync(
        Stream source,
        MemoryStream raw,
        MemoryStream preview,
        long byteCount,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        long transferred = 0;
        var buffer = new byte[BufferSize];

        while (transferred < byteCount)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var bytesToRead = checked((int)Math.Min(byteCount - transferred, buffer.Length));
            var read = await source.ReadAsync(buffer.AsMemory(0, bytesToRead), timeoutCts.Token);
            if (read == 0)
                break;

            raw.Write(buffer, 0, read);
            preview.Write(buffer, 0, read);
            transferred = checked(transferred + read);
        }

        return transferred;
    }

    private static async Task<byte[]?> ReadLineAsync(
        Stream source,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        using var line = new MemoryStream(128);
        var previousWasCarriageReturn = false;
        var singleByte = new byte[1];

        while (line.Length < MaxChunkLineSize)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var read = await source.ReadAsync(singleByte, timeoutCts.Token);
            if (read == 0)
                return null;

            var value = singleByte[0];
            line.WriteByte(value);
            if (previousWasCarriageReturn && value == '\n')
                return line.ToArray();

            previousWasCarriageReturn = value == '\r';
        }

        throw new InvalidDataException(
            $"Chunk metadata line exceeded the {MaxChunkLineSize}-byte limit.");
    }

    private static long ParseChunkSize(byte[] sizeLine)
    {
        var value = Encoding.Latin1.GetString(sizeLine, 0, sizeLine.Length - 2);
        var extensionIndex = value.IndexOf(';');
        if (extensionIndex >= 0)
            value = value[..extensionIndex];

        if (!long.TryParse(
                value.Trim(),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var chunkSize) ||
            chunkSize < 0)
        {
            throw new InvalidDataException($"Invalid HTTP chunk size '{value}'.");
        }

        return chunkSize;
    }

    private static async Task<byte[]> ReadExactBytesAsync(
        Stream source,
        int byteCount,
        CancellationTokenSource timeoutCts,
        int timeoutMs)
    {
        var bytes = new byte[byteCount];
        var offset = 0;
        while (offset < byteCount)
        {
            timeoutCts.CancelAfter(timeoutMs);
            var read = await source.ReadAsync(bytes.AsMemory(offset), timeoutCts.Token);
            if (read == 0)
                break;

            offset += read;
        }

        return offset == byteCount ? bytes : bytes[..offset];
    }
}
