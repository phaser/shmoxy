using Microsoft.FluentUI.AspNetCore.Components;

namespace shmoxy.frontend.services;

public class ThemeState
{
    public DesignThemeModes Mode { get; private set; } = DesignThemeModes.Dark;

    public event Action? OnChange;

    public void SetMode(DesignThemeModes mode)
    {
        Mode = mode;
        OnChange?.Invoke();
    }

    public void Toggle()
    {
        Mode = Mode == DesignThemeModes.Dark
            ? DesignThemeModes.Light
            : DesignThemeModes.Dark;
        OnChange?.Invoke();
    }
}
