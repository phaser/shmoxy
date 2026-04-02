using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class ImageContentTypeDetectorTests
{
    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/PNG", true)]
    [InlineData("image/JPEG", true)]
    [InlineData("image/png; charset=utf-8", true)]
    [InlineData("image/jpeg; boundary=something", true)]
    [InlineData("image/gif", false)]
    [InlineData("image/webp", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("application/json", false)]
    [InlineData("text/html", false)]
    [InlineData("application/pdf", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsImageContentType_DetectsSupported(string? contentType, bool expected)
    {
        Assert.Equal(expected, ImageContentTypeDetector.IsImageContentType(contentType));
    }

    [Theory]
    [InlineData("image/png", "AQID", "data:image/png;base64,AQID")]
    [InlineData("image/jpeg", "dGVzdA==", "data:image/jpeg;base64,dGVzdA==")]
    [InlineData("image/PNG", "AQID", "data:image/png;base64,AQID")]
    [InlineData("image/jpeg; charset=utf-8", "dGVzdA==", "data:image/jpeg;base64,dGVzdA==")]
    public void BuildDataUri_FormatsCorrectly(string contentType, string base64, string expected)
    {
        Assert.Equal(expected, ImageContentTypeDetector.BuildDataUri(contentType, base64));
    }
}
