using Microsoft.JSInterop;
using Moq;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class KeyboardShortcutServiceTests
{
    private static KeyboardShortcutService CreateService(Mock<IJSRuntime>? jsMock = null)
    {
        jsMock ??= new Mock<IJSRuntime>();
        return new KeyboardShortcutService(jsMock.Object);
    }

    [Fact]
    public void OnShortcut_InvokesOnShortcutTriggered()
    {
        var service = CreateService();
        string? received = null;
        service.OnShortcutTriggered += action => received = action;

        service.OnShortcut("clear");

        Assert.Equal("clear", received);
    }

    [Fact]
    public void OnShortcut_NoSubscribers_DoesNotThrow()
    {
        var service = CreateService();

        var ex = Record.Exception(() => service.OnShortcut("clear"));

        Assert.Null(ex);
    }

    [Fact]
    public void RequestResend_InvokesOnResendRequested()
    {
        var service = CreateService();
        var invoked = false;
        service.OnResendRequested += () => invoked = true;

        service.RequestResend();

        Assert.True(invoked);
    }

    [Fact]
    public async Task InitializeAsync_CallsJsInit()
    {
        var jsMock = new Mock<IJSRuntime>();
        var service = CreateService(jsMock);

        await service.InitializeAsync();

        jsMock.Verify(
            js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "keyboardShortcuts.init",
                It.IsAny<object?[]>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_OnlyInitsOnce()
    {
        var jsMock = new Mock<IJSRuntime>();
        var service = CreateService(jsMock);

        await service.InitializeAsync();
        await service.InitializeAsync();

        jsMock.Verify(
            js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "keyboardShortcuts.init",
                It.IsAny<object?[]>()),
            Times.Once);
    }

    [Fact]
    public async Task FocusSearchAsync_CallsJsFocusElement()
    {
        var jsMock = new Mock<IJSRuntime>();
        var service = CreateService(jsMock);

        await service.FocusSearchAsync();

        jsMock.Verify(
            js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "keyboardShortcuts.focusElement",
                It.Is<object?[]>(args => args.Length == 1 && (string)args[0]! == ".filters fluent-text-field input")),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_CallsJsDispose()
    {
        var jsMock = new Mock<IJSRuntime>();
        var service = CreateService(jsMock);
        await service.InitializeAsync();

        await service.DisposeAsync();

        jsMock.Verify(
            js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "keyboardShortcuts.dispose",
                It.IsAny<object?[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithoutInit_SkipsJsDispose()
    {
        var jsMock = new Mock<IJSRuntime>();
        var service = CreateService(jsMock);

        await service.DisposeAsync();

        jsMock.Verify(
            js => js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "keyboardShortcuts.dispose",
                It.IsAny<object?[]>()),
            Times.Never);
    }

    [Fact]
    public void OnShortcut_MultipleSubscribers_AllReceive()
    {
        var service = CreateService();
        string? received1 = null;
        string? received2 = null;
        service.OnShortcutTriggered += action => received1 = action;
        service.OnShortcutTriggered += action => received2 = action;

        service.OnShortcut("toggle-capture");

        Assert.Equal("toggle-capture", received1);
        Assert.Equal("toggle-capture", received2);
    }
}
