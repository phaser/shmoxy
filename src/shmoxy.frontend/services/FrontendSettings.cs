using Microsoft.JSInterop;

namespace shmoxy.frontend.services;

public class FrontendSettings
{
    private const string EnableCyberChefKey = "shmoxy-enable-cyberchef";
    private const string CyberChefUrlKey = "shmoxy-cyberchef-url";
    private const string DefaultCyberChefUrl = "/_content/shmoxy.frontend/cyberchef/CyberChef.html";
    private const string EnableImagePreviewKey = "shmoxy-enable-image-preview";

    public bool EnableCyberChef { get; private set; }
    public string CyberChefUrl { get; private set; } = DefaultCyberChefUrl;
    public bool EnableImagePreview { get; private set; } = true;

    public event Action? OnChange;

    public async Task LoadAsync(IJSRuntime js)
    {
        try
        {
            var enabledStr = await js.InvokeAsync<string?>("localStorage.getItem", EnableCyberChefKey);
            if (bool.TryParse(enabledStr, out var enabled))
            {
                EnableCyberChef = enabled;
            }

            var url = await js.InvokeAsync<string?>("localStorage.getItem", CyberChefUrlKey);
            if (!string.IsNullOrWhiteSpace(url))
            {
                CyberChefUrl = url;
            }

            var imagePreviewStr = await js.InvokeAsync<string?>("localStorage.getItem", EnableImagePreviewKey);
            if (bool.TryParse(imagePreviewStr, out var imagePreview))
            {
                EnableImagePreview = imagePreview;
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
    }

    public async Task SetEnableCyberChefAsync(IJSRuntime js, bool enabled)
    {
        EnableCyberChef = enabled;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", EnableCyberChefKey, enabled.ToString());
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
        OnChange?.Invoke();
    }

    public async Task SetEnableImagePreviewAsync(IJSRuntime js, bool enabled)
    {
        EnableImagePreview = enabled;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", EnableImagePreviewKey, enabled.ToString());
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
        OnChange?.Invoke();
    }

    public async Task SetCyberChefUrlAsync(IJSRuntime js, string url)
    {
        CyberChefUrl = url;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", CyberChefUrlKey, url);
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
        OnChange?.Invoke();
    }
}
