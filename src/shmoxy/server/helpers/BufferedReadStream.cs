namespace shmoxy.server.helpers;

/// <summary>
/// Read-through stream that retains bytes read beyond an HTTP header boundary.
/// This prevents body or pipelined bytes delivered in the same TCP segment from
/// being discarded.
/// </summary>
internal sealed class BufferedReadStream : Stream
{
    private const int BufferSize = 8192;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _bufferOffset;
    private int _bufferCount;

    public BufferedReadStream(Stream inner, bool leaveOpen)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _leaveOpen = leaveOpen;
    }

    public async Task<byte[]?> ReadHeadersAsync(int maxHeaderSize, CancellationToken cancellationToken)
    {
        if (maxHeaderSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHeaderSize));

        using var headers = new MemoryStream(Math.Min(maxHeaderSize, BufferSize));
        var terminatorBytesMatched = 0;

        while (headers.Length < maxHeaderSize)
        {
            if (_bufferCount == 0)
            {
                _bufferOffset = 0;
                _bufferCount = await _inner.ReadAsync(_buffer, cancellationToken);
                if (_bufferCount == 0)
                    return headers.Length == 0 ? null : headers.ToArray();
            }

            while (_bufferCount > 0 && headers.Length < maxHeaderSize)
            {
                var value = _buffer[_bufferOffset++];
                _bufferCount--;
                headers.WriteByte(value);

                if (value == HeaderTerminator[terminatorBytesMatched])
                {
                    terminatorBytesMatched++;
                    if (terminatorBytesMatched == HeaderTerminator.Length)
                        return headers.ToArray();
                }
                else
                {
                    terminatorBytesMatched = value == HeaderTerminator[0] ? 1 : 0;
                }
            }
        }

        throw new InvalidDataException($"HTTP headers exceeded the {maxHeaderSize}-byte limit.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (_bufferCount > 0)
            return CopyBufferedBytes(buffer.AsSpan(offset, count));

        return _inner.Read(buffer, offset, count);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_bufferCount > 0)
            return CopyBufferedBytes(buffer.Span);

        return await _inner.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private int CopyBufferedBytes(Span<byte> destination)
    {
        var bytesToCopy = Math.Min(destination.Length, _bufferCount);
        _buffer.AsSpan(_bufferOffset, bytesToCopy).CopyTo(destination);
        _bufferOffset += bytesToCopy;
        _bufferCount -= bytesToCopy;
        return bytesToCopy;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) =>
        _inner.Write(buffer, offset, count);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
            await _inner.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
