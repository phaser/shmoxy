using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class ProxyConfigPageTests
{
    private readonly FrontendTestFixture _fixture;

    public ProxyConfigPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProxyConfigPage_ShowsStoppedByDefault()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/proxy-config", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var statusBadge = page.Locator(".status-badge");
        await statusBadge.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var statusText = await statusBadge.InnerTextAsync();
        Assert.Equal("Stopped", statusText);

        var startButton = page.GetByText("Start Proxy");
        Assert.True(await startButton.IsVisibleAsync());
    }

    [Fact]
    public async Task ProxyConfigPage_SaveFailsWhenProxyStopped()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/proxy-config", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(5000);

        var saveButton = page.GetByText("Save Configuration");
        await saveButton.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var message = page.Locator(".message.error");
        await message.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var messageText = await message.InnerTextAsync();
        Assert.Contains("Start the proxy", messageText);
    }

    [Fact]
    public async Task ProxyConfigPage_HasCorrectFormFields()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/proxy-config", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        // Port field
        var portField = page.Locator("fluent-number-field");
        Assert.True(await portField.First.IsVisibleAsync());

        // Log Level select
        var logLevelSelect = page.Locator("fluent-select");
        Assert.True(await logLevelSelect.IsVisibleAsync());

        // Save button
        var saveButton = page.GetByText("Save Configuration");
        Assert.True(await saveButton.IsVisibleAsync());
    }

    [Fact]
    public async Task ProxyConfigPage_StartProxy_StatusChangesToRunning()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/proxy-config", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(5000);

        // Verify initial state is Stopped
        var statusBadge = page.Locator(".status-badge");
        await statusBadge.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.Equal("Stopped", await statusBadge.InnerTextAsync());

        // Click Start Proxy
        var startButton = page.GetByText("Start Proxy");
        await startButton.ClickAsync();

        // Wait for the API call to complete — starting the proxy with dotnet can take time
        await page.WaitForTimeoutAsync(20000);

        // Capture what the page shows for diagnostics
        var allMessages = page.Locator(".proxy-controls .message");
        var msgVisible = await allMessages.IsVisibleAsync();
        var msgText = msgVisible ? await allMessages.InnerTextAsync() : "(no message visible)";
        var currentStatus = await statusBadge.InnerTextAsync();

        Assert.True(currentStatus == "Running",
            $"Expected status 'Running' but got '{currentStatus}'. UI message: {msgText}");

        // Success message should be visible
        var successMessage = page.Locator(".proxy-controls .message.success");
        await successMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.Contains("started successfully", await successMessage.InnerTextAsync());

        // Stop button should now be visible instead of Start
        var stopButton = page.GetByText("Stop Proxy");
        Assert.True(await stopButton.IsVisibleAsync());

        // Clean up: stop the proxy (graceful shutdown can take up to 10s + force-kill)
        await stopButton.ClickAsync();
        await page.WaitForTimeoutAsync(15000);
        Assert.Equal("Stopped", await statusBadge.InnerTextAsync());
    }

    [Fact]
    public async Task ProxyConfigPage_StartProxy_NoErrorMessageAppears()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/proxy-config", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(5000);

        // Verify initial state is Stopped
        var statusBadge = page.Locator(".status-badge");
        await statusBadge.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.Equal("Stopped", await statusBadge.InnerTextAsync());

        // Click Start Proxy
        var startButton = page.GetByText("Start Proxy");
        await startButton.ClickAsync();

        // Wait for the API call to complete
        await page.WaitForTimeoutAsync(20000);

        // No error message should appear after starting the proxy
        var errorMessage = page.Locator(".proxy-controls .message.error");
        var errorVisible = await errorMessage.IsVisibleAsync();
        var errorText = errorVisible ? await errorMessage.InnerTextAsync() : "(no error)";
        Assert.False(errorVisible,
            $"Expected no error message after starting proxy, but got: {errorText}");

        // The success message should still be visible (not replaced by an error)
        var successMessage = page.Locator(".proxy-controls .message.success");
        Assert.True(await successMessage.IsVisibleAsync(),
            "Expected success message to remain visible after starting proxy");

        // Clean up: stop the proxy
        var stopButton = page.GetByText("Stop Proxy");
        if (await stopButton.IsVisibleAsync())
        {
            await stopButton.ClickAsync();
            await page.WaitForTimeoutAsync(15000);
        }
    }
}
