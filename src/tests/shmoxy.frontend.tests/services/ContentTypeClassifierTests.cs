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
        var headers = new Dictionary<string, string>
        {
            { "Content-Type", "application/json" }
        };

        Assert.True(ContentTypeClassifier.IsApiResponse(headers));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_ReturnsFalse_ForNonApiContentType()
    {
        var headers = new Dictionary<string, string>
        {
            { "Content-Type", "text/html" }
        };

        Assert.False(ContentTypeClassifier.IsApiResponse(headers));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_ReturnsTrue_ForEmptyHeaders()
    {
        Assert.True(ContentTypeClassifier.IsApiResponse((Dictionary<string, string>?)null));
        Assert.True(ContentTypeClassifier.IsApiResponse(new Dictionary<string, string>()));
    }

    [Fact]
    public void IsApiResponse_WithHeaders_IsCaseInsensitiveOnHeaderName()
    {
        var headers = new Dictionary<string, string>
        {
            { "content-type", "application/json" }
        };

        Assert.True(ContentTypeClassifier.IsApiResponse(headers));
    }
}
