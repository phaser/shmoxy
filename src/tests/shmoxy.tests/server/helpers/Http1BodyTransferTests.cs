using shmoxy.server.helpers;

namespace shmoxy.tests.server.helpers;

public class Http1BodyTransferTests
{
    [Fact]
    public async Task CopyFixedLengthAsync_LargeGeneratedBody_KeepsMemoryBounded()
    {
        const long bodyLength = 32L * 1024 * 1024;
        const int captureLimit = 64 * 1024;
        await using var source = new GeneratedReadStream(bodyLength);
        await using var destination = new CountingWriteStream();
        using var capture = new BodyPreviewCapture(captureLimit);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await Http1BodyTransfer.CopyFixedLengthAsync(
            source,
            destination,
            bodyLength,
            ReadOnlyMemory<byte>.Empty,
            capture,
            cts,
            timeoutMs: 5000);

        Assert.True(result.Completed);
        Assert.Equal(bodyLength, result.PayloadBytes);
        Assert.Equal(bodyLength, destination.BytesWritten);
        Assert.Equal(bodyLength, capture.TotalBytes);
        Assert.Equal(captureLimit, capture.CapturedLength);
        Assert.True(capture.IsTruncated);
        Assert.InRange(source.MaximumRequestedReadSize, 1, 8192);
    }

    [Fact]
    public async Task CopyChunkedAsync_ForwardsTrailersAndCapturesPayloadOnly()
    {
        var chunked = "4\r\ntest\r\n3;ext=1\r\ning\r\n0\r\nX-Trailer: value\r\n\r\n"u8.ToArray();
        await using var source = new MemoryStream(chunked);
        await using var destination = new MemoryStream();
        using var capture = new BodyPreviewCapture(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var prefix = new Http1BodyTransfer.ChunkedPrefix(
            Array.Empty<byte>(),
            Array.Empty<byte>(),
            0,
            Completed: false,
            PendingChunkBytes: 0);

        var result = await Http1BodyTransfer.CopyChunkedAsync(
            source,
            destination,
            prefix,
            capture,
            cts,
            timeoutMs: 5000);

        Assert.True(result.Completed);
        Assert.Equal(7, result.PayloadBytes);
        Assert.Equal(chunked, destination.ToArray());
        Assert.Equal("testi"u8.ToArray(), capture.ToArray());
        Assert.True(capture.IsTruncated);
    }

    private sealed class GeneratedReadStream : Stream
    {
        private readonly long _length;
        private long _remaining;

        public GeneratedReadStream(long length)
        {
            _length = length;
            _remaining = length;
        }

        public int MaximumRequestedReadSize { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaximumRequestedReadSize = Math.Max(MaximumRequestedReadSize, buffer.Length);
            var bytesToReturn = checked((int)Math.Min(_remaining, buffer.Length));
            if (bytesToReturn == 0)
                return ValueTask.FromResult(0);

            buffer.Span[..bytesToReturn].Fill((byte)'x');
            _remaining -= bytesToReturn;
            return ValueTask.FromResult(bytesToReturn);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _length - _remaining;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingWriteStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            BytesWritten += count;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
