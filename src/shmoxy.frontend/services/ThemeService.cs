using Microsoft.JSInterop;

namespace shmoxy.frontend.services;

public class ThemeService
{
    private readonly IJSRuntime _jsRuntime;

    public event Action? OnThemeChanged;

    public string CurrentTheme { get; private set; } = "dark";

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SetThemeAsync(string theme)
    {
        if (theme is not "light" and not "dark")
            throw new ArgumentException("Theme must be 'light' or 'dark'", nameof(theme));

        CurrentTheme = theme;
        await _jsRuntime.InvokeVoidAsync("setLocalStorage", "preferred-theme", theme);
        OnThemeChanged?.Invoke();
    }

    public async Task<string> GetThemeAsync()
    {
        var storedTheme = await _jsRuntime.InvokeAsync<string?>("getLocalStorage", "preferred-theme");
        if (!string.IsNullOrEmpty(storedTheme))
            return storedTheme;

        var prefersDark = await _jsRuntime.InvokeAsync<bool>("matchMediaQuery", "(prefers-color-scheme: dark)");
        return prefersDark ? "dark" : "light";
    }
}
