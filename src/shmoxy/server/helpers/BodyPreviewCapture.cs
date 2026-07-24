namespace shmoxy.server.helpers;

/// <summary>
/// Captures at most a configured number of body bytes while tracking the full
/// transferred payload length.
/// </summary>
internal sealed class BodyPreviewCapture : IDisposable
{
    private readonly int _limit;
    private MemoryStream? _buffer;

    public BodyPreviewCapture(int limit)
    {
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Capture limit cannot be negative.");

        _limit = limit;
    }

    public int CapturedLength => checked((int)(_buffer?.Length ?? 0));
    public long TotalBytes { get; private set; }
    public bool IsTruncated => TotalBytes > CapturedLength;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        TotalBytes = checked(TotalBytes + bytes.Length);

        var remaining = _limit - CapturedLength;
        if (remaining <= 0 || bytes.IsEmpty)
            return;

        var bytesToCapture = Math.Min(remaining, bytes.Length);
        _buffer ??= new MemoryStream(Math.Min(_limit, 8192));
        _buffer.Write(bytes[..bytesToCapture]);
    }

    public byte[] ToArray() => _buffer?.ToArray() ?? Array.Empty<byte>();

    public void Dispose() => _buffer?.Dispose();
}
