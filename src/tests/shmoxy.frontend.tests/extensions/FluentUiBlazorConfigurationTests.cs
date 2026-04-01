using shmoxy.frontend.extensions;
using Xunit;

namespace shmoxy.frontend.tests.extensions;

public class FluentUiBlazorConfigurationTests
{
    [Theory]
    [InlineData("http://[::]:5000", "http://localhost:5000/")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000/")]
    [InlineData("http://+:5000", "http://localhost:5000/")]
    [InlineData("http://*:5000", "http://localhost:5000/")]
    [InlineData("https://[::]:5001", "https://localhost:5001/")]
    [InlineData("https://0.0.0.0:5001", "https://localhost:5001/")]
    public void NormalizeBindAddress_ReplacesWildcardWithLocalhost(string input, string expected)
    {
        var result = FluentUiBlazorConfiguration.NormalizeBindAddress(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000/")]
    [InlineData("https://myhost:5001", "https://myhost:5001/")]
    public void NormalizeBindAddress_PreservesNonWildcardAddresses(string input, string expected)
    {
        var result = FluentUiBlazorConfiguration.NormalizeBindAddress(input);
        Assert.Equal(expected, result);
    }
}
