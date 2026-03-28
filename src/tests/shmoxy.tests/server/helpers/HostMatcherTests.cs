using shmoxy.server.helpers;

namespace shmoxy.tests.server.helpers;

public class HostMatcherTests
{
    [Fact]
    public void ExactMatch_ShouldMatch()
    {
        Assert.True(HostMatcher.IsMatch("example.com", "example.com"));
    }

    [Fact]
    public void ExactMatch_ShouldBeCaseInsensitive()
    {
        Assert.True(HostMatcher.IsMatch("Example.COM", "example.com"));
    }

    [Fact]
    public void ExactMatch_ShouldNotMatchDifferentHost()
    {
        Assert.False(HostMatcher.IsMatch("other.com", "example.com"));
    }

    [Fact]
    public void WildcardPrefix_ShouldMatchSubdomain()
    {
        Assert.True(HostMatcher.IsMatch("foo.example.com", "*.example.com"));
    }

    [Fact]
    public void WildcardPrefix_ShouldMatchDeepSubdomain()
    {
        Assert.True(HostMatcher.IsMatch("a.b.example.com", "*.example.com"));
    }

    [Fact]
    public void WildcardPrefix_ShouldNotMatchExactDomain()
    {
        Assert.False(HostMatcher.IsMatch("example.com", "*.example.com"));
    }

    [Fact]
    public void WildcardPrefix_ShouldBeCaseInsensitive()
    {
        Assert.True(HostMatcher.IsMatch("FOO.Example.COM", "*.example.com"));
    }

    [Fact]
    public void ListMatch_ShouldMatchAnyPattern()
    {
        var patterns = new List<string> { "exact.com", "*.example.com" };

        Assert.True(HostMatcher.IsMatch("exact.com", patterns));
        Assert.True(HostMatcher.IsMatch("foo.example.com", patterns));
        Assert.False(HostMatcher.IsMatch("other.com", patterns));
    }

    [Fact]
    public void ListMatch_EmptyList_ShouldNotMatch()
    {
        Assert.False(HostMatcher.IsMatch("example.com", new List<string>()));
    }

    [Fact]
    public void WildcardPrefix_ShouldNotMatchPartialSuffix()
    {
        Assert.False(HostMatcher.IsMatch("notexample.com", "*.example.com"));
    }
}
