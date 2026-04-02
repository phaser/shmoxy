using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class ContentTypeClassifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsApiResponse_ReturnsTrue_ForNullOrEmpty(string? contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/grpc")]
    public void IsApiResponse_ReturnsTrue_ForApiContentTypes(string contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("application/problem+json")]
    [InlineData("application/hal+json")]
    [InlineData("application/vnd.api+json")]
    [InlineData("application/graphql-response+json")]
    public void IsApiResponse_ReturnsTrue_ForJsonSuffixed(string contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("application/atom+xml")]
    [InlineData("application/rss+xml")]
    public void IsApiResponse_ReturnsTrue_ForXmlSuffixed(string contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/xml; charset=utf-8")]
    [InlineData("text/plain; charset=us-ascii")]
    [InlineData("application/problem+json; charset=utf-8")]
    public void IsApiResponse_ReturnsTrue_WithCharsetParameter(string contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("APPLICATION/JSON")]
    [InlineData("Application/Json")]
    [InlineData("TEXT/XML")]
    public void IsApiResponse_IsCaseInsensitive(string contentType)
    {
        Assert.True(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/css")]
    [InlineData("text/javascript")]
    [InlineData("application/javascript")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/svg+xml")]
    [InlineData("font/woff2")]
    [InlineData("audio/mpeg")]
    [InlineData("video/mp4")]
    [InlineData("application/wasm")]
    [InlineData("application/octet-stream")]
    public void IsApiResponse_ReturnsFalse_ForNonApiContentTypes(string contentType)
    {
        Assert.False(ContentTypeClassifier.IsApiResponse(contentType));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_ReturnsTrue_ForApiContentType()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", "application/json")
        };

        Assert.True(ContentTypeClassifier.IsApiResponse(headers));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_ReturnsFalse_ForNonApiContentType()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", "text/html")
        };

        Assert.False(ContentTypeClassifier.IsApiResponse(headers));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_ReturnsTrue_ForEmptyHeaders()
    {
        Assert.True(ContentTypeClassifier.IsApiResponse((List<KeyValuePair<string, string>>?)null));
        Assert.True(ContentTypeClassifier.IsApiResponse(new List<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_IsCaseInsensitiveOnHeaderName()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("content-type", "application/json")
        };

        Assert.True(ContentTypeClassifier.IsApiResponse(headers));
    }

    // --- IsApiCall tests ---

    [Theory]
    [InlineData("https://cdn.example.com/bundle.js")]
    [InlineData("https://cdn.example.com/app.min.js")]
    [InlineData("https://cdn.example.com/styles.css")]
    [InlineData("https://cdn.example.com/logo.png")]
    [InlineData("https://cdn.example.com/photo.jpg")]
    [InlineData("https://cdn.example.com/icon.svg")]
    [InlineData("https://cdn.example.com/font.woff2")]
    [InlineData("https://cdn.example.com/module.wasm")]
    [InlineData("https://cdn.example.com/bundle.js.map")]
    [InlineData("https://cdn.example.com/page.html")]
    [InlineData("https://cdn.example.com/archive.zip")]
    [InlineData("https://cdn.example.com/doc.pdf")]
    public void IsApiCall_ReturnsFalse_ForStaticResourceUrls(string url)
    {
        Assert.False(ContentTypeClassifier.IsApiCall(url, null, null));
    }

    [Theory]
    [InlineData("https://cdn.example.com/p-5394e5f7.js")]
    [InlineData("https://cdn.example.com/chunk-abc123.mjs")]
    public void IsApiCall_ReturnsFalse_ForHashedJsFiles(string url)
    {
        Assert.False(ContentTypeClassifier.IsApiCall(url, null, null));
    }

    [Theory]
    [InlineData("https://cdn.example.com/bundle.js?v=123")]
    [InlineData("https://cdn.example.com/styles.css?hash=abc")]
    [InlineData("https://cdn.example.com/image.png?width=100")]
    public void IsApiCall_ReturnsFalse_ForStaticResourceUrlsWithQueryString(string url)
    {
        Assert.False(ContentTypeClassifier.IsApiCall(url, null, null));
    }

    [Theory]
    [InlineData("https://api.example.com/users")]
    [InlineData("https://api.example.com/v1/data")]
    [InlineData("https://example.com/api/config")]
    [InlineData(null)]
    [InlineData("")]
    public void IsApiCall_ReturnsTrue_ForApiUrls(string? url)
    {
        Assert.True(ContentTypeClassifier.IsApiCall(url, null, null));
    }

    [Fact]
    public void IsApiCall_ReturnsFalse_ForAzureBlobResponse()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-ms-blob-type", "BlockBlob")
        };

        Assert.False(ContentTypeClassifier.IsApiCall("https://storage.blob.core.windows.net/container/file", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsFalse_ForS3Response()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-amz-request-id", "ABC123")
        };

        Assert.False(ContentTypeClassifier.IsApiCall("https://bucket.s3.amazonaws.com/file", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsFalse_ForGcsResponse()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-goog-stored-content-length", "12345")
        };

        Assert.False(ContentTypeClassifier.IsApiCall("https://storage.googleapis.com/bucket/file", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsFalse_ForFileDownloadDisposition()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("Content-Disposition", "attachment; filename=\"report.csv\"")
        };

        Assert.False(ContentTypeClassifier.IsApiCall("https://api.example.com/export", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsTrue_ForInlineDisposition()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("Content-Disposition", "inline"),
            new("Content-Type", "application/json")
        };

        Assert.True(ContentTypeClassifier.IsApiCall("https://api.example.com/data", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsTrue_ForJsonResponseWithNoExtension()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", "application/json")
        };

        Assert.True(ContentTypeClassifier.IsApiCall("https://api.example.com/users/123", null, responseHeaders));
    }

    [Fact]
    public void IsApiCall_ReturnsFalse_ForJsFileEvenWithMissingContentType()
    {
        // This is the exact scenario from the bug report
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-ms-blob-type", "BlockBlob")
        };

        Assert.False(ContentTypeClassifier.IsApiCall(
            "https://platform-cdn.uipath.com/apollo-packages/portal-shell/3.348.5/p-5394e5f7.js",
            null,
            responseHeaders));
    }

    // --- HasStaticResourceExtension tests ---

    [Theory]
    [InlineData("https://example.com/api/users", false)]
    [InlineData("https://example.com/file.js", true)]
    [InlineData("https://example.com/file.JS", true)]
    [InlineData("https://example.com/path/to/file.css?v=1", true)]
    [InlineData("https://example.com/path/to/file.png#hash", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("https://example.com/path.with.dots/api", false)]
    public void HasStaticResourceExtension_DetectsCorrectly(string? url, bool expected)
    {
        Assert.Equal(expected, ContentTypeClassifier.HasStaticResourceExtension(url));
    }
}
