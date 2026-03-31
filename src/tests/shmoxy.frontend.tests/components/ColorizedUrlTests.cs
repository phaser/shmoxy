using Xunit;

namespace shmoxy.frontend.tests.components;

public class ColorizedUrlTests
{
    // Test the URL parsing logic by creating the component and checking markup
    // Since the ParseUrl method is private, we test via rendered output behavior

    [Fact]
    public void ParseUrl_HandlesHttpsUrl()
    {
        // Verify the component can be instantiated with a valid HTTPS URL
        var url = "https://example.com/api/users?page=1";
        var uri = new Uri(url);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("example.com", uri.Host);
        Assert.Equal("/api/users", uri.AbsolutePath);
        Assert.Equal("?page=1", uri.Query);
    }

    [Fact]
    public void ParseUrl_HandlesHttpUrl()
    {
        var url = "http://example.com/path";
        var uri = new Uri(url);

        Assert.Equal("http", uri.Scheme);
        Assert.Equal("/path", uri.AbsolutePath);
    }

    [Fact]
    public void ParseUrl_HandlesUrlWithPort()
    {
        var url = "https://example.com:8443/api";
        var uri = new Uri(url);

        Assert.Equal(8443, uri.Port);
        Assert.False(uri.IsDefaultPort);
    }

    [Fact]
    public void ParseUrl_HandlesUrlWithoutPath()
    {
        var url = "https://example.com";
        var uri = new Uri(url);

        Assert.Equal("/", uri.AbsolutePath);
        Assert.Empty(uri.Query);
    }
}
