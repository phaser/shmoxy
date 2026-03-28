using Microsoft.JSInterop;
using Moq;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class FrontendSettingsTests
{
    [Fact]
    public void EnableCyberChef_IsFalseByDefault()
    {
        var settings = new FrontendSettings();

        Assert.False(settings.EnableCyberChef);
    }

    [Fact]
    public void CyberChefUrl_HasDefaultValue()
    {
        var settings = new FrontendSettings();

        Assert.Equal("/_content/shmoxy.frontend/cyberchef/CyberChef.html", settings.CyberChefUrl);
    }

    [Fact]
    public async Task SetEnableCyberChefAsync_UpdatesValue()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        await settings.SetEnableCyberChefAsync(jsMock.Object, true);

        Assert.True(settings.EnableCyberChef);
    }

    [Fact]
    public async Task SetEnableCyberChefAsync_PersistsToLocalStorage()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        await settings.SetEnableCyberChefAsync(jsMock.Object, true);

        jsMock.Verify(js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "localStorage.setItem",
            It.Is<object[]>(args =>
                args.Length == 2 &&
                (string)args[0] == "shmoxy-enable-cyberchef" &&
                (string)args[1] == "True")),
            Times.Once);
    }

    [Fact]
    public async Task SetEnableCyberChefAsync_FiresOnChange()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();
        var changed = false;
        settings.OnChange += () => changed = true;

        await settings.SetEnableCyberChefAsync(jsMock.Object, true);

        Assert.True(changed);
    }

    [Fact]
    public async Task SetCyberChefUrlAsync_UpdatesValue()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        await settings.SetCyberChefUrlAsync(jsMock.Object, "http://example.com/cyberchef");

        Assert.Equal("http://example.com/cyberchef", settings.CyberChefUrl);
    }

    [Fact]
    public async Task SetCyberChefUrlAsync_FiresOnChange()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();
        var changed = false;
        settings.OnChange += () => changed = true;

        await settings.SetCyberChefUrlAsync(jsMock.Object, "http://example.com");

        Assert.True(changed);
    }

    [Fact]
    public async Task LoadAsync_RestoresEnabledFromLocalStorage()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        // First call returns EnableCyberChef value
        jsMock.Setup(js => js.InvokeAsync<string?>(
                "localStorage.getItem",
                It.Is<object[]>(args => args.Length == 1 && (string)args[0] == "shmoxy-enable-cyberchef")))
            .ReturnsAsync("True");

        // Second call returns CyberChefUrl value
        jsMock.Setup(js => js.InvokeAsync<string?>(
                "localStorage.getItem",
                It.Is<object[]>(args => args.Length == 1 && (string)args[0] == "shmoxy-cyberchef-url")))
            .ReturnsAsync((string?)null);

        await settings.LoadAsync(jsMock.Object);

        Assert.True(settings.EnableCyberChef);
    }

    [Fact]
    public async Task LoadAsync_RestoresUrlFromLocalStorage()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        jsMock.Setup(js => js.InvokeAsync<string?>(
                "localStorage.getItem",
                It.Is<object[]>(args => args.Length == 1 && (string)args[0] == "shmoxy-enable-cyberchef")))
            .ReturnsAsync((string?)null);

        jsMock.Setup(js => js.InvokeAsync<string?>(
                "localStorage.getItem",
                It.Is<object[]>(args => args.Length == 1 && (string)args[0] == "shmoxy-cyberchef-url")))
            .ReturnsAsync("http://custom.url/cyberchef");

        await settings.LoadAsync(jsMock.Object);

        Assert.Equal("http://custom.url/cyberchef", settings.CyberChefUrl);
    }

    [Fact]
    public async Task LoadAsync_KeepsDefaults_WhenLocalStorageEmpty()
    {
        var settings = new FrontendSettings();
        var jsMock = new Mock<IJSRuntime>();

        jsMock.Setup(js => js.InvokeAsync<string?>(
                "localStorage.getItem",
                It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);

        await settings.LoadAsync(jsMock.Object);

        Assert.False(settings.EnableCyberChef);
        Assert.Equal("/_content/shmoxy.frontend/cyberchef/CyberChef.html", settings.CyberChefUrl);
    }
}
