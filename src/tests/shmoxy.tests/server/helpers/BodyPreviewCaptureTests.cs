using shmoxy.server.helpers;

namespace shmoxy.tests.server.helpers;

public class BodyPreviewCaptureTests
{
    [Fact]
    public void Append_TracksTotalBytesWhileCappingPreview()
    {
        using var capture = new BodyPreviewCapture(4);

        capture.Append("abc"u8);
        capture.Append("defgh"u8);

        Assert.Equal(8, capture.TotalBytes);
        Assert.Equal(4, capture.CapturedLength);
        Assert.True(capture.IsTruncated);
        Assert.Equal("abcd"u8.ToArray(), capture.ToArray());
    }

    [Fact]
    public void ZeroLimit_TracksBytesWithoutRetainingPayload()
    {
        using var capture = new BodyPreviewCapture(0);

        capture.Append("payload"u8);

        Assert.Equal(7, capture.TotalBytes);
        Assert.Empty(capture.ToArray());
        Assert.True(capture.IsTruncated);
    }
}
